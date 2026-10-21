# Registro de decisiones de CursorShake

Actualizado: 21-oct-2026. Fase: requisitos y diseño; implementación todavía no autorizada.

## Cómo usar este registro

Una decisión formal aceptada debe quedar en un documento del repositorio, no únicamente en el chat. Este registro resume qué se ha decidido, por qué y dónde está el detalle. Las propuestas y preguntas abiertas van en [PendientesDiseno.md](PendientesDiseno.md); no se convierten en decisiones por aparecer en un ejemplo o una explicación del asistente.

Cuando el usuario cambie una decisión, se actualizan su entrada y los documentos afectados, indicando qué acuerdo sustituye. No se mantienen dos reglas vigentes contradictorias. Los valores iniciales provisionales se identifican como tales y siguen siendo configurables.

## Decisiones aceptadas

| ID | Decisión y alcance | Motivo o consecuencia | Detalle |
| --- | --- | --- | --- |
| D-01 | El usuario conduce el diseño. Se mantiene la fase de especificación, sin implementación. | Primero se explican y discuten conceptos, datos y reglas; no se impone un catálogo de clases o patrones. | [Requisitos](Requisitos.md), sección 3. |
| D-02 | Implementar la futura aplicación en C#. | Elección explícita del usuario después de comparar Haskell y C#. La integración de bandeja, selector de carpetas y ciclo de vida de Windows favorece C#. | Este registro. Sustituye la evaluación abierta de lenguaje. |
| D-03 | No hay CLI de usuario. Interacción mediante bandeja, diálogos nativos y TOML. | La elección de rutas y el cambio rápido de temas deben ser accesibles gráficamente. | Requisitos R-31–R-35; ExperienciaUsuario, secciones 2–4. |
| D-04 | Usar como referencia las funciones estándar del cursor de Windows y su equivalencia verificada con los temas. | No es necesario inventar un catálogo basado en elementos como archivo/carpeta para las funciones estándar. La detección del rol real sigue pendiente de integración. | [Equivalencias](EquivalenciasCursores.md), tabla de 17 roles y extras. |
| D-05 | Aplicar el tema permanentemente y agrandar el cursor del rol actual. | El aumento no sustituye siempre el dibujo por una flecha. | Requisitos R-18–R-21. |
| D-06 | Temas iniciales: Windows, Bibata Ice y Nordzy blanco; se admiten asociaciones personalizadas. | Los extras sin código estándar se documentan por separado; todavía no se decide cómo reconocerlos. | Requisitos R-18 y R-30; EquivalenciasCursores. |
| D-07 | Resolver ausencias mediante recurso del rol → predeterminado del tema → recurso Windows del rol si no hay predeterminado. | La ausencia de una asociación no equivale a un recurso existente pero inválido. Default es un campo, no un nuevo rol visual. | Requisito R-22; EquivalenciasCursores, sección de predeterminados. |
| D-08 | Incluir animaciones desde el principio y conservar tiempos y hotspots. Separar animación del dibujo y del tamaño. | El escalado no reinicia ni acelera los fotogramas. Un cambio de rol conserva el tamaño/fase y comienza la animación del nuevo rol desde su inicio. | Requisitos R-23–R-28. |
| D-09 | Sacudida: cambio de dirección deliberado, también diagonal; umbral de activación, velocidad, recorrido e intervalo configurables. | La detección se basa en secuencias de movimiento. No se fuerza una ida y vuelta completa por sacudida. | Requisitos R-01–R-06; tabla de valores iniciales más abajo. |
| D-10 | Crecimiento continuo mientras se mantiene el gesto, máximo proporcional y transiciones ease-in/ease-out. La frecuencia acelera moderadamente el avance, no el máximo. | Separar intensidad/frecuencia del gesto de tamaño máximo. No se ha elegido una fórmula. | Requisitos R-07–R-13. |
| D-11 | Pulsar izquierdo, central o derecho inicia inmediatamente reducción, reinicia detección y bloquea nuevas activaciones mientras haya un botón pulsado. | Arrastre y selección no reactivan el aumento. | Requisitos R-14–R-16. |
| D-12 | Configuración en un TOML único, ubicación elegible desde la bandeja y traslado verificado sin abandonar el original. Generación automática de valores predeterminados si el archivo realmente falta. | Separar ausencia, contenido inválido y ubicación inaccesible. Aplicar cambios válidos sin reiniciar. | Requisitos R-31–R-33; [ExperienciaUsuario](ExperienciaUsuario.md), secciones 3–4. |
| D-13 | Instalador con destino elegible, acceso Inicio, desinstalación e inicio de sesión opcional, marcado inicialmente. Bandeja con activo/pausado/error. Sin instancias duplicadas. | El usuario final no necesita herramientas de desarrollo. Actualizar conserva configuración y ubicación. | ExperienciaUsuario, secciones 1–2 y 5. |
| D-14 | MIT para nuestro trabajo en la medida aplicable; los temas mantienen sus licencias originales. | No se declara todo el repositorio MIT si contiene recursos GPL. Recursos independientes, créditos, licencias y fuentes; dependencias por revisar. | ExperienciaUsuario, sección 6. |
| D-15 | Aceptar el icono azul de cursor con trazos de agitación, propuesta 01. | Conservar PNG original, ICO multirresolución y procedencia generativa. | [Generación del icono](../assets/branding/GENERACION.md). |
| D-16 | Respetar la organización del equipo mediante destinos configurables y mecanismos soportados. | AppData es un valor inicial permitido para preferencias pequeñas, no un destino impuesto. No se cambian HOME/USERPROFILE/TEMP globales ni se fuerzan traslados con enlaces. | Requisito R-36; ExperienciaUsuario, sección 3. |
| D-17 | Al concluir esta consolidación, realizar un commit local de la documentación y materiales de diseño aceptados. No hacer push ni implementar. | Autorización expresa del usuario de este turno. No constituye autorización permanente para futuros commits. | Estado del proyecto y alcance de esta entrega. |

