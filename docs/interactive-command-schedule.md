---
title: "/schedule"
layout: default
parent: Interactive Mode
grand_parent: Use WUM
nav_order: 10
description: "Interactive /schedule command: show, set, or clear weekly update schedule."
---

# `/schedule`

Shows, sets, or clears WUM weekly update schedule.

## Synopsis

```text
/schedule
/schedule show
/schedule set [--day <DayOfWeek>] [--time <HH:mm>] [--auto-install] [--auto-reboot] [--all]
/schedule clear
```

CLI equivalent:

```powershell
wum schedule [show|set|clear] [options]
```

## Subcommands

| Subcommand | Detail |
|---|---|
| none | Same as `show`. |
| `show` | Show saved schedule. |
| `set` | Save weekly schedule. |
| `clear` | Remove saved schedule. |

## Set options

| Option | Detail |
|---|---|
| `--day <DayOfWeek>` | Schedule day, such as `Friday`. |
| `--time <HH:mm>` | 24-hour time, such as `03:00`. |
| `--auto-install` | Install automatically when schedule runs. |
| `--auto-reboot` | Reboot automatically if needed. |
| `--all` | Install all available updates, not only default targets. |

## Behavior

`/schedule set` and `/schedule clear` require Administrator. `/schedule show` is read-only.

## Examples

```text
/schedule
/schedule show
/schedule set
/schedule set --day Friday --time 03:00 --auto-install --auto-reboot --all
/schedule clear
```

More backend detail: [`wum schedule`](command-schedule.md)

Next: [`/settings`](interactive-command-settings.md)
