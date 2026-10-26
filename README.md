# CursorShake

A Windows tray utility that makes your cursor grow when you shake the mouse, with adjustable sensitivity, smooth scaling, and cursor themes.

**Status: development build.** The self-contained application is implemented and can be built from source. Automated and native checks are documented below; there is no public release or installer yet.

## Features

- Adjustable shake count, movement thresholds, timing, and maximum size multiplier.
- Smooth growth and return; clicking starts the return immediately.
- Permanent Windows/Aero-style, Bibata Ice, and Nordzy white themes that follow the current Windows cursor role.
- Static and animated cursors, with hotspot alignment and animation phase preserved during scaling.
- Personal `.cur` and `.ani` resources, plus vector `.vcur` and `.vani` containers.
- A tray menu for themes, preview, pause, configuration, and optional startup with Windows.
- TOML configuration that reloads when saved and can move together with personal themes.

The initial target is a local Windows 11 x64 session. Hidden or application-specific cursors suspend the effect. Changes made by other cursor tools cause CursorShake to yield control. Visual quality, perceived sensitivity, and startup timing after an actual sign-in require separate checks.

The Windows theme uses local system SVGs with verified hashes; missing or different resources produce a diagnostic. Bibata Ice and Nordzy white sources are bundled and do not require those system SVGs.

## Build and run

On Windows, install the .NET SDK selected by `global.json`, then run PowerShell from the repository root:

```powershell
./build/Build.ps1 -Task Publish
```

This prepares the bundled themes, restores locked dependencies, builds, runs the automated tests, and publishes to `artifacts/publish/win-x64`. Run `CursorShake.exe` from that folder. Keep its DLLs, `CursorShake.RecoveryGuard.exe`, themes, and notices together. The published application includes its .NET runtime.

Right-click the tray icon to open the configuration, choose a theme, preview growth, or pause. If the icon is hidden, open the Windows tray overflow. Saving a valid configuration applies it automatically when the cursor returns to normal size. There is no user CLI. The installer is a later step; running the application does not enable startup registration automatically.

`./build/Build.ps1 -Task Test` runs the regular automated checks. Projects under `experiments/` and the explicit native smoke modes are test tools: some temporarily replace system cursors and must run separately from other cursor tests.

## Documentation

The specification is in Spanish:

| Document | Contents |
| --- | --- |
| [Architecture](docs/Arquitectura.md) | Components, contracts, Windows integration, recovery, and validation. |
| [Default settings](docs/ConfiguracionInicial.md) | Gesture and growth rules, defaults, and calibration evidence. |
| [User experience](docs/ExperienciaUsuario.md) | Tray menu, errors, configuration folders, and distribution. |
| [Cursor mappings](docs/EquivalenciasCursores.md) | Windows roles, theme associations, and source provenance. |
| [Cursor formats](docs/FormatosCursores.md) | Native and custom vector formats, hotspots, and animation order. |

## License and credits

Original code and documentation are licensed under [MIT](LICENSE). Third-party material retains its own license.

- [Bibata](https://github.com/ful1e5/Bibata_Cursor), by Abdulkaiz Khatri (ful1e5), retains its original GPL terms.
- [Nordzy](https://github.com/guillaumeboehm/Nordzy-cursors), by Guillaume Boehm and the project contributors, retains its original GPL terms.

Editable theme sources, credits, and licenses are under `assets/themes`; vector containers in the published themes include their SVG sources. Dependency notices accompany the publication. Windows references are read locally; proprietary Windows cursor images and binaries are not distributed. The Wait/Working Aero-style animations are an original recreation. Icon provenance is recorded in [assets/branding](assets/branding/GENERACION.md).
