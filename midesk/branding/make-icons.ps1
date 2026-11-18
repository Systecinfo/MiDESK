# Gera todos os ícones do MiDESK a partir de midesk-icon.svg.
# Uso (na raiz do repositório):  .\midesk\branding\make-icons.ps1
# Requer o Microsoft Edge (renderiza o SVG em 1024 px; o resto é redimensionado com GDI+).
param([string]$Edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = (Resolve-Path "$PSScriptRoot\..\..").Path
$svg = Join-Path $PSScriptRoot 'midesk-icon.svg'
$tmp = Join-Path ([IO.Path]::GetTempPath()) 'midesk-icons'
New-Item -ItemType Directory -Force $tmp | Out-Null

# 1) SVG -> PNG 1024 (fundo transparente)
$html = Join-Path $tmp 'render.html'
"<html><body style='margin:0;background:transparent'><img src='file:///$($svg -replace '\\','/')' style='width:1024px;height:1024px;display:block'></body></html>" | Set-Content $html -Encoding UTF8
$master = Join-Path $tmp 'icon-1024.png'
Remove-Item $master -ErrorAction SilentlyContinue
& $Edge --headless=new --disable-gpu --hide-scrollbars --default-background-color=00000000 --window-size=1024,1024 "--screenshot=$master" "file:///$html" | Out-Null
for ($i = 0; $i -lt 30 -and -not (Test-Path $master); $i++) { Start-Sleep -Milliseconds 500 }
$src = [Drawing.Image]::FromFile($master)

function New-Size([int]$n) {
    $bmp = New-Object Drawing.Bitmap $n, $n, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'HighQuality'
    $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $g.Clear([Drawing.Color]::Transparent)
    $g.DrawImage($src, 0, 0, $n, $n); $g.Dispose()
    return $bmp
}
function Save-Png([int]$n, [string]$rel) {
    $b = New-Size $n; $p = Join-Path $root $rel
    $b.Save($p, [Drawing.Imaging.ImageFormat]::Png); $b.Dispose(); Write-Host "png  $rel ($n)"
}
function Get-PngBytes($bmp) { $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $ms.ToArray() }
function Get-DibBytes($bmp) {
    # Entrada BMP 32 bpp (BITMAPINFOHEADER + BGRA de baixo para cima + máscara AND), compatível com qualquer leitor de .ico
    $n = $bmp.Width; $ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ms
    $w.Write([int]40); $w.Write([int]$n); $w.Write([int]($n * 2)); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]($n * $n * 4)); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $n - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $n; $x++) {
        $c = $bmp.GetPixel($x, $y); $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A) } }
    $maskRow = [int]([Math]::Ceiling($n / 32.0) * 4); $w.Write((New-Object byte[] ($maskRow * $n)))
    $w.Flush(); $ms.ToArray()
}
function Save-Ico([int[]]$sizes, [string]$rel) {
    $entries = foreach ($n in $sizes) { $b = New-Size $n; $bytes = if ($n -ge 256) { Get-PngBytes $b } else { Get-DibBytes $b }; $b.Dispose(); , @($n, $bytes) }
    $ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ms
    $w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$entries.Count)
    $offset = 6 + 16 * $entries.Count
    foreach ($e in $entries) {
        $n = $e[0]; $len = $e[1].Length; $d = if ($n -ge 256) { 0 } else { $n }
        $w.Write([byte]$d); $w.Write([byte]$d); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$len); $w.Write([int]$offset); $offset += $len
    }
    foreach ($e in $entries) { $w.Write([byte[]]$e[1]) }
    $w.Flush(); [IO.File]::WriteAllBytes((Join-Path $root $rel), $ms.ToArray()); Write-Host "ico  $rel ($($sizes -join ','))"
}

$app = 16, 20, 24, 32, 40, 48, 64, 128, 256
Save-Ico $app 'res\icon.ico'
Save-Ico $app 'flutter\windows\runner\resources\app_icon.ico'
Save-Ico @(16, 20, 24, 32, 48) 'res\tray-icon.ico'
Save-Png 1024 'res\icon.png'
Save-Png 1024 'res\mac-icon.png'
Save-Png 32 'res\32x32.png'
Save-Png 64 'res\64x64.png'
Save-Png 128 'res\128x128.png'
Save-Png 256 'res\128x128@2x.png'
Save-Png 256 'flutter\assets\icon.png'
Copy-Item $svg (Join-Path $root 'flutter\assets\icon.svg') -Force; Write-Host "svg  flutter\assets\icon.svg"
$src.Dispose()
