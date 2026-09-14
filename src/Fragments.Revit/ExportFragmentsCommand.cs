using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FormSaveFileDialog = System.Windows.Forms.SaveFileDialog;

namespace Fragments.Revit;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ExportFragmentsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiDoc = commandData.Application.ActiveUIDocument;
        if (uiDoc == null)
        {
            message = "Open a Revit document first.";
            return Result.Failed;
        }

        var view = uiDoc.ActiveView as View3D ?? Find3DView(uiDoc.Document);
        if (view == null)
        {
            Autodesk.Revit.UI.TaskDialog.Show("Fragments", "Open a 3D view, then run Export .frag.");
            return Result.Cancelled;
        }

        using var dialog = new FormSaveFileDialog
        {
            Title = "Export Fragments",
            Filter = "Fragments (*.frag)|*.frag",
            FileName = SanitizeFileName(uiDoc.Document.Title) + ".frag",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return Result.Cancelled;
        }

        try
        {
            var summary = RevitFragmentExporter.Export(uiDoc.Document, view, dialog.FileName);
            Autodesk.Revit.UI.TaskDialog.Show(
                "Fragments",
                $"Exported {summary.ElementCount} elements, {summary.TriangleCount} triangles\n{dialog.FileName}\n{summary.FileBytes:N0} bytes");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            Autodesk.Revit.UI.TaskDialog.Show("Fragments export failed", ex.ToString());
            return Result.Failed;
        }
    }

    private static View3D? Find3DView(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .FirstOrDefault(v => !v.IsTemplate);
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
