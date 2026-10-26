param([string]$PayloadRoot)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$builder=Join-Path $PSScriptRoot 'BuildInstaller.ps1'
if(-not $PayloadRoot){$PayloadRoot=Join-Path $projectRoot 'artifacts/publish/win-x64'}
$PayloadRoot=[IO.Path]::GetFullPath($PayloadRoot)
# Validate the immutable source before enumerating/copying it; the builder rejects links.
& $builder -PayloadRoot $PayloadRoot -ValidateOnly
$sourceManifestHash=(Get-FileHash -LiteralPath (Join-Path $PayloadRoot 'manifest.sha256')).Hash
$runRoot=Join-Path $projectRoot ('artifacts/installer-tests/'+[guid]::NewGuid().ToString('N'))
$fixture=Join-Path $runRoot 'payload con espacios y ñ'
[IO.Directory]::CreateDirectory($fixture)|Out-Null
$files=@(Get-ChildItem -LiteralPath $PayloadRoot -Recurse -File -Force)
for($index=0;$index -lt $files.Count;$index++){
    $relative=[IO.Path]::GetRelativePath($PayloadRoot,$files[$index].FullName)
    $destination=Join-Path $fixture $relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))|Out-Null
    [IO.File]::Copy($files[$index].FullName,$destination)
    Write-Progress -Activity 'Copying one private installer validation fixture' -Status $relative -PercentComplete (($index+1)*100/$files.Count)
}
Write-Progress -Activity 'Copying one private installer validation fixture' -Completed
$manifest=Join-Path $fixture 'manifest.sha256'
$originalManifest=[IO.File]::ReadAllBytes($manifest)
$originalLines=[IO.File]::ReadAllLines($manifest)
$results=[Collections.Generic.List[object]]::new()

