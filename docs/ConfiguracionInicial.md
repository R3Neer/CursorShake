# CursorShake — configuración inicial y reglas

Valores y reglas aceptados. Fuente normativa de defaults del gesto y crecimiento; implementación y pruebas automatizadas completadas para este hito. Límites gráficos en [Arquitectura](Arquitectura.md).

## 1. Valores iniciales

| Parámetro | Valor | Estado |
| --- | --- | --- |
| Sacudidas para activar | 3 inversiones válidas | Aceptado; configurable. |
| Intervalo máximo entre inversiones | **130 ms** | Aceptado; configurable y límite inclusivo. |
| Recorrido mínimo de un tramo | 16 píxeles lógicos, referencia 96 DPI | Aceptado; configurable. |
| Velocidad mínima de un tramo | 200 píxeles lógicos/s de media en el tramo | Aceptado; configurable. |
| Inversión de dirección | Ángulo de al menos 120 grados respecto al tramo anterior | Aceptado; sin otro ajuste de usuario. |
| Tamaño máximo | ×16 del tamaño normal | Aceptado; configurable. Ejemplo: 32→512 px; no promete el mismo coste a otros tamaños base/DPI. |
| Tiempo base hasta máximo | 1200 ms de avance de crecimiento desde ×1 | Aceptado; configurable. |
| Aceleración máxima por cadencia | ×1,5 | Aceptado; fórmula debajo. |
| Inactividad antes de reducir | 500 ms | Aceptado; configurable; semántica debajo. |
| Duración de retorno | 250 ms desde el tamaño alcanzado | Aceptado; configurable. |
| Curva de transición | smoothstep cúbico: 3u²−2u³, u entre 0 y 1 | Aceptado. |

Se mide desplazamiento del cursor normalizado, no cuentas físicas del sensor. Píxeles lógicos significa espacio de referencia a 96 DPI; la normalización entre monitores debe concretarse en el adaptador para que una mezcla de escalas no invente un gesto. No se convierte a milímetros ni se presume conocer el DPI físico del ratón. La velocidad usa duración y longitud del mismo tramo; no la velocidad instantánea de una muestra ni la frecuencia de polling del dispositivo.

Reconocimiento acordado: un recorrido inicial prepara el primer tramo; por sí solo no cuenta. Una inversión clara se valida tras completar un tramo opuesto con longitud/velocidad suficientes. Contar cada inversión una vez, incluidas diagonales; no contar cada muestra ni el simple cruce de velocidad por cero. Giros pequeños o trayectorias en círculo no deben convertirse en sacudidas por sumar ángulos sin un tramo de referencia estable. La regla geométrica exacta debe quedar comprobable con trazas antes de congelar el contrato.

Con 130 ms se exige una cadencia de al menos unas 7,7 inversiones por segundo durante la secuencia (aproximadamente 3,85 idas y vueltas). Este parámetro mide cadencia; velocidad mínima y recorrido mínimo son restricciones distintas.

## 2. Reconocimiento y transiciones

1. La inversión válida que completa la cuenta configurada (tres inicialmente) inicia crecimiento desde la escala actual. La cuenta previa se pierde si el intervalo supera el máximo configurado (130 ms inicialmente); una inversión al límite exacto pertenece a la secuencia.
2. Cada inversión válida mantiene actividad hasta el intervalo máximo configurado después de su instante (130 ms inicialmente). Al caducar esa ventana se congela el crecimiento. La reducción comienza al vencer la pausa de inactividad configurada desde la última inversión. La gracia de crecimiento está incluida en esa pausa, no se suma después. Con defaults: retorno a los 500 ms, incluidos 130 ms de gracia y 370 ms de mantenimiento. El validador exige que la pausa de inactividad sea al menos igual al intervalo máximo entre inversiones, sin convertir las cifras iniciales en constantes.
3. Durante crecimiento o mantenimiento, una nueva inversión válida mantiene o retoma el crecimiento sin repetir la cuenta completa. Durante reducción también basta una inversión válida mientras la escala aún supere ×1. Alcanzar ×1 rearma la cuenta configurada completa. Un clic siempre fuerza rearme completo y bloqueo hasta soltar todos los botones.
4. El avance de la curva continúa entre sacudidas; no se reinicia cada vez. Para cadencia, usar la media de los dos últimos intervalos válidos, si existen. Multiplicador: clamp(intervalo máximo configurado / intervalo medio, 1, 1,5); si no hay estimación se usa ×1. Cambia la velocidad de avance, no el límite ni el reloj de los fotogramas del dibujo. La curva se evalúa desde la escala actual al cambiar fase, sin saltos. Al retomar crecimiento, tiempo base proporcional a la distancia restante; retorno con duración configurada (250 ms inicialmente) desde el tamaño alcanzado. No se promete continuidad de velocidad al interrumpir fase.
5. Button-down prevalece sobre sacudida y plazos de crecimiento admitidos en el mismo instante: resetea y reduce. Salida/pausa o pérdida de sesión prevalecen sobre preview y gesto. Si coinciden una inversión y vencimiento de inactividad, se resuelve la inversión válida antes del plazo para evitar un retorno espurio. Admisión temporal según Arquitectura 3.1.
6. Validar/preparar inmediatamente y conservar un pendiente latest. Aplicar TODO el candidato (tema y perfil) al llegar a ×1 en un turno serial, reiniciando tramo/cuenta. Hasta entonces conservar configuración efectiva completa, sin mezclar revisiones. UI informa de pendiente; no botón Aplicar. Trasladar ubicación conserva su propia transacción de almacenamiento.
7. Preview solo en reposo y botones libres: un ciclo visual de crecimiento/retorno sin movimiento sintético. Durante gesto/retorno informar de indisponibilidad y no encolar. Durante preview ignorar activación por sacudidas; clic/pausa/salida cancelan. Al terminar resetear tramo/cuenta y exigir la cuenta configurada completa de inversiones nuevas.

