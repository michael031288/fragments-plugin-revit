# Tessera folder conversion: every .rvt in .\input becomes a .tsra in .\output.
# Uses your installed Revit and the Tessera.Revit add-in's own exporter (no IFC step).
# Revit runs in the background, exports each model, and closes itself when done.
$ErrorActionPreference = "Stop"
$Here   = $PSScriptRoot
$InDir  = Join-Path $Here "input"
$OutDir = Join-Path $Here "output"
New-Item -ItemType Directory -Force -Path $InDir, $OutDir | Out-Null

$Cfg  = Get-Content (Join-Path $Here "config.json") -Raw | ConvertFrom-Json
$Year = [string]$Cfg.revit_year
$TimeoutMin = if ($Cfg.timeout_minutes_per_model) { [int]$Cfg.timeout_minutes_per_model } else { 30 }
$ClientId = "c4e8a1d2-6b3f-4e90-9c17-2a5f8d000202"   # Tessera.Revit's add-in id

# ---------------------------------------------------------------- Revit
function Find-Revit($year) {
    $default = "C:\Program Files\Autodesk\Revit $year\Revit.exe"
    if (Test-Path $default) { return $default }
    $keys = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
            "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"
    foreach ($k in Get-ItemProperty $keys -ErrorAction SilentlyContinue) {
        if ($k.DisplayName -eq "Revit $year" -and $k.InstallLocation) {
            $exe = Join-Path $k.InstallLocation "Revit.exe"
            if (Test-Path $exe) { return $exe }
        }
    }
    return $null
}
$RevitExe = Find-Revit $Year
if (-not $RevitExe) { Write-Host "Revit $Year not found. Set revit_year in config.json." -ForegroundColor Red; exit 1 }

# ---------------------------------------------------------------- add-in
# A Tessera.Revit.dll that knows batch mode contains the job variable's name.
function Test-BatchSupport($dll) {
    if (-not (Test-Path $dll)) { return $false }
    # String literals are UTF-16 in the assembly and may start at an odd offset: check both alignments.
    $bytes = [IO.File]::ReadAllBytes($dll)
    $even = [Text.Encoding]::Unicode.GetString($bytes)
    $odd  = [Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1)
    return $even.Contains("TESSERA_BATCH_JOB") -or $odd.Contains("TESSERA_BATCH_JOB")
}

$AddinDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$Year"
$Installed = Join-Path $AddinDir "Tessera.Revit.addin"
$InstalledDll = Join-Path $AddinDir "Tessera.Revit\Tessera.Revit.dll"
$BundledDll = Join-Path $Here "addin\$Year\Tessera.Revit\Tessera.Revit.dll"

