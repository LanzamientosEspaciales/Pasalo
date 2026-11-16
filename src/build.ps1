# Compila "Convertidor de Video.exe" con ffmpeg incluido.
# Uso: powershell -ExecutionPolicy Bypass -File src\build.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$src = $PSScriptRoot
$root = Split-Path $src
$obj = Join-Path $src 'obj'
New-Item -ItemType Directory -Force $obj | Out-Null

# 1) Icono (PNG dentro de .ico): cuadrado azul redondeado con un "play".
$ico = Join-Path $obj 'app.ico'
$bmp = New-Object Drawing.Bitmap 256, 256
$g = [Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$path = New-Object Drawing.Drawing2D.GraphicsPath
$r = 56; $s = 240; $o = 8
$path.AddArc($o, $o, $r, $r, 180, 90); $path.AddArc($o + $s - $r, $o, $r, $r, 270, 90)
$path.AddArc($o + $s - $r, $o + $s - $r, $r, $r, 0, 90); $path.AddArc($o, $o + $s - $r, $r, $r, 90, 90)
$path.CloseFigure()
$brush = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.Point 0, 0), (New-Object Drawing.Point 256, 256), ([Drawing.Color]::FromArgb(59, 130, 246)), ([Drawing.Color]::FromArgb(29, 78, 216))
$g.FillPath($brush, $path)
$pts = [Drawing.Point[]]@((New-Object Drawing.Point 98, 70), (New-Object Drawing.Point 98, 186), (New-Object Drawing.Point 190, 128))
$g.FillPolygon([Drawing.Brushes]::White, $pts)
$g.Dispose()
$ms = New-Object IO.MemoryStream
$bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
$png = $ms.ToArray()
$w = New-Object IO.BinaryWriter ([IO.File]::Create($ico))
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]1)
$w.Write([byte]0); $w.Write([byte]0); $w.Write([byte]0); $w.Write([byte]0)
$w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$png.Length); $w.Write([uint32]22)
$w.Write($png); $w.Close()

# 2) ffmpeg comprimido (solo si cambió).
$ff = (Get-Command ffmpeg).Source
$gz = Join-Path $obj 'ffmpeg.exe.gz'
if (-not (Test-Path $gz) -or (Get-Item $gz).LastWriteTime -lt (Get-Item $ff).LastWriteTime) {
    Write-Host "Comprimiendo ffmpeg..."
    $in = [IO.File]::OpenRead($ff)
    $out = New-Object IO.Compression.GZipStream ([IO.File]::Create($gz)), ([IO.Compression.CompressionLevel]::Optimal)
    $in.CopyTo($out); $out.Close(); $in.Close()
}

# 3) Compilar.
$exe = Join-Path $root 'Convertidor de Video.exe'
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:winexe /optimize+ /codepage:65001 /platform:x64 "/out:$exe" "/win32icon:$ico" "/resource:$gz,ffmpeg.exe.gz" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll (Join-Path $src 'Convertidor.cs')
if ($LASTEXITCODE) { throw "Error de compilación" }
"{0} ({1:N0} MB)" -f $exe, ((Get-Item $exe).Length / 1MB)
