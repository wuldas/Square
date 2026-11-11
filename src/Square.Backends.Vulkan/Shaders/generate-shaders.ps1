param(
    [string]$Compiler = 'glslangValidator',
    [string]$Validator = 'spirv-val'
)
$ErrorActionPreference = 'Stop'
$buildRoot = Join-Path $PSScriptRoot '.build'
New-Item -ItemType Directory -Force $buildRoot | Out-Null
$builder = [System.Text.StringBuilder]::new()
[void]$builder.AppendLine('namespace Square.Backends.Vulkan;')
[void]$builder.AppendLine('')
[void]$builder.AppendLine('/// <summary>')
[void]$builder.AppendLine('/// Embedded SPIR-V for Vulkan 1.1, generated from Shaders/*.glsl; do not edit by hand.')
[void]$builder.AppendLine('/// Regenerate: pwsh -File src/Square.Backends.Vulkan/Shaders/generate-shaders.ps1')
[void]$builder.AppendLine('/// Tools: glslangValidator -V --target-env vulkan1.1; spirv-val --target-env vulkan1.1.')
[void]$builder.AppendLine('/// </summary>')
[void]$builder.AppendLine('internal static class EmbeddedShaders')
[void]$builder.AppendLine('{')
foreach ($shader in @(@{Source='vert2d.glsl'; Stage='vert'; Name='Vertex2D'}, @{Source='frag2d.glsl'; Stage='frag'; Name='Fragment2D'})) {
    $inputPath = Join-Path $PSScriptRoot $shader.Source
    $binaryPath = Join-Path $buildRoot ($shader.Stage + '.spv')
    & $Compiler -V --target-env vulkan1.1 -S $shader.Stage $inputPath -o $binaryPath
    if ($LASTEXITCODE -ne 0) { throw "Shader compilation failed: $inputPath" }
    & $Validator --target-env vulkan1.1 $binaryPath
    if ($LASTEXITCODE -ne 0) { throw "SPIR-V validation failed: $binaryPath" }
    $bytes = [System.IO.File]::ReadAllBytes($binaryPath)
    [void]$builder.AppendLine("    public static readonly byte[] $($shader.Name) =")
    [void]$builder.AppendLine('    [')
    for ($offset = 0; $offset -lt $bytes.Length; $offset += 16) {
        $last = [Math]::Min($offset + 15, $bytes.Length - 1)
        $line = ($bytes[$offset..$last] | ForEach-Object { '0x{0:X2}' -f $_ }) -join ', '
        $comma = if ($last -lt $bytes.Length - 1) { ',' } else { '' }
        [void]$builder.AppendLine("        $line$comma")
    }
    [void]$builder.AppendLine('    ];')
    [void]$builder.AppendLine('')
    Write-Output "$($shader.Name): $($bytes.Length) validated SPIR-V bytes"
}
[void]$builder.AppendLine('}')
$outputPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'EmbeddedShaders.cs'
[System.IO.File]::WriteAllText($outputPath, $builder.ToString(), [System.Text.UTF8Encoding]::new($false))
Write-Output "Generated $outputPath"
