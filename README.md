<![CDATA[# WUM — Windows Update Manager CLI

A modern, feature-rich command-line tool for managing Windows Updates directly from the terminal. Built with .NET 10 and the Windows Update Agent (WUA) COM API, WUM gives system administrators and power users full control over the Windows Update lifecycle — from listing and installing updates to pausing, scheduling, and diagnosing issues — all without opening the Settings app.

> **Requires:** Windows 10/11 · .NET 10 SDK · **Administrator privileges**

---

## ✨ Features

| Area | Capabilities |
|---|---|
| **Discovery** | List available, installed, and hidden updates with rich filtering by category |
| **Installation** | Download & install updates by KB number, category, or all at once with progress bars |
| **Uninstall** | Remove installed updates by KB article number |
| **Pause / Resume** | Pause Windows Updates for 1–35 days via registry, resume early |
| **Scheduling** | Configure a weekly update schedule (day, time, auto-install, auto-reboot) |
| **Settings** | View and modify Windows Update settings — active hours, deferrals, auto-download, metered network |
| **Hide / Unhide** | Suppress unwanted updates from appearing in the available list |
| **Reboot Management** | Schedule or cancel post-update reboots with configurable delay |
| **Diagnostics** | 10-point health check — WU service, network, COM, proxy, registry, admin, OS version |
| **Output Formats** | Colorized tables, verbose detail view, `--json` for scripting, `--no-color` for piping |

---

## 📦 Quick Start

### Build

```bash
git clone https://github.com/<your-org>/wum.git
cd wum
dotnet build
```

### Run (Debug)

```bash
# From the repo root — runs without enforcing admin in Debug mode
dotnet run --project src/WUM.CLI -- status
```

### Publish (Single-file, Self-contained)

```bash
dotnet publish src/WUM.CLI -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

This produces a single `wum.exe` in `./publish` that requires no .NET runtime on the target machine. The Release build includes an application manifest that auto-elevates to Administrator.

---

## 🚀 Usage

All commands require **Administrator** privileges. In Debug mode, the tool prints a warning but continues with limited functionality.

### `wum status`

Show a dashboard with WU service state, reboot status, pause info, and available update counts by category.

```
wum status              # Colorized dashboard
wum status --json       # Machine-readable JSON
wum status -v           # Verbose debug output (step-by-step)
```

### `wum list`

List available (or installed) updates with category filtering.

```
wum list                  # All available updates (table)
wum list --installed      # Show installed updates instead
wum list --security       # Security updates only
wum list --critical       # Critical updates only
wum list --drivers        # Driver updates only
wum list --definition     # Defender/definition updates only
wum list --hidden         # Include hidden updates
wum list --json           # JSON output
wum list -v               # Full detail per update
wum list --no-color       # Disable ANSI colors
```

### `wum search`

Search for updates matching a keyword.

```
wum search <query>
```

### `wum install`

Download and install updates with an install plan preview, progress bars, and a post-install summary.

```
wum install KB5034441                  # Install a specific KB
wum install KB5034441 KB5035853        # Install multiple KBs
wum install --security                 # Install security updates only
wum install --critical                 # Install critical updates only
wum install --definition               # Install definition/Defender updates
wum install --all                      # Install everything available
wum install --all --force              # Skip confirmation prompt
wum install --all --dry-run            # Preview without making changes
wum install --all --no-reboot          # Don't prompt to reboot after install
```

### `wum uninstall`

Remove a previously installed update.

```
wum uninstall <KB number>       # e.g. wum uninstall KB5034441
```

### `wum hide`

Suppress updates you don't want to see.

```
wum hide add <update-id>        # Hide an update
wum hide remove <update-id>     # Unhide a hidden update
wum hide list                   # List all hidden updates
```

### `wum pause`

Pause all Windows Updates for a number of days (1–35).

```
wum pause                       # Pause for 7 days (default)
wum pause --days 14             # Pause for 14 days
wum pause resume                # Resume updates early
```

### `wum schedule`

Set up a recurring weekly update schedule persisted to the registry.

```
wum schedule                    # Show current schedule
wum schedule show               # Same as above
wum schedule set                # Set schedule (defaults: Sunday 02:00)
wum schedule set --day Friday --time 03:00 --auto-install --auto-reboot --all
wum schedule clear              # Remove the schedule
```

### `wum settings`

View and modify Windows Update registry settings.

```
wum settings                              # Display all settings
wum settings show                         # Same as above
wum settings set auto-download true       # Enable auto-download
wum settings set active-hours 9-18        # Set active hours
wum settings set defer-feature 30         # Defer feature updates 30 days
wum settings reset                        # Reset all to defaults
```

**Available setting keys:**

| Key | Values | Description |
|---|---|---|
| `auto-download` | `true` / `false` | Automatically download updates |
| `auto-install` | `true` / `false` | Automatically install updates |
| `install-drivers` | `true` / `false` | Include driver updates |
| `install-optional` | `true` / `false` | Include optional updates |
| `notify-new` | `true` / `false` | Notify on new updates |
| `notify-complete` | `true` / `false` | Notify on install complete |
| `pause-metered` | `true` / `false` | Pause on metered connections |
| `defer-feature` | `0`–`365` | Defer feature updates (days) |
| `defer-quality` | `0`–`30` | Defer quality updates (days) |
| `active-hours` | `start-end` | Active hours range (e.g. `8-22`) |

### `wum reboot`

Manage post-update system restarts.

```
wum reboot                      # Prompt to reboot (30s delay)
wum reboot --delay 60           # 60 second delay
wum reboot --force              # Reboot without confirmation
wum reboot --cancel             # Cancel a scheduled reboot
```

### `wum diagnose`

Run a comprehensive diagnostics check (takes 30–60s).

```
wum diagnose
```

Checks performed:
1. Windows Update service status
2. Network connectivity to `windowsupdate.microsoft.com`
3. WUA COM session creation
4. COM searcher initialization
5. Online update search
6. System proxy configuration
7. Last WU check timestamp
8. Windows Update AU registry policy
9. Current admin elevation status
10. OS version and build

---

## 🏗️ Architecture

```
wum.slnx
├── src/
│   ├── WUM.CLI          # Console application — commands, rendering, entry point
│   └── WUM.Core         # Business logic — services, models, system helpers
└── tests/
    └── WUM.CLI.Tests    # Unit tests
```

### WUM.Core (Class Library)

The platform layer. No UI dependencies — all operations return domain models.

| Layer | Files | Purpose |
|---|---|---|
| **Models** | `WindowsUpdate`, `UpdateHistory`, `UpdateSettings`, `UpdateSchedule`, `PauseInfo` | Domain types with enums for `UpdateStatus`, `UpdateCategory` |
| **Services** | `UpdateService`, `PauseService`, `SchedulerService`, `SettingsService`, `HistoryService` | Interfaces + implementations for all update operations |
| **Helpers** | `PowerShellHelper`, `RegistryHelper`, `AdminHelper` | System-level utilities for PowerShell execution, registry I/O, and admin checks |

**How it works:** `UpdateService` is the core engine. It invokes PowerShell scripts that use the WUA COM API (`Microsoft.Update.Session`) to search, download, install, and manage updates. Results are returned as JSON from PowerShell and parsed into C# models via `System.Text.Json`. Registry operations (pause, schedule, settings) use `RegistryHelper` for direct `HKLM` access.

### WUM.CLI (Console App)

The presentation layer. Uses `System.CommandLine` (beta4) for argument parsing and DI via `Microsoft.Extensions.DependencyInjection`.

| Layer | Files | Purpose |
|---|---|---|
| **Commands** | `StatusCommand`, `ListCommand`, `InstallCommand`, `UninstallCommand`, `SearchCommand`, `HideCommand`, `HistoryCommand`, `PauseCommand`, `ScheduleCommand`, `SettingsCommand`, `RebootCommand`, `DiagnoseCommand` | 12 CLI commands, each self-contained with `Build()` returning a `System.CommandLine.Command` |
| **Helpers** | `ConsoleRenderer`, `TableRenderer`, `ProgressRenderer`, `AdminHelper` | Rich console output — box-drawn headers, spinners, progress bars, colored status lines, confirmation prompts |

### Dependency Injection

All services are registered as singletons in `Program.cs` and resolved by each command via `IServiceProvider`:

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

### Logging

Structured logging via **Serilog** with daily rolling file output:

```
%ProgramData%\WUM\logs\wum-YYYY-MM-DD.log
```

Retains 7 days of logs automatically.

---

## 📋 Prerequisites

- **OS:** Windows 10 or Windows 11
- **SDK:** [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (for building)
- **Runtime:** None required when published as self-contained
- **Privileges:** Administrator (Release builds auto-elevate via app manifest)

---

## 🧪 Testing

```bash
dotnet test
```

Test project: `tests/WUM.CLI.Tests`

---

## 📁 Project Files

```
wum/
├── wum.slnx                               # Solution file
├── cmd.bat                                 # Scaffolding script (initial structure creation)
├── src/
│   ├── wum.sln                             # Legacy solution file
│   ├── WUM.CLI/
│   │   ├── WUM.CLI.csproj                  # Console app project (net10.0-windows, win-x64)
│   │   ├── Program.cs                      # Entry point, DI setup, logger config
│   │   ├── app.manifest                    # UAC elevation manifest (Release only)
│   │   ├── Commands/
│   │   │   ├── StatusCommand.cs            # wum status
│   │   │   ├── ListCommand.cs              # wum list
│   │   │   ├── SearchCommand.cs            # wum search
│   │   │   ├── InstallCommand.cs           # wum install
│   │   │   ├── UninstallCommand.cs         # wum uninstall
│   │   │   ├── HideCommand.cs              # wum hide
│   │   │   ├── HistoryCommand.cs           # wum history
│   │   │   ├── PauseCommand.cs             # wum pause
│   │   │   ├── ScheduleCommand.cs          # wum schedule
│   │   │   ├── SettingsCommand.cs          # wum settings
│   │   │   ├── RebootCommand.cs            # wum reboot
│   │   │   └── DiagnoseCommand.cs          # wum diagnose
│   │   └── Helpers/
│   │       ├── ConsoleRenderer.cs          # Headers, spinners, colors, prompts
│   │       ├── TableRenderer.cs            # Tabular update display
│   │       ├── ProgressRenderer.cs         # Download/install progress bars
│   │       └── AdminHelper.cs              # CLI-level admin check + re-launch
│   └── WUM.Core/
│       ├── WUM.Core.csproj                 # Class library project (net10.0-windows)
│       ├── Models/
│       │   ├── WindowsUpdate.cs            # Update model + enums
│       │   ├── UpdateHistory.cs            # History entry model
│       │   ├── UpdateSettings.cs           # Settings model
│       │   ├── UpdateSchedule.cs           # Schedule model
│       │   └── PauseInfo.cs                # Pause state model
│       ├── Services/
│       │   ├── IUpdateService.cs           # Core update operations interface
│       │   ├── UpdateService.cs            # WUA COM via PowerShell implementation
│       │   ├── IPauseService.cs            # Pause interface
│       │   ├── PauseService.cs             # Registry-based pause implementation
│       │   ├── ISchedulerService.cs        # Scheduler interface
│       │   ├── SchedulerService.cs         # Registry-persisted schedule
│       │   ├── ISettingsService.cs         # Settings interface
│       │   ├── SettingsService.cs          # Registry-backed settings
│       │   ├── IHistoryService.cs          # History interface
│       │   └── HistoryService.cs           # WUA history query
│       └── Helpers/
│           ├── PowerShellHelper.cs         # PowerShell script/command execution
│           ├── RegistryHelper.cs           # HKLM registry CRUD operations
│           └── AdminHelper.cs              # Admin role check
└── tests/
    └── WUM.CLI.Tests/
        └── WUM.CLI.Tests.csproj            # Test project
```

---

## 📜 License

This project is licensed under the [MIT License](LICENSE).
]]>
