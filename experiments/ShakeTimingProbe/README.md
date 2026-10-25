# ShakeTimingProbe — medir el ritmo elegido por el usuario

Herramienta descartable autorizada el 25-oct-2026, separada del producto. C#/.NET 10 WinForms, sin paquetes externos, instalación, arranque automático ni cambios del cursor. No configura la aplicación; el intervalo adoptado se registra en ConfiguracionInicial.

Abrir bin/Debug/net10.0-windows/ShakeTimingProbe.exe y pulsar «Prueba guiada: 4 ritmos». Seguir MEDIO → LENTO → MUY RÁPIDO → MEDIO. Cada etapa tiene tres segundos de preparación y seis de captura; el paso a la siguiente es automático. Agitar aproximadamente en una línea (horizontal, vertical o diagonal), sin pulsar botones. Cada resultado incluye GuidedRunId, Stage y StageIndex. Se captura movimiento global solo durante la prueba mediante WH_MOUSE_LL y se propaga con CallNextHookEx; no intercepta ni mueve el puntero. Al terminar se retira el hook. No empieza a medir por el mero hecho de abrir la ventana.

Cada sesión se guarda en artifacts/sessions/<fecha-hora> con movement.csv, intervals.csv, result.json y status.json. Los registros se leen tras la prueba sin cerrar la ventana. Conservar los ensayos previos para comparar 2–3 repeticiones; no hay envío por red. Los logs contienen coordenadas de pantalla de ese breve ensayo.

## Qué mide y qué no

Se proyecta el movimiento sobre su eje principal (PCA de las posiciones). Una inversión se confirma al retroceder el umbral espacial configurable, inicialmente 8 píxeles físicos, y se fecha en el extremo anterior, no en el instante de confirmación. Una ida y vuelta produce dos inversiones. Los extremos menores que el filtro no se cuentan; se guardan los movimientos originales para poder reanalizar. Los resultados incluyen también umbrales 4/8/16 px para comprobar sensibilidad. No hay filtro de velocidad ni techo de intervalo; los intervalos largos permanecen en estadísticas y CSV.

PCA favorece un movimiento aproximadamente rectilíneo; trayectorias circulares o cambios de eje hacen que estas estimaciones no representen el detector definitivo. Se usa para medir cadencia humana, no para congelar la arquitectura de reconocimiento. Mediana/P90/P95/mínimo/máximo incluyen todos los intervalos positivos registrados, incluidas pausas. El último extremo no confirmado al acabar no se inventa como inversión.

La primera prueba libre usaba MSLLHOOKSTRUCT.time, que en los datos observados tiene cuantización cercana a 16 ms. Las nuevas pruebas guiadas calculan Final e intervals.csv con Stopwatch al recibir el movimiento (peak_received_ms); conservan el análisis WindowsEventClock y las dos marcas de cada muestra para contrastar cuantización frente a latencia de callbacks. El reloj de recepción no elimina por sí solo la latencia de captura. Coordenadas en píxeles físicos con PerMonitorV2. Se excluyen movimientos marcados como inyectados. El callback solo acumula muestras en memoria; los archivos finales se escriben al terminar o cerrar mientras se graba. Un cierre abrupto puede perder las muestras de esa sesión; status.json permite detectar una sesión no finalizada.

Compilación: dotnet build experiments/ShakeTimingProbe. Comprobación: ShakeTimingProbe.exe --self-test (auxiliar interno, no CLI de producto). Prueba trazas horizontales, verticales y diagonales de 200 ms entre inversiones, otras lentas de 800 ms y temblor subumbral. No sustituye la prueba real del usuario ni demuestra la captura global hasta hacerla. Compilación inicial: cero errores/advertencias; comprobaciones sintéticas pasadas.

La grabación libre 20261025-154653-575 queda intacta. El usuario recuerda un orden aproximado medio-lento-rápido-medio, sin límites temporales. Sus 74 intervalos no permiten etiquetar con certeza las cuatro distribuciones ni validar que 150 ms rechace el lento; esa cifra fue solo un candidato preliminar. La secuencia guiada se añade para obtener datos etiquetados y medir falsos positivos/negativos antes de elegir un default.

## Calibración guiada del 25-oct-2026

Grupo 215cd55d0f2e449089a656f5a8b39897; sesiones 171944-487, 171953-766, 172002-850 y 172011-920 (todas completadas, seis segundos por tramo). Etiquetas automáticas medium-1, slow, fast, medium-2. [Análisis reproducible](guided-analysis.json), con hashes de movement.csv y sensibilidad a filtros espaciales 8/16/32/48/64 px. No se editan ni sustituyen los logs originales.

El filtro auxiliar de 8 px cuenta 38 microinversiones de menos de 10 ms en el rápido; esos intervalos no representan una cadencia humana completa de agitación. No se presupone su causa física ni se certifica el reconocedor. Se reanalizan TODOS los tramos con el mismo filtro de 32 px, elegido como comprobación espacial que elimina esas microinversiones sin alterar la cuenta de los medios ni del lento. No se borran intervalos por tiempo, ni se decide con esto un nuevo default de recorrido. A 48 px las conclusiones temporales se mantienen; a 64 px aparece alguna pérdida de inversiones en Medio 1, registrada en el informe.

| Tramo | Intervalos a 32 px | Mediana | P95 | Admitidos con máximo 130 ms |
| --- | ---: | ---: | ---: | ---: |
| Medio 1 | 59 | 98,0 ms | 113,9 ms | 59/59, 100 % |
| Lento | 26 | 215,9 ms | 313,5 ms | 0/26, 0 % |
| Rápido | 82 | 67,0 ms | 103,8 ms | 80/82, 97,6 % |
| Medio 2 | 64 | 88,6 ms | 114,5 ms | 62/64, 96,9 % |

**Intervalo adoptado: 130 ms.** Retiene 121/123 intervalos medios (98,4 %) y deja un margen de unos 13 ms frente al menor intervalo lento observado (143 ms). 140 ms también rechaza todos los lentos en esta muestra, pero deja solo 3 ms de margen. 150 ms admite uno de los 26 intervalos lentos; ese único intervalo no activa por sí solo un umbral de tres inversiones, que necesita dos intervalos consecutivos válidos. No confundir porcentaje de intervalos admitidos con probabilidad de activación o falsos positivos del producto.

El umbral previo de 100 ms admite solo 91/123 intervalos medios (74,0 %). El candidato preliminar de 150 ms procedía de una toma sin etiquetas; el valor aceptado posteriormente es130ms, según [ConfiguracionInicial](../../docs/ConfiguracionInicial.md). Se trata de una calibración exploratoria de una persona en cuatro tramos breves; no un óptimo universal ni validación frente al uso cotidiano del ratón.

Gráfica de cajas y bigotes de intervalos y velocidades bajo artifacts/analysis-guided-01. Velocidad significa longitud proyectada de cada tramo dividida por duración, en píxeles físicos/s; no se confunde con cadencia ni se usa como unidad aprobada para el producto.

Reproducir con Python, numpy y matplotlib ya instalados:

```powershell
python experiments/ShakeTimingProbe/analyze_guided.py experiments/ShakeTimingProbe/artifacts/sessions experiments/ShakeTimingProbe/artifacts/otro-analisis
```
