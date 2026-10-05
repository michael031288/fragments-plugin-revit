using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FormSaveFileDialog = System.Windows.Forms.SaveFileDialog;

namespace Tessera.Revit;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ExportTesseraCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiDoc = commandData.Application.ActiveUIDocument;
        if (uiDoc == null)
        {
            message = "Open a Revit document first.";
            return Result.Failed;
        }

        if (uiDoc.ActiveView is not View3D view || view.IsTemplate)
        {
            Autodesk.Revit.UI.TaskDialog.Show("Tessera", "Open a 3D view, then run Export .tsra.");
            return Result.Cancelled;
        }

        using var options = new ExportTesseraForm();
        if (options.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return Result.Cancelled;
        }

        using var dialog = new FormSaveFileDialog
        {
            Title = "Export Tessera model",
            Filter = "Tessera model (*.tsra)|*.tsra",
            FileName = SanitizeFileName(uiDoc.Document.Title) + ".tsra",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return Result.Cancelled;
        }

        try
        {
            var summary = TesseraRevitExporter.Export(uiDoc.Document, view, dialog.FileName, options.Settings);
            Autodesk.Revit.UI.TaskDialog.Show(
                "Tessera",
                $"Exported {summary.ElementCount} elements, {summary.TriangleCount} triangles\n{summary.FileBytes:N0} bytes\n\n{summary.File}");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            Autodesk.Revit.UI.TaskDialog.Show("Tessera export failed", ex.ToString());
            return Result.Failed;
        }
    }

    private static string SanitizeFileName(string title)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            title = title.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(title) ? "model" : title;
    }
}
