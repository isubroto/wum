# WUM — WinGet CI/CD Fix Tasks for Claude Code

This document describes every bug found in `.github/workflows/release.yml` and
exactly what needs to be changed. Apply all fixes in order.

---

## Context

`winget install SubrotoSaha.wum` downloads the MSI to the user's temp folder but
then shows:

> "This file does not have an app associated with it for performing this action."

Manual double-click of the same MSI works fine. The cause is that WinGet cannot
read `InstallerType` from the generated manifest YAML (due to Bug 1 below), so it
falls back to `ShellExecute` on the `.msi` file instead of calling `msiexec.exe`.

---

## Bug 1 — YAML Manifests Have Broken Indentation (Root Cause)

### File
`.github/workflows/release.yml`

### What is wrong

The `Create WinGet Manifests` step builds three YAML strings using PowerShell
here-strings. The `run:` block is indented 10 spaces inside the workflow YAML, so
every line inside the `@"..."@` blocks carries 10 spaces of literal whitespace.

`.Trim()` only removes whitespace from the very start and end of the whole string.
The first line ends up at column 0, but every other line stays at column 10.

The generated YAML file actually looks like this:

```
# Created with wum Release Workflow          ← col 0 (Trim ate the leading newline+spaces)
          # yaml-language-server: ...        ← col 10 ← WRONG
          PackageIdentifier: SubrotoSaha.wum ← col 10 ← WRONG
          InstallerType: msi                 ← col 10 ← WRONG
```

WinGet's YAML parser cannot find `InstallerType` in this malformed document, so it
never calls `msiexec.exe` and falls back to ShellExecute → "no associated app".

### What to fix

After each of the three here-string assignments, add a `-replace` that strips
exactly 10 leading spaces from every line using a multiline regex.

Find these three lines (they appear one after another, around line 430):

```powershell
$versionManifest  = $versionManifest  -replace '(?m)^ {10}', ''
```

Wait — those lines do not exist yet. You need to ADD them.

Find the existing assignments:

```powershell
$versionManifest = @"
          ...
          "@.Trim()
```

```powershell
$localeManifest = @"
          ...
          "@.Trim()
```

```powershell
$installerManifest = @"
          ...
          "@.Trim()
```

For each one:
1. Remove the `.Trim()` at the end of the closing `"@`.
2. On the very next line after the `"@`, add the dedent:

```powershell
# version manifest
$versionManifest = @"
          ...
          "@
$versionManifest = ($versionManifest -replace '(?m)^ {10}', '').Trim()

# locale manifest
$localeManifest = @"
          ...
          "@
$localeManifest = ($localeManifest -replace '(?m)^ {10}', '').Trim()

# installer manifest
$installerManifest = @"
          ...
          "@
$installerManifest = ($installerManifest -replace '(?m)^ {10}', '').Trim()
```

The `(?m)` flag makes `^` match the start of every line, not just the string.
The `.Trim()` after the replace cleans up any remaining leading/trailing blank lines.

---

## Bug 2 — `InstallerType` Is Wrong Value and Wrong Location

### File
`.github/workflows/release.yml` → `Create WinGet Manifests` step → `$installerManifest`

### What is wrong

```yaml
InstallerType: msi          # ← wrong value AND wrong location
...
Installers:
  - Architecture: x64
    Scope: machine
```

Two problems:
- Value should be `wix`, not `msi`. This package is built with WiX Toolset v6.
  WinGet treats `wix` and `msi` slightly differently for elevation and repair.
- The field is at the manifest root level. It is safer and more explicit to put it
  inside each installer entry so there is zero ambiguity about which installer it
  applies to.

### What to fix

Remove `InstallerType: msi` from the root level of `$installerManifest`.
Add `InstallerType: wix` inside the `Installers:` block.

Before:
```yaml
          InstallerType: msi
          ExpectedReturnCodes:
            ...
          UpgradeBehavior: install
          Installers:
            - Architecture: x64
              Scope: machine
              InstallerUrl: $msiUrl
```

After:
```yaml
          ExpectedReturnCodes:
            ...
          UpgradeBehavior: install
          Installers:
            - Architecture: x64
              InstallerType: wix
              Scope: machine
              InstallerUrl: $msiUrl
```

---

## Bug 3 — MSI Filename Casing Mismatch

### File
`.github/workflows/release.yml` → `Build MSI Installer` step

### What is wrong

The WiX build outputs:
```
publish\WUM-0.3.0.58.msi        ← capital WUM
```

But the manifest URL and release upload glob both reference:
```
publish\wum-0.3.0.58.msi        ← lowercase wum
```

Windows file system is case-insensitive, so the build and upload steps do not
error. However, GitHub's release asset CDN is case-sensitive (Linux-backed). The
manifest URL pointing to `wum-*.msi` can silently 404 for some clients even though
the file exists as `WUM-*.msi`.

### What to fix

In the `Build MSI Installer` step, change the `-out` argument:

Find:
```powershell
-out "../publish/WUM-$safeVersion.msi"
```

Replace with:
```powershell
-out "../publish/wum-$safeVersion.msi"
```

No other lines need changing — the manifest step and the release upload step
already use the lowercase `wum-` form.

---

## Bug 4 — No Manifest Content Verification Before Submission

### File
`.github/workflows/release.yml` → after `Set-Content` calls in `Create WinGet Manifests` step

### What is wrong

The manifests are written to disk and immediately submitted without any log output
showing their actual content. If the YAML is malformed, there is no way to see it
in the Actions run log.

### What to fix

Add these lines immediately after the three `Set-Content` calls and before
`Write-Host "✅ WinGet manifests created"`:

```powershell
Write-Host ""
Write-Host "=== SubrotoSaha.wum.yaml ==="
Get-Content "winget/SubrotoSaha.wum.yaml"

Write-Host ""
Write-Host "=== SubrotoSaha.wum.locale.en-US.yaml ==="
Get-Content "winget/SubrotoSaha.wum.locale.en-US.yaml"

Write-Host ""
Write-Host "=== SubrotoSaha.wum.installer.yaml ==="
Get-Content "winget/SubrotoSaha.wum.installer.yaml"
Write-Host ""
```

---

## Summary of All Changes

| # | File | Location | Change |
|---|------|----------|--------|
| 1 | `release.yml` | After each `@"..."@` here-string | Add `-replace '(?m)^ {10}', ''` dedent on all 3 manifest strings |
| 2 | `release.yml` | `$installerManifest` here-string | Remove root-level `InstallerType: msi`; add `InstallerType: wix` inside the `Installers:` entry |
| 3 | `release.yml` | `Build MSI Installer` step `-out` arg | Change `WUM-$safeVersion.msi` → `wum-$safeVersion.msi` |
| 4 | `release.yml` | After `Set-Content` calls | Add `Get-Content` dump of all 3 manifest files to the log |

---

## How to Verify After Applying Fixes

1. Trigger the workflow and open the Actions run log.
2. In the `Create WinGet Manifests` step log, the manifest content should now show:
   ```
   === SubrotoSaha.wum.installer.yaml ===
   # Created with wum Release Workflow
   # yaml-language-server: $schema=...
   PackageIdentifier: SubrotoSaha.wum    ← must be at column 0, no leading spaces
   PackageVersion: 0.3.0.58
   ...
   Installers:
     - Architecture: x64
       InstallerType: wix                ← wix, not msi
       Scope: machine
   ```
3. Check the GitHub Release assets page — the MSI should be listed as `wum-*.msi`
   (all lowercase).
4. On a clean Windows machine run `winget install SubrotoSaha.wum` — it should
   silently invoke `msiexec.exe` with no dialogs or error boxes.
