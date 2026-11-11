param(
    [ValidateSet('comprehensive','extended')][string]$Suite = 'comprehensive',
    [string]$OutputDirectory = '',
    [string]$Python = 'python',
    [string]$ElectronExe = "$PSScriptRoot/Electron/.runtime/electron.exe",
    [string]$FlutterExe = "$PSScriptRoot/V11/Flutter/build/windows/x64/runner/Release/square_canvas_v11.exe",
    [string]$BaselineSquare = '',
    [string]$BaselineRoot = '',
    [switch]$PlanOnly,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$runner = "$PSScriptRoot/V11/tools/run-v11.py"
$arguments = @($runner, '--suite', $Suite)
if ($OutputDirectory) { $arguments += @('--output', $OutputDirectory) }
if ($BaselineSquare) { $arguments += @('--baseline-square', $BaselineSquare) }
if ($BaselineRoot) { $arguments += @('--baseline-root', $BaselineRoot) }
if ($PlanOnly) {
    & $Python @arguments --plan-only
    if ($LASTEXITCODE) { throw 'Composite input/plan validation failed' }
    return
}
if (!$SkipBuild) {
    & dotnet publish "$PSScriptRoot/V11/Square/CanvasBench.SquareV11.csproj" -c Release -r win-x64 --self-contained true -p:RuntimeFrameworkVersion=10.0.14 --nologo
    if ($LASTEXITCODE) { throw 'Square self-contained publish failed' }
    & dotnet publish "$PSScriptRoot/V11/Avalonia/Avalonia.csproj" -c Release -r win-x64 --self-contained true -p:RuntimeFrameworkVersion=10.0.14 --nologo
    if ($LASTEXITCODE) { throw 'Avalonia self-contained publish failed' }
    & dotnet build "$PSScriptRoot/V11/Observer/Observer.csproj" -c Release --nologo
    if ($LASTEXITCODE) { throw 'Observer build failed' }
}
# External pinned Electron/Flutter runtimes are never installed or silently replaced.
if (!(Test-Path $ElectronExe -PathType Leaf)) { throw 'Provide -ElectronExe with the pinned runtime executable.' }
if (!(Test-Path $FlutterExe -PathType Leaf)) { throw 'Build the V11 Flutter Release app first or provide -FlutterExe.' }
$arguments += @('--electron', $ElectronExe, '--flutter', $FlutterExe)
& $Python @arguments
if ($LASTEXITCODE) { throw 'Collection incomplete; original raw/failure records retained.' }
