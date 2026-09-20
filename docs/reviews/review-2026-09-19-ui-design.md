# Review — Slim and Web UI design

**Date:** 2026-09-19
**Surface:** Current working-tree XAML, relevant HEAD source at `b65ad473`, and four committed screenshots recovered to a temporary directory for visual inspection.
**Spec source:** User follow-up: also review the app's UI/design, continuing the repository cleanup review.
**Standards sources:** `AGENTS.md`, `DESIGN.md`, `docs/adr/001-reset-time-presentation.md`, `design/theme-catalog.json`.
**Verdict:** PASS WITH NOTES for this design review; one confirmed medium copy/behavior mismatch in HEAD. Live UI and accessibility verification remain unavailable because the checkout has missing application source.

## Findings

### Medium — pace tooltip promises behavior the renderer does not provide

- [axis: standards] HEAD `AIUsageTracker.UI.Slim/SettingsWindow.xaml:357` says yellow/red thresholds apply to both pace-adjusted and raw usage modes. HEAD `ProviderCardRenderer.cs:343–348` returns red for OverPace and green otherwise when pace-adjusted, matching `DESIGN.md`.
- Impact: users can change the yellow threshold expecting a pace-mode warning that will never appear.
- Recommendation: explain that thresholds govern raw used-percentage colors and pace mode uses on/over-pace states. Preserve the accepted rendering rule. Clarify the threshold controls' applicability when pace mode is enabled.

### Low — provider visibility is separated from provider setup

- [axis: spec, design recommendation] HEAD `SettingsWindow.xaml:284–292` puts the full provider configuration stack before the separate Card Visibility panel. The stored Providers screenshot shows a long list of key fields and repeated Tray/Notify controls before visibility settings.
- Impact: finding a provider and deciding whether its card appears requires navigating two separate lists. Many controls compete with authentication status.
- Recommendation: searchable provider rows, each with name, connection status, and a clearly labeled dashboard visibility control; expand a row for credentials and less-used options. Keep tray visibility distinct from dashboard visibility. Keep every configured provider visible even when unavailable, with its cached data and status intact.

### Low — Cards exposes layout mechanics before common choices

- [axis: spec, design recommendation] HEAD `SettingsWindow.xaml:323,327` offers both “Show progress bar fill” and “No quota bar in background.” The Cards screenshot also exposes four content slots and preset management together; the Primary Badge label is at line 427.
- Impact: users must understand overlapping display options and the renderer's slots to make a simple visual choice.
- Recommendation: put existing Compact/Detailed/Pace Focus presets and the live preview first. Put slot assignment and preset management in an Advanced expander. Investigate existing preference combinations before replacing the two fill controls with one mutually exclusive display choice; preserve serialized preferences and behavior during migration.

### Low — dashboard styling gives secondary information too much visual weight

- [axis: spec, visual judgment] In the stored Slim dashboard screenshot, broad green fills compete with provider names and remaining quota. Cyan section rules, repeated “just now” labels, yellow reset labels, and pace details all seek attention on the same line.
- Recommendation: align the main quota value in a stable right-hand column; use provider name as the primary label, with window/reset and freshness as secondary information. De-emphasize repeated fresh timestamps while keeping stale/error text visible. Reduce healthy-fill visual intensity through theme tokens after checking contrast; retain the meaning, width direction, and threshold behavior required by `DESIGN.md`.
- This is a hierarchy recommendation, not a finding that green remaining bars are mathematically wrong. In particular, do not change remaining to used by default or interpret every large green bar as excess consumption.

### Low — compact chrome needs an accessibility and narrow-window pass

- [axis: regression, unverified risk] Current `MainWindow.xaml:43` gives the hide button an 18-DIP width; lines 100–110 define four 32×28-DIP icon buttons. MainWindow has a 320-DIP minimum width and a non-wrapping footer. Status/version text uses 10 DIPs. Icon buttons have tooltips but no explicit `AutomationProperties.Name` declarations in this file.
- Recommendation: name actions explicitly for assistive technology, including “Hide to tray” and the current Monitor start/stop action. Check focus indication, keyboard order, and layout at minimum width and 125–200% scaling. Enlarge hit areas where space allows; move occasional commands into an overflow if the footer cannot fit.
- No screen-reader failure, clipping defect, or numerical contrast failure is claimed without a live check. Tooltips and inherited styles may affect the actual accessibility surface.

## Screenshot freshness: do not fix already-fixed Web issues

