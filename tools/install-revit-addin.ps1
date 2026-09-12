param(
    [int]$RevitYear = 2024,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root "src\Fragments.Revit\bin\$Configuration\$RevitYear"

if (-not (Test-Path $source)) {
    # Check fallback without year subfolder just in case
    $fallback = Join-Path $root "src\Fragments.Revit\bin\$Configuration"
    if (Test-Path (Join-Path $fallback "Fragments.Revit.dll")) {
        $source = $fallback
    } else {
        throw "Build the Revit project first: dotnet build src\Fragments.Revit\Fragments.Revit.csproj -c $Configuration -p:RevitVersion=$RevitYear"
    }
}

$addinsRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitYear"
$pluginDir = Join-Path $addinsRoot "Fragments.Revit"

New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null

# Copy assemblies to the Fragments.Revit subfolder
$filesToCopy = Get-ChildItem $source -File | Where-Object {
    $_.Name -notlike "Autodesk.*" -and $_.Name -ne "Fragments.Revit.addin" -and ($_.Extension -ne ".pdb") -and ($_.Extension -ne ".xml")
}

foreach ($file in $filesToCopy) {
    Copy-Item $file.FullName $pluginDir -Force
    Unblock-File -Path (Join-Path $pluginDir $file.Name) -ErrorAction SilentlyContinue
}

# Write the .addin manifest to the root of the year addins folder
$addinXml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Fragments Exporter</Name>
    <Description>Fragments Exporter for Revit</Description>
    <Assembly>Fragments.Revit\Fragments.Revit.dll</Assembly>
    <FullClassName>Fragments.Revit.App</FullClassName>
    <ClientId>a7e5c4b1-9d2f-4c8a-b1e3-0f1a9c000001</ClientId>
    <VendorId>FRAG</VendorId>
    <VendorDescription>That Open Fragments exporter for Revit</VendorDescription>
  </AddIn>
"@

if ($RevitYear -ge 2026) {
    $addinXml += @"

  <ManifestSettings>
    <UseRevitContext>False</UseRevitContext>
    <ContextName>FragmentsExporter</ContextName>
  </ManifestSettings>
"@
}

$addinXml += "`n</RevitAddIns>`n"

$addinPath = Join-Path $addinsRoot "Fragments.Revit.addin"
[System.IO.File]::WriteAllText($addinPath, $addinXml, [System.Text.Encoding]::UTF8)

Write-Host "Installed Fragments exporter for Revit $RevitYear to $addinsRoot"
Write-Host "  Manifest: $addinPath"
Write-Host "  DLLs:     $pluginDir"
