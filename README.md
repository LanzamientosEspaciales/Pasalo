# Pásalo

Programa para Windows que **convierte y comprime videos, audio e imágenes**, de casi cualquier formato a cualquiera.
Ideal para grabaciones de DVR (`.dav`). Un solo `.exe`, sin instalar nada: lleva [FFmpeg](https://ffmpeg.org) incluido.

## Descarga

Descarga `Pasalo.exe` desde la sección **Releases** y ábrelo con doble clic.

> Como el programa no está firmado, Windows puede mostrar "Windows protegió su PC": pulsa
> **Más información → Ejecutar de todas formas**.

## Uso

1. Arrastra archivos (o carpetas) a la ventana, o pulsa **Agregar archivos**.
2. Elige el formato de salida, la compresión y dónde guardar.
3. Pulsa **Convertir**.

### Formatos de salida

| Tipo | Formatos |
|---|---|
| Video | MP4, MKV, MOV, AVI, WEBM, WMV, GIF animado |
| Audio | MP3, M4A (AAC), WAV, FLAC, OGG, OPUS |
| Imagen | JPG, PNG, WEBP, BMP |

Se puede extraer el audio de un video, o sacar una imagen (el primer fotograma).

### Compresión

- **Sin comprimir:** copia el video sin re-codificar. Es instantáneo y sin pérdida de calidad.
- **Alta calidad / Equilibrada / Archivo pequeño / Archivo muy pequeño:** re-codifica con menos calidad y menos peso.
- **Tamaño máximo:** indicas los MB y el video se ajusta para no pasarse (útil para WhatsApp o correo).
- **Resolución:** baja el video a 1080p, 720p, 480p o 360p. Nunca lo agranda.
- **H.265:** archivos más pequeños con la misma calidad, pero menos compatibles con reproductores antiguos.

Los videos comprimidos se guardan como `nombre (comprimido).mp4`. Nunca se sobrescribe un archivo.

### Formatos de entrada

Video: `.dav .mp4 .mkv .mov .avi .wmv .flv .webm .ts .mts .m2ts .mpg .3gp .vob .gif` y más.
Audio: `.mp3 .m4a .aac .wav .flac .ogg .opus .wma .amr .aiff`.
Imagen: `.jpg .png .webp .bmp .tif .heic .avif`.

## Compilar

Requisitos: Windows y FFmpeg en el PATH (`winget install Gyan.FFmpeg`). El compilador de C# ya viene con Windows.

```
powershell -ExecutionPolicy Bypass -File src\build.ps1
```

Genera `Pasalo.exe` en la raíz del proyecto.

## Licencia

El `.exe` incluye FFmpeg (build de [gyan.dev](https://www.gyan.dev/ffmpeg/builds/)), distribuido bajo licencia GPL.
