[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$Deep,
    [switch]$IncludeLegacy,
    [switch]$IncludePackages,
    [ValidateRange(0, 1000)]
    [int]$KeepNewestPackages = 6
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot/generated-path-safety.ps1"

# Normal cleanup retains restore/intermediate caches. Deep cleanup removes them too.
$relativeDirectories = @('artifacts/bin', 'artifacts/publish', 'artifacts/logs', 'artifacts/test-results', 'artifacts/build-output-manifest.json')
if ($Deep) { $relativeDirectories += 'artifacts/obj', 'artifacts/sonar-user-home' }
if ($IncludeLegacy) {
    $relativeDirectories += @('bin', 'obj', 'bin_unlocked', 'bin_unlocked_ui', 'TempBin', 'TestResults')
    $projects = & git -C $repoRoot ls-files -- '*.csproj'
    if ($LASTEXITCODE -ne 0) { throw "Cannot enumerate repository projects." }
    foreach ($project in $projects) {
        $directory = Split-Path -Parent $project
        $relativeDirectories += "$directory/bin", "$directory/obj", "$directory/TestResults"
    }
    $dist = Join-Path $repoRoot 'dist'
    Assert-GeneratedPath -RepositoryRoot $repoRoot -Path $dist
    if (Test-Path -LiteralPath $dist -PathType Container) {
        $relativeDirectories += Get-ChildItem -LiteralPath $dist -Directory -Filter 'publish-*' | ForEach-Object { "dist/$($_.Name)" }
    }
}

$targets = @($relativeDirectories | Sort-Object -Unique | ForEach-Object { Join-Path $repoRoot $_ } | Where-Object { Test-Path -LiteralPath $_ })
if ($IncludePackages) {
    $dist = Join-Path $repoRoot 'dist'
    Assert-GeneratedPath -RepositoryRoot $repoRoot -Path $dist
    if (Test-Path -LiteralPath $dist -PathType Container) {
        $targets += Get-ChildItem -LiteralPath $dist -File | Where-Object {
            $_.Name -match '^AIUsageTracker(?:_Setup)?_v[^\/]+_(?:win|linux|osx)-[^\/]+\.(?:zip|exe)$'
        } | Sort-Object LastWriteTimeUtc -Descending | Select-Object -Skip $KeepNewestPackages | ForEach-Object { $_.FullName }
    }
}

# Preflight every target before deleting any, including in WhatIf mode.
foreach ($target in $targets) { Assert-GeneratedPath -RepositoryRoot $repoRoot -Path $target }
foreach ($target in $targets) {
    if ($PSCmdlet.ShouldProcess($target, 'Remove generated output')) {
        Assert-GeneratedPath -RepositoryRoot $repoRoot -Path $target
        Remove-Item -LiteralPath $target -Recurse -Force
        Write-Host "Removed $target"
    }
}
if ($targets.Count -eq 0) { Write-Host 'No generated output selected for cleanup.' }
