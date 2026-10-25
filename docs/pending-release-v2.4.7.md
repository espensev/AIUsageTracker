# Pending v2.4.7 release material

Status: held. This document preserves release-facing material without
authorizing a tag, GitHub release, package publication, or deployment. It is
not one of the paths that triggers `.github/workflows/auto-tag-release.yml`.
Promote the entries below into `CHANGELOG.md` and update the `README.md` badge
and repository links only when the owner explicitly authorizes the release.

## Added

- **CLI home overrides in session discovery** — `GROK_HOME`, `CODEX_HOME` and
  `CLAUDE_CONFIG_DIR` are honored ahead of the profile default, so a relocated
  CLI home no longer leaves the provider on a stale stored token. Auth path
  templates accept any `%NAME%` token; an unset variable drops that candidate.
  Claude Code's per-refresh credential re-read uses the same candidate list
  instead of a hardcoded profile path. See `environment_variables.md`.

- **`scripts/local-stack.ps1`** — publishes the Monitor and Web pair into a
  release directory, points the scheduled tasks at it, restarts them in a safe
  order, and verifies the dashboard; `-Action status` inspects the running
  stack. Task ownership must be unambiguous, only processes from the stack's
  release directories are stopped, a port that never frees refuses cutover,
  and a failed action update restores tasks already re-pointed. See
  `local-stack.md`.

## Fixed

- **Dashboard summary ignores suppressed providers** — the provider count and
  average usage on the Web dashboard no longer include providers listed in
  `SuppressedProviderIds`, matching the card filter.

- **CI seeder keeps history rows whose provider metadata is gone** — exported
  `provider_history` can outlive `providers` rows; the seeder inserts inactive
  stubs for those IDs so the Web smoke database satisfies the foreign key.
  Tests call `SeedDatabase` with an explicit path and do not touch live data.

## README updates held for release

- Change the version badge from `2.4.6` to `2.4.7`.
- Change the downloads and releases links from the historical `rygel` owner to
  `espensev`.
