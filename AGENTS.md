# Trabajo en CursorShake

## Alcance y autoridad

- Copia canónica: D:\Desarrollo\Proyectos\Herramientas\CursorShake. Preservar cambios locales y retiradas deliberadas; no reset/clean ni recrear documentos históricos.
- Fase actual: implementación autorizada expresamente por el usuario hasta ejecutable autónomo con GUI, temas, recuperación, pruebas y revisión. Autorizados crear proyectos, compilar y probar; sin instalador, instalación ni inicio automático persistente durante QA. No commit/push sin autorización nueva.
- Distribución: versión de proyecto 1.0.0. No crear la primera release de GitHub hasta que el instalador esté listo, según instrucción expresa del usuario.
- El usuario dirige comportamiento del dominio. Detalles técnicos de integración delegados pueden concretarse y documentarse; consultar cambios materiales del comportamiento o limitaciones nuevas.

## Fuentes y fronteras

- README es índice; docs/Arquitectura.md define responsabilidades/protocolos y trazabilidad; docs/ConfiguracionInicial.md es única fuente de defaults/reglas; docs/ExperienciaUsuario.md define uso/distribución; docs/EquivalenciasCursores.md conserva catálogo/procedencia; docs/FormatosCursores.md define recursos. No duplicar acuerdos ni mantener un documento por turno del chat.
- PointerInput traduce entrada a eventos semánticos; ShakeDetector dirige gesto y GrowthController; motor traduce a Windows. AppHost compone/lifecycle. Sin otro controlador del movimiento, clases dios, gestos generales, plugins o CLI de producto.
- Windows selecciona/posiciona/oculta; observación/publicación dentro del motor. Sin ThemeController contextual, overlay ni doble fachada Engine/Renderer. Interfaces solo en fronteras útiles.
- Tema permanente; fallback específico→predeterminado del tema→Windows estándar. Baseline de restauración es esquema previo distinto del catálogo Windows. Ceder ante cambios externos, no restaurar ciegamente.
- CUR/ANI/VCUR/VANI aceptados. VCUR/VANI: ZIP+cursor.toml+SVG, versión, hotspot viewBox, orden de bloques y bucle. No PNG/SVG sueltos iniciales. Cuotas de fuentes/modelos propuestas no se vuelven aceptadas por repetirse.
- Configuración se regenera solo ante ausencia confirmada accesible; inválido/inaccesible no es ausencia. Recursos personales no se regeneran. Tema/perfil se aplican juntos en ×1. No sobrescribir ediciones ni retirar originales sin verificación protegida.
- Errores por popup estándar y Ver errores; agrupar repetidos. Otro TOML en destino impide traslado con popup. Tras commit manda Root destino, sin rollback silencioso al origen.
- Stack y límites aceptados en sus documentos. No atribuir a muestras experimentales certificación total de roles, DPI, sesiones, rendimiento o memoria. QA visual y logon real no quedan probados por tests sintéticos.
- Respetar política del equipo: binarios/proyectos/datos separados; no mover AppData global ni forzar HOME/TEMP. MIT propio previsto, GPL de temas y licencias transitivas preservadas; no redistribuir imágenes Windows.

## Revisión iterativa

Arquitecto+simplificador+adversario, una misma versión por ronda, máximo diez. Cerrar solo con ambos sin objeciones. No retirar objeciones sin revisión ni atribuir dictámenes anteriores a cambios nuevos. Al agotar límite informar discusión final. Conservar solo recibo breve vigente en Arquitectura; Git conserva versiones. La revisión documental no sustituye la revisión de código; la implementación ha sido autorizada posteriormente.

## Objetivo y documentación durante implementación

- Objetivo autorizado: carpeta autónoma Windows11x64 con CursorShake.exe, RecoveryGuard, GUI/temas/dependencias; compilación reproducible, pruebas exigidas y revisión adversaria. Sin instalador, instalación persistente ni registro de inicio permanente durante QA. Distinguir evidencia automática, observación Windows y QA visual pendiente.
- Equipo previsto: dos implementadores (principal integra), adversario y responsable de pruebas. Concretar contratos comunes primero y repartir archivos por dueño; no rediseñar arquitectura como fase nueva. Pruebas que alteran cursores/entrada/registro/tareas se ejecutan exclusivamente y con limpieza verificada.
- Documentar solo cambios relevantes de comportamiento, contrato, formato persistente, dependencia importante o compilación/uso. Editar la fuente temática existente; no diarios, informe por agente, inventarios de clases ni crónicas por ronda.
- Código/tests describen implementación. Comentarios de contratos solo para unidades, tiempos, ownership y errores no evidentes. Decisiones no obvias: motivo y consecuencia breves. Hallazgos accionables con evidencia y prueba cuando proceda.
- Cerrar bloque con código, tests y actualización documental imprescindible. Tests exigidos pasando y adversario sin objeciones; máximo diez rondas por bloque. Al agotar informar discrepancias. Una síntesis final de resultados/limitaciones, sin duplicar informes.
- Árbol de Arquitectura12 aceptado; proceder con la implementación autorizada.
