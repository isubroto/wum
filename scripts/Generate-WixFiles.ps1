param(
    [Parameter(Mandatory = $true)]
    [string] $SourceDir,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [string] $ComponentGroupId = "AppFiles",
    [string] $DirectoryRefId = "INSTALLFOLDER"
)

$ErrorActionPreference = "Stop"

$sourceRoot = (Resolve-Path -LiteralPath $SourceDir).Path
$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($outputFullPath)

if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$sha256 = [System.Security.Cryptography.SHA256]::Create()
$componentIds = New-Object System.Collections.Generic.List[string]

function Get-RelativePath {
    param([string] $Path)
    return [System.IO.Path]::GetRelativePath($script:sourceRoot, $Path).Replace('/', '\')
}

function Test-IsPublishPath {
    param([string] $RelativePath)
    $parts = $RelativePath -split '[\\/]'
    return $parts -contains 'publish'
}

function New-WixId {
    param(
        [string] $Prefix,
        [string] $Path
    )

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Path.ToLowerInvariant())
    $hashBytes = $script:sha256.ComputeHash($bytes)
    $hash = -join ($hashBytes[0..5] | ForEach-Object { $_.ToString('x2') })

    $name = [System.IO.Path]::GetFileNameWithoutExtension($Path)
    if ([string]::IsNullOrWhiteSpace($name)) {
        $name = Split-Path -Path $Path -Leaf
    }

    $safeName = [regex]::Replace($name, '[^A-Za-z0-9_]', '_')
    if ([string]::IsNullOrWhiteSpace($safeName)) {
        $safeName = "item"
    }
    if ($safeName -notmatch '^[A-Za-z_]') {
        $safeName = "_$safeName"
    }
    if ($safeName.Length -gt 32) {
        $safeName = $safeName.Substring(0, 32)
    }

    return "${Prefix}_${safeName}_${hash}"
}

function Write-FileComponent {
    param([System.IO.FileInfo] $File)

    $relativePath = Get-RelativePath -Path $File.FullName
    if (Test-IsPublishPath -RelativePath $relativePath) {
        return
    }

    $componentId = New-WixId -Prefix "cmp" -Path $relativePath
    $fileId = New-WixId -Prefix "fil" -Path $relativePath
    $source = "`$(var.SourceDir)\$relativePath"

    $script:writer.WriteStartElement("Component")
    $script:writer.WriteAttributeString("Id", $componentId)
    $script:writer.WriteAttributeString("Guid", "*")

    $script:writer.WriteStartElement("File")
    $script:writer.WriteAttributeString("Id", $fileId)
    $script:writer.WriteAttributeString("Source", $source)
    $script:writer.WriteAttributeString("KeyPath", "yes")
    $script:writer.WriteEndElement()

    $script:writer.WriteEndElement()
    $script:componentIds.Add($componentId)
}

function Write-DirectoryTree {
    param([System.IO.DirectoryInfo] $Directory)

    $relativePath = Get-RelativePath -Path $Directory.FullName
    if (Test-IsPublishPath -RelativePath $relativePath) {
        return
    }

    $directoryId = New-WixId -Prefix "dir" -Path $relativePath

    $script:writer.WriteStartElement("Directory")
    $script:writer.WriteAttributeString("Id", $directoryId)
    $script:writer.WriteAttributeString("Name", $Directory.Name)

    Get-ChildItem -LiteralPath $Directory.FullName -File |
        Sort-Object Name |
        ForEach-Object { Write-FileComponent -File $_ }

    Get-ChildItem -LiteralPath $Directory.FullName -Directory |
        Sort-Object Name |
        ForEach-Object { Write-DirectoryTree -Directory $_ }

    $script:writer.WriteEndElement()
}

$files = Get-ChildItem -LiteralPath $sourceRoot -File -Recurse |
    Where-Object { -not (Test-IsPublishPath -RelativePath (Get-RelativePath -Path $_.FullName)) }

if (-not $files) {
    throw "No files found to harvest from '$sourceRoot'."
}

$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)

$writer = [System.Xml.XmlWriter]::Create($outputFullPath, $settings)
try {
    $writer.WriteStartDocument()
    $writer.WriteStartElement($null, "Wix", "http://wixtoolset.org/schemas/v4/wxs")

    $writer.WriteStartElement("Fragment")
    $writer.WriteStartElement("DirectoryRef")
    $writer.WriteAttributeString("Id", $DirectoryRefId)

    Get-ChildItem -LiteralPath $sourceRoot -File |
        Sort-Object Name |
        ForEach-Object { Write-FileComponent -File $_ }

    Get-ChildItem -LiteralPath $sourceRoot -Directory |
        Sort-Object Name |
        ForEach-Object { Write-DirectoryTree -Directory $_ }

    $writer.WriteEndElement()
    $writer.WriteEndElement()

    $writer.WriteStartElement("Fragment")
    $writer.WriteStartElement("ComponentGroup")
    $writer.WriteAttributeString("Id", $ComponentGroupId)

    foreach ($componentId in $componentIds) {
        $writer.WriteStartElement("ComponentRef")
        $writer.WriteAttributeString("Id", $componentId)
        $writer.WriteEndElement()
    }

    $writer.WriteEndElement()
    $writer.WriteEndElement()

    $writer.WriteEndElement()
    $writer.WriteEndDocument()
}
finally {
    $writer.Dispose()
    $sha256.Dispose()
}

Write-Host "Generated $OutputPath from $($componentIds.Count) files."
