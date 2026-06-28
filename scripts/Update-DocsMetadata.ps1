param(
    [string]$ProjectPath = "src/WUM.CLI/WUM.CLI.csproj",
    [string]$OutputPath = "docs/_data/project.yml"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-MsBuildProperty {
    param(
        [xml]$Project,
        [string]$Name
    )

    foreach ($propertyGroup in $Project.Project.PropertyGroup) {
        $value = $propertyGroup.$Name
        if ($null -ne $value -and -not [string]::IsNullOrWhiteSpace([string]$value)) {
            return [string]$value
        }
    }

    return ""
}

function ConvertTo-YamlString {
    param([string]$Value)

    return "'" + $Value.Replace("'", "''") + "'"
}

$resolvedProjectPath = Resolve-Path -LiteralPath $ProjectPath
[xml]$project = Get-Content -LiteralPath $resolvedProjectPath -Raw

$version = Get-MsBuildProperty -Project $project -Name "Version"
$targetFramework = Get-MsBuildProperty -Project $project -Name "TargetFramework"
$runtimeIdentifier = Get-MsBuildProperty -Project $project -Name "RuntimeIdentifier"
$license = Get-MsBuildProperty -Project $project -Name "PackageLicenseExpression"

if ([string]::IsNullOrWhiteSpace($license)) {
    $license = "MIT"
}

foreach ($pair in @{
    Version = $version
    TargetFramework = $targetFramework
    RuntimeIdentifier = $runtimeIdentifier
    License = $license
}.GetEnumerator()) {
    if ([string]::IsNullOrWhiteSpace($pair.Value)) {
        throw "Missing $($pair.Key) in $ProjectPath"
    }
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$lines = @(
    "# Generated from $ProjectPath. Run scripts/Update-DocsMetadata.ps1 after changing project metadata.",
    "version: $(ConvertTo-YamlString $version)",
    "target_framework: $(ConvertTo-YamlString $targetFramework)",
    "runtime_identifier: $(ConvertTo-YamlString $runtimeIdentifier)",
    "license: $(ConvertTo-YamlString $license)"
)

Set-Content -LiteralPath $OutputPath -Value $lines -Encoding UTF8
