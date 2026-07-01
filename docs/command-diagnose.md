---
title: diagnose
layout: default
parent: CLI Mode
grand_parent: Use WUM
nav_order: 13
description: "Diagnose command details: health checks, refresh, JSON, exit bitmask, reset fix."
---

# `wum diagnose`

Runs Windows Update health checks and can optionally reset Windows Update components.

## Synopsis

```powershell
wum diagnose [--refresh] [--json]
wum diagnose --fix [--force|-f]
```

## Interactive form

```text
/diagnose [--refresh] [--json]
/diagnose --fix [--force|-f]
```

## Options

| Option | Detail |
|---|---|
| `--refresh` | Force a fresh available-update scan so diagnose, status, and list agree. |
| `--json` | Output structured diagnostics and return scriptable exit bitmask. |
| `--fix` | Reset Windows Update components. Requires Administrator. |
| `--force`, `-f` | Skip `--fix` confirmation prompt. |

## Behavior

Normal diagnose mode is read-only. `--fix` is destructive: pending downloads are discarded and reboot is advised.

## `--fix` reset steps

`--fix` stops Windows Update services, renames `SoftwareDistribution` and `catroot2` to `.bak`, reregisters WU DLLs, resets WinSock/WinHTTP proxy, restarts services, invalidates scan cache, and triggers re-detection.

## Exit bitmask

With `--json`, the exit code is a bitmask for automation. See [Diagnostics](diagnostics.md) for the full check list, bit values, health score, and HRESULT decoding.

## Examples

```powershell
wum diagnose
wum diagnose --refresh
wum diagnose --json
wum diagnose --fix
wum diagnose --fix --force
```

Next: [`update` / `upgrade`](command-update.md)
