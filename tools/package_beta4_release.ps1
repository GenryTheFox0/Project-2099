param(
  [string]$Version='v2.0.0-beta.4.1',
  [string]$PayloadSource='D:\EOT_PC_FEATURES_20260905\EOTInstallerPrivateQA_20260923\InstallerBeta4StreamingPayload',
  [switch]$ValidateFilterOnly
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source=[IO.Path]::GetFullPath($PayloadSource)
$artifacts=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))
$release=[IO.Path]::GetFullPath((Join-Path $artifacts ('release-'+$Version)))
if($Version -ne 'v2.0.0-beta.4.1'){throw 'This package recipe is pinned to the beta 4.1 packaging hotfix'}
if(-not $release.StartsWith($artifacts+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Release path escaped artifacts'}
if(-not(Test-Path -LiteralPath (Join-Path $source 'port') -PathType Container) -or
   -not(Test-Path -LiteralPath (Join-Path $source 'patches\index.json') -PathType Leaf)){throw 'Incomplete payload source'}
$indexPath=Join-Path $source 'patches\index.json'
$indexHash=(Get-FileHash -Algorithm SHA256 -LiteralPath $indexPath).Hash
if($indexHash -ne '606FCD5A773D4B9565DBEE16A0FE9A51226A22471C20E2D87DD531985ADDA29E'){
  throw 'The verified streaming language index changed'
}
$index=Get-Content -LiteralPath $indexPath -Raw -Encoding UTF8|ConvertFrom-Json
if($index.Schema -ne 4 -or -not $index.CanonicalTargetsVerified -or
   $index.CanonicalOriginal.Count -ne 293 -or $index.CanonicalRussian.Count -ne 293){throw 'Unverified language index'}
$manifest=Get-Content -LiteralPath (Join-Path $source 'payload-manifest.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($manifest.Files.Count -ne 142){throw 'Unexpected port inventory'}
$allSourceFiles=@($manifest.Files)
$publicExclusions=@(
  'ModManager.exe',
  'Launcher.Tests.exe',
  'LOCAL_UPDATE_PACKAGE.md',
  'local-update-manifest.schema.json',
  'UPDATE_20260908_RU.txt',
  'UPDATE_SYSTEM.md',
  'UPDATE_V1_PHOTO_20260919_RU.txt',
  'UPDATE_V100_SETTINGS_MODS_20260919_RU.txt',
  'UPDATE_V101_PERF_DETAIL_20260921_RU.txt',
  'UPDATE_V95_HITCH_20260918_RU.txt',
  'update-manifest.schema.json',
  'update-sources.schema.json',
  'V2_BETA1_TEST.txt',
  'Support/BUILD_INFO.txt',
  'Support/DLSS5/AUTO_DLSS5_DEPLOYMENT.json',
  'Support/DLSS5/AUTOPILOT_STAGE_REPORT.json',
  'Support/DLSS5/DEPTH_FIX_DEPLOYMENT.json',
  'Support/DLSS5/SELECTED_PROFILE.json'
)
# The standalone manager, developer tools, stale update notes, schemas and QA
# state do not belong in an installed game root. Legal notices and player-facing
# README files are deliberately not covered by this list.
$manifest.Files=@($manifest.Files|Where-Object {
  -not $_.Path.StartsWith('Tools/',[StringComparison]::OrdinalIgnoreCase) -and
  $_.Path -notin $publicExclusions
})
if($manifest.Files.Count -ne 108 -or ($allSourceFiles.Count-$manifest.Files.Count) -ne 34){
  throw 'Unexpected beta 4.1 public file exclusion'
}
$forbidden=@($manifest.Files|Where-Object {
  $_.Path.StartsWith('Tools/',[StringComparison]::OrdinalIgnoreCase) -or
  $_.Path -in $publicExclusions
})
if($forbidden.Count -ne 0){throw ('Forbidden public payload entries remain: '+(($forbidden.Path|Sort-Object)-join ', '))}
$requiredPublic=@(
  'Launcher.exe','Launcher.UpdateHelper.exe','launcher.paths.json','runtime.manifest.json','update.sources.json',
  'README_RU.txt','Mods/README_RU.txt','Mods/_example/mod.json','Mods/_example/Data/README.txt',
  'Support/DLSS5/GENRY_DLSS5_PACKAGE.json','Support/DLSS5/Autopilot/LICENSE','Support/DLSS5/Autopilot/README.md',
  'Support/FontsV3/README_RU.txt',
  'Support/Licenses/DearImGui.txt','Support/Licenses/fmt.txt','Support/Licenses/ReXGlue.txt',
  'Support/Licenses/SDL3.txt','Support/Licenses/spdlog.txt'
)
foreach($path in $requiredPublic){
  if(@($manifest.Files|Where-Object {$_.Path -ieq $path}).Count -ne 1){
    throw ('Required runtime, player or legal payload entry was excluded: '+$path)
  }
}
$portExtras=@(Get-ChildItem -LiteralPath (Join-Path $source 'port') -File -Recurse)
if($portExtras.Count -ne $allSourceFiles.Count){throw 'Source port contains files outside payload manifest'}
if($ValidateFilterOnly){
  [pscustomobject]@{Status='BETA4_1_FILTER_VALIDATED_NO_FILES_CHANGED';Version=$Version;
    SourceFiles=$allSourceFiles.Count;PublicFiles=$manifest.Files.Count;ExcludedFiles=($allSourceFiles.Count-$manifest.Files.Count);
    RequiredPublicFiles=$requiredPublic.Count;Release=$release} | ConvertTo-Json -Compress
  return
}
if(Test-Path -LiteralPath $release){throw ('Release artifacts already exist; preserve and inspect: '+$release)}
$manifest.Build='Project 2099 V2 BETA 4.1 packaging hotfix (V2 BETA 3.1 TEST runtime)'
[void][IO.Directory]::CreateDirectory($release)
$staged=Join-Path $release 'payload'
[void][IO.Directory]::CreateDirectory($staged)
function LinkVerified($inputPath,$outputPath,$size,$hash) {
  if(-not(Test-Path -LiteralPath $inputPath -PathType Leaf)){throw ('Missing payload file: '+$inputPath)}
  $item=Get-Item -LiteralPath $inputPath
  if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw ('Payload file is a link: '+$inputPath)}
  if($item.Length -ne $size -or (Get-FileHash -Algorithm SHA256 -LiteralPath $inputPath).Hash -ne $hash){
    throw ('Payload source mismatch: '+$inputPath)
  }
  [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputPath))
  New-Item -ItemType HardLink -Path $outputPath -Value $inputPath | Out-Null
}
foreach($file in $manifest.Files){
  $rel=$file.Path.Replace('/','\')
  LinkVerified (Join-Path $source ('port\'+$rel)) (Join-Path $staged ('port\'+$rel)) $file.Size $file.Sha256
}
$unique=@{}
foreach($patch in @($index.English)+@($index.Russian)){
  $key=$patch.Delta.ToLowerInvariant()
  if($unique.ContainsKey($key)){
    if($unique[$key] -ne $patch.DeltaSha256){throw 'Conflicting patch digest'}
  }else{$unique[$key]=$patch.DeltaSha256}
}
foreach($key in $unique.Keys){
  $patch=@($index.English)+@($index.Russian)|Where-Object {$_.Delta.ToLowerInvariant() -eq $key}|Select-Object -First 1
  $rel=$patch.Delta.Replace('/','\')
  LinkVerified (Join-Path $source ('patches\'+$rel)) (Join-Path $staged ('patches\'+$rel)) $patch.DeltaSize $patch.DeltaSha256
}
$utf8=New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $staged 'payload-manifest.json'),($manifest|ConvertTo-Json -Depth 8),$utf8)
Copy-Item -LiteralPath $indexPath -Destination (Join-Path $staged 'patches\index.json')
$seven='C:\Program Files\7-Zip\7z.exe'
if(-not(Test-Path -LiteralPath $seven)){throw '7-Zip unavailable'}
$assetName='EOT-PC-Payload-'+$Version+'.zip'
$zip=Join-Path $release $assetName
Push-Location $release
try{& $seven a -tzip -mm=Deflate -mx=5 -mmt=on -- $zip '.\payload'}finally{Pop-Location}
if($LASTEXITCODE -ne 0){throw 'ZIP creation failed'}
& $seven t -- $zip
if($LASTEXITCODE -ne 0){throw 'ZIP integrity test failed'}
$zipHash=(Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash
$zipLength=(Get-Item -LiteralPath $zip).Length
$channel=[ordered]@{Schema=1;Version=$Version;
  PayloadUrl="https://github.com/GenryTheFox0/Project-2099/releases/download/$Version/$assetName";
  PayloadSize=$zipLength;PayloadSha256=$zipHash}
$channelFile=Join-Path $release 'release-channel.json'
[IO.File]::WriteAllText($channelFile,($channel|ConvertTo-Json -Compress),$utf8)
$build=Join-Path $repo ('build\release-'+($Version -replace '[^A-Za-z0-9._-]','_'))
& (Join-Path $repo 'build.ps1') -OutDir $build -ChannelFile $channelFile
if($LASTEXITCODE -ne 0){throw 'Installer build failed'}
$installerName='EOTInstaller-'+$Version+'.exe'
$installer=Join-Path $release $installerName
Copy-Item -LiteralPath (Join-Path $build 'EOTInstaller.exe') -Destination $installer
$installerHash=(Get-FileHash -Algorithm SHA256 -LiteralPath $installer).Hash
$summary=[ordered]@{
  Schema=1;Version=$Version;Repository='https://github.com/GenryTheFox0/Project-2099';
  Installer=[ordered]@{Name=$installerName;Size=(Get-Item -LiteralPath $installer).Length;Sha256=$installerHash};
  Payload=[ordered]@{Name=$assetName;Size=$zipLength;Sha256=$zipHash};
  SupportedSources=@('usa-europe-retail','usa-europe-retail-r2','eu-retail','ru-god-alt','sazanoff-rus-god');
  OriginalGameImageIncluded=$false;ContainsGameDerivedPatchBytes=$true;
  ModManager='separate executable paused; basic launcher Mods page remains';ExcludedDeveloperOnlyFiles=33;ExcludedStandaloneModManager=$true;
  PublicPortFiles=$manifest.Files.Count;
  LanguageIndexSha256=$indexHash
}
[IO.File]::WriteAllText((Join-Path $release 'release-manifest.json'),($summary|ConvertTo-Json -Depth 6),$utf8)
[IO.File]::WriteAllText((Join-Path $release 'SHA256SUMS.txt'),
  "$installerHash  $installerName`r`n$zipHash  $assetName`r`n",$utf8)
[pscustomobject]@{Status='LOCAL_RELEASE_ARTIFACTS_VALIDATED_NOT_UPLOADED';Release=$release;
  InstallerSha256=$installerHash;PayloadSha256=$zipHash;PayloadBytes=$zipLength;Patches=$unique.Count} | ConvertTo-Json -Compress
