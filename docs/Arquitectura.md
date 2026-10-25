# CursorShake — arquitectura

Especificación vigente, 25-oct-2026. Fase de diseño: aplicación sin implementar. Acuerdos funcionales adoptados; detalles técnicos y pruebas pendientes se identifican en su sección. Revisión documental cerrada en ronda3/10 con ambos revisores sin objeciones; recibo en apartado11.

Referencias: [configuración inicial y reglas](ConfiguracionInicial.md), [experiencia de usuario](ExperienciaUsuario.md), [equivalencias y procedencia](EquivalenciasCursores.md). No duplicar tablas ni convertir propuestas en acuerdos por repetición.

## 1. Acuerdos y simplificación central

C#, bandeja, tema permanente y crecimiento por sacudidas. Sin CLI de producto, gestos generales, plugins, scripts externos, interceptar clics ni mover el puntero. El usuario dirige el dominio; la integración Windows se concreta dentro de esos límites.

Entrada traduce movimiento y botones a objetos semánticos. **ShakeDetector es el controlador del gesto**; decide estados y dirige GrowthController. GrowthController calcula la evolución solicitada y comunica escala al motor. AppHost compone y termina, no dirige cada movimiento.

**Windows selecciona el rol, coloca, muestra y oculta el cursor.** Preparamos un mapa completo por tema y publicamos recursos nativos con reloj propio de fotogramas para escalar sin reiniciar ANI. Observar rol/identidad/visibilidad/DPI sigue siendo necesario, pero queda dentro del motor.

No hay controlador contextual por hover ni fachada de renderer adicional. Observación nativa y publicación pertenecen al motor, un módulo con auxiliares, no una clase dios. No se promete controlar cursores privados o dibujados por aplicaciones.

Los límites de presentación y las políticas de compatibilidad se definen en el apartado 4; los valores y reglas del gesto, únicamente en ConfiguracionInicial.

## 2. Componentes y dependencias

```mermaid
flowchart LR
    W[Windows movimiento y botones] --> I[PointerInput]
    I -->|ShakeStroke y ButtonsChanged| S[ShakeDetector]
    S -->|Ordenes semanticas| G[GrowthController]
    G -->|ScaleSample| E[WindowsCursorEngine]
    C[ConfigurationCoordinator] -->|Perfil efectivo| S
    C -->|Tema preparado| E
    F[WorkspaceStore y FileMonitor] --> C
    P[ThemePreparer] --> C
    E -->|Recursos por rol| N[Windows selecciona y presenta]
    N -->|Observacion tecnica| E
    H[AppHost y bandeja] --> C
    H --> S
    H --> E
```

| Componente | Ownership | Colaboradores internos |
| --- | --- | --- |
| AppHost | Composición, instancia, sesión, arranque/cierre y comandos de bandeja. | TrayApplicationContext/TrayView, StartupRegistration, SessionMonitor y coordinación de RecoveryGuard. |
| RuntimeLoop | Estado serial, admisión ordenada y próximo despertar. | Reloj monótono y adaptador de espera/dispatch. No decide reglas. |
| PointerInput | Traducción semántica, botones y preparación de tramos. | WindowsPointerSource captura; ShakeStrokeRecognizer reconoce inversiones. |
| ShakeDetector | Cuenta, bloqueo, fase semántica y fechas de cambio. | GrowthController y perfil efectivo; máquina compacta, sin clase por estado. |
| GrowthController | Única evolución de escala y avance de curva. | GrowthProfile como valor/funciones. No decide umbral de activación. |
| WindowsCursorEngine | Tema instalado, escala, activación de rol y reloj visual. | NativeCursorSession, CursorTimeline, RasterCache. |
| ConfigurationCoordinator | Revisiones deseadas/preparadas/efectivas y diagnósticos; pipeline único. | Parser, validador, ThemePreparer concretos. Sin fachada adicional. |
| WorkspaceStore | Ubicación, plantilla, lectura/escritura y traslado recuperables. | FileMonitor, LocatorStore y operaciones separadas de escritura/traslado. |
| ThemePreparer | Resolver mapa y fallback, verificar y preparar fuentes. | CursorAssetLoader, decodificadores/rasterizador; no modifica Windows. |

Cada fila es responsabilidad, no archivo único. No fusionar parser/traslado/captura/presentación para reducir nombres. Tres puertos aíslan dominio: **IPointerInput**, **ICursorEngine**, **IMonotonicClock**. Sin interfaz por helper, bus global, IFileSystem genérico ni contenedor de inyección obligatorio. El motor tiene API semántica aunque internamente conozca Windows.

