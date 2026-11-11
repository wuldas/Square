param(
    [Parameter(Mandatory=$true)][string]$BaselineSquare,
    [Parameter(Mandatory=$true)][string]$BaselineRoot,
    [string]$OutputDirectory = '',
    [string]$Python = 'python',
    [string]$ElectronExe = "$PSScriptRoot/Electron/.runtime/electron.exe",
    [string]$FlutterExe = "$PSScriptRoot/V11/Flutter/build/windows/x64/runner/Release/square_canvas_v11.exe",
    [switch]$PlanOnly,
    [switch]$SkipBuild
)
# Same new harness + original baseline engine, not an old measurement or v4 binary.
& "$PSScriptRoot/Run-Benchmark.ps1" -BaselineSquare $BaselineSquare -BaselineRoot $BaselineRoot -OutputDirectory $OutputDirectory -Python $Python -ElectronExe $ElectronExe -FlutterExe $FlutterExe -PlanOnly:$PlanOnly -SkipBuild:$SkipBuild
