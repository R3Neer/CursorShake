# Pendientes de diseño antes de implementar

Actualizado: 21-oct-2026. Este documento contiene preguntas y alternativas, no decisiones aprobadas. Los acuerdos vigentes están en [Decisiones.md](Decisiones.md).

## Orden propuesto de trabajo

1. Modelo de dominio: datos y reglas de gesto, evolución del tamaño, rol y tema.
2. Semántica temporal y numérica del comportamiento.
3. Recursos gráficos e integración con Windows, contrastando la viabilidad de los requisitos.
4. Contrato TOML y ciclo de vida de la aplicación.
5. Arquitectura y estrategia de validación de las decisiones anteriores.

El usuario decide cómo avanzar y propone primero sus modelos. No se inicia código ni una prueba técnica hasta que lo autorice.

## Preguntas por resolver

| ID | Cuestión | Por qué importa |
| --- | --- | --- |
| P-01 | ¿Qué datos conserva el detector, qué recibe por actualización y qué resultado produce? | Distinguir movimientos, sacudidas reconocidas y frecuencia sin acoplarlos prematuramente al multiplicador de tamaño. |
| P-02 | ¿Cómo se miden velocidad, recorrido y dirección, con qué unidades y tratamiento de diagonales/DPI? | Evitar que sensibilidad y comportamiento dependan accidentalmente del monitor o de la frecuencia de entrada. |
| P-03 | ¿Cómo se relacionan el intervalo de detección de 100 ms y la pausa de reducción de 500 ms? | Son temporizadores distintos. Hay que definir qué sucesos renuevan cada uno y comprobar que 100 ms encaja con la intención de un gesto cómodo. No se cambia el valor acordado por suposición. |
| P-04 | ¿Reanudar durante la reducción exige de nuevo las tres sacudidas? | Está acordado continuar desde el tamaño actual, pero no el umbral de reactivación. |
| P-05 | ¿Cuáles son la curva ease-in/ease-out, las duraciones, el crecimiento base, la frecuencia de referencia y su aceleración hasta ×1,5? | Evitar saltos al variar la frecuencia, retomar el gesto o pulsar un botón. No confundir la aceleración temporal con el máximo de tamaño. |
| P-06 | ¿Qué valor inicial y límites tiene el máximo proporcional? ¿Qué significa tamaño normal al cambiar DPI/monitor/configuración? | Garantizar una relación de tamaño definida y un rango de recursos asumible sin introducir un límite de píxeles no acordado. |
| P-07 | ¿Cómo reconocer roles estándar y cursores propios de una aplicación? ¿Cómo resolver los no reconocidos? | Los códigos IDC son referencias de recursos, no un enum que la consulta de cursor activo devuelva directamente. |
| P-08 | ¿Qué formatos y metadatos admitimos para imágenes, animaciones y hotspots? | Hay dos temas con ANI y se quiere nitidez a gran tamaño. Faltan formato interno y forma de producir los tamaños. |
| P-09 | ¿Cursor nativo, representación propia u otra estrategia? ¿Qué restricciones impone Windows? | Evaluar tamaño enorme, animación, fidelidad, recursos gráficos, compatibilidad y restauración antes de elegir arquitectura gráfica. |
| P-10 | ¿Windows base es el tema de Microsoft o el esquema que el usuario tenía seleccionado? | Evitar un fallback circular al recurso que nuestra aplicación acaba de sustituir y definir qué se restaura al salir. |
| P-11 | ¿Cómo se recuerda la ubicación del TOML y cómo se recupera un traslado interrumpido? ¿Qué hacer si ya hay un TOML en destino? | El registro como localizador y las opciones «usar existente/cancelar» son propuestas. Deben preservarse datos y referencias de recursos. |
| P-12 | ¿Qué rutas son relativas, respecto a qué base y cómo mantienen su significado al trasladar el TOML? | Elegir una carpeta nueva no debe romper los temas personalizados ni trasladar automáticamente archivos ajenos. |
| P-13 | ¿Cuál es el esquema TOML, su validación y política de compatibilidad entre versiones? | Un valor inválido no debe sobrescribir datos ni dejar una configuración parcialmente aplicada. |
| P-14 | ¿Cómo se responde al bloqueo, suspensión, cierre de Explorer, error de renderizado y fallo del proceso? | La presencia de un proceso o icono no prueba que el gesto funcione. Hay que especificar restauración y diagnóstico. |
| P-15 | ¿Qué .NET, biblioteca gráfica, componentes de bandeja/diálogos y modalidad de publicación elegimos? | C# está decidido; su stack aún no. Revisar soporte, licencias y distribución de las dependencias. |
| P-16 | ¿Cómo se reciben las entradas y avanza el tiempo? ¿Cómo se limita el trabajo visual? | Evaluar eventos/polling y frecuencia con medidas. No dibujar necesariamente por cada movimiento ni descartar cambios de dirección útiles. |
| P-17 | ¿Qué objetivos medibles de latencia, fluidez, CPU, memoria y estabilidad pedimos? | «Eficiente» necesita criterios. No se inventan presupuestos de rendimiento sin discutirlos y medir. |
| P-18 | ¿Qué instalador, arquitecturas de CPU, inicio automático y mecanismo de actualización usamos? | Deben conservarse rutas elegidas y preferencias, y probar instalación, actualización y desinstalación. |

No es necesario responder todas las preguntas en una sola sesión. No se tratarán como autorizaciones implícitas para decidirlas durante la programación.

## Verificaciones de comportamiento derivadas de requisitos

Ejemplos para completar cuando exista un modelo; no son tests implementados ni cubren todavía todos los casos.

- Un movimiento que no cumple recorrido/dirección/velocidad no activa el crecimiento (R-01–R-05).
- El contador se reinicia según el intervalo configurado, no según la pausa de reducción (R-05, R-11).
- El tamaño no excede el máximo proporcional y no baja de ×1 (R-08, R-17).
- Una pulsación inicia la reducción sin esperar la pausa y bloquea nuevas activaciones mientras haya un botón pulsado (R-14–R-16).
- Cambiar de rol conserva tamaño/fase, reinicia solo la animación del nuevo dibujo y mantiene el punto de clic fijo (R-24–R-28).
- Un rol sin recurso utiliza el predeterminado del tema; si tampoco existe, usa el de Windows para ese rol (R-22).
- Un traslado fallido no pierde el TOML anterior; uno completado no abandona una copia antigua. Un TOML inválido no se reemplaza como si faltara (R-32–R-33).

## Condición para pasar a implementación

Antes de programar un área, se documentan las decisiones que afectan a su comportamiento y se obtiene autorización del usuario para implementar esa área. No se exige cerrar hoy todos los detalles del producto; se exige no introducir decisiones formales únicamente en el chat o silenciosamente en el código.
