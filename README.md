# PSUM Check Interrogation WinUI 3

[![Build on main](https://github.com/KyotoBlazeDev/PSUM-Check-Interrogation/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/KyotoBlazeDev/PSUM-Check-Interrogation/actions/workflows/build.yml)
[![CodeQL analysis on main](https://github.com/KyotoBlazeDev/PSUM-Check-Interrogation/actions/workflows/codeql.yml/badge.svg?branch=main)](https://github.com/KyotoBlazeDev/PSUM-Check-Interrogation/actions/workflows/codeql.yml)
[![Latest stable release](https://img.shields.io/github/v/release/KyotoBlazeDev/PSUM-Check-Interrogation?label=stable%20release)](https://github.com/KyotoBlazeDev/PSUM-Check-Interrogation/releases/latest)
[![License](https://img.shields.io/github/license/KyotoBlazeDev/PSUM-Check-Interrogation)](LICENSE)
[![Platform: Windows 10 1809 or later, x64 installer](https://img.shields.io/badge/platform-Windows%2010%201809%2B%20%7C%20x64-0078D4)](#install)
[![Current release signing: unsigned](https://img.shields.io/badge/signing-unsigned-orange)](#automated-analysis-and-security)

[Install](#install) · [Standard / Business modes](#standard-and-business-modes-development) · [Features](#features) · [Build](#build-and-run) · [Security](#automated-analysis-and-security) · [Report a bug](https://github.com/KyotoBlazeDev/PSUM-Check-Interrogation/issues)

PSUM Check Interrogation is a Windows battery monitoring and inspection tool in the **PSUM family**, with local check-ins, evidence exports, stored-battery inspections, and Lenovo cached observations. This repository contains the unpackaged **C# / WinUI 3 edition**, ported from the original Python application.

**Current stable release:** [v1.0.0](https://github.com/KyotoBlazeDev/PSUM-Check-Interrogation/releases/tag/v1.0.0), released 7 October 2026. The WinUI 3 edition is in maintenance. The app and installer are currently unsigned.

## Install

1. Download the x64 setup executable and matching `.sha256` file from the [official releases](https://github.com/KyotoBlazeDev/PSUM-Check-Interrogation/releases).
2. Compare the downloaded setup's SHA-256 hash with the checksum file before running it:

   ```powershell
   Get-FileHash -LiteralPath '.\PSUM-Check-Interrogation-1.0.0-Setup-x64.exe' -Algorithm SHA256
   ```

3. Run setup, then open **PSUM Check Interrogation** from the Start menu.

Setup requires Windows 10 version 1809 or later with x64 compatibility and includes the .NET runtime and Windows App SDK files. It installs for the current user without requiring administrator privileges. Battery readings depend on hardware and firmware; Lenovo WMI may be unavailable or access-denied.

## Standard and Business modes (development)

The working source includes the unified modes described below. These additions have not yet been published in stable v1.0.0.

The unified app supports **Standard** and **Business** modes. Open **Mode & Settings** in the navigation footer to switch immediately. The preference is saved per Windows user; first launch defaults to Standard.

Standard retains the personal dashboard, check-ins, history, stored-battery inspections, Lenovo snapshots, alerts and backup/restore. Business adds the maintenance workspace from PSUM Check - Business Edition: asset tags and label provenance, five physical observations, 1–365 day inspection intervals (90 by default), replacement tickets and inventory IDs, lifecycle trends, due reminders, CSV/JSON audit exports and diagnostic logs. Switching mode retains your records and unfinished form entries.

| Capacity policy | Standard | Business |
| --- | --- | --- |
| ≥80% | Good | Healthy |
| 70–<80% | Service recommended | Watch |
| 60–<70% | Service recommended | Degraded |
| 50–<60% | Poor | Degraded |
| 30–<50% | Poor | Critical |
| <30% | Critical | Critical |
| Unavailable | Unknown | Unknown |

The dashboard and alerts use the selected policy; Business audit exports always use Business policy. These are capacity estimates and application policies. Physical hazards take precedence in Business inspection advice; unavailable readings remain Unknown.

Both modes use the existing `%LOCALAPPDATA%\PSUM\psum.sqlite3` telemetry database. Business audit events reuse `%LOCALAPPDATA%\PSUM\BusinessEdition\events`, so existing Business inspection records are available. The separate legacy Business `telemetry.sqlite3` is retained and is not automatically merged; use Backup & Restore to explicitly restore a compatible legacy telemetry backup, which replaces the shared telemetry history after confirmation and a safety backup. SQLite backups do not include Business audit files: export JSON from the Business workspace or separately back up the events folder.

Business audit history is append-only at application level, not tamper-proof. It remains local and per Windows user, with no cloud inventory or background fleet collection. Diagnostic logs retain the Business Edition log location and bounded rotation, and omit asset tags, notes, serials and operator identity. The unified app keeps this repository's existing unpackaged build and installer identity.

Run `dotnet run --project Verification/Verification.csproj` to verify mode persistence, both capacity policies, battery parsing, physical hazard precedence, audit persistence/overwrite protection, export escaping, diagnostic retention, and telemetry backup/restore.

## Features

### Live battery diagnostics

The current window reads charge percentage, AC state, battery state, and Windows' remaining-time estimate through `GetSystemPowerStatus`. It refreshes those values at a selectable 10–300 second interval. On launch and manual refresh, it also queries Windows battery WMI for design capacity, full-charge capacity, cycle count, voltage, temperature, power flow, and remaining capacity. Capacity health and wear follow the selected mode policy: the replacement alert opens below 30% in Standard mode and below 50% in Business mode. Unsupported firmware values remain `Unknown`.

### Check-ins and evidence

Manual check-ins save the current charge, state, health, cycle count, an optional note, a hashed battery identity, and a stable Evidence ID. The history view reads up to the latest 1,000,000 check-ins and can filter by battery, state, and local date range. Select a record to export its evidence as UTF-8 `.txt`, or export the currently shown records as UTF-8 `.csv`. CSV text fields that look like spreadsheet formulas are prefixed with an apostrophe. The WinUI edition reads and writes the same `%LOCALAPPDATA%\PSUM\psum.sqlite3` check-in table as the original Python app. It uses Windows' built-in SQLite library and adds missing Evidence IDs for legacy check-ins on first open.

### Navigation

Use the navigation menu to switch between **Dashboard**, **Check-in**, **History**, **Battery Storage**, **Lenovo Snapshots**, **HMM Parts Map**, **Alerts**, **Data Sources & Thresholds**, **Backup & Restore**, **Mode & Settings**, and **About**. Business mode also shows **Business workspace**, and labels Dashboard and History as Live telemetry and Telemetry history. The menu adapts to narrower windows. History reloads when opened, and the Check-in note remains in place when switching sections.

### Backup and restore

**Backup & Restore** creates a consistent SQLite backup. Restore checks the selected file's integrity and required PSUM tables, creates a dated safety copy of the current database, then restores the selected backup. Close the original Python PSUM app before restoring. The confirmation dialog shows the selected file before any current records are replaced.

### Stored-battery inspections

**Battery Storage** reads Lenovo's retained Power Manager profiles without writing to the registry. A cached slot mapping can be stale and does not prove which battery is installed. The pre-reset capacity ratio is not live battery health. Saved lifecycle states and physical inspections use the original PSUM SQLite tables; inspection history and four-month check dates are shown for stored batteries, with a seven-day follow-up after an incomplete inspection. A reported physical hazard stops measurement entry and sets the profile to **Attention required**. In-app reminders appear for inspections due within 14 days.

### Lenovo cached snapshots

**Lenovo Snapshots** queries the read-only `root/Lenovo:Lenovo_Battery` Vantage WMI cache once at launch and on explicit request. It saves one current row per battery per local day in the shared PSUM database, archiving a same-day replacement in `lenovo_battery_snapshot_revisions`. Failed queries leave saved data intact. The displayed timestamp is PSUM's query time; Vantage cache freshness is unknown. Access to this WMI class may be denied on some PCs.

### Enable Lenovo battery WMI snapshots

On supported ThinkPads with Commercial Vantage installed, enable **Write Battery Information to WMI** under:

**Computer Configuration → Administrative Templates → Commercial Vantage → Device → Device Settings → Power**

For local Group Policy setup:

1. Copy `CommercialVantage.admx` from Lenovo's deployment package to `C:\Windows\PolicyDefinitions`.
2. Copy its matching `en-US\CommercialVantage.adml` to `C:\Windows\PolicyDefinitions\en-US`.
3. Open `gpedit.msc`, enable the policy above, and configure its daily, weekly, or monthly schedule.

Domain administrators can use the Group Policy Central Store. Lenovo also documents registry deployment and Intune configuration, so templates are the Group Policy configuration route rather than a runtime dependency of PSUM.

Commercial Vantage populates `ROOT\Lenovo:Lenovo_Battery` on the configured schedule. Disabled or unconfigured policy does not write battery information. Installing templates alone does not enable collection; PSUM's refresh queries the cache.

See [Lenovo's configuration guide](https://docs.lenovocdrt.com/guides/lcv/configuration/#battery-information). PSUM does not enable this policy or install templates.

### Data sources and thresholds

**Data Sources & Thresholds** labels Windows power API, Windows battery WMI, Lenovo WMI cache, and Lenovo registry cache readings. It compares current charge and charging state with user-entered start/stop percentages, saved in `app_settings`. The comparison does not read or change active firmware thresholds.

The extended Windows WMI read also shows remaining capacity, accepted and reported voltage, design voltage, temperature, and charge/discharge rate when firmware exposes them. Implausible reported voltage is retained as reported evidence but not accepted as a valid live voltage.

Saved Lenovo snapshots can be filtered by battery and local date, compared side by side, and exported as CSV. The selected snapshot shows reference gauges, a chassis illustration with callouts from the saved Lenovo row, saved capacity/cycle trends, and significant saved-observation changes (at least five capacity-ratio points or 50 cycles). The illustration is a reference only; its callouts do not identify sensor locations or give a safety verdict. These are cached observations, not live measurements. The page reads the Lenovo Commercial Vantage WMI battery policy without changing it.

Battery Storage inspection history can be filtered by result and date, compared, exported as UTF-8 text evidence, or exported as CSV. History also supports confirmed deletion of a selected check-in. **Alerts** lists due stored-battery checks, service attention, critical live capacity health, and significant saved Lenovo cache changes. A launch reminder preference is saved locally. The Dashboard includes selectable 10–300 second refresh, tip cards, and keyboard navigation. The **HMM Parts Map** is a read-only L15 Gen 2 parts reference copied from the original PSUM assets, with the same Lenovo-authorized service note. **About** links to Lenovo's Battery Q&A.

## Local data and limitations

Check-ins, inspections, saved snapshots, and settings use `%LOCALAPPDATA%\PSUM\psum.sqlite3`. Exports and backups are saved to the location you select. The mode preference is saved in `%LOCALAPPDATA%\PSUM\mode.txt`. Back up this database and the separate Business audit events before moving data or restoring an older copy.

Hardware interrogation is read-only. The app records local observations and inspections; it does not change battery firmware or Lenovo charge thresholds. Cached Lenovo readings and retained registry profiles must remain distinguishable from live Windows telemetry. A capacity ratio, reference illustration, or inspection record does not certify a battery's physical safety.

## Build and run

Build on Windows with the **.NET 10 SDK**, matching the repository workflows. The application targets `net8.0-windows10.0.19041.0`; the SDK version used to build it is separate from its target framework.

Open the solution in Visual Studio and select **PSUM Check Interrogation WinUI 3 (Unpackaged)**, or run:

```powershell
dotnet run --project '.\PSUM Check Interrogation WinUI 3.csproj' -p:Platform=x64
```

## Publish without installing an MSIX

```powershell
dotnet publish '.\PSUM Check Interrogation WinUI 3.csproj' -c Release -p:Platform=x64 -r win-x64 --self-contained true
```

Copy the **entire** `bin\Release\net8.0-windows10.0.19041.0\win-x64\publish` folder to the target Windows PC and run `PSUM Check Interrogation WinUI 3.exe` from that folder. The folder includes the .NET runtime and Windows App SDK binaries; the app does not require MSIX installation. It remains a multi-file deployment and requires a compatible Windows version and architecture. Use `win-x86` or `win-arm64` with the matching platform for those targets.

## Setup installer and GitHub Releases

The **Build** workflow publishes the self-contained x64 app, compiles an Inno Setup installer, verifies silent install/uninstall and installed file hashes on the disposable runner, and uploads the installer and its SHA-256 checksum as Actions artifacts on pushes and pull requests to `main`.

For a new release, open **Actions → Release → Run workflow**, select the intended source ref, enter a new `X.Y.Z` version, and choose whether it is a prerelease. A successful run creates a **draft** `vX.Y.Z` release containing `PSUM-Check-Interrogation-X.Y.Z-Setup-x64.exe` and its `.sha256` file. Download and test the setup, edit the release notes, then publish the draft. Publishing a release with a `vX.Y.Z` tag also rebuilds and attaches the installer from that tag. A manual run refuses an existing tag that points to a different commit.

Setup installs the whole app under `%LOCALAPPDATA%\Programs\PSUM Check Interrogation`, creates a Start menu shortcut, and offers an optional desktop shortcut. It requires Windows 10 version 1809 or later with x64 compatibility. Updates reuse the same install location and app identity. Uninstall removes installed application files and retains the PSUM database under `%LOCALAPPDATA%\PSUM`.

Build and Release currently produce unsigned artifacts. The release workflow uses the repository's built-in `GITHUB_TOKEN`; production signing credentials are not configured in these workflows.

To build setup locally, install the .NET 10 SDK and Inno Setup 6.3 or newer, then run:

```powershell
./scripts/Build-Installer.ps1 -Version 1.0.0
```

Output is written to `artifacts\installer`. The script verifies required WinUI resources and runtime files before compiling setup. It checks for `ISCC.exe` on PATH, then in the default Inno Setup 6 installation directory, and stops with a clear error if it is missing. For a custom location, pass `-IsccPath 'C:\path\to\ISCC.exe'`. GitHub's `windows-2022` runner includes Inno Setup.

The installer smoke test in `scripts/Test-Installer.ps1` runs only on a disposable GitHub Actions runner. It checks silent installation, installed payload hashes, and silent uninstallation; it does not validate battery telemetry or interactive UI behavior.

## Automated analysis and security

[CodeQL Advanced](.github/workflows/codeql.yml) runs on pushes and pull requests to `main`, manual dispatch, and a weekly schedule. It analyzes:

| Area | Configuration |
| --- | --- |
| C# | Manual Release x64 WinUI build on `windows-2022`, followed by CodeQL analysis |
| GitHub Actions | Workflow analysis on `ubuntu-latest` without a build |

The current C# configuration uses an explicit Windows build rather than autobuild. A configured scan is not a guarantee of a clean result; inspect the relevant workflow run and code-scanning findings.

See [SECURITY.md](SECURITY.md) for supported versions, vulnerability reporting, release integrity, and signing plans. Report ordinary bugs through [Issues](https://github.com/KyotoBlazeDev/PSUM-Check-Interrogation/issues). For suspected vulnerabilities, follow the private-reporting guidance in the security policy and avoid posting exploit details publicly.

Authenticode signing and pre-signing malware scanning are **planned for the 2027 v1.1.0 generation**. They are not implemented in the current Build or Release workflows. Published SHA-256 checksums help verify artifact integrity; they do not establish publisher identity or guarantee vulnerability-free software.

## License

See [LICENSE](LICENSE) for the MIT license.

## Development disclosure

This utility was built with Codex assistance. You may review the source code before installing or modifying it.

<details>
<summary>README badges and GitHub formatting</summary>

Build and CodeQL badges show workflow status for remote `main`; they do not describe uncommitted changes or certify that there are no security findings. The stable-release and license badges use Shields.io repository metadata. The platform and signing badges are maintained manually and should be updated when release requirements or signing change. Remote badge images may be cached or temporarily unavailable; their links open the underlying workflow, release, license or documentation.

This README uses linked Markdown images with descriptive alternative text, heading links, GitHub Flavored Markdown tables, fenced PowerShell examples, and a collapsible HTML `details` / `summary` section. Badge widgets are served as images and require no executable scripts in the README.

Formatting references: [GitHub workflow badges](https://docs.github.com/en/actions/how-tos/monitor-workflows/add-a-status-badge), [GitHub Markdown formatting](https://docs.github.com/en/get-started/writing-on-github/getting-started-with-writing-and-formatting-on-github/basic-writing-and-formatting-syntax), [collapsed sections](https://docs.github.com/en/get-started/writing-on-github/working-with-advanced-formatting/organizing-information-with-collapsed-sections), [Shields.io release badges](https://shields.io/badges/git-hub-release), and [license badges](https://shields.io/badges/git-hub-license).

</details>
