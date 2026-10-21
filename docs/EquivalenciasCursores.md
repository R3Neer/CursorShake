# Equivalencias de cursores de Windows, Bibata Ice y Nordzy blanco

Comprobación: 21-oct-2026. Documento de especificación; no instala temas ni implementa código.

## Alcance e interpretación

Windows define identificadores de recursos de cursor. Podemos usarlos como referencia para las funciones visuales estándar, sin inventar nombres semánticos a partir del elemento bajo el puntero. Son identificadores de recursos; no constituyen una propiedad enum de estado que GetCursorInfo devuelva directamente.

Se compara el catálogo de Windows con los archivos de distribuciones para Windows y las asociaciones explícitas de sus instaladores. «Windows» en la tabla identifica la función y el recurso estándar; no se presupone un nombre de archivo fijo ni una imagen inmutable entre esquemas de Windows.

No se confunden tres catálogos:

1. Funciones estándar de Windows y sus recursos.
2. Archivos que un paquete instala para esas funciones.
3. Dibujos adicionales presentes en las fuentes del proyecto o archivos extra. Estos no adquieren un identificador estándar por existir en el paquete.

## Tabla de correspondencias

Los archivos de Bibata corresponden a la variante Regular; el ZIP incluye también Large y Extra-Large con los mismos nombres. Los nombres de Nordzy corresponden a la carpeta Windows_cursors/Nordzy-cursors-white.

| Función visual | Recurso de Windows | Valor | Windows (tema del sistema) | Bibata Modern Ice | Nordzy blanco |
| --- | --- | --- | --- | --- | --- |
| Selección normal | IDC_ARROW | 32512 | Recurso del sistema de este rol | Pointer.cur | normal-select.cur |
| Selección de texto | IDC_IBEAM | 32513 | Recurso del sistema de este rol | Text.cur | text-select.cur |
| Ocupado / espera | IDC_WAIT | 32514 | Recurso del sistema de este rol | Busy.ani | busy.ani |
| Selección de precisión | IDC_CROSS | 32515 | Recurso del sistema de este rol | Cross.cur | precision-select.cur |
| Selección alternativa | IDC_UPARROW | 32516 | Recurso del sistema de este rol | Alternate.cur | alt-select.cur |
| Escritura manual | Sin símbolo IDC público en el catálogo consultado; rol NWPen | 32631 | Recurso del sistema de este rol | Handwriting.cur | handwriting.cur |
| Redimensionamiento diagonal NO-SE | IDC_SIZENWSE | 32642 | Recurso del sistema de este rol | Dgn1.cur | diagonal-resize-1.cur |
| Redimensionamiento diagonal NE-SO | IDC_SIZENESW | 32643 | Recurso del sistema de este rol | Dgn2.cur | diagonal-resize-2.cur |
| Redimensionamiento horizontal | IDC_SIZEWE | 32644 | Recurso del sistema de este rol | Horz.cur | horizontal-resize.cur |
| Redimensionamiento vertical | IDC_SIZENS | 32645 | Recurso del sistema de este rol | Vert.cur | vertical-resize.cur |
| Movimiento | IDC_SIZEALL | 32646 | Recurso del sistema de este rol | Move.cur | move.cur |
| No disponible | IDC_NO | 32648 | Recurso del sistema de este rol | Unavailable.cur | unavailable.cur |
| Selección de enlace | IDC_HAND | 32649 | Recurso del sistema de este rol | Link.cur | link-select.cur |
| Trabajo en segundo plano | IDC_APPSTARTING | 32650 | Recurso del sistema de este rol | Work.ani | working-in-background.ani |
| Ayuda | IDC_HELP | 32651 | Recurso del sistema de este rol | Help.cur | help-select.cur |
| Selección de ubicación | IDC_PIN | 32671 | Recurso del sistema de este rol | Pin.cur | No asociado en el paquete Windows comprobado |
| Selección de persona | IDC_PERSON | 32672 | Recurso del sistema de este rol | Person.cur | No asociado en el paquete Windows comprobado |

