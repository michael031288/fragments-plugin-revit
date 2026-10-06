# Fragments exporters for Revit and Navisworks

Native C# exporters that write [That Open Fragments](https://docs.thatopen.com/fragments/schema) (`.frag`) files directly from Autodesk Revit and Navisworks without an IFC round-trip.

## What you get

- **Fragments.Core** — `netstandard2.0` writer for the FlatBuffers schema (`file_identifier "0001"`), with pako-compatible RFC 1950 zlib compression.
- **Fragments.Revit** — Ribbon button on the **Add-Ins** tab that exports the active 3D view (CustomExporter, view-aware, hierarchy, IFC parameter mapping).
- **Fragments.Navisworks** — Add-in plugin that exports the current selection or the whole federated model.
- **Tessera.Revit** — Revit 2025 and 2026 button that writes a Tessera compiled building (`.tsra`, format 0.6) directly from the active 3D view. No IFC file in between.

Output `.frag` files load directly in That Open viewers via `fragments.load(bytes)` (compressed, default) or `{ raw: true }` for uncompressed buffers. `.tsra` files load in the Tessera viewer.

---

## Tessera exporter for Revit 2025

`Tessera.Revit` writes Tessera format 0.6 straight from the Revit view. The file is the compiled-building container (`TSRA`, minor version 6): paged zstd tables, world-chunk meshlets, and lossless canonical meshes. Geometry stays Z-up, in metres, on a 0.1 mm grid.

- One `.tsra` for the active 3D view, including links
- Entities for the project, site, building, storeys, and elements, with IFC class names (`IfcWall`, `IfcWindow`, …)
- Aggregates and contained-in relations, plus Revit parameters. Lengths, areas, and volumes are stored in SI; other parameters stay text
- Material colors, or the class color when Revit has no material (walls orange, windows red, roofs magenta, stairs green)
- Mesh detail: coarse (LoD 2), medium (LoD 8), fine (LoD 15), or whatever the active view is using
- Project coordinates by default, so the model stays near the origin. Shared coordinates can be baked in. The survey point is stored on the project either way
- Full-detail render chunks and canonical blobs. Level-of-detail proxy meshes are not written

The meshlet and canonical codecs are meshoptimizer 0.25, the same sources Tessera's `meshopt` 0.6.2 crate links. `meshoptimizer.dll` has to sit next to `Tessera.Core.dll`.

### Build

```powershell
dotnet test tests\Tessera.Core.Tests\Tessera.Core.Tests.csproj

dotnet build src\Tessera.Revit\Tessera.Revit.csproj -c Release -p:RevitVersion=2025
dotnet build src\Tessera.Revit\Tessera.Revit.csproj -c Release -p:RevitVersion=2026

.\tools\pack-tessera-addin.ps1
```

`pack-tessera-addin.ps1` writes `artifacts\tessera\Tessera.Revit-2025.zip` and `Tessera.Revit-2026.zip`. The Windows build compiles `meshoptimizer.dll` with MSVC.

### Install

Copy the zip contents into the matching user Addins folder:

- **Revit 2025:** `%APPDATA%\Autodesk\Revit\Addins\2025`
- **Revit 2026:** `%APPDATA%\Autodesk\Revit\Addins\2026`

```text
%APPDATA%\Autodesk\Revit\Addins\<year>\
│
├── Tessera.Revit.addin
└── Tessera.Revit\
    ├── Tessera.Revit.dll
    ├── Tessera.Core.dll
    ├── meshoptimizer.dll
    └── ZstdSharp.dll
```

Unblock the DLLs if Windows marks them as downloaded. Revit 2026's manifest includes `<ManifestSettings>`; the 2025 manifest does not.

Open a 3D view, then use **Add-Ins → Tessera → Export .tsra**. The button does not add its own ribbon tab.

---

## Build

Requires .NET SDK 9+ on the development machine.

