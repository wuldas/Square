[CmdletBinding()]
param(
    [ValidateSet('Allocation', 'HeapSnapshot', 'NativeHeap')]
    [string]$Mode = 'Allocation',
    [int]$ProcessId = 0,
    [string]$PerfViewPath = $env:PERFVIEW_PATH,
    [string]$OutputDirectory = '',
    [ValidateRange(1, 600)]
    [int]$DurationSeconds = 30,
    [ValidateRange(30, 1800)]
    [int]$CompletionTimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))

if ([string]::IsNullOrWhiteSpace($PerfViewPath)) {
    $localPerfView = 'C:\quick\PerfView\PerfView.exe'
    if (Test-Path -LiteralPath $localPerfView) {
        $PerfViewPath = $localPerfView
    } else {
        $command = Get-Command 'PerfView.exe' -ErrorAction SilentlyContinue
        if ($null -ne $command) { $PerfViewPath = $command.Source }
    }
}
if ([string]::IsNullOrWhiteSpace($PerfViewPath) -or -not (Test-Path -LiteralPath $PerfViewPath)) {
    throw 'PerfView.exe was not found. Pass -PerfViewPath or set PERFVIEW_PATH.'
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot 'artifacts\perfview'
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$prefix = Join-Path $OutputDirectory "directwrite-$($Mode.ToLowerInvariant())-$timestamp"
$common = @('/AcceptEula', '/NoView', "/LogFile:$prefix.log")
$expectedOutput = $null

Push-Location $projectRoot
try {
    switch ($Mode) {
        'Allocation' {
            $expectedOutput = "$prefix.etl"
            $arguments = $common + @(
                "/DataFile:$prefix.etl",
                '/DotNetAllocSampled',
                '/NoNGenRundown',
                '/RundownTimeout:15',
                "/MaxCollectSec:$DurationSeconds",
                'run',
                'dotnet',
                'test',
                'tests\Square.Backends.Conformance.Tests\Square.Backends.Conformance.Tests.csproj',
                '-c', 'Release',
                '-p:SquareTargetPlatform=Win32',
                '--filter', 'Category=RealDirect2D',
                '-e', 'SQUARE_RUN_REAL_DIRECT2D_CONFORMANCE=1')
            & $PerfViewPath @arguments
        }
        'HeapSnapshot' {
            if ($ProcessId -le 0) { throw '-ProcessId must be provided for HeapSnapshot.' }
            $expectedOutput = "$prefix.gcDump"
            & $PerfViewPath @common 'HeapSnapshot' $ProcessId "$prefix.gcDump"
        }
        'NativeHeap' {
            if ($ProcessId -le 0) { throw '-ProcessId must be provided for NativeHeap.' }
            $expectedOutput = "$prefix.etl"
            $arguments = $common + @(
                "/DataFile:$prefix.etl",
                "/Process:$ProcessId",
                "/OSHeapProcess:$ProcessId",
                "/MaxCollectSec:$DurationSeconds",
                '/NoNGenRundown',
                'collect')
            & $PerfViewPath @arguments
        }
    }
    if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "PerfView exited with code $LASTEXITCODE."
    }
    $deadline = [DateTime]::UtcNow.AddSeconds($CompletionTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath "$prefix.log") {
            $log = Get-Content -LiteralPath "$prefix.log" -Raw
            if ($log -match '\[DONE .* SUCCESS:') { break }
            if ($log -match '\[DONE .* FAIL:') {
                throw "PerfView failed. Check '$prefix.log'."
            }
        }
        Start-Sleep -Milliseconds 500
    }
    if ([DateTime]::UtcNow -ge $deadline) {
        throw "Timed out waiting for PerfView. Check '$prefix.log'."
    }
    if ($null -ne $expectedOutput -and -not (Test-Path -LiteralPath $expectedOutput)) {
        throw "PerfView did not create '$expectedOutput'. Check '$prefix.log'; heap/native modes require an elevated PowerShell."
    }
    "PerfView output prefix: $prefix"
} finally {
    Pop-Location
}