```mermaid
classDiagram
    class IPointerInput {
        <<interface>>
    }
    class ICursorEngine {
        <<interface>>
    }
    class IMonotonicClock {
        <<interface>>
    }
    class PointerInput
    class WindowsPointerSource
    class ShakeStrokeRecognizer
    class ShakeDetector
    class GrowthController
    class GrowthProfile
    class WindowsCursorEngine
    class NativeCursorSession
    class CursorTimeline
    class RasterCache
    class ConfigurationCoordinator
    class WorkspaceStore
    class ThemePreparer
    PointerInput ..|> IPointerInput
    PointerInput *-- WindowsPointerSource
    PointerInput *-- ShakeStrokeRecognizer
    WindowsCursorEngine ..|> ICursorEngine
    WindowsCursorEngine *-- NativeCursorSession
    WindowsCursorEngine *-- CursorTimeline
    WindowsCursorEngine *-- RasterCache
    ShakeDetector --> IPointerInput
    ShakeDetector --> GrowthController
    GrowthController --> GrowthProfile
    GrowthController --> ICursorEngine
    ConfigurationCoordinator --> WorkspaceStore
    ConfigurationCoordinator --> ThemePreparer
    ConfigurationCoordinator --> ShakeDetector
    ConfigurationCoordinator --> ICursorEngine
```

## 3. Contratos y estado serial

Son especificaciones, no contratos compilados. No exponer handles/eventos Win32 al dominio.

| Dato | Campos necesarios / consumidor |
| --- | --- |
| Instant / Duration | Base monótona común / duración. Calendario solo en diagnóstico. |
| ShakeStroke | OccurredAt: instante de admisión de la muestra que CONFIRMA una inversión válida, no el extremo retrospectivo. Cuenta/cadencia usan esta base común; PeakAt puede quedar en diagnóstico del reconocedor. Geometría no viaja al crecimiento. |
| ButtonsChanged | Instante, conjunto izquierdo/central/derecho y transición. Detector bloquea hasta soltar todos. |
| GestureProfile | Reconocimiento, cuenta, tiempos y GrowthProfile; una identidad para todo el conjunto efectivo. Defaults en documento propio. |
| GrowthDrive | Cadencia interpretada por detector o ausencia explícita. No movimientos/contador para reinterpretar activación. |
| ScaleSample | Scale≥1, instante de evaluación y GestureEpoch; máximo del perfil de ese gesto. |
| PreparedTheme | Mapa completo, fuentes/timings/hotspots, revisión y ownership transferible. |
| LoadToken | LifecycleEpoch, WorkspaceGeneration, ContentRevision, ResourceRevision. No contador genérico que oculte qué invalida. |
| ConfigurationStatus | Revisión deseada/preparada, configuración efectiva/pendiente (tema y perfil juntos) y diagnósticos. |

### 3.1. Bucle, próximos plazos y captura

RuntimeLoop posee dominio y commits lógicos. Componentes ofrecen Advance(now) y NextDeadline; se despierta en el menor plazo o entrada. Sin timer/callback cancelable por clase ni giro continuo sin plazos. UI recibe estado por dispatcher, nunca llamadas desde worker arbitrario.

Captura nativa breve, con hilo de mensajes dedicado si hace falta. Admisión serial asigna secuencia e instante común; movimiento, botones y discontinuidades se procesan en ese orden. No reducir movimiento a una posición por fotograma. Transporte acotado: pérdida/atraso incompatible resetea tramo/cuenta y resincroniza, no inventa activaciones. No se promete preservar tiempo físico no observado. Cola y atraso admitidos serán límites explícitos medidos.

Lectura/preparación y rasterización salen del bucle. Commit serializa mutaciones de presentación. Una API nativa puede tardar: se mide y luego se procesan entradas admitidas; nunca bloquear la captura esperando rasterizar.


Contrato temporal: PointerInput reconoce en el RuntimeLoop, sobre muestras admitidas en FIFO. OccurredAt de ShakeStroke es la marca de admisión de la muestra que confirma el tramo invertido; no se reconstruye una orden de dominio en el extremo anterior. Las trazas del medidor conservan extremos retrospectivos: su calibración de130ms es punto de partida, y habrá que contrastar la cadencia entre confirmaciones con el reconocedor definitivo. No fingir equivalencia perfecta de ambos relojes ni cambiar130ms automáticamente.

