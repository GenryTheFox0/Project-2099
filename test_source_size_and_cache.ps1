param([string]$Assembly='')
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$testAssembly=if([String]::IsNullOrWhiteSpace($Assembly)){
  Join-Path $root 'build\EOTInstaller.Tests.exe'
}else{[IO.Path]::GetFullPath($Assembly)}
if(-not(Test-Path -LiteralPath $testAssembly -PathType Leaf)){
  throw "Test-host assembly not found: $testAssembly"
}

$tempBase=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
$testRoot=[IO.Path]::GetFullPath((Join-Path $tempBase ('EOTSSC-'+[Guid]::NewGuid().ToString('N').Substring(0,8))))
if(-not $testRoot.StartsWith($tempBase,[StringComparison]::OrdinalIgnoreCase)){
  throw "Unsafe temporary test path: $testRoot"
}
New-Item -ItemType Directory -Path $testRoot | Out-Null
try{
  $fx='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
  $csc=Join-Path $fx 'csc.exe'
  $exe=Join-Path $testRoot 'TestSourceSizeAndCache.exe'
  $source=Join-Path $root 'tools\TestSourceSizeAndCache.cs'
  $refs=@(
    "/reference:$fx\System.dll",
    "/reference:$fx\System.Core.dll",
    "/reference:$fx\System.IO.Compression.dll",
    "/reference:$fx\System.IO.Compression.FileSystem.dll"
  )
  & $csc /nologo /target:exe /platform:x64 /optimize+ /utf8output "/out:$exe" @refs $source
  if($LASTEXITCODE){throw 'TestSourceSizeAndCache compilation failed'}
  & $exe $testAssembly
  if($LASTEXITCODE){throw "TestSourceSizeAndCache failed with exit code $LASTEXITCODE"}
}finally{
  $resolved=[IO.Path]::GetFullPath($testRoot)
  if($resolved.StartsWith($tempBase,[StringComparison]::OrdinalIgnoreCase) -and
      [IO.Path]::GetFileName($resolved).StartsWith('EOTSSC-',[StringComparison]::Ordinal)){
    Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue
  }
}