Presupuesto aceptado en Arquitectura 4.3. ×16 sujeto a presupuesto, sin memoria ilimitada ni garantía de 60 Hz a cualquier DPI.

## 3. Evidencia de calibración

El 25-oct-2026 se completa una prueba guiada medio/lento/rápido/medio, seis segundos por tramo. [Análisis y metodología](../experiments/ShakeTimingProbe/README.md), [resultados y sensibilidad](../experiments/ShakeTimingProbe/guided-analysis.json). Valor aceptado: **130 ms**. El medidor fecha extremos retrospectivos y el reconocedor del producto usa confirmaciones; contrastarlos con trazas sin cambiar automáticamente el default.

Con un reanálisis espacial uniforme de 32 px, se admiten 121/123 intervalos medios (98,4 %), 80/82 rápidos (97,6 %) y 0/26 lentos. Medianas 98/216/67/89 ms. Se conserva todo intervalo temporal; se compara también el filtro original de 8 px y 16/48/64 px. El rápido contiene microinversiones que el auxiliar de 8 px cuenta; la comprobación a 32 px las elimina y no cambia los tramos medio/lento. No se adopta ese filtro como requisito de reconocimiento ni se atribuye una causa no comprobada.

El menor intervalo lento observado es 143 ms: 130 ms deja más margen que 140 ms, que también separa esta muestra. A 150 ms entra un intervalo lento aislado, insuficiente por sí solo para activar tres inversiones. El valor elegido procede de una calibración para este usuario; no es un óptimo universal ni una medición de falsos positivos en uso cotidiano. Las fórmulas y ventanas de las reglas anteriores usan el parámetro configurado, con 130 ms iniciales; no una constante antigua de 100 ms.

## 4. Esquema de configuración implementado

La plantilla comentada se genera desde los defaults tipados en ConfigurationDefaults; no se mantiene otra plantilla con cifras duplicadas. La versión del TOML de aplicación es 1 (omitirla conserva compatibilidad con ese esquema). Se rechazan campos desconocidos y documentos de más de 1 MiB.

| Sección | Claves |
| --- | --- |
| appearance | theme |
| gesture | count, maximum_interval_ms, minimum_travel, minimum_speed |
| growth | maximum_scale, growth_duration_ms, inactivity_delay_ms, return_duration_ms, maximum_cadence_multiplier |
| themes.<id> | default y tabla roles |

count admite enteros de 1 a 100. Los tiempos configurables se expresan en milisegundos, entre 0,001 ms y una hora. Recorrido positivo; velocidad mínima no negativa; escala y multiplicador de cadencia al menos 1; todos finitos. El máximo efectivo también debe caber en 2048 px para cada monitor conectado, sin recortar silenciosamente lo configurado. El ángulo de inversión conserva el valor fijo acordado.

IDs personales: entre 1 y 64 letras/dígitos/guion/guion bajo, comenzando por letra o dígito. Las rutas son relativas a themes/<id> y usan CUR/ANI/VCUR/VANI. La bandeja modifica el tema conservando comentarios; sintaxis TOML válida que esa edición no pueda preservar se rechaza con error, permitiendo editarla directamente.

El replay del reconocedor implementado contrasta las cuatro trazas guiadas con confirmaciones, no extremos retrospectivos. Con supuestos de DPI 96/120/144/192, los dos medios y el rápido activan y el lento no. El DPI original no fue registrado: es sensibilidad frente a esos supuestos, no certificación del monitor ni de falsos positivos cotidianos. Fixtures relativos bajo tests/Fixtures/Calibration; informe reproducible en artifacts/tests/calibration-replay.json. No se han cambiado los defaults para obtener ese resultado.

## Idioma de la bandeja (1.1.0)

`language` es una clave raíz del TOML, anterior a las tablas. Admite exactamente `"english"` y `"spanish"`; su valor por defecto y al omitirla es `"english"`. Otros valores o tipos son configuración inválida y conservan la última revisión efectiva. El idioma forma parte de esa revisión, aplicada a tamaño normal junto al resto de configuración. Cubre menús, submenús, estado, tooltip, selector de carpeta y mensajes de error orientados al usuario. Los detalles técnicos conservan el texto de sus excepciones originales. La versión del esquema sigue siendo 1.
## Recuperación de recargas del esquema

Reglas internas, sin nuevas opciones TOML: cuando otra utilidad sustituye los cursores, pausar publicaciones y observar como máximo una vez por segundo, incluso si llegan otros eventos. Esperar sin límite temporal mientras los ajustes de Windows sigan idénticos y el Guard original permanezca vivo; no mostrar popup por la mera duración de esa espera. Cuando vuelvan los 17 roles originales, abrir una ventana máxima de un segundo para confirmar la normalización y verificar la readopción. Si vuelven cursores ajenos durante esa ventana, regresar a la espera. Hasta tres readopciones en una ventana móvil de 60 segundos, contadas al confirmarse cada una; consultar capacidad no consume presupuesto. Cambios de ajustes, Guard no disponible, comprobaciones inconcluyentes, fallo/cancelación de normalización o agotamiento del plazo/presupuesto mantienen la cesión definitiva hasta reiniciar, con un único aviso. El éxito reaplica el último tema válido a ×1 sin popup y permite activar un gesto nuevo. Reaplicar externamente el mismo esquema y los mismos ajustes resulta indistinguible de una recarga del sistema y también puede activar esta recuperación.
