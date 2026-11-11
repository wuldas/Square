param(
    [string]$BaselineDirectory = 'artifacts/canvas-optimization/baseline-square',
    [string]$OutputDirectory = '',
    [int]$Version = 0,
    [int[]]$Loads = @(1,2,4,8,16,32),
    [int]$Repetitions = 3,
    [double]$Seconds = 5,
    [double]$Warmup = 2
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
if ($Version -eq 0) {
    $previousVersions = @(Get-ChildItem "$repo/artifacts/canvas-optimization" -Recurse -File -Filter 'report-v*.md' | ForEach-Object {
        if ($_.Name -match '^report-v(\d+)\.md$') { [int]$Matches[1] }
    })
    $Version = if ($previousVersions.Count) { [int]($previousVersions | Measure-Object -Maximum).Maximum + 1 } else { 1 }
}
if ($Version -lt 1) { throw 'Version must be positive or zero for automatic increment' }
if (!$OutputDirectory) { $OutputDirectory = "artifacts/canvas-optimization/run-v$Version-$(Get-Date -Format yyyyMMdd-HHmmss)" }
$baseline = (Resolve-Path $BaselineDirectory).Path
$optimized = (Resolve-Path "$PSScriptRoot/Square/bin/Release/net10.0").Path
$avalonia = (Resolve-Path "$PSScriptRoot/Avalonia/bin/Release/net10.0/CanvasBenchmark.Avalonia.exe").Path
$scene = (Resolve-Path "$PSScriptRoot/scene.json").Path
$outputRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if ($Repetitions -lt 1 -or $Seconds -le 0 -or $Warmup -lt 0 -or ($Loads | Where-Object {$_ -lt 1})) { throw 'Invalid measurement configuration' }
if ((Test-Path "$outputRoot/manifest.json") -or (Test-Path "$outputRoot/environment.json")) { throw 'Use a fresh output directory; existing measurements are preserved' }
New-Item -ItemType Directory -Force $outputRoot | Out-Null
$targets = @(
    @{name='before-software'; exe="$baseline/CanvasBench.Square.exe"; backend='Software'},
    @{name='after-software'; exe="$optimized/CanvasBench.Square.exe"; backend='Software'},
    @{name='before-direct2d'; exe="$baseline/CanvasBench.Square.exe"; backend='Direct2D'},
    @{name='after-direct2d'; exe="$optimized/CanvasBench.Square.exe"; backend='Direct2D'},
    @{name='avalonia'; exe=$avalonia; backend=''}
)
foreach($target in $targets) { if(!(Test-Path $target.exe -PathType Leaf)) {throw "Missing executable $($target.exe)"} }
$environment = [ordered]@{
    started=(Get-Date -Format o); optimizationVersion=$Version; commit=(& git -C $repo rev-parse HEAD)
    workingTree=(& git -C $repo status --short | Out-String)
    sceneSha256=(Get-FileHash $scene -Algorithm SHA256).Hash
    iconSha256=(Get-FileHash "$PSScriptRoot/icon.png" -Algorithm SHA256).Hash
    benchmarkSha256=(Get-FileHash "$PSScriptRoot/Square/Program.cs" -Algorithm SHA256).Hash
    binaryHashes=(Get-FileHash -Algorithm SHA256 -Path "$baseline/Square.dll","$baseline/Square.Backends.Direct2D.dll","$baseline/CanvasBench.Square.dll","$optimized/Square.dll","$optimized/Square.Backends.Direct2D.dll","$optimized/CanvasBench.Square.dll" | Select-Object Path,Hash)
    implementationHashes=(Get-FileHash -Algorithm SHA256 -Path "$repo/src/Square/Backends/RenderContext.cs","$repo/src/Square.Backends.Direct2D/Direct2DRenderContext.cs" | Select-Object Path,Hash)
    os=(Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber)
    cpu=(Get-CimInstance Win32_Processor | Select-Object Name,NumberOfLogicalProcessors)
    gpu=(Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,CurrentRefreshRate)
    loads=$Loads; repetitions=$Repetitions; seconds=$Seconds; warmup=$Warmup; targets=$targets
}
$environment | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 "$outputRoot/environment.json"
$manifest = [System.Collections.Generic.List[object]]::new()
$sequence=0
for($repeat=1;$repeat -le $Repetitions;$repeat++) {
    for($li=0;$li -lt $Loads.Count;$li++) {
        $load=$Loads[$li]
        for($offset=0;$offset -lt $targets.Count;$offset++) {
            $target=$targets[($repeat-1+$li+$offset)%$targets.Count]
            $sequence++
            $stem='{0:D3}-r{1}-load{2}-{3}' -f $sequence,$repeat,$load,$target.name
            $output="$outputRoot/$stem.json"
            if(Test-Path $output) {throw "Refusing to replace existing sample $output"}
            $info=[System.Diagnostics.ProcessStartInfo]::new()
            $info.FileName=$target.exe; $info.WorkingDirectory=$repo; $info.UseShellExecute=$false
            $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
            $arguments=@('--scene',$scene,'--output',$output,'--load',"$load",'--seconds',$Seconds.ToString([cultureinfo]::InvariantCulture),'--warmup',$Warmup.ToString([cultureinfo]::InvariantCulture))
            $arguments+=@('--label',$target.name)
            if($target.backend) {$arguments+=@('--backend',$target.backend)}
            foreach($argument in $arguments) {$info.ArgumentList.Add($argument)}
            Write-Host "[$sequence/$($Repetitions*$Loads.Count*$targets.Count)] $($target.name) load=$load round=$repeat"
            $started=Get-Date -Format o
            $process=[System.Diagnostics.Process]::Start($info)
            $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
            $complete=$process.WaitForExit([int]([Math]::Max(60,($Seconds+$Warmup)*5)*1000))
            if(!$complete) {$process.Kill($true);$process.WaitForExit()}
            $stdout.GetAwaiter().GetResult() | Set-Content -Encoding utf8 "$outputRoot/$stem.stdout.log"
            $stderr.GetAwaiter().GetResult() | Set-Content -Encoding utf8 "$outputRoot/$stem.stderr.log"
            $row=[ordered]@{sequence=$sequence;repeat=$repeat;load=$load;target=$target.name;started=$started;exitCode=$process.ExitCode;timedOut=(!$complete);result="$stem.json";valid=$false}
            if($complete -and $process.ExitCode -eq 0 -and (Test-Path $output)) {
                $r=Get-Content $output -Raw | ConvertFrom-Json
                $row.valid=($r.frames -gt 0 -and $r.fps -gt 0 -and $r.clientWidth -eq 800 -and $r.clientHeight -eq 600 -and $r.devicePixelRatio -eq 1 -and $r.load -eq $load -and [Math]::Abs($r.seconds-$Seconds) -lt 0.0001 -and [Math]::Abs($r.fps-$r.frames/$r.seconds) -lt 0.000001 -and $r.drawTimesMs.Count -eq $r.frames -and $r.frameIntervalsMs.Count -eq $r.frames-1)
                if($row.valid) {Write-Host ('  {0:N2} fps' -f $r.fps)}
            }
            $manifest.Add($row)
            $manifest | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 "$outputRoot/manifest.json"
            if(!$row.valid) {throw "Invalid sample preserved: $stem"}
        }
    }
}
Write-Host "Completed $sequence before/after measurements: $outputRoot"
