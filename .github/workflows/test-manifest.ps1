$ErrorActionPreference = "Stop"

# Mock vars (mirror workflow)
$version = '0.3.0.58'
$repo = "SubrotoSaha/wum"
$repoUrl = "https://github.com/$repo"
$msiUrl = "$repoUrl/releases/download/v$version/wum-$version.msi"
$hash = "ABCDEF0123456789"
$cleanProductCode = "{12345678-1234-1234-1234-123456789012}"

New-Item -ItemType Directory -Force -Path "winget-test" | Out-Null

$versionManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.12.0.schema.json
# Created with wum Release Workflow

PackageIdentifier: SubrotoSaha.wum
PackageVersion: $version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.12.0
"@
$versionManifest = $versionManifest.Trim()

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
$installerManifest = $installerManifest.Trim()

Set-Content -Path "winget-test/SubrotoSaha.wum.yaml" -Value $versionManifest -Encoding utf8NoBOM
Set-Content -Path "winget-test/SubrotoSaha.wum.installer.yaml" -Value $installerManifest -Encoding utf8NoBOM

Write-Host "=== version manifest ==="
Get-Content "winget-test/SubrotoSaha.wum.yaml"
Write-Host "=== installer manifest ==="
Get-Content "winget-test/SubrotoSaha.wum.installer.yaml"
