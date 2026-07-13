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

## Options

| Option | Detail |
|---|---|
| `--delay <seconds>` | Seconds before restart. |
| `--force`, `-f` | Restart even when no restart is required and skip confirmation. |
| `--cancel` | Cancel pending scheduled restart. |

## Behavior

`/reboot` requires Administrator.

For a pending update restart, `/reboot` finalizes staged WUA updates once for the current boot and then schedules the restart. `/reboot --cancel` only aborts the countdown; retry protection prevents a second commit in the same boot.

## Examples

```text
/reboot
/reboot --delay 60
/reboot --force
/reboot --cancel
```

Implementation detail: [reboot behavior](command-reboot.md)

Next: [`/diagnose`](interactive-command-diagnose.md)
