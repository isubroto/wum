---
title: reboot
layout: default
parent: CLI Mode
grand_parent: Use WUM
nav_order: 12
description: "Reboot command details: delay, force, cancel, shutdown behavior."
---

# `wum reboot`

Schedules or cancels a Windows restart after updates.

## Synopsis

```powershell
wum reboot [--delay <seconds>] [--force|-f] [--cancel]
```

## Options

| Option | Default | Detail |
|---|---|---|
| `--delay <seconds>` | `30` | Seconds before restart. |
| `--force`, `-f` | off | Skip confirmation and allow reboot even when no reboot is currently required. |
| `--cancel` | off | Abort scheduled restart with `shutdown /a`. |

## Behavior

When Windows Update reports a pending restart, `reboot` requires Administrator. WUM calls WUA `Commit(0)` immediately before `shutdown /r /t <delay>` so staged feature updates finalize. A machine-wide mutex serializes WUM processes. A `Started` marker is persisted before Commit and a `Committed` marker after success; cancelling, retrying, or recovering from an interrupted process never risks a second unsupported commit in the same boot. Commit failure aborts restart scheduling.

Without `--force`, WUM exits when no update restart is required. `--force` can still schedule a normal restart. `--cancel` only runs `shutdown /a` and never commits an update.

## Examples

```powershell
wum reboot
wum reboot --delay 60
wum reboot --force
wum reboot --cancel
```

Next: [`diagnose`](command-diagnose.md)
