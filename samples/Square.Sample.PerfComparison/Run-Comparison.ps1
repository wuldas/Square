#requires -Version 7.0
[CmdletBinding()]
param(
    [int]$Seconds = 30,
    [double]$WarmupSeconds = 2,
    [string]$FlutterCommand = "flutter",
    [string]$OutputDirectory,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
$buildDirectory = Join-Path $repoRoot "artifacts/perf-comparison-builds"
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot ("artifacts/perf-comparison-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null

function Invoke-Checked {
    param([string]$Command, [string[]]$Arguments)
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command exited with code $LASTEXITCODE" }
}

if (-not $SkipBuild) {
    foreach ($target in @(
        @{ Project = "Square/Square.PerfComparison.csproj"; Directory = "square-jit" },
        @{ Project = "Avalonia/Avalonia.csproj"; Directory = "avalonia-jit" }
    )) {
        $publishDirectory = Join-Path $buildDirectory $target.Directory
        if ([IO.Directory]::Exists($publishDirectory)) { [IO.Directory]::Delete($publishDirectory, $true) }
        Invoke-Checked "dotnet" @("publish", (Join-Path $PSScriptRoot $target.Project), "--configuration", "Release", "--runtime", "win-x64", "--self-contained", "true", "-p:SquareTargetPlatform=Win32", "-p:DebugType=None", "-p:DebugSymbols=false", "--output", (Join-Path $buildDirectory $target.Directory))
    }
    foreach ($target in @(
        @{ Project = "Square/Square.PerfComparison.csproj"; Directory = "square-nativeaot" },
        @{ Project = "Avalonia/Avalonia.csproj"; Directory = "avalonia-nativeaot" }
    )) {
        $nativeDirectory = Join-Path $buildDirectory $target.Directory
        if ([IO.Directory]::Exists($nativeDirectory)) { [IO.Directory]::Delete($nativeDirectory, $true) }
        Invoke-Checked "dotnet" @("publish", (Join-Path $PSScriptRoot $target.Project), "--configuration", "Release", "--runtime", "win-x64", "--self-contained", "true", "-p:SquareTargetPlatform=Win32", "-p:SquarePerfPublishAot=true", "-p:DebugType=None", "-p:DebugSymbols=false", "--output", $nativeDirectory)
    }
    Push-Location (Join-Path $PSScriptRoot "Electron")
    try {
        Invoke-Checked "npm.cmd" @("ci")
        Invoke-Checked "npm.cmd" @("run", "package")
    }
    finally { Pop-Location }
    Push-Location (Join-Path $PSScriptRoot "Flutter")
    try { Invoke-Checked $FlutterCommand @("build", "windows", "--release") }
    finally { Pop-Location }
}

function Invoke-Benchmark {
    param([string]$Name, [string]$Executable, [string[]]$Arguments, [string]$PublishedDirectory, [string]$Deployment)
    $resultPath = Join-Path $OutputDirectory "$Name.json"
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.WorkingDirectory = [IO.Path]::GetDirectoryName($Executable)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in ($Arguments + @("--seconds", "$Seconds", "--warmup", "$WarmupSeconds", "--output", $resultPath))) {
        $start.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    Write-Host "Measuring $Name ($WarmupSeconds s warmup + $Seconds s measurement)..."
    try {
        if (-not $process.Start()) { throw "Could not start $Executable" }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit([int](($Seconds + $WarmupSeconds + 120) * 1000))) {
            $process.Kill($true)
            throw "$Name did not finish its measurement"
        }
        [IO.File]::WriteAllText((Join-Path $OutputDirectory "$Name.stdout.log"), $stdout.GetAwaiter().GetResult())
        $errorText = $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $OutputDirectory "$Name.stderr.log"), $errorText)
        if ($process.ExitCode -ne 0) { throw "$Name exited with code $($process.ExitCode): $errorText" }
        if (-not [IO.File]::Exists($resultPath)) { throw "$Name did not write $resultPath" }
        $result = [IO.File]::ReadAllText($resultPath) | ConvertFrom-Json
        if ($result.frames -le 0 -or $result.seconds -le 0) { throw "$Name did not measure animation frames" }
        if ([Math]::Abs($result.clientWidth - 900) -gt 0.5 -or [Math]::Abs($result.clientHeight - 1240) -gt 0.5) {
            throw "$Name viewport differs from 900x1240: $($result.clientWidth)x$($result.clientHeight)"
        }
        $result | Add-Member -NotePropertyName cpuMsPerFrame -NotePropertyValue ($result.cpuSeconds * 1000 / $result.frames)
        $result | Add-Member -NotePropertyName cpuWholeMachinePercent -NotePropertyValue ($result.cpuOneCorePercent / [Environment]::ProcessorCount)
        $publishedBytes = 0L
        $publishedFiles = 0
        foreach ($file in [IO.Directory]::EnumerateFiles($PublishedDirectory, "*", [IO.SearchOption]::AllDirectories)) {
            if ([IO.Path]::GetExtension($file) -in @(".pdb", ".dbg")) { continue }
            $publishedBytes += [IO.FileInfo]::new($file).Length
            $publishedFiles++
        }
        $entrypointBytes = [IO.FileInfo]::new($Executable).Length
        $result | Add-Member -NotePropertyName publishedBytes -NotePropertyValue $publishedBytes
        $result | Add-Member -NotePropertyName publishedMiB -NotePropertyValue ($publishedBytes / 1MB)
        $result | Add-Member -NotePropertyName publishedFileCount -NotePropertyValue $publishedFiles
        $result | Add-Member -NotePropertyName entrypointBytes -NotePropertyValue $entrypointBytes
        $result | Add-Member -NotePropertyName entrypointMiB -NotePropertyValue ($entrypointBytes / 1MB)
        $result | Add-Member -NotePropertyName publishedDirectory -NotePropertyValue ([IO.Path]::GetRelativePath($repoRoot, $PublishedDirectory))
        $result | Add-Member -NotePropertyName deployment -NotePropertyValue $Deployment
        return $result
    }
    finally { $process.Dispose() }
}

