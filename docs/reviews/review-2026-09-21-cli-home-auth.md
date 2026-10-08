# Review - CLI home authentication discovery

**Date:** 2026-09-21
**Surface:** `f7f05a58..af688ee2`, plus the documentation corrections in this worktree
**Spec source:** User request to continue the CLI home/auth fix; intent recorded in `af688ee2`. No separate specification found.
**Standards sources:** `AGENTS.md`, `CLAUDE.md`, `docs/test_fixture_sync.md`
**Verdict:** PASS

## Findings

No outstanding blocking findings. Two documentation issues were corrected during follow-up:

- **Low, regression:** `docs/environment_variables.md` named the Spark provider `codex-spark`; `CodexProvider.SparkDefinition` uses `codex.spark`. Corrected the table.
- **Low, regression:** The same document claimed that redirecting the profile root isolates environment overrides. `IAppPathProvider.GetEnvironmentVariable` defaults to the process environment, and provider refreshes use the resolver's process-environment overload. Corrected the wording to require callers to override the environment lookup as well.

## Verification

Command run from the CLI home worktree:

```powershell
dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj --configuration Debug --filter 'FullyQualifiedName~AuthPathTemplateResolverTests|FullyQualifiedName~ProviderAuthCandidatePathResolverTests|FullyQualifiedName~TokenDiscovery|FullyQualifiedName~ProviderDiscovery|FullyQualifiedName~ProviderSessionTokenResolver|FullyQualifiedName~ClaudeCodeProvider|FullyQualifiedName~CodexAuthService|FullyQualifiedName~CodexProvider|FullyQualifiedName~GrokProvider|FullyQualifiedName~DefaultAppPathProvider' --logger 'trx;LogFileName=cli-home-auth.trx'
```

- Passed: 112; failed: 0; skipped: 0. This command also built the test project and its product dependencies.
- Build emitted documentation-analyzer configuration warnings and two arithmetic-precedence warnings in unchanged `ZaiProviderTests.cs`; no build errors.
- `git diff --check HEAD^ HEAD` passed for the original commit.
- `git diff --check` passed for the follow-up documentation edits.

Continuation validation ran `bash scripts/pre-commit-check.sh`:

- Release solution build passed with 0 errors and 17 warnings: documentation-analyzer configuration warnings, the two arithmetic-precedence warnings above, and a final-newline warning in unchanged `MonitorApiSecurityTests.cs`.
- Core tests: 1,492 passed, 0 failed, 2 skipped (the GitHub authentication tests marked skipped by the suite).
- Monitor tests: 153 passed, 0 failed, 0 skipped.
- The script skipped source formatting because the follow-up contains only Markdown changes.

## Coverage notes

All 12 files changed by `af688ee2` were reviewed: the path-provider interface and implementation, both path resolvers, Claude/Codex/Grok definitions and credential reads, all three new test files, the changelog, and environment-variable documentation. Direct session-discovery and Codex-auth callers were also inspected.

Tests cover override ordering, unset variables, profile-token isolation, substituted values containing percent signs, Claude's relocated credential read, and existing discovery/provider behavior. No new API payload shapes or screenshot changes are involved.

The targeted tests and repository pre-commit checks passed. The separate Web test suite, live authenticated provider calls, installed Monitor behavior, and scheduled-task environment propagation were not tested. `gh pr list --head fix/cli-home-env-auth-discovery-20260921 --state all` returned no PR during continuation.

## Open questions

None for the reviewed code and documentation.
