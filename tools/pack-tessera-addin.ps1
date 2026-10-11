param(
    [int[]]$RevitYears = @(2025, 2026),
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$revitProject = Join-Path $root "src\Tessera.Revit\Tessera.Revit.csproj"
$artifacts = Join-Path $root "artifacts\tessera"
$batch = Join-Path $artifacts "batch"

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
if (Test-Path $batch) { Remove-Item $batch -Recurse -Force }

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

    # Native libraries from NuGet packages, in case the build left them under runtimes\win-x64\native.
    $nestedNative = Join-Path $source "runtimes\win-x64\native"
    if (Test-Path $nestedNative) {
        Get-ChildItem $nestedNative -File -Filter *.dll | Copy-Item -Destination $packagePluginDir -Force
    }

    foreach ($required in "meshoptimizer.dll", "blake3_dotnet.dll") {
        if (-not (Test-Path (Join-Path $packagePluginDir $required))) {
            throw "$required was not copied for Revit $year. Tessera cannot write .tsra files without it."
        }
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

    # Folder-conversion kit (tools\batch) carries its own copy of the add-in per year.
    $batchAddin = Join-Path $batch "addin\$year"
    New-Item -ItemType Directory -Force -Path $batchAddin | Out-Null
    Copy-Item $packagePluginDir $batchAddin -Recurse -Force

    $successfulYears += $year
    Write-Host "Created package and zip for Revit $year in $packageYearDir"
}

if ($successfulYears.Count -gt 0) {
    $kit = Join-Path $root "tools\batch"
    foreach ($item in "Convert.bat", "Convert.ps1", "config.json", "README.md") {
        Copy-Item (Join-Path $kit $item) $batch -Force
    }
    foreach ($dir in "input", "output") {
        New-Item -ItemType Directory -Force -Path (Join-Path $batch $dir) | Out-Null
        Set-Content -Path (Join-Path $batch "$dir\.keep") -Value ""
    }
    $batchZip = Join-Path $artifacts ("Tessera.Batch-" + ($successfulYears -join "-") + ".zip")
    if (Test-Path $batchZip) { Remove-Item $batchZip -Force }
    Compress-Archive -Path (Join-Path $batch "*") -DestinationPath $batchZip -Force
    Write-Host "Created folder-conversion kit $batchZip"
}

Write-Host ""
Write-Host "Packaging complete."
Write-Host "Successfully packaged years: $($successfulYears -join ', ')"
Write-Host "Artifacts location: $artifacts"
