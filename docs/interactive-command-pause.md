---
title: "/pause"
layout: default
parent: Interactive Mode
grand_parent: Use WUM
nav_order: 9
description: "Interactive /pause command: pause Windows Update or resume early."
---

# `/pause`

Pauses Windows Updates for fixed days, or resumes updates early.

## Synopsis

```text
/pause [--days <N>]
/pause resume
```

CLI equivalent:

```powershell
wum pause [--days <N>]
wum pause resume
```

## Options and subcommands

| Form | Detail |
|---|---|
| `/pause` | Pause for default 7 days. |
| `/pause --days <N>` | Pause for N days. Values clamp to 1-35. |
| `/pause resume` | Resume updates early. |

## Behavior

`/pause` requires Administrator.

It writes standard Windows Update pause registry values. `resume` deletes those values.

## Examples

```text
/pause
/pause --days 14
/pause --days 99
/pause resume
```

More backend detail: [`wum pause`](command-pause.md)

Next: [`/schedule`](interactive-command-schedule.md)
