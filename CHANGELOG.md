# Changelog

All notable changes to Why Did I Reboot are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

When a `vX.Y.Z` tag is pushed, the release workflow copies the matching `## [X.Y.Z]` section below into the GitHub Release notes, so write the notes here first.

## [Unreleased]

## [0.6.1] - 2026-09-20

### Changed
- The *Installed updates* and *Details and log records* labels are now searchable and highlighted like the rest of a card. A match on the label alone does not open the block.

### Fixed
- The red *failed* marker on an update is highlighted when the search matches it.

## [0.6.0] - 2026-09-20

### Added
- **Update now.** When a newer release is found, the Settings button (and the startup banner) turns into *Update now*: one click downloads the matching asset (the self-contained exe, or the framework-dependent zip when `WhyDidIReboot.dll` sits beside the exe), verifies its size, the SHA-256 digest GitHub publishes, the PE header and the file version, swaps the files in beside the running copy, relaunches and cleans up on the next start. If the folder is not writable it explains and points at the release page.
- **default** preset under *Show:*, left of *everything*: every category except Sleep / wake.
- **Installed updates** heading on cards, with updates grouped into Windows, Security, Drivers, Apps and Other. Driver updates now get their descriptions from Windows Update history too (it was keyed by KB number only).
- **Driver labels.** Known driver and system module names are followed by a plain-English label wherever they appear in summaries, details or log messages, e.g. `nvlddmkm.sys (NVIDIA kernel-mode display driver)`, `Netwtw04.sys (Intel wireless adapter driver)`.

- **Search improvements.** An ✕ button (or Esc) clears the search box. Search now covers everything on a card: title, summary, category, dates, installed update titles, KB numbers, descriptions, classifications and groups, details, log records and link labels. Multi-word queries require every word to appear somewhere on the card. Matches are highlighted on the cards, and a collapsed *Installed updates* or *Details and log records* block opens by itself when a match is inside it. Static labels such as the *Installed updates* heading are not searched.

### Changed
- Removed the *Event Viewer* button from the header.
- The *Installed updates* list on a card is collapsed behind a down arrow, like *Details and log records*, so long driver lists no longer push the next card off screen.

### Fixed
- Failed updates are marked *failed* in red on cards in the app, as they already were in the HTML export.

## [0.5.0] - 2026-09-20

### Added
- **Logs from another Windows installation.** Settings → *Open logs…* (or **F9**) picks a drive, a Windows folder or a `winevt\Logs` folder and reads its `System.evtx` and `Application.evtx` instead of this PC's logs. Crash dump paths are remapped onto that drive, the banner names the source, and a *Back to this PC* link returns to the live logs. Also `--source <folder>` for the headless export.
- **Check for updates on startup**, with a toggle in Settings (on by default). A newer release shows as a dismissible banner in the main window.
- **Microsoft Learn links** for STOP codes on blue screen and live kernel event cards.
- **Update details on Windows Update cards**: each update installed shortly before the restart is listed with its short description and classification from Windows Update history, and a link to its Microsoft Support (KB) article.

### Changed
- Version shown in Settings now reads `v0.4.0` style.

## [0.4.0] - 2026-09-20

### Added
- **Settings** dialog, opened from a cog button at the right of the header. It holds the theme picker (moved out of the header), a **Check for updates** button that compares the running version with the latest GitHub Release and links to the download page, and the version number with a link to the repository at the bottom.

### Changed
- GitHub Releases are now titled with just the version tag (for example `v0.3.0`), and the assets are named `WhyDidIReboot.exe` and `WhyDidIReboot-framework-dependent.zip` without a version in the file name. Existing releases were renamed to match.

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

[Unreleased]: https://github.com/WelFedTed/why-did-i-reboot/compare/v0.6.1...HEAD
[0.6.1]: https://github.com/WelFedTed/why-did-i-reboot/compare/v0.6.0...v0.6.1
[0.6.0]: https://github.com/WelFedTed/why-did-i-reboot/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/WelFedTed/why-did-i-reboot/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/WelFedTed/why-did-i-reboot/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/WelFedTed/why-did-i-reboot/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/WelFedTed/why-did-i-reboot/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/WelFedTed/why-did-i-reboot/releases/tag/v0.1.0
