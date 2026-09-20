# Build and Slim UI cleanup: develop recovery

The cleanup is recovered on `fix/build-ui-cleanup-develop-20260920`, based on upstream `develop` at `e0f38b0f9279a46fc8f6ee5de11fff0b6c2cc1f2`. Only the implementation delta from `09e81b614fcb39c2bc0c5aae9200478527505c13` was transplanted. The fork's stable release and unrelated history are excluded. Version metadata remains `2.4.7-beta.3`.

This supersedes the landing plan in the [original implementation note](implementation-2026-09-19-build-ui-cleanup.md). The controlling findings are in the [September 20 branch review](../../../docs/reviews/review-2026-09-20-tracker-build-ui-cleanup-branch.md). Local receipts are in [the recovery evidence folder](../../../.scratch/tracker-recovery-20260920/); that folder is not part of the commit.

## Review findings addressed

| Finding | Recovery |
|---|---|
| F1: footer test needs a source checkout | Copy the real XAML into test output and resolve it from the assembly. Full artifact testing also exposed 97 existing source-root failures; package the guardrail/appcast inputs and resolve those from test output too. |
| F2: stale fork base | Start from current upstream `develop`, preserving upstream behavior and analyzer settings. |
| F3: stable release path | Retain beta version metadata and prepare a topic branch for a PR to `develop`. No stable release metadata or release commits are included. |
| F4: Web launcher searches obsolete outputs | Resolve matching centralized build pivots, co-located executables, and portable sibling components. Add tests covering those choices and fallback behavior. |
| F5: unsafe documentation changes | Exclude the proposed Spark-removal/open-issues material. README, CHANGELOG, and existing open-issues content remain unchanged from upstream. |
| F6: Compact name clipped by empty status column | Span the name across both header columns when status is absent; verify actual arranged width at the narrow layout size. |
| F7: stop/status require the SDK | Resolve executables only for start. Exercise stop/status with a failing mock `dotnet` in PowerShell 7 and 5.1. |

The Windows checkout also exposed upstream's case-colliding `docs/ARCHITECTURE.md` and `docs/architecture.md`. Preserve both documents by renaming the latter to `architecture-legacy.md` and fixing its current reference.

The test runner previously terminated machine-wide test processes, interrupting independent suites. Remove that sweep; timeout cleanup remains limited to the process tree launched by the runner. Correct its VSTest filter argument as well.

Web fixture verification exposed that Windows special-folder lookup ignores a process `LOCALAPPDATA` override. Web now honors that override, and Seeder accepts an explicit destination argument. Final Web tests and the packaged smoke test use the repository's recorded provider fixture in a scratch database. The populated-provider assertion now selects a provider with corresponding history in that fixture; missing-provider behavior is checked separately. No provider responses were invented.

## Verification

| Check | Result and local receipt |
|---|---|
| Regression reproduction | Footer fails without checkout; five launcher/header checks fail before fixes; SDK-free action harness fails before fix. `footer-before.trx`, `regressions-before.trx`, `actions-before.log`. |
| Required pre-commit gate | Pass: Release solution build, changed C# formatting/style, core and Monitor suites. `pre-commit.log`. |
| Core tests without screenshot category | 1,449 passed, 2 skipped in source and copied Debug/Release artifacts. `core-source.trx`, `artifact-final/artifacts/test-results/artifact-core-final.trx`. |
| Monitor tests | 143 passed, including a concurrent run beside the final artifact core suite. `artifact-final/artifacts/test-results/artifact-monitor-final.trx`. |
| Web tests without browser screenshot class | 47 passed against the isolated recorded fixture. `web-final.trx`. |
| Build-output and cleanup safety | Pass on PowerShell 7 and Windows PowerShell 5.1, including Windows junction checks. `build-tools-ps7-final.log`, `build-tools-ps51-final.log`. |
| SDK-free stop/status | Pass on both PowerShell engines. `actions-after.log`, `actions-ps51.log`. |
| UI checks | Nine headless screenshots generated; dashboard, Providers, and Cards visually inspected. All 14 themes and theme contract pass. `screenshots.log`, `themes.log`, `theme-contract.log`. |
| Packaging | Four Windows component executables and an 18,152,581-byte beta ZIP produced. Packaged CLI prints usage; packaged Web serves fixture history with HTTP 200. `publish.log`, `packaged-cli-usage.log`, `packaged-web-smoke.json`. |
| Existing work preserved | Five original Tracker/Usage Atlas worktrees retain their starting HEAD and porcelain status. `worktree-baseline.json`, `preservation.json`. |

The distinct executed non-screenshot suites total **1,639 passed and 2 skipped**. Focused repetitions and the five locally inactive screenshot-baseline checks are excluded from that total. Git whitespace and documentation image-reference checks pass. No analyzer rules were weakened.

## Remaining gates

- Windows CI must generate and review the authoritative screenshot baselines and exercise the updated artifact upload/download workflow. Local candidate images did not replace committed baselines.
- Inno Setup is absent, so installer compilation is unverified. The packaging run produced the ZIP but incorrectly identified this Windows environment as non-Windows because `OS` was unset. Host detection now uses the operating-system platform; missing Inno Setup correctly remains a prerequisite, not a successful installer result. See `installer-gate.json`.
- The new branch is local pending push confirmation and a beta PR to upstream `develop`. No tag, release, deployment, or installed-app replacement was performed. Final test helpers and the packaged Web process were stopped.

The [September 19 deletion review](../../../docs/reviews/review-controller-2026-09-19-claude-last-section.md) remains deferred; existing deletions were not restored. The completed Usage Atlas quota-recovery round was not reopened.
