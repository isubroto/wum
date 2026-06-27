---
title: Installation
layout: default
parent: Get Started
nav_order: 1
description: "Build, publish, or install WUM, and what it writes on disk."
---

# Installation
{: .no_toc }

1. TOC
{:toc}

---

[← Docs index](index.md)

## Requirements

| Requirement | Detail |
|---|---|
| **OS** | Windows 10 or Windows 11 (x64) |
| **SDK** | [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) — only needed to **build** from source |
| **Runtime** | None when published self-contained; the `wum.exe` bundles the runtime |
| **PowerShell** | `powershell.exe` (Windows PowerShell 5.1, ships with Windows) — WUM shells out to it for all WUA COM calls |
| **Privileges** | Administrator for update operations only. Read-only commands run as a standard user |

The app targets `net10.0-windows` with `RuntimeIdentifier=win-x64`. PowerShell is invoked as:

```
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "<script>"
```

so WUM works regardless of the machine's default execution policy.

---

## Option 1 — Build from source

```bash
git clone https://github.com/isubroto/wum.git
cd wum
dotnet build
```

Run directly from the repo (read-only commands need no admin):

```bash
dotnet run --project src/WUM.CLI -- status
dotnet run --project src/WUM.CLI -- list --security
```

Everything after `--` is passed to `wum` as arguments.

---

## Option 2 — Publish a single-file `wum.exe`

```bash
dotnet publish src/WUM.CLI -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

Produces a standalone `./publish/wum.exe` that needs **no .NET runtime** on the target machine. The Release build applies `app.manifest` (`asInvoker`) so launching never triggers a UAC prompt; modifying commands request elevation themselves.

> The `PublishSingleFile` and `SelfContained` properties are already set in `WUM.CLI.csproj`, so the `-p:`/`--self-contained` flags above are belt-and-suspenders.

Add the publish folder to your `PATH`, or copy `wum.exe` somewhere already on `PATH`, to call `wum` from any terminal.

---

## Option 3 — MSI / WinGet

```bash
winget install SubrotoSaha.WUM
```

The MSI installs `wum.exe` to:

```
C:\Program Files\Subroto Saha\WUM
```

and **adds that folder to the system `PATH`**, so `wum` is available everywhere. Tagged builds publish both the portable `wum.exe` and the MSI automatically (see `.github/workflows/release.yml` and `wingetRelease.yml`).

---

## Verify the install

```bash
wum --version      # e.g. 0.2.0.77
wum --info         # Version, commit, build date, author, license, runtime, OS
wum --help         # Top-level help and the command list
```

`--info` prints application metadata (version/commit/build date/author/license/repo), runtime details (framework, CLR, process arch), and system info (OS, machine, user, CPU cores). It is handled in `Program.Main` before the command pipeline runs, so it is fast and needs no admin.

---

## Where WUM writes on disk

| Path | Purpose |
|---|---|
| `%ProgramData%\WUM\logs\wum-YYYY-MM-DD.log` | Serilog daily rolling logs (7 days retained) |
| `%ProgramData%\WUM\cache\available-h{0/1}-m{0/1}.json` | Available-updates scan cache (5-minute TTL) |

`%ProgramData%` is normally `C:\ProgramData`. The cache is keyed by search variant (`h` = include-hidden, `m` = Microsoft Update) so different scans never collide. See [Architecture → Scan cache](architecture.md#scan-cache).

Next: [Command Reference →](commands.md)

