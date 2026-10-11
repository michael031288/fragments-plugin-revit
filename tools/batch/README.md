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
| `shared_coordinates` | `false` | Bake shared coordinates into the geometry (project coordinates otherwise; the survey point is stored either way) |
| `view_name` | empty | 3D view to export. Empty means `{3D}`, else the first 3D view, else a temporary isometric view |
| `timeout_minutes_per_model` | `30` | Give up on Revit after this long per model |

## What happens to your models
Nothing. Workshared files open detached, Revit's dialogs and warnings are dismissed, and every model closes without saving. A model that fails is logged and skipped while the rest still convert. An existing `.tsra` in `output\` is only replaced once the new one is complete.

Logs: `output\_export_log.txt` and `output\_last_run.json`.

If Revit asks about an unsigned add-in the first time, choose **Always Load**.