## Valores iniciales acordados

| Parámetro | Valor | Naturaleza |
| --- | --- | --- |
| Sacudidas para activar | 3 | Configurable; valor inicial provisional. |
| Intervalo máximo entre sacudidas válidas | 0,1 s | Configurable; elección expresa del usuario. Sustituye propuestas anteriores de 0,5 s para este parámetro. |
| Pausa antes de reducir al dejar de agitar | 0,5 s | Configurable. Es independiente del intervalo de detección. |
| Aceleración máxima por frecuencia | ×1,5 | Punto de partida aceptado; no es el multiplicador máximo del tamaño. |
| Tamaño normal | ×1 | Referencia del escalado; el máximo será configurable y todavía no tiene valor inicial decidido. |

Velocidad mínima, recorrido mínimo, duración de crecimiento/reducción y función de aceleración no tienen valores ni fórmulas elegidos.

## Qué NO se ha decidido

- Versión de .NET, framework de interfaz de bandeja y publicación JIT/AOT/ReadyToRun. .NET 10/JIT fue una recomendación, no una elección formal del usuario.
- Clases, estructura de módulos, patrón Command, bus de eventos, concurrencia y modelo de dominio definitivo.
- Raw Input, hooks, polling o combinación, ni frecuencia de muestreo/renderizado. Los 60 Hz mencionados eran una hipótesis de evaluación.
- Método de representación gráfica: SVG, CUR/ANI, otro formato, biblioteca o renderizado nativo/overlay.
- Que la clave de registro para recordar el TOML sea ya una decisión tomada: sigue siendo propuesta.
- Inno Setup u otro instalador concreto, mecanismo de inicio automático y actualizador.

Estas cuestiones se desarrollan en [PendientesDiseno.md](PendientesDiseno.md). No se toma ninguna por defecto durante la implementación.
