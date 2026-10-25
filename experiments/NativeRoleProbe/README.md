# Prueba aislada: publicación nativa de fotogramas sobre otras aplicaciones

Autorizada y ejecutada el 25-oct-2026. No implementa CursorShake, no elige su backend ni instala nada. Windows 11 25H2, 26200.9711; .NET Desktop 10.0.8 y SDK 11 preview.

## Resultado

La publicación nativa con reloj propio funciona en el alcance probado: otro proceso conserva su HCURSOR estándar y Windows presenta los cambios publicados mediante SetSystemCursor. El publicador no llama a SetCursor. No necesita inyección, captura del ratón ni una ventana superpuesta.

| Propietario / rol | Identificador | Cambios presentados | SetCursor del propietario durante el tramo |
| --- | ---: | ---: | ---: |
| Proceso auxiliar, selección alternativa | 32516 | 4/4 | 0 |
| Proceso auxiliar, ayuda | 32651 | 4/4 | 0 |
| Proceso auxiliar, escritura manual | 32631 | 4/4 | 0 |
| Proceso auxiliar, ubicación | 32671 | 4/4 | 0 |
| Proceso auxiliar, persona | 32672 | 4/4 | 0 |
| Explorador real, flecha | 32512 | 4/4 | No instrumentado; no se le envían órdenes de selección |
| Proceso auxiliar, copia privada de selección alternativa | Copia propia | 0/4, control negativo esperado | 0 |

Cada tramo publica rojo/32 px, verde/64, azul/96 y amarillo/32, sin mover el puntero entre publicaciones. Se observa la imagen real mediante DXGI Desktop Duplication; el retorno TRUE de la función no es el único criterio. El HCURSOR seleccionado se mantiene estable. Los 20 cambios del proceso auxiliar tardaron 0,2699–2,9014 ms en ser observados desde el comienzo de la llamada. Los cuatro del Explorador, 0,3249–2,3026 ms. Son mediciones locales de captura, no una garantía de latencia del monitor.

## Continuidad temporal con cambios de escala

Se usa la misma secuencia sintética de cuatro dibujos y duraciones 300/500/700/900 ms del experimento anterior. Se publican sus fotogramas como CUR estáticos con un único origen QPC. No se implementa un parser ANI ni se pretende mantener el temporizador privado de Windows.

Ejemplo de la ejecución válida:

| Vencimiento / operación prevista | Presentación observada | Fotograma | Tamaño |
| ---: | ---: | ---: | ---: |
| 800 ms: entrar al paso 2 | 814,53 ms | 2 | 64 px |
| 1000 ms: ampliar dentro del paso 2 | 1019,45 ms | 2 | 96 px |
| 1200 ms: reducir dentro del paso 2 | 1204,53 ms | 2 | 64 px |
| 1500 ms: siguiente paso | 1520,88 ms | 3 | 64 px |

La escala no reinicia la secuencia ni cambia el vencimiento lógico de 1500 ms. Las 15 publicaciones de los 4,4 segundos observados coinciden en orden, fotograma y tamaño. Desviación máxima frente al plan: 20,884 ms, debida al conjunto de planificación/copia/publicación/captura de este auxiliar. Se usa Thread.Sleep y no se prepara un planificador de producción. El nombre interno own-timeline-two-cycles describe la intención de recorrer dos veces los cuatro pasos: la ventana registrada es 4,4 s, no dos ciclos completos de 2,4 s.

## Controles y límites

- Publicador y propietario son procesos independientes. En la ejecución final: publicador 25476, auxiliar 42916 y Explorador 25124.
- El auxiliar selecciona el rol antes de medir. Durante el tramo, un flag hace que WM_SETCURSOR devuelva TRUE sin llamar a SetCursor. Su registro confirma cero selecciones adicionales, también durante la línea temporal. No hay un timer que refresque el cursor.
- El publicador no tiene ventana visible. Solo el propietario de prueba muestra una ventana. No hay un compositor que imite el puntero.
- La prueba real abre una ventana nueva del Explorador en la carpeta de artefactos. Verifica el proceso propietario bajo el punto y la flecha compartida antes de modificar; mantiene posición/handle entre publicaciones y cierra únicamente esa ventana recién creada.
- La primera ejecución validó los roles pero no el Explorador, cuyo punto aún no estaba estabilizado. La segunda validó el Explorador, pero el auxiliar recibió mensajes de reselección. La ejecución final añade la prohibición expresa de reseleccionar y es la evidencia canónica. Los intentos previos están en artifacts ignorados.
- El control con CopyImage crea una copia privada antes de cambiar el rol. La copia conserva su imagen: el reemplazo de roles globales no cubre automáticamente cursores privados, incluso si nacieron de un cursor estándar.
- Una sola versión de Windows y un monitor; tamaños 32/64/96 px, dibujos opacos. No demuestra escalado continuo a 60 Hz, tamaños enormes, calidad vectorial, alfa/máscaras, cambios de DPI, aplicaciones elevadas, escritorio seguro ni rendimiento sostenido.
- No se ha construido el observador de roles ni probado la regla R-27 de reinicio al entrar a otro rol. La prueba no descubre el rol semántico de cualquier HCURSOR.

