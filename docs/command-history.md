---
title: history
layout: default
parent: CLI Mode
grand_parent: Use WUM
nav_order: 8
description: "History command details: count, failed-only filter, KB filter, JSON output."
---

# `wum history`

Shows Windows Update install history from the WUA history store.

## Synopsis

```powershell
wum history [--count|-n <N>] [--failed] [--kb <KB>] [--json]
```

## Interactive form

```text
/history [--count|-n <N>] [--failed] [--kb <KB>] [--json]
```

## Options

| Option | Default | Detail |
|---|---|---|
| `--count`, `-n <N>` | `20` | Number of records to fetch. |
| `--failed` | off | Show failed entries only. |
| `--kb <KB>` | none | Filter to history entries whose KB contains this value. |
| `--json` | off | Emit history rows as JSON. |

## Output fields

Each record includes title, KB, installed date, success flag, result code, error text, and operation type.

## Examples

```powershell
wum history
wum history -n 50
wum history --failed
wum history --kb KB5034441
wum history --json -n 100
```

Next: [`pause`](command-pause.md)
