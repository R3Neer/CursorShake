# Build, package and test CursorShake

The README covers installation and use. These instructions are for contributors working from the repository root on Windows.

## Build and publish

Install the .NET SDK selected by `global.json`:

```powershell
./build/Build.ps1 -Task Test
./build/Build.ps1 -Task Publish
```

Publishing prepares themes, restores locked dependencies, runs the automated tests, and creates the self-contained folder in `artifacts/publish/win-x64`. The version is defined in `Directory.Build.props`. Keep the main executable, RecoveryGuard, runtime dependencies, themes and notices together.

When a running copy uses the default output, publish into a separate owned directory:

```powershell
./build/Build.ps1 -Task Publish -PublishDirectory artifacts/publish/installer-win-x64
```

Some native baseline tests require CursorShake and other cursor writers to be closed. An explicitly supplied `-TestFilter` is printed by the build; a filtered run does not certify the excluded checks. Native smoke modes and `experiments/` are separate tools, and mutating modes must run exclusively. Never start a second cursor engine just to test alongside the user's active copy.

## Build the installer

Use [Inno Setup 7.1.0](https://jrsoftware.org/isdl.php). After publishing:

```powershell
./build/BuildInstaller.ps1 -Compiler 'C:/path/to/Inno Setup 7/ISCC.exe'
./build/TestInstaller.ps1
```

If you used the separate `installer-win-x64` publication above, add `-PayloadRoot artifacts/publish/installer-win-x64` to both installer commands. Otherwise they use the default `win-x64` output.

The builder verifies publication hashes, stages a separate snapshot, and produces `CursorShake-<version>-Setup.exe`, its SHA-256, the compiler payload manifest and build metadata in `artifacts/installer`. It does not run Setup. `-ValidateOnly` checks an existing publication without compiling or changing it. Packaging requires binaries supporting the current `installer-protocol.txt`, not just matching version numbers.

The wizard supports per-user or all-user scope and a selectable destination. Start it normally and let it request elevation: launching it elevated loses the original-user context needed for safe startup and final-launch integration. Themes and dependency notices are part of the payload. Build metadata describes that build invocation; independent QA receipts record actual installation tests.

## Maintenance and native QA

Updates and uninstallation request verified shutdown of App and RecoveryGuard. Unresponsive processes, failed restoration, pending recovery or uncertain ownership block maintenance; processes are never forcibly terminated. The maintenance gate prevents admission of a new engine, while Windows file locks can still require abort/retry. Protocols are specified in [Architecture](Arquitectura.md).

Uninstallation keeps personal data by default. An unelevated per-user uninstall offers explicit deletion of verified configuration/themes after showing their locations. All-user uninstall preserves all personal data. The protected installation index lets maintenance inspect recovery state for inactive users; its records are separate from the movable configuration folder and retired after binary removal.

Test both scopes, update and uninstall paths without overwriting user configuration or leaving QA startup registrations. Back up and verify restoration of any pre-existing startup task. Preserve pending recovery instead of deleting it to unblock a test. See [user experience and validation](ExperienciaUsuario.md) for the accepted lifecycle rules and evidence limits.

The local real-logon sample reached operational readiness in 6.745 seconds. That sample does not certify other hardware or every future build. Code signing is not configured; released installers are unsigned.

## Updating a source-built copy

Exit CursorShake before replacing its application files. Keep the configuration folder and personal themes. If you remove the application, disable **Iniciar con Windows** first. For installed copies, run the installer to update or use Windows Settings to uninstall.


## Specification

The detailed specification is in Spanish:

- [Default settings](ConfiguracionInicial.md): gesture and growth rules, defaults, and calibration.
- [User experience](ExperienciaUsuario.md): tray menu, errors, folders, and distribution.
- [Cursor mappings](EquivalenciasCursores.md): Windows roles and theme provenance.
- [Cursor formats](FormatosCursores.md): native/vector formats, hotspots, and animation.
- [Architecture](Arquitectura.md): implementation contracts, recovery, validation evidence, and its limits.

