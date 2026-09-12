param(
    [int[]]$RevitYears = @(2024, 2025, 2026)
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$installerDir = Join-Path $root "src\Fragments.Revit.Installer"
$payloadDir = Join-Path $installerDir "Payload"
$zipPath = Join-Path $installerDir "payload.zip"
$revitProject = Join-Path $root "src\Fragments.Revit\Fragments.Revit.csproj"
$artifacts = Join-Path $root "artifacts"

if (Test-Path $payloadDir) { Remove-Item $payloadDir -Recurse -Force }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
New-Item -ItemType Directory -Force -Path $payloadDir, $artifacts | Out-Null

$bundled = @()
foreach ($year in $RevitYears) {
    Write-Host "Building Revit $year add-in..."
    $build = dotnet build $revitProject -c Release -p:RevitVersion=$year --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Skipping Revit $year (build failed). Install the matching .NET Windows targeting pack if you need this year."
        continue
    }

    $output = Join-Path $root "src\Fragments.Revit\bin\Release\$year"
    if (-not (Test-Path $output)) {
        $output = Join-Path $root "src\Fragments.Revit\bin\Release"
    }
    $yearDir = Join-Path $payloadDir $year
    New-Item -ItemType Directory -Force -Path $yearDir | Out-Null

    Get-ChildItem $output -File | Where-Object {
        $_.Name -notlike "Autodesk.*" -and $_.Name -ne "Fragments.Revit.addin" -and ($_.Extension -ne ".pdb") -and ($_.Extension -ne ".xml")
    } | Copy-Item -Destination $yearDir -Force

    if (-not (Test-Path (Join-Path $yearDir "Fragments.Revit.dll"))) {
        Write-Warning "Revit $year output is missing Fragments.Revit.dll; skipped."
        Remove-Item $yearDir -Recurse -Force
        continue
    }

    $bundled += $year
    Write-Host "Bundled Revit $year"
}

if ($bundled.Count -eq 0) {
    throw "No Revit add-in years were bundled."
}

Compress-Archive -Path (Join-Path $payloadDir "*") -DestinationPath $zipPath -Force

Write-Host "Publishing per-user setup EXE..."
dotnet publish $installerDir -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $artifacts "installer") --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Installer publish failed."
}

$exe = Join-Path $artifacts "installer\Fragments.Revit.Setup.exe"
Write-Host "Setup EXE: $exe"
Write-Host "Bundled years: $($bundled -join ', ')"
Write-Host "This installer writes to the user's AppData folder (no admin / no Autodesk login)."
