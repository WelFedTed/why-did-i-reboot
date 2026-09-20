# Changelog

All notable changes to Why Did I Reboot are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

When a `vX.Y.Z` tag is pushed, the release workflow copies the matching `## [X.Y.Z]` section below into the GitHub Release notes, so write the notes here first.

## [Unreleased]

### Added
- **Ask AI** link beside *Debug in WinDbg* on crash cards. It has WinDbg run `!analyze -v` on the dump into a log file, keeps the parts that matter (probable cause, module and image names, bucket id, bugcheck parameters, the top of the stack), combines them with the card's "Copy this entry" text into a prompt, copies the prompt to the clipboard, and opens a chat service with the prompt prefilled: ChatGPT (default, no sign-in needed), Microsoft Copilot, Perplexity or Claude, chosen in Settings. If the address would be too long the analysis part is shortened and the full prompt is left on the clipboard. WinDbg is installed through winget first if needed.

### Changed
- **F9** closes the Settings screen when it is already open, and **F5** there presses the update button.
- While Ask AI waits for WinDbg, the app shows a spinner with what is happening and a Cancel link.

### Fixed
- Ask AI failed with "Unable to create a debug session" because the log path inside WinDbg's `-c` command was quoted, which its parser does not support. The log now goes to a folder without spaces (Public profile, then ProgramData, then Temp) and is passed unquoted.

## [0.8.0] - 2026-09-20

### Added
- **Debug in WinDbg** link on crash cards whose dump file is on disk. It opens the dump in WinDbg with `!analyze -v` queued. If WinDbg is not installed, the app offers to install it with winget (`Microsoft.WinDbg`) in a visible console window and opens the dump when that finishes; without winget it opens the Microsoft Store page.
- **Custom range…** in the period drop-down: pick a from/to date (with this month / last month / this year shortcuts). The chosen window appears as its own entry above *Custom range…*. Headless: `--from yyyy-MM-dd --to yyyy-MM-dd`.
- **Open a saved CSV report.** Settings → *Open CSV…*, `--source report.csv`, or drop a `.csv` (or a Windows folder) onto the main window. The CSV export now carries details, links, installed updates and log records in extra columns so a re-opened file shows every card in full; older nine-column files still open.

### Fixed
- Driver updates linked to Windows Update history's generic `support.microsoft.com/select/?target=hub` page, which does not work. Such links are dropped; updates without a KB number now link to a Microsoft Update Catalog search for the title instead.

### Changed
- **F9** now opens the Settings screen (same as the cog) instead of jumping straight to the folder picker for another installation's logs.
- The app shows "N of M shown" under the search box, like the HTML report, and the status bar's right corner shows the app version (e.g. v0.7.0) instead of the count.
- The HTML report's footer and the text report's header name the app version that produced them.

## [0.7.0] - 2026-09-20

### Added
- **Interactive HTML report.** The export now has the same category chips (click to show or hide), the *default / everything / reboots only / problems only / none* presets, and a search box with a clear button, all working offline inside the file. Every search word must appear somewhere on a card; matches are highlighted and a collapsed block opens when a match is inside it. Opening the file with `?q=words` starts with that search.

### Fixed
- The HTML report's search box showed two clear buttons in Chromium-based browsers (the browser's own and the page's); the native one is now hidden.
- The HTML report's *Installed updates* list is collapsed behind a down arrow like the app.
- Monthly Windows updates named "2026-06 Security Update (KB…)" are grouped under *Windows* even when Windows Update history has no classification for them.

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

[Unreleased]: https://github.com/WelFedTed/why-did-i-reboot/compare/v0.8.0...HEAD
[0.8.0]: https://github.com/WelFedTed/why-did-i-reboot/compare/v0.7.0...v0.8.0
[0.7.0]: https://github.com/WelFedTed/why-did-i-reboot/compare/v0.6.1...v0.7.0
[0.6.1]: https://github.com/WelFedTed/why-did-i-reboot/compare/v0.6.0...v0.6.1
[0.6.0]: https://github.com/WelFedTed/why-did-i-reboot/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/WelFedTed/why-did-i-reboot/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/WelFedTed/why-did-i-reboot/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/WelFedTed/why-did-i-reboot/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/WelFedTed/why-did-i-reboot/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/WelFedTed/why-did-i-reboot/releases/tag/v0.1.0