### Build and Test Core
```powershell
dotnet test tests\Fragments.Core.Tests\Fragments.Core.Tests.csproj
```

### Build Revit Exporter (by Year)
Revit 2024 targets `.NET Framework 4.8`, while Revit 2025 and 2026 target `.NET 8`. Build outputs are automatically placed in `bin\Release\<year>\`.

```powershell
# Revit 2024
dotnet build src\Fragments.Revit\Fragments.Revit.csproj -c Release -p:RevitVersion=2024

# Revit 2025 (requires .NET 8 Windows desktop targeting pack)
dotnet build src\Fragments.Revit\Fragments.Revit.csproj -c Release -p:RevitVersion=2025

# Revit 2026 (requires .NET 8 Windows desktop targeting pack)
dotnet build src\Fragments.Revit\Fragments.Revit.csproj -c Release -p:RevitVersion=2026
```

### Package all Revit versions into ready-to-copy zips
```powershell
.\tools\pack-revit-addins.ps1
```
This builds each version and generates ready-to-copy folders and zip archives in `artifacts\revit\`:
- `Fragments.Revit-2024.zip`
- `Fragments.Revit-2025.zip`
- `Fragments.Revit-2026.zip`

---

## Install the Revit Add-In

### ⚠️ Important: Version Compatibility
Revit 2024 runs on **.NET Framework 4.8**, whereas Revit 2025 and 2026 run on **.NET 8**. **Do not copy Revit 2024 DLLs into a 2025 or 2026 folder** — Revit will fail to load them due to runtime incompatibility. Always use the build matching your Revit year.

---

### Option A: Manual Copy (Primary — No Admin Rights Needed)

This is the cleanest and most reliable way to install, especially on managed work machines where installer EXEs are restricted.

#### 1. Open your user Addins folder
In Windows File Explorer, navigate to the folder corresponding to your Revit version (paste into the address bar):

- **Revit 2024:** `%APPDATA%\Autodesk\Revit\Addins\2024`
- **Revit 2025:** `%APPDATA%\Autodesk\Revit\Addins\2025`
- **Revit 2026:** `%APPDATA%\Autodesk\Revit\Addins\2026`

*(These expand to `C:\Users\<YourUsername>\AppData\Roaming\Autodesk\Revit\Addins\<year>`)*

#### 2. Copy the files
Extract or copy the four files into the year folder using the **subfolder layout** (same structure used by Speckle and other major add-ins):

```text
%APPDATA%\Autodesk\Revit\Addins\<year>\
│
├── Fragments.Revit.addin
└── Fragments.Revit\
    ├── Fragments.Revit.dll
    ├── Fragments.Core.dll
    └── Google.FlatBuffers.dll
```

> **Tip:** If downloading `Fragments.Revit-<year>.zip` from GitHub Releases, you can extract its contents directly into `%APPDATA%\Autodesk\Revit\Addins\<year>\`.

#### 3. Unblock the DLLs (Windows Security)
When files are downloaded or copied across computers, Windows may block them:
1. Open the `Fragments.Revit` subfolder.
2. Right-click each `.dll` (`Fragments.Revit.dll`, `Fragments.Core.dll`, `Google.FlatBuffers.dll`) → **Properties**.
3. If an **Unblock** checkbox appears at the bottom, check it and click **Apply** / **OK**.

#### 4. The `.addin` Manifest Content
If creating or inspecting `Fragments.Revit.addin` manually, make sure it is saved with **UTF-8 encoding**:

**For Revit 2024 and 2025:**
```xml
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
</RevitAddIns>
```

**For Revit 2026:**
```xml
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
  <ManifestSettings>
    <UseRevitContext>False</UseRevitContext>
    <ContextName>FragmentsExporter</ContextName>
  </ManifestSettings>
