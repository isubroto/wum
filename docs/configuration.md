---
title: Configuration
layout: default
parent: Get Started
nav_order: 2
description: "Exact registry keys and on-disk paths each WUM command reads and writes."
---

# Configuration
{: .no_toc }

1. TOC
{:toc}

---

[← Docs index](index.md)

WUM stores configuration in the Windows registry (the same locations Windows itself uses) and on disk for logs/cache. This page documents the exact keys and values each command writes, so you know precisely what changes on your system.

All registry access is through `RegistryHelper`, which opens the **64-bit view** of `HKEY_LOCAL_MACHINE` (`HKLM`). Writing therefore requires Administrator.

---

## Settings

`wum settings` reads/writes via `SettingsService` → `UpdateService.GetSettingsAsync/SaveSettingsAsync`.

### Registry paths

| Constant | Path (under `HKLM`) |
|---|---|
| UX settings | `SOFTWARE\Microsoft\WindowsUpdate\UX\Settings` |
| AU policy | `SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU` |

### Keys (`wum settings set <key> <value>`)

| Key | Value range | Maps to | Default |
|---|---|---|---|
| `auto-download` | `true`/`false` | `AUOptions ≥ 3` | `true` |
| `auto-install` | `true`/`false` | `AUOptions == 4` | `false` |
| `install-drivers` | `true`/`false` | `InstallDrivers` | `true` |
| `install-optional` | `true`/`false` | `InstallOptional` | `false` |
| `notify-new` | `true`/`false` | `NotifyOnNewUpdates` | `true` |
| `notify-complete` | `true`/`false` | `NotifyOnInstallComplete` | `true` |
| `pause-metered` | `true`/`false` | `AllowAutoWindowsUpdateDownloadOverMeteredNetwork == 0` | `true` |
| `defer-feature` | `0`–`365` | `DeferFeatureUpdatesDays` | `0` |
| `defer-quality` | `0`–`30` | `DeferQualityUpdatesDays` | `0` |
| `active-hours` | `start-end` (e.g. `8-22`) | `ActiveHoursStart` / `ActiveHoursEnd` | `8`–`22` |

> **Boolean parsing:** `true`, `1`, `yes`, `on` → true; anything else → false (`SettingsService.ParseBool`).
>
> **`active-hours`** expects two integers separated by `-`. `wum settings set active-hours 9-18` sets start=9, end=18.

> **Note on persistence:** `SaveSettingsAsync` currently persists **Active Hours** (`ActiveHoursStart`/`ActiveHoursEnd`) to the UX settings key. The displayed values for auto-download/install etc. are read back from the AU policy + UX keys on `GetSettingsAsync`. `wum settings reset` writes a fresh default `UpdateSettings`.

### Examples

```bash
wum settings                              # Show all current values
wum settings set auto-download true
wum settings set active-hours 9-18
wum settings set defer-feature 30
wum settings reset                        # Confirm, then restore defaults
```

---

## Pause

`wum pause` / `wum pause resume` via `PauseService`.

**Path:** `HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings`

**On pause** (`--days N`, clamped 1–35), these string values are written (UTC, `yyyy-MM-ddTHH:mm:ssZ`):

| Value name | Meaning |
|---|---|
| `PauseFeatureUpdatesStartTime` | now (UTC) |
| `PauseFeatureUpdatesEndTime` | now + N days |
| `PauseQualityUpdatesStartTime` | now (UTC) |
| `PauseQualityUpdatesEndTime` | now + N days |
| `PauseUpdatesExpiryTime` | now + N days |

**On resume** all five values are deleted.

**Reading state** (`GetPauseInfoAsync`, used by `wum status`): `IsPaused = now < PauseUpdatesExpiryTime`; `PausedUntil` and `PausedOn` are converted to local time; `DaysLeft` is derived.

```bash
wum pause --days 14            # Sets the five values, expiry = now + 14d
wum pause resume               # Deletes them
wum status                     # Shows "Updates Paused: Yes - until … (Nd left)"
```

---

## Schedule

`wum schedule` via `SchedulerService` (registry-persisted). The model:

```csharp
class UpdateSchedule {
    bool      Enabled;            // false
    DayOfWeek Day;               // Sunday
    TimeSpan  Time;              // 02:00
    bool      AutoInstall;       // false
    bool      AutoReboot;        // false
    bool      InstallAll;        // false (else security only)
}
```

- **`set`** validates `--day` against `DayOfWeek` and `--time` against `HH:mm`, then saves.
- **`NextRun()`** computes the next occurrence of `Day` at `Time` (rolls to next week if today's slot already passed).
- **`IsWithinSchedule(now)`** is true within a 2-hour window from `Time` on the matching day.

```bash
wum schedule set --day Friday --time 03:00 --auto-install --auto-reboot --all
wum schedule show              # Day/Time/Auto Install/Auto Reboot/Install All/Next Run
wum schedule clear
```

---

## Reboot detection

`IsRebootRequired()` checks **update-specific** markers only (kept in sync with the `diagnose` D1 check) — `PendingFileRenameOperations` is deliberately **excluded** (noisy, set by routine installers):

| Registry key (presence = reboot) |
|---|
| `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired` |
| `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending` |

`diagnose` additionally reports `…\RebootRequired\UpdateExeVolatile` as an `UpdateExe` reason. Used by `wum status`, `wum reboot`, and `wum install`'s post-install prompt.

---

## On-disk locations

| Path | Written by | Notes |
|---|---|---|
| `%ProgramData%\WUM\logs\wum-YYYY-MM-DD.log` | Serilog | Daily roll, 7-day retention |
| `%ProgramData%\WUM\cache\available-h{0/1}-m{0/1}.json` | `UpdateService` | Scan cache, 5-min TTL — see [Architecture → Scan cache](architecture.md#scan-cache) |

`%ProgramData%` resolves via `Environment.SpecialFolder.CommonApplicationData` (normally `C:\ProgramData`).

Next: [Diagnostics →](diagnostics.md)
