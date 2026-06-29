---
title: settings
layout: default
parent: CLI Mode
grand_parent: Use WUM
nav_order: 11
description: "Settings command details: show, set, reset, setting keys and accepted values."
---

# `wum settings`

Shows or changes WUM/Windows Update settings stored in the registry.

## Synopsis

```powershell
wum settings
wum settings show
wum settings set <key> <value>
wum settings reset
```

## Interactive form

```text
/settings
/settings show
/settings set <key> <value>
/settings reset
```

## Subcommands

| Subcommand | Admin | Detail |
|---|---|---|
| default / `show` | No | Shows current settings. |
| `set <key> <value>` | Yes | Changes one setting. |
| `reset` | Yes | Resets all settings to defaults after confirmation. |

## Setting keys

| Key | Values | Detail |
|---|---|---|
| `auto-download` | `true` / `false` | Automatically download updates. |
| `auto-install` | `true` / `false` | Automatically install updates. |
| `install-drivers` | `true` / `false` | Include driver updates. |
| `install-optional` | `true` / `false` | Include optional updates. |
| `notify-new` | `true` / `false` | Notify when new updates exist. |
| `notify-complete` | `true` / `false` | Notify when install completes. |
| `pause-metered` | `true` / `false` | Pause on metered connections. |
| `defer-feature` | `0`-`365` | Defer feature updates in days. |
| `defer-quality` | `0`-`30` | Defer quality updates in days. |
| `active-hours` | `start-end` | Active hours, for example `8-22`. |

Booleans accept `true`, `1`, `yes`, and `on` as true values.

## Examples

```powershell
wum settings
wum settings show
wum settings set auto-download true
wum settings set active-hours 9-18
wum settings set defer-feature 30
wum settings reset
```

Next: [`reboot`](command-reboot.md)
