# CursorShake — requisitos y decisiones de diseño

Estado: especificación en elaboración con el usuario. Fecha: 21 de octubre de 2026.

Este documento recoge acuerdos de la conversación, valores provisionales y cuestiones abiertas. No es un diseño de clases ni autoriza implementación. El usuario conduce el diseño; las propuestas deben discutirse antes de adoptarse.

## 1. Objetivo y alcance

Crear una utilidad de escritorio para Windows que permita utilizar un tema de cursores permanentemente y localizar el puntero mediante un aumento progresivo al agitar el ratón. El cursor correspondiente al estado actual crece conservando su punto de clic.

El lenguaje elegido es **C#**, por decisión expresa del usuario. La versión de .NET, el framework de interfaz y la modalidad de publicación quedan pendientes; JIT con .NET fue una recomendación inicial. No se ha cerrado la arquitectura ni medido el rendimiento. Véase [Decisiones.md](Decisiones.md), D-02.

Este repositorio conserva documentación y el icono aceptado. No se incorpora ni instala el código preliminar preparado anteriormente en ReorganizacionPC. El usuario ha autorizado un commit local de esta consolidación documental; no ha autorizado implementación ni push. Los commits futuros requieren su autorización correspondiente.

## 2. Requisitos acordados

### 2.1. Detección del gesto

- R-01. El número de sacudidas necesarias para activar el crecimiento es configurable. Valor inicial provisional: tres.
- R-02. Se cuentan cambios de dirección deliberados, incluidos los diagonales, en lugar de exigir movimientos completos de ida y vuelta.
- R-03. La velocidad mínima necesaria para que un movimiento contribuya a una sacudida es configurable. Valor y unidad pendientes.
- R-04. El recorrido mínimo necesario para validar una sacudida es configurable. Valor y unidad pendientes. Debe permitir ignorar pequeños temblores.
- R-05. El tiempo máximo entre sacudidas válidas antes de reiniciar el contador es configurable. Valor inicial acordado: **0,1 segundos (100 ms)**. Sustituye la propuesta anterior de 0,5 segundos.
- R-06. El gesto se detecta a partir del movimiento; no es necesario que el usuario pulse botones para activarlo.

### 2.2. Crecimiento y reducción

- R-07. Tras la activación, el crecimiento continúa mientras el usuario mantiene el gesto de agitación.
- R-08. El tamaño máximo se configura como multiplicador del tamaño normal del cursor, no como un número fijo de píxeles.
- R-09. Las transiciones de tamaño emplean ease-in/ease-out. La función exacta y las duraciones están pendientes de diseño.
- R-10. Una mayor frecuencia de sacudidas acelera moderadamente el avance del crecimiento. No modifica el tamaño máximo. Se acepta como punto de partida una aceleración máxima de ×1,5. La relación entre frecuencia y aceleración queda pendiente.
- R-11. Al dejar de agitar, se mantiene brevemente el tamaño alcanzado antes de reducirse. Esta pausa es configurable y su valor inicial es **0,5 segundos**.
- R-12. Tras la pausa, el tamaño vuelve suavemente a su valor normal.
- R-13. Reanudar la agitación durante la reducción permite retomar el crecimiento desde el tamaño actual, sin un salto de tamaño. Falta concretar si exige repetir el umbral completo de activación.
- R-14. Pulsar cualquiera de los tres botones considerados inicia inmediatamente la reducción, sin esperar la pausa de R-11.
- R-15. Pulsar un botón reinicia el contador de sacudidas. Mientras se mantiene algún botón pulsado no se permiten nuevas activaciones.
- R-16. Se consideran botón izquierdo, central y derecho. Los botones adicionales quedan fuera del alcance inicial.
- R-17. El tamaño permanece entre ×1 y el máximo configurado. No debe cambiar bruscamente por una transición o por un cambio de estado.

### 2.3. Temas, estados y representación