El bucle distingue **tiempo de ocurrencia/admisión**, usado para intervalos, de **ApplyAt**, tomado del reloj monótono al procesar una orden y nunca menor que el último ApplyAt. No calcular una curva en el pasado. Wake/plazos entran por la misma admisión FIFO; antes de un mensaje se resuelven los vencidos estrictamente anteriores a su marca, y las coincidencias siguen la política de prioridades aceptada en ConfiguracionInicial. Un prefijo admitido tiene orden determinista; el handler no adelanta plazos por delante de mensajes ya admitidos solo porque ahora el reloj de pared monótono sea mayor.

Si una fuente trae una marca anterior al watermark de ocurrencia ya consumido, hay reordenación/pérdida o atraso superior al presupuesto de admisión, el dueño de admisión declara discontinuidad: reset coordinado de tramo/cuenta y resincronización, sin replay ni restaurar una cuenta ya vencida. Una pulsación tardía se procesa a ApplyAt actual y bloquea/reduce, nunca se pierde para evitar discontinuidad. El valor del presupuesto de atraso queda para medición; su propietario y resultado están fijados. Un retraso tolerado conserva OccurredAt para cadencia y aplica órdenes a ApplyAt actual, no retroactivamente.

### 3.2. Gesto y crecimiento

ShakeDetector posee Idle/Counting/Growing/Holding/Returning/Blocked; GrowthController solo evolución matemática ordenada. No dos máquinas deciden fin/rearme. Finalización de retorno vuelve al detector con GestureEpoch; épocas viejas no cambian fase.

Órdenes: BeginGrowth(drive,now), UpdateDrive(drive,now), Hold(now), Return(now), Advance(now). Antes de orden se evalúa escala actual, sin saltar a×1. Reglas en ConfiguracionInicial.

Down limpia primero tramo de entrada y después cuenta, reduce y bloquea mientras haya botones. Inicio/reanudación toma snapshot de botones antes de habilitar. Pausa, pérdida/sesión y cambio efectivo de reconocimiento limpian ambas memorias. Solo ShakeDetector consume input semántico; lifecycle/configuración coordinan su reset sin otro controlador del movimiento.

### 3.3. Perfil efectivo y reconfiguración

DesiredConfiguration es último candidato válido; EffectiveConfiguration contiene tema y perfil efectivos. EffectiveGestureProfile tiene identidad común para PointerInput, ShakeDetector y GrowthController.

Preparar inmediatamente y conservar un pendiente latest. Aplicar TODO el candidato al llegar a ×1, en un turno serial: publicar tema e instalar perfil coherente; resetear tramo/cuenta. Un fallo conserva/compensa conjunto anterior sin activar solo perfil. UI distingue configuración efectiva y pendiente. Win32 conserva límites de atomicidad de 5.1.

## 4. Motor nativo y recursos

API: SetScale(ScaleSample), TryApplyTheme(PreparedTheme), Suspend y Stop/Restore. GrowthController solo usa SetScale; composición usa las otras operaciones. No lee TOML ni interpreta movimiento.

NativeCursorSession posee baseline, identidades compartidas/propias, observación y publicación. CursorTimeline es lógica temporal interna, no controlador del contexto de aplicaciones. Windows selecciona/presenta; el motor transforma dibujo/hotspot y no fuerza SetCursor ajeno.

Cambio de rol conserva escala/fase de crecimiento y empieza fotograma0 de nueva activación. Repetir rol, mutación propia, movimiento o escala no reinician. Roles inactivos se preparan para reentrada. Observar transiciones muy breves no está garantizado. HCURSOR privado no se clasifica por parecido; Hidden≠Unknown. Sin overlay/ShowCursor para ampliar compatibilidad sin aprobación.

### 4.1. Preparación y fallback

ThemePreparer resuelve mapa completo una vez: asociación específica→predeterminado del tema→catálogo estándar Windows del mismo rol. Asociación existente inválida es error, no ausencia. Validar tema seleccionado completo, no solo rol visible. Un tema inactivo inválido puede figurar no disponible y se prepara al solicitarlo.

Loader valida dimensiones, formato, frames, duraciones y hotspots antes de entregar ownership. Los cuatro formatos y sus metadatos se definen exclusivamente en [FormatosCursores](FormatosCursores.md). CursorAssetLoader los normaliza a fuentes, hotspots y pasos temporales. Retener instrucciones vectoriales y rasterizar al tamaño final; CUR/ANI raster no adquieren resolución adicional.

