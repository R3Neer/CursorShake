function Reset-OwnedBuildOutput([string]$ProjectRoot,[string]$Destination) {
    $root=[IO.Path]::GetFullPath($ProjectRoot)
    $path=[IO.Path]::GetFullPath($Destination)
    $artifacts=(Join-Path $root 'artifacts')+[IO.Path]::DirectorySeparatorChar
    if(-not $path.StartsWith($artifacts,[StringComparison]::OrdinalIgnoreCase)){throw 'Generated output must stay beneath artifacts.'}
    $current=$path
    while($current){
        if(Test-Path -LiteralPath $current){
            if((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Output path contains a link: $current"}
        }
        $current=[IO.Path]::GetDirectoryName($current)
    }
    $marker=Join-Path $path '.cursorshake-build-owned'
    $ownership='Generated CursorShake build output.'
    if(Test-Path -LiteralPath $path){
        if(-not(Test-Path -LiteralPath $marker -PathType Leaf)){throw "Refusing to replace an unmarked output folder: $path"}
        if((Get-Item -LiteralPath $marker -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Output ownership marker cannot be a link.'}
        if([IO.File]::ReadAllText($marker) -ne $ownership){throw 'Output ownership marker does not match.'}
        Remove-Item -LiteralPath $path -Recurse -Force
    }
    [IO.Directory]::CreateDirectory($path)|Out-Null
    [IO.File]::WriteAllText($marker,$ownership)
    return $path
}
