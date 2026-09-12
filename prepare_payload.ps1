param(
  [Parameter(Mandatory=$true)][string]$ReleaseRoot,
  [Parameter(Mandatory=$true)][string]$OriginalRoot,
  [Parameter(Mandatory=$true)][string]$RussianRoot,
  [Parameter(Mandatory=$true)][string]$AlternateGodRoot
)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$release=[IO.Path]::GetFullPath($ReleaseRoot)
$original=[IO.Path]::GetFullPath($OriginalRoot)
$russian=[IO.Path]::GetFullPath($RussianRoot)
$alternate=[IO.Path]::GetFullPath($AlternateGodRoot)
$payload=Join-Path $root 'payload'
$port=Join-Path $payload 'port'
$patches=Join-Path $payload 'patches'
$euManifest=Join-Path $root 'manifests\source-manifest-eu.json'
$godManifest=Join-Path $root 'manifests\source-manifest-ru-god.json'
if(-not(Test-Path -LiteralPath "$release\Launcher.exe")){throw 'ReleaseRoot is not a PC Edition release'}
if(-not(Test-Path -LiteralPath "$original\Default.xex")){throw 'OriginalRoot has no Default.xex'}
if(-not(Test-Path -LiteralPath "$russian\Default.xex")){throw 'RussianRoot has no Default.xex'}
if(-not(Test-Path -LiteralPath "$alternate\Default.xex")){throw 'AlternateGodRoot has no Default.xex'}

& "$root\build_tools.ps1"
if($LASTEXITCODE){throw 'Tool build failed'}

foreach($generated in @($port,$patches)){
  $full=[IO.Path]::GetFullPath($generated)
  if(-not $full.StartsWith([IO.Path]::GetFullPath($payload),[StringComparison]::OrdinalIgnoreCase)){throw "Unsafe generated path: $full"}
  if(Test-Path -LiteralPath $full){Remove-Item -LiteralPath $full -Recurse -Force}
  New-Item -ItemType Directory -Force -Path $full | Out-Null
}

$releasePrefix=$release.TrimEnd('\')+'\'
foreach($file in Get-ChildItem -LiteralPath $release -Recurse -File){
  $relative=$file.FullName.Substring($releasePrefix.Length)
  $normalized=$relative.Replace('\','/')
  $exclude=$normalized -like 'Data/Original/*' -or $normalized -like 'Data/Russian/*' -or
    $normalized -like 'UserData/*' -or $normalized -like 'Reports/*' -or
    $normalized -like 'tests/*' -or $normalized -eq 'tests' -or
    $normalized -like 'Support/Backups/*' -or $normalized -like 'Support/Revisions/*' -or
    $normalized -like 'Support/Build/*' -or $normalized -like 'Support/Evidence/*' -or
    $normalized -eq 'Launcher.Tests.exe' -or $normalized -like '*.log' -or
    $normalized -like '*.previous'
  if($exclude){continue}
  $target=Join-Path $port $relative
  New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($target)) | Out-Null
  Copy-Item -LiteralPath $file.FullName -Destination $target
}

foreach($language in @('Original','Russian')){
  $meta=Join-Path $release "Data\$language\Data\GENRY_BUILD_ALL_LANGS.json"
  if(Test-Path -LiteralPath $meta){$target=Join-Path $port "Data\$language\Data\GENRY_BUILD_ALL_LANGS.json";New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($target))|Out-Null;Copy-Item -LiteralPath $meta -Destination $target}
}

& "$root\build\tools\BuildGameManifest.exe" $original $euManifest 'eu-retail' 'Xbox 360 EU retail donor'
if($LASTEXITCODE){throw 'Source manifest generation failed'}
& "$root\build\tools\BuildGameManifest.exe" $alternate $godManifest 'ru-god-alt' 'Xbox 360 Russian alternate GOD (LIVE/XSF)'
if($LASTEXITCODE){throw 'Alternate source manifest generation failed'}
& "$root\build\tools\BuildPayloadManifest.exe" $port "$payload\payload-manifest.json"
if($LASTEXITCODE){throw 'Payload manifest generation failed'}

$euPatches=Join-Path $patches 'eu-retail';$godPatches=Join-Path $patches 'ru-god-alt'
New-Item -ItemType Directory -Force -Path $euPatches,$godPatches | Out-Null
& "$root\build\tools\BuildEotpPatches.exe" $euManifest $original $original $euPatches 'original.json' 'EU donor to clean Original'
if($LASTEXITCODE){throw 'EU Original delta generation failed'}
& "$root\build\tools\BuildEotpPatches.exe" $euManifest $original $russian $euPatches 'russian.json' 'EU donor to final Russian PC Edition'
if($LASTEXITCODE){throw 'EU Russian delta generation failed'}
& "$root\build\tools\BuildEotpPatches.exe" $godManifest $alternate $original $godPatches 'original.json' 'Alternate Russian GOD to clean Original'
if($LASTEXITCODE){throw 'GOD Original delta generation failed'}
& "$root\build\tools\BuildEotpPatches.exe" $godManifest $alternate $russian $godPatches 'russian.json' 'Alternate Russian GOD to final Russian PC Edition'
if($LASTEXITCODE){throw 'GOD Russian delta generation failed'}

Write-Output "Payload ready: $payload"