Windows del catálogo y último fallback significan apariencia estándar Windows/Aero. Para Wait/Working se acepta la recreación geométrica propia; los otros roles requieren verificar fuente y fidelidad. El baseline de restauración es el esquema real anterior a CursorShake, que puede ser otro tema. Ejemplo: Bibata externo previo → elegir Windows muestra Aero → salir restaura Bibata. La referencia estándar no se obtiene recapturando nuestras propias sustituciones. No redistribuir imágenes propietarias de Windows.

NativeCursorSession distingue recursos propios, externos y desconocidos. Ante cambio externo detectado cede control, pausa mutaciones y no restaura encima. Reanudar requiere una referencia coherente; no capturar un mapa híbrido como nuevo baseline. No existe garantía de compare-and-swap de SetSystemCursor ni compatibilidad con dos gestores modificando el esquema a la vez.

### 4.2. Progreso sin backlog

HardEpoch cambia por lifecycle, tema/recurso, activación de rol o DPI incompatible. **Escala nueva y avance del tiempo NO invalidan por sí solos un trabajo.** Un activo y un destino latest. Un resultado compatible se publica aunque haya nuevas escalas; medir latencia e iniciar luego destino vigente. Así hay progreso con trabajos de40ms y solicitudes cada16,7ms.

Cambio duro descarta/libera resultado. Cambios duros continuos pueden impedir publicar un contexto ya desaparecido; no crean cola. Reloj absoluto: saltar pasos vencidos no reinicia/frena animación lógica. Preparación/publicación tiene latencia; no se promete exactitud infinita de cada frame bajo carga.

### 4.3. Presupuesto aceptado y ownership

| Recurso/límite | Dueño/regla |
| --- | --- |
| 2048px físicos/lado | Preparar conjunto de roles a máximo/DPI aplicable. No recortar cada rol con min que cambie multiplicador. |
| Hasta60Hz escala | Techo/objetivo visual, no captura ni velocidad ANI; sin ticks de escala estable. |
| Caché raster64MiB | RasterCache cuenta imágenes retenidas, incluidas copias nativas estimables; reserva antes de insertar y expulsa sin uso. No contar solo referencias managed. |
| Trabajo gráfico | Uno activo+destino latest; liberar resultados obsoletos antes de admitir reemplazo. |
| Preparación configuración | Un candidato activo+descriptor latest, independiente del gráfico. Cancelación cooperativa no inicia candidatos nuevos hasta liberar anterior. |
| Temas | Admisión explícita de bytes fuente, frames y representación vector/decodificada; contar baseline/activo/candidato/compensación. Valores por fijar antes de congelar loader; no esconderlos en los64MiB. |
| Temporales | Medir filtros/copias y reservar presupuesto propio. Concurrencia acotada no garantiza memoria total acotada. Rechazar admisión que exceda presupuesto. |
| Handles | SafeHandle/lease nativo. LoadCursor compartido no se destruye; SetSystemCursor consume copia propia. Toda cancelación tiene dueño. |
| Baseline | NativeCursorSession captura esquema previo para restaurar; fallback usa catálogo Windows estándar. Ceder ante cambios externos, según 4.1. |

ARGB2048² cuesta16MiB solo en píxeles;54frames=864MiB. No precalcular combinaciones ilimitadas. Si no alcanza capacidad, reducir frecuencia conservando fuente/calidad, sin estirar miniaturas ni quitar sombras. Medir reposo, carga sostenida y DWM/GPU antes de afirmar consumo del producto.

### 4.4. DPI, visibilidad y sesiones

El tamaño normal sigue el escalado Windows y se conserva el multiplicador configurado. Validar máximo para todos los monitores conectados. Si aparece un contexto/DPI que excedería el límite físico, bloquear crecimiento allí, mantener representación segura y retornar antes de adoptar el nuevo tamaño normal. Avisar una vez con máximo compatible; no editar TOML ni recortar roles individualmente. Rearmar al recuperar compatibilidad. La transición nativa entre DPI requiere pruebas del producto.

Cursor privado/desconocido: conservar dibujo de la aplicación, cancelar gesto y normalizar roles propios sin imponer selección. Cursor oculto: no mostrarlo ni admitir activaciones. Reentrada visible/conocida en ×1 requiere activación completa. Bloqueo, suspensión o cambio de usuario resetean el gesto; normalizar si escritorio permite, o diferir. No operar sobre escritorio seguro.

