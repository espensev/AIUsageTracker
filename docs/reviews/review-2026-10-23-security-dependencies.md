# Dependency security repair

Date: 2026-10-23. Scope: the shipped Slim SignalR chain and test-only screenshot comparison dependency. No installed release was changed.

## Changes

- SignalR.Client moves from 10.0.5 to 10.0.13, resolving the affected Common package. Slim's explicit Logging reference also moves to 10.0.13 to satisfy the client's minimum dependency without a downgrade. [Microsoft advisory](https://github.com/advisories/GHSA-334f-vvp4-7g6p).
- Removed Codeuctivity.ImageSharpCompare and its ImageSharp dependency from the Windows test project. The patched ImageSharp package passed the comparison probe but required a separate license for Release builds. The replacement uses the WPF PNG codec already supplied by the project's Windows framework; no additional package or license bypass is introduced.
- The screenshot contract is unchanged: reject differing dimensions, compare exact RGB channels without alpha compositing or color correction, count a pixel once if any channel differs, accept only when differing pixels are strictly below 1%, and retain a per-channel PNG difference image on failure.
- The implementation is deliberately limited to the PNG screenshots used by this test suite. It does not add a general image processing API or change production rendering.

## Verification

- Before repair, the solution-wide transitive audit reported high-severity SignalR and ImageSharp vulnerabilities. After repair, all ten projects report no vulnerable packages using the current NuGet advisory source.
- Seven comparator regressions cover unchanged/different pixels, 0.5%/1%/1.5% boundaries, alpha and RGB/RGBA normalization, dimension mismatch, and reopening the generated PNG mask. They first failed against an unimplemented comparator and then passed.
- A temporary differential probe compared the old Codeuctivity 4.1.379 API with the replacement before removing the dependency. Thirteen tests passed, including the threshold/alpha cases and all ten committed privacy screenshots both unchanged and with one modified pixel. The probe and its output are retained in the workspace execution evidence, not shipped as a permanent dependency on the removed library.
- `dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj --configuration Release`: 1,517 passed, two existing authentication tests skipped, zero failed. Existing documentation/style warnings remain unrelated.
- The five `ScreenshotBaselineTests` also passed with both screenshot directory selectors explicitly pointing at the committed fixture directory. This exercises actual decoding and assertions; it is fixture acceptance, not a fresh installed UI rendering claim.

## Reproduce

```powershell
dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj --configuration Release
dotnet list AIUsageTracker.sln package --vulnerable --include-transitive
```

The existing Windows CI screenshot workflow remains the authority for newly rendered UI baselines. Baseline image files and the 1% threshold were not changed.
