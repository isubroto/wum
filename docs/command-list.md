---
title: list
layout: default
parent: CLI Mode
grand_parent: Use WUM
nav_order: 3
description: "List command details: filters, installed updates, hidden updates, Microsoft Update, JSON and verbose output."
---

# `wum list`

Lists available updates by default. It can also show installed updates, include hidden updates, query Microsoft Update, and emit JSON.

## Synopsis

```powershell
wum list [--security] [--critical] [--optional] [--drivers] [--definition]
         [--hidden] [--installed] [--microsoft-update|--mu]
         [--json] [--no-color] [--verbose|-v] [--refresh]
```

## Interactive form

```text
/list [--security] [--critical] [--optional] [--drivers] [--definition]
      [--hidden] [--installed] [--microsoft-update|--mu]
      [--json] [--no-color] [--verbose|-v] [--refresh]
```

## Filter options

| Option | Detail |
|---|---|
| `--security` | Security updates only. |
| `--critical` | Critical updates only. |
| `--optional` | Optional updates only. |
| `--drivers` | Driver updates only. |
| `--definition` | Definition / Defender updates only. |

Category filters are OR-combined. `wum list --security --drivers` shows security updates and drivers.

## Scan options

| Option | Detail |
|---|---|
| `--hidden` | Include hidden updates in the scan result. |
| `--installed` | Show installed updates instead of available updates. |
| `--microsoft-update`, `--mu` | Include Microsoft Update service for drivers and other Microsoft products. |
| `--refresh` | Force a fresh scan and bypass the shared 5-minute cache. |

## Output options

| Option | Detail |
|---|---|
| `--json` | Emits the filtered list as JSON. |
| `--no-color` | Disables ANSI colors for piping/log capture. |
| `--verbose`, `-v` | Shows full per-update details: KB, category, severity, size, status, reboot requirement, description, support URL. |

## Behavior

`list` is read-only and does not require Administrator. Filtering happens after scan data is collected. The footer reports total download size and reboot count.

## Examples

```powershell
wum list
wum list --installed
wum list --security
wum list --drivers --mu
wum list --hidden
wum list -v
wum list --json
wum list --no-color
wum list --refresh
```

Next: [`search`](command-search.md)