$squareJitDirectory = Join-Path $buildDirectory "square-jit"
$squareAotDirectory = Join-Path $buildDirectory "square-nativeaot"
$avaloniaDirectory = Join-Path $buildDirectory "avalonia-jit"
$avaloniaAotDirectory = Join-Path $buildDirectory "avalonia-nativeaot"
$electronDirectory = Join-Path $PSScriptRoot "Electron/dist/square-electron-perf-win32-x64"
$flutterDirectory = Join-Path $PSScriptRoot "Flutter/build/windows/x64/runner/Release"
$squareJit = Join-Path $squareJitDirectory "Square.PerfComparison.exe"
$squareAot = Join-Path $squareAotDirectory "Square.PerfComparison.exe"
$avalonia = Join-Path $avaloniaDirectory "Square.PerfComparison.Avalonia.exe"
$avaloniaAot = Join-Path $avaloniaAotDirectory "Square.PerfComparison.Avalonia.exe"
$electron = Join-Path $electronDirectory "square-electron-perf.exe"
$flutter = Join-Path $flutterDirectory "square_flutter_perf.exe"
$targets = @(
    @{ Name = "square-software-jit"; Exe = $squareJit; Args = @("--backend", "Software"); Directory = $squareJitDirectory; Deployment = "self-contained .NET JIT" },
    @{ Name = "square-direct2d-jit"; Exe = $squareJit; Args = @("--backend", "Direct2D"); Directory = $squareJitDirectory; Deployment = "self-contained .NET JIT" },
    @{ Name = "square-software-nativeaot"; Exe = $squareAot; Args = @("--backend", "Software"); Directory = $squareAotDirectory; Deployment = "NativeAOT" },
    @{ Name = "square-direct2d-nativeaot"; Exe = $squareAot; Args = @("--backend", "Direct2D"); Directory = $squareAotDirectory; Deployment = "NativeAOT" },
    @{ Name = "avalonia-jit"; Exe = $avalonia; Args = @(); Directory = $avaloniaDirectory; Deployment = "self-contained .NET JIT" },
    @{ Name = "avalonia-nativeaot"; Exe = $avaloniaAot; Args = @(); Directory = $avaloniaAotDirectory; Deployment = "NativeAOT" },
    @{ Name = "electron-release"; Exe = $electron; Args = @(); Directory = $electronDirectory; Deployment = "packaged Electron" },
    @{ Name = "flutter-release"; Exe = $flutter; Args = @(); Directory = $flutterDirectory; Deployment = "Flutter release" }
)

