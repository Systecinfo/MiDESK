// MiDESK Setup - instalador pequeno (bootstrapper).
// Baixa a versão mais recente do MiDESK das Releases do GitHub e instala.
// Compila com o csc.exe do .NET Framework 4 (já vem no Windows 10/11), sem dependências.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("MiDESK Setup")]
[assembly: System.Reflection.AssemblyProduct("MiDESK")]
[assembly: System.Reflection.AssemblyDescription("Instalador online do MiDESK")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace MiDesk
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }

    class SetupForm : Form
    {
        readonly Label status = new Label();
        readonly ProgressBar bar = new ProgressBar();
        readonly Button cancel = new Button();
        volatile bool cancelled;
        WebClient client;

        public SetupForm()
        {
            Text = "Instalador do " + BuildConfig.AppName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(440, 130);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var title = new Label();
            title.Text = BuildConfig.AppName;
            title.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
            title.SetBounds(16, 10, 400, 30);

            status.Text = "Procurando a versão mais recente...";
            status.Font = new Font("Segoe UI", 9f);
            status.SetBounds(16, 44, 408, 20);

            bar.SetBounds(16, 68, 408, 18);
            bar.Style = ProgressBarStyle.Marquee;

            cancel.Text = "Cancelar";
            cancel.SetBounds(344, 96, 80, 26);
            cancel.Click += delegate { cancelled = true; if (client != null) client.CancelAsync(); Close(); };

            Controls.AddRange(new Control[] { title, status, bar, cancel });
            Shown += delegate { new Thread(Run) { IsBackground = true }.Start(); };
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
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2

                string api = "https://api.github.com/repos/" + BuildConfig.Repo + "/releases/latest";
                string json;
                using (var wc = NewClient()) json = wc.DownloadString(api);

                var release = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
                string tag = release.ContainsKey("tag_name") ? (string)release["tag_name"] : "?";
                string exeUrl = null, exeName = null, sumsUrl = null;
                foreach (Dictionary<string, object> asset in (IEnumerable)release["assets"])
                {
                    string name = (string)asset["name"];
                    string url = (string)asset["browser_download_url"];
                    string lower = name.ToLowerInvariant();
                    if (lower.EndsWith(BuildConfig.AssetSuffix)) { exeName = name; exeUrl = url; }
                    else if (lower == "sha256sums.txt") sumsUrl = url;
                }
                if (exeUrl == null) { Fail("A release " + tag + " não tem o arquivo do Windows (*" + BuildConfig.AssetSuffix + ")."); return; }

                string dir = Path.Combine(Path.GetTempPath(), BuildConfig.AppName);
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, exeName);

                SetStatus("Baixando " + BuildConfig.AppName + " " + tag + "...");
                Download(exeUrl, file);
                if (cancelled) return;

                if (sumsUrl != null)
                {
                    SetStatus("Verificando a integridade do arquivo...");
                    string sums;
                    using (var wc = NewClient()) sums = wc.DownloadString(sumsUrl);
                    string expected = FindHash(sums, exeName);
                    if (expected == null || !expected.Equals(Sha256(file), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(file);
                        Fail("O arquivo baixado não confere com o SHA256 publicado. Instalação cancelada.");
                        return;
                    }
                }

                SetStatus("Instalando (aceite a permissão de administrador)...");
                var psi = new ProcessStartInfo(file, "--silent-install") { UseShellExecute = true, Verb = "runas" };
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

                string installed = FindInstalledExe();
                Process.Start(installed ?? file);
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
            client = NewClient();
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

        static WebClient NewClient()
        {
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = BuildConfig.AppName + "-Setup";
            return wc;
        }

        static string FindHash(string sums, string fileName)
        {
            foreach (string line in sums.Split('\n'))
            {
                string[] parts = line.Trim().Split(new[] { ' ', '*' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[1] == fileName) return parts[0];
            }
            return null;
        }

        static string Sha256(string file)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
        }

        static string FindInstalledExe()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), BuildConfig.AppName);
            if (!Directory.Exists(dir)) return null;
            string named = Path.Combine(dir, BuildConfig.AppName + ".exe");
            if (File.Exists(named)) return named;
            foreach (string f in Directory.GetFiles(dir, "*.exe"))
            {
                string n = Path.GetFileName(f).ToLowerInvariant();
                if (!n.Contains("uninstall") && !n.Contains("unins")) return f;
            }
            return null;
        }
    }
}
