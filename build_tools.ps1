param()
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$out=Join-Path $root 'build\tools'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$fx='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc=Join-Path $fx 'csc.exe'
$refs=@('/reference:System.dll','/reference:System.Core.dll','/reference:System.Web.Extensions.dll')
foreach($name in @('BuildGameManifest','BuildPayloadManifest','BuildEotpPatches')){
  $toolRefs=@($refs)
  if($name -eq 'BuildEotpPatches'){$toolRefs+="/reference:$fx\System.IO.Compression.dll"}
  & $csc /nologo /target:exe /platform:x64 /optimize+ "/out:$out\$name.exe" @toolRefs "$root\tools\$name.cs"
  if($LASTEXITCODE){throw "$name compilation failed"}
}
Get-ChildItem -LiteralPath $out -Filter '*.exe' | Get-FileHash -Algorithm SHA256
