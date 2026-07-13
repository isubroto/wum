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

## Options

| Option | Detail |
|---|---|
| `--json` | Print machine-readable status JSON instead of the dashboard. |
| `--verbose`, `-v` | Show service check, reboot check, pause check, scan detail, and per-update detail. |
| `--refresh` | Request an explicit user-initiated Windows Update refresh before querying current status. |

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

Implementation detail: [status behavior](command-status.md)

Next: [`/list`](interactive-command-list.md)
