# CursorShake

A planned Windows tray utility that makes your cursor grow when you shake the mouse, with configurable sensitivity, smooth scaling, and cursor themes.

**Status: design and technical prototypes.** The application has not been implemented yet. There is no end-user executable or installer to download, and the experiments in this repository are not a usable release.

## Planned features

- Adjustable shake count, movement thresholds, timing, and maximum size multiplier.
- Smooth growth and return, with a click starting the return immediately.
- A permanent cursor theme that follows the current Windows cursor role.
- Static and animated cursors, with timing and hotspot alignment preserved while scaling.
- Windows/Aero-style, Bibata Ice, and Nordzy white themes.
- Custom `.cur` and `.ani` resources, plus vector `.vcur` and `.vani` containers.
- A tray menu for themes, preview, pause, configuration, and startup settings.
- A TOML configuration file that reloads when saved and can move with your personal themes.

These are agreed design goals, not features available in a released application. Initial target: local Windows 11 x64 sessions. Compatibility and visual quality still require testing with the finished application.

## Installation and use

No release is available yet. Once implemented, the application is intended to run without a separately installed .NET runtime. An installer will follow; startup with Windows will be optional.

There is no end-user CLI. Configuration and quick actions will be accessible from the system tray. Do not run the experimental projects as an installation method: some temporarily replace system cursors or create temporary scheduled tasks.

## Design documentation

The detailed specification is currently in Spanish:

| Document | Contents |
| --- | --- |
| [Architecture](docs/Arquitectura.md) | Components, contracts, Windows integration, recovery, and validation plan. |
| [Default settings](docs/ConfiguracionInicial.md) | Gesture and growth rules, defaults, and calibration evidence. |
| [User experience](docs/ExperienciaUsuario.md) | Tray menu, errors, configuration folders, and distribution. |
| [Cursor mappings](docs/EquivalenciasCursores.md) | Windows roles, theme associations, and source provenance. |
| [Cursor formats](docs/FormatosCursores.md) | Native and custom vector formats, hotspots, and animation order. |

Technical experiments and their limitations are indexed in [the evidence section](docs/Arquitectura.md#8-evidencias-y-límites-de-lo-probado). They demonstrate specific mechanisms on the test machine; they do not certify the finished product. The [application icon](assets/branding/GENERACION.md) is already selected.

## License and credits

Original project code and documentation are licensed under [MIT](LICENSE). Third-party material retains its own license; this does not relicense Bibata or Nordzy.

- [Bibata](https://github.com/ful1e5/Bibata_Cursor), by Abdulkaiz Khatri (ful1e5), retains its original GPL terms.
- [Nordzy](https://github.com/guillaumeboehm/Nordzy-cursors), by Guillaume Boehm and the project contributors, retains its original GPL terms.

The theme packages are not bundled as application assets yet. No proprietary Windows cursor images or binaries are included. Dependency notices and corresponding theme sources must accompany any future distribution that includes them.
