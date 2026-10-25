# CursorShake — formatos de cursor

Especificación acordada, sin implementar. Aquí se define cada recurso; config.toml de la aplicación asocia recursos a roles. Presentación nativa en [Arquitectura](Arquitectura.md).

## 1. Formatos admitidos

| Extensión | Dibujo | Metadatos |
| --- | --- | --- |
| .cur | Raster, con posibles variantes de resolución. | Hotspot por imagen. |
| .ani | Secuencia raster en contenedor RIFF. | Secuencia/tiempos y hotspots de recursos CUR embebidos. No asumir hotspot explícito si usa otro tipo de frame. |
| .vcur | Un SVG estático autocontenido. | ZIP con cursor.toml, versión y hotspot. |
| .vani | Fotogramas SVG estáticos. | ZIP con cursor.toml, versión, orden, tiempos y hotspots. |

Sin importación directa inicial de PNG/SVG sueltos, SMIL, GIF/APNG/WebP animado ni programas externos. Empaquetar SVG no rasteriza su geometría. Windows no entiende VCUR/VANI: el motor rasteriza al tamaño final y entrega recursos nativos.

## 2. Contenedores vectoriales

cursor.toml está en la raíz del ZIP y referencia SVG internos. version=1 identifica el esquema del recurso, no el programa ni el tema. Convertir SVG estático a VCUR consiste en declarar hotspot y empaquetar, sin generar CUR primero.

Los siguientes ejemplos expresan la estructura acordada; esquema completo, obligatoriedad y errores se congelarán antes del lector.

```text
pointer.vcur (ZIP)
├── cursor.toml
└── pointer.svg
```

```toml
version = 1
image = "pointer.svg"
hotspot = [4.0, 2.0]
```

## 3. Hotspot

Coordenadas del viewBox SVG, con decimales, referidas al lienzo completo. No se presupone esquina superior izquierda ni se mide el rectángulo del contenido visible. Para viewBox="0 0 64 64", hotspot=[4,2] corresponde a (16,8) al renderizar a 256×256. Con origen distinto de cero se resta dicho origen. Dibujo y hotspot comparten transformación, incluidos márgenes de conservación de aspecto.

El hotspot de cada frame se alinea con la misma coordenada de pantalla al cambiar tamaño o fotograma. CUR/ANI conservan metadatos raster y no ganan detalle al ampliar. Falta concretar validación de viewBox/dimensiones, coordenadas válidas, redondeo y tratamiento de ANI sin hotspot explícito. No adivinar un hotspot ausente obligatorio de VCUR/VANI.

## 4. Secuencia VANI

Orden de bloques [[frames]] = orden de reproducción, independientemente de nombres. Tras el último se vuelve al primero, en bucle. Cada paso tiene duración positiva en milisegundos. Una imagen puede repetirse por referencia sin duplicar SVG. Hotspot común y sobrescritura por frame:

```toml
version = 1
hotspot = [32.0, 32.0]

[[frames]]
image = "a.svg"
duration_ms = 50

[[frames]]
image = "b.svg"
duration_ms = 100
hotspot = [31.0, 32.0]

[[frames]]
image = "a.svg"
duration_ms = 50
```

A durante50ms → B durante100ms → A durante50ms → repetir. El hotspot común usa coordenadas del viewBox de cada frame; lienzos distintos requieren comprobación al preparar el recurso. No tabla de índices adicional ni conversión automática de animaciones SVG arbitrarias.

Los cuatro formatos se traducen a una representación común de fuentes, hotspots y pasos con duración. Las referencias repetidas pueden compartir fuente; no exige precalcular bitmaps. Conservar tiempos ANI en su unidad original al convertir, sin redondearlos primero a milisegundos enteros. Escala y reloj de frames son independientes según Arquitectura.

## 5. Validación y límites por concretar

Aceptar los formatos no ratifica cifras de cuotas. Antes del lector se fijarán bytes comprimidos/descomprimidos, entradas, tamaño SVG, frames/pasos, complejidad retenida y memoria temporal. Candidatos estudiados, no acuerdos:4MiB/SVG,256 pasos/cursor,64MiB de fuentes/modelos retenidos entre baseline/activo/candidato/compensación. Estos últimos son distintos de la caché raster aceptada de64MiB.

Referencias internas verificadas sin escapar del ZIP; recursos personales dentro de themes/<identidad> del workspace. Sin enlaces/reparse ni referencias SVG exteriores; preservar defs/filtros internos requeridos por los temas. Rechazar inválido, versión/formato no admitido o límite excedido con diagnóstico, antes de grandes asignaciones. No truncar pasos ni degradar calidad silenciosamente, ni modificar originales. Memoria de bibliotecas/DWM no queda acotada por estas cuotas.

Decodificar no decide el rol. Asociación ausente puede usar fallback; recurso asociado ilegible es error. Validación final de paquetes y metadatos antes de distribuir.

## Fuentes

- [Microsoft: formatos de recursos](https://learn.microsoft.com/en-us/windows/win32/menurc/resource-file-formats).
- [CUR en ImageSharp](https://docs.sixlabors.com/articles/imagesharp/cur.html).
- [ANI en ImageSharp](https://docs.sixlabors.com/articles/imagesharp/ani.html).

Estas referencias explican formatos nativos; no implican elegir ImageSharp. VCUR/VANI son formatos propios del proyecto.
