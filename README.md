# CursorShake

Make your mouse pointer grow when you shake the mouse. CursorShake is a Windows tray utility with adjustable sensitivity, smooth scaling, and static or animated cursor themes.

**Availability:** version 1.0.0 is being prepared. The first GitHub release will include an installer; no downloadable release or installer is available yet. You can currently build and run the application from source.

## Features

- Adjustable shake count, movement thresholds, timing, and maximum size multiplier.
- Smooth growth and return; clicking starts the return immediately.
- Permanent Windows/Aero-style, Bibata Ice, and Nordzy white themes that follow the current Windows cursor role.
- Static `.cur` and animated `.ani` cursors, plus vector `.vcur` and `.vani` containers.
- Hotspot alignment and animation phase preserved during scaling.
- A tray menu that follows the device's light/dark theme and respects high contrast.
- TOML configuration that reloads when saved and can move together with personal themes.
- Optional startup with Windows, controlled from the tray menu.

## Requirements

Windows 11 x64 in a local interactive session. Published packages include the .NET runtime; the SDK is only needed to build from source. The current tray interface is in Spanish.

The Windows theme reads local system SVGs and checks their hashes against the verified references. Missing or different resources produce a diagnostic. Bibata Ice and Nordzy white are bundled and do not require those system SVGs.

## Build and run

Clone this repository and install the .NET SDK selected by `global.json`. In PowerShell, from the repository root:

```powershell
./build/Build.ps1 -Task Publish
```

This prepares the bundled themes, restores locked dependencies, builds, runs the automated tests, and creates `artifacts/publish/win-x64`.

Run `CursorShake.exe` from that folder. Keep its DLLs, `CursorShake.RecoveryGuard.exe`, `themes`, and `notices` together. Starting the app is silent and does not enable startup registration automatically.

## Use CursorShake

Shake the mouse to grow the pointer. Continue shaking to keep it growing; pause or click to let it return to normal size.

Right-click the tray icon for these actions. If the icon is hidden, open the Windows tray overflow.

| Menu item | Action |
| --- | --- |
| Abrir configuración | Open the commented TOML configuration in your associated editor. |
| Ubicación de configuración… | Choose a folder and move the configuration and personal themes together. |
| Abrir carpeta de configuración | Open the active configuration folder. |
| Tema | Choose a bundled or declared personal theme. |
| Probar crecimiento | Preview growth while the pointer is idle and its buttons are released. |
| Pausar crecimiento | Pause the enlargement effect while keeping the selected theme. |
| Iniciar con Windows | Enable or disable startup for your Windows user. |
| Ver errores… | Review diagnostics and available recovery actions. |
| Salir | Close the utility and restore cursors whose ownership can be verified. |

There are no keyboard shortcuts or user CLI. Hidden or application-specific cursors suspend the effect. If another tool changes the system cursors, CursorShake yields control.

## Configuration and personal themes

The app generates a commented `config.toml` on first use. Open it through the tray menu to find its current location. Saving valid changes reloads them automatically; the theme and gesture settings apply together when the pointer returns to normal size.

Personal themes live under `themes/<id>` beside that file. Associate supported cursor files with Windows roles in the TOML. Moving the configuration through the tray menu also moves personal themes; bundled themes stay with the application files.

Raster CUR/ANI files retain their source detail when enlarged. VCUR/VANI resources contain editable SVGs and are rasterized at the requested size. The physical output limit is 2048 pixels per side; large animated cursors can reduce the frame rate.

Invalid configuration or missing personal resources produce a diagnostic and preserve the last applicable configuration. The app does not regenerate deleted personal assets.

## Updating a source-built copy

Exit CursorShake before replacing its application files. Keep the configuration folder and personal themes. If you remove the application, disable **Iniciar con Windows** first. The installer will provide the installation, update, and uninstall workflow when the first release is ready.

## Development and specification

Run the regular automated checks with:

```powershell
./build/Build.ps1 -Task Test
```

The version is defined centrally in `Directory.Build.props`. Native smoke modes and projects under `experiments/` are separate test tools; some temporarily replace system cursors and must run in isolation.

The detailed specification is in Spanish:

- [Default settings](docs/ConfiguracionInicial.md): gesture and growth rules, defaults, and calibration.
- [User experience](docs/ExperienciaUsuario.md): tray menu, errors, folders, and distribution.
- [Cursor mappings](docs/EquivalenciasCursores.md): Windows roles and theme provenance.
- [Cursor formats](docs/FormatosCursores.md): native/vector formats, hotspots, and animation.
- [Architecture](docs/Arquitectura.md): implementation contracts, recovery, validation evidence, and its limits.

## License and credits

Original code and documentation are licensed under [MIT](LICENSE). Third-party material retains its own license.

- [Bibata](https://github.com/ful1e5/Bibata_Cursor), by Abdulkaiz Khatri (ful1e5), retains its original GPL terms.
- [Nordzy](https://github.com/guillaumeboehm/Nordzy-cursors), by Guillaume Boehm and the project contributors, retains its original GPL terms.

Editable theme sources, credits, and licenses are under `assets/themes`; published vector containers include their SVG sources. Dependency and runtime notices accompany the publication. Windows references are read locally: proprietary Windows cursor images and binaries are not distributed. The Wait/Working Aero-style animations are an original recreation. Icon provenance is recorded in [assets/branding](assets/branding/GENERACION.md).
