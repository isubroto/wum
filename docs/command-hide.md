---
title: hide
layout: default
parent: CLI Mode
grand_parent: Use WUM
nav_order: 7
description: "Hide command details: hide, unhide, list hidden updates, update IDs."
---

# `wum hide`

Hides, unhides, or lists hidden Windows Updates. Hidden updates are suppressed from the normal available-update list.

## Synopsis

```powershell
wum hide add <update-id>
wum hide remove <update-id>
wum hide list
```

## Interactive form

```text
/hide add <update-id>
/hide remove <update-id>
/hide list
```

## Subcommands

| Subcommand | Argument | Admin | Detail |
|---|---|---|---|
| `add` | `<update-id>` | Yes | Sets the update hidden flag. |
| `remove` | `<update-id>` | Yes | Clears the update hidden flag. |
| `list` | none | No | Lists updates currently hidden. |

## Update IDs

Use `wum list --json` or `wum search <kb> --json` to get an update `Id`. Depending on scan result, WUM can also match KB-like identifiers when available.

## Examples

```powershell
wum hide add 12345678-90ab-cdef-1234-567890abcdef
wum hide remove 12345678-90ab-cdef-1234-567890abcdef
wum hide list
```

Next: [`history`](command-history.md)
