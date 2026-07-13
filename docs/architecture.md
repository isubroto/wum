---
title: Architecture
layout: default
parent: Internals
nav_order: 1
description: "How WUM is structured: engine, services, models, helpers, DI, live Windows Update state, logging."
---

# Architecture
{: .no_toc }

1. TOC
{:toc}

---

[← Docs index](index.md)

WUM is a two-project solution plus tests. The CLI is the presentation layer; Core is the platform/business layer with no UI dependencies.

```
wum.slnx
├── src/
│   ├── WUM.CLI          # Console app — commands, rendering, entry point
│   └── WUM.Core         # Services, models, system helpers
└── tests/
    └── WUM.CLI.Tests    # xUnit + Moq + FluentAssertions
```

---

## How it works (the engine)

`UpdateService` is the core engine. It generates PowerShell scripts that query the Settings-backed **Windows Update Manager** API and the **WUA COM API** (`Microsoft.Update.Session`), runs them via `PowerShellHelper`, and parses their JSON into C# models with `System.Text.Json`.

```
Command (CLI)
   │  resolves IUpdateService from DI
   ▼
UpdateService.GetAvailableUpdatesAsync(...)
   │  1. optionally request a user-initiated OS refresh
   │  2. query WindowsUpdateManager applicability/action state
   │  3. return active state immediately, or enrich via online WUA
   │  4. merge exact identities and live status
   │  5. parse JSON → List<WindowsUpdate>
   ▼
powershell.exe → Windows.Management.Update + Microsoft.Update.Session
```

Registry-backed operations (pause, schedule, settings) skip PowerShell and use `RegistryHelper` for direct `HKLM` access. Reboot/uninstall shell out to `shutdown.exe` / `wusa.exe`.

**Why PowerShell + COM?** WUA's COM interfaces are awkward to consume directly from .NET and behave differently under elevation. Shelling to `powershell.exe -ExecutionPolicy Bypass` gives a reliable, admin-friendly path and keeps update queries out of the main process.

---

## WUM.Core (class library)

`net10.0-windows`. No UI; every operation returns a domain model.

### Models (`Models/`)

| Type | Key members |
|---|---|
| `WindowsUpdate` | WUA identity/revision/source metadata; title, KB, type, categories, and status; installed/downloaded/hidden/browse-only/beta/reboot/applicability flags; computed `IsSecurityUpdate` (Security **or** Critical) and `FormattedSize` |
| `UpdateHistory` | `Title`, `KBArticle`, `InstalledDate`, `Success`, `ResultCode`, `ErrorMessage`, `Operation` |
| `UpdateSettings` | Auto-update flags, `ActiveHoursStart/End`, notification flags, `PauseOnMeteredConnection`, `MaxBandwidthPercent`, `DeferFeatureUpdatesDays`, `DeferQualityUpdatesDays` |
| `UpdateSchedule` | `Enabled`, `Day`, `Time`, `AutoInstall`, `AutoReboot`, `InstallAll`; methods `IsWithinSchedule(now)`, `NextRun()` |
| `PauseInfo` | `IsPaused`, `PausedOn`, `PausedUntil`, computed `DaysLeft` |

**Enums**

```csharp
enum UpdateStatus   { NotStarted, Downloading, Downloaded, Installing,
                      Installed, Failed, PendingReboot, Hidden, Available,
                      PendingInstallation, RestartRequired,
                      SupersededOrNotApplicable }

enum UpdateCategory { Security, Critical, Optional, Driver, FeatureUpdate,
                      CumulativeUpdate, Definition, ServicePack, Unknown }
```

Live orchestrator and WUA results map current action/progress, action result, attention reason, `IsInstalled`, `IsHidden`, `IsDownloaded`, and actual reboot state to the user-facing status. Downloaded updates become `PendingInstallation`; installed, superseded, and no-longer-applicable updates are not shown as available.

