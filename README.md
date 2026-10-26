# CursorShake

Make your mouse pointer grow when you shake the mouse. CursorShake is a Windows tray utility with smooth scaling, adjustable sensitivity, and **vector cursor themes that are rendered at the enlarged size**, including animated cursors.

[Download CursorShake 1.1.0 for Windows 11 x64](https://github.com/R3Neer/CursorShake/releases/download/v1.1.0/CursorShake-1.1.0-Setup.exe) · [Release notes and SHA-256](https://github.com/R3Neer/CursorShake/releases/tag/v1.1.0)

The current **1.1.1 source** fixes a timing error that could interrupt growth or return when input was delayed. The latest published installer is still 1.1.0.

## Features

- Adjustable shake count, movement thresholds, timing, and maximum size multiplier.
- Smooth growth and return; clicking starts the return immediately.
- Permanent Windows/Aero-style, Bibata Ice, and Nordzy white themes that follow the current Windows cursor role.
- **Static and animated vector cursors:** `.vcur` and `.vani` keep their SVG sources and render them at the requested size. Raster `.cur` and `.ani` files also work.
- Hotspot alignment and animation phase preserved during scaling.
- A tray menu that follows the device's light/dark theme and respects high contrast.
- TOML configuration that reloads when saved and can move together with personal themes.
- Optional startup with Windows, controlled from the tray menu.

## Requirements

Windows 11 x64 in a local interactive session. Published packages include the .NET runtime; the SDK is only needed to build from source. The tray menu supports English (default) and Spanish.

The Windows theme reads local system SVGs and checks their hashes against the verified references. Missing or different resources produce a diagnostic. Bibata Ice and Nordzy white are bundled and do not require those system SVGs.

## Install

Download and open `CursorShake-1.1.0-Setup.exe` normally. The installer defaults to your Windows user; you can choose all users and a different installation folder. Windows requests administrator approval when needed. Leave **Iniciar con Windows** selected to start the utility after signing in, and **Ejecutar CursorShake** to launch it when setup finishes.

The installer is unsigned, so Windows may show an unknown-publisher or SmartScreen warning. No separate .NET installation is required. To update, run the new installer over the existing installation. To uninstall, use Windows Settings; configuration and personal themes are kept by default.

## Use CursorShake

Shake the mouse to grow the pointer. Continue shaking to keep it growing; pause or click to let it return to normal size.

Right-click the tray icon for these actions. If the icon is hidden, open the Windows tray overflow.

| Menu item (English / Spanish) | Action |
| --- | --- |
| Open configuration / Abrir configuración | Open the commented TOML in your associated editor. |
| Configuration location… / Ubicación de configuración… | Move the configuration and personal themes to a folder you choose. |
| Open configuration folder / Abrir carpeta de configuración | Open the active folder in Explorer. |
| Theme / Tema | Choose a bundled or personal theme. |
| Preview growth / Probar crecimiento | Preview the effect while the pointer is idle and its buttons are released. |
| Pause growth / Pausar crecimiento | Pause enlargement while keeping the selected theme. |
| Start with Windows / Iniciar con Windows | Enable or disable startup for your account. |
| View errors… / Ver errores… | Review diagnostics and recovery actions. |
| Exit / Salir | Close the utility and restore cursors whose ownership can be verified. |

There are no keyboard shortcuts or user CLI. Hidden or application-specific cursors suspend the effect. If another tool changes the system cursors, CursorShake yields control.

## Vector cursors and your own themes

CursorShake keeps the SVG geometry in `.vcur` (static) and `.vani` (animated) files and rasterizes it at the size being displayed. Enlarging therefore does not stretch a small source bitmap. Windows receives the resulting native cursor; the hotspot stays aligned and animation timing is preserved while the size changes.

The bundled themes are ready to use from **Theme**. To add your own:

1. Open the configuration folder from the tray menu.
2. Put your `.vcur`, `.vani`, `.cur`, or `.ani` files under `themes/my-theme` beside `config.toml`.
3. Edit the existing appearance section and add a theme definition:

```toml
[appearance]
theme = "my-theme"

[themes.my-theme]
default = "pointer.vcur"
```

This example uses one default cursor for every role. The guide below explains individual roles and animated cursors. Saving applies the theme automatically at normal cursor size.

**Starting with an SVG?** Follow the [worked guide to creating static and animated vector cursors](docs/CustomCursors.md): it covers the SVG, hotspot, ZIP packaging, and frame order. Loose SVG/PNG files cannot be assigned directly. VCUR/VANI are CursorShake formats, rather than native Windows cursor files.

Raster CUR/ANI sources do not gain detail when enlarged. Output is limited to 2048 pixels per side, and large animations can reduce the frame rate. Missing personal assets or invalid configuration preserve the last applicable settings and produce a diagnostic.

## Configuration and menu language

The app generates a commented `config.toml` on first use. **Open configuration** shows it in your associated editor. Changes reload when saved; themes and gesture settings apply together at normal cursor size. Moving the configuration through the tray menu also moves personal themes.

Choose the tray-menu language at the **top of the file, before any `[section]`**:

```toml
language = "english" # english or spanish
```

English is the default, including when the setting is omitted. Use `language = "spanish"` for Spanish. Unsupported values are rejected. This setting covers the tray menus, status text and tooltip; it is not a translation of every technical diagnostic.

## Build and development

```powershell
./build/Build.ps1 -Task Publish
```

Requires Windows and the .NET SDK selected by `global.json`. This builds, tests and publishes the self-contained app to `artifacts/publish/win-x64`. Run `CursorShake.exe` there and keep its companion files together. Starting a source-built copy does not enable startup automatically.

Run tests separately with `./build/Build.ps1 -Task Test`. See the [build, installer and native-test instructions](docs/Development.md) for packaging and checks that require exclusive access to the system cursors.

## License and credits

Original code and documentation are licensed under [MIT](LICENSE). Third-party material retains its own license.

- [Bibata](https://github.com/ful1e5/Bibata_Cursor), by Abdulkaiz Khatri (ful1e5), retains its original GPL terms.
- [Nordzy](https://github.com/guillaumeboehm/Nordzy-cursors), by Guillaume Boehm and the project contributors, retains its original GPL terms.

Editable theme sources, credits, and licenses are under `assets/themes`; published vector containers include their SVG sources. Dependency and runtime notices accompany the publication. Windows references are read locally: proprietary Windows cursor images and binaries are not distributed. The Wait/Working Aero-style animations are an original recreation. Icon provenance is recorded in [assets/branding](assets/branding/GENERACION.md).
