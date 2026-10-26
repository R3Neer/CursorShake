# CursorShake — formatos de cursor

Especificación acordada; lector implementado y verificado mediante pruebas automatizadas. Aquí se define cada recurso; config.toml de la aplicación asocia recursos a roles. Presentación nativa en [Arquitectura](Arquitectura.md).

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

El lector exige version=1 y rechaza campos desconocidos. En VCUR son obligatorios image y hotspot; en VANI cada bloque exige image y duration_ms, con hotspot propio o heredado del encabezado. TOML se decodifica como UTF-8 estricto, con BOM opcional.

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

El hotspot de cada frame se alinea con la misma coordenada de pantalla al cambiar tamaño o fotograma. CUR/ANI conservan metadatos raster y no ganan detalle al ampliar. El viewBox debe tener cuatro números finitos, coordenadas de magnitud máxima 1.000.000 y dimensiones positivas. El hotspot debe quedar dentro del lienzo, con límite superior exclusivo. Al rasterizar se redondea alejándose de cero en las mitades y se limita al último píxel válido. ANI con frames ICO usa el centro como hotspot; ANI de bitmaps raw sin contenedor CUR/ICO se rechaza explícitamente. No adivinar un hotspot ausente obligatorio de VCUR/VANI.

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

## 5. Validación y límites de admisión

Límites técnicos fijados al implementar el lector; son distintos del presupuesto de caché raster:

| Recurso | Límite |
| --- | --- |
| Archivo CUR/ANI/VCUR/VANI | 16 MiB |
| SVG individual | 4 MiB |
| Contenido ZIP descomprimido | 32 MiB |
| Manifiesto cursor.toml | 64 KiB |
| Entradas ZIP | 257, incluido manifiesto |
| Frames/pasos por cursor | 256 |
| Fuente/modelo retenido estimado | 64 MiB entre baseline, activo y candidato/compensación |
| Dimensión física de salida | 2048 px por lado |

Las duraciones VANI deben ser positivas, representables por el reloj de .NET y de hasta un día por paso. ANI conserva los jiffies de 1/60s al convertir a ticks, sin redondear primero a milisegundos enteros. CUR selecciona la variante de mayor resolución disponible. No truncar pasos ni degradar calidad al exceder cuotas: se informa error y se conserva el tema efectivo.

Las rutas ZIP no admiten escapes, duplicados ambiguos ni enlaces. Los recursos personales se leen dentro de themes/<identidad>, con todos los ancestros y el archivo protegidos durante la lectura; se rechazan reparse points y hardlinks. La excepción interna de solo lectura para referencias conocidas Windows/Cursors permite hardlinks legítimos de WinSxS y verifica el hash del recurso.

SVG no admite scripts, eventos, imágenes externas, animaciones, DTD ni referencias exteriores; sí defs y filtros internos necesarios para los temas. El presupuesto vectorial combina bytes fuente y estimación de Skia; no garantiza la memoria total de bibliotecas, filtros temporales, proceso o DWM. La caché limita píxeles retenidos y recursos nativos estimables según Arquitectura.

Decodificar no decide el rol. Asociación ausente puede usar fallback; recurso asociado ilegible es error. Fuentes editables y metadatos de construcción de los temas incorporados están en assets/themes.

## Fuentes

- [Microsoft: formatos de recursos](https://learn.microsoft.com/en-us/windows/win32/menurc/resource-file-formats).
- [CUR en ImageSharp](https://docs.sixlabors.com/articles/imagesharp/cur.html).
- [ANI en ImageSharp](https://docs.sixlabors.com/articles/imagesharp/ani.html).

Estas referencias explican formatos nativos; no implican elegir ImageSharp. VCUR/VANI son formatos propios del proyecto.