- R-18. Temas iniciales: Windows, Bibata Ice y Nordzy blanco.
- R-19. El tema seleccionado se utiliza permanentemente, también cuando el cursor está en tamaño normal.
- R-20. Al agitar, crece el cursor correspondiente al estado actual; no se sustituye siempre por una flecha genérica.
- R-21. Un tema permite asociar un recurso visual a cada estado soportado.
- R-22. Si falta la asociación de un estado, se utiliza el cursor predeterminado del mismo tema. Si tampoco se define ese predeterminado, se utiliza el cursor de Windows para ese estado. Esta última regla incorpora la aclaración más reciente del usuario. Debe preservarse la diferencia entre estado sin asociación y recurso asociado que no puede leerse.
- R-23. Se incluyen cursores estáticos y animados desde el principio si alguno de los tres temas iniciales contiene animaciones.
- R-24. Se conservan las duraciones originales de los fotogramas de una animación de cursor.
- R-25. La animación del dibujo y la animación del tamaño son independientes. El aumento no acelera la animación del dibujo.
- R-26. Cambiar el tamaño no reinicia la animación del dibujo.
- R-27. Al cambiar de estado del puntero, cambia inmediatamente su dibujo, conserva el multiplicador y la fase de crecimiento/reducción, y la animación propia del nuevo estado comienza desde su primer fotograma.
- R-28. La coordenada de pantalla del punto de clic o hotspot permanece fija durante el escalado. El hotspot puede estar en una posición distinta en cada recurso o fotograma; no se presupone que coincida con la esquina superior izquierda.
- R-29. Se desea evitar pixelado al crecer. **No se ha decidido todavía cómo representar ni admitir gráficos vectoriales y rasterizados**, ni los formatos aceptados. No se da por aprobada ninguna biblioteca ni estrategia de renderizado.
- R-30. Se contempla un tema personalizado mediante asociaciones a recursos; falta definir los formatos, metadatos, validación y alcance de la importación de paquetes.

### 2.4. Configuración y uso

- R-31. La configuración inicial se edita en TOML, accesible desde el menú contextual del icono de bandeja. No se requiere un editor GUI propio en la primera versión; sí un selector nativo para elegir su carpeta.
- R-32. La configuración se conserva en un TOML único. Si falta en su ubicación activa, se genera automáticamente con valores predeterminados. Archivo inválido y ubicación inaccesible se tratan por separado, sin sobrescribir datos.
- R-33. Desde la bandeja se puede elegir otra carpeta y trasladar el TOML sin dejar una copia antigua activa o residual; se verifica el destino antes de retirar el original. Los conflictos y errores se gestionan sin sobrescribir datos ajenos. Un editor GUI completo es una posibilidad futura.
- R-34. No hay CLI de usuario. El cambio rápido de tema se realiza desde la bandeja. Este acuerdo sustituye la solicitud previa de cambios desde terminal.
- R-35. La utilidad debe poder iniciarse automáticamente con la sesión de Windows y detenerse de forma sencilla.
- R-36. Binarios, proyectos y datos seguirán la política vigente del equipo: proyecto en D:\Desarrollo\Proyectos\CursorShake; binarios de una futura instalación en D:\Program Files\CursorShake; estado interno pequeño en AppData salvo decisión posterior justificada. No se instala nada durante la fase de especificación.

## 3. Modelo de dominio — en discusión

El punto de partida es definir qué es un cursor y qué comportamientos y datos pertenecen a ese concepto, antes de decidir clases o patrones.

La propuesta inicial del usuario, anterior al contraste de roles estándar, era:

- Un cursor como concepto central.
- Interacciones de sacudida y clic con tres botones.
- Hover como interacción contextual.
- Un contexto observable descrito mediante un conjunto finito de valores.
- Una transformación de contexto e interacción actual, excluyendo shake, para determinar el estado del cursor.
- Un tema con asociaciones estado → recurso y una asociación predeterminada para resolver ausencias.

El contraste posterior simplifica esa propuesta: para los roles estándar se parte del cursor que el sistema o la aplicación ya eligen, sin reconstruir semánticas como archivo/carpeta a partir del hover y los clics. Sigue pendiente cómo reconocerlo. Los botones afectan al crecimiento y no determinan por sí solos el rol visual. No se ha cerrado un modelo de clases, funciones, eventos o patrón Command. Debe distinguirse entre interacción, evento, condición y estado; el usuario diseñará estas fronteras.

### 3.1. Cuestiones que hay que contrastar

- El puntero visual, el dispositivo ratón y el cursor de inserción de texto no son necesariamente el mismo concepto.
- El movimiento y los cambios de botones se observan; la sacudida es una interpretación de una secuencia de movimientos.
- Hover puede entenderse como condición de estar sobre un objetivo, en vez de un evento continuo o un botón adicional. Falta fijar su significado en el dominio.
- La interacción mantenida requiere distinguir pulsación y liberación. Un clic completo no basta para representar un arrastre.
- Un contexto como «archivo» no determina por sí solo la apariencia del cursor. Las aplicaciones pueden elegir el cursor; una flecha puede estar sobre un archivo, un fondo o un botón.
- El estado «ocupado» puede cambiar sin movimiento ni clic. El contexto debe actualizarse también cuando cambia lo que solicita la aplicación.
- Windows proporciona cursores estándar y permite a las aplicaciones usar cursores propios. No se presupone que ofrezca un enum universal con la semántica del elemento situado bajo el puntero.

### 3.2. Catálogo candidato de funciones contextuales

Lista para discutir, **no un enum aprobado**. Se distingue función contextual de tipo de elemento («archivo», «carpeta», etc.).

