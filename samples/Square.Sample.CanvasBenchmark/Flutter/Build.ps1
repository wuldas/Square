param([string]$FlutterSdk = (Join-Path $PSScriptRoot '.sdk\flutter'))
$ErrorActionPreference = 'Stop'
$env:PUB_CACHE = Join-Path $PSScriptRoot '.pub-cache'
$env:TEMP = Join-Path $PSScriptRoot '.tmp'
$env:TMP = $env:TEMP
$env:FLUTTER_SUPPRESS_ANALYTICS = 'true'
$env:CI = 'true'
New-Item -ItemType Directory -Force $env:PUB_CACHE, $env:TEMP | Out-Null
Push-Location $PSScriptRoot
try {
    $flutter = Join-Path $FlutterSdk 'bin\flutter.bat'
    & $flutter pub get
    if ($LASTEXITCODE -ne 0) { throw "flutter pub get failed: $LASTEXITCODE" }
    $version = (& $flutter --version --machine) | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw "flutter version failed: $LASTEXITCODE" }
    & $flutter build windows --release --no-pub "--dart-define=CANVAS_FLUTTER_VERSION=$($version.frameworkVersion)" "--dart-define=CANVAS_ENGINE_REVISION=$($version.engineRevision)"
    if ($LASTEXITCODE -ne 0) { throw "flutter build failed: $LASTEXITCODE" }
} finally { Pop-Location }
