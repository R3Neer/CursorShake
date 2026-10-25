# Experimento: cambios de rol, tamaños grandes y consumo

Autorizado y ejecutado el 25-oct-2026. Solo auxiliar técnico, sin implementación de CursorShake ni elección automática de backend. Windows 11 25H2 26200.9711; 16 procesadores lógicos; runtime .NET Desktop 10.0.8, SDK 11 preview. El publicador no llama a SetCursor, no inyecta código y no ofrece una función de producto.

## Resultado y alcance

La presentación nativa con reloj propio supera los casos funcionales probados: cambios de rol sin movimiento, reentrada, selección repetida del mismo rol y tamaños desde 128 hasta 4096 px. Por sí sola no cierra el motor: rasterización posterior en VectorRasterProbe y políticas de compatibilidad/calidad en [Arquitectura](../../docs/Arquitectura.md); QA DPI/sesión del producto pendiente. Los cursores privados siguen fuera de la actualización por roles compartidos.

## Cambios de rol

Un proceso independiente, el host de NativeRoleProbe, selecciona UP y HELP: A, B, A, A, B, A. El publicador no utiliza esos comandos como fuente para su observador; obtiene un snapshot inicial de GetCursorInfo y después escucha WinEvents fuera del proceso. Los callbacks registran GetCursorInfo y se reconcilian con los dos handles compartidos conocidos. La escala se mantiene en 96 px.

- Cinco activaciones observadas: la inicial más cuatro cambios de rol. Las seis selecciones terminaron en el rol correcto.
- La segunda selección de A estando ya en A no reinicia el reloj.
- Veinticinco EVENT_OBJECT_NAMECHANGE recibidos, pero solo cuatro cambios después de la inicial: las notificaciones de publicaciones propias no producen activaciones nuevas.
- Al entrar a un rol comienza el paso cero. Al abandonar un rol se prepara su primer paso a la escala actual, para que la reentrada no muestre el último paso de su reproducción anterior.
- La fase temporal pertenece al rol activo y la escala es una entrada independiente. La prueba usa pasos de 300/500/700/900 ms, no un parser ANI de producción ni GrowthController.

La identidad de los roles se obtiene de los recursos compartidos de Windows; no se adivina mediante hover. Esta prueba controlada no garantiza identificar cualquier cursor privado, observar transiciones arbitrariamente rápidas ni cumplir cero latencia: WinEvents es asíncrono y consulta el estado actual. No se ha probado ocultación, cambios de sesión, todas las apps, múltiples DPI ni simultáneamente una curva continua de crecimiento.

## Tamaños enormes: dos vías de presentación

| Tamaño solicitado | Recurso nativo creado | Evidencia presentada |
| ---: | ---: | --- |
| 128 | 128×128 | Forma independiente del puntero en DXGI. |
| 256 | 256×256 | Forma independiente del puntero en DXGI. |
| 257 | 257×257 | Píxeles del cursor compuestos en la textura real del escritorio. |
| 384 | 384×384 | Igual. |
| 512 | 512×512 | Igual. |
| 1024 | 1024×1024 | Igual, con recorte por bordes del monitor. |
| 2048 | 2048×2048 | Igual, con recorte. |
| 4096 | 4096×4096 | Cubre el monitor según el recorte esperado. |

Se usa CreateIconIndirect con bitmaps de tamaño real; no una cabecera CUR de un byte para afirmar dimensiones mayores de 256. La posición/hotspot de Windows no se reinterpreta como esquina superior izquierda: los fixtures tienen hotspot central proporcional.

A partir de 257 px este equipo dejó de entregar el dibujo como GetFramePointerShape. La ausencia ahí NO significaba que desapareciese: una segunda observación lee la textura DXGI de escritorio mediante staging y cuenta píxeles azules en una cuadrícula. Los recuentos de 257/384/512/1024/2048/4096 fueron 64/144/256/780/1564/2040. Corresponden al rectángulo visible recortado. No se guardan capturas de pantalla ni contenido ajeno, solo recuentos. Los píxeles que quedan fuera de la pantalla no se pueden inspeccionar.

La lectura GDI CopyFromScreen del intento inicial no detectó esos píxeles. La evidencia canónica es la textura de Desktop Duplication, no esa lectura. Cada tamaño se acompaña de un desplazamiento diagnóstico de un píxel para forzar una actualización capturable; no se usa ese movimiento para seleccionar el rol ni como parte del efecto. No se afirma inmovilidad en ese caso concreto. El umbral 256/257 es observación de este equipo, no un límite universal del API ni una garantía de plano hardware.

## Consumo del publicador

Captura DXGI en otro proceso. Publicador, propietario y observador tienen PIDs y métricas separados. Las etapas activas duran 6 s, salvo la última (5 s). Caché: cuatro HCURSOR preparados por tamaño, copiados antes de entregarlos porque SetSystemCursor los consume. Los dibujos cambian de color en cada publicación; el coste no incluye rasterizar SVG.

