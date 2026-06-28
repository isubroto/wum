---
title: Command Reference
layout: default
parent: Interactive Mode
grand_parent: Use WUM
nav_order: 1
description: "Interactive mode slash commands, arguments, options, session commands, and deep links."
---

# Interactive Command Reference

Interactive mode uses slash-prefixed commands. These map directly to CLI commands:

```text
/status
/list --security
/install KB5034441 --dry-run
```

equals:

```powershell
wum status
wum list --security
wum install KB5034441 --dry-run
```

All normal command arguments and options work in interactive mode. The prompt adds completion, history, command groups, and session-only helpers.

## Command groups

| Group | Commands |
|---|---|
| Look around | `/status`, `/list`, `/search`, `/history` |
| Take action | `/install`, `/uninstall`, `/hide` |
| Stay in control | `/pause`, `/schedule`, `/settings`, `/reboot`, `/diagnose` |
| This session | [`/commands`, `/help`, `/keys`, `/clear`, `/version`, `/info`, `/exit`](interactive-session.md) |

## Slash command matrix

| Interactive command | Arguments / subcommands | Supported options | Interactive page | CLI page |
|---|---|---|---|---|
| `/status` | none | `--json`, `--verbose`/`-v`, `--refresh` | [`/status`](interactive-command-status.md) | [`wum status`](command-status.md) |
| `/list` | none | `--security`, `--critical`, `--optional`, `--drivers`, `--definition`, `--hidden`, `--installed`, `--microsoft-update`/`--mu`, `--json`, `--no-color`, `--verbose`/`-v`, `--refresh` | [`/list`](interactive-command-list.md) | [`wum list`](command-list.md) |
| `/search` | `<term>` | `--category <name>`, `--json`, `--microsoft-update`/`--mu` | [`/search`](interactive-command-search.md) | [`wum search`](command-search.md) |
| `/install` | `[<kb-articles>...]` | `--security`, `--critical`, `--all`, `--definition`, `--dry-run`, `--force`/`-f`, `--no-reboot`, `--microsoft-update`/`--mu` | [`/install`](interactive-command-install.md) | [`wum install`](command-install.md) |
| `/uninstall` | `<kb-article>` | `--force`/`-f` | [`/uninstall`](interactive-command-uninstall.md) | [`wum uninstall`](command-uninstall.md) |
| `/hide add` | `<update-id>` | none | [`/hide`](interactive-command-hide.md) | [`wum hide`](command-hide.md) |
| `/hide remove` | `<update-id>` | none | [`/hide`](interactive-command-hide.md) | [`wum hide`](command-hide.md) |
| `/hide list` | none | none | [`/hide`](interactive-command-hide.md) | [`wum hide`](command-hide.md) |
| `/history` | none | `--count`/`-n <N>`, `--failed`, `--kb <KB>`, `--json` | [`/history`](interactive-command-history.md) | [`wum history`](command-history.md) |
| `/pause` | none | `--days <N>` | [`/pause`](interactive-command-pause.md) | [`wum pause`](command-pause.md) |
| `/pause resume` | none | none | [`/pause`](interactive-command-pause.md) | [`wum pause`](command-pause.md) |
| `/schedule` | none; same as `show` | none | [`/schedule`](interactive-command-schedule.md) | [`wum schedule`](command-schedule.md) |
| `/schedule show` | none | none | [`/schedule`](interactive-command-schedule.md) | [`wum schedule`](command-schedule.md) |
| `/schedule set` | none | `--day <DayOfWeek>`, `--time <HH:mm>`, `--auto-install`, `--auto-reboot`, `--all` | [`/schedule`](interactive-command-schedule.md) | [`wum schedule`](command-schedule.md) |
| `/schedule clear` | none | none | [`/schedule`](interactive-command-schedule.md) | [`wum schedule`](command-schedule.md) |
| `/settings` | none; same as `show` | none | [`/settings`](interactive-command-settings.md) | [`wum settings`](command-settings.md) |
| `/settings show` | none | none | [`/settings`](interactive-command-settings.md) | [`wum settings`](command-settings.md) |
| `/settings set` | `<key> <value>` | none | [`/settings`](interactive-command-settings.md) | [`wum settings`](command-settings.md) |
| `/settings reset` | none | none | [`/settings`](interactive-command-settings.md) | [`wum settings`](command-settings.md) |
| `/reboot` | none | `--delay <seconds>`, `--force`/`-f`, `--cancel` | [`/reboot`](interactive-command-reboot.md) | [`wum reboot`](command-reboot.md) |
| `/diagnose` | none | `--refresh`, `--json`, `--fix`, `--force`/`-f` | [`/diagnose`](interactive-command-diagnose.md) | [`wum diagnose`](command-diagnose.md) |

## Detailed interactive pages

| Page | Detail covered |
|---|---|
| [`/status`](interactive-command-status.md) | Status display, JSON, verbose, refresh |
| [`/list`](interactive-command-list.md) | Filters, installed/hidden scans, output modes |
| [`/search`](interactive-command-search.md) | Search terms, category filter, Microsoft Update |
| [`/install`](interactive-command-install.md) | KB targets, category installs, dry-run, confirmation |
| [`/uninstall`](interactive-command-uninstall.md) | KB uninstall, force mode |
| [`/hide`](interactive-command-hide.md) | Add/remove/list hidden updates |
| [`/history`](interactive-command-history.md) | Count, failed-only, KB filter, JSON |
| [`/pause`](interactive-command-pause.md) | Pause days, resume |
| [`/schedule`](interactive-command-schedule.md) | Weekly schedule, auto install, clear |
| [`/settings`](interactive-command-settings.md) | Show/set/reset WU settings |
| [`/reboot`](interactive-command-reboot.md) | Delay, force, cancel restart |
| [`/diagnose`](interactive-command-diagnose.md) | Health check, JSON, fix |
| [Session Commands](interactive-session.md) | `/commands`, `/help`, `/keys`, `/clear`, `/version`, `/info`, `/exit` |

## Completion behavior

Typing `/` opens command suggestions. Typing a command plus a space opens subcommand or option suggestions when WUM knows them:

```text
/status --v
```

can complete to:

```text
/status --verbose
```

`Tab` accepts current suggestion. `Shift+Tab` cycles backward. `Right Arrow` accepts ghost-text completion at end of line.

Next: [`/status`](interactive-command-status.md)
