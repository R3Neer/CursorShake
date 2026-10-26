# CursorShake — experiencia de usuario y distribución

Comportamiento acordado; ejecutable y GUI implementados; evidencia y límites de validación en Arquitectura9. El instalador tiene QA local de instalación, actualización y retirada en ambos ámbitos; entrega pública identificada como v1.0.0. [Arquitectura](Arquitectura.md) contiene los protocolos técnicos; [ConfiguracionInicial](ConfiguracionInicial.md), los defaults; [FormatosCursores](FormatosCursores.md), los recursos admitidos.

## 1. Instalación y primera ejecución

El hito actual entrega artifacts/publish/win-x64/CursorShake.exe junto con dependencias, RecoveryGuard, temas y avisos; conservar la carpeta completa. La versión de proyecto es 1.0.0, fijada en Directory.Build.props. No se creará la primera release de GitHub hasta disponer del instalador; todavía no hay release público; el candidato local del instalador no se ha instalado ni publicado. Ejecutar el EXE no registra inicio automático por sí solo.

En la fase de instalador, el usuario descargará CursorShake-Setup.exe de GitHub Releases. El instalador permite elegir carpeta de binarios, registra desinstalación y acceso en Inicio. Ofrece «Iniciar con Windows» y «Ejecutar al terminar» marcados y desmarcables. Ejecutar con identidad/sesión del usuario correcto, incluso si el destino requiere elevación. La aplicación trabaja normalmente sin administrador; no exige SDK ni instalación manual de .NET.

Aspecto del instalador aceptado: `WizardStyle=modern dynamic windows11`, sin imágenes decorativas laterales ni superiores. Desactivar también las imágenes predeterminadas mediante `WizardImageFile=` y `WizardSmallImageFile=`; ajustar el espacio reservado de las páginas que las usaban para evitar huecos vacíos. Ámbito aceptado: usuario actual por defecto en la primera instalación, con elección de todos los usuarios mediante `PrivilegesRequired=lowest` y `PrivilegesRequiredOverridesAllowed=dialog`. Las actualizaciones respetan el ámbito y la carpeta de la instalación existente. Carpeta propuesta general: `{autopf}\CursorShake`, resuelta por Inno según el ámbito; siempre elegible. En este equipo se utilizará `D:\Program Files\CursorShake`, sin exigir unidad D a otros equipos. Instalar para todos comparte los binarios; configuración, temas personales y arranque automático siguen siendo por usuario. La casilla de inicio del instalador afecta a la cuenta que inició la instalación, conservando su identidad al elevar.

La primera ejecución asegura config.toml, prepara el tema, habilita el gesto y aparece en bandeja. Arranque silencioso, sin popup ni notificación de bienvenida. El acceso a configuración está en el menú del icono, que Windows puede colocar en el desplegable de bandeja. Una segunda apertura no duplica instancia en la misma sesión. Plataforma inicial: consola local Windows11x64; las pruebas nativas se realizan en este equipo, sin certificar otras combinaciones de monitores o drivers.

Inicio automático mediante tarea por usuario, sin retraso configurado. Objetivo: activa en menos de un minuto desde logon, no exactamente al entrar. StartupProbe comprueba lanzamiento manual con permisos normales, no ese plazo tras logon real. Al desactivar el inicio se retira solo nuestro registro; no mantener tarea y Run duplicados.

## 2. Bandeja y ventanas

WinForms NotifyIcon/ContextMenuStrip y diálogos nativos con modo claro/oscuro del dispositivo y respeto del alto contraste, sin ventana principal permanente, CLI de usuario ni editor completo de preferencias inicial. Comprobar DPI, modo oscuro, alto contraste, teclado y recreación del icono tras reinicio de Explorer; el menú usa espaciado amplio y resaltado redondeado inspirado en Windows11, con preferencia de esquinas DWM cuando se admita. No requiere migrar a WinUI. Los cambios de preferencia se refrescan en el hilo UI y también al abrir el menú y sus submenús.