| Función | Ejemplo |
| --- | --- |
| Selección normal | Flecha ordinaria |
| Ayuda | Selección con indicación de ayuda |
| Enlace | Mano sobre elemento activable |
| Selección de texto | Puntero de inserción/selección; distinto del caret del editor |
| Precisión | Cruceta |
| Escritura manual | Lápiz |
| Ocupado | Espera |
| Trabajo en segundo plano | Flecha con indicador de actividad |
| No disponible | Operación no permitida |
| Movimiento | Desplazar un elemento |
| Redimensionamiento horizontal | Borde izquierdo/derecho |
| Redimensionamiento vertical | Borde superior/inferior |
| Redimensionamiento diagonal NO-SE | Una pareja de esquinas |
| Redimensionamiento diagonal NE-SO | La otra pareja de esquinas |
| Selección alternativa | Puntero alternativo |
| Selección de ubicación | Ubicación, si el tema/sistema la proporciona |
| Selección de persona | Persona, si el tema/sistema la proporciona |
| Desconocido o propio de aplicación | Función sin correspondencia reconocida; política pendiente |

Los paquetes también pueden contener representaciones extra, como agarrar/arrastrar o zoom. Su inclusión en el catálogo inicial depende del inventario final y de la información que pueda observarse de forma fiable. No se asume que todos los temas incluyan todas las funciones.

## 4. Pendientes antes de implementar

1. Definición compartida de cursor, interacción, contexto y estado; modelo de dominio acordado por el usuario.
2. Catálogo inicial y reglas de transformación; tratamiento de estados desconocidos o cursores propios de aplicaciones.
3. Definición precisa de velocidad, recorrido y cambios de dirección: unidades, ventanas de medida y relación con DPI.
4. Valores iniciales de sensibilidad y aumento máximo; límites válidos y qué ocurre ante valores inválidos.
5. Reanudación durante reducción y relación entre contador, pausa, clic y cambio de tema.
6. Representación de recursos estáticos/animados, hotspot, formatos y política ante rutas ilegibles o archivos inválidos.
7. Persistencia y traslado seguro de configuración; los cambios sin reinicio y la conservación de la última configuración válida ante errores de TOML están aceptados. Falta concretar los mecanismos técnicos.
8. Comportamiento con múltiples monitores, DPI diferentes, bloqueo, suspensión, escritorio seguro y fallo del proceso.
9. Restauración del cursor, convivencia con otras utilidades y criterios medibles de fiabilidad, CPU/memoria y fluidez.
10. Arquitectura, interfaz mínima de bandeja/diálogos, esquema TOML y pruebas de aceptación. Un editor GUI completo no es requisito de la primera versión.

Estos puntos se resolverán por partes. No es necesario convertirlos ahora en una batería de preguntas.

## 5. Fuentes para contrastar la integración y los temas

- Microsoft, cursores de Win32: https://learn.microsoft.com/es-es/windows/win32/menurc/about-cursors
- Microsoft, selección del cursor por una aplicación: https://learn.microsoft.com/en-us/windows/win32/learnwin32/setting-the-cursor-image
- Bibata, fuentes y configuración de temas: https://github.com/ful1e5/Bibata_Cursor
- Nordzy, fuentes y temas: https://github.com/guillaumeboehm/Nordzy-cursors

Las restricciones de una API informan sobre viabilidad; no sustituyen las definiciones del dominio que se acuerden en esta especificación.

## 6. Catálogo contrastado de cursores

Se ha documentado la [equivalencia de los recursos de Windows con Bibata Ice y Nordzy blanco](EquivalenciasCursores.md), mediante el catálogo de Microsoft y los archivos/asociaciones reales de los paquetes. Sirve para sustituir las equivalencias meramente candidatas de la sección 3.2 por referencias verificadas, sin cerrar todavía las clases o el conjunto definitivo del dominio.

La distinción pendiente ya no es inventar equivalentes de las funciones estándar: es decidir cómo reconocer la función solicitada por las aplicaciones y qué alcance dar a las representaciones adicionales de los temas. No se ha implementado nada.

## 7. Experiencia de usuario y distribución

El recorrido aceptado, el requisito de rutas configurables, las condiciones de integración de temas y la propuesta de icono se recogen en [ExperienciaUsuario.md](ExperienciaUsuario.md). El uso de AppData sigue siendo un valor predeterminado permitido, no una ubicación obligatoria. El lenguaje elegido es C#; la representación gráfica permanece abierta.


## 8. Registro de decisiones y siguiente fase

Los acuerdos formales y valores iniciales están consolidados en [Decisiones.md](Decisiones.md). Las cuestiones todavía abiertas y un orden propuesto de trabajo están en [PendientesDiseno.md](PendientesDiseno.md). No se inicia implementación ni se adoptan alternativas pendientes sin autorización del usuario.
