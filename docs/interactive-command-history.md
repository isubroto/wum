---
title: "/history"
layout: default
parent: Interactive Mode
grand_parent: Use WUM
nav_order: 8
description: "Interactive /history command: update install history, count, failed-only, KB filter, JSON."
---

# `/history`

Shows Windows Update install history.

## Synopsis

```text
/history [--count|-n <N>] [--failed] [--kb <KB>] [--json]
```

CLI equivalent:

```powershell
wum history [--count|-n <N>] [--failed] [--kb <KB>] [--json]
```

## Options

| Option | Detail |
|---|---|
| `--count`, `-n <N>` | Limit number of rows. |
| `--failed` | Show failed history entries only. |
| `--kb <KB>` | Filter by KB number. |
| `--json` | Print JSON history. |

## Behavior

`/history` is read-only. No Administrator terminal needed.

This is different from interactive command-line history. It reads Windows Update install history.

## Examples

```text
/history
/history -n 50
/history --failed
/history --kb KB5034441
/history --json -n 100
```

More backend detail: [`wum history`](command-history.md)

Next: [`/pause`](interactive-command-pause.md)
