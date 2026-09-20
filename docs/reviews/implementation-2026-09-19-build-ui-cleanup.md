# Build and UI cleanup — implementation evidence

This note records the original fork-based implementation. The current recovery and its verification are recorded in [the develop recovery report](recovery-2026-09-20-build-ui-cleanup.md); the original landing plan below is superseded.

Implemented on local branch `chore/build-ui-cleanup` in the sibling `AIUsageTracker-build-ui-cleanup` worktree. The original checkout still has its pre-existing tracked-file deletions; none were restored or modified. No branch was pushed during implementation or review.

## Changes

- Centralized binaries, intermediates, staging, reports, and logs under `artifacts/`; finished packages stay in `dist/`.
- Shared evaluated build-path resolver, explicit CI assembly manifest, matching Monitor build-pivot discovery, and portable-package sibling lookup.
- Guarded cleanup with preview, legacy migration, cache controls, and final-package retention. Screenshot scripts no longer terminate all app processes.
- Searchable provider settings with inline connection/dashboard controls and expandable details. Card presets/preview precede advanced options; pace tooltip now matches the renderer.
- Normal cards separate primary name/quota from secondary details. Compact mode, quota math/color semantics, reset rules, privacy, and shortcuts remain intact. Accessible action names, focus indication, and 32-DIP actions fit the narrow window.

See [build and cleanup](../build-and-cleanup.md) for commands and [user manual](../user_manual.md) for the updated settings flow.

## Verification

| Check | Result |
|---|---|
| `dotnet build AIUsageTracker.sln --configuration Debug` | Pass; zero warnings and errors on the final run |
| Main tests, `Category!=Screenshot` | 1,470 passed, 2 skipped |
| Monitor tests | 153 passed |
| Web tests excluding browser ScreenshotTests | 66 passed |
| Final targeted settings/layout/launcher tests | 56 passed; subset of main tests |
| Build-output script harness | Pass on PowerShell 7 and Windows PowerShell 5.1 |
| PowerShell parser for changed/new scripts | Pass |
| Changed C# whitespace formatting and Git diff whitespace | Pass |
| Headless privacy screenshots | Nine generated successfully; dashboard, Providers, and Cards visually inspected |
| Theme smoke | All 14 concrete themes passed |
| README/docs image references | Pass |
| Windows Release publish | All four components and ZIP created; installer unavailable because Inno Setup is absent |

Evidence remains in ignored `artifacts/logs/`, `artifacts/test-results/`, and `artifacts/screenshots/review/`. The ZIP is `dist/AIUsageTracker_v2.4.7_win-x64.zip`. No installer was installed, no live provider refresh was triggered, and no release/tag was created.

Initial verification found missing files in two shared NuGet cache packages (TestPlatform.TestHost and Playwright). Their cached archives matched their stored SHA-512 hashes and were re-extracted; subsequent compilation/tests succeeded. Playwright browser automation was not run.

## Remaining limits

- Slim screenshot baseline comparison is intentionally excluded from local test totals. The layout has changed; the authoritative Windows CI screenshots must be generated/reviewed and synced under the existing baseline workflow before its comparison gate can pass. Local candidates did not replace committed PNGs.
- GitHub workflows were updated and their upload/download path layout reviewed locally; no remote CI run was triggered. Installer compilation and installed-app smoke checks require Inno Setup.
- Automatic approval review rejected deletion of the isolated worktree's legacy bin/obj folders and one temporary script-test fixture with the reason “blocked by policy.” Those folders remain. The cleanup command and its independent safety tests are complete; future builds use centralized outputs.

Independent path/cleanup review prompted two hardening fixes: literal Git path matching for tracked-file checks, and rejection of manifest paths through junctions or with a different assembly filename.

## Continuation review and remediation

A second working-tree review found and resolved four integration issues before handoff:

- Moved the build-output and UI source reviews into this worktree and indexed the full review trail.
- Required screenshot generators to reject non-zero capture exits, timeouts, and stale files rather than accepting existing images after a failed run.
- Disambiguated settings rows whose provider metadata resolves to the same display name (currently the two MiniMax regional IDs) without hardcoded provider-specific UI rules.
- Restricted Inno Setup discovery to Windows hosts so cross-publishing a Windows ZIP does not dereference Windows-only environment variables.

Final verification after those fixes: Debug solution build passed with zero warnings; main tests passed 1,470 with 2 intentional skips; Monitor tests passed 153; non-browser Web tests passed 66; the focused UI/settings/launcher suite passed 56; all 14 themes passed; all 9 UI screenshots and all 16 card-catalog screenshots were freshly generated; build-output tooling, PowerShell parsing, documentation image references, changed-file formatting, diff whitespace, and credential/debug-marker scans passed. The repository pre-commit gate also passed its Release build plus main and Monitor tests.

## Landing and follow-up plan

1. Push `chore/build-ui-cleanup` and open the appropriate PR without mixing in the damaged release-notes checkout. Use `develop` for a beta-bound change or `main` for a stable-bound change, following `docs/release-process.md`.
2. Let the existing Windows CI workflows exercise the rewritten artifact upload/download layout and generate the authoritative `windows-2025` Slim screenshot artifact.
3. Review that artifact for the dashboard, Providers, Cards, privacy, theme, minimum-width, unavailable-provider, and long-name states. Sync only drifted `docs/screenshot_*_privacy.png` files from the artifact, then run `scripts/verify-doc-images.ps1`; do not substitute the local candidates.
4. Re-run CI after any authoritative screenshot sync and confirm the screenshot-baseline, tests, analyzer, coverage, and provider-contract workflows are green.
5. On a Windows machine with Inno Setup 6, run `scripts/publish-app.ps1 -Runtime win-x64`, verify the ZIP and installer names, install to the default `{autopf}\AIUsageTracker` location, and smoke-test Slim, Monitor, Web, CLI, and Monitor auto-launch without triggering live provider refreshes.
6. Resolve the damaged `chore/release-2.4.7-notes` checkout as a separate recovery task. Do not stage, restore, or combine its deletion set with this branch until its intended state is independently established.
