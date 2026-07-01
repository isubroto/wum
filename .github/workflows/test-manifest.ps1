$ErrorActionPreference = "Stop"

# Mock vars (mirror workflow)
$version = '0.3.0.58'
$repo = "SubrotoSaha/wum"
$repoUrl = "https://github.com/$repo"
$msiUrl = "$repoUrl/releases/download/v$version/wum-$version.msi"
$hash = "ABCDEF0123456789"
$cleanProductCode = "{12345678-1234-1234-1234-123456789012}"

New-Item -ItemType Directory -Force -Path "winget-test" | Out-Null

# NOTE: here-strings below MUST keep exactly 10 leading spaces to mirror the
# workflow run: block indentation, so the (?m)^ {10} dedent applies.
          $versionManifest = @"
          # Created with wum Release Workflow
          # yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.6.0.schema.json

          PackageIdentifier: SubrotoSaha.wum
          PackageVersion: $version
          DefaultLocale: en-US
          ManifestType: version
          ManifestVersion: 1.6.0
          "@
          $versionManifest = ($versionManifest -replace '(?m)^ {10}', '').Trim()

          $installerManifest = @"
          # Created with wum Release Workflow
          # yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.6.0.schema.json

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
          ManifestVersion: 1.6.0
          "@
          $installerManifest = ($installerManifest -replace '(?m)^ {10}', '').Trim()

Set-Content -Path "winget-test/SubrotoSaha.wum.yaml" -Value $versionManifest -Encoding UTF8
Set-Content -Path "winget-test/SubrotoSaha.wum.installer.yaml" -Value $installerManifest -Encoding UTF8

Write-Host "=== version manifest ==="
Get-Content "winget-test/SubrotoSaha.wum.yaml"
Write-Host "=== installer manifest ==="
Get-Content "winget-test/SubrotoSaha.wum.installer.yaml"
