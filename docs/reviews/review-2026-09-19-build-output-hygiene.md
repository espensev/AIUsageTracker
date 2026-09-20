# Review — build-output hygiene

**Date:** 2026-09-19
**Surface:** Current working tree and build/packaging configuration; this is a repository hygiene review, not a full review of the deletion diff.
**Spec source:** User request to review clutter, compare other repositories, and recommend clean handling of executables and intermediate outputs.
**Standards sources:** `AGENTS.md`, `CLAUDE.md`, `docs/ARCHITECTURE.md` (contains stale guidance).
**Verdict:** FAIL for current build readiness; centralized output migration is recommended but not implemented.

## Findings

### High — existing missing source prevents verification

- [axis: regression] `AIUsageTracker.Core/Models/UsageMath.cs` is deleted in the working tree. `git diff --name-only --diff-filter=D` reports 168 deleted tracked files, including application entry points, tests, fixtures, screenshots, and `.github/workflows/tests.yml`.
- Evidence: `dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj --configuration Debug --no-restore --verbosity quiet` exits 1 with CS0103 references to missing `UsageMath`, beginning at `AIUsageTracker.Core/MonitorClient/AgentGroupedUsageValueResolver.cs:24`. No tests executed.
- Resolve the intended state of these pre-existing deletions before validating a build migration. This review did not restore or delete them.

### Medium — output consumers assume the old directory layout

- [axis: regression] `scripts/run-slim-and-monitor.ps1:16`, `scripts/generate_screenshots.ps1:19`, `scripts/generate_card_catalog.ps1:23`, `scripts/generate_web_screenshots.ps1:20`, `scripts/verify_slim_theme_smoke.ps1:12`, and `scripts/verify-monitor-openapi-contract.ps1:13` embed project-local `bin` paths.
- `scripts/resolve-test-assembly.ps1:15` lists old paths and then recursively chooses the first matching assembly at lines 29–31. With multiple configurations present, that fallback can select an unintended build.
- Missing working-tree files were inspected through `git show HEAD:<path>` only: `MonitorLauncherProcessController.cs:200–201` prefers old Debug/Release executable paths; `.github/workflows/tests.yml:215–219,380` embeds the old test and Seeder paths. These are HEAD evidence, not claims that the files currently exist.
- Enabling centralized output alone would leave these consumers broken or selecting stale binaries. Change producers, launchers, screenshot tooling, test resolution, and CI together. Prefer evaluated MSBuild paths or a shared deterministic resolver with explicit configuration/framework/runtime inputs.

### Low — generated files have several homes and no clear retention policy

- [axis: spec] `Directory.Build.props:2` contains shared properties but does not centralize outputs. MSBuild evaluation confirms project-local `bin/Debug/...` and `obj/`.
- Inventory before test execution: `bin` contains 1,684 files / 470.7 MiB; `obj` 705 files / 19.7 MiB; `dist` 180 files / 63.0 MiB. Total: 2,569 files / 553.4 MiB. Five EXEs were present in the checkout. This snapshot does not establish historical growth or redundant copies of every app.
- `scripts/publish-app.ps1:14,95,169` keeps expanded staging and final ZIPs together in `dist`, replacing staging only for the selected runtime. Versioned packages have no pruning policy in this script.
- `.gitignore:482–487` retains `bin_unlocked`, `bin_unlocked_ui`, and `TempBin` exceptions. These show supported historical locations, not proof those folders currently exist. No EXEs, DLLs, or project bin/obj files were found tracked by Git.
- Ignoring outputs already prevents Git noise. Centralizing their production and defining cleanup/retention addresses filesystem clutter.

### Low — architecture guidance is stale

- [axis: standards] `docs/ARCHITECTURE.md:9–17` requires .NET 8, conflicting with `AGENTS.md`, `global.json`, and current net10.0 project targets. Lines 35 onward also contain machine-specific file links.
- Update this document alongside a build-layout migration so future work follows the actual framework and portable paths.

## Other repositories inspected

Paths below are relative to this repository; no sibling repository was modified.

| Repository | Verified pattern | What to adopt |
|---|---|---|
| `../../DesktopApps/SoleX` | `Directory.Build.props:18–19` enables `UseArtifactsOutput` and a root-relative `build` directory. `scripts/Build-Publish.ps1` stages a runnable payload, promotes it, prunes archives, and removes intermediates after success unless `-KeepBuildDir` is supplied. | Central output ownership, validated staging, explicit retention, path-checked cleanup. Keep development caches here by default to preserve incremental builds. |
| `../../DesktopApps/Terminal-HQ` | Shared build properties; no centralized output setting found in scanned props/targets/projects. Ignores artifacts and dist. | Not a stronger output-layout example than SoleX. |
| `../../DesktopApps/appzone` | Shared .NET 10 properties; no centralized output setting found in scanned props/targets/projects. Ignores per-project bin/obj and assorted logs. | Illustrates why ignore rules alone do not centralize outputs. |
| `../usage-atlas` | Concise `.gitignore` names dist, dist-server, coverage, and test-report directories. | Explicit output categories; this is not a .NET implementation to copy. |