function Assert-PrivatePath([string]$Path){
    $full=[IO.Path]::GetFullPath($Path)
    if(-not $full.StartsWith($runRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw "Test mutation escaped its private fixture: $full"}
    return $full
}
function Invoke-Validation { & $builder -PayloadRoot $fixture -ValidateOnly 6>&1 | Out-String }
function Assert-Rejected([string]$Pattern){
    $failure=$null
    try { Invoke-Validation|Out-Null } catch {$failure=$_}
    if($null -eq $failure){throw 'Invalid installer payload was accepted.'}
    if($failure.Exception.Message -notmatch $Pattern){throw "Unexpected rejection: $($failure.Exception.Message); expected category $Pattern"}
    return $failure.Exception.Message
}
function Invoke-Case([string]$Name,[scriptblock]$Body){
    try {$details=(& $Body | Out-String).Trim();$results.Add([pscustomobject]@{Name=$Name;Passed=$true;Details=$details});Write-Host "PASS $Name"}
    catch {$results.Add([pscustomobject]@{Name=$Name;Passed=$false;Details=$_.Exception.ToString()});Write-Host "FAIL $Name`: $($_.Exception.Message)"}
}
function Set-FixtureManifest([string[]]$Lines){[IO.File]::WriteAllLines($manifest,$Lines,[Text.UTF8Encoding]::new($false))}
function Restore-FixtureManifest {[IO.File]::WriteAllBytes($manifest,$originalManifest)}
function Update-FixtureHash([string]$Relative){
    $hash=(Get-FileHash -LiteralPath (Join-Path $fixture $Relative)).Hash.ToLowerInvariant()
    $pattern='^[0-9a-fA-F]{64}  '+[regex]::Escape($Relative)+'$'
    Set-FixtureManifest @($originalLines|ForEach-Object {if($_ -match $pattern){$hash+'  '+$Relative}else{$_}})
}

Invoke-Case 'Valid publication in a Unicode path with spaces' {Invoke-Validation}
Invoke-Case 'Changed content is rejected before compiler invocation' {
    $path=Assert-PrivatePath (Join-Path $fixture 'CursorShake.dll');$bytes=[IO.File]::ReadAllBytes($path);$changed=[byte[]]$bytes.Clone();$changed[0]=$changed[0] -bxor 1
    try {[IO.File]::WriteAllBytes($path,$changed);Assert-Rejected 'hash mismatch'}finally{[IO.File]::WriteAllBytes($path,$bytes)}
}
Invoke-Case 'Manifested missing file is rejected' {
    $path=Assert-PrivatePath (Join-Path $fixture 'LICENSE');$backup=Assert-PrivatePath (Join-Path $runRoot 'missing-license.backup')
    try {[IO.File]::Move($path,$backup);Assert-Rejected 'missing files'}finally{if([IO.File]::Exists($backup)){[IO.File]::Move($backup,$path)}}
}
Invoke-Case 'Required file remains required after removing its manifest entry' {
    $path=Assert-PrivatePath (Join-Path $fixture 'LICENSE');$backup=Assert-PrivatePath (Join-Path $runRoot 'required-license.backup')
    try {[IO.File]::Move($path,$backup);Set-FixtureManifest @($originalLines|Where-Object {$_ -notmatch '  LICENSE$'});Assert-Rejected 'Required publication file missing'}finally{Restore-FixtureManifest;if([IO.File]::Exists($backup)){[IO.File]::Move($backup,$path)}}
}
Invoke-Case 'Unmanifested extra file is rejected' {
    $path=Assert-PrivatePath (Join-Path $fixture 'qa-extra.txt')
    try {[IO.File]::WriteAllText($path,'Private test data');Assert-Rejected 'File missing from publication manifest'}finally{[IO.File]::Delete($path)}
}
Invoke-Case 'Case-insensitive duplicate manifest entry is rejected' {
    try {Set-FixtureManifest @($originalLines+($originalLines[0].ToUpperInvariant()));Assert-Rejected 'Duplicate manifest path'}finally{Restore-FixtureManifest}
}
Invoke-Case 'Traversal in manifest is rejected' {
    try {Set-FixtureManifest @($originalLines+(('0'*64)+'  ../escape.txt'));Assert-Rejected 'Invalid manifest path'}finally{Restore-FixtureManifest}
}
Invoke-Case 'Malformed manifest is rejected' {
    try {Set-FixtureManifest @($originalLines+'not a valid manifest record');Assert-Rejected 'Malformed publication manifest'}finally{Restore-FixtureManifest}
}
foreach($privateFile in @('config.toml','logs/private.txt','test-results/run.trx')){
    Invoke-Case "Personal/test data is rejected even with a matching hash: $privateFile" {
        $path=Assert-PrivatePath (Join-Path $fixture $privateFile)
        try {
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))|Out-Null
            [IO.File]::WriteAllText($path,'Private fixture data')
            $hash=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
            Set-FixtureManifest @($originalLines+($hash+'  '+$privateFile))
            Assert-Rejected 'Personal or test data in publication'
        } finally {Restore-FixtureManifest;[IO.File]::Delete($path)}
    }
}
Invoke-Case 'x86 PE with an updated manifest cannot masquerade as x64' {
    $path=Assert-PrivatePath (Join-Path $fixture 'CursorShake.exe');$bytes=[IO.File]::ReadAllBytes($path);$changed=[byte[]]$bytes.Clone()
    $pe=[BitConverter]::ToInt32($changed,0x3c);$changed[$pe+4]=0x4c;$changed[$pe+5]=0x01
    try {[IO.File]::WriteAllBytes($path,$changed);Update-FixtureHash 'CursorShake.exe';Assert-Rejected 'x64 PE executable'}finally{[IO.File]::WriteAllBytes($path,$bytes);Restore-FixtureManifest}
}
Invoke-Case 'Wrong executable version is rejected despite a matching manifest' {
    $path=Assert-PrivatePath (Join-Path $fixture 'CursorShake.exe');$bytes=[IO.File]::ReadAllBytes($path)
    try {[IO.File]::Copy((Join-Path $PSHOME 'pwsh.exe'),$path,$true);Update-FixtureHash 'CursorShake.exe';Assert-Rejected 'version differs from project version'}finally{[IO.File]::WriteAllBytes($path,$bytes);Restore-FixtureManifest}
}
Invoke-Case 'A missing bundled theme cannot be removed from the manifest to bypass validation' {
    $path=Assert-PrivatePath (Join-Path $fixture 'themes/nordzy-white');$backup=Assert-PrivatePath (Join-Path $runRoot 'nordzy-white.backup')
    try {[IO.Directory]::Move($path,$backup);Set-FixtureManifest @($originalLines|Where-Object {$_ -notmatch '  themes/nordzy-white/'});Assert-Rejected 'Bundled theme missing'}finally{Restore-FixtureManifest;if([IO.Directory]::Exists($backup)){[IO.Directory]::Move($backup,$path)}}
}
Invoke-Case 'A junction is rejected without traversing or altering its target' {
    $target=Assert-PrivatePath (Join-Path $runRoot 'junction-target');$junction=Assert-PrivatePath (Join-Path $fixture 'unsafe-junction')
    [IO.Directory]::CreateDirectory($target)|Out-Null;$proof=Join-Path $target 'preserve.txt';[IO.File]::WriteAllText($proof,'Target must survive.')
    try {
        New-Item -ItemType Junction -Path $junction -Target $target|Out-Null
        Assert-Rejected 'Payload contains a link'
    } finally {if([IO.Directory]::Exists($junction)){[IO.Directory]::Delete($junction)};if([IO.File]::ReadAllText($proof) -ne 'Target must survive.'){throw 'Junction target changed during rejection.'}}
}
Invoke-Case 'Fixture is valid again after all negative cases' {Invoke-Validation}
Invoke-Case 'Original publication is unchanged and remains valid' {
    if((Get-FileHash -LiteralPath (Join-Path $PayloadRoot 'manifest.sha256')).Hash -ne $sourceManifestHash){throw 'Original publication manifest changed during tests.'}
    & $builder -PayloadRoot $PayloadRoot -ValidateOnly 6>&1 | Out-String
}
$failed=@($results|Where-Object {-not $_.Passed}).Count
[pscustomobject]@{Passed=($failed -eq 0);CaseCount=$results.Count;Failed=$failed;InstallerExecuted=$false;CompilerInvoked=$false;OriginalPayload=$PayloadRoot;PrivateFixture=$fixture;Cases=$results}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $runRoot 'validation-report.json') -Encoding utf8
Write-Host "Installer validation: $($results.Count-$failed)/$($results.Count) passed. Report: $(Join-Path $runRoot 'validation-report.json')"
if($failed){throw "$failed installer validation test(s) failed."}
