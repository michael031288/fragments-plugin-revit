param(
    [int[]]$RevitYears = @(2024, 2025, 2026),
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$revitProject = Join-Path $root "src\Fragments.Revit\Fragments.Revit.csproj"
$artifacts = Join-Path $root "artifacts\revit"

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

$successfulYears = @()

foreach ($year in $RevitYears) {
    Write-Host "Building Revit $year add-in ($Configuration)..."
    dotnet build $revitProject -c $Configuration -p:RevitVersion=$year --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Skipping Revit ${year}: build failed. (Revit 2025+ requires .NET 8 Windows desktop targeting pack)."
        continue
    }

    $source = Join-Path $root "src\Fragments.Revit\bin\$Configuration\$year"
    if (-not (Test-Path $source)) {
        # Fallback if output path wasn't year-nested
        $source = Join-Path $root "src\Fragments.Revit\bin\$Configuration"
    }

    $packageYearDir = Join-Path $artifacts $year
    $packagePluginDir = Join-Path $packageYearDir "Fragments.Revit"
    if (Test-Path $packageYearDir) { Remove-Item $packageYearDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $packagePluginDir | Out-Null

    # Copy assemblies to package/Fragments.Revit
    $filesToCopy = Get-ChildItem $source -File | Where-Object {
        $_.Name -notlike "Autodesk.*" -and $_.Name -ne "Fragments.Revit.addin" -and ($_.Extension -ne ".pdb") -and ($_.Extension -ne ".xml")
    }
    foreach ($file in $filesToCopy) {
        Copy-Item $file.FullName $packagePluginDir -Force
    }

    # Generate Speckle-style .addin manifest
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

    if ($year -ge 2026) {
        $addinXml += @"

  <ManifestSettings>
    <UseRevitContext>False</UseRevitContext>
    <ContextName>FragmentsExporter</ContextName>
  </ManifestSettings>
"@
    }

    $addinXml += "`n</RevitAddIns>`n"

    $addinPath = Join-Path $packageYearDir "Fragments.Revit.addin"
    [System.IO.File]::WriteAllText($addinPath, $addinXml, [System.Text.Encoding]::UTF8)

    # Create convenient zip file
    $zipPath = Join-Path $artifacts "Fragments.Revit-$year.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $packageYearDir "*") -DestinationPath $zipPath -Force

    $successfulYears += $year
    Write-Host "Created package and zip for Revit $year in $packageYearDir"
}

Write-Host ""
Write-Host "Packaging complete."
Write-Host "Successfully packaged years: $($successfulYears -join ', ')"
Write-Host "Artifacts location: $artifacts"