The SDK supports a centralized artifacts tree separated by project and configuration/framework/runtime pivots. See [Microsoft's artifacts output documentation](https://learn.microsoft.com/en-us/dotnet/core/sdk/artifacts-output).

## Recommended implementation

Configure this in the existing root `Directory.Build.props`:

```xml
<UseArtifactsOutput>true</UseArtifactsOutput>
<ArtifactsPath>$(MSBuildThisFileDirectory)artifacts</ArtifactsPath>
```

Use the following ownership rules:

```text
artifacts/
  bin/<project>/<pivot>/       development binaries
  obj/<project>/              generated/intermediate files
  publish/<runtime>/<component>/ packaging staging
  test-results/               test and coverage reports
  logs/                       build diagnostics
dist/                         completed installers and ZIPs only
```

The publish/test/log locations require explicit script arguments; the SDK switch does not automatically relocate all custom outputs.

1. Resolve the missing tracked-file state, then establish passing baseline tests.
2. Enable centralized outputs and update all known path consumers in the same change. Query MSBuild properties where possible; do not scan recursively for the first matching executable or assembly. Include the Monitor auto-launch path and CI artifact download layout.
3. Move publish staging out of `dist`; update `scripts/setup.iss:7` and the `/DSourcePath` argument in `scripts/publish-app.ps1:183`. Keep final archive and installer paths compatible with `.github/workflows/publish.yml:70–71`.
4. Add one cleanup entry point with preview/WhatIf support. Enumerate only known generated directories, reject paths outside the resolved repository and reparse-point escapes, and check for tracked contents. Preserve runtime databases/settings, fixtures, screenshots, source, and other worktrees. Avoid blanket `git clean -xfd` and process-wide dotnet termination.
5. Remove obsolete per-project bin/obj once during migration; retain explicit exclusions for legacy generated directories so leftover generated C# cannot enter compilation after the output path changes. Keep caches during ordinary development; provide explicit deep cleanup and package retention controls.
6. Validate Debug build and tests, Monitor auto-launch, Slim/Web/CLI launch, screenshot commands, CI test assembly resolution, and a Release publish/install smoke test. Update architecture docs and simplify obsolete ignore entries only after their producers are gone.

Centralizing output reduces scattered directories, not the number of assemblies the build requires. `scripts/publish-app.ps1:99–103` intentionally publishes four applications: Tracker, Monitor, Web, and CLI. Seeder and test/dependency copies are development outputs. Keep those roles separate; a single-file publish would still be per application and is not needed to solve this layout problem.

## Verification and coverage

- `git status --short`, unstaged/cached diff inspection: 168 pre-existing deletions; no staged diff.
- Filesystem inventory and tracked-binary scan: results above; sizes are a point-in-time measurement.
- `dotnet --version`: 10.0.401, allowed by `global.json`'s `latestFeature` roll-forward from 10.0.300.
- `dotnet msbuild AIUsageTracker.UI.Slim/AIUsageTracker.UI.Slim.csproj -nologo -getProperty:OutputPath,BaseIntermediateOutputPath,TargetPath,UseArtifactsOutput`: pass; confirms current project-local layout.
- Same evaluation with `-p:UseArtifactsOutput=true`: pass; resolves to `artifacts/bin/AIUsageTracker.UI.Slim/debug` and `artifacts/obj/AIUsageTracker.UI.Slim`. Evaluation only, not a migration or successful build.
- Main test command: failed during compilation due to pre-existing missing source. No successful test or package result is claimed.
- Deep review: shared build config, ignore rules, Slim/Monitor/main-test project files, publish script, test assembly resolver, and relevant sibling output configuration. Sampled: installer, other scripts/workflows, architecture docs, and HEAD launcher/CI content. All other deletion-diff files are outside this hygiene review; their application behavior was not reviewed.
- No source/build configuration changes, cleanup, application launch, installation, or release actions were performed. Only this report and its index entry were added.

## Open question

The cause and intended outcome of the 168 pre-existing deletions are unknown. They must be resolved before implementation can be fully verified.