Inspected HEAD images: `docs/screenshot_dashboard_privacy.png`, `docs/screenshot_settings_providers_privacy.png`, `docs/screenshot_settings_cards_privacy.png`, and `docs/screenshot_web_dashboard.png`. They were extracted without restoring or overwriting working-tree files. Screenshot fixture dimensions are not proof of normal runtime window size.

The Web image places Provider Reliability above Current Usage and contains malformed percentage labels. HEAD `AIUsageTracker.Web/Pages/Index.cshtml:112,323` already places Current Usage first, and line 349 formats success percentages with `ToString("F1")`. These screenshot issues are historical, not current source findings. The Slim screenshot title also predates the current version. Fresh screenshots are needed after the missing-file state is resolved; authoritative Slim baseline updates must use the documented Windows CI artifact workflow.

For the current Web source, retain the usage-first order. A compact reliability summary with details on the existing Reliability page is a design option to reduce repetition. Do not infer runtime behavior from the outdated screenshot or remove useful failure visibility.

## Useful patterns in other local repositories

| Repository / evidence | Borrow | Do not copy blindly |
|---|---|---|
| `../usage-atlas/public/quota-view.js:45–79,103,160` | Explicit freshness and partial-data vocabulary, understandable quota-window labels, and retention of prior readings after a failed refresh. | Its 70/90 color thresholds differ from this repo's accepted 60/80 thresholds. Preserve this app's provider definitions and cache behavior. |
| `../../DesktopApps/appzone/src/AppZone.Wpf.View/Controls/PresetLookupAndAdvancedPanel.xaml:8–14,20–53` | Collapsed Advanced section, explicit automation names, and field-local validation messages. | Its domain-specific settings or full shell layout. |
| `../../DesktopApps/appzone/src/AppZone.Wpf.UI/Resources/Themes/ProTool.xaml:6–20` | Named padding, radius, focus, and semantic color resources instead of per-control styling decisions. | The literal palette without checking this app's quota colors and every supported theme. |

These are source comparisons, not claims that sibling apps were launched or visually validated. SoleX remains the relevant comparison for build-output ownership in the companion review.

Microsoft's [Fluent color guidance](https://fluent2.microsoft.design/color) supports using neutral surfaces for hierarchy and pairing meaningful status color with other indicators. Its [settings guidance](https://learn.microsoft.com/en-us/windows/apps/design/app-settings/guidelines-for-app-settings) recommends grouping related settings and revealing secondary options on demand. Apply these interaction principles in the existing WPF app; a framework migration is unnecessary for this cleanup.

## Proposed design direction and sequence

The product's primary job is to let someone quickly see available quota and the next reset while keeping failures and stale readings honest. Keep Slim a compact desktop utility and Web the more spacious inspection surface.

1. Correct misleading copy and improve explicit action names. Keep all shortcuts and hide-to-tray behavior unchanged.
2. Rework one provider row using existing fixture data: aligned name and remaining value, stable placement for quota windows and resets, clear stale/error state. Preserve dual-window/reset rules and provider-driven presentation.
3. Restructure Providers and Cards settings around common tasks and progressive disclosure. Reuse the existing live preview and presets.
4. Share terminology and semantic token roles between Slim and Web through the existing theme catalog and platform resources. Use a small spacing/type scale; make quota figures more prominent than timestamps. Avoid adding another decorative theme as a substitute for fixing hierarchy.
5. Verify representative quota, currency, dual-window, unavailable, stale, privacy, and long-name states; minimum-width layout, keyboard focus, scaling, and representative light/dark themes. Run affected UI tests and generate screenshots from existing authentic fixtures. Do not invent provider responses or replace CI baselines locally.

A thin standalone quota track could be explored later, but changing the accepted default progress-bar presentation requires a separate explicit product decision under `DESIGN.md`. The initial hierarchy work can retain the present semantics.

## Verification and scope

- Read current MainWindow XAML, design rules, reset ADR, theme metadata, and manual. Inspected relevant HEAD Settings/renderer/Web source because those files are deleted in the working tree.
- Viewed four committed PNGs recovered into a unique directory under the OS temporary directory; no image modifications.
- Read the sibling components listed above and current primary Microsoft design guidance.
- The companion review's test command failed during compilation due to missing `UsageMath.cs`; no source changed since that failure, so repeating it would not verify this review. No live WPF, browser, focus, contrast, or screen-reader results are claimed.
- Other settings tabs, full provider-state permutations, and every theme were not visually reviewed. No product source, settings, data, screenshot baseline, or build configuration changed.
- Companion: [build-output hygiene review](review-2026-09-19-build-output-hygiene.md).
