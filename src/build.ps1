# Compila "Pasalo.exe" con ffmpeg incluido.
# Uso: powershell -ExecutionPolicy Bypass -File src\build.ps1
$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$root = Split-Path $src
$obj = Join-Path $src 'obj'
New-Item -ItemType Directory -Force $obj | Out-Null
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$refs = '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll'

# 1) Icono .ico (16/32/48/256 px) dibujado con la misma clase Logo que usa el programa.
$tool = Join-Path $obj 'IconTool.cs'
Set-Content $tool -Encoding UTF8 @'
using System.Drawing; using System.Drawing.Imaging; using System.IO;
static class IconTool {
    static void Main(string[] a) {
        int[] sizes = { 16, 32, 48, 256 };
        var pngs = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
            using (var bmp = Logo.Render(sizes[i])) using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); pngs[i] = ms.ToArray(); }
        using (var w = new BinaryWriter(File.Create(a[0]))) {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++) {
                w.Write((byte)(sizes[i] % 256)); w.Write((byte)(sizes[i] % 256)); w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32); w.Write(pngs[i].Length); w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (var p in pngs) w.Write(p);
        }
        using (var bmp = Logo.Render(256)) bmp.Save(Path.ChangeExtension(a[0], ".png"), ImageFormat.Png);
    }
}
'@
$ico = Join-Path $obj 'app.ico'
& $csc /nologo /codepage:65001 /main:IconTool "/out:$obj\IconTool.exe" @refs (Join-Path $src 'Convertidor.cs') $tool
if ($LASTEXITCODE) { throw "Error compilando el icono" }
& "$obj\IconTool.exe" $ico

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
$exe = Join-Path $root 'Pasalo.exe'
& $csc /nologo /target:winexe /optimize+ /codepage:65001 /platform:x64 /main:Program "/out:$exe" "/win32icon:$ico" "/resource:$gz,ffmpeg.exe.gz" `
    @refs (Join-Path $src 'Convertidor.cs')
if ($LASTEXITCODE) { throw "Error de compilación" }
"{0} ({1:N0} MB)" -f $exe, ((Get-Item $exe).Length / 1MB)
