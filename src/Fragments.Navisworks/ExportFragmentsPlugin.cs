using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using Fragments.Core;
using NwApp = Autodesk.Navisworks.Api.Application;

namespace Fragments.Navisworks;

[Plugin("FragmentsExporter", "FRAG", DisplayName = "Export Fragments")]
[AddInPlugin(AddInLocation.AddIn)]
public sealed class ExportFragmentsPlugin : AddInPlugin
{
    public override int Execute(params string[] parameters)
    {
        var document = NwApp.ActiveDocument;
        if (document == null)
        {
            MessageBox.Show("Open a Navisworks document first.", "Fragments");
            return 0;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Export Fragments",
            Filter = "Fragments (*.frag)|*.frag",
            FileName = Sanitize(document.Title) + ".frag",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return 0;
        }

        try
        {
            var items = document.CurrentSelection.IsEmpty
                ? CollectAll(document)
                : document.CurrentSelection.SelectedItems.DescendantsAndSelf.ToList();

            var summary = NavisFragmentExporter.Export(document, items, dialog.FileName);
            MessageBox.Show(
                $"Exported {summary.ElementCount} items, {summary.TriangleCount} triangles\n{dialog.FileName}\n{summary.FileBytes:N0} bytes",
                "Fragments");
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Fragments export failed");
            return 1;
        }
    }

    private static List<ModelItem> CollectAll(Document document)
    {
        var items = new List<ModelItem>();
        foreach (Model model in document.Models)
        {
            items.AddRange(model.RootItem.DescendantsAndSelf);
        }

        return items;
    }

    private static string Sanitize(string title)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            title = title.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(title) ? "model" : title;
    }
}
