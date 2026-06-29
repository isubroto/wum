---
title: Development
layout: default
parent: Internals
nav_order: 2
description: "Build, test, publish, versioning, and how to add a new command."
---

# Development
{: .no_toc }

1. TOC
{:toc}

---

[← Docs index](index.md)

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows 10/11 (the projects target `net10.0-windows`)
- An Administrator terminal to exercise modifying commands end-to-end

## Solution layout

```
wum.slnx                          # Solution (newer slnx format)
src/
  wum.sln                         # Legacy solution file
  WUM.CLI/
    WUM.CLI.csproj                # Exe, net10.0-windows, win-x64, AssemblyName=wum
    Program.cs                    # Entry point, DI, Serilog, --info short-circuit
    app.manifest                  # asInvoker (Release only)
    Commands/                     # 12 command classes (Build() → Command)
    Helpers/                      # ConsoleRenderer, TableRenderer, ProgressRenderer, AdminHelper
  WUM.Core/
    WUM.Core.csproj               # Class library, net10.0-windows
    Models/                       # WindowsUpdate, UpdateHistory, UpdateSettings, UpdateSchedule, PauseInfo
    Services/                     # I*Service + implementations
    Helpers/                      # PowerShellHelper, RegistryHelper, AdminHelper, WuErrorCodes
tests/
  WUM.CLI.Tests/                  # xUnit + Moq + FluentAssertions
Installer/                        # WiX MSI (Product.wxs, Files.wxs)
.github/workflows/                # codeql, release, wingetRelease
```

## Key dependencies

| Package | Version | Used for |
|---|---|---|
| `System.CommandLine` | `2.0.0-beta4.22272.1` | Argument parsing |
| `Microsoft.Extensions.DependencyInjection` | `10.0.0` | Service wiring |
| `Serilog` + `Sinks.File` + `Sinks.Console` | `4.2.0` / `6.0.0` | Logging |

---

## Build & run

```bash
dotnet build                                          # Build all projects
dotnet run --project src/WUM.CLI -- status            # Run a command
dotnet run --project src/WUM.CLI -- list --security   # Args go after --
```

## Test

```bash
dotnet test                                           # Run the suite
dotnet test --filter FullyQualifiedName~ListCommand   # One area
```

Tests use **Moq** to mock `IUpdateService` and **FluentAssertions** for assertions. They cover command-level behavior (e.g. `list`/`status` filtering) and `ConsoleRenderer`.

> **Heads-up on mock signatures:** `GetAvailableUpdatesAsync` has the shape
> `(bool includeHidden, bool useMicrosoftUpdate, bool forceRefresh, CancellationToken ct)`.
> Mocks must match all four positionally:
> ```csharp
> _mock.Setup(s => s.GetAvailableUpdatesAsync(
>     It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
>     .ReturnsAsync(SampleUpdates());
> ```
> If you add a parameter to the interface, update the mocks or the test project fails to compile.

## Publish

```bash
dotnet publish src/WUM.CLI -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

`PublishSingleFile`/`SelfContained` are already in the csproj. The Release config applies `app.manifest` (`asInvoker`). Output: a standalone `./publish/wum.exe`.

## Versioning

`WUM.CLI.csproj` pins a 4-part numeric `<Version>` (currently `{{ site.data.project.version }}`) and mirrors it into `AssemblyVersion`/`FileVersion`/`InformationalVersion`/`PackageVersion`. CI overwrites `<Version>` on tagged releases and refreshes `docs/_data/project.yml` from the project file. `IncludeSourceRevisionInInformationalVersion=false` keeps the SDK from appending a git sha, so `wum --version` stays clean.

---

## Adding a new command

1. **Create** `src/WUM.CLI/Commands/FooCommand.cs`:

```csharp
public class FooCommand
{
    private readonly IUpdateService _updates;
    public FooCommand(IServiceProvider sp) =>
        _updates = sp.GetRequiredService<IUpdateService>();

    public Command Build()
    {
        var cmd    = new Command("foo", "Do the foo thing");
        var barOpt = new Option<bool>("--bar", "Enable bar");
        cmd.AddOption(barOpt);

        // ≤ ~8 options: typed handler
        cmd.SetHandler(async (bool bar) => await RunAsync(bar), barOpt);

        // many options: context handler
        // cmd.SetHandler(async ctx => {
        //     bool bar = ctx.ParseResult.GetValueForOption(barOpt);
        //     await RunAsync(bar);
        // });
        return cmd;
    }

    private async Task RunAsync(bool bar) { /* ... */ }
}
```

2. **Register** it in `Program.cs`:

```csharp
root.AddCommand(new FooCommand(services).Build());
```

3. If it **modifies the system**, call `WUM.CLI.Helpers.AdminHelper.RequireAdmin();` first.
4. If it **reads available updates**, go through `IUpdateService.GetAvailableUpdatesAsync(...)` so it uses the shared scan cache; add a `--refresh` option that passes `forceRefresh: true`.
5. If it **changes state** (install/hide/etc.), make sure the underlying service invalidates the cache (the engine already does for its mutations).
6. Use `ConsoleRenderer` for output and `--json` for a machine-readable path.

## Conventions

- **Output:** colorized by default; honor `--json` and (where relevant) `--no-color`.
- **Long-running calls:** wrap in `ConsoleRenderer.ShowSpinnerAsync(...)` with a sensible timeout.
- **Failures:** surface `IUpdateService.LastError` and decode HRESULTs via `WuErrorCodes`.
- **Registry:** go through `RegistryHelper`; never hand-roll `HKLM` access.
- **PowerShell:** build scripts with single quotes / careful escaping (WUA search criteria doesn't support `UpdateID` — search broad, then match in PowerShell).

## CI / release

`.github/workflows/`:

- `codeql.yml` — static analysis
- `release.yml` — builds and publishes the portable `wum.exe` + MSI on tagged builds
- `wingetRelease.yml` — pushes to the WinGet community repo

---

[← Back to docs index](index.md)
