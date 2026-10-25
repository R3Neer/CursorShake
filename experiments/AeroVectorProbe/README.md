# AeroVectorProbe — recreación vectorial experimental

Prueba visual autorizada el 25-oct-2026; no implementa CursorShake ni modifica cursores, temas, registro o arranque. El usuario acepta expresamente la apariencia recreada de Wait/Working el 25-oct-2026. No se extiende esa aceptación a todos los roles de Windows ni a la elección del backend.

## Qué se ha construido

Recreación geométrica del anillo Aero y una flecha aproximada para Working. Círculos/arcos y un polígono, sin imágenes incrustadas en los SVG generados. Se miden localmente el color y orientación del sector celeste de los 18 fotogramas de aero_busy.ani y aero_working.ani. Se reconstruyen mediante geometría, colores, gradación por sectores y una sombra pequeña; no se convierten sus píxeles en miles de polígonos.

Las dos fuentes inspeccionadas tienen 18 pasos de 50 ms: 900 ms por ciclo. La comparación usa el mismo índice y reloj para original y recreación. No introduce giro continuo interpolado ni cambia la cadencia original. Los hotspots extraídos son (32,32) en Wait y (0,16) en Working, sobre lienzo 64×64. La referencia local aporta imágenes de 64,48,32; se compara la mayor. La medida se refiere a este Windows y hashes, no a todas las versiones de Aero.

[measurements.json](measurements.json) registra hashes, geometría, hotspots y ajuste de color/dirección. [fidelity-results.json](fidelity-results.json) compara los 36 fotogramas originales con los SVG nuevos, ambos a 64 px sobre blanco mediante Chromium. Error RGB absoluto medio en la imagen completa: Wait 1,21/255 y Working 2,20/255. El fondo vacío contribuye a esas medias; también se registra error sobre la unión de zonas con contenido. No es identidad de píxel ni una certificación perceptual. La flecha, bordes y sombra muestran diferencias residuales. La aceptación se hace viendo la animación, no a partir de un porcentaje de similitud inventado.

La comparación permite cambiar estado, tamaño, pausar y recorrer fotogramas. Se verificó visualmente a tamaño ampliado y en una hoja de los 36 pares. La escala del canvas y la del SVG se igualaron: no se compara un original ampliado más que la recreación. Los archivos originales y sus previsualizaciones quedan exclusivamente en artifacts ignorado; no se incorporan imágenes Windows al repositorio.

## Reproducción

Python se usa solo para medidas de imágenes y generar fixtures, por la ventaja concreta de numpy/Pillow para analizar los fotogramas. El producto sigue diseñado en C#. Requiere Python con numpy y Pillow ya disponibles, y recursos Aero locales; no instala dependencias ni software.

```powershell
python experiments/AeroVectorProbe/probe.py C:\ruta\nueva\visual experiments/AeroVectorProbe/comparison-template.txt
```

El destino debe ser nuevo. Se generan SVG, medidas y la comparación. Los SVG no incluyen animación propia: cada uno es un fotograma escalable; la duración pertenece al reloj, como ya establece la arquitectura.

Para repetir la medida, disponer los pares (PNG original, SVG generado) a 64×64 px en una cuadrícula sin márgenes de seis columnas y doce filas, primero Wait y después Working, sobre blanco. Capturar con Chromium a escala de dispositivo 1 y ventana suficiente (500×1000), perfil aislado. La captura canónica es artifacts/visual-03/fidelity.png y su documento fidelity.html. Ejecutar:

```powershell
python experiments/AeroVectorProbe/measure_fidelity.py C:\ruta\fidelity.png C:\ruta\resultado.json
```

La plantilla usa solo recursos locales incrustados al generar la comparación; no contacta servicios externos. No se distribuye una copia oficial de Aero. La apariencia recreada está aceptada para Wait/Working; su integración y distribución como recurso de producto aún deben concretarse. La prueba demuestra viabilidad visual; no mide todavía coste de publicación nativa de esta fuente específica, todos los DPI ni restauración/lifecycle. Esas capacidades del motor se estudian por separado en las otras pruebas.
