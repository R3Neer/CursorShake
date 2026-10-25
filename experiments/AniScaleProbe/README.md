# Experimento aislado: fase ANI al cambiar de tamaño

Prueba técnica autorizada el 25-oct-2026. No es código de la aplicación CursorShake ni selecciona su backend definitivo.

## Resultado vigente

Windows 11 25H2, build 26200.9711. Captura física del puntero mediante DXGI Desktop Duplication, sin reconstruir el frame con DrawIconEx. ANI sintético de cuatro colores, duraciones 300/500/700/900 ms, tamaños 32 y 64 píxeles y hotspot proporcional.

El control sin cambio de handle mantiene el frame hasta su transición natural. Las seis sustituciones medidas de 32 a 64 cambian inmediatamente del frame 2 al 0; no conservan el frame ni su tiempo restante. La continuidad requerida falla para las vías probadas, incluida SetSystemCursor.

| Caso | Tiempo nominal restante antes de cambiar | Primer frame después | Primer cambio después |
| --- | ---: | ---: | ---: |
| Mismo handle, control sin escalar | 499,30 ms | 3, transición natural | 514,18 ms |
| LoadImage, mismo ANI solicitado a 64 | 513,17 ms | 0 | 3,95 ms |
| LoadImage, ANI ya preparado a 64 | 499,21 ms | 0 | 0,98 ms |
| Mismo caso, sustitución temprana | 636,18 ms | 0 | 3,20 ms |
| Mismo caso, sustitución tardía | 342,76 ms | 0 | 3,67 ms |
| CopyImage del cursor activo a 64 | 498,39 ms | 0 | 0,41 ms |
| SetSystemCursor con ANI a 64 | 499,39 ms | 0 | 1,40 ms |

CopyImage solo mostró frame 0 durante los 1200 ms posteriores observados. No se generaliza ese dato a toda carga/versión de Windows. Los tiempos son observación de pantalla y planificación, no lectura del temporizador privado del sistema. La discrepancia del control respecto a la duración nominal fue aproximadamente 15 ms, mucho menor que la discontinuidad de los casos escalados.

## Evidencia y alcance

- [results.json](results.json): muestras del puntero, reloj QPC, órdenes y casos de la ejecución final.
- [summary.json](summary.json): interpretación y metadatos de Windows, geometría y restauración.
- Código en esta carpeta; ejecutables, capturas de recursos Windows y fixtures generados quedan bajo artifacts/bin/obj ignorados.
- Un control inicial se rechazó porque un Label de WinForms sustituyó el cursor. Se retiró ese control. La ejecución final emplea handles nuevos por caso y un ciclo de estabilización para no confundir reutilización de handles con escalado.
- La utilidad ShakeToFindCursor existente no se cerró ni se reconfiguró. No se tocaron preferencias de registro.
- El único rol global modificado fue OCR_UP. Se preparó copia propia y backup de recuperación, se verificó su reproducción antes de modificar y se restauró al terminar comparando hash de píxeles.
- No quedó flag de restauración ni proceso de prueba; la utilidad original siguió ejecutándose.
- El experimento no demuestra ausencia de otras técnicas ni de comportamientos diferentes en otras versiones de Windows. No mide rendimiento de producción, tamaño máximo, nitidez vectorial ni todos los temas.

## Reproducción

Requiere Windows interactivo con DXGI Desktop Duplication y .NET 10 Windows Desktop; el equipo de la medición usó SDK 11 preview para compilar y runtime 10.0.8. No necesita paquetes NuGet externos.

1. Compilar AniScaleProbe.csproj con dotnet build.
2. Ejecutar AniScaleProbe.exe --check para comprobar captura, sin modificar cursores.
3. Ejecutar AniScaleProbe.exe pasando una carpeta de artefactos absoluta.

La prueba muestra una ventana temporal, mueve el puntero y sustituye temporalmente OCR_UP. Esc cancela. No ejecutar mientras se interactúa con el ratón o con otra utilidad que cambie cursores: esa interferencia invalida la medición.

Se usa un proceso de recuperación oculto y flag antes de la primera modificación global. finally restaura primero y verifica imagen; solo entonces elimina el flag. Si se interrumpe la prueba y el flag persiste, revisar el registro de recuperación antes de considerar el sistema restaurado.

Los cursores de Windows respaldados son datos locales de recuperación y no se incorporan a la distribución del proyecto.
