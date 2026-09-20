# Build and cleanup

Build and test commands are unchanged:

```powershell
dotnet build AIUsageTracker.sln --configuration Debug
dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj --configuration Debug
dotnet run --project AIUsageTracker.UI.Slim
```

All projects put binaries and intermediate files under `artifacts/`, split by project and build configuration/runtime. Tests write results under `artifacts/test-results/<project>`. Do not create alternate `bin_unlocked` or `TempBin` trees to work around running processes.

To resolve a build output without guessing its path:

```powershell
pwsh -File scripts/resolve-build-output.ps1 -Project AIUsageTracker.UI.Slim -Property ExecutablePath -RequireExists
```

Pass `-Configuration Release` or `-Runtime win-x64` when resolving those builds. `TargetPath` (the default property) resolves the assembly; `ExecutablePath` resolves the application host. Missing builds fail explicitly. CI exports evaluated paths in `artifacts/build-output-manifest.json` so jobs downloading test binaries do not need to search for matching filenames.

The core test artifact also includes the footer XAML and repository inputs required by source guardrails and appcast checks under `TestData/`. These checks use the packaged inputs without searching for a checkout. The test runner terminates only its own process tree on timeout; it does not sweep other test sessions. `run-slim-and-monitor.ps1 -Action status` and `-Action stop` do not require SDK discovery.

## Isolated Web test data

Web view checks need the recorded provider fixture. Seed a new disposable directory by passing its absolute path as the Seeder's second argument, then set `LOCALAPPDATA` to that directory only for the test process. Web honors this override on Windows; other application components retain their existing data-path behavior.

```powershell
dotnet build scripts/Seeder/Seeder.csproj --configuration Debug
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('tracker-web-tests-' + [Guid]::NewGuid().ToString('N'))
$seeder = & scripts/resolve-build-output.ps1 -Project scripts/Seeder/Seeder.csproj -RequireExists
dotnet $seeder test-fixtures/provider-data.json $fixtureRoot
if ($LASTEXITCODE -ne 0) { throw 'Fixture seeding failed.' }
$previousLocalAppData = $env:LOCALAPPDATA
try {
    $env:LOCALAPPDATA = $fixtureRoot
    dotnet test AIUsageTracker.Web.Tests/AIUsageTracker.Web.Tests.csproj --configuration Debug --filter 'FullyQualifiedName!~ScreenshotTests'
} finally {
    $env:LOCALAPPDATA = $previousLocalAppData
}
```

Seeder replaces `AIUsageTracker/usage.db` beneath its selected root. Keep fixture roots separate from an installed application's data directory.

## Cleanup

Preview before deleting generated files:

```powershell
pwsh -File scripts/clean-build-output.ps1 -WhatIf
pwsh -File scripts/clean-build-output.ps1
```

Normal cleanup removes compiled binaries, publish staging, build logs, and test results, while keeping restore/intermediate caches. Optional switches:

| Switch | Removes |
|---|---|
| `-Deep` | Also `artifacts/obj` |
| `-IncludeLegacy` | Old project bin/obj/TestResults, root bin/obj and temporary build folders, and old dist/publish-* staging |
| `-IncludePackages -KeepNewestPackages 6` | Older matching final packages in dist, retaining the six most recently written files across versions/runtimes |

For the one-time migration from project-local output folders:

```powershell
pwsh -File scripts/clean-build-output.ps1 -IncludeLegacy -WhatIf
pwsh -File scripts/clean-build-output.ps1 -IncludeLegacy
dotnet build AIUsageTracker.sln --configuration Debug
```

Cleanup checks all targets before deleting anything, rejects tracked content, nested repositories and reparse points, and keeps paths within the repository. It does not terminate processes; close the relevant app if Windows reports a locked output. It does not touch runtime settings or databases, provider fixtures, committed screenshots, or other worktrees. `artifacts/screenshots/` review images are retained.

## Packaging and screenshots

`scripts/publish-app.ps1 -Runtime win-x64` stages the four application components in `artifacts/publish/win-x64/` and places finished ZIPs and installers in `dist/`. The installed layout and package filenames are unchanged. Inno Setup is needed to finish the Windows installer.

Generate review screenshots without overwriting committed baselines:

```powershell
dotnet run --project AIUsageTracker.UI.Slim --configuration Release -- --test --screenshot --output-dir artifacts/screenshots/review
pwsh -File scripts/verify-doc-images.ps1
```

Use the existing authentic screenshot fixtures. Slim baseline updates come from the authoritative `windows-2025` workflow artifacts; local review images are not replacement baselines.