| Acción | Resultado |
| --- | --- |
| Abrir configuración | Abre TOML en editor; Bloc de notas si no hay asociación adecuada. |
| Ubicación de configuración… | Selector de carpeta y traslado de TOML/themes, sin mover binarios. |
| Abrir carpeta de configuración | Explorador en la raíz activa. |
| Tema | Elegir Windows, Bibata Ice, Nordzy blanco o tema personal disponible; persiste en TOML. |
| Probar crecimiento | Demostración visual, con condiciones/cancelación de ConfiguracionInicial. |
| Pausar/reanudar crecimiento | Mantiene tema, suspende/rearma el efecto. |
| Iniciar con Windows | Cambia y muestra estado real del inicio automático. |
| Ver errores | Diagnósticos vigentes y acciones de recuperación, también tras cerrar popup. |
| Salir | Detiene trabajo y restaura recursos propios cuando pueda verificarse procedencia. |

Icono/texto distinguen activo, pausado y errores. Configuración preparada pero no efectiva se muestra como pendiente: tema y perfil cambian juntos al volver a tamaño normal, sin botón Aplicar ni reinicio.

### Errores

Forma estándar: popup, más estado de bandeja y Ver errores. Distribución inspirada en AutoHotkey, no colores ni tema: resumen; operación fallida; ubicación; explicación y detalles seleccionables/copiables. Incluir ruta, línea/columna TOML y tema/rol cuando existan. Acciones según problema: abrir configuración/carpeta/versiones conservadas, reintentar, copiar detalles, cerrar. Un conflicto de escritura ofrece revisar el TOML actual y conservar explícitamente su revisión validada; si cambia mientras se revisa se rechaza la resolución, sin sobrescribirlo. Explicar qué configuración sigue efectiva y qué datos se han conservado.

Agrupar repeticiones del mismo fallo. Debounce/reintento trata guardados transitorios antes de diagnosticar; no popup por cada evento de FileSystemWatcher. Cursor oculto/privado es un estado previsto de compatibilidad, no un error repetitivo.

## 3. Carpetas y traslado

Separar instalación, proyecto y datos. En este equipo: proyecto D:\Desarrollo\Proyectos\Herramientas\CursorShake y destino previsto de binarios D:\Program Files\CursorShake. No son rutas obligatorias para otros usuarios. Usar mecanismos soportados y verificar destino efectivo; no mover aplicaciones registradas a mano ni cambiar HOME, USERPROFILE, TEMP o AppData global.

Workspace contiene config.toml y themes/<identidad> personales. Ubicación inicial prevista: carpeta conocida LocalAppData/CursorShake; reubicable desde bandeja. Los temas incorporados pertenecen a la instalación y no se trasladan con el workspace. Localizador Root+OperationId/fase en un valor HKCU\Software\CursorShake; pequeño estado interno de journals/recuperación por usuario, sin segunda copia de preferencias. Recursos/metadatos admitidos en FormatosCursores.

Condiciones del traslado:

1. Cancelar o seleccionar raíz actual no cambia datos. Rechazar origen/destino anidados o incompatibles.
2. Si existe otro config.toml en destino, popup de error y no trasladar. Conflictos en themes tampoco se sobrescriben ni fusionan: cancelar o escoger otra carpeta vacía. No copiar archivos ajenos de la raíz.
3. Preparar/verificar copia y referencias antes de cambiar localizador o retirar originales. Cambios concurrentes se conservan; no elegir versión por fecha.
4. Antes de confirmar el cambio de raíz, un fallo conserva origen como autoridad. Después de confirmar, destino es autoridad: informar recuperación/limpieza pendiente y conservar originales necesarios, sin volver al origen silenciosamente. Protocolo en Arquitectura, apartado6.
5. Solo retirar identidades/contenidos verificados y ya no usados; nunca borrar recursivamente por un inventario antiguo ni forzar ACL. Un archivo cambiado o bloqueo deja limpieza pendiente y diagnóstico. Éxito completo implica una sola ubicación activa y retirada verificada de originales administrados; interrupción puede dejar copias conservadas.

