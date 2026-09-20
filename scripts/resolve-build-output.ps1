param(
    [Parameter(Mandatory = $true)]
    [string]$Project,
    [string]$Configuration = "Debug",
    [string]$TargetFramework = "",
    [string]$Runtime = "",
    [ValidateSet("TargetPath", "TargetDir", "ExecutablePath", "ArtifactsPath")]
    [string]$Property = "TargetPath",
    [string]$OutputRoot = "",
    [switch]$RequireExists
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = if ([IO.Path]::IsPathRooted($Project)) { $Project } else { Join-Path $repoRoot $Project }
if (Test-Path -LiteralPath $projectPath -PathType Container) {
    $projectPath = Join-Path $projectPath ((Split-Path -Leaf $projectPath) + ".csproj")
}
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Project not found: $projectPath"
}

$arguments = @(
    "msbuild", $projectPath, "-nologo", "-verbosity:quiet",
    "-p:Configuration=$Configuration",
    "-getProperty:TargetPath,TargetDir,TargetName,UseAppHost,ArtifactsPath,TargetFramework,TargetFrameworks,_NativeExecutableExtension"
)
if ($TargetFramework) { $arguments += "-p:TargetFramework=$TargetFramework" }
if ($Runtime) { $arguments += "-p:RuntimeIdentifier=$Runtime" }
$evaluation = & dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild could not evaluate output paths for $projectPath."
}
$properties = ($evaluation -join [Environment]::NewLine | ConvertFrom-Json).Properties
if (-not $properties.TargetFramework -and $properties.TargetFrameworks) {
    throw "Project has multiple frameworks. Supply -TargetFramework explicitly."
}

if ($Property -eq "ExecutablePath") {
    if ($properties.UseAppHost -ne "true") {
        throw "Project does not produce an executable apphost: $projectPath"
    }
    $result = Join-Path $properties.TargetDir ($properties.TargetName + $properties._NativeExecutableExtension)
} else {
    $result = $properties.$Property
}
if ([string]::IsNullOrWhiteSpace($result)) { throw "MSBuild returned an empty $Property for $projectPath." }
$result = [IO.Path]::GetFullPath($result)

if ($OutputRoot) {
    $rootPrefix = [IO.Path]::GetFullPath($repoRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $result.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Cannot remap an output outside the repository: $result"
    }
    $result = [IO.Path]::GetFullPath((Join-Path $OutputRoot $result.Substring($rootPrefix.Length)))
}
if ($RequireExists -and -not (Test-Path -LiteralPath $result)) {
    throw "Build output not found: $result. Build $Project with configuration $Configuration first."
}
$result