$results = [Collections.Generic.List[object]]::new()
foreach ($target in $targets) {
    $results.Add((Invoke-Benchmark $target.Name $target.Exe $target.Args $target.Directory $target.Deployment))
    Start-Sleep -Seconds 2
}

$os = Get-CimInstance Win32_OperatingSystem
$processor = Get-CimInstance Win32_Processor | Select-Object -First 1
$displays = @(Get-CimInstance Win32_VideoController | Where-Object CurrentRefreshRate | Select-Object Name, CurrentRefreshRate, CurrentHorizontalResolution, CurrentVerticalResolution)
$summary = [ordered]@{
    measuredUtc = [DateTimeOffset]::UtcNow.ToString("O")
    environment = [ordered]@{
        os = $os.Caption
        osVersion = $os.Version
        cpu = $processor.Name
        logicalProcessors = [Environment]::ProcessorCount
        displays = $displays
        dotnetSdk = (& dotnet --version | Out-String).Trim()
        flutterSdk = (& $FlutterCommand --version --machine | ConvertFrom-Json | Select-Object frameworkVersion, channel, frameworkRevision, engineRevision, dartSdkVersion)
        electronPackage = [IO.File]::ReadAllText((Join-Path $PSScriptRoot "Electron/package.json")) | ConvertFrom-Json | Select-Object name, version, devDependencies
    }
    workload = "900x1240 client viewport; 3 groups of 240 boxes; transform, opacity and width; 6s linear loops; target 60 FPS"
    secondsPerTrial = $Seconds
    warmupSeconds = $WarmupSeconds
    rounds = 1
    order = @($targets | ForEach-Object Name)
    cpuDefinition = "CPU seconds / measured wall seconds * 100; 100% is one logical processor. Electron includes its associated browser/renderer/GPU/utility processes."
    memoryDefinition = "Private committed and resident working-set bytes sampled at about 1 Hz, reported as mean/observed peak MiB. Dedicated GPU VRAM is excluded; summed Electron working sets can count shared pages more than once."
    binarySizeDefinition = "Uncompressed runnable Windows x64 published directory, including bundled runtimes and resources, excluding .pdb/.dbg debug symbols. .NET JIT targets are self-contained. Entrypoint EXE bytes are recorded separately."
    results = $results.ToArray()
    limitations = @(
        "One sequential trial per target, not a statistically established ranking; order, background activity, JIT and thermal state can affect results.",
        "Square counts render submissions, Avalonia and Electron count animation callbacks, Flutter counts engine frame timings. None is a physical scanout counter; consult fpsDefinition per row.",
        "All scored processes use Release builds, and all publishing/compilation is completed before measuring. No debug or DevTools frontend is attached.",
        "Square uses Auto dirty-region rendering. The Direct2D target requires a hardware device. Framework-native layout/compositing optimizations are not disabled.",
        "Electron process metrics are sampled, so CPU spent after the last observation of a process that exits between samples can be missed."
    )
}
$summaryPath = Join-Path $OutputDirectory "results.json"
[IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json -Depth 12) + [Environment]::NewLine)
$results | Select-Object target, fps, cpuOneCorePercent, privateAvgMiB, workingAvgMiB, publishedMiB | Format-Table -AutoSize
Write-Host "Results: $summaryPath"
