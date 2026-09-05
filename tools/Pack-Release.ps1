param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$')]
    [string]$Version,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/packages')
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path $output) -and (Get-ChildItem $output -Force | Select-Object -First 1)) {
    throw "Release output must be empty: $output"
}
New-Item -ItemType Directory -Force $output | Out-Null
function Pack-Project([string]$Project, [string]$Platform, [string[]]$ExtraProperties = @()) {
    & dotnet pack $Project -c Release -o $output "-p:Version=$Version" "-p:SquareTargetPlatform=$Platform" @ExtraProperties
    if ($LASTEXITCODE -ne 0) { throw "Failed to pack $Project" }
}

$projects = Get-ChildItem (Join-Path $repo 'src/*/*.csproj') | Where-Object {
    ([xml](Get-Content $_.FullName -Raw)).Project.PropertyGroup.IsPackable -contains 'true'
} | Sort-Object @{ Expression = { if ($_.BaseName -eq 'Square') { 0 } else { 1 } } }, Name
foreach ($project in $projects) {
    $platform = switch ($project.BaseName) {
        'Square.Platform.X11' { 'X11' }
        'Square.Platform.MacOS' { 'MacOS' }
        'Square.Platform.Android' { 'Android' }
        'Square.Backends.AndroidCanvas' { 'Android' }
        default { 'Win32' }
    }
    Pack-Project $project.FullName $platform
}

# Package a version-adjusted template without modifying tracked source files.
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('square-release-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory $scratch | Out-Null
    $content = Join-Path $scratch 'content'
    Copy-Item (Join-Path $repo 'templates/content') $content -Recurse
    $appProject = Join-Path $content 'square/SquareApp.csproj'
    $xml = [xml](Get-Content $appProject -Raw)
    foreach ($reference in $xml.SelectNodes('//PackageReference')) {
        $reference.SetAttribute('Version', $Version)
    }
    $xml.Save($appProject)
    Pack-Project (Join-Path $repo 'templates/Square.Templates.csproj') 'Win32' @("-p:TemplateContentRoot=$content")
} finally {
    Remove-Item $scratch -Recurse -Force
}

# Fail before any upload if a package or internal dependency uses another version.
$metadata = @{}
foreach ($package in Get-ChildItem $output -Filter '*.nupkg') {
    $archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $entry = @($archive.Entries | Where-Object FullName -Like '*.nuspec')
        if ($entry.Count -ne 1) { throw "Expected one nuspec in $($package.Name)" }
        $reader = [IO.StreamReader]::new($entry[0].Open())
        try { $spec = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
        $item = $spec.package.metadata
        if ($item.version -ne $Version) { throw "Unexpected version in $($package.Name): $($item.version)" }
        if ($metadata.ContainsKey([string]$item.id)) { throw "Duplicate package ID: $($item.id)" }
        $metadata[[string]$item.id] = $item
    } finally { $archive.Dispose() }
}
if ($metadata.Count -ne @($projects).Count + 1) { throw 'The release package set is incomplete.' }
foreach ($item in $metadata.Values) {
    foreach ($dependency in $item.SelectNodes('.//*[local-name()="dependency"]')) {
        $id = [string]$dependency.id
        if ($id -eq 'Wuldas.Square' -or $id.StartsWith('Wuldas.Square.')) {
            if (-not $metadata.ContainsKey($id)) { throw "Missing internal dependency: $id" }
            if ($dependency.version -notin @($Version, "[$Version]", "[$Version, )")) {
                throw "Wrong internal dependency version: $($item.id) -> $id $($dependency.version)"
            }
        }
    }
}
Write-Host "Verified $($metadata.Count) release packages at version $Version in $output"
