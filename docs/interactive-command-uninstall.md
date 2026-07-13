---
title: "/uninstall"
layout: default
parent: Interactive Mode
grand_parent: Use WUM
nav_order: 6
description: "Interactive /uninstall command: remove an installed KB update."
---

# `/uninstall`

Removes an installed update by KB number.

## Synopsis

```text
/uninstall <kb-article> [--force|-f]
```

## Arguments

| Argument | Required | Detail |
|---|---|---|
| `kb-article` | Yes | KB identifier, such as `KB5034441` or `5034441`. |

## Options

| Option | Detail |
|---|---|
| `--force`, `-f` | Skip confirmation prompt. |

## Behavior

`/uninstall` requires Administrator.

It normalizes the KB number, runs the uninstall, and reports whether Windows requires a restart.

## Examples

```text
/uninstall KB5034441
/uninstall 5034441 --force
```

Implementation detail: [uninstall behavior](command-uninstall.md)

Next: [`/hide`](interactive-command-hide.md)