La asociación de selección alternativa reproduce la del paquete: Bibata usa Alternate.cur, derivado de right_ptr en su configuración de construcción, aunque Windows describa el recurso mediante una flecha ascendente. No se sustituye el diseño por una equivalencia visual supuesta.

Microsoft documenta 16 símbolos IDC para estas funciones y, aparte, el recurso de lápiz 32631 sin un símbolo definido en WinUser.h. Por eso «17 roles configurables» no equivale a «17 constantes IDC públicas».

## Cursor predeterminado y tema Windows

Windows aparece explícitamente como tercer tema en la tabla. Para cada función se utiliza el recurso de sistema correspondiente, no una ruta de imagen que tengamos que inventar ni una copia PNG incluida en el proyecto. No se han inspeccionado aquí archivos gráficos internos de una versión concreta de Windows.

El cursor predeterminado del tema es una asociación distinta del catálogo de funciones:

| Tema | Predeterminado natural de los paquetes comparados |
| --- | --- |
| Windows | IDC_ARROW, recurso 32512: flecha de selección normal |
| Bibata Ice | Pointer.cur |
| Nordzy blanco | normal-select.cur |

Estos son los candidatos naturales para el campo predeterminado; el usuario podrá configurar ese campo según los requisitos. No hace falta un nuevo código de estado «Default»: el campo indica qué recurso usar al faltar una asociación de función.

Se conservan dos decisiones diferentes:

- Seleccionar el tema Windows: cada función usa su representación de Windows.
- Resolver una ausencia en otro tema: asociación explícita del estado → campo predeterminado del tema → recurso de Windows de ese estado si tampoco existe el campo predeterminado.

Por tanto, el último fallback no es siempre la flecha: para texto sería IDC_IBEAM, para espera IDC_WAIT, etc. Si sí hay un predeterminado configurado en el tema, se utiliza ese recurso antes de recurrir a Windows.

Queda pendiente definir, al tratar los recursos gráficos, si «Windows» significa exactamente el esquema incorporado de Microsoft o el esquema de sistema que el usuario tenía seleccionado antes de activar nuestra utilidad. La referencia de fallback no debe acabar apuntando al tema que nosotros mismos hayamos aplicado; el mecanismo para evitarlo no se decide todavía.

## Animaciones verificadas

- Bibata Ice distribuye Busy.ani y Work.ani.
- Nordzy blanco distribuye busy.ani y working-in-background.ani.
- Esto confirma que el soporte de animaciones forma parte del alcance inicial acordado.
- La inspección realizada comprueba los archivos y su asociación. Aún no se han analizado los fotogramas, tiempos y hotspots internos de los ANI.

## Dibujos y archivos adicionales

### Bibata Ice: extras distribuidos para Windows

| Archivo | Función prevista por el paquete | Identificador estándar verificado |
| --- | --- | --- |
| Pan.cur | Mano para desplazar | No identificado en el catálogo estándar consultado |
| Grabbing.cur | Mano agarrando | No identificado en el catálogo estándar consultado |
| Zoom-in.cur | Ampliar | No identificado en el catálogo estándar consultado |
| Zoom-out.cur | Reducir | No identificado en el catálogo estándar consultado |

El INF de Bibata copia estos archivos y escribe nombres Pan, Grabbing, Zoom-in y Zoom-out en el registro. Esa escritura no demuestra que Windows los reconozca como nuevos roles de cursor ni que una aplicación los solicite automáticamente. Requieren estudiar su integración si se incorporan al alcance.

### Nordzy: fuente completa frente a paquete Windows

La hoja SVG fuente contiene representaciones como openhand, closedhand, zoom-in, zoom-out, vertical-text, color-picker, copy y variantes dnd-*. Sin embargo, el paquete Windows blanco comprobado contiene los 15 archivos de la tabla principal y no instala todas esas representaciones adicionales.

