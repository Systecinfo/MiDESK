// MiDESK Setup - instalador pequeno (bootstrapper) e atualizador automático.
// Baixa a versão mais recente do MiDESK das Releases do GitHub e instala.
// Compila com o csc.exe do .NET Framework 4 (já vem no Windows 10/11), sem dependências.
//
// Modos:
//   (sem argumentos)        instalador com janela; pede UAC uma vez
//   --finish <exe> <tag>    parte elevada da instalação: instala, copia o atualizador e cria a tarefa agendada
//   --update                atualização silenciosa (roda como SYSTEM pela tarefa agendada)
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("MiDESK Setup")]
[assembly: System.Reflection.AssemblyProduct("MiDESK")]
[assembly: System.Reflection.AssemblyDescription("Instalador e atualizador do MiDESK")]
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]

namespace MiDesk
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length >= 1 && args[0] == "--update") return Updater.RunUpdate();
            if (args.Length >= 3 && args[0] == "--finish")
                return Updater.RunFinish(args[1], args[2], Array.IndexOf(args, "--no-autoupdate") < 0);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
            return 0;
        }
    }

    class Release
    {
        public string Tag, ExeName, ExeUrl, SumsUrl, SetupName, SetupUrl;
    }

    static class Core
    {
        static string ProgramFiles { get { return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles); } }
        public static string InstalledExe { get { return Path.Combine(Path.Combine(ProgramFiles, BuildConfig.AppName), BuildConfig.AppName + ".exe"); } }
        public static string UpdaterDir { get { return Path.Combine(ProgramFiles, BuildConfig.AppName + " Updater"); } }
        public static string UpdaterExe { get { return Path.Combine(UpdaterDir, BuildConfig.AppName + "-Updater.exe"); } }
        public static string TagFile { get { return Path.Combine(UpdaterDir, "installed-tag.txt"); } }
        public static string LogFile { get { return Path.Combine(UpdaterDir, "updater.log"); } }
        public static string TaskName { get { return BuildConfig.AppName + " Update"; } }
        const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\";

        public static WebClient NewClient()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = BuildConfig.AppName + "-Setup";
            return wc;
        }

        public static Release GetLatest()
        {
            string api = "https://api.github.com/repos/" + BuildConfig.Repo + "/releases/latest";
            string json;
            using (var wc = NewClient()) json = wc.DownloadString(api);
            var release = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
            var r = new Release();
            r.Tag = release.ContainsKey("tag_name") ? (string)release["tag_name"] : "?";
            foreach (Dictionary<string, object> asset in (IEnumerable)release["assets"])
            {
                string name = (string)asset["name"];
                string url = (string)asset["browser_download_url"];
                string lower = name.ToLowerInvariant();
                if (lower.EndsWith(BuildConfig.AssetSuffix)) { r.ExeName = name; r.ExeUrl = url; }
                else if (lower == "sha256sums.txt") r.SumsUrl = url;
                else if (lower == (BuildConfig.AppName + "-Setup.exe").ToLowerInvariant()) { r.SetupName = name; r.SetupUrl = url; }
            }
            return r;
        }

        public static string DownloadString(string url)
        {
            using (var wc = NewClient()) return wc.DownloadString(url);
        }

        public static string FindHash(string sums, string fileName)
        {
            foreach (string line in sums.Split('\n'))
            {
                string[] parts = line.Trim().Split(new[] { ' ', '*' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[1] == fileName) return parts[0];
            }
            return null;
        }

        public static string Sha256(string file)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
        }

        public static bool HashMatches(string file, string name, string sums)
        {
            string expected = FindHash(sums, name);
            return expected != null && expected.Equals(Sha256(file), StringComparison.OrdinalIgnoreCase);
        }

        // Data do build gravada pelo instalador do MiDESK; muda a cada build novo.
        public static string InstalledBuildDate()
        {
            using (var k = Registry.LocalMachine.OpenSubKey(UninstallKey + BuildConfig.AppName))
                return k == null ? null : k.GetValue("BuildDate") as string;
        }

        public static bool IsInstalled() { return File.Exists(InstalledExe); }
    }

    static class Updater
    {
        static void Log(string text)
        {
            try
            {
                Directory.CreateDirectory(Core.UpdaterDir);
                var fi = new FileInfo(Core.LogFile);
                if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();
                File.AppendAllText(Core.LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        static string ReadTag()
        {
            try { return File.Exists(Core.TagFile) ? File.ReadAllText(Core.TagFile).Trim() : ""; } catch { return ""; }
        }

        // Alguém está acessando este PC agora (o MiDESK abre o gerenciador de conexão com --cm).
        static bool HasIncomingSession()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE Name = '" + BuildConfig.AppName + ".exe'"))
                    foreach (ManagementObject p in s.Get())
                    {
                        string cmd = p["CommandLine"] as string;
                        if (cmd != null && cmd.Contains("--cm")) return true;
                    }
            }
            catch (Exception ex) { Log("Aviso: não foi possível listar processos: " + ex.Message); }
            return false;
        }

        // Roda o pacote do MiDESK e espera o instalador interno gravar o build novo no registro.
        static bool RunPackage(string file, string arg, string buildBefore, int timeoutSec)
        {
            using (var p = Process.Start(new ProcessStartInfo(file, arg) { UseShellExecute = false, CreateNoWindow = true }))
                p.WaitForExit(timeoutSec * 1000);
            for (int i = 0; i < timeoutSec; i++)
            {
                string now = Core.InstalledBuildDate();
                if (now != null && now != buildBefore && Core.IsInstalled()) return true;
                Thread.Sleep(1000);
            }
            return false;
        }

        static string Download(string url, string name, string dir)
        {
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, name);
            using (var wc = Core.NewClient()) wc.DownloadFile(url, file);
            return file;
        }

        // Pasta do atualizador: só SYSTEM e Administradores podem gravar (ele roda como SYSTEM).
        static void SecureUpdaterDir()
        {
            Directory.CreateDirectory(Core.UpdaterDir);
            var sec = new DirectorySecurity();
            sec.SetAccessRuleProtection(true, false);
            var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(Core.UpdaterDir, sec);
        }

        static int Schtasks(string args)
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), args) { UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psi)) { p.WaitForExit(); return p.ExitCode; }
        }

        // Tarefa agendada: verifica 5 min depois de ligar o PC e todo dia às 03:00, como SYSTEM.
        static void RegisterTask()
        {
            string xml =
"<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
"<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
"  <RegistrationInfo><Description>Instala automaticamente novas versões do " + BuildConfig.AppName + " publicadas no GitHub.</Description></RegistrationInfo>\r\n" +
"  <Triggers>\r\n" +
"    <BootTrigger><Enabled>true</Enabled><Delay>PT5M</Delay></BootTrigger>\r\n" +
"    <CalendarTrigger><StartBoundary>2026-01-01T03:00:00</StartBoundary><Enabled>true</Enabled><RandomDelay>PT30M</RandomDelay><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay></CalendarTrigger>\r\n" +
"  </Triggers>\r\n" +
"  <Principals><Principal id=\"Author\"><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
"  <Settings>\r\n" +
"    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
"    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
"    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
"    <RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable>\r\n" +
"    <ExecutionTimeLimit>PT1H</ExecutionTimeLimit>\r\n" +
"    <Enabled>true</Enabled>\r\n" +
"  </Settings>\r\n" +
"  <Actions Context=\"Author\"><Exec><Command>\"" + Core.UpdaterExe + "\"</Command><Arguments>--update</Arguments></Exec></Actions>\r\n" +
"</Task>\r\n";
            string xmlFile = Path.Combine(Core.UpdaterDir, "task.xml");
            File.WriteAllText(xmlFile, xml, Encoding.Unicode);
            int code = Schtasks("/Create /TN \"" + Core.TaskName + "\" /XML \"" + xmlFile + "\" /F");
            File.Delete(xmlFile);
            if (code != 0) throw new Exception("schtasks retornou " + code);
        }

        // Parte elevada da instalação feita pela janela do Setup.
        public static int RunFinish(string file, string tag, bool autoUpdate)
        {
            try
            {
                Log("Instalação: " + tag);
                string before = Core.InstalledBuildDate();
                // Reinstalando a mesma release a data do build não muda: espera menos.
                bool ok = RunPackage(file, "--silent-install", before, ReadTag() == tag ? 30 : 120);
                if (!ok && Core.IsInstalled()) ok = true; // reinstalação do mesmo build não muda a data
                if (!ok) { Log("A instalação não terminou."); return 2; }

                SecureUpdaterDir();
                if (!autoUpdate)
                {
                    // O usuário desmarcou a atualização automática: remove a tarefa de uma instalação anterior.
                    Schtasks("/Delete /TN \"" + Core.TaskName + "\" /F");
                    if (File.Exists(Core.UpdaterExe)) File.Delete(Core.UpdaterExe);
                    File.WriteAllText(Core.TagFile, tag);
                    Log("Instalado " + tag + " sem atualização automática.");
                    return 0;
                }
                string self = Application.ExecutablePath;
                if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(Core.UpdaterExe), StringComparison.OrdinalIgnoreCase))
                    File.Copy(self, Core.UpdaterExe, true);
                File.WriteAllText(Core.TagFile, tag);
                RegisterTask();
                Log("Instalado " + tag + "; atualização automática ativada.");
                return 0;
            }
            catch (Exception ex)
            {
                Log("Erro na instalação: " + ex);
                return 1;
            }
        }

        public static int RunUpdate()
        {
            bool created;
            using (var mutex = new Mutex(true, @"Global\" + BuildConfig.AppName + "-Updater", out created))
            {
                if (!created) return 0;
                try { return DoUpdate(); }
                catch (Exception ex) { Log("Erro: " + ex.Message); return 1; }
            }
        }

        static int DoUpdate()
        {
            if (!Core.IsInstalled())
            {
                // O MiDESK foi desinstalado: remove a tarefa e a pasta do atualizador.
                Log(BuildConfig.AppName + " não está instalado; removendo o atualizador.");
                Schtasks("/Delete /TN \"" + Core.TaskName + "\" /F");
                Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 4 >nul & rd /s /q \"" + Core.UpdaterDir + "\"") { UseShellExecute = false, CreateNoWindow = true });
                return 0;
            }

            Release r = Core.GetLatest();
            string current = ReadTag();
            if (r.Tag == current) return 0;
            if (r.ExeUrl == null || r.SumsUrl == null) { Log("A release " + r.Tag + " está incompleta; ignorando."); return 1; }
            if (HasIncomingSession()) { Log("Nova versão " + r.Tag + ", mas há uma sessão remota em andamento; fica para a próxima verificação."); return 0; }

            Log("Atualizando de " + (current == "" ? "?" : current) + " para " + r.Tag);
            string tmp = Path.Combine(Path.GetTempPath(), BuildConfig.AppName + "-update");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            string sums = Core.DownloadString(r.SumsUrl);
            string file = Download(r.ExeUrl, r.ExeName, tmp);
            if (!Core.HashMatches(file, r.ExeName, sums)) { Log("SHA256 não confere; atualização cancelada."); return 1; }

            // Confere de novo logo antes de trocar os arquivos (o download pode levar alguns minutos).
            if (HasIncomingSession()) { Log("Sessão remota iniciada durante o download; fica para a próxima verificação."); return 0; }

            string before = Core.InstalledBuildDate();
            if (!RunPackage(file, "--update", before, 300)) { Log("A atualização não terminou; tenta de novo na próxima verificação."); return 1; }
            File.WriteAllText(Core.TagFile, r.Tag);
            Log("Atualizado para " + r.Tag);

            UpdateSelf(r, sums, tmp);
            try { Directory.Delete(tmp, true); } catch { }
            return 0;
        }

        // Troca o próprio atualizador pela versão publicada junto com a release.
        static void UpdateSelf(Release r, string sums, string tmp)
        {
            try
            {
                if (r.SetupUrl == null) return;
                string file = Download(r.SetupUrl, r.SetupName, tmp);
                if (!Core.HashMatches(file, r.SetupName, sums)) { Log("SHA256 do atualizador novo não confere; mantendo o atual."); return; }
                if (Core.Sha256(file) == Core.Sha256(Core.UpdaterExe)) return;
                string old = Core.UpdaterExe + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(Core.UpdaterExe, old); // um exe em execução pode ser renomeado
                File.Copy(file, Core.UpdaterExe, true);
                Log("Atualizador atualizado.");
            }
            catch (Exception ex) { Log("Não foi possível atualizar o atualizador: " + ex.Message); }
        }
    }

    class SetupForm : Form
    {
        readonly Label status = new Label();
        readonly ProgressBar bar = new ProgressBar();
        readonly Button cancel = new Button();
        readonly Button install = new Button();
        readonly Label info = new Label();
        readonly CheckBox autoUpdate = new CheckBox();
        readonly LinkLabel policy = new LinkLabel();
        volatile bool cancelled;
        bool withAutoUpdate = true;
        WebClient client;

        public static string PolicyUrl { get { return "https://github.com/" + BuildConfig.Repo + "/blob/master/CODE_SIGNING.md"; } }

        public SetupForm()
        {
            Text = "Instalador do " + BuildConfig.AppName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 330);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var title = new Label();
            title.Text = BuildConfig.AppName;
            title.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
            title.SetBounds(16, 10, 440, 30);

            // Aviso do que será instalado e do que o programa transmite (exigido pela política de assinatura).
            info.Text =
                "Serão instalados no computador:\n" +
                "  • o " + BuildConfig.AppName + " em C:\\Program Files\\" + BuildConfig.AppName + ";\n" +
                "  • o serviço \"" + BuildConfig.AppName + "\", que inicia com o Windows e permite\n" +
                "    acesso remoto somente com o ID e a senha deste computador;\n" +
                "  • regras no Firewall e atalhos no Menu Iniciar e na Área de Trabalho.\n\n" +
                "Privacidade: o " + BuildConfig.AppName + " envia o ID, a chave pública e o endereço IP\n" +
                "apenas ao servidor de conexão do " + BuildConfig.AppName + ". As sessões são criptografadas.\n" +
                "Para remover: Configurações > Aplicativos > " + BuildConfig.AppName + ".";
            info.SetBounds(16, 46, 448, 170);

            autoUpdate.Text = "Atualizar automaticamente (verifica o GitHub uma vez por dia)";
            autoUpdate.Checked = true;
            autoUpdate.SetBounds(16, 220, 448, 22);

            policy.Text = "Política de privacidade e de assinatura de código";
            policy.SetBounds(16, 246, 448, 20);
            policy.LinkClicked += delegate { try { Process.Start(PolicyUrl); } catch { } };

            status.SetBounds(16, 50, 448, 20);
            bar.SetBounds(16, 76, 448, 18);
            bar.Style = ProgressBarStyle.Marquee;
            status.Visible = bar.Visible = false;

            install.Text = "Instalar";
            install.SetBounds(292, 290, 82, 28);
            install.Click += delegate { StartInstall(); };

            cancel.Text = "Cancelar";
            cancel.SetBounds(382, 290, 82, 28);
            cancel.Click += delegate { cancelled = true; if (client != null) client.CancelAsync(); Close(); };

            Controls.AddRange(new Control[] { title, info, autoUpdate, policy, status, bar, install, cancel });
            AcceptButton = install;
        }

        void StartInstall()
        {
            withAutoUpdate = autoUpdate.Checked;
            info.Visible = autoUpdate.Visible = policy.Visible = install.Visible = false;
            status.Text = "Procurando a versão mais recente...";
            status.Visible = bar.Visible = true;
            ClientSize = new Size(480, 140);
            cancel.SetBounds(382, 104, 82, 28);
            new Thread(Run) { IsBackground = true }.Start();
        }

        void SetStatus(string text) { BeginInvoke((MethodInvoker)delegate { status.Text = text; }); }

        void Fail(string text)
        {
            BeginInvoke((MethodInvoker)delegate
            {
                MessageBox.Show(this, text, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            });
        }

        void Run()
        {
            try
            {
                Release r = Core.GetLatest();
                if (r.ExeUrl == null) { Fail("A release " + r.Tag + " não tem o arquivo do Windows (*" + BuildConfig.AssetSuffix + ")."); return; }

                string dir = Path.Combine(Path.GetTempPath(), BuildConfig.AppName);
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, r.ExeName);

                SetStatus("Baixando " + BuildConfig.AppName + " " + r.Tag + "...");
                Download(r.ExeUrl, file);
                if (cancelled) return;

                if (r.SumsUrl != null)
                {
                    SetStatus("Verificando a integridade do arquivo...");
                    if (!Core.HashMatches(file, r.ExeName, Core.DownloadString(r.SumsUrl)))
                    {
                        File.Delete(file);
                        Fail("O arquivo baixado não confere com o SHA256 publicado. Instalação cancelada.");
                        return;
                    }
                }

                SetStatus("Instalando (aceite a permissão de administrador)...");
                var psi = new ProcessStartInfo(Application.ExecutablePath,
                    "--finish \"" + file + "\" \"" + r.Tag + "\"" + (withAutoUpdate ? "" : " --no-autoupdate")) { UseShellExecute = true, Verb = "runas" };
                Process p;
                try { p = Process.Start(psi); }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Usuário recusou o UAC: abre o MiDESK sem instalar (modo portátil).
                    Process.Start(file);
                    Finish();
                    return;
                }
                p.WaitForExit();
                if (cancelled) return;

                if (p.ExitCode == 2 || !Core.IsInstalled())
                {
                    Fail("A instalação não foi concluída. O " + BuildConfig.AppName + " será aberto sem instalar.");
                    Process.Start(file);
                    return;
                }
                if (p.ExitCode != 0)
                    BeginInvoke((MethodInvoker)delegate
                    {
                        MessageBox.Show(this, BuildConfig.AppName + " instalado, mas não foi possível ativar a atualização automática.\nDetalhes em " + Core.LogFile,
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    });
                Process.Start(Core.InstalledExe);
                Finish();
            }
            catch (Exception ex)
            {
                if (!cancelled) Fail("Não foi possível instalar o " + BuildConfig.AppName + ":\n\n" + ex.Message);
            }
        }

        void Finish() { BeginInvoke((MethodInvoker)Close); }

        void Download(string url, string file)
        {
            var done = new ManualResetEvent(false);
            Exception error = null;
            client = Core.NewClient();
            client.DownloadProgressChanged += (s, e) => BeginInvoke((MethodInvoker)delegate
            {
                if (bar.Style != ProgressBarStyle.Continuous) bar.Style = ProgressBarStyle.Continuous;
                bar.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage));
                status.Text = string.Format("Baixando... {0:0.0} de {1:0.0} MB", e.BytesReceived / 1048576.0, e.TotalBytesToReceive / 1048576.0);
            });
            client.DownloadFileCompleted += (s, e) => { error = e.Cancelled ? null : e.Error; done.Set(); };
            client.DownloadFileAsync(new Uri(url), file);
            done.WaitOne();
            client.Dispose();
            client = null;
            if (error != null) throw error;
        }
    }
}