Category is derived from all WUA category names plus update type and title. This recognizes drivers, Insider Preview and feature upgrades, cumulative updates, security intelligence/Defender definitions, security/critical updates, and service packs; remaining applicable items are optional.

### Services (`Services/`)

| Interface | Implementation | Responsibility |
|---|---|---|
| `IUpdateService` | `UpdateService` | Search / download / install / uninstall / hide / history / diagnose / reset — the engine. Owns live orchestrator/WUA queries and `LastError` |
| `IPauseService` | `PauseService` | Pause/resume via WU pause registry values |
| `ISchedulerService` | `SchedulerService` | Persist/read/clear the weekly schedule |
| `ISettingsService` | `SettingsService` | Map setting keys → `UpdateSettings`, save/reset |
| `IHistoryService` | `HistoryService` | History queries (`GetHistoryAsync`, `GetFailedAsync`) |

`IUpdateService` surface:

```csharp
Task<List<WindowsUpdate>> GetAvailableUpdatesAsync(
    bool includeHidden = false, bool useMicrosoftUpdate = false,
    bool forceRefresh = false, CancellationToken ct = default);
Task<List<WindowsUpdate>> GetInstalledUpdatesAsync(CancellationToken ct = default);
Task<List<UpdateHistory>> GetUpdateHistoryAsync(int count = 50);
string? LastError { get; }                 // set by update mutations on failure
Task<bool> DownloadUpdateAsync(string updateId, IProgress<double>? p = null, CancellationToken ct = default);
Task<bool> InstallUpdateAsync(string updateId, IProgress<double>? p = null, CancellationToken ct = default);
Task<bool> UninstallUpdateAsync(string kbArticle);
Task<bool> HideUpdateAsync(string updateId);
Task<bool> UnhideUpdateAsync(string updateId);
Task<UpdateSettings> GetSettingsAsync();
Task SaveSettingsAsync(UpdateSettings settings);
bool IsRebootRequired();
Task<bool> CommitAndScheduleRebootAsync(
    int secondsDelay, string message, CancellationToken ct = default);
Task<string> GetServiceStatusAsync();
Task<string> DiagnoseAsync();
Task<string> ResetComponentsAsync();       // destructive
```

> **`SettingsService` depends on `IUpdateService`** — `GetAsync`/`SaveAsync` delegate to `GetSettingsAsync`/`SaveSettingsAsync` on the engine, which reads/writes the registry. So the DI registration order matters only in that both must be present (they are; all singletons).

### Helpers (`Helpers/`)

| Helper | Purpose |
|---|---|
| `PowerShellHelper` | `RunScriptAsync` (returns `(Success, Output, Error)`), `RunCommandAsync` (cmd.exe), `ScheduleRebootAsync`, `CancelRebootAsync`. Runs `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass` |
| `RegistryHelper` | Typed `GetValue<T>`, `SetValue` (auto-picks DWord/QWord/String), `DeleteValue`, `KeyExists`. Opens `Registry64` view of `HKLM` by default |
| `AdminHelper` (Core) | Admin role check |
| `WuErrorCodes` | Maps common WU/WININET HRESULTs (0x8024xxxx, 0x80072xxx, 0x80070xxx) to human causes; `Decode(text)` extracts the first `0x…` code from a message |

---

## WUM.CLI (console app)

`net10.0-windows`, `win-x64`, `AssemblyName=wum`. Uses **System.CommandLine** (2.0.0-beta4) for parsing and **Microsoft.Extensions.DependencyInjection** for wiring.

### Commands (`Commands/`)

Each command is a self-contained class with a `Build()` that returns a `System.CommandLine.Command`. The constructor takes `IServiceProvider` and resolves the services it needs. The 12 commands: `Status`, `List`, `Search`, `Install`, `Uninstall`, `Hide`, `History`, `Pause`, `Schedule`, `Settings`, `Reboot`, `Diagnose`.

