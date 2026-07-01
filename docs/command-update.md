---
title: update / upgrade
layout: default
parent: CLI Mode
grand_parent: Use WUM
nav_order: 14
description: "Update command details: check GitHub releases, download the MSI, and upgrade WUM."
---

# `wum update` / `wum upgrade`

Checks the latest GitHub release and can install the newest WUM MSI. `upgrade` is an alias for `update`.

## Synopsis

```powershell
wum update [--check|-c] [--force|-f|--yes|-y]
wum upgrade [--check|-c] [--force|-f|--yes|-y]
```

## Interactive form

```text
/update [--check|-c] [--force|-f|--yes|-y]
/upgrade [--check|-c] [--force|-f|--yes|-y]
```

## Options

| Option | Detail |
|---|---|
| `--check`, `-c` | Check for a newer release without downloading or installing. |
| `--force`, `-f` | Skip confirmation and install if an update is available. |
| `--yes`, `-y` | Alias for `--force`. |

## Admin

`--check` does not require Administrator.

Installing the MSI requires an elevated terminal because Windows Installer writes to Program Files and updates the installed app registration.

## Behavior

The command reads the current assembly version, calls GitHub's latest release API for `isubroto/wum`, compares versions, then downloads the first `.msi` release asset when a newer version exists.

The installer runs as:

```powershell
msiexec /i <downloaded-msi> /qb /norestart
```

`/qb` shows basic installer UI. `/norestart` lets WUM report reboot-required code `3010` instead of forcing a restart.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Up to date, cancelled, or installed successfully. |
| `1` | GitHub check, download, installer launch, or installer failed. |
| `2` | Update available in `--check` mode. |

## Examples

```powershell
wum update --check
wum update
wum update --force
wum upgrade --yes
```

Next: [Diagnostics](diagnostics.md)
