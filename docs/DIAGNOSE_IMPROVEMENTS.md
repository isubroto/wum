# `wum diagnose` — Improvement Plan

Tracks fixes + new features for the diagnostics command.

Source files:
- `src/WUM.Core/Services/UpdateService.cs` → `DiagnoseAsync()` (PowerShell probe script)
- `src/WUM.CLI/Commands/DiagnoseCommand.cs` → rendering + hints

## Known bugs (current behavior)

1. **False `Network UNREACHABLE`.** `UpdateService.cs:511` uses `Test-Connection` (ICMP ping). Microsoft blocks ICMP on Windows Update hosts, so ping fails even when connectivity is fine. Windows Update traffic is HTTPS/443, not ICMP.
2. **Static, contradictory hints.** `DiagnoseCommand.cs:111-118` prints all four hints unconditionally. `COM Session FAILED -> run as Administrator` shows even when the session is OK and the user is admin.
3. **No managed-environment detection.** `Updates Found: 0` + `Last WU Check: Never` on managed machines (WSUS / Group Policy) looks like a failure but is expected. Not surfaced.

## Tasks

### 1. HTTPS reachability (replace ICMP) — DONE
- Dropped `Test-Connection` ping.
- Probes real WU hosts on TCP 443 via `Test-NetConnection -Port 443`: `sls.update.microsoft.com`, `download.windowsupdate.com`, `ctldl.windowsupdate.com`.
- Reports `Reachable` if any host answers on 443, else `UNREACHABLE`.
- Implemented in `UpdateService.cs` section 2. Core build green.

### 2. Result-driven hints — DONE
- `DiagnoseCommand` now collects each `label: value` into a dictionary during the render loop.
- Static four-hint block removed. Hints emitted only when the matching check actually failed:
  - `Network (WU): UNREACHABLE` → firewall/proxy hint
  - `COM Session/Searcher: FAILED` → run as admin
  - `Search Error` present → WU service may be disabled
  - `WU Service: Stopped/NOT FOUND` → start `wuauserv`
  - `Running Admin: False` → re-run elevated
  - `Updates Found: 0` and no other failure → informational (up to date / managed)
- When zero hints fire → prints `All checks passed`.
- Added `using System.Collections.Generic;`. CLI build green.

### 3. WSUS / policy detection — DONE
- New probe section 8b in `UpdateService.cs` reads `...\WindowsUpdate\WUServer` + `...\AU\UseWUServer`.
- Prints `Update Source : WSUS (<url>)`, `WSUS (policy set, server URL missing)`, or `Microsoft Update (direct)`.
- `DiagnoseCommand` fires a `Update Source: WSUS` hint when managed — explains `0 found` + `Last WU Check: Never` on org machines.
- CLI build green.

### 4. BITS + dosvc service checks — DONE
- Probe section 1 now loops a service map: `wuauserv`, `bits`, `dosvc` (Delivery Optimization), `appidsvc`, `cryptsvc` (the 4 services `Reset-WUComponents` touches + dosvc).
- Each reports `Status` plus `(DISABLED)` when `Win32_Service.StartMode = Disabled` — a disabled service fails silently, so `Stopped` alone is not enough.
- `DiagnoseCommand` hints: WU Service down (Stopped/NOT FOUND/DISABLED), BITS down (downloads fail), Crypto Svc down (signature checks fail).
- CLI build green.

### 5. Which service searched — DONE (diagnose + command parity)
- New probe section 4b in `UpdateService.cs` enumerates `Microsoft.Update.ServiceManager.Services`.
- Prints each `Service : <name> [default] [Microsoft Update]` and a `Microsoft Upd : Registered / NOT registered` line.
- `DiagnoseCommand` hint when Microsoft Update missing.
- Parity: `--microsoft-update` / `--mu` now wired into `SearchCommand` and `InstallCommand` (was list-only), both passing `useMicrosoftUpdate` through to `GetAvailableUpdatesAsync`.
- CLI build green.

## Findings from PSWindowsUpdate (mgajda83) analysis

Module v2.2.x is a **binary module** wrapping the same WUA COM API wum uses (`Microsoft.Update.Session`). Key takeaways:

- **Microsoft Update vs Windows Update.** wum's searcher (`UpdateService.cs:524`) uses the **default Windows Update service only**. PSWindowsUpdate's `-MicrosoftUpdate` registers service `7971f918-a847-4430-9279-4a52d1efe18d` via `IUpdateServiceManager.AddService2`, then searches it — surfacing drivers + other-MS-product updates that plain WU hides. Likely cause of false `0 found`.
- Well-known WUA ServiceIDs: Windows Update `9482F4B4-E343-43B6-B170-9A65BC822C77`, Microsoft Update `7971F918-A847-4430-9279-4A52D1EFE18D`, Store `855E8A7C-ECB4-4CA3-B045-1DFA50104289`, WSUS `3DA21691-E39D-4DA6-8A4B-B43877BCB1B7`.
- **`Reset-WUComponents` recipe** (use for task #6 `--fix`): stop `bits`+`wuauserv`+`appidsvc`+`cryptsvc` → delete `qmgr*.dat` → rename `SoftwareDistribution` + `System32\Catroot2` → reregister WU DLLs (`regsvr32 atl.dll urlmon.dll mshtml.dll ...`) → `netsh winsock reset` + `netsh winhttp reset proxy` → restart services → `wuauclt /resetauthorization /detectnow`.
- **`Invoke-WUJob`** runs update ops under SYSTEM via a scheduled task — dodges WUA non-interactive/remote access limits. Relevant if wum search proves unreliable in some contexts.
- `Get-WULastResults` exposes `LastSearchSuccessDate` / `LastInstallationSuccessDate` — more reliable source for "Last WU Check" than the registry path currently read in section 7.

### NEW. Register + search Microsoft Update in the real search path — DONE (list)
- Confirmed mechanism from kliebenberg/PSWindowsUpdate (readable script source):
  - `ServerSelection`: `2` = Windows Update, `3` = Others (used with `ServiceID`).
  - Microsoft Update = `ServerSelection=3` + `ServiceID=7971f918-a847-4430-9279-4a52d1efe18d`.
  - Register via `Microsoft.Update.ServiceManager.AddService2($id, 2, '')` (flag 2 = asfAllowOnlineRegistration).
  - GUIDs: WU `9482f4b4-...`, MU `7971f918-...`, Store `117cab2d-...`, WSUS `3da21691-...`.
- `IUpdateService.GetAvailableUpdatesAsync` gained `bool useMicrosoftUpdate = false`.
- `UpdateService` injects a service-setup block: registers MU if absent, sets `ServerSelection=3` + `ServiceID`.
- `ListCommand` gained `--microsoft-update` / `--mu` flag, passed through.
- CLI build green. All other callers use named/empty args — unaffected.

### Driver criteria fix — DONE
- Confirmed from kliebenberg/PSWindowsUpdate `Get-WUInstall.ps1` / `Get-WUList.ps1`: default criteria is `IsInstalled=0` + `IsHidden=0`, **Type left open** (only filtered when `-UpdateType` set). So drivers are returned by default.
- wum forced `Type='Software'` in `GetAvailableUpdatesAsync`, silently hiding all driver updates — so `--drivers` could never match. Also `includeHidden` was ignored in the query.
- Fix: criteria now `IsInstalled=0` (+ `AND IsHidden=0` unless `includeHidden`). Type clause removed.
- `ParseCategory` already maps `"driver"` → `UpdateCategory.Driver` (`UpdateService.cs:477`), so `--drivers` now works.
- CLI build green.

**Still open:**
- `GetInstalledUpdatesAsync` (and the search method at line ~92) still hard-code `Type='Software'` — same hidden-driver issue for the installed list. Apply the same fix.
- `--microsoft-update` parity into `SearchCommand` / `InstallCommand` — DONE (see task 5).
- Reboot check uses registry key; optional upgrade to `Microsoft.Update.SystemInfo.RebootRequired` COM (canonical).

### 6. `wum diagnose --fix` (WU component reset) — DONE
- `IUpdateService.ResetComponentsAsync()` + impl in `UpdateService.cs`, mirroring PSWindowsUpdate `Reset-WUComponents`:
  1. Stop `bits`, `wuauserv`, `appidsvc`, `cryptsvc`.
  2. Delete `qmgr*.dat` (BITS queue).
  3. Rename `SoftwareDistribution` + `System32\catroot2` to `.bak` (removes stale `.bak` first).
  4. Reregister WU DLLs via `regsvr32 /s`.
  5. `netsh winsock reset` + `netsh winhttp reset proxy`.
  6. Restart services (reverse order).
  7. `wuauclt /resetauthorization /detectnow` + `UsoClient StartScan`.
- `DiagnoseCommand`: `--fix` flag routes to `RunFixAsync`. Requires admin (`AdminHelper.RequireAdmin`), prints a destructive-action warning, and confirms via `ConsoleRenderer.Confirm` unless `--force` / `-f`. 180s spinner timeout. Step output colorized.
- CLI build green.

**Note:** not yet runtime-tested (needs an elevated Windows session). Logic mirrors the documented recipe; verify on a real machine before release.

### 7. `wum diagnose --json` + exit codes — DONE
- `--json` flag: suppresses the colored report (via new `ShowSpinnerAsync(silent:)` param so the spinner doesn't pollute stdout) and emits a single JSON object `{ checks: {label:value}, exitCode, healthy }`.
- `RunAsync` now returns `int`; handler sets `ctx.ExitCode`, which `parser.InvokeAsync` propagates through `Program` to the process exit code.
- Exit-code **bitmask** (0 = healthy, informational states excluded):
  - `1` ExitNetwork — WU hosts unreachable on 443
  - `2` ExitCom — COM session/searcher failed
  - `4` ExitService — wuauserv/bits/cryptsvc down or disabled
  - `8` ExitSearch — searcher threw
  - `16` ExitNotElevated — not running as admin
  - `255` — diagnostics produced no output
- WSUS / Microsoft-Update-not-registered / `0 updates` are informational and do **not** set bits.
- CLI build green.

### 8. Health score — DONE
- `ComputeHealth` counts failures across the 5 failure classes (network, COM, service, search, elevation) → `(passed, total)`, total = 5.
- Text mode: footer verdict line `Health: N/5 checks passed` — green at 5/5, yellow otherwise.
- JSON mode: payload gains `score` (`"N/5"`), `passed`, `total`.
- Reuses the `ComputeExitCode` bitmask, so score and exit code can never disagree.
- CLI build green.

### 9. WU error-code decode — DONE
- New `WUM.Core.Helpers.WuErrorCodes`: dictionary of common WU/WININET HRESULTs (`0x8024xxxx`, `0x80072xxx`, `0x80070422`, `0x800F0922`, …) → human cause.
- `Decode(text)` regex-extracts the first `0x`-prefixed HRESULT from a raw COM message and returns `"0xCODE — meaning"` (or a generic line for unknown codes, null when none present). `Lookup(code)` for exact match.
- `DiagnoseCommand`: the `Search Error` hint now shows the decoded cause instead of the generic "service may be disabled" line.
- JSON payload gains a `searchError` field (decoded when a code is present, else the raw message, else null).
- CLI build green.

### 10. Proxy honor
- `GetSystemWebProxy` currently only prints the proxy; actually test reachability through it.
