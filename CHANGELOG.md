# Changelog

All notable changes to Why Did I Reboot are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

When a `vX.Y.Z` tag is pushed, the release workflow copies the matching `## [X.Y.Z]` section below into the GitHub Release notes, so write the notes here first.

## [Unreleased]

## [0.3.0] - 2026-09-20

### Added
- xUnit test project (`tests/WhyDidIReboot.Tests`) covering the analyzer's categorisation, timings and evidence over synthetic event records, plus the exporters, bugcheck catalog and formatting helpers. CI runs it on every push and publishes the results.

### Changed
- Added a repo-level `nuget.config` pointing at nuget.org so restores work on machines with no NuGet sources configured.

## [0.2.0] - 2026-09-20

### Added
- **Light and dark themes.** A theme picker in the header offers System theme, Light or Dark. System theme follows the Windows "Choose your default app mode" setting and switches live when it changes. The title bar is recoloured to match, every control follows the palette, and category colours have brighter dark-mode variants. The choice is remembered in `%LocalAppData%\WhyDidIReboot\settings.json`.
- **HTML export.** Export now offers a self-contained HTML report (the new default) with the same cards, colour accents, category counts and day grouping as the app, a collapsible details section per entry, automatic light/dark styling, and print-friendly output. Also available headless: `WhyDidIReboot.exe --export report.html --days 90`.
- **"none" filter preset** under *Show:* to clear every category and build a view from scratch.

### Fixed
- The search box's icon, placeholder and typed text were slightly misaligned. They now share one container with the same baseline and left edge.

## [0.1.0] - 2026-09-20

### Added
- First release. Reads the Windows System and Application event logs and shows one plain-English card per reboot, newest first and grouped by day.
- Categories: Windows Update, User, App or service, Blue screen, Power loss / freeze, Clean (no reason), Unknown, Kernel error (no reboot), and Sleep / wake.
- Blue screens decoded with the STOP code, symbolic name, a plain-English hint, the minidump path and whether the file still exists. Works with or without a dump.
- Unexpected stops refined into held power button, power lost while asleep, firmware hardware error, or plain freeze/reset.
- Live kernel events (GPU hangs, resource timeouts) that did not reboot the machine, de-duplicated across Windows Error Reporting retries.
- Category filter chips with counts, quick presets, free-text search, and a date range from 7 days to the whole log.
- Export to text or CSV, copy a single entry, open Event Viewer, and jump to a crash dump in Explorer.
- Headless `--export` mode for scripts.

[Unreleased]: https://github.com/WelFedTed/why-did-i-reboot/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/WelFedTed/why-did-i-reboot/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/WelFedTed/why-did-i-reboot/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/WelFedTed/why-did-i-reboot/releases/tag/v0.1.0
