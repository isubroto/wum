---
title: "/list"
layout: default
parent: Interactive Mode
grand_parent: Use WUM
nav_order: 3
description: "Interactive /list command: update filters, installed/hidden scans, JSON, verbose output."
---

# `/list`

Lists available updates inside interactive mode. Can also show installed updates, hidden updates, Microsoft Update results, and machine-readable JSON.

## Synopsis

```text
/list [--security] [--critical] [--optional] [--drivers] [--definition]
      [--hidden] [--installed] [--microsoft-update|--mu]
      [--json] [--no-color] [--verbose|-v] [--refresh]
```

CLI equivalent:

```powershell
wum list [filters] [options]
```

## Filter options

| Option | Detail |
|---|---|
| `--security` | Show security updates only. |
| `--critical` | Show critical updates only. |
| `--optional` | Show optional updates only. |
| `--drivers` | Show driver updates only. |
| `--definition` | Show definition / Defender updates only. |

Filters combine as OR. `/list --security --drivers` shows security updates and drivers.

## Scan and output options

| Option | Detail |
|---|---|
| `--hidden` | Include hidden updates. |
| `--installed` | Show installed updates instead of available updates. |
| `--microsoft-update`, `--mu` | Include Microsoft Update service. Useful for drivers and other Microsoft products. |
| `--refresh` | Force fresh scan and bypass cache. |
| `--json` | Print JSON list. |
| `--no-color` | Disable ANSI color. |
| `--verbose`, `-v` | Show full update detail. |

## Behavior

`/list` is read-only. No Administrator terminal needed.

Interactive completion suggests known filters and aliases after `/list `.

## Examples

```text
/list
/list --installed
/list --security
/list --drivers --mu
/list -v
/list --json
/list --refresh
```

More backend detail: [`wum list`](command-list.md)

Next: [`/search`](interactive-command-search.md)
