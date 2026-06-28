---
title: "/install"
layout: default
parent: Interactive Mode
grand_parent: Use WUM
nav_order: 5
description: "Interactive /install command: KB targeting, category install, dry-run, force, no-reboot."
---

# `/install`

Downloads and installs selected updates from interactive mode.

## Synopsis

```text
/install [<kb-articles>...] [--security] [--critical] [--all] [--definition]
         [--dry-run] [--force|-f] [--no-reboot] [--microsoft-update|--mu]
```

CLI equivalent:

```powershell
wum install [<kb-articles>...] [options]
```

## Arguments

| Argument | Required | Detail |
|---|---|---|
| `kb-articles` | No | One or more KBs, such as `KB5034441 KB5035853`. Bare numbers are normalized to KB form. |

## Selection options

| Option | Detail |
|---|---|
| `--security` | Install security updates only. |
| `--critical` | Install critical updates only. |
| `--all` | Install every available update. |
| `--definition` | Install definition / Defender updates. |
| `--microsoft-update`, `--mu` | Include Microsoft Update service during scan. |

## Execution options

| Option | Detail |
|---|---|
| `--dry-run` | Show install plan only. No download or install. |
| `--force`, `-f` | Skip confirmation prompt. |
| `--no-reboot` | Do not prompt for restart after install. |

## Behavior

`/install` requires Administrator.

It scans, resolves targets, shows plan, confirms unless forced, downloads/installs with progress, then returns to interactive `ready`.

## Examples

```text
/install KB5034441
/install KB5034441 KB5035853
/install --security
/install --all --dry-run
/install --all --force
/install KB5034441 --no-reboot
```

More backend detail: [`wum install`](command-install.md)

Next: [`/uninstall`](interactive-command-uninstall.md)
