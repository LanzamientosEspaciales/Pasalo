# Pásalo

Programa para Windows que convierte grabaciones de DVR (`.dav`) y otros videos a **MP4, MKV, AVI o MOV**.
Un solo `.exe`, sin instalar nada: lleva [FFmpeg](https://ffmpeg.org) incluido.

## Descarga

Descarga `Pasalo.exe` desde la sección **Releases** y ábrelo con doble clic.

> Como el programa no está firmado, Windows puede mostrar "Windows protegió su PC": pulsa
> **Más información → Ejecutar de todas formas**.

## Uso

1. Arrastra videos (o carpetas) a la ventana, o pulsa **Agregar archivos**.
2. Elige el formato de salida y dónde guardar.
3. Pulsa **Convertir**.

Siempre que se puede, el video se copia sin re-codificar: es instantáneo y sin pérdida de calidad.
Si no se puede (por ejemplo, video H.265 a AVI), se re-codifica a H.264.

Formatos de entrada: `.dav .avi .mkv .mov .mp4 .wmv .flv .ts .mts .m2ts .h264 .264 .h265 .265 .mpg .mpeg .3gp .webm .asf .vob`

## Compilar

Requisitos: Windows y FFmpeg en el PATH (`winget install Gyan.FFmpeg`). El compilador de C# ya viene con Windows.

```
powershell -ExecutionPolicy Bypass -File src\build.ps1
```

Genera `Pasalo.exe` en la raíz del proyecto.

## Licencia

El `.exe` incluye FFmpeg (build de [gyan.dev](https://www.gyan.dev/ffmpeg/builds/)), distribuido bajo licencia GPL.