</RevitAddIns>
```

#### 5. Launch Revit
1. Start Revit.
2. If Revit displays **"The publisher of this add-in could not be verified"**, click **Always Load**.
3. Open any 3D view.
4. Navigate to the **Add-Ins** ribbon tab. You will see a panel named **Fragments** with the **Export .frag** button *(note: it does not create its own top-level tab)*.

---

### Option B: Automated PowerShell Script

If running on a machine with PowerShell:

```powershell
# Default installs Revit 2024
.\tools\install-revit-addin.ps1 -RevitYear 2024

# For other versions:
.\tools\install-revit-addin.ps1 -RevitYear 2025
.\tools\install-revit-addin.ps1 -RevitYear 2026
```

The script copies the files into `%APPDATA%\Autodesk\Revit\Addins\<year>\`, writes the appropriate `.addin` manifest, and automatically unblocks the DLLs.

---

### Option C: Per-User Setup EXE

On a development machine, you can generate a single setup executable:

```powershell
.\tools\publish-revit-installer.ps1
```
This produces `artifacts\installer\Fragments.Revit.Setup.exe`.
- Runs per-user into `%APPDATA%` without requiring local administrator rights.
- Supports silent switches: `Fragments.Revit.Setup.exe /install /quiet` or `/uninstall /quiet`.

---

## Troubleshooting Checklist

If the add-in button does not appear after starting Revit:

1. **Where to look:** Check the **Add-Ins** ribbon tab for a panel named **Fragments**. It is not a standalone ribbon tab.
2. **File extensions:** In File Explorer, enable **View → File name extensions**. Verify that `Fragments.Revit.addin` ends in `.addin` and is not accidentally named `Fragments.Revit.addin.txt`.
3. **Folder location:** The `.addin` file must be in `Addins\<year>\`, and the three DLLs must be inside `Addins\<year>\Fragments.Revit\`. Do not place files in `AddinsData` or `Program Files`.
4. **Revit Edition:** Add-ins only work on full **Autodesk Revit**. **Revit LT** does not support the Revit API or external add-ins.
5. **No `ManifestSettings` in 2024 / 2025:** Ensure `<ManifestSettings>` is omitted in the `.addin` manifest for Revit 2024 and 2025.
6. **Check the Revit Journal:**
   Revit logs detailed startup information. Close Revit and open the latest journal in:
   ```text
   %LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit <year>\Journals\
   ```
   *(Note: This is under `AppData\Local`, not `AppData\Roaming`)*. Search the file for `Fragments` or `addin` to see the exact message or exception.

---

## Install the Navisworks Add-In

1. Build with Navisworks installed locally:
   ```powershell
   dotnet build src\Fragments.Navisworks\Fragments.Navisworks.csproj -p:NavisworksYear=2024
   ```
2. Create `%APPDATA%\Autodesk\ApplicationPlugins\FragmentsExporter.bundle\`
3. Copy `PackageContents.xml` to the bundle root and `Fragments.Navisworks.dll`, `Fragments.Core.dll`, and `Google.FlatBuffers.dll` into `Contents\`.
4. Restart Navisworks Manage or Simulate.
5. In Navisworks, open **Add-ins → Export Fragments**. An empty selection exports the whole model.

---

## Validate a .frag File

You can validate generated `.frag` files with the bundled Node.js validator:

```powershell
cd tools\validate
npm install
node validate.mjs ..\reference\cube.frag
```

`dotnet test` also automatically outputs reference files:
- `tools/reference/cube.frag` (RFC 1950 zlib compressed)
- `tools/reference/cube.raw.frag` (uncompressed FlatBuffers)

---

## FlatBuffers Schema

The schema is vendored from [@thatopen/fragments](https://github.com/ThatOpen/engine_fragment) 3.4.7 (MIT) in `schema/index.fbs`.

To regenerate the C# classes (requires `flatc 25.2.10`):
```powershell
.\tools\generate-schema.ps1
```

Generated code lives under `src/Fragments.Core/Generated/`. Do not edit generated files manually.
