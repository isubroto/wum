---
title: search
layout: default
parent: CLI Mode
grand_parent: Use WUM
nav_order: 4
description: "Search command details: search terms, category filters, Microsoft Update, JSON output."
---

# `wum search`

Searches available updates by KB number, title, or description. Search includes hidden updates so a hidden match is still discoverable.

## Synopsis

```powershell
wum search <term> [--category <name>] [--json] [--microsoft-update|--mu]
```

## Interactive form

```text
/search <term> [--category <name>] [--json] [--microsoft-update|--mu]
```

## Arguments

| Argument | Required | Detail |
|---|---|---|
| `term` | Yes | KB number or keyword. Matching is case-insensitive. |

## Options

| Option | Detail |
|---|---|
| `--category <name>` | Restricts matches to one category. Accepted values are parsed case-insensitively. Common values: `Security`, `Critical`, `Optional`, `Driver`, `FeatureUpdate`, `CumulativeUpdate`, `Definition`, `ServicePack`. |
| `--json` | Emits matched updates as JSON. |
| `--microsoft-update`, `--mu` | Also query Microsoft Update. Useful for drivers and other Microsoft products. |

## Behavior

`search` is read-only and does not require Administrator. It scans available updates, includes hidden updates, then matches the term against title, KB article, and description.

## Examples

```powershell
wum search KB5034441
wum search defender
wum search cumulative --category Security
wum search driver --mu --json
```

Next: [`install`](command-install.md)
