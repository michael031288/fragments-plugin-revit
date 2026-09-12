$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$flatc = Join-Path $root "tools\flatc\flatc.exe"
$schema = Join-Path $root "schema\index.fbs"
$outDir = Join-Path $root "src\Fragments.Core\Generated"

if (-not (Test-Path $flatc)) {
    throw "flatc.exe not found at $flatc. Download FlatBuffers 25.2.10 into tools\flatc."
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
& $flatc --csharp --gen-object-api -o $outDir $schema
Write-Host "Generated C# schema into $outDir"
