---
title: "/hide"
layout: default
parent: Interactive Mode
grand_parent: Use WUM
nav_order: 7
description: "Interactive /hide command: hide, unhide, and list hidden updates."
---

# `/hide`

Hides, unhides, or lists hidden Windows Updates.

## Synopsis

```text
/hide add <update-id>
/hide remove <update-id>
/hide list
```

CLI equivalent:

```powershell
wum hide add <update-id>
wum hide remove <update-id>
wum hide list
```

## Subcommands

| Subcommand | Argument | Admin | Detail |
|---|---|---|---|
| `add` | `<update-id>` | Yes | Hide update. |
| `remove` | `<update-id>` | Yes | Unhide update. |
| `list` | none | No | Show hidden updates. |

## Update IDs

Use `/list --json` or `/search <kb> --json` to find update `Id` values.

## Examples

```text
/hide add 12345678-90ab-cdef-1234-567890abcdef
/hide remove 12345678-90ab-cdef-1234-567890abcdef
/hide list
```

More backend detail: [`wum hide`](command-hide.md)

Next: [`/history`](interactive-command-history.md)
