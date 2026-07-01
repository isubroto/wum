---
title: "/diagnose"
layout: default
parent: Interactive Mode
grand_parent: Use WUM
nav_order: 13
description: "Interactive /diagnose command: health check, JSON, refresh, and optional reset fix."
---

# `/diagnose`

Runs Windows Update health checks from interactive mode.

## Synopsis

```text
/diagnose [--refresh] [--json]
/diagnose --fix [--force|-f]
```

CLI equivalent:

```powershell
wum diagnose [--refresh] [--json]
wum diagnose --fix [--force|-f]
```

## Options

| Option | Detail |
|---|---|
| `--refresh` | Force fresh scan during diagnosis. |
| `--json` | Print structured diagnostic report. |
| `--fix` | Reset Windows Update components. Requires Administrator. |
| `--force`, `-f` | Skip `--fix` confirmation prompt. |

## Behavior

`/diagnose` health check is read-only unless `--fix` is used.

`/diagnose --fix` is destructive: stops WU services, renames WU stores, resets network/proxy pieces, restarts services, then invalidates cache.

## Examples

```text
/diagnose
/diagnose --refresh
/diagnose --json
/diagnose --fix
/diagnose --fix --force
```

More backend detail: [`wum diagnose`](command-diagnose.md)

Next: [`/update` / `/upgrade`](interactive-command-update.md)
