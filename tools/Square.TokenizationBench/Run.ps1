param(
    [string]$Python = "python",
    [ValidateSet("all", "synthetic", "textmate", "large")][string]$Profile = "all",
    [ValidateSet("production", "legacy", "checkpoints")][string]$Comparison = "production",
    [int]$Rounds = 9,
    [string]$OutputDirectory = "artifacts/tokenization-bench/results",
    [switch]$ValidateOnly
)
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
Push-Location $repoRoot
try {
    & $Python (Join-Path $PSScriptRoot "prepare.py")
    if ($LASTEXITCODE -ne 0) { throw "Benchmark preparation failed ($LASTEXITCODE)." }
    $env:DOTNET_CLI_HOME = Join-Path $repoRoot "artifacts/tokenization-bench/dotnet-home"
    $env:NUGET_PACKAGES = Join-Path $repoRoot "artifacts/tokenization-bench/packages"
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = "false"
    $env:DOTNET_TieredCompilation = "0"
    $project = Join-Path $PSScriptRoot "Square.TokenizationBench.csproj"
    dotnet build $project -c Release --disable-build-servers -p:UseSharedCompilation=false -p:MSBuildEnableWorkloadResolver=false -p:NuGetAudit=false -m:1
    if ($LASTEXITCODE -ne 0) { throw "Benchmark build failed ($LASTEXITCODE)." }
    $benchmarkArgs = @("--profile", $Profile, "--comparison", $Comparison, "--rounds", $Rounds, "--output", $OutputDirectory)
    if ($ValidateOnly) { $benchmarkArgs += "--validate-only" }
    dotnet (Join-Path $PSScriptRoot "bin/Release/net10.0/Square.TokenizationBench.dll") @benchmarkArgs
    if ($LASTEXITCODE -ne 0) { throw "Benchmark run failed ($LASTEXITCODE)." }
}
finally { Pop-Location }
