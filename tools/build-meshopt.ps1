# Builds meshoptimizer.dll (0.25 vertex and index codecs) for the Revit add-in.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "native\meshoptimizer\src"
$out = Join-Path $root "native\meshoptimizer\build"
New-Item -ItemType Directory -Force -Path $out | Out-Null

$dll = Join-Path $out "meshoptimizer.dll"
$vertex = Join-Path $src "vertexcodec.cpp"
$index = Join-Path $src "indexcodec.cpp"
if ((Test-Path $dll) -and
    (Get-Item $dll).LastWriteTimeUtc -ge (Get-Item $vertex).LastWriteTimeUtc -and
    (Get-Item $dll).LastWriteTimeUtc -ge (Get-Item $index).LastWriteTimeUtc) {
    exit 0
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
    throw "vswhere was not found. Install Visual Studio with the Desktop development with C++ workload."
}
$install = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $install) {
    throw "MSVC C++ tools were not found. Tessera needs them to build meshoptimizer.dll."
}
$vcvars = Join-Path $install "VC\Auxiliary\Build\vcvars64.bat"
Push-Location $out
cmd /c "call `"$vcvars`" && cl /nologo /LD /O2 /EHsc /DNDEBUG /DMESHOPTIMIZER_API=__declspec(dllexport) `"$vertex`" `"$index`" /link /OUT:`"$dll`""
$code = $LASTEXITCODE
Pop-Location
if ($code -ne 0) {
    throw "cl failed while building meshoptimizer.dll ($code)."
}
