param([string]$Version='v1.0.0-beta.1')
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$payload=Join-Path $root 'payload'
$artifacts=Join-Path $root ('artifacts\release-'+$Version)
$channel=Join-Path $root 'manifests\release-channel.json'
if(-not(Test-Path -LiteralPath "$payload\payload-manifest.json") -or -not(Test-Path -LiteralPath "$payload\patches\eu-retail\russian.json") -or -not(Test-Path -LiteralPath "$payload\patches\ru-god-alt\russian.json")){throw 'Complete payload has not been prepared'}
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$assetName="EOT-PC-Payload-$Version.zip"
$asset=Join-Path $artifacts $assetName
$seven=(Get-Command 7z.exe -ErrorAction SilentlyContinue).Source
if(-not $seven){$seven='C:\Program Files\7-Zip\7z.exe'}
if(-not(Test-Path -LiteralPath $seven)){throw '7z.exe was not found'}
if(-not(Test-Path -LiteralPath $asset)){
  Push-Location $root
  try{& $seven a -tzip -mm=Deflate -mx=7 -mmt=on -- $asset '.\payload'}finally{Pop-Location}
  if($LASTEXITCODE){throw 'Payload ZIP creation failed'}
}else{
  Write-Output "Reusing existing payload ZIP: $asset"
}
& $seven t -- $asset
if($LASTEXITCODE){throw 'Payload ZIP verification failed'}
$payloadItem=Get-Item -LiteralPath $asset
$payloadHash=(Get-FileHash -Algorithm SHA256 -LiteralPath $asset).Hash
$payloadUrl="https://github.com/GenryTheFox/Spider-Man-Edge-of-Time-PC-Edition/releases/download/$Version/$assetName"
$channelObject=[ordered]@{Schema=1;Version=$Version;PayloadUrl=$payloadUrl;PayloadSize=$payloadItem.Length;PayloadSha256=$payloadHash}
$channelText=$channelObject|ConvertTo-Json -Compress
[IO.File]::WriteAllText($channel,$channelText,[Text.UTF8Encoding]::new($false))

& "$root\build.ps1"
if($LASTEXITCODE){throw 'Final installer build failed'}
$installerName="EOTInstaller-$Version.exe"
$installer=Join-Path $artifacts $installerName
Copy-Item -LiteralPath "$root\build\EOTInstaller.exe" -Destination $installer -Force
$installerItem=Get-Item -LiteralPath $installer
$installerHash=(Get-FileHash -Algorithm SHA256 -LiteralPath $installer).Hash
$release=[ordered]@{
  Schema=1;Version=$Version;Repository='https://github.com/GenryTheFox/Spider-Man-Edge-of-Time-PC-Edition';
  Installer=[ordered]@{Name=$installerName;Size=$installerItem.Length;Sha256=$installerHash};
  Payload=[ordered]@{Name=$assetName;Size=$payloadItem.Length;Sha256=$payloadHash};
  SupportedSources=@('eu-retail','ru-god-alt');OriginalGameImageIncluded=$false;
  ContainsGameDerivedPatchBytes=$true
}
[IO.File]::WriteAllText((Join-Path $artifacts 'release-manifest.json'),($release|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
$sums="$installerHash  $installerName`r`n$payloadHash  $assetName`r`n"
[IO.File]::WriteAllText((Join-Path $artifacts 'SHA256SUMS.txt'),$sums,[Text.UTF8Encoding]::new($false))
Write-Output "INSTALLER=$installer"
Write-Output "INSTALLER_SHA256=$installerHash"
Write-Output "PAYLOAD=$asset"
Write-Output "PAYLOAD_SHA256=$payloadHash"