## 4. TOML y recursos durante el uso

| Situación | Comportamiento |
| --- | --- |
| TOML realmente ausente, carpeta accesible | Crear exclusivamente desde plantilla comentada generada desde los defaults tipados; misma fuente de defaults en memoria. Comprobar al iniciar y durante uso. No sobrescribir archivo aparecido concurrentemente. |
| TOML presente inválido | Conservarlo y mantener última configuración válida; sin una previa, defaults en memoria y error. |
| Ubicación inaccesible/unidad desconectada | No confundir con ausencia; conservar ubicación elegida, informar y mantener último estado válido cuando sea posible. Sin dispersar copias. |
| Guardado válido | Preparar automáticamente y aplicar candidato completo en reposo, sin reiniciar ni Aplicar. |
| Recurso personal borrado/inválido | No regenerar ni tratar como asociación ausente; diagnóstico con ruta/tema/rol y conservar configuración aplicada cuando sea posible. |
| Editor bloquea escritura de bandeja | Conservar estado y ofrecer reintentar; no forzar cierre ni sobrescribir cambios. |
| Escritura/traslado parcial o conflicto | Popup con versiones/rutas conservadas; resolución explícita sin sustitución por fecha. |

FileSystemWatcher observa TOML y recursos personales. No respaldos acumulativos por cada edición; journals y temporales solo para recuperación, limpiados tras resolución verificable.

## 5. Detener, recuperar, actualizar y desinstalar

Pausa conserva tema y devuelve tamaño normal. Salir impide publicaciones tardías antes de restaurar. Ante cambio externo del esquema, ceder control: no sobrescribirlo para restaurar a ciegas. RecoveryGuard auxiliar conservador cubre muerte del proceso solo si reconoce roles propios y baseline verificado; si no puede, deja diagnóstico y recuperación explícita. No promete restauración infalible. Un cambio externo puede conservar un recibo bloqueante para evitar capturar un baseline incierto. En el siguiente arranque fallido, la recuperación ofrece «Volver a cargar esquema de Windows…», con confirmación: recarga el esquema guardado en Windows y puede reemplazar cambios temporales de otras utilidades. Solo procede sin motor activo y si se verifican identidad y ausencia de procesos dueños vivos; un fallo conserva los recibos. No se ejecuta automáticamente al salir ni supone adoptar el cursor actual.

Actualización inicial mediante nuevo instalador, sin actualizador propio. Conservar TOML, ubicación y temas personales; política técnica de compatibilidad/migración por definir antes de distribuir versiones con cambios de esquema. Desinstalar detiene utilidad y auxiliar, retira accesos/inicio propios, restaura lo verificablemente propio antes de quitar recursos y ofrece conservar o retirar datos del usuario. No borrar datos ajenos ni recursos de recuperación aún necesarios.

## 6. Ejecutable, GUI e instalador

Stack aceptado: C# estable/.NET10LTS, WinForms, JIT self-contained win-x64 en carpeta con DLL/runtime/recursos. Inno Setup para instalador. No exigir single-file con extracción de nativos a TEMP. SDK11preview instalado no es dependencia del producto. Svg.Skia5.2.3 es punto de partida probado; Tomlyn elegido para TOML. Fijar versiones/licencias y comprobar preservación de comentarios antes de integrar.

CursorShake.exe es la aplicación, generado durante desarrollo y publicable con dependencias. CursorShake-Setup.exe empaqueta e instala ese conjunto. La GUI mínima pertenece al ejecutable; no depende de tener instalador.

La entrega se construye en dos pasos: publicación autónoma con runtime y recursos, seguida del instalador generado desde una instantánea verificada. La QA local del paquete cubre instalación, actualización y desinstalación en ambos ámbitos; sus límites están al final de esta sección.