Una instancia por sesión interactiva, con exclusión por usuario para mutaciones de workspace compartido. Plataforma inicial objetivo: consola local Windows 11 x64; efecto desactivado en contextos remotos/no certificados. La política está acordada; la certificación del producto no se ha ejecutado.

## 5. Configuración: un coordinador, dos productos

ParsedConfiguration contiene documento tipado/origen/diagnósticos. PreparedConfiguration añade validación y recursos. Active es ownership/estado, no cuarto modelo clonado. Applying/Degraded describen operación y no se confunden con qué tema/perfil es efectivo.

Pipeline único de arranque/watcher/bandeja: localizar→asegurar archivo→leer→parsear entero→validar→preparar→comprobar LoadToken→aplicar→publicar estado efectivo. Fallo conserva último aplicable, no anuncia éxito. ThemePreparer no modifica Windows; coordinador no decodifica gráficos/captura.

FileMonitor usa FileSystemWatcher sobre carpeta: Changed/Created/Renamed/Deleted/Error de TOML y recursos personales referenciados. Debounce y reintento de guardados transitorios; reconciliar por contenido/generación, no hash periódico primario ni botón Aplicar. Error/overflow fuerza reconciliación completa. Resultado obsoleto libera recursos sin culpar al TOML.

Ausencia real en carpeta accesible crea exclusivamente plantilla empaquetada ConfigurationDefaults, también fuente de defaults en memoria. Presente inválido se conserva; usar último preparado o defaults en memoria sin uno previo. Unidad/permisos inaccesibles no son ausencia ni permiten dispersar copias. Recursos personales borrados no se regeneran.

### 5.1. Aplicación y estado observable

Mapa de roles no es transacción atómica de Windows. Preparar rollback, retener baseline/tema previo y serializar mutaciones. Fallo intenta compensar solo lo propio; si falla compensación, Degraded con roles/errores, detener mutaciones del efecto y retener recursos de recuperación. No decir restaurado por completo.

Tema y perfil son unidad de validación y compromiso lógico. Esperar ×1 para aplicar conjunto. Tema rechazado no activa perfil acompañante. No declarar Applied con partes pendientes o Degraded.

```mermaid
sequenceDiagram
    participant F as FileMonitor
    participant C as ConfigurationCoordinator
    participant P as Parser y ThemePreparer
    participant E as WindowsCursorEngine
    participant D as ShakeDetector
    participant U as Bandeja
    F->>C: Posible cambio
    C->>P: Preparar con LoadToken
    P-->>C: PreparedConfiguration o diagnosticos
    alt Obsoleto o error
        C->>C: Liberar candidato y conservar efectivos
    else Vigente
        C->>C: Esperar reposo x1 y conservar latest
        C->>E: TryApplyTheme
        E-->>C: Applied o Compensated o Degraded
        alt Tema aplicado
            C->>D: Instalar perfil del mismo candidato y reset
        end
    end
    C-->>U: Revisiones efectivas y pendientes
```

### 5.2. Escritura de bandeja frente a editor externo

Protocolo técnico propuesto; leer y luego reemplazar no equivale a compare-and-swap:

1. WorkspaceStore serializa escrituras/traslado internos. Abre config.toml para lectura/escritura compartiendo solo lectura: deniega nuevas escrituras/delete mientras mantiene ESE handle. Si editor tiene escritura incompatible, Busy; no forzar cierre.
2. Leer/verificar contenido/FileId por handle protegido. Revisión distinta o inválida→Conflict/Invalid sin escribir. Parchear solo ajuste solicitado preservando comentarios/resto.
3. Antes de tocar TOML, journal durable con identidad, previo y candidato en estado interno. Material transitorio recuperable, no histórico acumulativo.
4. Escribir por MISMO handle, ajustar longitud, flush y verificar antes de soltar. Sin hueco cerrar-comparar-reemplazar ni rename sobre handle que deniega delete. No prometer visibilidad atómica a otros lectores durante escritura in-place.
5. Confirmar journal, liberar bloqueo y usar pipeline normal. Limpiar temporales al terminar. Watcher propio no procesa parcial propio ni se desactiva frente a ediciones posteriores.

Crash: si coincide con candidato confirmado, finalizar; si coincide con previo, abandonar sin cambio. Parcial/distinto/edición ajena: conservar TOML actual y versiones del journal, RecoveryRequired con rutas y resolución guiada; no sobrescribir edición posterior. Usar último efectivo/defaults en memoria y mostrar error accesible. Popup con rutas/versiones conservadas y resolución explícita. Contrato de recuperación pendiente de concretar antes de implementar.

