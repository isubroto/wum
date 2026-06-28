---
title: "/settings"
layout: default
parent: Interactive Mode
grand_parent: Use WUM
nav_order: 11
description: "Interactive /settings command: show, set, or reset Windows Update settings."
---

# `/settings`

Shows, changes, or resets Windows Update preferences.

## Synopsis

```text
/settings
/settings show
/settings set <key> <value>
/settings reset
```

CLI equivalent:

```powershell
wum settings [show|set|reset] [key] [value]
```

## Subcommands

| Subcommand | Detail |
|---|---|
| none | Same as `show`. |
| `show` | Show current settings. |
| `set <key> <value>` | Set one preference. |
| `reset` | Reset preferences to defaults. |

## Common keys

| Key | Example |
|---|---|
| `auto-download` | `/settings set auto-download true` |
| `active-hours` | `/settings set active-hours 9-18` |
| `defer-feature` | `/settings set defer-feature 30` |

## Behavior

`/settings show` is read-only. `/settings set` and `/settings reset` require Administrator.

Use quotes if a value needs spaces.

## Examples

```text
/settings
/settings show
/settings set auto-download true
/settings set active-hours 9-18
/settings set defer-feature 30
/settings reset
```

More backend detail: [`wum settings`](command-settings.md)

Next: [`/reboot`](interactive-command-reboot.md)
