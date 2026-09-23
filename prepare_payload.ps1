param(
  [Parameter(Mandatory=$true)][string]$ReleaseRoot,
  [Parameter(Mandatory=$true)][string]$OriginalRoot,
  [Parameter(Mandatory=$true)][string]$RussianRoot,
  [Parameter(Mandatory=$true)][string]$AlternateGodRoot,
  [Parameter(Mandatory=$true)][string]$UsaEuropeRoot,
  [Parameter(Mandatory=$true)][string]$UsaEuropeR2Root,
  [Parameter(Mandatory=$true)][string]$SazanOffRoot,
  [string]$ExpectedOriginalManifest='',
  [string]$ExpectedRussianManifest='',
  [string]$OriginalCorrections='',
  [string]$RussianCorrections='',
  [switch]$ValidateOnly
)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
# Fail before a build or any payload mutation. Historical thirteen-file indices
# and unverified "Original" trees must not silently become a new release.
$proofInputs=@{
  ExpectedOriginalManifest=$ExpectedOriginalManifest
  ExpectedRussianManifest=$ExpectedRussianManifest
  OriginalCorrections=$OriginalCorrections
  RussianCorrections=$RussianCorrections
}
foreach($name in $proofInputs.Keys){
  $value=$proofInputs[$name]
  if([String]::IsNullOrWhiteSpace($value)){
    throw "Schema 4 payload requires -$name; no payload files were changed. Supply verified canonical manifests and both correction manifests."
  }
  if(-not(Test-Path -LiteralPath $value -PathType Leaf)){throw "Missing schema 4 input: $name = $value"}
  $proofFull=[IO.Path]::GetFullPath($value)
  foreach($generatedRoot in @((Join-Path $root 'payload'),(Join-Path $root 'patchsets'))){
    if($proofFull.StartsWith([IO.Path]::GetFullPath($generatedRoot).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){
      throw "Schema 4 proof inputs must be outside regenerated payload/patchsets: $proofFull"
    }
  }
  $proof=Get-Content -LiteralPath $proofFull -Raw -Encoding UTF8 | ConvertFrom-Json
  if($null -eq $proof.Files -or @($proof.Files).Count -eq 0){throw "Empty or invalid schema 4 manifest: $proofFull"}
}
$release=[IO.Path]::GetFullPath($ReleaseRoot)
$original=[IO.Path]::GetFullPath($OriginalRoot)
$russian=[IO.Path]::GetFullPath($RussianRoot)
$alternate=[IO.Path]::GetFullPath($AlternateGodRoot)
$usa=[IO.Path]::GetFullPath($UsaEuropeRoot)
$usaR2=[IO.Path]::GetFullPath($UsaEuropeR2Root)
$sazan=[IO.Path]::GetFullPath($SazanOffRoot)
$payload=Join-Path $root 'payload'
$port=Join-Path $payload 'port'
$patches=Join-Path $payload 'patches'
$sets=Join-Path $root 'patchsets'
$euManifest=Join-Path $root 'manifests\source-manifest-eu.json'
$godManifest=Join-Path $root 'manifests\source-manifest-ru-god.json'
$usaManifest=Join-Path $root 'manifests\source-manifest-usa-europe.json'
$usaR2Manifest=Join-Path $root 'manifests\source-manifest-usa-europe-r2.json'
$sazanManifest=Join-Path $root 'manifests\source-manifest-sazanoff.json'
if(-not(Test-Path -LiteralPath "$release\Launcher.exe")){throw 'ReleaseRoot is not a PC Edition release'}
if(-not(Test-Path -LiteralPath "$original\Default.xex")){throw 'OriginalRoot has no Default.xex'}
if(-not(Test-Path -LiteralPath "$russian\Default.xex")){throw 'RussianRoot has no Default.xex'}
if(-not(Test-Path -LiteralPath "$alternate\Default.xex")){throw 'AlternateGodRoot has no Default.xex'}
if(-not(Test-Path -LiteralPath "$usa\Default.xex")){throw 'UsaEuropeRoot has no Default.xex'}
if(-not(Test-Path -LiteralPath "$usaR2\Default.xex")){throw 'UsaEuropeR2Root has no Default.xex'}
if(-not(Test-Path -LiteralPath "$sazan\Default.xex")){throw 'SazanOffRoot has no Default.xex'}
if($ValidateOnly){Write-Output 'PASS: schema 4 inputs and source roots present; no payload files were changed';return}

& "$root\build_tools.ps1"
if($LASTEXITCODE){throw 'Tool build failed'}

# Validate every cleanup target before touching either one. patchsets is a build
# input tree outside payload and must not be deleted as though it were a cache.
foreach($generated in @($port,$patches)){
  $full=[IO.Path]::GetFullPath($generated)
  if(-not $full.StartsWith([IO.Path]::GetFullPath($payload).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw "Unsafe generated path: $full"}
  if((Test-Path -LiteralPath $full) -and ((Get-Item -LiteralPath $full).Attributes -band [IO.FileAttributes]::ReparsePoint)){
    throw "Generated payload target is a link: $full"
  }
}
foreach($generated in @($port,$patches)){
  $full=[IO.Path]::GetFullPath($generated)
  if(Test-Path -LiteralPath $full){Remove-Item -LiteralPath $full -Recurse -Force}
  New-Item -ItemType Directory -Force -Path $full | Out-Null
}
New-Item -ItemType Directory -Force -Path $sets | Out-Null

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
& "$root\build\tools\BuildGameManifest.exe" $usa $usaManifest 'usa-europe-retail' 'Xbox 360 USA / Europe retail donor'
if($LASTEXITCODE){throw 'USA/Europe source manifest generation failed'}
& "$root\build\tools\BuildGameManifest.exe" $usaR2 $usaR2Manifest 'usa-europe-retail-r2' 'Xbox 360 USA / Europe retail donor (revision 2)'
if($LASTEXITCODE){throw 'USA/Europe revision 2 source manifest generation failed'}
& "$root\build\tools\BuildGameManifest.exe" $sazan $sazanManifest 'sazanoff-rus-god' 'Xbox 360 Region Free RUS GOD (SazanOFF v1.0b)'
if($LASTEXITCODE){throw 'SazanOFF source manifest generation failed'}
& "$root\build\tools\BuildPayloadManifest.exe" $port "$payload\payload-manifest.json"
if($LASTEXITCODE){throw 'Payload manifest generation failed'}

$euPatches=Join-Path $sets 'eu-retail';$godPatches=Join-Path $sets 'ru-god-alt';$usaPatches=Join-Path $sets 'usa-europe-retail'
$usaR2Patches=Join-Path $sets 'usa-europe-retail-r2';$sazanPatches=Join-Path $sets 'sazanoff-rus-god'
New-Item -ItemType Directory -Force -Path $euPatches,$godPatches,$usaPatches,$usaR2Patches,$sazanPatches | Out-Null
& "$root\build\tools\BuildEotpPatches.exe" $euManifest $original $original $euPatches 'original.json' 'EU donor to clean Original'
if($LASTEXITCODE){throw 'EU Original delta generation failed'}
& "$root\build\tools\BuildEotpPatches.exe" $euManifest $original $russian $euPatches 'russian.json' 'EU donor to final Russian PC Edition'
if($LASTEXITCODE){throw 'EU Russian delta generation failed'}
& "$root\build\tools\BuildEotpPatches.exe" $godManifest $alternate $original $godPatches 'original.json' 'Alternate Russian GOD to clean Original'
if($LASTEXITCODE){throw 'GOD Original delta generation failed'}
& "$root\build\tools\BuildEotpPatches.exe" $godManifest $alternate $russian $godPatches 'russian.json' 'Alternate Russian GOD to final Russian PC Edition'
if($LASTEXITCODE){throw 'GOD Russian delta generation failed'}
& "$root\build\tools\BuildEotpPatches.exe" $usaManifest $usa $original $usaPatches 'original.json' 'USA/Europe retail donor to clean Original'
if($LASTEXITCODE){throw 'USA/Europe Original delta generation failed'}
& "$root\build\tools\BuildEotpPatches.exe" $usaManifest $usa $russian $usaPatches 'russian.json' 'USA/Europe retail donor to final Russian PC Edition'
if($LASTEXITCODE){throw 'USA/Europe Russian delta generation failed'}
& "$root\build\tools\BuildEotpPatches.exe" $usaR2Manifest $usaR2 $original $usaR2Patches 'original.json' 'USA/Europe revision 2 to clean Original'
if($LASTEXITCODE){throw 'USA/Europe revision 2 Original delta generation failed'}
& "$root\build\tools\BuildEotpPatches.exe" $usaR2Manifest $usaR2 $russian $usaR2Patches 'russian.json' 'USA/Europe revision 2 to final Russian PC Edition'
if($LASTEXITCODE){throw 'USA/Europe revision 2 Russian delta generation failed'}
& "$root\build\tools\BuildEotpPatches.exe" $sazanManifest $sazan $original $sazanPatches 'original.json' 'SazanOFF Russian GOD to clean Original'
if($LASTEXITCODE){throw 'SazanOFF Original delta generation failed'}
& "$root\build\tools\BuildEotpPatches.exe" $sazanManifest $sazan $russian $sazanPatches 'russian.json' 'SazanOFF Russian GOD to final Russian PC Edition'
if($LASTEXITCODE){throw 'SazanOFF Russian delta generation failed'}

# Everything above is per-image. This turns it into what actually ships: one
# index keyed by file hash, with each delta stored once.
& python "$root\tools\build_patch_index.py" --root $root --output $patches `
  --expected-original $ExpectedOriginalManifest --expected-russian $ExpectedRussianManifest `
  --original-corrections $OriginalCorrections --russian-corrections $RussianCorrections
if($LASTEXITCODE){throw 'Patch index generation failed'}
if(-not(Test-Path -LiteralPath "$patches\index.json")){throw 'Patch index was not produced'}

Write-Output "Payload ready: $payload"