## 6. Ubicación y traslado: un punto de autoridad

Workspace es RootPath/ConfigPath/ThemesPath/Generation, no carpeta EXE. Localizador fuera del TOML trasladado: un valor HKCU\Software\CursorShake con Root+OperationId y fase de operación. AppData para estado pequeño cumple política; no se mueve perfil global.

Única raíz autoritativa: **Root durable del localizador**. Watcher/candidatos/snapshot se derivan, no son autoridades alternativas. Journal referencia source/destination/manifest/hashes y operación ligada al estado de commit, sin adivinar por fecha.

| Etapa durable | Autoridad | Recuperación |
| --- | --- | --- |
| Sin operación / Preparing | Origen | Copiar sin retirar; no adoptar destino parcial. |
| Prepared | Origen | Destino verificado/preparado; cancelar sin cambiar Root. |
| Commit Root=destino, OperationId=CleanupPending | Destino | Punto único de cambio; reconciliar watcher/snapshot contra Root. |
| CleanupPending | Destino | Conservar originales hasta coherencia del conjunto destino y trabajos; limpiar solo identidades/hashes confirmados. |
| Completed | Destino | Limpiar journal, sin copia antigua activa. |

Root y estado/OperationId se escriben como UN valor durable, no dos claves con orden ambiguo. Manifest se escribe/verifica antes. Tecnología concreta debe cumplirlo y probar interrupciones; no fingir filesystem transaccional implementado.

Trasladar solo config.toml y themes personales; no binarios, raíz entera, incorporados ni enlaces externos. Cancelar/same root no cambian nada; rechazar raíces anidadas/incompatibles. Copiar sin sobrescribir colisiones; verificar recursos/rutas y cambios concurrentes antes de commit. Cambio durante preparación conserva versiones nuevas y abandona/reconcilia.

Tras commit invalidar WorkspaceGeneration y vigilar destino. Caída previa al nuevo watcher/snapshot: arranque lee Root=destino. Retardar limpieza si destino no se hace efectivo. Nunca retirar por inventario antiguo: verificar de nuevo identidad/hash, no seguir enlaces; diferencias quedan conservadas, CleanupPending/RecoveryRequired. No prometer cero copias tras interrupción, sino no retirar sin verificación.

```mermaid
sequenceDiagram
    participant U as Selector
    participant S as WorkspaceStore
    participant L as LocatorStore
    participant C as ConfigurationCoordinator
    U->>S: Move(destination)
    S->>S: Copiar y verificar, sin borrar origen
    S->>C: Preparar destino
    C-->>S: Prepared o error
    S->>L: Commit unico Root y OperationId
    Note over L,C: Destino es autoridad durable
    S->>C: Reconciliar destino e invalidar origen
    C-->>S: Estado efectivo o error recuperable
    S->>S: Limpiar originales aun verificados y no usados
```


La retirada de cada original NO es check-then-delete por ruta: abrir con lectura y DELETE y compartición que impida writers/reemplazos incompatibles, verificar identidad/hash por ESE handle y marcar para retirada esa MISMA identidad mediante operación por handle. No cerrar/reabrir ni resolver de nuevo la ruta después de comprobar. Si no se obtiene la protección, hay reparse/enlace no autorizado o cambia identidad, conservar entrada y CleanupPending/Conflict. Directorios solo se retiran vacíos con identidad protegida, nunca recursivamente por un inventario previo; un objeto nuevo en la ruta no se retira por pertenecer al mismo nombre. Se verifican identidades/volumen y no se fuerzan ACL/propietarios. La semántica de la API por handle y compartición se verificará al implementar el auxiliar, no se sustituirá por Delete(path) como atajo.

## 7. Lifecycle, bandeja y arranque

AppHost compone por constructores, adquiere SingleInstanceGuard, recupera journals, establece baseline coherente, carga configuración y publica estado real. No duplicar captura/efecto ante otra instancia en la misma sesión. Pausa/reanudación y cambios de sesión siguen los resets de entrada/dominio y las políticas de 4.4.

Cierre: Stopping deja de admitir entradas/candidatos e invalida épocas; cancelar preparación y esperar liberación. Una API nativa ya en curso puede terminar, no prometer interrumpir Win32. Barrera serial de presentación rechaza publicaciones posteriores, drena la ya iniciada y solo después restaura/libera. StopCompleted implica ninguna mutación futura/callback suscrito/lease pendiente. No restaurar primero y permitir que un resultado tardío reinstale el cursor.

