# PSUM Check Interrogation WinUI 3

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

## Features

### Live battery diagnostics

The current window reads charge percentage, AC state, battery state, and Windows' remaining-time estimate through `GetSystemPowerStatus`. It refreshes those values at a selectable 10–300 second interval. On launch and manual refresh, it also queries Windows battery WMI for design capacity, full-charge capacity, cycle count, voltage, temperature, power flow, and remaining capacity. Capacity health and wear follow the original PSUM thresholds, including a replacement alert below 30%. Unsupported firmware values remain `Unknown`.

### Check-ins and evidence

Manual check-ins save the current charge, state, health, cycle count, an optional note, a hashed battery identity, and a stable Evidence ID. The history view reads up to the latest 1,000,000 check-ins and can filter by battery, state, and local date range. Select a record to export its evidence as UTF-8 `.txt`, or export the currently shown records as UTF-8 `.csv`. CSV text fields that look like spreadsheet formulas are prefixed with an apostrophe. The WinUI edition reads and writes the same `%LOCALAPPDATA%\PSUM\psum.sqlite3` check-in table as the original Python app. It uses Windows' built-in SQLite library and adds missing Evidence IDs for legacy check-ins on first open.

### Navigation

Use the navigation menu to switch between **Dashboard**, **Check-in**, **History**, **Battery Storage**, **Lenovo Snapshots**, **HMM Parts Map**, **Alerts**, **Data Sources & Thresholds**, **Backup & Restore**, and **About**. The menu adapts to narrower windows. History reloads when opened, and the Check-in note remains in place when switching sections.

### Backup and restore

**Backup & Restore** creates a consistent SQLite backup. Restore checks the selected file's integrity and required PSUM tables, creates a dated safety copy of the current database, then restores the selected backup. Close the original Python PSUM app before restoring. The confirmation dialog shows the selected file before any current records are replaced.

### Stored-battery inspections

**Battery Storage** reads Lenovo's retained Power Manager profiles without writing to the registry. A cached slot mapping can be stale and does not prove which battery is installed. The pre-reset capacity ratio is not live battery health. Saved lifecycle states and physical inspections use the original PSUM SQLite tables; inspection history and four-month check dates are shown for stored batteries, with a seven-day follow-up after an incomplete inspection. A reported physical hazard stops measurement entry and sets the profile to **Attention required**. In-app reminders appear for inspections due within 14 days.

### Lenovo cached snapshots

**Lenovo Snapshots** queries the read-only `root/Lenovo:Lenovo_Battery` Vantage WMI cache once at launch and on explicit request. It saves one current row per battery per local day in the shared PSUM database, archiving a same-day replacement in `lenovo_battery_snapshot_revisions`. Failed queries leave saved data intact. The displayed timestamp is PSUM's query time; Vantage cache freshness is unknown. Access to this WMI class may be denied on some PCs.

### Data sources and thresholds

**Data Sources & Thresholds** labels Windows power API, Windows battery WMI, Lenovo WMI cache, and Lenovo registry cache readings. It compares current charge and charging state with user-entered start/stop percentages, saved in `app_settings`. The comparison does not read or change active firmware thresholds.

The extended Windows WMI read also shows remaining capacity, accepted and reported voltage, design voltage, temperature, and charge/discharge rate when firmware exposes them. Implausible reported voltage is retained as reported evidence but not accepted as a valid live voltage.

Saved Lenovo snapshots can be filtered by battery and local date, compared side by side, and exported as CSV. The selected snapshot shows reference gauges, a chassis illustration with callouts from the saved Lenovo row, saved capacity/cycle trends, and significant saved-observation changes (at least five capacity-ratio points or 50 cycles). The illustration is a reference only; its callouts do not identify sensor locations or give a safety verdict. These are cached observations, not live measurements. The page reads the Lenovo Commercial Vantage WMI battery policy without changing it.

Battery Storage inspection history can be filtered by result and date, compared, exported as UTF-8 text evidence, or exported as CSV. History also supports confirmed deletion of a selected check-in. **Alerts** lists due stored-battery checks, service attention, critical live capacity health, and significant saved Lenovo cache changes. A launch reminder preference is saved locally. The Dashboard includes selectable 10–300 second refresh, tip cards, and keyboard navigation. The **HMM Parts Map** is a read-only L15 Gen 2 parts reference copied from the original PSUM assets, with the same Lenovo-authorized service note. **About** links to Lenovo's Battery Q&A.

## Local data and limitations

Check-ins, inspections, saved snapshots, and settings use `%LOCALAPPDATA%\PSUM\psum.sqlite3`. Exports and backups are saved to the location you select. Back up this database before moving data or restoring an older copy.

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
