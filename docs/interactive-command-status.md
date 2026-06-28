---
title: "/status"
layout: default
parent: Interactive Mode
grand_parent: Use WUM
nav_order: 2
description: "Interactive /status command: status dashboard, JSON, verbose detail, and refresh."
---

# `/status`

Shows current Windows Update state inside interactive mode.

## Synopsis

```text
/status [--json] [--verbose|-v] [--refresh]
```

CLI equivalent:

```powershell
wum status [--json] [--verbose|-v] [--refresh]
```

## Options

| Option | Detail |
|---|---|
| `--json` | Print machine-readable status JSON instead of the dashboard. |
| `--verbose`, `-v` | Show service check, reboot check, pause check, scan/cache detail, and per-update detail. |
| `--refresh` | Force fresh online scan and bypass the 5-minute scan cache. |

## Behavior

`/status` is read-only. No Administrator terminal needed.

It prints service state, reboot requirement, pause state, available update count, and category counts. After output, interactive mode returns to the `ready` prompt without pause.

## Examples

```text
/status
/status --json
/status -v
/status --refresh
```

More backend detail: [`wum status`](command-status.md)

Next: [`/list`](interactive-command-list.md)
