---
title: "/search"
layout: default
parent: Interactive Mode
grand_parent: Use WUM
nav_order: 4
description: "Interactive /search command: query by KB, title, description, category, and Microsoft Update."
---

# `/search`

Searches available updates by KB number, title, or description.

## Synopsis

```text
/search <term> [--category <name>] [--json] [--microsoft-update|--mu]
```

CLI equivalent:

```powershell
wum search <term> [--category <name>] [--json] [--microsoft-update|--mu]
```

## Arguments

| Argument | Required | Detail |
|---|---|---|
| `term` | Yes | KB number or keyword. Matching is case-insensitive. |

Use quotes for multi-word terms:

```text
/search "security update"
```

## Options

| Option | Detail |
|---|---|
| `--category <name>` | Limit matches to one category, such as `Security`, `Critical`, `Driver`, or `Definition`. |
| `--json` | Print JSON results. |
| `--microsoft-update`, `--mu` | Include Microsoft Update service. |

## Behavior

`/search` is read-only. No Administrator terminal needed.

It includes hidden updates, so a hidden matching update can still be found.

## Examples

```text
/search KB5034441
/search defender
/search cumulative --category Security
/search driver --mu --json
```

More backend detail: [`wum search`](command-search.md)

Next: [`/install`](interactive-command-install.md)
