$ErrorActionPreference = "Stop"

$version = '0.3.0.58'
$repo = "SubrotoSaha/wum"
$repoUrl = "https://github.com/$repo"

$msiName = "wum-$version.msi"
$msiUrl = "$repoUrl/releases/download/v$version/$msiName"
$hash = 'ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789'
$cleanProductCode = '{12345678-1234-1234-1234-123456789012}'
# Create output directory
New-Item -ItemType Directory -Force -Path "winget-test" | Out-Null

# ------------------------------
# Version Manifest
# FIX #3: Escape $schema with backtick
# ------------------------------
$versionManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.12.0.schema.json
# Created with wum Release Workflow

PackageIdentifier: SubrotoSaha.wum
PackageVersion: $version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.12.0
"@
$versionManifest = ($versionManifest -replace '(?m)^ {10}', '').Trim()

# ------------------------------
# Default Locale Manifest
# ------------------------------
$localeManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.12.0.schema.json
# Created with wum Release Workflow

PackageIdentifier: SubrotoSaha.wum
PackageVersion: $version
PackageLocale: en-US
Publisher: Subroto Saha
PublisherUrl: $repoUrl
PublisherSupportUrl: $repoUrl/issues
Author: Subroto Saha
PackageName: wum
PackageUrl: $repoUrl
License: MIT
LicenseUrl: $repoUrl/blob/main/LICENSE
Copyright: Copyright (c) $(Get-Date -Format "yyyy") Subroto Saha
ShortDescription: A powerful Windows temporary files cleaner.
Description: |-
  wum is a professional Windows utility that helps you clean temporary files, system cache, and free up disk space. 
  Features include safe cleaning algorithms, detailed scan reports, and scheduled cleaning options.
Moniker: wum
Tags:
  - cleaner
  - temp-files
  - disk-cleanup
  - system-utility
  - windows
  - maintenance
  - privacy
ReleaseNotes: |
  This release includes improvements and bug fixes.
  For detailed changelog, visit: $repoUrl/releases/tag/v$version
ReleaseNotesUrl: $repoUrl/releases/tag/v$version
ManifestType: defaultLocale
ManifestVersion: 1.12.0
"@
$localeManifest = ($localeManifest -replace '(?m)^ {10}', '').Trim()

# ------------------------------
# Installer Manifest
# FIX #1: Changed successRebootRequired to rebootRequiredToFinish
# ------------------------------
$installerManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.12.0.schema.json
# Created with wum Release Workflow

PackageIdentifier: SubrotoSaha.wum
PackageVersion: $version
Platform:
  - Windows.Desktop
MinimumOSVersion: 10.0.0.0
InstallModes:
  - interactive
  - silent
  - silentWithProgress
ExpectedReturnCodes:
  - InstallerReturnCode: 1602
    ReturnResponse: cancelledByUser
  - InstallerReturnCode: 1618
    ReturnResponse: installInProgress
  - InstallerReturnCode: 1638
    ReturnResponse: alreadyInstalled
  - InstallerReturnCode: 3010
    ReturnResponse: rebootRequiredToFinish
UpgradeBehavior: install
Installers:
  - Architecture: x64
    InstallerType: wix
    Scope: machine
    InstallerUrl: $msiUrl
    InstallerSha256: $hash
    ProductCode: '$cleanProductCode'
    ElevationRequirement: elevationRequired
    InstallerSwitches:
      Silent: /qn /norestart
      SilentWithProgress: /qb! /norestart
ManifestType: installer
ManifestVersion: 1.12.0
"@
$installerManifest = ($installerManifest -replace '(?m)^ {10}', '').Trim()

Set-Content -Path "winget-test/SubrotoSaha.wum.yaml" -Value $versionManifest -Encoding utf8NoBOM
Set-Content -Path "winget-test/SubrotoSaha.wum.locale.en-US.yaml" -Value $localeManifest -Encoding utf8NoBOM
Set-Content -Path "winget-test/SubrotoSaha.wum.installer.yaml" -Value $installerManifest -Encoding utf8NoBOM

Write-Host ""
Write-Host "=== SubrotoSaha.wum.yaml ==="
Get-Content "winget-test/SubrotoSaha.wum.yaml"

Write-Host ""
Write-Host "=== SubrotoSaha.wum.locale.en-US.yaml ==="
Get-Content "winget-test/SubrotoSaha.wum.locale.en-US.yaml"

Write-Host ""
Write-Host "=== SubrotoSaha.wum.installer.yaml ==="
Get-Content "winget-test/SubrotoSaha.wum.installer.yaml"
Write-Host ""

Write-Host "✅ WinGet manifests created"
Write-Host "MSI URL:     $msiUrl"
Write-Host "SHA256:      $hash"
Write-Host "ProductCode: $cleanProductCode"
