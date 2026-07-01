---
title: CLI Mode
layout: default
parent: Use WUM
nav_order: 1
has_children: true
description: "Run WUM as one-shot commands for scripts, automation, and terminal workflows."
---

# CLI Mode

CLI mode is the one-shot command style:

```powershell
wum status
wum list --security
wum install KB5034441 --dry-run
```

Use CLI mode when you want repeatable commands, scripts, JSON output, CI checks, or a direct terminal workflow. Each command exits after it finishes.

## CLI vs interactive

| Mode | Best for | Command shape |
|---|---|---|
| CLI mode | Scripts, automation, one-off terminal commands, JSON output | `wum list --security` |
| Interactive mode | Exploring commands, using completion, repeating update tasks in one session | `/list --security` |

Both modes call the same command handlers. Interactive mode adds slash prefixes, history, completion, and session commands.

## CLI docs

<div class="card-grid">
  <a class="card" href="{{ '/commands.html' | relative_url }}">
    <span class="card-title">Command Reference</span>
    <span class="card-desc">Every command, argument, option, admin requirement, and example.</span>
  </a>
  <a class="card" href="{{ '/diagnostics.html' | relative_url }}">
    <span class="card-title">Diagnostics</span>
    <span class="card-desc">Health checks, exit-code bitmask, scan details, and component reset guidance.</span>
  </a>
  <a class="card" href="{{ '/troubleshooting.html' | relative_url }}">
    <span class="card-title">Troubleshooting</span>
    <span class="card-desc">Symptom-to-fix notes for common Windows Update failures.</span>
  </a>
</div>

## Command pages

| Command | Detail page |
|---|---|
| `status` | [Status](command-status.md) |
| `list` | [List](command-list.md) |
| `search` | [Search](command-search.md) |
| `install` | [Install](command-install.md) |
| `uninstall` | [Uninstall](command-uninstall.md) |
| `hide` | [Hide](command-hide.md) |
| `history` | [History](command-history.md) |
| `pause` | [Pause](command-pause.md) |
| `schedule` | [Schedule](command-schedule.md) |
| `settings` | [Settings](command-settings.md) |
| `reboot` | [Reboot](command-reboot.md) |
| `diagnose` | [Diagnose](command-diagnose.md) |
| `update`, `upgrade` | [Update / Upgrade](command-update.md) |

Next: [Command Reference →](commands.md)
