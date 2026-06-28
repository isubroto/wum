---
title: "/reboot"
layout: default
parent: Interactive Mode
grand_parent: Use WUM
nav_order: 12
description: "Interactive /reboot command: schedule, force, or cancel restart."
---

# `/reboot`

Schedules or cancels restart from interactive mode.

## Synopsis

```text
/reboot [--delay <seconds>] [--force|-f] [--cancel]
```

CLI equivalent:

```powershell
wum reboot [--delay <seconds>] [--force|-f] [--cancel]
```

## Options

| Option | Detail |
|---|---|
| `--delay <seconds>` | Seconds before restart. |
| `--force`, `-f` | Force applications to close. |
| `--cancel` | Cancel pending scheduled restart. |

## Behavior

`/reboot` requires Administrator.

It uses the same restart scheduling and cancellation behavior as CLI mode.

## Examples

```text
/reboot
/reboot --delay 60
/reboot --force
/reboot --cancel
```

More backend detail: [`wum reboot`](command-reboot.md)

Next: [`/diagnose`](interactive-command-diagnose.md)
