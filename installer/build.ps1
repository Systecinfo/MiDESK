# Compila o MiDESK-Setup.exe (instalador pequeno).
# Uso: .\build.ps1 -Repo "seu-usuario/MiDESK"
param(
    [Parameter(Mandatory = $true)][string]$Repo,
    [string]$AppName = "MiDESK",
    [string]$AssetSuffix = "-x86_64.exe",
    [string]$Icon = ""
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $here 'dist'
New-Item -ItemType Directory -Force $out | Out-Null

$cfg = @"
namespace MiDesk {
    static class BuildConfig {
        public const string Repo = "$Repo";
        public const string AppName = "$AppName";
        public const string AssetSuffix = "$($AssetSuffix.ToLowerInvariant())";
    }
}
"@
$cfgFile = Join-Path $out 'BuildConfig.g.cs'
Set-Content -Path $cfgFile -Value $cfg -Encoding UTF8

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }

$cscArgs = @('/nologo', '/target:winexe', '/optimize+', "/out:$out\$AppName-Setup.exe",
    '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll')
if ($Icon -and (Test-Path $Icon)) { $cscArgs += "/win32icon:$Icon" }
$cscArgs += @((Join-Path $here 'MiDeskSetup.cs'), $cfgFile)

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar (csc exit $LASTEXITCODE)" }
Get-Item "$out\$AppName-Setup.exe" | Select-Object Name, @{n = 'KB'; e = { [math]::Round($_.Length / 1KB, 1) } }