$TempManifest = $null      # manifest this run adds, removed afterwards
$Parked = $null            # installed manifest set aside for this run, restored afterwards
if ((Test-Path $Installed) -and (Test-BatchSupport $InstalledDll)) {
    Write-Host "Using the installed Tessera add-in." -ForegroundColor DarkGray
} elseif (Test-BatchSupport $BundledDll) {
    Unblock-File (Join-Path (Split-Path $BundledDll) "*") -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $AddinDir | Out-Null
    if (Test-Path $Installed) {
        # An older Tessera add-in is installed: set it aside so Revit doesn't load two with one id.
        $Parked = "$Installed.batch-off"
        Move-Item -Force $Installed $Parked
        Write-Host "Your installed Tessera add-in predates batch mode; using the bundled one for this run." -ForegroundColor Yellow
    }
    $TempManifest = Join-Path $AddinDir "Tessera.Revit.Batch.addin"
    $settings = if ([int]$Year -ge 2026) { "`n  <ManifestSettings>`n    <UseRevitContext>False</UseRevitContext>`n    <ContextName>TesseraExporter</ContextName>`n  </ManifestSettings>" } else { "" }
    @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Tessera Exporter</Name>
    <Assembly>$BundledDll</Assembly>
    <FullClassName>Tessera.Revit.App</FullClassName>
    <ClientId>$ClientId</ClientId>
    <VendorId>TESS</VendorId>
    <VendorDescription>Tessera model exporter for Revit</VendorDescription>
  </AddIn>$settings
</RevitAddIns>
"@ | Set-Content -Path $TempManifest -Encoding UTF8
} else {
    Write-Host "No Tessera.Revit add-in with batch support found for Revit $Year." -ForegroundColor Red
    Write-Host "Install the latest Tessera.Revit-$Year.zip, or put its contents in addin\$Year\." -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------- models
$Models = @(Get-ChildItem $InDir -Filter *.rvt -File | Where-Object { $_.Name -notmatch '\.\d{4}\.rvt$' })
if ($Models.Count -eq 0) {
    if ($Parked) { Move-Item -Force $Parked $Installed }
    if ($TempManifest) { Remove-Item $TempManifest -ErrorAction SilentlyContinue }
    Write-Host "No .rvt files found in $InDir" -ForegroundColor Yellow
    exit 0
}

$Job = Join-Path $OutDir "_job.json"
[ordered]@{
    out_dir            = $OutDir
    models             = @($Models.FullName)
    detail             = [string]$Cfg.detail
    shared_coordinates = [bool]$Cfg.shared_coordinates
    view_name          = [string]$Cfg.view_name
} | ConvertTo-Json -Depth 4 | Set-Content -Path $Job -Encoding UTF8
$Summary = Join-Path $OutDir "_last_run.json"
Remove-Item $Summary -ErrorAction SilentlyContinue

# ---------------------------------------------------------------- run
Write-Host "Exporting $($Models.Count) model(s) to .tsra with Revit $Year ($($Cfg.detail) detail)..." -ForegroundColor Cyan
Write-Host "Revit runs in the background and closes itself when finished." -ForegroundColor DarkGray
$proc = $null
try {
    $env:TESSERA_BATCH_JOB = $Job
    $proc = Start-Process -FilePath $RevitExe -ArgumentList "/nosplash" -PassThru
    $deadline = (Get-Date).AddMinutes(5 + $TimeoutMin * $Models.Count)
    while (-not (Test-Path $Summary) -and -not $proc.HasExited -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 3
    }
    if (-not (Test-Path $Summary) -and -not $proc.HasExited) { Write-Host "Timed out waiting for Revit." -ForegroundColor Red }
}
finally {
    Remove-Item Env:\TESSERA_BATCH_JOB -ErrorAction SilentlyContinue
    if ($proc -and -not $proc.HasExited) {
        Start-Sleep -Seconds 2
        $null = $proc.CloseMainWindow()
        if (-not $proc.WaitForExit(60000)) { Stop-Process -Id $proc.Id -Force }
    }
    if ($TempManifest) { Remove-Item $TempManifest -ErrorAction SilentlyContinue }
    if ($Parked) { Move-Item -Force $Parked $Installed }
}

# ---------------------------------------------------------------- report
if (-not (Test-Path $Summary)) {
    Write-Host "Revit closed without finishing. See output\_export_log.txt" -ForegroundColor Red
    exit 1
}
$ok = 0
foreach ($r in (Get-Content $Summary -Raw | ConvertFrom-Json)) {
    $name = if ($r.model) { Split-Path $r.model -Leaf } else { "(run)" }
    if ($r.status -eq "ok") {
        $ok++
        Write-Host ("  OK      {0} -> {1} ({2:N0} elements, {3:N1} MB)" -f $name, (Split-Path $r.tsra -Leaf), $r.elements, ($r.bytes / 1MB)) -ForegroundColor Green
    } else {
        Write-Host "  FAILED  $name : $($r.error)" -ForegroundColor Red
    }
}
Write-Host "$ok of $($Models.Count) converted to .tsra in $OutDir"
