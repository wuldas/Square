param([switch]$SkipPub)
$ErrorActionPreference = 'Stop'
$previousDirectory = Get-Location
$previousTemp = $env:TEMP
$previousTmp = $env:TMP
$previousPubCache = $env:PUB_CACHE
try {
  Set-Location $PSScriptRoot
  $flutterRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\Flutter\.sdk\flutter')).Path
  $flutter = Join-Path $flutterRoot 'bin\flutter.bat'
  $dart = Join-Path $flutterRoot 'bin\cache\dart-sdk\bin\dart.exe'
  $env:TEMP = Join-Path $PSScriptRoot '.tmp'
  $env:TMP = $env:TEMP
  $env:PUB_CACHE = (Resolve-Path (Join-Path $PSScriptRoot '..\..\Flutter\.pub-cache')).Path
  New-Item -ItemType Directory -Force $env:TEMP | Out-Null
  $version = Get-Content (Join-Path $flutterRoot 'bin\cache\flutter.version.json') -Raw | ConvertFrom-Json
  if (-not $SkipPub) {
    & $flutter pub get --offline
    if ($LASTEXITCODE -ne 0) { throw 'Offline pub get failed; inspect the cache error before continuing.' }
  }
  & $dart format --output=none --set-exit-if-changed lib
  if ($LASTEXITCODE -ne 0) { throw 'Dart formatting check failed.' }
  & $flutter analyze --no-pub
  if ($LASTEXITCODE -ne 0) { throw 'Flutter analyze failed.' }
  & $flutter build windows --release --no-pub "--dart-define=CANVAS_FLUTTER_VERSION=$($version.frameworkVersion)" "--dart-define=CANVAS_ENGINE_REVISION=$($version.engineRevision)"
  if ($LASTEXITCODE -ne 0) { throw 'Flutter release build failed.' }
  Write-Output (Join-Path $PSScriptRoot 'build\windows\x64\runner\Release\square_canvas_v11.exe')
} finally {
  $env:TEMP = $previousTemp
  $env:TMP = $previousTmp
  $env:PUB_CACHE = $previousPubCache
  Set-Location $previousDirectory
}
