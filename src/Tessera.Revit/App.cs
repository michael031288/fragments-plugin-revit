using System.Reflection;
using Autodesk.Revit.UI;

namespace Tessera.Revit;

public sealed class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        var panel = application.CreateRibbonPanel("Tessera");
        var button = new PushButtonData(
            "ExportTessera",
            "Export\n.3dm",
            Assembly.GetExecutingAssembly().Location,
            typeof(ExportTesseraCommand).FullName)
        {
            ToolTip = "Export the active 3D view to a Tessera Rhino model (.3dm) with class and storey layers."
        };

        panel.AddItem(button);
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        return Result.Succeeded;
    }
}