Versiones de dependencias y runtime fijadas para este hito mediante proyectos y locks. La identidad estable del instalador es `R3Neer.CursorShake`; la versión se toma de `Directory.Build.props`. La política de versión/migración sigue pendiente antes de versiones con cambios incompatibles. El aviso MIT del trabajo original se publica a nombre de R3Neer en LICENSE. No hay certificado de firma disponible: esta entrega es sin firma y Windows puede mostrar editor desconocido o SmartScreen. La cobertura de pruebas y QA visual se define en Arquitectura9.

### Estado de las cinco fases de distribución

1. Publicación autónoma: disponible; `build/Build.ps1 -Task Publish` genera la carpeta con runtime y recursos.
2. Receta básica: `installer/CursorShake.iss` y `build/BuildInstaller.ps1`, con Inno Setup **7.1.0**, Windows11x64, ámbito y destino elegibles, acceso al menú Inicio, icono propio y sin imágenes del asistente. El build verifica hashes, versión, arquitectura y contenido antes de copiar una instantánea a una carpeta separada. Genera EXE, SHA-256, manifiesto de archivos de Inno y metadatos de compilación bajo `artifacts/installer`. No ejecuta el instalador. `build/TestInstaller.ps1` comprueba rechazos con una copia privada de la publicación.
3. Integración implementada en la receta: «Iniciar con Windows» seleccionada inicialmente en tareas y «Ejecutar al terminar» seleccionada en la última página, ambas desmarcables. La primera aplica habilitar/deshabilitar a la misma tarea por SID que usa la bandeja; la segunda arranca sin bloquear al instalador. Inno conserva las credenciales anteriores a UAC mediante `ExecAsOriginalUser`/`runasoriginaluser`; instalar para todos no configura el arranque de otros usuarios. La preferencia de inicio se aplica antes de permitir la ejecución final. Si falla, se informa y no se lanza automáticamente. En modo silencioso se aplica la selección de tareas, pero no se abre la aplicación al terminar.

   Abrir el instalador normalmente: si nace elevado (por ejemplo «Ejecutar como administrador»), Inno no puede recuperar el usuario anterior. La entrada interna de la aplicación rechaza permisos administrativos o un contexto sin usuario interactivo antes de tocar tareas, configuración o motor. En escritorio interactivo muestra el diagnóstico; sin escritorio devuelve error sin intentar GUI. Puede abrirse luego CursorShake normalmente y configurar el inicio desde la bandeja. No adivinar usuarios mediante Explorer ni registrar la cuenta del administrador como sustituto.
4. Ciclo de vida implementado: barrera por ruta de instalación, petición de cierre a instancias identificadas, espera de salida limpia de App y RecoveryGuard y aborto ante timeout, error o recuperación pendiente. La actualización conserva ámbito/carpeta y datos; no adopta un destino no vacío ajeno ni cambia de ámbito sobre la misma carpeta. El desinstalador retira solo tareas verificadas que apunten a esta copia.

   Decisión aceptada: conservar TOML y temas personales por defecto; en desinstalación por usuario sin elevar se ofrece eliminarlos y se confirman sus rutas antes de hacerlo. Solo se retiran config.toml, themes y estado interno vacío/verificado; nunca la carpeta contenedora entera ni archivos ajenos. Revalidar autoridad, identidad, contenido y ausencia de recuperación/journals antes del primer borrado. Una desinstalación elevada/para todos conserva los datos personales de todas las cuentas. Una desinstalación por usuario iniciada como otro administrador se rechaza; abrirla desde la cuenta normal que la instaló. El modo silencioso conserva los datos.