## Los cuatro roles que faltaban

Los cuatro aceptan el mismo mecanismo en este Windows; no necesitan un renderer especial ni cambios de registro por fotograma:

| Rol | ID que se carga y se entrega a SetSystemCursor | Evidencia documental |
| --- | ---: | --- |
| Ayuda | 32651 | IDC_HELP y binding oficial OCR_HELP de tipo SYSTEM_CURSOR_ID; falta en la tabla de parámetros de SetSystemCursor consultada. |
| Escritura manual | 32631 | Recurso de lápiz documentado sin símbolo IDC; NWPen aparece en el formato de temas. |
| Ubicación | 32671 | IDC_PIN documentado; no aparece en aquella tabla de SetSystemCursor. |
| Persona | 32672 | IDC_PERSON documentado; no aparece en aquella tabla de SetSystemCursor. |

Se distingue conocer un ID de recurso de tener una garantía explícita de su aceptación en todas las versiones por SetSystemCursor. La prueba demuestra aceptación y actualización efectiva aquí, incluida restauración. Diseño vigente en [Arquitectura](../../docs/Arquitectura.md): tabla interna explícita con estos IDs y compatibilidad del backend comprobada por versión/capacidad; un fallo no se oculta sustituyendo el rol por la flecha. No se inventan símbolos OCR_PIN/OCR_PERSON/OCR_NWPEN como si fueran constantes públicas del SDK consultado.

Leer asociaciones de un .theme ayuda a instalar el tema, pero no ofrece un mecanismo alternativo de escalado temporal. Recargar todo el esquema mediante SPI_SETCURSORS tampoco sustituye la publicación de fotogramas.

Fuentes primarias:

- [SetSystemCursor](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setsystemcursor).
- [Catálogo de cursores](https://learn.microsoft.com/en-us/windows/win32/menurc/about-cursors).
- [OCR_HELP en bindings oficiales de Microsoft](https://microsoft.github.io/windows-docs-rs/doc/windows/Win32/UI/WindowsAndMessaging/constant.OCR_HELP.html).
- [Sección de cursores de .theme](https://learn.microsoft.com/en-us/windows/win32/controls/themesfileformat-overview).

## Restauración y evidencia

Los seis roles se respaldan antes de modificar nada. Cada snapshot CUR se recarga y se comprueban píxeles, dimensiones y hotspot. Solo se usan copias propias para SetSystemCursor. Un proceso de recuperación oculto restaura los roles con flag si el publicador termina o excede 90 segundos. finally restaura y compara antes de retirar cada flag.

Resultado final: las ocho restauraciones necesarias coinciden con sus originales; no hay flags ni procesos de prueba residuales. ShakeToFindCursor-x64, PID 34288, siguió ejecutándose. Se restaura también posición del puntero y se solicita recuperar la ventana antes activa. No se escriben preferencias del registro. Los backups de imágenes Windows permanecen locales, ignorados por Git.

- [results.json](results.json): informe bruto de la ejecución final.
- [summary.json](summary.json): métricas, verificación de secuencia y límites.
- [host-selections.jsonl](host-selections.jsonl): selecciones realizadas por el proceso propietario.
- Program.cs: protocolo del auxiliar; Native.cs, Capture.cs y Fixtures.cs se reutilizan por enlaces de compilación desde AniScaleProbe, sin modificar aquella prueba.

## Reproducción

1. Compilar NativeRoleProbe.csproj con dotnet build.
2. Ejecutar NativeRoleProbe.exe --inventory para inspección sin cambios.
3. Ejecutar NativeRoleProbe.exe con una carpeta absoluta de artefactos nueva y vacía.

La CLI es exclusivamente del experimento, no de la futura aplicación. No interactuar con el ratón mientras se mide. El programa abre y cierra sus ventanas de prueba y cambia temporalmente seis roles; no usar contra otra utilidad que esté sustituyéndolos en ese momento. Los flags persistentes significan recuperación pendiente y deben revisarse antes de dar el sistema por restaurado.
