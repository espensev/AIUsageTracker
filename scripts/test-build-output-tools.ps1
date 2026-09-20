# Tests cleanup against an isolated temporary Git repository, never this checkout's outputs.
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot/generated-path-safety.ps1"

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Rejected {
    param([scriptblock]$Action, [string]$ExpectedMessage)
    try { & $Action }
    catch {
        if ($_.Exception.Message -notlike "*$ExpectedMessage*") { throw }
        return
    }
    throw "Expected rejection containing '$ExpectedMessage'."
}

foreach ($script in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1') {
    $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$null, [ref]$parseErrors)
    Assert-True ($parseErrors.Count -eq 0) "PowerShell parse errors in $($script.Name): $parseErrors"
}

$debugExe = & "$PSScriptRoot/resolve-build-output.ps1" -Project AIUsageTracker.Monitor -Configuration Debug -Property ExecutablePath
$releaseExe = & "$PSScriptRoot/resolve-build-output.ps1" -Project AIUsageTracker.Monitor -Configuration Release -Runtime win-x64 -Property ExecutablePath
Assert-True ($debugExe -eq (Join-Path $repoRoot 'artifacts/bin/AIUsageTracker.Monitor/debug/AIUsageTracker.Monitor.exe')) 'Debug apphost did not resolve to its evaluated centralized output.'
Assert-True ($releaseExe -eq (Join-Path $repoRoot 'artifacts/bin/AIUsageTracker.Monitor/release_win-x64/AIUsageTracker.Monitor.exe')) 'Release/runtime apphost did not resolve independently of Debug.'

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$scratchRoot = Join-Path $tempRoot ('AIUsageTracker-build-output-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratchRoot | Out-Null
try {
    & git init --quiet $scratchRoot
    if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize isolated cleanup test repository.' }
    New-Item -ItemType Directory -Path "$scratchRoot/scripts", "$scratchRoot/artifacts/bin", "$scratchRoot/artifacts/obj", "$scratchRoot/dist", "$scratchRoot/Example/bin", "$scratchRoot/user-data" | Out-Null
    Copy-Item -LiteralPath "$PSScriptRoot/clean-build-output.ps1", "$PSScriptRoot/generated-path-safety.ps1", "$PSScriptRoot/resolve-test-assembly.ps1" -Destination "$scratchRoot/scripts"
    Set-Content -LiteralPath "$scratchRoot/Example/Example.csproj" '<Project />'
    & git -C $scratchRoot add Example/Example.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Cannot stage isolated project fixture.' }
    Set-Content -LiteralPath "$scratchRoot/artifacts/bin/app.exe" 'generated'
    Set-Content -LiteralPath "$scratchRoot/artifacts/obj/cache" 'cache'
    Set-Content -LiteralPath "$scratchRoot/Example/bin/legacy.exe" 'legacy'
    Set-Content -LiteralPath "$scratchRoot/user-data/settings.json" 'preserve'
    1..3 | ForEach-Object {
        $packagePath = "$scratchRoot/dist/AIUsageTracker_v1.0.$($_)_win-x64.zip"
        Set-Content -LiteralPath $packagePath 'package'
        (Get-Item -LiteralPath $packagePath).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays($_ - 10)
    }

    & "$scratchRoot/scripts/clean-build-output.ps1" -Deep -IncludeLegacy -IncludePackages -KeepNewestPackages 1 -WhatIf
    Assert-True (Test-Path -LiteralPath "$scratchRoot/artifacts/bin/app.exe") 'WhatIf removed a binary.'
    Assert-True (Test-Path -LiteralPath "$scratchRoot/artifacts/obj/cache") 'WhatIf removed the cache.'
    Assert-True ((Get-ChildItem -LiteralPath "$scratchRoot/dist" -File).Count -eq 3) 'WhatIf removed a package.'

    # A tracked file in any selected output must prevent all deletions.
    Set-Content -LiteralPath "$scratchRoot/Example/bin/tracked.txt" 'tracked'
    & git -C $scratchRoot add Example/bin/tracked.txt
    if ($LASTEXITCODE -ne 0) { throw 'Cannot stage tracked-file guard fixture.' }
    Assert-Rejected { & "$scratchRoot/scripts/clean-build-output.ps1" -IncludeLegacy } 'tracked contents'
    Assert-True (Test-Path -LiteralPath "$scratchRoot/artifacts/bin/app.exe") 'Cleanup deleted another target before preflight completed.'
    & git -C $scratchRoot rm --cached --quiet Example/bin/tracked.txt
    if ($LASTEXITCODE -ne 0) { throw 'Cannot unstage tracked-file guard fixture.' }

    Assert-Rejected { Assert-GeneratedPath -RepositoryRoot $scratchRoot -Path $tempRoot } 'outside the repository'
    Set-Content -LiteralPath "$scratchRoot/artifacts/bin/.git" 'gitdir: elsewhere'
    Assert-Rejected { & "$scratchRoot/scripts/clean-build-output.ps1" } 'containing a repository'
    Remove-Item -LiteralPath "$scratchRoot/artifacts/bin/.git"

    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        $junction = Join-Path $scratchRoot 'artifacts/bin/external'
        New-Item -ItemType Junction -Path $junction -Target "$scratchRoot/user-data" | Out-Null
        Assert-Rejected { & "$scratchRoot/scripts/clean-build-output.ps1" } 'reparse point'
        [IO.Directory]::Delete($junction)
        Assert-True (Test-Path -LiteralPath "$scratchRoot/user-data/settings.json") 'Junction guard affected the target.'
    }

    & "$scratchRoot/scripts/clean-build-output.ps1"
    Assert-True (-not (Test-Path -LiteralPath "$scratchRoot/artifacts/bin")) 'Normal cleanup retained binaries.'
    Assert-True (Test-Path -LiteralPath "$scratchRoot/artifacts/obj/cache") 'Normal cleanup removed restore/intermediate caches.'
    Assert-True (Test-Path -LiteralPath "$scratchRoot/Example/bin/legacy.exe") 'Normal cleanup removed legacy output without an explicit flag.'
    Assert-True ((Get-ChildItem -LiteralPath "$scratchRoot/dist" -File).Count -eq 3) 'Normal cleanup removed final packages.'

    & "$scratchRoot/scripts/clean-build-output.ps1" -Deep -IncludeLegacy -IncludePackages -KeepNewestPackages 1
    Assert-True (-not (Test-Path -LiteralPath "$scratchRoot/artifacts/obj")) 'Deep cleanup retained caches.'
    Assert-True (-not (Test-Path -LiteralPath "$scratchRoot/Example/bin")) 'Legacy cleanup retained project binaries.'
    Assert-True ((Get-ChildItem -LiteralPath "$scratchRoot/dist" -File).Count -eq 1) 'Package retention did not keep exactly one newest package.'
    Assert-True (Test-Path -LiteralPath "$scratchRoot/dist/AIUsageTracker_v1.0.3_win-x64.zip") 'Package retention selected the wrong package.'
    Assert-True (Test-Path -LiteralPath "$scratchRoot/user-data/settings.json") 'Cleanup affected user data.'
    Assert-True (Test-Path -LiteralPath "$scratchRoot/Example/Example.csproj") 'Cleanup affected source.'

    New-Item -ItemType Directory -Path "$scratchRoot/artifacts/bin/FakeTests/debug" -Force | Out-Null
    Set-Content -LiteralPath "$scratchRoot/artifacts/bin/FakeTests/debug/FakeTests.dll" 'assembly'
    $entry = @{ ProjectName = 'FakeTests'; Configuration = 'Debug'; AssemblyName = 'FakeTests.dll'; RelativePath = 'artifacts/bin/FakeTests/debug/FakeTests.dll' }
    ConvertTo-Json -InputObject @($entry) | Set-Content -LiteralPath "$scratchRoot/artifacts/build-output-manifest.json"
    $resolved = & "$scratchRoot/scripts/resolve-test-assembly.ps1" -RootPath $scratchRoot -ProjectName FakeTests -AssemblyName FakeTests.dll
    Assert-True ($resolved -eq (Join-Path $scratchRoot 'artifacts/bin/FakeTests/debug/FakeTests.dll')) 'Downloaded artifact manifest did not resolve the exact assembly.'
    Assert-Rejected { & "$scratchRoot/scripts/resolve-test-assembly.ps1" -RootPath $scratchRoot -ProjectName FakeTests -AssemblyName FakeTests.dll -Configuration Release } 'Expected one manifest entry'
    $entry.RelativePath = '../outside.dll'
    ConvertTo-Json -InputObject @($entry) | Set-Content -LiteralPath "$scratchRoot/artifacts/build-output-manifest.json"
    Assert-Rejected { & "$scratchRoot/scripts/resolve-test-assembly.ps1" -RootPath $scratchRoot -ProjectName FakeTests -AssemblyName FakeTests.dll } 'escapes artifact root'
    $entry.RelativePath = 'user-data/settings.json'
    ConvertTo-Json -InputObject @($entry) | Set-Content -LiteralPath "$scratchRoot/artifacts/build-output-manifest.json"
    Assert-Rejected { & "$scratchRoot/scripts/resolve-test-assembly.ps1" -RootPath $scratchRoot -ProjectName FakeTests -AssemblyName FakeTests.dll } 'does not match requested assembly name'
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        $junction = Join-Path $scratchRoot 'linked-output'
        New-Item -ItemType Junction -Path $junction -Target "$scratchRoot/artifacts/bin/FakeTests/debug" | Out-Null
        $entry.RelativePath = 'linked-output/FakeTests.dll'
        ConvertTo-Json -InputObject @($entry) | Set-Content -LiteralPath "$scratchRoot/artifacts/build-output-manifest.json"
        Assert-Rejected { & "$scratchRoot/scripts/resolve-test-assembly.ps1" -RootPath $scratchRoot -ProjectName FakeTests -AssemblyName FakeTests.dll } 'reparse point'
        [IO.Directory]::Delete($junction)
    }
    Set-Content -LiteralPath "$scratchRoot/dist/AIUsageTracker_v1.0.4[test]_win-x64.zip" 'tracked literal path'
    & git --literal-pathspecs -C $scratchRoot add -- 'dist/AIUsageTracker_v1.0.4[test]_win-x64.zip'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot stage literal-path guard fixture.' }
    Assert-Rejected { Assert-GeneratedPath -RepositoryRoot $scratchRoot -Path "$scratchRoot/dist/AIUsageTracker_v1.0.4[test]_win-x64.zip" } 'tracked contents'
    Write-Host 'Build output tools passed: evaluation, cleanup boundaries, preview, caches, legacy output, package retention and CI manifest.' -ForegroundColor Green
}
finally {
    # Only this newly created, uniquely named fixture tree is eligible for final cleanup.
    $fullScratchPath = [IO.Path]::GetFullPath($scratchRoot)
    $tempPrefix = $tempRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $fullScratchPath.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $fullScratchPath) -notmatch '^AIUsageTracker-build-output-tests-[0-9a-f]{32}$') {
        throw "Refusing to remove unexpected test fixture path: $fullScratchPath"
    }
    $links = @(Get-ChildItem -LiteralPath $fullScratchPath -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
    foreach ($link in $links) {
        if ($link.PSIsContainer) { [IO.Directory]::Delete($link.FullName) }
        else { [IO.File]::Delete($link.FullName) }
    }
    Remove-Item -LiteralPath $fullScratchPath -Recurse -Force
}
