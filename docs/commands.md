---
title: Command Reference
layout: default
parent: Use WUM
nav_order: 2
description: "Every WUM command, sub-command, argument, option, and example."
---

# Command Reference
{: .no_toc }

1. TOC
{:toc}

---

[← Docs index](index.md)

Complete reference for every WUM command, sub-command, argument, and option. Synopsis applies whether you run the published `wum.exe` or `dotnet run --project src/WUM.CLI -- <args>`.

Running `wum` with no arguments opens the smart interactive shell. In that shell, prefix commands with `/`: `/status`, `/list --security`, `/install KB5034441 --dry-run`. See [Interactive Mode](interactive.md) for the full prompt, shortcuts, history, and completion behavior.

## Global options & flags

| Flag | Effect | Admin |
|---|---|---|
| `--version` | Print the version string (e.g. `{{ site.data.project.version }}`) | No |
| `--info` | Show developer / build info (version, commit, build date, author, license, repo, runtime, OS) | No |
| `--help` | Show help for the root command or any sub-command | No |

`--info` is a global option intercepted in `Program.Main` before parsing, so `wum --info` works from anywhere in the command tree.

### Common output options

Most read commands share these:

| Option | Effect |
|---|---|
| `--json` | Emit machine-readable JSON instead of the colorized view. Ideal for scripting/piping |
| `--no-color` | Disable ANSI colors (where supported — `list`) |
| `--verbose`, `-v` | Step-by-step / full-detail debug output (`status`, `list`) |
| `--refresh` | Force a fresh online scan, bypassing the 5-minute scan cache (`status`, `list`, `diagnose`) |

---

## Command summary

