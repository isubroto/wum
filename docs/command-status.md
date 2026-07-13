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

## Options

| Option | Default | Detail |
|---|---|---|
| `--json` | off | Emits machine-readable JSON instead of the colorized dashboard. Use for scripts and monitoring. |
| `--verbose`, `-v` | off | Prints each status step: service check, reboot check, pause read, live update scan, and per-update detail. |
| `--refresh` | off | Requests an explicit user-initiated Windows Update refresh before querying current status. |

## Behavior

`status` is read-only and does not require Administrator.

The command combines local system checks with the Settings-backed Windows Update orchestrator and WUA catalog data when available. Every invocation reads current operating-system state; `--refresh` additionally asks Windows Update to initiate a user scan first.

## Verdicts

| Condition | Verdict |
|---|---|
| Restart attention or WUA reboot flag | `Restart required` |
| Failed action/result | `Update failure needs attention` |
| Install/deploy/commit/reboot/post-reboot action | `Updates installing` |
| Download action | `Updates downloading` |
| Successful download awaiting install | `Updates pending installation` |
| No available updates | `System is up to date` |
| Security or critical updates exist | `Security updates available` |
| Other updates exist | `Updates available` |

## JSON shape

```json
{
  "reboot_required": false,
  "updates_available": 3,
  "available_updates": 1,
  "security_updates": 1,
  "feature_updates": 1,
  "downloading_updates": 0,
  "pending_installation": 1,
  "installing_updates": 0,
  "restart_required_updates": 0,
  "failed_updates": 0,
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
