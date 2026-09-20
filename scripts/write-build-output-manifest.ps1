param(
    [Parameter(Mandatory = $true)]
    [string[]]$Projects,
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$rootPrefix = [IO.Path]::GetFullPath($repoRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$entries = foreach ($project in $Projects) {
    $targetPath = & "$PSScriptRoot/resolve-build-output.ps1" -Project $project -Configuration $Configuration
    if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) { continue }
    if (-not $targetPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Cannot export output outside the repository: $targetPath"
    }
    [PSCustomObject]@{
        ProjectName = [IO.Path]::GetFileNameWithoutExtension($project)
        Configuration = $Configuration
        AssemblyName = [IO.Path]::GetFileName($targetPath)
        RelativePath = $targetPath.Substring($rootPrefix.Length).Replace('\', '/')
    }
}
if (-not $entries) { throw "No built outputs found for the manifest." }
$manifestPath = Join-Path $repoRoot "artifacts/build-output-manifest.json"
New-Item -ItemType Directory -Path (Split-Path -Parent $manifestPath) -Force | Out-Null
ConvertTo-Json -InputObject @($entries) | Set-Content -LiteralPath $manifestPath
Write-Host "Wrote build output manifest: $manifestPath"
