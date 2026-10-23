# Review - AIUsageTracker monitor stack

**Date:** 2026-09-21
**Surface:** `origin/main` `5c3ca89e`...`HEAD` `fc71e547` on `fix/monitor-scheduled-task-status-20260921` (PR https://github.com/espensev/AIUsageTracker/pull/5)
**Spec source:** ship the monitor stack; review and test it
**Standards sources:** `AGENTS.md`, `CLAUDE.md`
**Verdict:** PASS WITH NOTES

## Findings

### High

No findings remaining. `LoadConfigAsync` filtering of suppressed IDs was a save-path hazard; it now lives in `GetConfigsAsync` (`fc71e547`).

### Medium

No remaining merge blockers. Dashboard summary still counts suppressed providers (`Index.cshtml` uses `Summary.ProviderCount` from unfiltered history).

### Low

- [axis: standards] PR #5 currently carries seven product commits plus the seeder CI fix. The last three product commits (WPF test isolation, Claude CLI session, suppressed providers) are disjoint file sets. GitHub stacking on #4 explains the first four. This is a note, not a merge block: the user asked to ship this stack as one branch.

## Verification

- `dotnet test AIUsageTracker.Tests` Debug - pass (1466 passed, 2 skipped)
- `dotnet test AIUsageTracker.Web.Tests` Debug - pass (70 passed, 6 skipped)
- `dotnet test AIUsageTracker.Monitor.Tests` Debug - pass (153 passed)
- Targeted filter (MonitorStartupPath, ClaudeCodeProvider, ConfigLoader, DialogOpen, PrivacyButton) - pass (68 passed)
- GitHub Actions on `f7f05a58` - Web Tests (Windows) fail: seeder lacked `card_id`
- Seeder schema fix `c7ca2eed` merged in `a37c51ea`; new CI run in progress at review time

## Coverage Notes

- Files reviewed deeply: `MonitorLauncher.cs`, `ClaudeCodeProvider.cs`, `ClaudeCodeProviderTests.cs`, `JsonConfigLoader.cs`, `DefaultAppPathProvider.cs`, `Index.cshtml.cs`, `MonitorStartupPathTests.cs`, `ConfigLoaderTests.cs`, `ViewTests.cs`, `scripts/Seeder/Program.cs`
- Files sampled: dashboard CSS, `_Layout.cshtml`, WPF `TestUiPreferencesStore.cs`
- Excluded: cli-home worktree `8cd58c35` / PR #6

## Open Questions

- Whether GitHub Web Tests (Windows) go green on `a37c51ea` after the seeder fix.
