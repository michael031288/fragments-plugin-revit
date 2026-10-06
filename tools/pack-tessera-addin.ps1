param(
    [int[]]$RevitYears = @(2025, 2026),
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$revitProject = Join-Path $root "src\Tessera.Revit\Tessera.Revit.csproj"
$artifacts = Join-Path $root "artifacts\tessera"

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

$successfulYears = @()

foreach ($year in $RevitYears) {
    if ($year -lt 2025) {
        Write-Warning "Skipping Revit ${year}: Tessera.Revit requires Revit 2025 or newer."
        continue
    }

    Write-Host "Building Tessera Revit $year add-in ($Configuration)..."
    dotnet build $revitProject -c $Configuration -p:RevitVersion=$year --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Skipping Revit ${year}: build failed. Revit 2025+ requires the .NET 8 Windows desktop targeting pack."
        continue
    }

    $source = Join-Path $root "src\Tessera.Revit\bin\$Configuration\$year"
    $packageYearDir = Join-Path $artifacts $year
    $packagePluginDir = Join-Path $packageYearDir "Tessera.Revit"
    if (Test-Path $packageYearDir) { Remove-Item $packageYearDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $packagePluginDir | Out-Null

    $filesToCopy = Get-ChildItem $source -File | Where-Object {
        $_.Name -notlike "Autodesk.*" -and
        $_.Name -ne "Tessera.Revit.addin" -and
        $_.Extension -ne ".pdb" -and
        $_.Extension -ne ".xml"
    }
    foreach ($file in $filesToCopy) {
        Copy-Item $file.FullName $packagePluginDir -Force
    }

    $native = Join-Path $packagePluginDir "meshoptimizer.dll"
    if (-not (Test-Path $native)) {
        throw "meshoptimizer.dll was not copied for Revit $year. Tessera cannot encode .tsra geometry without it."
    }

    $blake3 = Join-Path $packagePluginDir "blake3_dotnet.dll"
    if (-not (Test-Path $blake3)) {
        throw "blake3_dotnet.dll was not copied for Revit $year. Tessera cannot hash a .tsra file without it."
    }

    $addinXml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Tessera Exporter</Name>
    <Description>Export the active 3D view to a Tessera compiled building (.tsra).</Description>
    <Assembly>Tessera.Revit\Tessera.Revit.dll</Assembly>
    <FullClassName>Tessera.Revit.App</FullClassName>
    <ClientId>c4e8a1d2-6b3f-4e90-9c17-2a5f8d000202</ClientId>
    <VendorId>TESS</VendorId>
    <VendorDescription>Tessera model exporter for Revit</VendorDescription>
  </AddIn>
"@

    if ($year -ge 2026) {
        $addinXml += @"

  <ManifestSettings>
    <UseRevitContext>False</UseRevitContext>
    <ContextName>TesseraExporter</ContextName>
  </ManifestSettings>
"@
    }

    $addinXml += "`n</RevitAddIns>`n"
    $addinPath = Join-Path $packageYearDir "Tessera.Revit.addin"
    [System.IO.File]::WriteAllText($addinPath, $addinXml, [System.Text.Encoding]::UTF8)

    $zipPath = Join-Path $artifacts "Tessera.Revit-$year.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $packageYearDir "*") -DestinationPath $zipPath -Force

    $successfulYears += $year
    Write-Host "Created package and zip for Revit $year in $packageYearDir"
}

Write-Host ""
Write-Host "Packaging complete."
Write-Host "Successfully packaged years: $($successfulYears -join ', ')"
Write-Host "Artifacts location: $artifacts"
