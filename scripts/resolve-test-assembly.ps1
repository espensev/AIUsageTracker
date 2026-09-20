param(
    [Parameter(Mandatory = $true)]
    [string]$RootPath,

    [Parameter(Mandatory = $true)]
    [string]$ProjectName,

    [Parameter(Mandatory = $true)]
    [string]$AssemblyName,
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$root = (Resolve-Path -LiteralPath $RootPath).Path
$project = Join-Path $root "$ProjectName/$ProjectName.csproj"
if (Test-Path -LiteralPath $project) {
    $assembly = & "$PSScriptRoot/resolve-build-output.ps1" -Project $project -Configuration $Configuration -RequireExists
    if ([IO.Path]::GetFileName($assembly) -ne $AssemblyName) {
        throw "Evaluated assembly does not match requested name: $assembly"
    }
    $assembly
    return
}

# Downloaded CI artifacts contain evaluated paths, so they need neither source nor MSBuild.
$manifestPath = Join-Path $root "artifacts/build-output-manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Build output manifest not found: $manifestPath" }
$entries = @(Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json | Where-Object {
    $_.ProjectName -eq $ProjectName -and $_.AssemblyName -eq $AssemblyName -and $_.Configuration -eq $Configuration
})
if ($entries.Count -ne 1) { throw "Expected one manifest entry for $ProjectName ($Configuration); found $($entries.Count)." }
$rootPrefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$relativePath = $entries[0].RelativePath
if ([IO.Path]::IsPathRooted($relativePath)) { throw "Manifest output must be relative: $relativePath" }
$assembly = [IO.Path]::GetFullPath((Join-Path $root $relativePath))
if (-not $assembly.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Manifest output escapes artifact root: $relativePath"
}
if ([IO.Path]::GetFileName($assembly) -ne $AssemblyName) {
    throw "Manifest path does not match requested assembly name: $relativePath"
}
if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw "Test assembly not found: $assembly" }
$ancestor = $assembly
while ($ancestor -and $ancestor.Length -ge $root.Length) {
    if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Manifest output traverses a reparse point: $ancestor"
    }
    $ancestor = Split-Path -Parent $ancestor
}
$assembly
