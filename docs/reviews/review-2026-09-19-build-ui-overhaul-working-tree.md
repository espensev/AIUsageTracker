# Review — build and UI overhaul working tree

**Date:** 2026-09-19
**Surface:** Unstaged and untracked work in `chore/build-ui-cleanup` at `b65ad473`
**Spec source:** Current user request and `docs/reviews/implementation-2026-09-19-build-ui-cleanup.md`
**Standards sources:** `AGENTS.md`, `CLAUDE.md`, `DESIGN.md`, `docs/ARCHITECTURE.md`, `docs/adr/001-reset-time-presentation.md`, `design/theme-catalog.json`
**Verdict:** PASS — the initial review failed on two medium findings; all findings below were remediated and re-verified

## Findings

### Medium — resolved: the source reviews were stranded outside the implementation worktree

- [axis: standards] The implementation worktree initially contained only `docs/reviews/implementation-2026-09-19-build-ui-cleanup.md`; the underlying `review-2026-09-19-build-output-hygiene.md` and `review-2026-09-19-ui-design.md` existed only as untracked files in the separate, damaged release-notes checkout. `docs/INDEX.md` linked the implementation evidence but not those source reviews.
- Impact: landing only the implementation worktree would omit the audit evidence and design rationale that led to the overhaul, leaving them vulnerable to loss with the damaged checkout.
- Recommendation: add the two source reviews to this worktree and index all review/evidence documents together.

### Medium — resolved: screenshot generators could report stale files as a successful capture

- [axis: regression] `scripts/generate_screenshots.ps1:84` starts the screenshot process after the former process-wide termination was removed, but `scripts/generate_screenshots.ps1:122-130` treats any existing expected file as success without checking the child exit code or whether the file was written by this run. `scripts/generate_card_catalog.ps1:50,70-97` likewise counts any pre-existing `card_*.png` and does not check the capture process exit code.
- Impact: if the Slim single-instance lock redirects/exits the headless process, or capture otherwise exits early, both scripts can claim success using old documentation images. That undermines the fixture/baseline synchronization requirement.
- Recommendation: require a zero child exit code and files newer than the capture start time; make timeout a hard failure. Keep the removal of process-wide termination.

### Low — resolved: duplicate provider display names were indistinguishable in the redesigned settings list

- [axis: spec] `AIUsageTracker.UI.Slim/SettingsWindow.Providers.cs:594` renders only `ProviderMetadataCatalog.GetConfiguredDisplayName(config.ProviderId)` as the row title. `AIUsageTracker.Infrastructure/Providers/MinimaxProvider.cs:17-18,35-37,59-61` defines separate `minimax` and `minimax-io` configurations with the same `MiniMax.io` display name. The generated Providers screenshot consequently shows two identical connected `MiniMax.io` rows.
- Impact: users cannot tell which regional configuration they are opening or changing.
- Recommendation: use metadata names as the source of truth, but append the provider ID when a display name is duplicated in the settings list. Cover the disambiguation rule with a unit test.

### Low — resolved: Windows-target publishing assumed a Windows host

- [axis: regression] `scripts/publish-app.ps1:183-186` enters the installer block for any `win-*` runtime and calls `Join-Path ${env:ProgramFiles(x86)}`. That environment variable is absent on non-Windows hosts, so a cross-publish that already produced the ZIP can now terminate before completion. The previous literal candidate path did not dereference the missing variable.
- Impact: the script's accepted `win-*` runtime values cannot reliably be used for cross-publishing the ZIP.
- Recommendation: run Inno Setup discovery only on a Windows host; on other hosts, retain the ZIP and report that installer creation was skipped.

## Verification

- `pwsh -NoProfile -File scripts/test-build-output-tools.ps1` — pass.
- `dotnet build AIUsageTracker.sln --configuration Debug` — pass with four existing documentation/style warnings.
- `dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj --configuration Debug --no-build` — pass: 1,469 passed, 2 skipped.
- Release headless screenshot capture to a unique temporary directory — pass; nine images generated. Dashboard, Cards, and Providers were visually inspected.
- `git diff --check` — pass; Git emitted only line-ending notices.

## Remediation verification

- Added and indexed both source reviews in this worktree.
- Screenshot and card-catalog scripts now require a zero exit code and fresh files from the current run; a fresh run produced all 9 UI screenshots and all 16 catalog images.
- Duplicate settings names are disambiguated generically from metadata collisions; the focused UI/settings/launcher suite passed 56 tests and the generated Providers image was visually checked.
- Inno Setup discovery is restricted to Windows hosts; non-Windows Windows-target publishing retains the completed ZIP and skips installer creation.
- Final gates: Debug build with zero warnings; 1,470 main tests passed with 2 intentional skips; 153 Monitor tests passed; 66 non-browser Web tests passed; all 14 themes passed; PowerShell harness and parser passed; Release pre-commit build/tests passed.

## Coverage notes

- Deep-reviewed: `Directory.Build.props`; generated-path safety, cleanup, resolver, manifest, publish, and screenshot scripts; Monitor launcher and tests; MainWindow XAML/rendering/card renderer; Providers and Cards settings; new UI tests; primary CI test workflow; architecture/build/user documentation.
- Sampled: analyzer/coverage/provider-drift workflows; analyzer, Sonar, vstest, local-test, OpenAPI, and theme-smoke script path rewrites; installer source-path change; `.gitignore`.
- Excluded: unchanged provider/network/database behavior, live provider refresh, installer execution, remote CI, browser screenshot tests, and authoritative screenshot-baseline replacement.

## Open questions

- Authoritative Slim baseline images still require the existing `windows-2025` workflow after the code is ready; local images must not replace them directly.