```mermaid
sequenceDiagram
    participant H as AppHost
    participant D as Entrada y dominio
    participant C as Configuracion
    participant E as Motor
    H->>D: Dejar de admitir, reset y epoca
    H->>C: Cancelar candidatos y vigilancia
    H->>E: Stop con barrera de publicacion
    E->>E: Drenar, liberar, restaurar lo propio
    E-->>H: StopCompleted o error de restauracion
    H->>H: Retirar bandeja y disponer captura y loop
```

La experiencia de bandeja, popups, rutas e instalación se define en [ExperienciaUsuario](ExperienciaUsuario.md). WinForms despacha comandos al RuntimeLoop; ninguna ventana manipula directamente el estado del gesto o los roles.

StartupRegistration usa tarea por usuario InteractiveToken/LeastPrivilege, delay0, sin condiciones de red/reposo/batería y sin límite de72h (ExecutionTimeLimit=PT0S). No duplicar Run+tarea ni usar servicio de sesión0. El instalador conserva identidad del usuario original aunque deba elevarse para el destino elegido. StartupProbe prueba lanzamiento manual, no latencia de logon del producto.

RecoveryGuard es un proceso auxiliar de recuperación, no otro controlador: espera muerte del proceso y solo intenta restaurar último baseline verificado sobre roles identificables como propios. Cede ante cambios ajenos; sin gestos, relanzamiento en bucle ni preferencias independientes. Un cierre correcto debe comunicar restauración/disposición antes de desarmar al auxiliar; un fallo deja recibo y recuperación explícita. Contrato exacto de ownership, identificación de generaciones y handshake por definir/verificar antes de implementar esta caja.

## 8. Evidencias y límites de lo probado

Las alternativas antiguas dejan el cuerpo operativo, no sus evidencias. Experimentos enlazados conservan protocolos, fuentes primarias, versiones y mediciones. Son pruebas aisladas autorizadas, no implementación del producto.

| Evidencia | Conclusión / límites |
| --- | --- |
| [AniScaleProbe](../experiments/AniScaleProbe/README.md) | Sustituir ANI nativo pierde fase/tiempo restante. No transferencia pública de fase demostrada. |
| [NativeRoleProbe](../experiments/NativeRoleProbe/README.md) | Reloj propio y frames estáticos nativos funcionan entre procesos/Explorer y cuatro roles adicionales. Copias privadas no se actualizan por cambiar compartidos. |
| [EngineLimitsProbe](../experiments/EngineLimitsProbe/README.md) | Roles/identidad propia y hasta4096px visibles en este equipo;257+ compuestos en escritorio. No garantía universal.2048 no sostiene60Hz. |
| [VectorRasterProbe](../experiments/VectorRasterProbe/README.md) | Muestras Bibata/Nordzy con filtros/alpha e instrucciones retenidas. Espera Bibata60/45/29 publicaciones/s hasta512/1024/2048, cuatro segundos. No todos los roles/frames ni DWM/GPU/potencia. |
| [AeroVectorProbe](../experiments/AeroVectorProbe/README.md) | Apariencia Wait/Working aceptada;18pasos de50ms y hotspots verificados. No aceptación automática de otros roles. |
| [StartupProbe](../experiments/StartupProbe/README.md) | Tarea por usuario normal creada/lanzada y retirada. No prueba latencia de logon del producto. |
| [ShakeTimingProbe](../experiments/ShakeTimingProbe/README.md) | Calibración130ms adoptada. No detector definitivo ni validación de falsos positivos cotidianos. |

Equipo probado Windows11 25H2 26200.9711. No certificar drivers/DPI/sesiones/versiones por esos resultados. Motor nativo con reloj propio es la vía elegida; overlay/ANI recompuesto por escala requieren rediseño explícito.

## 9. Preparación técnica y validación

No quedan decisiones funcionales que requieran una nueva aceptación antes de comenzar la implementación cuando el usuario la autorice. Lo siguiente es trabajo técnico delegado, no funcionalidad ya comprobada:

