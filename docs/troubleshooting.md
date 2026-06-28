---
title: Troubleshooting
layout: default
parent: Use WUM
nav_order: 4
description: "Symptom → cause → fix for the most common Windows Update problems WUM surfaces."
---

# Troubleshooting
{: .no_toc }

1. TOC
{:toc}

---

[← Docs index](index.md)

Most problems are diagnosable with one command:

```console
wum diagnose
```

It prints targeted hints for whatever actually failed. This page expands each common symptom into **cause → fix**, and decodes the error codes WUM reports.

{: .note }
> When in doubt, run `wum diagnose --json` and read the `exitCode` bitmask (see [Diagnostics](diagnostics.md#exit-code-bitmask)). It tells you the failure class without reading the whole report.

---

## "0 updates found" but I expect some

`wum status` / `wum list` show zero, yet Windows Update GUI shows updates (or you know some are due).

| Likely cause | How to confirm | Fix |
|---|---|---|
| **Managed by WSUS / policy** | `wum diagnose` → `Update Source : WSUS (...)` | Updates come from the org server, not Microsoft Update. Expected on managed PCs — not a fault |
| **Microsoft Update service not registered** | `wum diagnose` → `Microsoft Upd : NOT registered` | Driver / MS-product updates are hidden from the default scan. Use `--mu`: `wum list --mu` |
| **Stale cache** | Ran a scan < 5 min ago | Force a fresh scan: `wum list --refresh` (or `status --refresh`) |
| **Not elevated** | `wum diagnose` → `Running Admin : False` | Some results need admin. Re-run in an Administrator terminal |
| **Genuinely up to date** | `Updates Found : 0` and no other hint | Nothing to do |

```console
wum list --refresh        # bypass the 5-minute scan cache
wum list --mu             # include Microsoft Update (drivers + MS products)
wum diagnose              # see Update Source + Microsoft Upd lines
```

---

## "COM Session: FAILED" / search throws

| Cause | Fix |
|---|---|
| Not running as Administrator | Re-run from an elevated terminal. COM failures almost always mean "not admin" |
| WU service disabled | `wum diagnose` shows `WU Service : ... (DISABLED)` → set `wuauserv` to Manual and start it |
| Corrupted WU components | `wum diagnose --fix` (resets `SoftwareDistribution`, re-registers DLLs), then reboot |

---

## Network shows UNREACHABLE

`wum diagnose` → `Network (WU) : UNREACHABLE`. WUM probes **TCP 443** to `sls.update.microsoft.com`, `download.windowsupdate.com`, `ctldl.windowsupdate.com` (it does **not** use ping — ICMP is blocked on MS hosts).

| Cause | Fix |
|---|---|
| Firewall blocking 443 to WU hosts | Allow outbound 443 to `*.windowsupdate.com` / `*.update.microsoft.com` |
| Proxy required but not configured | Check `wum diagnose` → `Proxy :` line; set WinHTTP proxy (`netsh winhttp set proxy ...`) |
| Wrong system clock breaks TLS | Error `0x80072F8F` — fix date/time, then retry |
| DNS failure | Error `0x80072EE7` — check DNS / `hosts` file |

---

## Updates won't download or install

| Symptom | Cause | Fix |
|---|---|---|
| `✗ Download failed` | BITS service down | `wum diagnose` → `BITS : Stopped` → start `bits` |
| `✗ Install failed — 0x800F0922` | Insufficient WinRE / recovery-partition space | Free space / resize the recovery partition |
| `update not found in catalog` | Already installed, hidden, or superseded | `wum list --hidden`; `wum history --kb <KB>` to check |
| Everything fails | Corrupt WU datastore | `wum diagnose` → `WU Datastore : MISSING` → `wum diagnose --fix` |
| Low disk space | `Disk Free : N GB (LOW)` (under 10 GB) | Free space on the system drive — WU needs headroom to stage |

WUM surfaces the underlying reason from `IUpdateService.LastError`, decoding the HRESULT where it can. See the [error-code table](#windows-update-error-codes) below.

---

## "A reboot is required" never clears

`wum status` keeps showing `Reboot Required : Yes`.

| Cause | Fix |
|---|---|
| Pending reboot genuinely outstanding | `wum reboot` (or `wum reboot --delay 60`) |
| Very long uptime delays finalization | `wum diagnose` → `Uptime : Nd ...` → just reboot |
| Stuck CBS state | `wum diagnose --fix`, then reboot |

WUM checks **update-specific** reboot markers only (WU / CBS / UpdateExe). It deliberately ignores `PendingFileRenameOperations` (set by routine installers), so a "Yes" here means a real update reboot is pending. See [Configuration → Reboot detection](configuration.md#reboot-detection).

---

## Pause / settings / schedule changes "don't stick"

| Cause | Fix |
|---|---|
| Not elevated | These write `HKLM` — run as Administrator |
| Org policy overrides | A WSUS/GPO-managed PC may re-apply policy. WUM writes the value; policy can revert it |
| Wrong key name | `wum settings set` prints the valid key list on an unknown key — see [Configuration → Settings](configuration.md#settings) |

```console
wum pause --days 14       # writes 5 pause values under HKLM\...\UX\Settings
wum pause resume          # deletes them
wum settings              # show current values to confirm
```

---

## `wum` is not recognized

| Cause | Fix |
|---|---|
| Not on `PATH` | MSI install adds it automatically; for a manual publish, add the folder containing `wum.exe` to `PATH` |
| Running from source | Use `dotnet run --project src/WUM.CLI -- <args>` instead |

---

## Reading the logs

Every run logs to:

```
%ProgramData%\WUM\logs\wum-YYYY-MM-DD.log
```

Debug-level, 7-day retention. When a command fails unexpectedly, the log has the full PowerShell stderr and exception. The colorized report footer points here too.

```console
# PowerShell — tail today's log
Get-Content "$env:ProgramData\WUM\logs\wum-$(Get-Date -f yyyy-MM-dd).log" -Tail 40 -Wait
```

---

## Windows Update error codes

WUM decodes the first `0x........` HRESULT in any failure message (`WuErrorCodes.Decode`). The high-signal set:

| Code | Meaning | Typical fix |
|---|---|---|
| `0x80072EE2` | Connection timed out (WININET) | Check network / proxy |
| `0x80072EE7` | Server name not resolved (DNS) | Check DNS / `hosts` |
| `0x80072EFD` | Cannot connect to update server | Check firewall / proxy |
| `0x80072F8F` | System clock wrong → TLS broken | Fix date/time |
| `0x8024401C` | HTTP 408 request timeout | Retry / check proxy |
| `0x80244022` | HTTP 503 — WSUS/IIS down | Wait / contact WSUS admin |
| `0x80244019` | HTTP 404 — content not found | Update superseded / retry later |
| `0x8024402C` | Proxy/connection error | Check proxy settings |
| `0x80070422` | WU service disabled | Set `wuauserv` to Manual + start |
| `0x8024002E` | Blocked by policy (WSUS/managed) | Expected on managed PCs |
| `0x8024A005` | Automatic Updates disabled by policy | Adjust GPO |
| `0x80240024` | No updates applicable | Up to date — no action |
| `0x8024000C` | No operation required | Up to date — no action |
| `0x80240034` | An update failed to download | Retry; check BITS / disk |
| `0x80246007` | Update not downloaded | Download first, then install |
| `0x800F0922` | Install failed — WinRE space | Free / resize recovery partition |

Full list: `src/WUM.Core/Helpers/WuErrorCodes.cs`. Unknown codes are reported as `0xCODE — Unrecognized Windows Update error code.`

---

## Last resort: reset Windows Update

When the datastore is corrupt or nothing scans, reset the components:

{: .warning }
> `wum diagnose --fix` is **destructive**. It stops WU services, renames `SoftwareDistribution` and `catroot2` to `.bak` (discarding pending downloads), re-registers WU DLLs, resets WinSock/WinHTTP proxy, and restarts services. **A reboot is advised afterward.**

```console
wum diagnose --fix         # confirms first
wum diagnose --fix --force # skip confirmation
# reboot, then:
wum diagnose               # re-check health
```

Next: [Development →](development.md)