No deben presentarse como estados adicionales ya soportados por el paquete Windows. Incorporarlos desde las fuentes sería una ampliación explícita que el usuario deberá decidir al diseñar el modelo.

### Recursos adicionales de Windows

El catálogo de Microsoft también enumera recursos numéricos de desplazamiento/scroll y otros antiguos o sin símbolo público (por ejemplo, 32652–32663). No se añaden automáticamente al alcance inicial. El catálogo completo de recursos de Windows es más amplio que los 17 roles de esta tabla.

## Aplicación al diseño en discusión

- La tabla permite acordar una base de funciones visuales usando equivalencias existentes, sin inferir «archivo», «carpeta» o «botón».
- La aplicación situada bajo el puntero normalmente decide qué cursor solicitar. Determinar cómo observaremos/reconoceremos esa solicitud sigue siendo una cuestión de integración pendiente.
- El conjunto de funciones es independiente del crecimiento: pulsar un botón inicia la reducción, pero no obliga a cambiar de flecha a mano.
- Para funciones sin asociación en el tema, sigue vigente la resolución acordada: recurso específico → predeterminado del tema → cursor de Windows para esa función si tampoco se define predeterminado.
- En Nordzy, ubicación y persona están ausentes de su distribución Windows. No se rellenan ahora ni se cambia la regla anterior.
- No se aprueba todavía una clase, enum, patrón Command, API de renderizado ni formato de imagen interno.

## Evidencia y fuentes

### Windows

- Catálogo de recursos, funciones e imágenes de referencia: https://learn.microsoft.com/en-us/windows/win32/menurc/about-cursors
- Selección del cursor por una aplicación: https://learn.microsoft.com/en-us/windows/win32/learnwin32/setting-the-cursor-image
- Reemplazo de cursores del sistema y constantes OCR: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setsystemcursor

Los identificadores IDC para cargar recursos y las constantes OCR para reemplazar cursores no deben intercambiarse sin comprobar la API concreta. La tabla es un catálogo funcional, no una garantía de que una sola API cubra todos los roles.

### Bibata Ice

- Release inspeccionada: v2.0.7.
- ZIP: https://github.com/ful1e5/Bibata_Cursor/releases/download/v2.0.7/Bibata-Modern-Ice-Windows.zip
- SHA-256 calculado del ZIP descargado: 0045E40324DA5B540B3BEE260F53D0792DF62BE9CDEF91655A024AE9F151BD04. Se registra como identidad del archivo inspeccionado; no se presenta como digest autenticado del proveedor.
- Dentro del ZIP se inspeccionaron los nombres de archivo de las tres variantes y el INF de Bibata-Modern-Ice-Regular-Windows/install.inf.
- El comentario interno del INF menciona v2.0.6 pese a estar distribuido en la release v2.0.7; se conserva esta discrepancia como metadato y se identifica el artefacto por release y hash.
- Configuración de fuentes contrastada, commit ddb09273cde49faeb6a32282660ff4293f52d6e0: https://github.com/ful1e5/Bibata_Cursor/blob/ddb09273cde49faeb6a32282660ff4293f52d6e0/configs/normal/win_rg.build.toml

### Nordzy blanco

- Commit inspeccionado: c7fc485e9e4fd974c4f4ff9f5f14610fa7835e7b.
- INF: https://github.com/guillaumeboehm/Nordzy-cursors/blob/c7fc485e9e4fd974c4f4ff9f5f14610fa7835e7b/Windows_cursors/Nordzy-cursors-white/install.inf
- Carpeta Windows: https://github.com/guillaumeboehm/Nordzy-cursors/tree/c7fc485e9e4fd974c4f4ff9f5f14610fa7835e7b/Windows_cursors/Nordzy-cursors-white
- SVG completo: https://github.com/guillaumeboehm/Nordzy-cursors/blob/c7fc485e9e4fd974c4f4ff9f5f14610fa7835e7b/tools/svgs/themes/Nordzy-cursors-white.svg

No se ejecutó ningún INF ni se instaló o activó un tema durante esta comprobación.
