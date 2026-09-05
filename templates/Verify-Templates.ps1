param(
    [Parameter(Mandatory)][ValidateSet('Win32', 'X11', 'MacOS', 'Android')][string]$Platform,
    [string]$WorkDirectory = (Join-Path ([IO.Path]::GetTempPath()) ("square-templates-" + [guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$work = [IO.Path]::GetFullPath($WorkDirectory)
if ($work.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Template validation must run outside the repository.'
}
New-Item -ItemType Directory -Force $work | Out-Null
$feed = Join-Path $work 'feed'
$hive = Join-Path $work 'hive'
New-Item -ItemType Directory -Force $feed | Out-Null
function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}
function Pack([string]$Project, [string]$Target) {
    Invoke-DotNet @('pack', (Join-Path $repo $Project), '-c', 'Release', '-o', $feed, "-p:SquareTargetPlatform=$Target")
}
$desktop = if ($IsWindows) { 'Win32' } elseif ($IsMacOS) { 'MacOS' } else { 'X11' }
$target = if ($Platform -eq 'Android') { $desktop } else { $Platform }
Pack 'src/Square/Square.csproj' $target
Pack 'src/Square.Compiler/Square.Compiler.csproj' $target
Pack "src/Square.Platform.$target/Square.Platform.$target.csproj" $target
if ($Platform -eq 'Android') {
    Pack 'src/Square.Backends.Vulkan/Square.Backends.Vulkan.csproj' 'Android'
    Pack 'src/Square.Backends.AndroidCanvas/Square.Backends.AndroidCanvas.csproj' 'Android'
    Pack 'src/Square.Platform.Android/Square.Platform.Android.csproj' 'Android'
}
Pack 'templates/Square.Templates.csproj' $target

$oldPackages = $env:NUGET_PACKAGES
$oldHome = $env:DOTNET_CLI_HOME
try {
    $env:NUGET_PACKAGES = Join-Path $work 'packages'
    $env:DOTNET_CLI_HOME = Join-Path $work 'dotnet-home'
    $escapedFeed = [Security.SecurityElement]::Escape($feed)
    @"
<configuration>
  <packageSources><clear /><add key="local" value="$escapedFeed" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="Wuldas.Square" /><package pattern="Wuldas.Square.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content (Join-Path $work 'NuGet.Config')
    Invoke-DotNet @('new', 'install', (Join-Path $feed 'Wuldas.Square.Templates.0.1.0.nupkg'), '--debug:custom-hive', $hive)
    foreach ($markup in @('sqv', 'sqx')) {
        $name = if ($markup -eq 'sqv') { 'TemplateSqv' } else { 'TemplateSqx' }
        $app = Join-Path $work $name
        $platforms = if ($Platform -eq 'Android') { 'desktop,android' } else { 'desktop' }
        Invoke-DotNet @('new', 'square', '-n', $name, '-o', $app, '--markup', $markup, '--platforms', $platforms, '--debug:custom-hive', $hive)
        Invoke-DotNet @('new', 'square-component', '-n', 'UserCard', '-o', (Join-Path $app 'Components'), '--namespace', "$name.Components", '--markup', $markup, '--debug:custom-hive', $hive)
        $project = Join-Path $app "$name.csproj"
        # The combined project must still restore/build its desktop target without selecting Android.
        Invoke-DotNet @('build', $project, '-c', 'Release', "-p:SquareTargetPlatform=$target")
        if ($Platform -eq 'Android') {
            Invoke-DotNet @('build', $project, '-c', 'Debug', '-r', 'android-x64', '--self-contained', 'false', '-p:SquareTargetPlatform=Android')
            continue
        }
        New-Item -ItemType Directory -Force (Join-Path $app 'Assets'), (Join-Path $app 'Public/Styles') | Out-Null
        'square-template-asset' | Set-Content (Join-Path $app 'Assets/template-probe.txt')
        '.template-probe { color: #123456; }' | Set-Content (Join-Path $app 'Public/Styles/template-probe.css')

        # A separate consumer exercises the unmodified generated app's factory and actual native host.
        $smoke = Join-Path $work "$name.Smoke"
        New-Item -ItemType Directory -Force $smoke | Out-Null
        (Get-Content (Join-Path $PSScriptRoot 'TemplateSmoke.cs') -Raw).Replace('TemplateApp', $name) | Set-Content (Join-Path $smoke 'Program.cs')
        $escapedProject = [Security.SecurityElement]::Escape($project)
        @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><ProjectReference Include="$escapedProject" /></ItemGroup>
</Project>
"@ | Set-Content (Join-Path $smoke 'Smoke.csproj')
        $rid = switch ($target) { 'Win32' { 'win-x64' } 'X11' { 'linux-x64' } 'MacOS' { 'osx-x64' } }
        $output = Join-Path $smoke 'publish'
        Invoke-DotNet @('publish', (Join-Path $smoke 'Smoke.csproj'), '-c', 'Release', '-r', $rid, '--self-contained', 'true', '-p:PublishAot=true', "-p:SquareTargetPlatform=$target", '-o', $output)
        $executable = Join-Path $output $(if ($IsWindows) { 'Smoke.exe' } else { 'Smoke' })
        $screenshot = Join-Path $work "$name.png"
        $process = if ($target -eq 'X11') {
            Start-Process 'xvfb-run' -ArgumentList @('-a', "`"$executable`"", "`"$screenshot`"") -PassThru -NoNewWindow
        } else {
            Start-Process $executable -ArgumentList "`"$screenshot`"" -PassThru -NoNewWindow
        }
        if (-not $process.WaitForExit(60000)) {
            $process.Kill($true)
            throw 'Generated app interaction smoke timed out.'
        }
        if ($process.ExitCode -ne 0) { throw "Generated app interaction smoke failed: $($process.ExitCode)" }
        $appPublish = Join-Path $work "$name.Publish"
        Invoke-DotNet @('publish', $project, '-c', 'Release', '-r', $rid, '--self-contained', 'true', '-p:PublishAot=true', "-p:SquareTargetPlatform=$target", '-o', $appPublish)
        if (-not (Test-Path (Join-Path $appPublish 'Styles/template-probe.css'))) { throw 'Public stylesheet was not published.' }
        if (-not (Test-Path $screenshot) -or (Get-Item $screenshot).Length -eq 0) { throw 'Generated app screenshot is missing.' }
    }
    Write-Host "Template package, isolated restore, component compilation and $Platform verification passed: $work"
} finally {
    $env:NUGET_PACKAGES = $oldPackages
    $env:DOTNET_CLI_HOME = $oldHome
}
