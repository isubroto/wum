---
title: Architecture
layout: default
parent: Internals
nav_order: 1
description: "How WUM is structured: engine, services, models, helpers, DI, cache, logging."
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

`UpdateService` is the core engine. It does **not** use a WUA managed wrapper — instead it generates PowerShell scripts that drive the **WUA COM API** (`Microsoft.Update.Session`), runs them via `PowerShellHelper`, and parses the JSON they emit (`ConvertTo-Json`) into C# models with `System.Text.Json`.

```
Command (CLI)
   │  resolves IUpdateService from DI
   ▼
UpdateService.GetAvailableUpdatesAsync(...)
   │  1. check scan cache (unless forceRefresh)
   │  2. build PS script (criteria, optional Microsoft Update service)
   │  3. PowerShellHelper.RunScriptAsync(script)
   │  4. parse JSON → List<WindowsUpdate>
   │  5. write scan cache
   ▼
powershell.exe → Microsoft.Update.Session (WUA COM)
```

Registry-backed operations (pause, schedule, settings) skip PowerShell and use `RegistryHelper` for direct `HKLM` access. Reboot/uninstall shell out to `shutdown.exe` / `wusa.exe`.

**Why PowerShell + COM?** WUA's COM interfaces are awkward to consume directly from .NET and behave differently under elevation. Shelling to `powershell.exe -ExecutionPolicy Bypass` gives a reliable, admin-friendly path and keeps update queries out of the main process.

---

## WUM.Core (class library)

`net10.0-windows`. No UI; every operation returns a domain model.

### Models (`Models/`)

| Type | Key members |
|---|---|
| `WindowsUpdate` | `Id`, `Title`, `Description`, `KBArticle`, `Category`, `Status`, `IsMandatory`, `IsHidden`, `RequiresReboot`, `SizeInBytes`, `SupportUrl`, `Severity`; computed `IsSecurityUpdate` (Security **or** Critical) and `FormattedSize` |
| `UpdateHistory` | `Title`, `KBArticle`, `InstalledDate`, `Success`, `ResultCode`, `ErrorMessage`, `Operation` |
| `UpdateSettings` | Auto-update flags, `ActiveHoursStart/End`, notification flags, `PauseOnMeteredConnection`, `MaxBandwidthPercent`, `DeferFeatureUpdatesDays`, `DeferQualityUpdatesDays` |
| `UpdateSchedule` | `Enabled`, `Day`, `Time`, `AutoInstall`, `AutoReboot`, `InstallAll`; methods `IsWithinSchedule(now)`, `NextRun()` |
| `PauseInfo` | `IsPaused`, `PausedOn`, `PausedUntil`, computed `DaysLeft` |

**Enums**

```csharp
enum UpdateStatus   { NotStarted, Downloading, Downloaded, Installing,
                      Installed, Failed, PendingReboot, Hidden }

enum UpdateCategory { Security, Critical, Optional, Driver, FeatureUpdate,
                      CumulativeUpdate, Definition, ServicePack, Unknown }
```

Category is derived from the WUA category name by `ParseCategory` (substring match: `security`, `critical`, `driver`, `feature`, `cumulative`, `definition`, `service pack`; else `Optional`).

### Services (`Services/`)

| Interface | Implementation | Responsibility |
|---|---|---|
| `IUpdateService` | `UpdateService` | Search / download / install / uninstall / hide / history / diagnose / reset — the engine. Owns the scan cache and `LastError` |
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
string? LastError { get; }                 // set by Download/Install on failure
Task<bool> DownloadUpdateAsync(string updateId, IProgress<double>? p = null, CancellationToken ct = default);
Task<bool> InstallUpdateAsync(string updateId, IProgress<double>? p = null, CancellationToken ct = default);
Task<bool> UninstallUpdateAsync(string kbArticle);
Task<bool> HideUpdateAsync(string updateId);
Task<bool> UnhideUpdateAsync(string updateId);
Task<UpdateSettings> GetSettingsAsync();
Task SaveSettingsAsync(UpdateSettings settings);
bool IsRebootRequired();
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

## Scan cache

To make `status`, `list`, and `diagnose` **agree on the update count**, available-update scans are cached. WUA online results drift between calls (e.g. Defender definitions republish hourly), so without a cache the three commands could each show a different number.

| Property | Value |
|---|---|
| **Location** | `%ProgramData%\WUM\cache\available-h{0/1}-m{0/1}.json` |
| **Key** | `h` = include-hidden, `m` = Microsoft Update — so variants never collide |
| **TTL** | 5 minutes (`CacheTtl`) |
| **Envelope** | `{ TimestampUtc, Updates[] }` |
| **Bypass** | `forceRefresh: true` (CLI `--refresh`) — re-scans and rewrites the cache |
| **Invalidation** | The whole cache dir is deleted after any state-changing op: install, uninstall, hide, unhide, component reset |

Best-effort: any IO/JSON error is treated as a cache miss and a live scan runs. Read path: `ReadCache`; write path: `WriteCache`; nuke: `InvalidateCache`.

---

## Logging

Structured logging via **Serilog**, daily rolling file:

```
%ProgramData%\WUM\logs\wum-YYYY-MM-DD.log
```

- Minimum level: `Debug`
- Retention: 7 files (7 days)
- Template: `{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}`

`Program.Main` logs startup args and the final exit code, then flushes with `Log.CloseAndFlushAsync()`.

Next: [Configuration →](configuration.md)
