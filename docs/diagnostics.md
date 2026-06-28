---
title: Diagnostics
layout: default
parent: Use WUM
nav_order: 3
description: "The diagnose command: checks, exit-code bitmask, health score, error decoding."
---

# Diagnostics
{: .no_toc }

1. TOC
{:toc}

---

[← Docs index](index.md)

`wum diagnose` runs a comprehensive Windows Update health check, prints a colorized report (or JSON), and returns a **scriptable exit-code bitmask**. `wum diagnose --fix` resets WU components.

```
wum diagnose [--refresh] [--json]
wum diagnose --fix [--force|-f]
```

See [Command Reference → diagnose](commands.md#wum-diagnose) for option semantics.

---

## What it checks

The diagnostic PowerShell script emits `label: value` lines in two blocks.

### Core checks

| Label | What it verifies |
|---|---|
| WU Service / BITS / Delivery Opt / App Identity / Crypto Svc / Update Orch / Modules Inst / MSI Installer | Service `Status` + `StartMode` (a `Disabled` start mode is flagged `(DISABLED)`, not just `Stopped`) |
| Network (WU) | TCP **443** reachability to `sls.update.microsoft.com`, `download.windowsupdate.com`, `ctldl.windowsupdate.com` (ICMP is blocked on MS hosts, so ping would give false negatives) |
| COM Session | `New-Object -ComObject Microsoft.Update.Session` |
| COM Searcher | `CreateUpdateSearcher()` with `Online = $true` |
| Service (registered) / Microsoft Upd | Lists registered update services; flags `[default]` and whether the **Microsoft Update** service (`7971f918-…`) is registered |
| Search Test | A live `Search('IsInstalled=0 AND Type=Software')` to confirm the searcher works |
| Proxy | System web proxy for `windowsupdate.microsoft.com` |
| Last WU Check | `…\Auto Update\Results\Detect\LastSuccessTime` |
| WU AU Options / WU No AutoUpd | AU policy values |
| Update Source | WSUS (with server URL) vs Microsoft Update direct — explains "0 found" on managed PCs |

### Deep checks

| Label | What it verifies |
|---|---|
| Pending Reboot | Update-specific reboot markers (WU / CBS / UpdateExe) — aligned with `IsRebootRequired()` |
| Disk Free | System-drive free space; `(LOW)` under 10 GB |
| SoftDist Cache | Size of `SoftwareDistribution\Download` |
| WU Datastore | Presence/size of `DataStore.edb` (`MISSING` breaks search) |
| Uptime | Time since last boot (long uptime delays reboot finalization) |
| Last Install | `…\Auto Update\Results\Install\LastSuccessTime` |
| Pending Queue | Count of downloaded-but-not-installed updates |
| Running Admin | Whether the process is elevated |
| OS Version | Caption + build |

### Updates Found

After the script, `diagnose` performs the **shared scan** (`GetAvailableUpdatesAsync`, honoring `--refresh`) and injects `Updates Found: N`. Because it reads/writes the same cache, `status` and `list` report the identical number within the 5-minute TTL.

---

## Exit-code bitmask

Exit code `0` = healthy. Failure classes set bits (OR-combined). Informational states (WSUS, Microsoft Update not registered, `0 updates`) are **not** failures and set no bits.

| Bit | Value | Constant | Set when |
|---|---|---|---|
| 0 | `1` | `ExitNetwork` | `Network (WU)` is `UNREACHABLE` |
| 1 | `2` | `ExitCom` | `COM Session` or `COM Searcher` `FAILED` |
| 2 | `4` | `ExitService` | WU Service / BITS down (Stopped/NOT FOUND/DISABLED) or Crypto Svc Stopped/Disabled |
| 3 | `8` | `ExitSearch` | A `Search Error` line is present (searcher threw) |
| 4 | `16` | `ExitNotElevated` | `Running Admin` is `False` |

**Examples**

- `0` → all good
- `16` → not elevated only
- `5` (`1 + 4`) → network unreachable **and** a service down
- `2` → COM failed (usually means "not running as admin")

**Read it in scripts**

```bash
# PowerShell
wum diagnose --json | Out-Null
if ($LASTEXITCODE -band 1) { "Network problem" }
if ($LASTEXITCODE -band 16) { "Re-run as admin" }

# bash (Git Bash)
wum diagnose --json >/dev/null
code=$?
(( code & 4 )) && echo "A WU service is down"
```

---

## Health score

A score over the **5 failure classes** (network, COM, service, search, elevation): `(passed, total)` where `total = 5` and `passed = 5 - (number of bits set)`. The footer prints `Health: passed/5 checks passed` (green if all pass, yellow otherwise).

---

## JSON output

`wum diagnose --json` emits:

```json
{
  "checks": { "WU Service": "Running", "Network (WU)": "Reachable", "...": "..." },
  "updatesFound": 3,
  "score": "5/5",
  "passed": 5,
  "total": 5,
  "searchError": null,
  "exitCode": 0,
  "healthy": true
}
```

`checks` is the full label→value map. `searchError`, when present, is the decoded HRESULT (see below).

---

## Error-code decoding (`WuErrorCodes`)

When a `Search Error` (or download/install failure) carries an HRESULT, WUM decodes the first `0x........` code in the message to a human cause. Selected mappings:

| Code | Meaning |
|---|---|
| `0x80072EE2` | Connection timed out reaching Windows Update (WININET). Check network/proxy |
| `0x80072EE7` | Server name could not be resolved (DNS) |
| `0x80072F8F` | System clock is wrong, breaking TLS — fix date/time |
| `0x80070422` | Windows Update service is disabled — set `wuauserv` to Manual and start it |
| `0x8024002E` | Access blocked by policy (managed / WSUS) |
| `0x8024402C` | Proxy/connection error contacting the update server |
| `0x80240024` | No updates are applicable to this computer |
| `0x8024000C` | No operation required (already up to date) |
| `0x80240034` | An update failed to download |
| `0x800F0922` | Install failed — often insufficient WinRE/recovery-partition space |

The full table lives in `src/WUM.Core/Helpers/WuErrorCodes.cs`. `Decode(text)` returns `"0xCODE — meaning"` (or `"0xCODE — Unrecognized…"` for unknown codes); `Lookup(code)` returns the description for an exact code.

---

## Footer hints

The colorized report ends with **targeted hints only for checks that actually failed** — e.g. network unreachable → check firewall/proxy on 443; COM failed → run as admin; BITS down → updates can't download; pending reboot → run `wum reboot`; low disk → free the system drive; datastore missing → `wum diagnose --fix`; Microsoft Update off → register it for drivers; WSUS-managed → updates controlled by the org server. If nothing failed, it prints "All checks passed".

---

## `--fix` (component reset)

**Destructive — Administrator only.** Mirrors `Reset-WUComponents`:

1. Stop `bits`, `wuauserv`, `appidsvc`, `cryptsvc`
2. Delete `qmgr*.dat` (BITS queue)
3. Rename `SoftwareDistribution` and `System32\catroot2` to `.bak`
4. Reregister 13 WU DLLs (`wuapi.dll`, `wuaueng.dll`, `wups.dll`, …)
5. Reset WinSock + WinHTTP proxy
6. Restart the services
7. Trigger detection (`wuauclt /detectnow`, `UsoClient StartScan`)

**Pending downloads are discarded; a reboot is advised.** Confirm prompt unless `--force`. The scan cache is invalidated afterward. Re-run `wum diagnose` after rebooting to re-check.

Next: [Troubleshooting →](troubleshooting.md)
