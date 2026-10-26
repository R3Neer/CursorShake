param(
    [string]$PayloadRoot,
    [string]$Compiler='D:\Desarrollo\Toolchains\InnoSetup7\ISCC.exe',
    [switch]$ValidateOnly
)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'BuildOutput.ps1')
if(-not $PayloadRoot){$PayloadRoot=Join-Path $projectRoot 'artifacts/publish/win-x64'}
$PayloadRoot=$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PayloadRoot)
[xml]$props=[IO.File]::ReadAllText((Join-Path $projectRoot 'Directory.Build.props'))
$version=[string]$props.Project.PropertyGroup.Version
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'The installer requires a stable three-part project version.'}

function Assert-NoLink([string]$Path) {
    $current=$Path
    while($current){
        $item=Get-Item -LiteralPath $current -Force -ErrorAction Stop
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Payload path contains a link: $current"}
        $current=[IO.Path]::GetDirectoryName($current)
    }
}
function Assert-X64([string]$Path) {
    $stream=[IO.File]::OpenRead($Path)
    $reader=[IO.BinaryReader]::new($stream)
    try {
        if($reader.ReadUInt16() -ne 0x5a4d){throw "Not a PE executable: $Path"}
        $stream.Position=0x3c;$offset=$reader.ReadUInt32();$stream.Position=$offset
        if($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664){throw "Expected an x64 PE executable: $Path"}
    } finally {$reader.Dispose()}
}
Assert-NoLink $PayloadRoot
$manifest=Join-Path $PayloadRoot 'manifest.sha256'
Assert-NoLink $manifest
$expected=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($line in [IO.File]::ReadAllLines($manifest)){
    if($line -notmatch '^([0-9a-fA-F]{64})  (.+)$'){throw 'Malformed publication manifest.'}
    $hash=$Matches[1];$relative=$Matches[2]
    if([IO.Path]::IsPathRooted($relative) -or $relative -match '(^|[\\/])\.\.?([\\/]|$)|[:*?"<>|]' -or $relative.Contains('\')){throw "Invalid manifest path: $relative"}
    if(-not $expected.TryAdd($relative,$hash)){throw "Duplicate manifest path: $relative"}
}
# Walk without following directory links, before any recursive file enumeration.
$pending=[Collections.Generic.Stack[string]]::new();$pending.Push($PayloadRoot)
$actual=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
while($pending.Count){
    foreach($item in Get-ChildItem -LiteralPath $pending.Pop() -Force){
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Payload contains a link: $($item.FullName)"}
        if($item.PSIsContainer){$pending.Push($item.FullName);continue}
        $relative=[IO.Path]::GetRelativePath($PayloadRoot,$item.FullName).Replace('\','/')
        if($relative -eq 'manifest.sha256'){continue}
        if(-not $expected.ContainsKey($relative)){throw "File missing from publication manifest: $relative"}
        if((Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash -ne $expected[$relative]){throw "Publication hash mismatch: $relative"}
        if($item.Name -match '^(config\.toml|.*\.log|.*\.trx)$' -or $relative -match '^(state|logs|test-results)/'){throw "Personal or test data in publication: $relative"}
        [void]$actual.Add($relative)
    }
}
if($actual.Count -ne $expected.Count){throw 'Publication manifest refers to missing files.'}
foreach($required in @('CursorShake.exe','CursorShake.dll','CursorShake.RecoveryGuard.exe','CursorShake.RecoveryGuard.dll','coreclr.dll','hostfxr.dll','CursorShake.runtimeconfig.json','CursorShake.RecoveryGuard.runtimeconfig.json','LICENSE','notices/packages.json')){
    if(-not $actual.Contains($required)){throw "Required publication file missing: $required"}
}
# An older 1.0.0 binary may ignore maintenance arguments and start the cursor engine.
$protocol=Join-Path $PayloadRoot 'installer-protocol.txt'
if(-not $actual.Contains('installer-protocol.txt') -or [IO.File]::ReadAllText($protocol).Trim() -ne 'CursorShake installer protocol 2'){throw 'Publication does not support installer protocol 2. Publish the current source before packaging.'}
foreach($theme in @('windows','bibata-ice','nordzy-white')){
    if(-not @($actual | Where-Object {$_ -like "themes/$theme/*"}).Count){throw "Bundled theme missing: $theme"}
}
foreach($exe in @('CursorShake.exe','CursorShake.RecoveryGuard.exe')){
    $path=Join-Path $PayloadRoot $exe;Assert-X64 $path
    $info=[Diagnostics.FileVersionInfo]::GetVersionInfo($path)
    if($info.FileVersion.Trim() -ne "$version.0"){throw "Publication version differs from project version: $exe ($($info.FileVersion))"}
}
if($ValidateOnly){Write-Host "Validated $($actual.Count) publication files for version $version.";return}
if(-not(Test-Path -LiteralPath $Compiler -PathType Leaf)){throw "Inno Setup 7.1.0 compiler not found: $Compiler. Supply -Compiler."}
$compilerVersion=(& $Compiler --version | Out-String).Trim()
if($LASTEXITCODE -ne 0 -or $compilerVersion -ne '7.1.0'){throw "Expected Inno Setup 7.1.0, got: $compilerVersion"}
$stagePath=Join-Path $projectRoot 'artifacts/installer-payload'
$outputPath=Join-Path $projectRoot 'artifacts/installer'
foreach($destination in @($stagePath,$outputPath)){
    $full=[IO.Path]::GetFullPath($destination)
    if($PayloadRoot.Equals($full,[StringComparison]::OrdinalIgnoreCase) -or $PayloadRoot.StartsWith($full+'\',[StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith($PayloadRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Payload and installer output must not overlap.'}
}
$stage=Reset-OwnedBuildOutput $projectRoot $stagePath
$locks=[Collections.Generic.List[IO.FileStream]]::new()
try {
    # Copy a verified snapshot. The app may continue using its original publication.
    $packaged=@($actual | Where-Object {[IO.Path]::GetFileName($_) -ne '.cursorshake-build-owned'} | Sort-Object)
    foreach($relative in $packaged){
        $destination=Join-Path $stage $relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))|Out-Null
        [IO.File]::Copy((Join-Path $PayloadRoot $relative),$destination)
        $locks.Add([IO.File]::Open($destination,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read))
        if((Get-FileHash -LiteralPath $destination).Hash -ne $expected[$relative]){throw "Publication changed while staging: $relative"}
    }
    # Emit the exact snapshot manifest, excluding build ownership markers.
    $lines=foreach($relative in $packaged){$expected[$relative].ToLowerInvariant()+'  '+$relative}
    [IO.File]::WriteAllLines((Join-Path $stage 'manifest.sha256'),[string[]]$lines,[Text.UTF8Encoding]::new($false))
    $locks.Add([IO.File]::Open((Join-Path $stage 'manifest.sha256'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read))
    $output=Reset-OwnedBuildOutput $projectRoot $outputPath
    & $Compiler --quiet --no-ide-signtools "--define=AppVersion=$version" "--define=PayloadRoot=$stage" "--define=InstallerOutput=$output" (Join-Path $projectRoot 'installer/CursorShake.iss')
    if($LASTEXITCODE -ne 0){throw "Inno Setup compilation failed: $LASTEXITCODE"}
    $setup=Join-Path $output "CursorShake-$version-Setup.exe"
    Assert-X64 $setup
    if(-not(Test-Path -LiteralPath (Join-Path $output 'payload-manifest.txt'))){throw 'Compiler payload manifest missing.'}
    # Compare Inno's actual compiled sources and hashes with the intended snapshot.
    $compiledExpected=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($relative in $packaged){$compiledExpected.Add($relative,$expected[$relative])}
    $compiledExpected.Add('manifest.sha256',(Get-FileHash -LiteralPath (Join-Path $stage 'manifest.sha256')).Hash)
    $compiledSeen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($entry in Import-Csv -LiteralPath (Join-Path $output 'payload-manifest.txt') -Delimiter "`t"){
        $source=[string]$entry.SourceFilename
        if($source.StartsWith('\\?\')){$source=$source.Substring(4)}
        $relative=[IO.Path]::GetRelativePath($stage,$source).Replace('\','/')
        if(-not $compiledExpected.ContainsKey($relative) -or $entry.SHA256Sum -ne $compiledExpected[$relative]){throw "Compiler payload differs from snapshot: $source"}
        [void]$compiledSeen.Add($relative)
    }
    if($compiledSeen.Count -ne $compiledExpected.Count){throw 'Compiler payload is incomplete.'}
    if([Diagnostics.FileVersionInfo]::GetVersionInfo($setup).FileVersion.Trim() -ne "$version.0"){throw 'Installer version differs from project version.'}
    $setupHash=(Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$setup.sha256","$setupHash  $([IO.Path]::GetFileName($setup))`n")
    [pscustomobject]@{Version=$version;CompilerVersion=$compilerVersion;PreviewOnly=$false;LifecycleIntegrated=$true;RealInstallQaComplete=$false;InstallerExecuted=$false;PayloadFiles=$packaged.Count+1;SetupSHA256=$setupHash}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'build.json') -Encoding utf8
    Write-Host "Installer: $setup"
} finally {foreach($handle in $locks){$handle.Dispose()}}
