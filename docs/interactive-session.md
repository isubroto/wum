---
title: "Session Commands"
layout: default
parent: Interactive Mode
grand_parent: Use WUM
nav_order: 14
description: "Interactive-only commands for help, command palette, clear, version, info, and exit."
---

# Session Commands

These commands exist only inside interactive mode. They control the shell session, not Windows Update state.

## Commands

| Command | Effect |
|---|---|
| `/commands` | Show grouped command palette. |
| `/help` or `/?` | Show interactive help. |
| `/help <command>` | Show help for one command, for example `/help list`. |
| `/keys` | Show keyboard shortcuts. |
| `/clear` | Clear visible screen and terminal scrollback, then redraw welcome panel. |
| `/version` | Print WUM version. |
| `/info` | Show build and developer information. |
| `/exit`, `/quit`, `exit`, `quit` | Leave interactive mode. |

## Behavior

Session commands do not use a slash command's Windows Update handler. They execute immediately in the interactive shell.

`/clear` redraws interactive mode after clearing. `/exit` and `/quit` print goodbye and return to the parent terminal.

## Examples

```text
/commands
/help status
/keys
/clear
/version
/info
/exit
```

Next: [Shortcuts](interactive-shortcuts.md)