| Trabajo | Resultado requerido |
| --- | --- |
| Contratos compartidos | Firmas C#, ownership, resultados/errores y ejemplos comunes antes de implementar cajas en paralelo. Los diagramas actuales son conceptuales. |
| Entrada | Concretar geometría/normalización DPI, cola y atraso máximo; contrastar confirmaciones con calibración, sin cambiar silenciosamente defaults. |
| Configuración | Nombres/rangos/versión del esquema, plantilla comentada única y tratamiento de versiones incompatibles sin sobrescribir. |
| Recursos | Esquema detallado de VCUR/VANI, cuotas de admisión y picos temporales; comprobar paquetes reales, según FormatosCursores. |
| Recuperación | Contratos de journals, bloqueo por usuario, API de retirada por handle y RecoveryGuard; inyectar fallos en cada fase. |
| Dependencias y distribución | Fijar versiones/licencias, integrar fuentes de temas con procedencia y completar recursos Windows. Publicar carpeta autónoma; instalador separado. |

Cambios que alteren comportamiento acordado o una limitación material descubierta se consultan al usuario. Los detalles técnicos anteriores pueden resolverse y registrarse autónomamente; no deben etiquetarse como elecciones humanas ya ratificadas.

Pruebas por código: trazas de movimientos/botones, relojes simulados y prioridades; curvas y límites; decodificación/formato/hotspot; FileSystemWatcher y guardados parciales; cambios concurrentes; traslado/recuperación con fallos antes y después de commit; publicación lenta, resultado obsoleto, cambio de rol y cierre con trabajo activo; conservación de handles/recursos y consumo sostenido. Las pruebas nativas requieren Windows interactivo cuando usan APIs de cursor, aunque se controlen mediante código.

La automatización sin computer use cubre gran parte de la lógica e integración, no certifica por sí sola sensibilidad percibida, fluidez/nitidez, acabado de diálogos, distintos monitores/drivers o logon real. Se reservan comprobaciones visuales y de uso para esos resultados. No cerrar sesiones activas ni cambiar arranque persistente como efecto de una comprobación no autorizada.

## 10. Cobertura de requisitos

Los identificadores existentes se conservan como trazabilidad; las reglas normativas viven en los documentos enlazados, sin otra copia de sus cifras.

| Requisitos | Contenido y fuente |
| --- | --- |
| R-01–R-06 | Cuenta configurable, inversiones diagonales, velocidad/recorrido/intervalo y gesto sin botones: ConfiguracionInicial, apartados 1–2. |
| R-07–R-13 | Crecimiento continuo, multiplicador, curva, cadencia, pausa, retorno y reanudación: ConfiguracionInicial, apartados 1–2. |
| R-14–R-17 | Clic inicia retorno, reinicia/bloquea, tres botones y continuidad de tamaño: ConfiguracionInicial, apartado 2. |
| R-18–R-22 | Tres temas, uso permanente, rol activo, asociaciones y fallback: apartados 1 y 4.1; EquivalenciasCursores. |
| R-23–R-29 | Animaciones, tiempos originales, relojes independientes, fase al escalar, cambio de rol/hotspot y vectores: apartado 4 y FormatosCursores. |
| R-30 | Temas personales, recursos admitidos y traslado conjunto: FormatosCursores y ExperienciaUsuario. |
| R-31, R-31a, R-32 | TOML desde bandeja, recarga al guardar y regeneración solo por ausencia: apartado 5 y ExperienciaUsuario. |
| R-33–R-36 | Traslado verificado, sin CLI, inicio/salida y política de rutas: apartados 6–7 y ExperienciaUsuario. |

## 11. Revisión documental

Revisión documental cerrada el25-oct-2026 en ronda3 de un máximo de10. Arquitecto, simplificador y adversario revisan el mismo conjunto: cinco documentos temáticos, README/AGENTS, siete informes experimentales y procedencia del icono. Ambos revisores terminan sin objeciones sobre la misma versión.

| Ronda | Simplificador | Adversario | Resultado |
| --- | --- | --- | --- |
| 1 | Una objeción: pausa configurable implícita y ejemplos expresados como constantes. | Cero objeciones. | Aclarar parámetro y defaults. |
| 2 | Cero objeciones. | Dos objeciones: cuenta de sacudidas y duración de retorno expresadas como constantes. | Usar parámetros también en activación, rearme y preview. |
| 3 | Cero objeciones. | Cero objeciones. | Cierre por el criterio acordado. |

Después de los dictámenes solo se añade este recibo y se actualizan referencias de estado; ambos revisores aceptan ese procedimiento. La revisión valida consistencia, organización y conservación de acuerdos, no código inexistente ni QA del producto. Las versiones y revisiones anteriores se consultan en Git.
