---
title: Home
layout: default
nav_order: 1
description: "WUM — a modern .NET 10 CLI for managing Windows Updates from the terminal."
permalink: /
---

<section class="hero hero--home">
  <p class="hero-eyebrow">Windows Update Manager CLI</p>
  <h1 class="hero-title hero-title--logo">
    <img class="hero-logo" src="{{ '/assets/wum-logo.svg' | relative_url }}" alt="WUM - Windows Update Manager">
  </h1>
  <p class="hero-tagline">
    Manage the entire Windows Update lifecycle from the terminal — list, search, install, uninstall, hide, pause, schedule, configure, reboot, and diagnose — without ever opening the Settings app.
  </p>
  <div class="hero-actions">
    <a href="{{ '/getting-started/' | relative_url }}" class="btn btn-primary fs-5 mb-4 mb-md-0 mr-2">Get started</a>
    <a href="{{ '/commands.html' | relative_url }}" class="btn fs-5 mb-4 mb-md-0">Command reference</a>
    <a href="https://github.com/isubroto/wum" class="btn fs-5 mb-4 mb-md-0">View on GitHub</a>
  </div>
</section>

---

> **Version:** {{ site.data.project.version }} · **Target:** `{{ site.data.project.target_framework }}` ({{ site.data.project.runtime_identifier }}) · **License:** {{ site.data.project.license }}
{: .note }

WUM is built on **.NET 10** and drives the **Windows Update Agent (WUA) COM API** through PowerShell, giving administrators and power users full, scriptable control over Windows Updates.

---

## Documentation map

| Document | What it covers |
|---|---|
| [Installation](installation.md) | Build from source, publish a single-file `wum.exe`, MSI / WinGet install, requirements |
| [Interactive Mode](interactive.md) | Smart shell, slash commands, completion, history, keyboard shortcuts, and session tools |
| [Command Reference](commands.md) | Every command, sub-command, argument, option, and worked examples |
| [Architecture](architecture.md) | Project layout, services, models, helpers, DI, the scan cache, logging |
| [Configuration](configuration.md) | Settings keys, schedule, pause, and the exact registry paths each one writes |
| [Diagnostics](diagnostics.md) | The `diagnose` command, exit-code bitmask, health score, WU error-code decoding |
| [Troubleshooting](troubleshooting.md) | Symptom → cause → fix for the most common Windows Update problems |
| [Development](development.md) | Building, testing, the publish pipeline, and how to add a new command |

---

## What WUM can do

| Area | Capabilities |
|---|---|
| **Interactive shell** | Run `wum` with no arguments for slash commands, smart completion, history, help, clear, and keyboard shortcuts |
| **Discovery** | List available, installed, and hidden updates with category filtering and full-text search |
| **Installation** | Download & install by KB number, category, or all at once — with an install plan, progress bars, and a summary |
| **Uninstall** | Remove an installed update by KB number (`wusa.exe`) |
| **Pause / Resume** | Pause all updates for 1–35 days via the registry; resume early |
| **Scheduling** | Persist a weekly update schedule (day, time, auto-install, auto-reboot, install-all) |
| **Settings** | View and change WU settings — active hours, deferrals, auto-download, metered network |
| **Hide / Unhide** | Suppress unwanted updates from the available list |
| **Reboot** | Schedule or cancel a post-update restart with a configurable delay |
| **Diagnostics** | Multi-point health check with an exit-code bitmask for scripting, plus an optional component reset (`--fix`) |
| **Output** | Colorized tables, verbose detail, `--json` for scripting, `--no-color` for piping |

---

## 30-second tour

```console
# Interactive shell
wum                        # Open smart prompt with slash commands and completion

# Read-only — no admin needed
wum status                 # Dashboard: service state, reboot, pause, update counts
wum list --security        # Show security updates only
wum search defender        # Find updates matching a keyword
wum diagnose               # Health check (why aren't updates showing?)

# Modifying — require an elevated (Administrator) terminal
wum install --security     # Install security updates
wum install KB5034441      # Install a specific KB
wum pause --days 14        # Pause updates for 14 days
wum reboot --delay 60      # Restart in 60 seconds
```

---

## Privilege model

WUM launches **as-invoker** — no UAC prompt on start. Commands split into two groups:

{: .note }
> **Read-only (standard user):** `status`, `list`, `search`, `history`, `hide list`, `--info`, `--version`, `--help`

{: .warning }
> **Modifying (Administrator):** `install`, `uninstall`, `pause`, `schedule`, `settings set/reset`, `reboot`, `diagnose --fix`. These call `AdminHelper.RequireAdmin()` and stop with guidance if not elevated.

See the [Command Reference](commands.md) for the exact requirement per command.

---

## How it works, in one diagram

```
Command (CLI)  ──resolves──▶  IUpdateService (DI singleton)
                                   │
                                   ▼
                 1. scan cache (5-min TTL, unless --refresh)
                 2. generate PowerShell script (WUA criteria)
                 3. powershell.exe ─▶ Microsoft.Update.Session (WUA COM)
                 4. parse ConvertTo-Json output ─▶ List<WindowsUpdate>
                 5. write scan cache
```

Registry-backed operations (pause, schedule, settings) bypass PowerShell and use direct `HKLM` access. See [Architecture](architecture.md) for the full picture.
