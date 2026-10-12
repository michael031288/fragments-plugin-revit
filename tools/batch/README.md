# Tessera folder conversion

Drop `.rvt` files into `input\`, double-click `Convert.bat`, collect `.tsra` files from `output\`.

Revit opens in the background, exports each model with the Tessera.Revit exporter (the same one behind the **Export .tsra** button, no IFC in between), and closes itself when it's done.

## Requirements
- Revit 2025 or 2026. Set `revit_year` in `config.json`.
- The Tessera.Revit add-in with batch mode: either installed as usual, or bundled in this kit under `addin\<year>\`. The kit uses your installed add-in if it supports batch mode. If not, it uses the bundled copy for the duration of the run and puts your installed one back afterwards.

## Settings (`config.json`)
| Setting | Default | |
|---|---|---|
| `detail` | `medium` | Mesh detail: `coarse` (Revit 2), `medium` (8), `fine` (15) or `view` (each view's own setting) |
| `coordinates` | `shared` | Where the geometry is measured from: `shared` (shared coordinates, from the Survey Point: the real-world system every discipline's model shares, so models line up when opened together), `project` (the Project Base Point, along project north) or `internal` (Revit's internal origin) |
| `crs` | empty | The map's coordinate system, e.g. `EPSG:27700` for the British National Grid. Empty: the one Revit's location carries, if any |

Whichever you choose, the export also records where the model sits in shared coordinates, the way an IFC export's map conversion does, so Tessera's Point measure shows both x, y, z and eastings / northings / height. `output\_export_log.txt` has a `Placement:` line per model with the model origin's eastings, northings and height: check them against the Survey Point in Revit.
| `view_name` | empty | 3D view to export. Empty means `{3D}`, else the first 3D view, else a temporary isometric view |
| `timeout_minutes_per_model` | `30` | Give up on Revit after this long per model |

## What happens to your models
Nothing. Workshared files open detached, Revit's dialogs and warnings are dismissed, and every model closes without saving. A model that fails is logged and skipped while the rest still convert. An existing `.tsra` in `output\` is only replaced once the new one is complete.

Logs: `output\_export_log.txt` and `output\_last_run.json`.

If Revit asks about an unsigned add-in the first time, choose **Always Load**.
