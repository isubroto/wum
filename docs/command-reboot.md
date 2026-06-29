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

## Interactive form

```text
/reboot [--delay <seconds>] [--force|-f] [--cancel]
```

## Options

| Option | Default | Detail |
|---|---|---|
| `--delay <seconds>` | `30` | Seconds before restart. |
| `--force`, `-f` | off | Skip confirmation and allow reboot even when no reboot is currently required. |
| `--cancel` | off | Abort scheduled restart with `shutdown /a`. |

## Behavior

`reboot` requires Administrator. Without `--force`, WUM checks whether a reboot is required and exits if not. Scheduling uses `shutdown /r /t <delay> /c "<message>"`.

## Examples

```powershell
wum reboot
wum reboot --delay 60
wum reboot --force
wum reboot --cancel
```

Next: [`diagnose`](command-diagnose.md)