5. QA local completada de instalación, actualización y desinstalación por usuario y para todos, con elevación UAC real y conservación de datos. Asistente oscuro observado hasta confirmación y cancelado sin instalar. Inicio de sesión real comprobado en este equipo: 6,745 s (0,424 s hasta crear el proceso y 6,321 s de inicialización), con ejecución de tarea y nueva sesión contrastadas. Después se produjo una cesión de cursores y se corrigió su clasificación/diagnóstico; no se atribuye su autoría a otra utilidad sin evidencia. Quedan otros equipos; sin certificado disponible, el EXE se entrega sin firma. Entrega pública v1.0.0 con instalador y SHA-256 adjuntos.

**Instalador vigente:** `CursorShake-1.0.0-Setup.exe`. PrepareToInstall verifica el destino, adquiere la barrera y ejecuta un auxiliar temporal que pide cerrar la aplicación instalada. El auxiliar extrae solo binarios en una ruta corta, evitando MAX_PATH en licencias anidadas; la instalación completa conserva todos los recursos y avisos. El desinstalador comprueba en usUninstall, después de confirmar y antes de retirar binarios; Abort impide continuar ante un fallo.

El protocolo privado app–instalador, identificado por `installer-protocol.txt` (`CursorShake installer protocol 2`), incluye configurar inicio, abrir, preparar el índice y mantener/desinstalar esta copia. No es una CLI de usuario. Las órdenes se procesan antes del mutex normal y sin arrancar AppHost/TOML/motor. Mantenimiento exige que el instalador mantenga la barrera; un uninstall comprueba además su propia ubicación antes de pedir cerrar nada. El rechazo de una apertura durante mantenimiento devuelve código 4 inmediatamente, sin popup que deje archivos abiertos. Las demás salidas conservan 0 éxito, 1 error, 2 argumentos inválidos, 3 contexto inseguro y 1223 cancelación explícita de la retirada personal.

Índice técnico: `maintenance-scope.txt` dentro de la instalación fija `user` o `all-users`. Inno provisiona con ACL explícita una raíz dedicada en `%LOCALAPPDATA%\CursorShake.Installations\<hash>\Users` o `%PROGRAMDATA%\CursorShake.Installations\<hash>\Users`. Cada usuario registra su SID y ruta real de estado antes de crear un motor. Los registros privados no cambian la ubicación elegible del TOML. El desinstalador verifica todos los registros, incluso los de usuarios inactivos, y bloquea ante recuperación, acceso incierto o volumen no accesible. El post-uninstall retira solo esos archivos planos bajo ancestros fijados y después directorios vacíos; entradas desconocidas o errores conservan metadatos y muestran diagnóstico. No se recorren ni eliminan perfiles. La ausencia de marcador no convierte en portable una instalación que aún tenga índice o registro de desinstalación.

La cesión durante publicación o rollback conserva ExternalChange y suspende las mutaciones, sin clasificarla como avería del renderizador. Los fallos de aplicar tema se comunican mediante su resultado; el caller presenta la causa una vez. StopFailed sigue bloqueando mantenimiento si queda un recibo; el texto distingue la instancia que está cerrándose de un dueño ajeno. Regresiones nativas verificadas: estado persistente, cero publicaciones tras cesión, un solo canal de diagnóstico y restauración condicional de las 17 posiciones usadas por QA.

Validación vigente: 47 Core y 314 Windows correctas sin exclusiones; 19 casos de empaquetado. QA nativa de arranque/preview/pausa/cierre correcta. Recorridos reales por usuario y para todos terminados con código 0; se verificaron retirada de archivos/registro/índice, conservación del TOML y restauración de la tarea previa. Para todos se arrancó mediante la tarea con token normal y se desinstaló con App/Guard activos: ambos cerraron limpiamente. El banco automático no pudo seguir un auxiliar efímero elevado; ese recorrido se terminó con pasos controlados y comprobaciones independientes, sin calificar el banco fallido como correcto. La retirada asíncrona de Inno exige esperar antes de comprobar ausencia. Arrancar una tarea manualmente no prueba el tiempo tras logon; la aceptación local posterior utilizó un cierre e inicio de sesión reales. La opción destructiva de borrar datos solo está probada en fixtures; no se borraron datos personales para QA.

