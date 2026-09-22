param([string]$OutDir='')
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$build=if([String]::IsNullOrWhiteSpace($OutDir)){Join-Path $root 'build'}else{[IO.Path]::GetFullPath($OutDir)}
$assets=Join-Path $root 'assets'
$euManifest=Join-Path $root 'manifests\source-manifest-eu.json'
$godManifest=Join-Path $root 'manifests\source-manifest-ru-god.json'
$usaManifest=Join-Path $root 'manifests\source-manifest-usa-europe.json'
$usaR2Manifest=Join-Path $root 'manifests\source-manifest-usa-europe-r2.json'
$sazanManifest=Join-Path $root 'manifests\source-manifest-sazanoff.json'
$projectLogo=Join-Path $assets 'brand\project2099_logo_transparent_v2.png'
foreach($required in @("$assets\miguel.png","$assets\peter.png","$assets\eot.ico",$projectLogo,$euManifest,$godManifest,$usaManifest,$usaR2Manifest,$sazanManifest)){
  if(-not(Test-Path -LiteralPath $required)){throw "Required build input is missing: $required"}
}
New-Item -ItemType Directory -Force -Path $build | Out-Null
$fx='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc=Join-Path $fx 'csc.exe'
$references=@('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll','System.Xaml.dll')|ForEach-Object{Join-Path $fx $_}
$references+=@('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll')|ForEach-Object{Join-Path "$fx\WPF" $_}
$common=@('/nologo','/platform:x64','/optimize+','/utf8output',"/win32icon:$assets\eot.ico","/win32manifest:$root\app.manifest",
  "/resource:$assets\miguel.png,EOT.miguel.png","/resource:$assets\peter.png,EOT.peter.png",
  "/resource:$projectLogo,EOT.project2099_logo.png",
  "/resource:$euManifest,EOT.source-manifest.eu.json","/resource:$godManifest,EOT.source-manifest.ru-god.json",
  "/resource:$usaManifest,EOT.source-manifest.usa-europe.json",
  "/resource:$usaR2Manifest,EOT.source-manifest.usa-europe-r2.json",
  "/resource:$sazanManifest,EOT.source-manifest.sazanoff.json")
foreach($reference in $references){$common+="/reference:$reference"}
$compression=@('System.IO.Compression.dll','System.IO.Compression.FileSystem.dll')|ForEach-Object{Join-Path $fx $_}
foreach($reference in $compression){$common+="/reference:$reference"}
$common+="/reference:$fx\System.Net.Http.dll"
$channel=Join-Path $root 'manifests\release-channel.json'
if(Test-Path -LiteralPath $channel){$common+="/resource:$channel,EOT.release-channel.json"}
$sources=@('AssemblyInfo.cs','Program.cs','Models.cs','XdvdfsImage.cs','SvodImage.cs','EotpPatch.cs','PayloadProvider.cs','InstallerCore.cs','InstallerWindow.cs')|ForEach-Object{Join-Path "$root\src" $_}
& $csc /target:winexe "/out:$build\EOTInstaller.exe" @common @sources
if($LASTEXITCODE){throw 'EOTInstaller compilation failed'}
& $csc /target:exe /define:EOT_INSTALLER_TEST "/out:$build\EOTInstaller.Tests.exe" @common @sources
if($LASTEXITCODE){throw 'EOTInstaller test-host compilation failed'}
Get-FileHash -Algorithm SHA256 -LiteralPath "$build\EOTInstaller.exe","$build\EOTInstaller.Tests.exe"
