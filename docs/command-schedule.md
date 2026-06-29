---
title: schedule
layout: default
parent: CLI Mode
grand_parent: Use WUM
nav_order: 10
description: "Schedule command details: show, set, clear, day/time validation, auto-install flags."
---

# `wum schedule`

Manages WUM's persisted weekly install schedule.

## Synopsis

```powershell
wum schedule
wum schedule show
wum schedule set [--day <DayOfWeek>] [--time <HH:mm>] [--auto-install] [--auto-reboot] [--all]
wum schedule clear
```

## Interactive form

```text
/schedule
/schedule show
/schedule set [--day <DayOfWeek>] [--time <HH:mm>] [--auto-install] [--auto-reboot] [--all]
/schedule clear
```

## Subcommands

| Subcommand | Admin | Detail |
|---|---|---|
| default / `show` | No | Shows current schedule and next run time. |
| `set` | Yes | Writes schedule settings. |
| `clear` | Yes | Removes the schedule. |

## `set` options

| Option | Default | Detail |
|---|---|---|
| `--day <DayOfWeek>` | `Sunday` | Day of week, case-insensitive. |
| `--time <HH:mm>` | `02:00` | 24-hour time. |
| `--auto-install` | off | Install updates when schedule fires. |
| `--auto-reboot` | off | Reboot after scheduled install when needed. |
| `--all` | off | Install all updates. Without this, scheduled installs target security updates. |

## Validation

Invalid days and non-`HH:mm` times are rejected. The schedule view shows computed next run time.

## Examples

```powershell
wum schedule
wum schedule show
wum schedule set
wum schedule set --day Friday --time 03:00 --auto-install --auto-reboot --all
wum schedule clear
```

Next: [`settings`](command-settings.md)
