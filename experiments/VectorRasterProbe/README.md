# VectorRasterProbe — prueba aislada, no aplicación

25-oct-2026. Windows 11 25H2 26200.9711, 16 procesadores lógicos. Svg.Skia5.2.3 es la versión probada y elegida como punto de partida; no hay implementación del producto. Paquetes fijados mediante packages.lock.json y restaurados bajo artifacts/packages.

## Protocolo y resultados

[results.json](results.json) corresponde a artifacts/raster-04. Nueve muestras: puntero Bibata, cuatro fotogramas de espera Bibata, puntero/espera/progreso Nordzy y flecha estática local Windows, esta última solo diagnóstica. La apariencia Wait/Working fue aceptada posteriormente en [AeroVectorProbe](../AeroVectorProbe/README.md); esta prueba no valida los demás roles Windows. No se ha validado exhaustivamente cada rol/fotograma de ambos paquetes.

- Rasterización a 64, 256, 512, 1024 y 2048 px; 45 conversiones a cursor nativo por CreateIconFromResourceEx, lectura de dimensiones, hotspot y alpha. Error medio de alpha cero en todas. Error medio RGB frente a composición propia sobre blanco como máximo 0,026 sobre 255.
- Comparación independiente con Chrome headless a 256 px: error medio por canal hasta 0,045/255 en las muestras de los paquetes. Conserva filtros, incluyendo sombra Bibata; quitarlos produce diferencias visibles. No es una prueba de equivalencia exhaustiva de todos los SVG.
- 36 comparaciones: cargar las instrucciones SVG una vez a coordenadas 256 y dibujarlas con escala a 256/512/1024/2048 produce error cero frente a cargar el SVG de nuevo al tamaño final. Se retienen instrucciones vectoriales, no un bitmap pequeño.
- La referencia Chromium usa perfil aislado, documentos SVG separados y ventana 1280×1100. No captura escritorio ni datos del usuario. NoSystemCursorMutation en results.json se refiere exclusivamente a esa ejecución de rasterización.

Corrección del instrumental: Graphics.GetHdc sobre bitmaps administrados alteraba el resultado de DrawIconEx al comprobar sombras. La ejecución canónica usa un DC nativo con DIB de 32 bits y fondo blanco. Los artefactos raster-01/02/03 y alpha-01/02 son diagnósticos intermedios ignorados; no son la evidencia canónica del roundtrip. alpha-03 confirmó ambas rutas nativas antes de raster-04.

## Publicación nativa y consumo

[native-bench-results.json](native-bench-results.json) corresponde a artifacts/native-bench-01. SKPicture retenido → bitmap al tamaño final → CreateIconIndirect → SetSystemCursor sobre UP. Una ventana de otro proceso conserva el handle estándar; el publicador no llama SetCursor. Baseline, watchdog temporal y finally restauran el rol con verificación de píxeles, dimensiones y hotspot. Restored=true; Failure=null.

Cada etapa dura cuatro segundos. Crecimiento sinusoidal sintético 64→máximo→64, objetivo 60 Hz, no curva del producto. Espera Bibata alterna cuatro muestras con reloj absoluto de 60 ms, no reproduce aquí sus 54 fotogramas originales. Nordzy wait usa un solo fotograma con tamaño variable, no su secuencia animada completa. Los slots vencidos se saltan, no se acumulan. Se mide también antigüedad entre selección de muestra y publicación: conservar el reloj lógico no hace desaparecer la latencia del renderizado.

| Caso | Máximo px | Publicaciones/s | CPU del equipo | Trabajo medio / p95 ms |
| --- | ---: | ---: | ---: | ---: |
| Puntero Bibata | 512 | 59,99 | 0,66 % | 2,13 / 4,88 |
| Espera Bibata | 512 | 60,00 | 2,73 % | 7,04 / 13,53 |
| Puntero Nordzy | 512 | 59,99 | 1,83 % | 1,91 / 4,17 |
| Espera Nordzy | 1024 | 60,00 | 2,59 % | 4,89 / 12,76 |
| Espera Bibata | 1024 | 45,00 | 4,15 % | 14,89 / 49,17 |
| Espera Bibata | 2048 | 29,25 | 4,81 % | 26,61 / 153,79 |
| Espera Bibata, cuatro cursores precalculados a tamaño fijo | 1024 | 60,00 | 1,44 % | 4,32 / 6,74 |

No mide consumo total de DWM/GPU/potencia, uso sostenido ni todos los roles activos simultáneamente. Las dos rutas nativas fueron probadas por separado; no se presume identidad perfecta de cada backend. Esta prueba por sí sola no fija política de producto; presupuesto y degradación aceptados en [Arquitectura](../../docs/Arquitectura.md).

## Reproducción y procedencia

Desde el repositorio, con .NET 10 y SDK compatible instalado:

```powershell
dotnet restore experiments/VectorRasterProbe --locked-mode
dotnet run --project experiments/VectorRasterProbe --no-restore -- C:\ruta\nueva\raster
# Modifica temporalmente UP: ejecutarlo solo como experimento autorizado.
dotnet run --project experiments/VectorRasterProbe --no-restore -- C:\ruta\nueva\native native-bench
```

Descargar antes las siete fuentes de [source-manifest.json](source-manifest.json) en artifacts/sources y comprobar sus SHA-256. La muestra Windows se lee de la instalación local. El ejecutable de referencia Chrome está localizado para este equipo en Program.cs; adaptar esa ruta al reproducir. NativeBench utiliza el host ya compilado de NativeRoleProbe; compilar ese proyecto previamente. No son herramientas de usuario ni APIs del producto.

SVG originales y derivados quedan en artifacts ignorado. Bibata/Nordzy conservan GPLv3 y atribución de sus proyectos; no se relicencian MIT ni se incorporan sus paquetes al producto. Svg.Skia declara MIT; antes de distribuir hay que inventariar y acompañar los avisos de todas las dependencias transitivas y nativas. No se copian recursos Windows al repositorio.
