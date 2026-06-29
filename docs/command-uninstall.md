---
title: uninstall
layout: default
parent: CLI Mode
grand_parent: Use WUM
nav_order: 6
description: "Uninstall command details: KB argument, force mode, wusa behavior."
---

# `wum uninstall`

Removes an installed update by KB number using `wusa.exe /uninstall`.

## Synopsis

```powershell
wum uninstall <kb-article> [--force|-f]
```

## Interactive form

```text
/uninstall <kb-article> [--force|-f]
```

## Arguments

| Argument | Required | Detail |
|---|---|---|
| `kb-article` | Yes | KB number, for example `KB5034441`. A bare number is auto-prefixed with `KB`. |

## Options

| Option | Detail |
|---|---|
| `--force`, `-f` | Skip confirmation prompt. |

## Behavior

`uninstall` requires Administrator. WUM normalizes the KB value, asks for confirmation unless forced, then calls Windows Update Standalone Installer.

Windows may require a reboot even when `wusa.exe` runs with no immediate restart.

## Examples

```powershell
wum uninstall KB5034441
wum uninstall 5034441 --force
```

Next: [`hide`](command-hide.md)
