---
title: status
layout: default
parent: CLI Mode
grand_parent: Use WUM
nav_order: 2
description: "Status command details: system verdict, JSON output, verbose trace, refresh behavior."
---

# `wum status`

Shows current Windows Update state: WU service status, reboot requirement, pause state, available update counts, and category counts.

## Synopsis

```powershell
wum status [--json] [--verbose|-v] [--refresh]
```

## Interactive form

```text
/status [--json] [--verbose|-v] [--refresh]
```

## Options

| Option | Default | Detail |
|---|---|---|
| `--json` | off | Emits machine-readable JSON instead of the colorized dashboard. Use for scripts and monitoring. |
| `--verbose`, `-v` | off | Prints each status step: service check, reboot check, pause read, update scan/cache read, and per-update detail. |
| `--refresh` | off | Forces a fresh online scan and bypasses the 5-minute available-update cache. |

## Behavior

`status` is read-only and does not require Administrator.

The command combines local system checks with available-update scan data. Without `--refresh`, it can reuse the shared scan cache so repeated calls stay fast.

## Verdicts

| Condition | Verdict |
|---|---|
| No available updates | `System is up to date` |
| Security or critical updates exist | `Security updates available` |
| Other updates exist | `Updates available` |

## JSON shape

```json
{
  "reboot_required": false,
  "updates_available": 3,
  "security_updates": 1,
  "updates_paused": false,
  "paused_until": null,
  "wu_service": "Running",
  "checked_at": "2026-06-24T10:15:00"
}
```

## Examples

```powershell
wum status
wum status --json
wum status -v
wum status --refresh
```

Next: [`list`](command-list.md)