| Prueba | Cambios/s efectivos | CPU del equipo (16 lógicos) | CPU de un núcleo | Trabajo medio por actualización |
| --- | ---: | ---: | ---: | ---: |
| Reposo sin publicaciones | 0 | 0,02 % | 0,39 % | — |
| Animación 128 px, objetivo 8 Hz | 8,17 | 0,03 % | 0,52 % | 0,62 ms |
| 128 px, objetivo 60 Hz | 60,16 | 0,42 % | 6,77 % | 0,68 ms |
| 256 px, objetivo 60 Hz | 60,16 | 0,42 % | 6,77 % | 0,89 ms |
| 512 px, objetivo 60 Hz | 60,14 | 0,70 % | 11,19 % | 2,33 ms |
| 1024 px, objetivo 60 Hz | 60,10 | 2,54 % | 40,58 % | 7,02 ms |
| 2048 px, objetivo 60 Hz | 25,16 | 5,82 % | 93,19 % | 39,66 ms |
| 512 px creando imágenes cada vez | 60,16 | 1,56 % | 24,98 % | 4,12 ms |

Las cifras se calculan como CPU consumida / tiempo de pared; dividir por 16 produce porcentaje del equipo. La cuenta incluye una primera publicación en el instante cero y puede incluir otra en el extremo final, de ahí los 60,16/8,17. No es una garantía de fps de pantalla. No se mide GPU, potencia, CPU global de DWM ni toda la memoria del subsistema gráfico.

El planificador de la prueba intenta alcanzar plazos atrasados. A 2048 px acumula unos 3,46 s de retraso y no mantiene 60 Hz; esto demuestra saturación, no un planificador listo para producto. Un futuro motor debe saltar estados visuales vencidos y presentar el correspondiente al tiempo actual, no reproducir una cola atrasada. La selección de curva/velocidad y cualquier reducción de calidad o límite requiere decisión de diseño, no se adopta en este experimento.

Los contadores USER y GDI se mantuvieron iguales al principio/final de cada etapa: 9/19 con la caché y 5/7 en creación por actualización. No apareció crecimiento lineal de esos contadores durante las muestras por segundo. El working set observado fue de unos 42–47 MiB en tamaños menores y 106,5 MiB en la etapa de 2048. Esto no demuestra ausencia de fugas a largo plazo ni contabiliza todas las imágenes retenidas por Windows. Como presupuesto de píxeles, un solo bitmap ARGB de 2048 necesita 16 MiB y uno de 4096, 64 MiB; almacenar todos los fotogramas a todos los tamaños no es una solución gratuita.

El reposo se midió sin publicaciones y con el cursor original del rol, aunque cuatro plantillas de 128 estaban preparadas; el campo Size=128 de ese caso identifica las plantillas, no un cursor que se redibujase a 128 durante el reposo. El observador permaneció vivo en la ejecución canónica; consumió 1093,75 ms de CPU aparte a lo largo de todas las etapas. La primera ejecución perdió el observador y no se usa como resultado final.

## Vectores

[vector-sources.json](vector-sources.json) registra la auditoría de fuentes con commits fijados. [windows-svg-inventory.json](windows-svg-inventory.json) registra solo metadatos y hashes de los 17 SVG locales de Windows; no redistribuye sus imágenes. La explicación y las correspondencias están en [EquivalenciasCursores.md](../../docs/EquivalenciasCursores.md).

Hay fuentes vectoriales en los tres conjuntos, con una diferencia: Bibata Modern Ice y Nordzy blanco incluyen fuentes de sus dibujos y fotogramas; los 17 SVG locales de Windows son estáticos y no equivalen automáticamente a los ANI Aero activos, cuyos dibujos de espera son diferentes. Esta ejecución no evalúa extracción/rasterizador/fidelidad. Véanse las pruebas posteriores VectorRasterProbe y AeroVectorProbe y el diseño vigente en Arquitectura; no implican importación exhaustiva de temas.

## Evidencia, restauración y reproducción

- [functional-results.json](functional-results.json): ejecución functional-02, roles y tamaños con auditoría de textura.
- [consumption-results.json](consumption-results.json): ejecución bench-02, observador separado vivo.
- [summary.json](summary.json): resultados, controles de cierre y limitaciones.
- [host-selections.jsonl](host-selections.jsonl): decisiones del proceso propietario durante la prueba funcional.

Solo se modifican temporalmente UP y HELP. Se respaldan antes, se verifica el roundtrip CUR y al terminar se comparan píxeles, dimensiones y hotspot. Watchdog separado con plazo de 240 s; no es una decisión de producto. Las ejecuciones canónicas restauraron ambos roles; cero flags pendientes y cero procesos auxiliares residuales. ShakeToFindCursor-x64 siguió funcionando. No se cambian preferencias de registro ni se instalan herramientas.

Compilar primero ../NativeRoleProbe/NativeRoleProbe.csproj para disponer de su host y después EngineLimitsProbe.csproj. Ejecutar EngineLimitsProbe.exe con una carpeta absoluta nueva/vacía y el argumento functional o bench. Los argumentos son solo del experimento. El modo functional hace aparecer cursores muy grandes durante instantes; bench tarda alrededor de un minuto. No interactuar con el ratón durante la medición. Los backups, fixtures, intentos previos, binarios y artefactos quedan ignorados. Una flag persistente significa recuperación pendiente.