| Command | Purpose | Admin |
|---|---|---|
| [`status`](#wum-status) | Dashboard of update state | No |
| [`list`](#wum-list) | List available / installed / hidden updates | No |
| [`search`](#wum-search) | Full-text search across available updates | No |
| [`install`](#wum-install) | Download & install updates | **Yes** |
| [`uninstall`](#wum-uninstall) | Remove an installed update | **Yes** |
| [`hide`](#wum-hide) | Hide / unhide / list hidden updates | Partial |
| [`history`](#wum-history) | Show install history | No |
| [`pause`](#wum-pause) | Pause / resume updates | **Yes** |
| [`schedule`](#wum-schedule) | Manage a weekly update schedule | **Yes** |
| [`settings`](#wum-settings) | View / change WU settings | **Yes** (set/reset) |
| [`reboot`](#wum-reboot) | Schedule / cancel a restart | **Yes** |
| [`diagnose`](#wum-diagnose) | Health check + optional component reset | **Yes** (`--fix`) |

---

## Command argument and option matrix

| Command | Arguments / subcommands | Supported options |
|---|---|---|
| `wum` | none; opens interactive mode when run with no args | `--version`, `--info`, `--help` |
| `wum status` | none | `--json`, `--verbose`/`-v`, `--refresh` |
| `wum list` | none | `--security`, `--critical`, `--optional`, `--drivers`, `--definition`, `--hidden`, `--installed`, `--microsoft-update`/`--mu`, `--json`, `--no-color`, `--verbose`/`-v`, `--refresh` |
| `wum search` | `<term>` | `--category <name>`, `--json`, `--microsoft-update`/`--mu` |
| `wum install` | `[<kb-articles>...]` | `--security`, `--critical`, `--all`, `--definition`, `--dry-run`, `--force`/`-f`, `--no-reboot`, `--microsoft-update`/`--mu` |
| `wum uninstall` | `<kb-article>` | `--force`/`-f` |
| `wum hide add` | `<update-id>` | none |
| `wum hide remove` | `<update-id>` | none |
| `wum hide list` | none | none |
| `wum history` | none | `--count`/`-n <N>`, `--failed`, `--kb <KB>`, `--json` |
| `wum pause` | none | `--days <N>` |
| `wum pause resume` | none | none |
| `wum schedule` | none; same as `show` | none |
| `wum schedule show` | none | none |
| `wum schedule set` | none | `--day <DayOfWeek>`, `--time <HH:mm>`, `--auto-install`, `--auto-reboot`, `--all` |
| `wum schedule clear` | none | none |
| `wum settings` | none; same as `show` | none |
| `wum settings show` | none | none |
| `wum settings set` | `<key> <value>` | none |
| `wum settings reset` | none | none |
| `wum reboot` | none | `--delay <seconds>`, `--force`/`-f`, `--cancel` |
| `wum diagnose` | none | `--refresh`, `--json`, `--fix`, `--force`/`-f` |

Interactive mode supports the same command arguments and options with a slash prefix, for example `/list --security` or `/install KB5034441 --dry-run`. Session-only commands are documented in [Interactive Mode → Session commands](interactive.md#session-commands).

---

## `wum status`

Dashboard showing WU service state, reboot requirement, pause info, and available-update counts by category.

**Synopsis**

```
wum status [--json] [--verbose|-v] [--refresh]
```

**Options**

| Option | Default | Description |
|---|---|---|
| `--json` | off | Output as JSON |
| `--verbose`, `-v` | off | Step-by-step debug trace (service check, reboot check, pause read, COM fetch, per-update list) |
| `--refresh` | off | Force a fresh online scan instead of the shared cache |

**Examples**

```bash
wum status                 # Colorized dashboard
wum status --json          # Machine-readable
wum status -v              # Debug trace of every step
wum status --refresh       # Re-scan WU online, ignore cache
```

**System verdict logic**

- `0` updates → `✓ System is up to date` (green)
- any security/critical update → `! Security updates available` (red)
- otherwise → `● Updates available` (yellow)

**JSON shape**

```json
{
  "reboot_required": false,
  "updates_available": 3,
  "security_updates": 1,
  "updates_paused": false,
  "paused_until": null,
  "wu_service": "Running",
  "checked_at": "2026-06-24T10:15:00"
}
```

---

## `wum list`

List available updates (default), installed updates, or include hidden ones — with category filtering.

**Synopsis**

```
wum list [--security] [--critical] [--optional] [--drivers] [--definition]
         [--hidden] [--installed] [--microsoft-update|--mu]
         [--json] [--no-color] [--verbose|-v] [--refresh]
```

**Options**

| Option | Description |
|---|---|
| `--security` | Security updates only |
| `--critical` | Critical updates only |
| `--optional` | Optional updates only |
| `--drivers` | Driver updates only |
| `--definition` | Definition / Defender updates only |
| `--hidden` | Include hidden updates in the scan |
| `--installed` | Show **installed** updates instead of available |
| `--microsoft-update`, `--mu` | Also query the Microsoft Update service (drivers + other MS products) |
| `--json` | Output as JSON (the filtered list of `WindowsUpdate` objects) |
| `--no-color` | Disable ANSI colors |
| `--verbose`, `-v` | Full detail per update (KB, category, severity, size, status, reboot, description, support URL) |
| `--refresh` | Force a fresh online scan instead of the shared cache |

> Category flags are **OR**-combined: `wum list --security --drivers` shows both. With no filter, all updates are listed. Filtering is applied **after** the scan, in C#.

**Examples**

```bash
wum list                       # All available updates (table)
wum list --installed           # Installed updates instead
wum list --security            # Security only
wum list --drivers --mu        # Drivers, querying Microsoft Update too
wum list --hidden              # Include hidden updates
wum list -v                    # Full detail per update
wum list --json                # JSON for scripting
wum list --no-color            # Plain text for piping
wum list --refresh             # Bypass the 5-min cache
```

The footer reports total download size and how many updates need a reboot.

---

## `wum search`

Full-text search across available updates. Matches the term (case-insensitive) against **title**, **KB article**, and **description**. The search always includes hidden updates so nothing is missed.

**Synopsis**

```
wum search <term> [--category <name>] [--json] [--microsoft-update|--mu]
```

**Argument**

| Argument | Description |
|---|---|
| `term` | KB number or keyword (required) |

**Options**

| Option | Description |
|---|---|
| `--category <name>` | Filter results by category: `Security`, `Critical`, `Optional`, `Driver`, `FeatureUpdate`, `CumulativeUpdate`, `Definition`, `ServicePack` (parsed case-insensitively) |
| `--json` | Output results as JSON |
| `--microsoft-update`, `--mu` | Also query Microsoft Update |

**Examples**

```bash
wum search KB5034441                  # By KB number
wum search defender                   # By keyword
wum search cumulative --category Security
wum search driver --mu --json
```

---

## `wum install`

Download and install updates with an install-plan preview, per-update progress bars, and a post-install summary. **Requires Administrator.**

**Synopsis**

```
wum install [<kb-articles>...] [--security] [--critical] [--all] [--definition]
            [--dry-run] [--force|-f] [--no-reboot] [--microsoft-update|--mu]
```

**Argument**

| Argument | Description |
|---|---|
| `kb-articles` | Zero or more KB numbers, e.g. `KB5034441 KB5035853`. A bare number is auto-prefixed with `KB` |

**Options**

| Option | Description |
|---|---|
| `--security` | Install security updates only |
| `--critical` | Install critical updates only |
| `--all` | Install **all** available updates |
| `--definition` | Install definition / Defender updates |
| `--dry-run` | Print the install plan and stop — no changes |
| `--force`, `-f` | Skip the confirmation prompt |
| `--no-reboot` | Do not prompt to reboot after install |
| `--microsoft-update`, `--mu` | Also query Microsoft Update when scanning |

**Target resolution order** (`ResolveTargets`)

1. **Explicit KBs** given → install exactly those (warns on any not found).
2. `--all` → every available update.
3. `--security` + `--critical` → both categories.
4. A single category flag → that category.
5. **No flags** → security + critical only; if there are none, falls back to **all** available.

**Examples**

```bash
wum install KB5034441                  # Specific KB
wum install KB5034441 KB5035853        # Multiple KBs
wum install --security                 # Security only
wum install --all                      # Everything
wum install --all --force              # No confirmation
wum install --all --dry-run            # Preview only
wum install --all --no-reboot          # Don't prompt for restart
wum install --definition --mu          # Defender defs incl. Microsoft Update
```

**Flow:** scan → resolve targets → show plan (grouped by category, totals, reboot count) → confirm (unless `--force`) → per-update download + install with progress → summary (installed / failed / skipped, with failure reasons) → reboot prompt (unless `--no-reboot`, schedules a 30s restart via `shutdown /r`).

> On any successful install the scan cache is invalidated so the next `status`/`list`/`diagnose` reflects the new state. Failure reasons come from `IUpdateService.LastError` (decoded HRESULT where possible).

---

## `wum uninstall`

Remove a previously installed update by KB number, via `wusa.exe /uninstall`. **Requires Administrator.**

**Synopsis**

```
wum uninstall <kb-article> [--force|-f]
```

**Argument**

| Argument | Description |
|---|---|
| `kb-article` | KB number, e.g. `KB5034441` (bare number auto-prefixed) |

**Options**

| Option | Description |
|---|---|
| `--force`, `-f` | Skip the confirmation prompt |

**Examples**

```bash
wum uninstall KB5034441
wum uninstall 5034441 --force
```

> Uninstall may trigger a system restart (handled by `wusa.exe /quiet /norestart`; Windows may still require a reboot to finalize).

---

## `wum hide`

Hide, unhide, or list hidden updates. Hidden updates are suppressed from the default available list.

**Sub-commands**

```
wum hide add <update-id>        # Hide an update
wum hide remove <update-id>     # Unhide a hidden update
wum hide list                   # List all hidden updates
```

| Sub-command | Argument | Admin | Description |
|---|---|---|---|
| `add` | `update-id` (Update ID or KB) | Yes | Sets `IsHidden = true` on the update via WUA |
| `remove` | `update-id` | Yes | Sets `IsHidden = false` |
| `list` | — | No | Scans with hidden included and shows only hidden ones |

**Examples**

```bash
wum hide add 12345678-90ab-cdef-1234-567890abcdef
wum hide remove 12345678-90ab-cdef-1234-567890abcdef
wum hide list
```

> Get an update's ID from `wum list --json` (the `Id` field) or `wum search <kb> --json`.

---

## `wum history`

Show Windows Update install history from the WUA history store.

**Synopsis**

```
wum history [--count|-n <N>] [--failed] [--kb <KB>] [--json]
```

**Options**

| Option | Default | Description |
|---|---|---|
| `--count`, `-n <N>` | `20` | Number of records to fetch |
| `--failed` | off | Show failed entries only (`Success == false`) |
| `--kb <KB>` | — | Filter to entries whose KB contains the value (case-insensitive) |
| `--json` | off | Output as JSON |

**Examples**

```bash
wum history                    # Last 20 records
wum history -n 50              # Last 50
wum history --failed           # Only failures
wum history --kb KB5034441     # One KB's history
wum history --json -n 100      # JSON, last 100
```

Each record carries: title, KB, installed date, success flag, result code, error message (hex), and operation type.

---

## `wum pause`

Pause all Windows Updates for a number of days, or resume early. Writes the standard WU pause registry values. **Requires Administrator.**

**Synopsis**

```
wum pause [--days <N>]
wum pause resume
```

**Options**

| Option | Default | Description |
|---|---|---|
| `--days <N>` | `7` | Days to pause. **Clamped to 1–35** |

**Sub-command**

| Sub-command | Description |
|---|---|
| `resume` | Resume updates immediately (deletes the pause registry values) |

**Examples**

```bash
wum pause                      # Pause 7 days (default)
wum pause --days 14            # Pause 14 days
wum pause --days 99            # Clamped to 35
wum pause resume               # Resume now
```

See [Configuration → Pause](configuration.md#pause) for the exact registry values written.

---

## `wum schedule`

Manage a persisted weekly update schedule. **Requires Administrator** for `set`/`clear`.

**Sub-commands**

```
wum schedule                   # Show current schedule (default)
wum schedule show              # Same as above
wum schedule set [options]     # Configure
wum schedule clear             # Remove the schedule
```

**`set` options**

| Option | Default | Description |
|---|---|---|
| `--day <DayOfWeek>` | `Sunday` | Day of week (`Monday`…`Sunday`, case-insensitive) |
| `--time <HH:mm>` | `02:00` | Time of day, 24-hour `HH:mm` |
| `--auto-install` | off | Auto-install when the schedule fires |
| `--auto-reboot` | off | Auto-reboot after install |
| `--all` | off | Install all updates (otherwise security only) |

**Examples**

```bash
wum schedule                   # Show
wum schedule set               # Defaults: Sunday 02:00
wum schedule set --day Friday --time 03:00 --auto-install --auto-reboot --all
wum schedule clear             # Remove
```

> Invalid `--day` or non-`HH:mm` `--time` is rejected with an error. The schedule view shows the computed **Next Run** time.

---

## `wum settings`

View or change Windows Update settings stored in the registry. **Requires Administrator** for `set`/`reset`.

**Sub-commands**

```
wum settings                       # Display all (default)
wum settings show                  # Same as above
wum settings set <key> <value>     # Change one setting
wum settings reset                 # Reset all to defaults (confirms first)
```

**`set` arguments**

| Argument | Description |
|---|---|
| `key` | Setting name (see table) |
| `value` | New value |

**Available keys**

| Key | Values | Description |
|---|---|---|
| `auto-download` | `true`/`false` | Automatically download updates |
| `auto-install` | `true`/`false` | Automatically install updates |
| `install-drivers` | `true`/`false` | Include driver updates |
| `install-optional` | `true`/`false` | Include optional updates |
| `notify-new` | `true`/`false` | Notify on new updates |
| `notify-complete` | `true`/`false` | Notify on install complete |
| `pause-metered` | `true`/`false` | Pause on metered connections |
| `defer-feature` | `0`–`365` | Defer feature updates (days) |
| `defer-quality` | `0`–`30` | Defer quality updates (days) |
| `active-hours` | `start-end` | Active-hours range, e.g. `8-22` |

> Booleans accept `true`, `1`, `yes`, or `on` (anything else is false). An unknown key throws and the CLI prints the full valid-key list.

**Examples**

```bash
wum settings                              # Show everything
wum settings set auto-download true       # Enable auto-download
wum settings set active-hours 9-18        # Active hours 9–18
wum settings set defer-feature 30         # Defer features 30 days
wum settings reset                        # Back to defaults
```

---

## `wum reboot`

Schedule or cancel a post-update restart. **Requires Administrator.**

**Synopsis**

```
wum reboot [--delay <seconds>] [--force|-f] [--cancel]
```

**Options**

| Option | Default | Description |
|---|---|---|
| `--delay <seconds>` | `30` | Delay before restart |
| `--force`, `-f` | off | Reboot without confirmation (and even if no reboot is "required") |
| `--cancel` | off | Cancel a scheduled restart (`shutdown /a`) |

**Examples**

```bash
wum reboot                     # Prompt, 30s delay
wum reboot --delay 60          # 60s delay
wum reboot --force             # No confirmation
wum reboot --cancel            # Abort a scheduled reboot
```

> Without `--force`, if no reboot is currently required the command reports that and exits. The restart is scheduled with `shutdown /r /t <delay> /c "<message>"`.

---

## `wum diagnose`

Run a comprehensive Windows Update health check (~30–60s). Optionally reset WU components with `--fix`. Optionally emit JSON with a scriptable exit code.

**Synopsis**

```
wum diagnose [--refresh] [--json]
wum diagnose --fix [--force|-f]
```

**Options**

| Option | Description |
|---|---|
| `--refresh` | Force a fresh update scan (re-counts available updates and rewrites the cache so `status`/`list` agree) |
| `--json` | Output structured diagnostics + an exit code, then stop |
| `--fix` | **Reset Windows Update components** — destructive, admin only (see below) |
| `--force`, `-f` | Skip the `--fix` confirmation prompt |

**Examples**

```bash
wum diagnose                   # Full colorized report
wum diagnose --refresh         # Re-scan instead of using the cache
wum diagnose --json            # Structured output + exit code
echo $?                        # (PowerShell: $LASTEXITCODE) read the bitmask
wum diagnose --fix             # Reset WU components (confirms first)
wum diagnose --fix --force     # Reset without confirmation
```

`--fix` stops services, renames `SoftwareDistribution` + `catroot2` to `.bak`, reregisters WU DLLs, resets WinSock/WinHTTP proxy, restarts services, and triggers re-detection. **Pending downloads are discarded; a reboot is advised.**

The exit code is a **bitmask** for scripting. See [Diagnostics](diagnostics.md) for the full check list, the bitmask values, the health score, and how `Search Error` HRESULTs are decoded.

Next: [Architecture →](architecture.md)
