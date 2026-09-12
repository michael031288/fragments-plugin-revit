using System.Reflection;
using Autodesk.Revit.UI;

namespace Fragments.Revit;

public sealed class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        var panel = application.CreateRibbonPanel("Fragments");
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var button = new PushButtonData(
            "ExportFragments",
            "Export\n.frag",
            assemblyPath,
            typeof(ExportFragmentsCommand).FullName);

        button.ToolTip = "Export the active 3D view to a That Open Fragments (.frag) file.";
        panel.AddItem(button);
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        return Result.Succeeded;
    }
}
