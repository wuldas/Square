param(
    [string]$FlutterExe = "$PSScriptRoot/Flutter/build/windows/x64/runner/Release/square_canvas_flutter.exe",
    [string]$ElectronExe = "$PSScriptRoot/Electron/.runtime/electron.exe",
    [string]$OutputDirectory = '',
    [Alias('ReportVersion')][int]$Version = 0,
    [int[]]$Loads = @(1, 2, 4, 8, 16, 32),
    [int]$Repetitions = 3,
    [double]$Seconds = 5,
    [double]$Warmup = 2,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
function Test-VulkanFrameMeasurement($Measurement) {
    $g = $Measurement.vulkanFrameDiagnostics
    if ($Measurement.measurementVersion -ne 5 -or $null -eq $g -or $null -eq $g.before -or $null -eq $g.atDeadline -or $null -eq $g.afterDrain) { return $false }
    $b = $g.before; $a = $g.atDeadline; $d = $g.afterDrain
    foreach ($value in @($g.observationSeconds, $g.completedFps, $g.warmupDrainMilliseconds, $g.endDrainMilliseconds)) {
        if ($null -eq $value -or [double]::IsNaN([double]$value) -or [double]::IsInfinity([double]$value) -or $value -lt 0) { return $false }
    }
    return ($g.observationSeconds -ge $Measurement.seconds -and [Math]::Abs($g.configuredSeconds - $Measurement.seconds) -lt 0.000001 -and
        $b.PendingFrames -eq 0 -and $b.SubmittedFrames -eq $b.CompletedFrames -and $b.FramesInFlight -eq 2 -and
        $a.FramesInFlight -eq 2 -and $a.PendingFrames -ge 0 -and $a.PendingFrames -le 2 -and
        $a.SubmittedFrames -ge $b.SubmittedFrames -and $a.CompletedFrames -ge $b.CompletedFrames -and
        $a.CompletedFrames -le $a.SubmittedFrames -and $a.PendingFrames -eq ($a.SubmittedFrames - $a.CompletedFrames) -and
        $g.submittedFrames -gt 0 -and $g.submittedFrames -eq ($a.SubmittedFrames - $b.SubmittedFrames) -and
        $g.completedFrames -ge 0 -and $g.completedFrames -eq ($a.CompletedFrames - $b.CompletedFrames) -and $g.completedFrames -le $g.submittedFrames -and
        $g.pendingAtDeadline -eq $a.PendingFrames -and $g.framesInFlight -eq 2 -and
        $g.apiReturnsObserved -ge $Measurement.frames -and $g.apiReturnsObserved -le ($Measurement.frames + 1) -and $g.submittedFrames -le $g.apiReturnsObserved -and
        [Math]::Abs($g.completedFps - $g.completedFrames / $g.observationSeconds) -lt 0.000001 -and
        $d.PendingFrames -eq 0 -and $d.FramesInFlight -eq 2 -and $d.SubmittedFrames -eq $a.SubmittedFrames -and $d.CompletedFrames -eq $d.SubmittedFrames)
}
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
if ($Repetitions -lt 1 -or $Seconds -le 0 -or $Warmup -lt 0 -or !$Loads.Count -or ($Loads | Where-Object { $_ -lt 1 })) {
    throw 'Invalid measurement parameters'
}
if ($Version -eq 0) {
    $searchRoots = @("$repo/artifacts/canvas-benchmark", "$repo/artifacts/canvas-optimization")
    if ($OutputDirectory) { $searchRoots += $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory) }
    $previousVersions = @(foreach ($searchRoot in ($searchRoots | Select-Object -Unique)) {
        if (Test-Path $searchRoot -PathType Container) {
            Get-ChildItem $searchRoot -Recurse -File -Filter 'report-v*.md' | ForEach-Object {
                if ($_.Name -match '^report-v(\d+)\.md$') { [int]$Matches[1] }
            }
        }
    })
    $nextVersion = if ($previousVersions.Count) { [int]($previousVersions | Measure-Object -Maximum).Maximum + 1 } else { 1 }
    $Version = [Math]::Max(5, $nextVersion)
}
if ($Version -lt 1) { throw 'Version must be positive or zero for automatic increment' }
if (!$OutputDirectory) { $OutputDirectory = "artifacts/canvas-benchmark/run-v$Version-$(Get-Date -Format yyyyMMdd-HHmmss)" }
$outputRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if ((Test-Path $outputRoot -PathType Container) -and (Get-ChildItem $outputRoot -File | Select-Object -First 1)) {
    throw "Use a fresh output directory; existing measurements and reports are preserved: $outputRoot"
}
if (!(Test-Path $FlutterExe -PathType Leaf)) { throw "Build Flutter Release first: $FlutterExe" }
if (!(Test-Path $ElectronExe -PathType Leaf)) { throw "Install Electron first: $ElectronExe" }
if (!$SkipBuild) {
    & dotnet build "$PSScriptRoot/Square/CanvasBench.Square.csproj" -c Release --nologo
    if ($LASTEXITCODE) { throw 'Square build failed' }
    & dotnet build "$PSScriptRoot/Avalonia/Avalonia.csproj" -c Release --nologo
    if ($LASTEXITCODE) { throw 'Avalonia build failed' }
}
New-Item -ItemType Directory -Force $outputRoot | Out-Null
$scene = (Resolve-Path "$PSScriptRoot/scene.json").Path
$targets = @(
    @{name='square-software'; exe="$PSScriptRoot/Square/bin/Release/net10.0/CanvasBench.Square.exe"; prefix=@(); suffix=@('--backend','Software')},
    @{name='square-direct2d'; exe="$PSScriptRoot/Square/bin/Release/net10.0/CanvasBench.Square.exe"; prefix=@(); suffix=@('--backend','Direct2D')},
    @{name='square-vulkan'; exe="$PSScriptRoot/Square/bin/Release/net10.0/CanvasBench.Square.exe"; prefix=@(); suffix=@('--backend','Vulkan')},
    @{name='electron'; exe=$ElectronExe; prefix=@('--no-sandbox',"$PSScriptRoot/Electron/main.js"); suffix=@()},
    @{name='flutter'; exe=$FlutterExe; prefix=@(); suffix=@()},
    @{name='avalonia'; exe="$PSScriptRoot/Avalonia/bin/Release/net10.0/CanvasBenchmark.Avalonia.exe"; prefix=@(); suffix=@()}
)
foreach ($target in $targets) {
    if (!(Test-Path $target.exe -PathType Leaf)) { throw "Missing executable: $($target.exe)" }
}
$implementationFiles = @("$PSScriptRoot/Square/Program.cs", "$PSScriptRoot/Avalonia/Program.cs", "$PSScriptRoot/Electron/main.js", "$PSScriptRoot/Electron/renderer.js", "$PSScriptRoot/Flutter/lib/main.dart", "$PSScriptRoot/Flutter/windows/runner/main.cpp", "$repo/src/Square/Backends/RenderContext.cs", "$repo/src/Square.Backends.Direct2D/Direct2DRenderContext.cs", "$PSScriptRoot/Run-Benchmark.ps1", "$PSScriptRoot/CONTRACT.md")
$implementationFiles += (Get-ChildItem "$repo/src/Square.Backends.Vulkan" -File -Filter '*.cs').FullName
$implementationFiles += (Get-ChildItem "$repo/src/Square.Backends.Vulkan/Shaders" -File -Filter '*.glsl').FullName
$environment = [ordered]@{
    started=(Get-Date -Format o); reportVersion=$Version; commit=(& git -C $repo rev-parse HEAD); workingTree=(& git -C $repo status --short | Out-String)
    sceneSha256=(Get-FileHash $scene -Algorithm SHA256).Hash
    iconSha256=(Get-FileHash "$PSScriptRoot/icon.png" -Algorithm SHA256).Hash
    implementationSha256=(Get-FileHash -Algorithm SHA256 -Path $implementationFiles | Select-Object Path,Hash)
    os=(Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber,TotalVisibleMemorySize)
    cpu=(Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors)
    gpu=(Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,CurrentHorizontalResolution,CurrentVerticalResolution,CurrentRefreshRate)
    powerPlan=(& powercfg /getactivescheme | Out-String)
    loads=$Loads; repetitions=$Repetitions; seconds=$Seconds; warmup=$Warmup
    targets=$targets
    renderingEquivalencePolicy='Vulkan requires explicit boolean true in every raw sample plus passed real-GPU feature validation matching these source hashes; false or missing remains limited, including archived v3.'
    implementationNotes='v5 uses two independent Vulkan frame-resource slots with a shared atlas. Root FPS counts fixed-window API returns/queued work, not GPU completion; fence-completed rate uses its own actual observation duration before end drain. Removing queue-idle and changing counters alone is not a speedup claim. v4 baseline remains separate. All six targets are freshly measured; scene/shader/geometry workload is preserved.'
    completionMeasurement='Vulkan warmup drain outside timing, baseline snapshot, new start+seconds deadline, nonblocking deadline poll, then separately timed end drain; no drain completion added to observation FPS.'
    visibility='Match visible child HWND by PID and raise TOPMOST once without activation; no readback or validation override.'
}
$environment | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 "$outputRoot/environment.json"
# Only windows owned by the benchmark child are raised. The topmost window vanishes with that process.
if (!('CanvasBenchmarkWindows' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public sealed class CanvasBenchmarkWindow {
    public string Title;
    public bool Raised;
    public int Error;
}
public static class CanvasBenchmarkWindows {
    private delegate bool Visit(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Visit visit, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    public static CanvasBenchmarkWindow Raise(int pid) {
        CanvasBenchmarkWindow found = null;
        EnumWindows((window, parameter) => {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner != (uint)pid || !IsWindowVisible(window)) return true;
            var text = new StringBuilder(1024);
            GetWindowText(window, text, text.Capacity);
            if (text.Length == 0) return true;
            bool raised = SetWindowPos(window, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
            found = new CanvasBenchmarkWindow { Title=text.ToString(), Raised=raised, Error=raised ? 0 : Marshal.GetLastWin32Error() };
            return false;
        }, IntPtr.Zero);
        return found;
    }
}
'@
}
$manifest = [System.Collections.Generic.List[object]]::new()
$sequence = 0
for ($repeat=1; $repeat -le $Repetitions; $repeat++) {
    for ($loadIndex=0; $loadIndex -lt $Loads.Count; $loadIndex++) {
        $load = $Loads[$loadIndex]
        for ($offset=0; $offset -lt $targets.Count; $offset++) {
            $target = $targets[($offset + $repeat - 1 + $loadIndex) % $targets.Count]
            $sequence++
            $stem = '{0:D3}-r{1}-load{2}-{3}' -f $sequence,$repeat,$load,$target.name
            $resultPath = "$outputRoot/$stem.json"
            if (Test-Path $resultPath) { throw "Refusing to overwrite measurement: $resultPath" }
            $arguments = @($target.prefix) + @('--scene',$scene,'--output',$resultPath,'--load',"$load",'--seconds',$Seconds.ToString([cultureinfo]::InvariantCulture),'--warmup',$Warmup.ToString([cultureinfo]::InvariantCulture)) + @($target.suffix) + @('--label', $target.name)
            $info = [System.Diagnostics.ProcessStartInfo]::new()
            $info.FileName = (Resolve-Path $target.exe).Path
            $info.WorkingDirectory = $repo
            $info.UseShellExecute = $false
            $info.Environment.Remove('ELECTRON_RUN_AS_NODE') | Out-Null
            $info.Environment.Remove('ELECTRON_FORCE_RENDERER_ACCESSIBILITY') | Out-Null
            if ($target.name -eq 'electron') {
                $localTemp = "$PSScriptRoot/Electron/.tmp"
                New-Item -ItemType Directory -Force $localTemp | Out-Null
                $info.Environment['TEMP'] = (Resolve-Path $localTemp).Path
                $info.Environment['TMP'] = $info.Environment['TEMP']
            }
            $info.RedirectStandardOutput = $true
            $info.RedirectStandardError = $true
            foreach ($argument in $arguments) { $info.ArgumentList.Add($argument) }
            Write-Host "[$sequence/$($Loads.Count*$Repetitions*$targets.Count)] $($target.name) load=$load round=$repeat"
            $started = Get-Date -Format o
            $process = [System.Diagnostics.Process]::Start($info)
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            $window = $null
            $windowDeadline = [datetime]::UtcNow.AddSeconds(20)
            while (!$process.HasExited -and [datetime]::UtcNow -lt $windowDeadline) {
                $window = [CanvasBenchmarkWindows]::Raise($process.Id)
                if ($null -ne $window) { break }
                Start-Sleep -Milliseconds 50
            }
            $timeout = [int]([Math]::Max(60,($Seconds+$Warmup)*5)*1000)
            $complete = $process.WaitForExit($timeout)
            if (!$complete) { $process.Kill($true); $process.WaitForExit() }
            $stdout.GetAwaiter().GetResult() | Set-Content -Encoding utf8 "$outputRoot/$stem.stdout.log"
            $stderr.GetAwaiter().GetResult() | Set-Content -Encoding utf8 "$outputRoot/$stem.stderr.log"
            $row = [ordered]@{sequence=$sequence; reportVersion=$Version; repeat=$repeat; load=$load; target=$target.name; started=$started; exitCode=$process.ExitCode; timedOut=(!$complete); result="$stem.json"; observedTitle=if ($window) {$window.Title} else {''}; raised=($null -ne $window -and $window.Raised); raiseError=if ($window) {$window.Error} else {$null}; valid=$false}
            if ($complete -and $process.ExitCode -eq 0 -and $row.raised -and (Test-Path $resultPath)) {
                $result = Get-Content $resultPath -Raw | ConvertFrom-Json
                $row.renderingEquivalent = if ($null -ne $result.PSObject.Properties['renderingEquivalent']) { ($result.renderingEquivalent -is [bool]) -and ($result.renderingEquivalent -eq $true) } else { $target.name -ne 'square-vulkan' }
                $row.renderingLimitations = if ($null -ne $result.renderingLimitations) { @($result.renderingLimitations) } else { @() }
                $row.valid = ($result.frames -gt 0 -and $result.fps -gt 0 -and $result.clientWidth -eq 800 -and $result.clientHeight -eq 600 -and $result.load -eq $load -and [Math]::Abs($result.seconds - $Seconds) -lt 0.0001 -and [Math]::Abs($result.fps - $result.frames / $result.seconds) -lt 0.000001 -and $result.drawTimesMs.Count -eq $result.frames -and $result.frameIntervalsMs.Count -eq $result.frames - 1)
                if ($target.name -eq 'square-vulkan' -and $Version -ge 5) {
                    $row.gpuMeasurementValid = Test-VulkanFrameMeasurement $result
                    $row.valid = $row.valid -and $row.gpuMeasurementValid
                    $row.gpuObservationSeconds = $result.vulkanFrameDiagnostics.observationSeconds
                    $row.gpuCompletedFrames = $result.vulkanFrameDiagnostics.completedFrames
                    $row.gpuCompletedFps = $result.vulkanFrameDiagnostics.completedFps
                    $row.gpuPendingAtDeadline = $result.vulkanFrameDiagnostics.pendingAtDeadline
                    $row.gpuEndDrainMilliseconds = $result.vulkanFrameDiagnostics.endDrainMilliseconds
                }
                if ($row.valid) { Write-Host ('  {0:N2} API/event fps ({1} frames / {2:N3}s); {3}' -f $result.fps,$result.frames,$result.seconds,$row.observedTitle) }
            }
            $manifest.Add($row)
            $manifest | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 "$outputRoot/manifest.json"
            if (!$row.valid) { throw "Benchmark or visible-window check failed; preserved logs and result: $stem" }
            $process.Dispose()
        }
    }
}
Write-Host "Completed $sequence measurements for report-v$Version`: $outputRoot"