Medición optativa de logon: una solicitud temporal en `%LOCALAPPDATA%\CursorShake\startup-timing\request.json` fija identidad de prueba, ejecutable, hora de armado y caducidad. Sin solicitud no se consulta LSA ni se escribe un resultado. Solo un logon interactivo nuevo posterior al armado puede generar `result-<id>-<pid>.json`; desbloquear o arrancar en la sesión actual no cuenta. El resultado separa espera hasta crear el proceso e inicialización hasta configuración válida, entrada instalada y motor disponible. Se contrasta con la tarea automática; una apertura manual tras el logon también podría producir un resultado y no demostraría autostart. La sonda no registra movimientos ni teclas. La solicitud de este ensayo está retirada y el resultado se conserva bajo artifacts/installer-qa/logon-acceptance.

El compilador se ha instalado en este equipo mediante su instalador oficial firmado en `D:\Desarrollo\Toolchains\InnoSetup7`; no es una dependencia de ejecución de CursorShake. En otros equipos, indicar su ruta con `-Compiler`. El tema dinámico del asistente se elige al abrirlo según el dispositivo.

## 7. Temas, licencias e icono

Temas iniciales Windows, Bibata Ice y Nordzy blanco, usados permanentemente. Catálogo y fuentes en [EquivalenciasCursores](EquivalenciasCursores.md); fallback/restauración en Arquitectura4.1. No se presupone compatibilidad de cualquier paquete por copiarlo. Temas personales permanecen junto al TOML y viajan con él; formatos en FormatosCursores.

| Componente | Tratamiento previsto |
| --- | --- |
| Código, instalador y documentación originales | [MIT](../LICENSE), copyright2026 R3Neer para el trabajo original; no cubre material ajeno con otra licencia. |
| Icono propio aceptado | Política MIT en la medida de derechos disponibles; conservar procedencia. |
| Bibata Ice y Nordzy blanco | Conservar GPL original, avisos, créditos y fuentes correspondientes; no relicenciar como MIT. |
| Windows | Fuentes locales verificadas y recreación propia aceptada de Wait/Working. No distribuir copias de imágenes/binarios propietarios. |
| Dependencias | Sus licencias, incluidos componentes transitivos/nativos, con avisos antes de distribución. |

Los temas se incorporan como recursos externos con licencia, procedencia, commit, modificaciones y fuentes editables. assets/themes contiene SVG y metadatos; el build genera VCUR/VANI y conserva créditos, licencias y fuentes upstream necesarias. El código original conserva MIT; cada dependencia y recurso conserva sus propios términos y avisos en la publicación.

Créditos: Bibata, Abdulkaiz Khatri (ful1e5), [licencia original](https://github.com/ful1e5/Bibata_Cursor/blob/main/LICENSE); Nordzy, Guillaume Boehm y créditos del proyecto, [COPYING](https://github.com/guillaumeboehm/Nordzy-cursors/blob/main/COPYING). Preservar declaraciones originales; [consulta Bibata sobre only/or-later](https://github.com/ful1e5/Bibata_Cursor/issues/181) no se resuelve inventando licencia.

Icono azul con líneas de agitación aceptado, PNG/ICO multirresolución y [procedencia](../assets/branding/GENERACION.md). No generar otro diseño como parte de esta limpieza.

## Fuentes técnicas

- [Publicación .NET](https://learn.microsoft.com/en-us/dotnet/core/deploying/).
- [Tarea de inicio de sesión](https://learn.microsoft.com/en-us/windows/win32/taskschd/logontrigger) y [prueba local](../experiments/StartupProbe/README.md).
- [Ejecución final en Inno Setup](https://jrsoftware.org/ishelp/topic_runsection.htm).
- [Selector de carpeta WinForms](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/folderbrowserdialog-component-windows-forms).
- [Texto MIT](https://opensource.org/license/mit); distribución de temas sujeta a sus textos originales.
