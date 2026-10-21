# CursorShake — experiencia de usuario, rutas y distribución

Estado: especificación de experiencia acordada; sin implementar. Actualización: 21-oct-2026.

Este documento sustituye las propuestas anteriores de CLI y selección de rutas mediante argumentos. La aplicación no ofrece CLI de usuario. El lenguaje elegido es C#. La versión de .NET, el framework de interfaz y la modalidad de publicación se decidirán después del modelo.

## 1. Instalación y primera ejecución

1. El usuario descarga CursorShake-Setup.exe de GitHub Releases.
2. El instalador permite elegir el destino de los binarios, registra desinstalación y crea un acceso en el menú Inicio.
3. Ofrece «Iniciar con Windows» y «Ejecutar al terminar», inicialmente marcados y desmarcables.
4. La primera ejecución se realiza en la sesión y perfil del usuario correcto. Si falta config.toml, la aplicación genera uno comentado con valores predeterminados.
5. La aplicación aplica el tema, activa el reconocimiento del gesto y aparece en la bandeja. Solo la primera vez muestra una explicación breve de cómo acceder a su configuración.
6. Abrir de nuevo la aplicación no crea una segunda instancia.

El objetivo es uso normal sin elevación. Las APIs elegidas deben probarse antes de dar ese requisito por validado. El usuario final no instala GHC, Cabal ni un SDK de .NET.

## 2. Bandeja y configuración

La configuración se edita en TOML. No se requiere una ventana propia con todos los parámetros en la primera versión. Sí hay interacción gráfica mínima mediante menús y diálogos nativos.

El menú contextual del icono ofrece:

| Acción | Comportamiento |
| --- | --- |
| Abrir configuración | Abre el TOML actual en un editor de texto; Bloc de notas como alternativa si no hay asociación adecuada. |
| Ubicación de configuración… | Muestra la carpeta actual y abre un selector nativo de carpetas para elegir otra. No exige escribir comandos ni rutas a mano. |
| Abrir carpeta de configuración | Abre en el Explorador la carpeta actualmente utilizada. |
| Tema | Permite alternar Windows, Bibata Ice y Nordzy blanco; se conserva la selección. |
| Probar crecimiento | Ejecuta una demostración del aumento y reducción. |
| Pausar/reanudar crecimiento | Suspende el gesto manteniendo el tema seleccionado. |
| Iniciar con Windows | Activa o desactiva el inicio de sesión y muestra su estado. |
| Salir | Termina y restaura los cursores sustituidos. |

La presentación exacta y agrupación del menú se diseñarán después. El icono y su texto distinguen activo, pausado y configuración con errores. Windows puede situarlo en el desplegable de iconos ocultos; la explicación inicial debe contemplarlo.

Se elimina el requisito anterior de comandos de terminal. Los cambios rápidos de tema se resuelven desde la bandeja.

## 3. Ubicación y traslado del TOML

### Criterio de rutas

La política vigente del equipo admite AppData para preferencias pequeñas y permite destinos configurables en D. En este equipo se prevén binarios bajo D:\Program Files\CursorShake y el proyecto bajo D:\Desarrollo\Proyectos\CursorShake. Estas rutas no se codifican como valores obligatorios para otros usuarios.

La ubicación inicial propuesta del TOML es la carpeta conocida LocalAppData/ CursorShake. El usuario puede cambiarla desde la bandeja. No se modifican globalmente HOME, USERPROFILE, TEMP ni AppData, ni se requieren enlaces de compatibilidad.

### Cambio de carpeta

Al elegir otra carpeta se traslada la configuración, no se crea una segunda configuración activa ni se abandona una copia antigua permanentemente.

Condiciones de seguridad del traslado:

1. Cancelar el selector no cambia nada. Elegir la carpeta actual tampoco provoca una copia ni un traslado.
2. Si el destino no contiene config.toml, se prepara allí el contenido actual y se verifica antes de activar la nueva ubicación y retirar el original.
3. El original no se retira antes de comprobar la nueva copia. El cambio debe poder recuperarse si se interrumpe a mitad; el mecanismo técnico queda pendiente.
4. Si ya existe un TOML en el destino, no se sobrescribe automáticamente. Se propone ofrecer «Usar el existente» o «Cancelar»; el uso de uno existente exige comprobarlo antes de retirar el anterior. Este caso excepcional queda pendiente de ratificación de sus textos.
5. Las referencias a temas personales deben conservar su significado al cambiar de carpeta. No se trasladan recursos de usuario ajenos automáticamente ni se rompen rutas relativas sin advertirlo. Falta elegir la representación de esas rutas.
6. Un fallo conserva la ubicación/configuración anterior utilizable e informa de las rutas afectadas. No se cambia silenciosamente a otra carpeta.
7. No se retiran directorios completos que puedan contener archivos ajenos. Solo se limpia lo creado por la aplicación y lo verificablemente innecesario; el procedimiento concreto se diseñará antes de implementarlo.

### Recordar la ubicación

La aplicación necesita recordar dónde está el TOML para el siguiente arranque. Esa referencia no puede depender exclusivamente del TOML trasladado. Se propone guardar únicamente el localizador de carpeta en una clave de registro por usuario de CursorShake; los parámetros funcionales siguen en un solo TOML. Es una decisión técnica pendiente, no una modificación aplicada al registro.

Accesos, inicio de sesión y actualizaciones usan la misma ubicación recordada. No se requieren argumentos de CLI ni un segundo archivo completo de configuración en AppData.

## 4. Archivo ausente, inválido o inaccesible

- Si se detecta que falta config.toml en la ubicación activa, se genera automáticamente con valores predeterminados y se aplica esa configuración. Esto se comprueba al arrancar y durante el uso; no se obliga a reiniciar.
- La creación no sobrescribe un archivo que aparezca mientras se estaba comprobando la ausencia.
- Un TOML presente pero inválido no es un archivo ausente: no se destruye ni se sustituye por valores predeterminados. Se conserva la última configuración válida y se explica el error. Si todavía no existe una configuración válida, se usan valores predeterminados en memoria y se informa del problema.
- Una carpeta sin permisos o una unidad desconectada no se tratan como un archivo simplemente ausente. La aplicación informa del problema y conserva su ubicación elegida; no dispersa copias en otras carpetas. Mientras sea posible conserva la última configuración válida en memoria.
- Los cambios válidos guardados por el usuario se aplican sin reiniciar. Lecturas parciales durante el guardado no deben causar avisos continuos ni restablecimientos.
- No se crean respaldos acumulativos de cada edición. Los temporales de una escritura o traslado se gestionarán con recuperación y limpieza verificables.

## 5. Actualizar, detener y desinstalar

- Actualizar conserva el TOML, su ubicación seleccionada y los temas personales. No reemplaza preferencias por una plantilla nueva.
- Cambios del esquema TOML requieren una política de compatibilidad/migración, pendiente de diseñar.
- La desinstalación elimina accesos e inicio automático, restaura los cursores modificados y ofrece conservar o retirar los datos propios del usuario.
- El tratamiento exacto de fallos del proceso y la restauración tras un cierre inesperado se diseñarán con la integración gráfica.
- El mecanismo inicial de actualización aún no se ha elegido; no se presupone un actualizador automático.

## 6. Temas y licencias

Preferencia del usuario: MIT para todo lo propio que pueda publicarse bajo esa licencia.

Propuesta de distribución:

| Componente | Tratamiento previsto |
| --- | --- |
| Código original de CursorShake, instalador y documentación propia | MIT, con los avisos correspondientes. No se ha generado aún el archivo legal final ni elegido su titular. |
| Icono aceptado de CursorShake | Recurso propio, incluido en la política MIT en la medida de los derechos disponibles. Se conserva su procedencia generativa. |
| Bibata Ice | Mantiene su GPL y avisos originales; no se relicencia como MIT. |
| Nordzy blanco | Mantiene su GPL y avisos originales; no se relicencia como MIT. |
| Recursos de Windows | Se obtienen del sistema. No se incluyen copias de sus imágenes o binarios propietarios. |
| Bibliotecas de terceros | Sus licencias respectivas, comprobadas antes de elegirlas y distribuirlas. |

