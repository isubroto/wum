---
title: pause
layout: default
parent: CLI Mode
grand_parent: Use WUM
nav_order: 9
description: "Pause command details: pause days, clamp behavior, resume, registry writes."
---

# `wum pause`

Pauses Windows Updates for a fixed number of days, or resumes updates early.

## Synopsis

```powershell
wum pause [--days <N>]
wum pause resume
```

## Interactive form

```text
/pause [--days <N>]
/pause resume
```

## Options

| Option | Default | Detail |
|---|---|---|
| `--days <N>` | `7` | Days to pause. Values are clamped to 1-35. |

## Subcommands

| Subcommand | Detail |
|---|---|
| `resume` | Deletes WU pause registry values and resumes updates immediately. |

## Behavior

`pause` requires Administrator. It writes standard Windows Update pause registry values. `resume` removes those values.

See [Configuration -> Pause](configuration.md#pause) for exact registry paths.

## Examples

```powershell
wum pause
wum pause --days 14
wum pause --days 99
wum pause resume
```

Next: [`schedule`](command-schedule.md)
