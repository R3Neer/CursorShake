param(
    [ValidateSet('Build','Test','Publish')][string]$Task='Test',
    [ValidateSet('Debug','Release')][string]$Configuration='Release',
    [string]$PublishDirectory='artifacts/publish/win-x64',
    [string]$TestFilter
)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dotnetExecutable=(Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
Push-Location -LiteralPath $projectRoot
function Invoke-DotNet([string[]]$CommandArguments) {
    & $dotnetExecutable @CommandArguments
    if($LASTEXITCODE -ne 0){throw "dotnet failed ($LASTEXITCODE): $($CommandArguments -join ' ')"}
}
. (Join-Path $PSScriptRoot "BuildOutput.ps1")

try {
    & (Join-Path $PSScriptRoot 'PrepareThemes.ps1')
    Invoke-DotNet -CommandArguments @('restore','CursorShake.slnx','--locked-mode','--disable-parallel')
    Invoke-DotNet -CommandArguments @('build','CursorShake.slnx','-c',$Configuration,'--no-restore')
    if($Task -eq 'Build'){return}
    $results=Join-Path $projectRoot 'artifacts/test-results'
    $testArguments=if($TestFilter){@('--filter',$TestFilter)}else{@()}
    if($TestFilter){Write-Host "Explicit test filter: $TestFilter"}
    Invoke-DotNet -CommandArguments (@('test','tests/CursorShake.Core.Tests/CursorShake.Core.Tests.csproj','-c',$Configuration,'--no-build','--no-restore','--logger','trx;LogFileName=core.trx','--results-directory',$results)+$testArguments)
    Invoke-DotNet -CommandArguments (@('test','tests/CursorShake.Windows.Tests/CursorShake.Windows.Tests.csproj','-c',$Configuration,'--no-build','--no-restore','--logger','trx;LogFileName=windows.trx','--results-directory',$results)+$testArguments)
    if($Task -ne 'Publish'){return}
    $publishPath=if([IO.Path]::IsPathRooted($PublishDirectory)){$PublishDirectory}else{Join-Path $projectRoot $PublishDirectory}
    $publish=Reset-OwnedBuildOutput $projectRoot $publishPath
    $guard=Reset-OwnedBuildOutput $projectRoot (Join-Path $projectRoot 'artifacts/guard-publish')
    # Pin the currently supported .NET10 patch; publishing does not install a machine-wide runtime.
    $runtime='10.0.13'
    foreach($project in @('src/CursorShake.App/CursorShake.App.csproj','src/CursorShake.RecoveryGuard/CursorShake.RecoveryGuard.csproj')){
        Invoke-DotNet -CommandArguments @('restore',$project,'-r','win-x64','--locked-mode',"-p:RuntimeFrameworkVersion=$runtime",'-p:SelfContained=true','-p:NuGetLockFilePath=packages.win-x64.lock.json','--disable-parallel')
        $output=if($project.Contains('RecoveryGuard')){$guard}else{$publish}
        Invoke-DotNet -CommandArguments @('publish',$project,'-c',$Configuration,'-r','win-x64','--self-contained','true',"-p:RuntimeFrameworkVersion=$runtime",'--no-restore','-o',$output)
    }
    foreach($file in Get-ChildItem -LiteralPath $guard -File){
        if($file.Name -eq '.cursorshake-build-owned'){continue}
        $target=Join-Path $publish $file.Name
        if(Test-Path -LiteralPath $target){if((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash){throw "Conflicting publication dependency: $($file.Name)"}}
        else{[IO.File]::Copy($file.FullName,$target)}
    }
    [IO.File]::Copy((Join-Path $projectRoot 'LICENSE'),(Join-Path $publish 'LICENSE'),$true)
    $notices=Join-Path $publish 'notices';[IO.Directory]::CreateDirectory($notices)|Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'assets/licenses') -Destination $notices -Recurse -Force
    $assets=Get-Content 'src/CursorShake.Windows/obj/project.assets.json' -Raw|ConvertFrom-Json -AsHashtable
    $packageRoot=@($assets.packageFolders.Keys)[0];$inventory=@()
    foreach($library in $assets.libraries.GetEnumerator()|Sort-Object Key){
        if($library.Value.type -ne 'package'){continue}
        $folder=Join-Path $packageRoot $library.Value.path
        $spec=Get-ChildItem -LiteralPath $folder -Filter '*.nuspec'|Select-Object -First 1
        [xml]$xml=[IO.File]::ReadAllText($spec.FullName)
        $metadata=$xml.package.metadata
        $packageNotice=Join-Path $notices ($library.Key.Replace('/','-'))
        [IO.Directory]::CreateDirectory($packageNotice)|Out-Null
        [IO.File]::Copy($spec.FullName,(Join-Path $packageNotice $spec.Name),$true)
        foreach($file in Get-ChildItem -LiteralPath $folder -File|Where-Object{$_.Name -match 'LICENSE|COPYING|NOTICE'}){[IO.File]::Copy($file.FullName,(Join-Path $packageNotice $file.Name),$true)}
        $inventory+=[pscustomobject]@{Package=$library.Key;License=[string]$metadata.license.'#text';Authors=[string]$metadata.authors;Copyright=[string]$metadata.copyright;Source=[string]$metadata.repository.url;Commit=[string]$metadata.repository.commit}
    }
    foreach($pack in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')){
        $folder=$null
        foreach($packageFolder in $assets.packageFolders.Keys){
            $candidate=Join-Path $packageFolder "$pack/$runtime"
            if(Test-Path -LiteralPath $candidate -PathType Container){$folder=$candidate;break}
        }
        if(-not $folder){throw "Runtime source notices are missing: $pack/$runtime"}
        $packageNotice=Join-Path $notices "$pack-$runtime"
        [IO.Directory]::CreateDirectory($packageNotice)|Out-Null
        $licenses=@(Get-ChildItem -LiteralPath $folder -File|Where-Object{$_.Name -match 'LICENSE|COPYING|NOTICE|\.nuspec$'})
        if(-not($licenses|Where-Object{$_.Name -match '^LICENSE'})){throw "Runtime license is missing: $pack/$runtime"}
        foreach($file in $licenses){[IO.File]::Copy($file.FullName,(Join-Path $packageNotice $file.Name),$true)}
        $inventory+=[pscustomobject]@{Package="$pack/$runtime";License='See bundled original notices';Authors='Microsoft and contributors';Copyright='See bundled original notices';Source='https://github.com/dotnet';Commit='See package nuspec'}
    }
    $inventory|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $notices 'packages.json') -Encoding utf8
    [IO.File]::WriteAllText((Join-Path $publish 'START-HERE.txt'),"CursorShake`r`nRun CursorShake.exe. Keep all files and the themes folder together.`r`nConfiguration is available from the tray menu; startup registration is optional.`r`nOriginal code: MIT (LICENSE). Themes and dependencies retain their own licenses (themes and notices).`r`n")
    $files=Get-ChildItem -LiteralPath $publish -File -Recurse|Where-Object{$_.Name -ne 'manifest.sha256'}|Sort-Object FullName
    $lines=foreach($file in $files){(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetRelativePath($publish,$file.FullName).Replace('\','/')}
    [IO.File]::WriteAllLines((Join-Path $publish 'manifest.sha256'),$lines,[Text.UTF8Encoding]::new($false))
    Write-Host "Published: $publish"
} finally {Pop-Location}
