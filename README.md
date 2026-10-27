# CursorShake

Find your pointer by shaking the mouse. CursorShake smoothly enlarges it and returns it to normal when you stop or click.

**Vector cursor themes stay sharp as they grow, including animated cursors.**

[Download for Windows 11 x64](https://github.com/R3Neer/CursorShake/releases/latest)

- Adjustable shake sensitivity, timing, and maximum cursor size.
- Themes follow the current cursor role, including text selection, links, and busy animations.
- Settings reload when you save the configuration.
- Runs in the system tray, with optional startup with Windows.

## Install

Requires **Windows 11 x64** in a local desktop session. The .NET runtime is included.

1. Download the setup executable from the [latest release](https://github.com/R3Neer/CursorShake/releases/latest).
2. Open it normally and choose the installation folder. You can install for your account or for all users; Windows requests administrator approval when needed.
3. Leave **Start CursorShake with Windows** selected if you want automatic startup, and **Run CursorShake now** to launch it.

The installer is unsigned, so Windows may show an unknown-publisher or SmartScreen warning. To update, run the new installer over your existing installation. Uninstall through Windows Settings; configuration and personal themes are kept by default.

## Use and configure

Right-click the tray icon to choose a theme, pause the effect, or open the configuration. If the icon is hidden, open the tray overflow.

Choose **Open configuration** to adjust sensitivity, size, and animation timing in the commented `config.toml`. Save the file to apply changes at normal cursor size.

The interface supports English (default) and Spanish. To switch to Spanish, change the existing top-level `language` setting to:

```toml
language = "spanish"
```
If the setting is missing, add it before the first `[section]`.

## Cursor themes

Choose **Windows** (Aero-style), **Bibata Ice**, or **Nordzy White** from the tray's **Theme** menu. The selected theme stays active during normal use; shaking enlarges the cursor for its current role.

To add your own theme:

1. Open the configuration folder from the tray menu.
2. Put your cursor files in `themes/my-theme` beside `config.toml`.
3. Declare the theme in the TOML, following the [custom cursor guide](docs/CustomCursors.md).

Static `.vcur` and animated `.vani` files support vector artwork. Standard `.cur` and `.ani` files also work. Starting from an SVG? The guide walks through creating both static and animated vector cursors.

## Documentation and license

See the [development guide](docs/Development.md) to build from source or work on the project.

Original code and documentation are licensed under [MIT](LICENSE). Bundled themes retain their own GPL terms:

- [Bibata](https://github.com/ful1e5/Bibata_Cursor), by Abdulkaiz Khatri (ful1e5).
- [Nordzy](https://github.com/guillaumeboehm/Nordzy-cursors), by Guillaume Boehm and the project contributors.

[Theme sources](assets/themes) and [license notices](assets/licenses) are included in the repository.
