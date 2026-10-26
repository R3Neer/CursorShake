# Create your own cursor theme

This guide uses CursorShake's vector formats. A VCUR file is a ZIP containing a static SVG and `cursor.toml`; VANI uses SVG frames plus their timing. Packaging preserves the geometry rather than converting it into a fixed-size bitmap. The [format specification](FormatosCursores.md) defines supported SVG features and resource limits.

## A static vector cursor

Open the configuration folder from CursorShake's tray menu. Create `themes/my-theme` there, then put these two files inside it.

**`pointer.svg`** — a simple arrow. Its hotspot will be the tip at `(2, 2)` in the 32 × 32 viewBox:

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">
  <path d="M2 2 L2 26 L8 20 L13 30 L18 27 L13 18 L23 18 Z"
        fill="#ffffff" stroke="#111111" stroke-width="1.5"
        stroke-linejoin="round"/>
</svg>
```

**`cursor.toml`** — metadata for the resource, separate from the app's `config.toml`:

```toml
version = 1
image = "pointer.svg"
hotspot = [2.0, 2.0]
```

The hotspot is the point that Windows treats as the pointer's actual click position. Its coordinates use the SVG viewBox, so it scales together with the drawing. It need not be the upper-left corner.

Open PowerShell in `themes/my-theme` and run:

```powershell
Compress-Archive -LiteralPath @('cursor.toml', 'pointer.svg') -DestinationPath pointer.zip
Rename-Item -LiteralPath pointer.zip -NewName pointer.vcur
```

The ZIP root must contain `cursor.toml` and `pointer.svg` directly, without an enclosing folder. Windows Explorer may hide extensions; the final filename must be `pointer.vcur`, rather than `pointer.vcur.zip`.

Now edit the existing appearance section of the app's `config.toml`, and add the theme definition:

```toml
[appearance]
theme = "my-theme"

[themes.my-theme]
default = "pointer.vcur"
```

Save the file. The theme applies when the pointer returns to normal size. This one-cursor example uses the arrow for every role; it is useful to get started, but you will usually want separate text, resize and busy cursors.

## Roles and fallback

Add only the roles for which you have a specific resource:

```toml
[themes.my-theme.roles]
arrow = "pointer.vcur"
text = "text.vcur"
wait = "busy.vani"
working = "working.vani"
```

Each named file must exist under `themes/my-theme`; remove entries for files you have not created. Paths are relative to that theme's folder. CUR/ANI files can be mixed with VCUR/VANI files.

A missing role uses the theme's `default`. If that is also omitted, it uses the Windows fallback. A resource explicitly assigned but missing or invalid is an error; it does not silently trigger fallback. The complete role list is in [cursor mappings](EquivalenciasCursores.md).

## An animated vector cursor

Prepare static SVG frames, such as `frame-a.svg` and `frame-b.svg`, with compatible viewBoxes. Give the animation its own staging folder and this `cursor.toml`:

```toml
version = 1
hotspot = [16.0, 16.0]

[[frames]]
image = "frame-a.svg"
duration_ms = 80

[[frames]]
image = "frame-b.svg"
duration_ms = 120
```

This hotspot assumes 32 × 32 viewBoxes. Frame blocks play in the order written: A for 80 ms, then B for 120 ms, then repeat. Filenames do not determine the sequence. A frame can override the common hotspot with its own `hotspot` field.

From that staging folder:

```powershell
Compress-Archive -LiteralPath @('cursor.toml', 'frame-a.svg', 'frame-b.svg') -DestinationPath busy.zip
Rename-Item -LiteralPath busy.zip -NewName busy.vani
```

Copy `busy.vani` into `themes/my-theme` and assign it to `wait`, as above. VANI is a sequence of static SVGs: it does not import arbitrary animated SVG/SMIL. Its frame timing remains independent of the cursor's growth animation.

## SVG preparation

Keep SVG files self-contained. External resources, scripts and event handlers are rejected. Export text as paths if the design depends on a particular font. Keep the viewBox and hotspot deliberate, and test at both normal and enlarged sizes. Rendering at a larger size preserves geometric detail, but the final output is still subject to CursorShake's pixel and memory limits.
