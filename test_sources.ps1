param()
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$build=Join-Path $root 'build\tests'
New-Item -ItemType Directory -Force -Path $build | Out-Null
$fx='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc=Join-Path $fx 'csc.exe'
$manifests=@(
  @{file='source-manifest-eu.json';name='EOT.source-manifest.eu.json'},
  @{file='source-manifest-ru-god.json';name='EOT.source-manifest.ru-god.json'},
  @{file='source-manifest-usa-europe.json';name='EOT.source-manifest.usa-europe.json'},
  @{file='source-manifest-usa-europe-r2.json';name='EOT.source-manifest.usa-europe-r2.json'},
  @{file='source-manifest-sazanoff.json';name='EOT.source-manifest.sazanoff.json'}
)
$args=@('/nologo','/target:exe','/platform:x64',"/out:$build\TestSourceMismatch.exe")
foreach($reference in @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Net.Http.dll')){
  $args+="/reference:$fx\$reference"
}
foreach($manifest in $manifests){
  $path=Join-Path $root ('manifests\'+$manifest.file)
  if(-not(Test-Path -LiteralPath $path)){throw "Missing manifest: $path"}
  $args+=("/resource:$path," + $manifest.name)
}
$channel=Join-Path $root 'manifests\release-channel.json'
if(Test-Path -LiteralPath $channel){$args+="/resource:$channel,EOT.release-channel.json"}
foreach($source in @('Models.cs','XdvdfsImage.cs','SvodImage.cs','EotpPatch.cs','PayloadProvider.cs','InstallerCore.cs')){
  $args+=(Join-Path "$root\src" $source)
}
$args+=(Join-Path "$root\tools" 'TestSourceMismatch.cs')
& $csc @args
if($LASTEXITCODE){throw 'Source-probe test build failed'}
& "$build\TestSourceMismatch.exe"
if($LASTEXITCODE){throw 'Source-probe test failed'}