La GPLv3 distingue una obra combinada de una colección de obras separadas e independientes. La intención de diseño es distribuir los temas como recursos externos, identificados y sustituibles, manteniendo su GPL; ello permite plantear código propio MIT sin declarar que todo el repositorio es MIT. No basta con separar carpetas si se incorporase código GPL al programa: debe revisarse la relación y distribución reales.

Para los temas se conservarán texto de licencia, créditos, procedencia, versión/commit, modificaciones y fuentes editables correspondientes. El plan es proporcionar SVG y los elementos necesarios para reproducir recursos derivados, no solo CUR/ANI. No se importan temas en este paso.

- Bibata: Abdulkaiz Khatri (ful1e5). https://github.com/ful1e5/Bibata_Cursor/blob/main/LICENSE
- Nordzy: Guillaume Boehm; conservar los créditos adicionales del proyecto. https://github.com/guillaumeboehm/Nordzy-cursors/blob/main/COPYING
- Bibata tiene una consulta abierta sobre only/or-later: https://github.com/ful1e5/Bibata_Cursor/issues/181 . Se preservan las declaraciones originales sin reinterpretarlas.

## 7. Icono aceptado

El usuario acepta el diseño azul de cursor con líneas de agitación generado en la propuesta 01. Se conservan PNG e ICO multirresolución bajo assets/branding, con el prompt y las comprobaciones en GENERACION.md. El nombre de archivo conserva «propuesta-01» para mantener su procedencia; su estado ahora es aceptado.

No se ha integrado en un ejecutable ni registrado en Windows durante este paso.

## 8. Ejecutable e instalador

Compilar/publicar produce CursorShake.exe y las dependencias de ejecución. Empaquetar reúne ejecutable, bibliotecas necesarias, temas y licencias. Otro ejecutable, CursorShake-Setup.exe, instala ese paquete, registra desinstalación, crea accesos y prepara el inicio automático.

La aplicación se implementará en C#. El instalador concreto todavía no se ha elegido; Inno Setup es una opción estudiada. C# permite distribuir el runtime con la aplicación, pero no se ha elegido aún la modalidad de publicación. El usuario final no debe instalar herramientas de desarrollo.

Firma de código, arquitectura de CPU, identidad estable de instalación y pruebas de actualización/desinstalación son aspectos de distribución pendientes. Crear un EXE no lo firma automáticamente.

## 9. Estado de la definición

El recorrido principal de usuario queda definido suficientemente para continuar el diseño de la aplicación C#. No se afirma que todos los detalles estén cerrados. Quedan:

- Textos de resolución de conflictos al elegir una carpeta con un TOML existente y política exacta de rutas de recursos.
- Persistencia técnica del localizador, traslado recuperable y avisos/diagnóstico ante errores.
- Definición de Windows como tema: esquema incorporado de Microsoft o esquema previo del usuario.
- Recursos gráficos, compatibilidad de configuración y distribución/actualización.

## Fuentes

- MIT: https://opensource.org/license/mit
- GPLv3, sección 5 sobre agregados y sección 6 sobre distribución: textos de licencia originales enlazados en la sección 6.
- Publicación .NET: https://learn.microsoft.com/en-us/dotnet/core/deploying/
- Inicio de sesión: https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys
- Inno Setup: https://jrsoftware.org/ishelp/topic_runsection.htm
- Selector de carpetas .NET: https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/folderbrowserdialog-component-windows-forms

Se ha actualizado únicamente documentación. No se ha implementado el traslado, escrito ninguna clave de registro, compilado la app, creado un instalador ni modificado la instalación actual de ShakeToFindCursor.

## Registro de acuerdos

[Decisiones.md](Decisiones.md) resume los acuerdos aceptados. [PendientesDiseno.md](PendientesDiseno.md) reúne las propuestas y detalles pendientes. Esta consolidación documental y el icono aceptado se incluyen en el commit autorizado por el usuario; no se ha autorizado push ni implementación.
