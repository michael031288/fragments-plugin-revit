using Autodesk.Revit.DB;

namespace Fragments.Revit;

internal static class IfcCategoryMap
{
    private static readonly Dictionary<BuiltInCategory, string> Map = new Dictionary<BuiltInCategory, string>
    {
        { BuiltInCategory.OST_Walls, "IFCWALL" },
        { BuiltInCategory.OST_Floors, "IFCSLAB" },
        { BuiltInCategory.OST_Roofs, "IFCROOF" },
        { BuiltInCategory.OST_Ceilings, "IFCCOVERING" },
        { BuiltInCategory.OST_Doors, "IFCDOOR" },
        { BuiltInCategory.OST_Windows, "IFCWINDOW" },
        { BuiltInCategory.OST_Columns, "IFCCOLUMN" },
        { BuiltInCategory.OST_StructuralColumns, "IFCCOLUMN" },
        { BuiltInCategory.OST_StructuralFraming, "IFCBEAM" },
        { BuiltInCategory.OST_StructuralFoundation, "IFCFOOTING" },
        { BuiltInCategory.OST_Stairs, "IFCSTAIR" },
        { BuiltInCategory.OST_StairsRailing, "IFCRAILING" },
        { BuiltInCategory.OST_Ramps, "IFCRAMP" },
        { BuiltInCategory.OST_Furniture, "IFCFURNISHINGELEMENT" },
        { BuiltInCategory.OST_FurnitureSystems, "IFCFURNISHINGELEMENT" },
        { BuiltInCategory.OST_GenericModel, "IFCBUILDINGELEMENTPROXY" },
        { BuiltInCategory.OST_Casework, "IFCFURNISHINGELEMENT" },
        { BuiltInCategory.OST_PlumbingFixtures, "IFCSANITARYTERMINAL" },
        { BuiltInCategory.OST_MechanicalEquipment, "IFCBUILDINGELEMENTPROXY" },
        { BuiltInCategory.OST_ElectricalEquipment, "IFCELECTRICDISTRIBUTIONBOARD" },
        { BuiltInCategory.OST_LightingFixtures, "IFCLIGHTFIXTURE" },
        { BuiltInCategory.OST_DuctCurves, "IFCDUCTSEGMENT" },
        { BuiltInCategory.OST_DuctFitting, "IFCDUCTFITTING" },
        { BuiltInCategory.OST_DuctAccessory, "IFCDUCTFITTING" },
        { BuiltInCategory.OST_PipeCurves, "IFCPIPESEGMENT" },
        { BuiltInCategory.OST_PipeFitting, "IFCPIPEFITTING" },
        { BuiltInCategory.OST_PipeAccessory, "IFCVALVE" },
        { BuiltInCategory.OST_CableTray, "IFCCABLECARRIERSEGMENT" },
        { BuiltInCategory.OST_Conduit, "IFCCABLESEGMENT" },
        { BuiltInCategory.OST_SpecialityEquipment, "IFCBUILDINGELEMENTPROXY" },
        { BuiltInCategory.OST_Site, "IFCSITE" },
        { BuiltInCategory.OST_Topography, "IFCGEOGRAPHICELEMENT" },
        { BuiltInCategory.OST_Planting, "IFCBUILDINGELEMENTPROXY" },
        { BuiltInCategory.OST_Parking, "IFCBUILDINGELEMENTPROXY" },
        { BuiltInCategory.OST_Entourage, "IFCBUILDINGELEMENTPROXY" },
        { BuiltInCategory.OST_Mass, "IFCBUILDINGELEMENTPROXY" },
        { BuiltInCategory.OST_CurtainWallPanels, "IFCMEMBER" },
        { BuiltInCategory.OST_CurtainWallMullions, "IFCMEMBER" },
        { BuiltInCategory.OST_Rooms, "IFCSPACE" },
        { BuiltInCategory.OST_Areas, "IFCSPACE" },
        { BuiltInCategory.OST_Levels, "IFCBUILDINGSTOREY" },
        { BuiltInCategory.OST_Grids, "IFCGRID" },
        { BuiltInCategory.OST_Parts, "IFCBUILDINGELEMENTPART" },
        { BuiltInCategory.OST_Assemblies, "IFCELEMENTASSEMBLY" }
    };

    public static string For(Element element)
    {
        var category = element.Category;
        if (category == null)
        {
            return "IFCBUILDINGELEMENTPROXY";
        }

        try
        {
            var builtIn = (BuiltInCategory)category.Id.Value;
            if (Map.TryGetValue(builtIn, out var ifc))
            {
                return ifc;
            }
        }
        catch
        {
            // Non-built-in categories fall through.
        }

        return "IFCBUILDINGELEMENTPROXY";
    }
}
