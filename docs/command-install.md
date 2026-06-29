---
title: install
layout: default
parent: CLI Mode
grand_parent: Use WUM
nav_order: 5
description: "Install command details: KB targeting, categories, dry-run, force, no-reboot, Microsoft Update, target resolution."
---

# `wum install`

Downloads and installs selected updates. Shows an install plan first, then per-update progress and a final summary.

## Synopsis

```powershell
wum install [<kb-articles>...] [--security] [--critical] [--all] [--definition]
            [--dry-run] [--force|-f] [--no-reboot] [--microsoft-update|--mu]
```

## Interactive form

```text
/install [<kb-articles>...] [--security] [--critical] [--all] [--definition]
         [--dry-run] [--force|-f] [--no-reboot] [--microsoft-update|--mu]
```

## Arguments

| Argument | Required | Detail |
|---|---|---|
| `kb-articles` | No | Zero or more KB numbers, for example `KB5034441 KB5035853`. A bare number is auto-prefixed with `KB`. |

## Selection options

| Option | Detail |
|---|---|
| `--security` | Install security updates only. |
| `--critical` | Install critical updates only. |
| `--all` | Install every available update. |
| `--definition` | Install definition / Defender updates. |
| `--microsoft-update`, `--mu` | Query Microsoft Update while scanning. |

## Execution options

| Option | Detail |
|---|---|
| `--dry-run` | Show install plan and stop. No download, install, or reboot prompt. |
| `--force`, `-f` | Skip confirmation prompt. |
| `--no-reboot` | Do not prompt for restart after installation. |

## Target resolution

1. Explicit KBs win. WUM installs those matching updates and warns for KBs not found.
2. `--all` installs every available update.
3. `--security --critical` installs both categories.
4. A single category flag installs that category.
5. No flags installs security + critical updates. If none exist, WUM falls back to all available updates.

## Flow

`install` requires Administrator.

Flow: scan -> resolve targets -> show plan -> confirm unless `--force` -> download/install with progress -> summary -> optional reboot prompt.

Any successful install invalidates the scan cache so future `status`, `list`, and `diagnose` calls see fresh state.

## Examples

```powershell
wum install KB5034441
wum install KB5034441 KB5035853
wum install --security
wum install --all
wum install --all --force
wum install --all --dry-run
wum install --all --no-reboot
wum install --definition --mu
```

Next: [`uninstall`](command-uninstall.md)