> Commands with more than ~8 options (e.g. `list`, `install`, `diagnose`) use the **context handler** form `SetHandler(async ctx => ...)` and pull each value with `ctx.ParseResult.GetValueForOption(...)`, because the typed `SetHandler` overloads cap out in beta4.

### Rendering helpers (`Helpers/`)

| Helper | Purpose |
|---|---|
| `ConsoleRenderer` | Box-drawn headers, section headers, spinners (`ShowSpinnerAsync`), colored status/count lines, dividers, `Confirm` prompts, hints/warnings/errors |
| `TableRenderer` | Tabular update + history rendering |
| `ProgressRenderer` | Download/install progress bars (`Update`, `Complete`) |
| `AdminHelper` (CLI) | `IsRunningAsAdmin`, `RequireAdmin` (stops modifying commands when not elevated) |

UTF-8 console output is forced in `Main` so glyphs (`✓ ✗ ↳ ● ○`) render.

---

## Dependency Injection

All services are singletons, registered in `Program.cs`:

```csharp
services = new ServiceCollection()
    .AddSingleton<RegistryHelper>()
    .AddSingleton<IUpdateService,    UpdateService>()
    .AddSingleton<IPauseService,     PauseService>()
    .AddSingleton<IHistoryService,   HistoryService>()
    .AddSingleton<ISchedulerService, SchedulerService>()
    .AddSingleton<ISettingsService,  SettingsService>()
    .BuildServiceProvider();
```

Commands are constructed with this provider and resolve dependencies via `sp.GetRequiredService<T>()`.

---

## Live Windows Update scans

Available-update results are not persisted or shared between commands. Each `status`, `list`, `search`, install planning, and diagnostics query asks the operating system for current applicability and action state.

The default search uses Windows' effective update source and policy configuration. That keeps managed WSUS devices on their configured source and lets Windows Update offer applicable Insider Preview builds, feature updates, cumulative updates, drivers, security intelligence, and optional updates. `--microsoft-update` / `--mu` explicitly adds the Microsoft Update service for other Microsoft products where supported.

On Windows builds exposing `Windows.Management.Update`, WUM queries `WindowsUpdateManager.GetApplicableUpdates()` before WUA. This is the live orchestrator state used by Settings: current download/install/finalization action, progress, restart attention, and action failure/HRESULT. Active or attention-required results return immediately instead of blocking behind a WUA search. Quiet default-visible results allow up to 30 seconds for WUA catalog enrichment, then fall back to the complete orchestrator view (including an authoritative empty result). Matching is GUID-first because the orchestrator's `:revision` suffix is not WUA's revision number.

`--hidden` and `--microsoft-update` / `--mu` request data outside the default orchestrator view, so those expanded queries require a complete online WUA result and never substitute an incomplete fallback. If `Windows.Management.Update` is unavailable, ordinary scans also use WUA.

`forceRefresh: true` (the `--refresh` option where exposed) does not bypass an application cache. On builds exposing `Windows.Management.Update`, it calls `WindowsUpdateManager.StartScan(true)` and waits for the user-initiated orchestrator scan before querying current state. If that newer API is unavailable, WUM logs the fallback and still performs an uncached online WUA search. Installation performs an exact-identity online verification, then the install command runs a full live query so superseded and newly applicable offers are refreshed. Pending feature updates are finalized with `IUpdateInstaller4.Commit(0)` immediately before WUM schedules a reboot. A machine-wide mutex plus durable `Started`/`Committed` boot-session states prevent concurrent or interrupted commands from issuing a second unsupported commit.

---

## Logging

Structured logging via **Serilog**, daily rolling file:

```
%ProgramData%\WUM\logs\wum-YYYY-MM-DD.log
%LocalAppData%\WUM\logs\wum-YYYY-MM-DD.log  # standard-user sessions
```

- Minimum level: `Debug`
- Retention: 7 files (7 days)
- Template: `{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}`

`Program.Main` logs startup args and the final exit code, then flushes with `Log.CloseAndFlushAsync()`.

Next: [Configuration →](configuration.md)
