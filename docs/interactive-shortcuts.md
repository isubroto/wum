---
title: Shortcuts
layout: default
parent: Interactive Mode
grand_parent: Use WUM
nav_order: 15
description: "Interactive mode keyboard shortcuts, completion, and history behavior."
---

# Interactive Shortcuts

## Keyboard shortcuts

| Key | Effect |
|---|---|
| `Tab` | Complete the current suggestion |
| `Shift+Tab` | Cycle suggestions backward |
| `Enter` | Run the command, or complete a selected bare command suggestion |
| `Right Arrow` at end of line | Accept ghost-text completion |
| `Up` / `Down` | Browse history at an empty prompt, or move through suggestions |
| `Ctrl+P` / `Ctrl+N` | Browse history backward / forward |
| `Ctrl+A` / `Ctrl+E` | Move to start / end of line |
| `Ctrl+U` / `Ctrl+K` | Delete before / after cursor |
| `Ctrl+W` | Delete previous word |
| `Backspace` / `Delete` | Delete previous / next character |
| `Esc` | Clear the current line |
| `Ctrl+L` | Clear screen and scrollback, then redraw interactive mode |
| `Ctrl+C` | First press cancels the current line; second press exits |
| `Ctrl+D` on an empty prompt | Exit interactive mode |

## History

History stores recent non-empty, non-exit commands in:

```text
%LOCALAPPDATA%\WUM\interactive-history.txt
```

WUM keeps the newest 200 entries and skips duplicate consecutive commands.

Back to [Session Commands](interactive-session.md) · [Interactive Command Reference](interactive-commands.md) · [Interactive Mode](interactive.md)
