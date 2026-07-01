---
title: "/update"
layout: default
parent: Interactive Mode
grand_parent: Use WUM
nav_order: 14
description: "Interactive /update command: check latest release and upgrade WUM from the MSI."
---

# `/update` / `/upgrade`

Checks the latest GitHub release from interactive mode and can install the newest WUM MSI. `/upgrade` is an alias for `/update`.

## Synopsis

```text
/update [--check|-c] [--force|-f|--yes|-y]
/upgrade [--check|-c] [--force|-f|--yes|-y]
```

CLI equivalent:

```powershell
wum update [--check|-c] [--force|-f|--yes|-y]
wum upgrade [--check|-c] [--force|-f|--yes|-y]
```

## Options

| Option | Detail |
|---|---|
| `--check`, `-c` | Check only. Returns exit code `2` when a newer release exists. |
| `--force`, `-f` | Skip confirmation and install. |
| `--yes`, `-y` | Alias for `--force`. |

## Behavior

`/update --check` is read-only and does not require Administrator.

`/update` downloads the latest GitHub release MSI and launches `msiexec`. Installing requires an elevated terminal.

## Completion

Typing `/up` suggests both `/update` and `/upgrade`. Typing `/update ` suggests `--check`, `--force`, `--yes`, and their short aliases.

## Examples

```text
/update --check
/update
/update --force
/upgrade --yes
```

More backend detail: [`wum update`](command-update.md)

Next: [Session Commands](interactive-session.md)
