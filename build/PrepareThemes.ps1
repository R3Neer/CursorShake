param([string]$Destination = (Join-Path $PSScriptRoot '../artifacts/generated-themes'))
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = Join-Path $projectRoot 'assets/themes'
. (Join-Path $PSScriptRoot 'BuildOutput.ps1')
$destinationRoot = Reset-OwnedBuildOutput $projectRoot $Destination
Add-Type -AssemblyName System.IO.Compression
foreach ($theme in Get-ChildItem -LiteralPath $source -Directory) {
    $output = Join-Path $destinationRoot $theme.Name
    [IO.Directory]::CreateDirectory($output) | Out-Null
    foreach ($resource in Get-ChildItem -LiteralPath $theme.FullName -Directory) {
        $manifest = Join-Path $resource.FullName 'cursor.toml'
        if (-not (Test-Path -LiteralPath $manifest)) { continue }
        $extension = if ([IO.File]::ReadAllText($manifest).Contains('[[frames]]')) { '.vani' } else { '.vcur' }
        $target = Join-Path $output ($resource.Name + $extension)
        $temporary = $target + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
        try {
            $stream = [IO.File]::Open($temporary,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
            $zip = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$false)
            try {
                foreach ($file in Get-ChildItem -LiteralPath $resource.FullName -File -Recurse | Sort-Object FullName) {
                    $relative = [IO.Path]::GetRelativePath($resource.FullName,$file.FullName).Replace('\','/')
                    $entry = $zip.CreateEntry($relative,[IO.Compression.CompressionLevel]::Optimal)
                    $entry.LastWriteTime = [DateTimeOffset]::new(2020,1,1,0,0,0,[TimeSpan]::Zero)
                    $entryStream = $entry.Open()
                    try { $input = [IO.File]::OpenRead($file.FullName); try { $input.CopyTo($entryStream) } finally { $input.Dispose() } } finally { $entryStream.Dispose() }
                }
            } finally { $zip.Dispose() }
            [IO.File]::Move($temporary,$target,$true)
        } finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
    }
    foreach ($name in @('theme.toml','LICENSE','CREDITS.md','source-manifest.json')) {
        $file = Join-Path $theme.FullName $name
        if (Test-Path -LiteralPath $file) { [IO.File]::Copy($file,(Join-Path $output $name),$true) }
    }
    $upstream = Join-Path $theme.FullName '_upstream'
    if (Test-Path -LiteralPath $upstream) { Copy-Item -LiteralPath $upstream -Destination $output -Recurse -Force }
}
Write-Host "Prepared themes: $destinationRoot"
