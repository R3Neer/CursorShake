# CursorShake — experiencia de usuario y distribución

Comportamiento acordado; ejecutable y GUI implementados; evidencia y límites de validación en Arquitectura9. El instalador queda fuera del hito actual. [Arquitectura](Arquitectura.md) contiene los protocolos técnicos; [ConfiguracionInicial](ConfiguracionInicial.md), los defaults; [FormatosCursores](FormatosCursores.md), los recursos admitidos.

## 1. Instalación y primera ejecución

El hito actual entrega artifacts/publish/win-x64/CursorShake.exe junto con dependencias, RecoveryGuard, temas y avisos; conservar la carpeta completa. No hay release público ni instalador todavía. Ejecutar el EXE no registra inicio automático por sí solo.

En la fase de instalador, el usuario descargará CursorShake-Setup.exe de GitHub Releases. El instalador permite elegir carpeta de binarios, registra desinstalación y acceso en Inicio. Ofrece «Iniciar con Windows» y «Ejecutar al terminar» marcados y desmarcables. Ejecutar con identidad/sesión del usuario correcto, incluso si el destino requiere elevación. La aplicación trabaja normalmente sin administrador; no exige SDK ni instalación manual de .NET.

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

Ruta de trabajo recomendada: primera función de extremo a extremo → publicación autónoma y prueba fuera del entorno de desarrollo → instalador básico para verificar rutas/identidad/inicio → producto y paquete final con pruebas de actualización/desinstalación. El ejecutable distribuible es un hito anterior al instalador final, pero conviene comprobar pronto supuestos de distribución. La implementación del ejecutable ha sido autorizada; el instalador sigue siendo una fase posterior.

Versiones de dependencias y runtime fijadas para este hito mediante proyectos y locks. Quedan para el instalador la identidad estable de instalación y la política de versión/migración. El aviso MIT del trabajo original se publica a nombre de R3Neer en LICENSE. Firma de código por resolver antes de distribuir ejecutables según medios disponibles; generar EXE no lo firma. La cobertura de pruebas y QA visual se define en Arquitectura9.

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
