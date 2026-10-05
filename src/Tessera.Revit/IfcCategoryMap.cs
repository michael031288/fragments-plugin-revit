using Autodesk.Revit.DB;

namespace Tessera.Revit;

internal static class IfcCategoryMap
{
    private static readonly Dictionary<BuiltInCategory, string> Map = new Dictionary<BuiltInCategory, string>
    {
        { BuiltInCategory.OST_Walls, "IfcWall" },
        { BuiltInCategory.OST_Floors, "IfcSlab" },
        { BuiltInCategory.OST_Roofs, "IfcRoof" },
        { BuiltInCategory.OST_Ceilings, "IfcCovering" },
        { BuiltInCategory.OST_Doors, "IfcDoor" },
        { BuiltInCategory.OST_Windows, "IfcWindow" },
        { BuiltInCategory.OST_Columns, "IfcColumn" },
        { BuiltInCategory.OST_StructuralColumns, "IfcColumn" },
        { BuiltInCategory.OST_StructuralFraming, "IfcBeam" },
        { BuiltInCategory.OST_StructuralFoundation, "IfcFooting" },
        { BuiltInCategory.OST_Stairs, "IfcStair" },
        { BuiltInCategory.OST_StairsRailing, "IfcRailing" },
        { BuiltInCategory.OST_Ramps, "IfcRamp" },
        { BuiltInCategory.OST_Furniture, "IfcFurnishingElement" },
        { BuiltInCategory.OST_FurnitureSystems, "IfcFurnishingElement" },
        { BuiltInCategory.OST_GenericModel, "IfcBuildingElementProxy" },
        { BuiltInCategory.OST_Casework, "IfcFurnishingElement" },
        { BuiltInCategory.OST_PlumbingFixtures, "IfcSanitaryTerminal" },
        { BuiltInCategory.OST_MechanicalEquipment, "IfcBuildingElementProxy" },
        { BuiltInCategory.OST_ElectricalEquipment, "IfcElectricDistributionBoard" },
        { BuiltInCategory.OST_LightingFixtures, "IfcLightFixture" },
        { BuiltInCategory.OST_DuctCurves, "IfcDuctSegment" },
        { BuiltInCategory.OST_DuctFitting, "IfcDuctFitting" },
        { BuiltInCategory.OST_DuctAccessory, "IfcDuctFitting" },
        { BuiltInCategory.OST_PipeCurves, "IfcPipeSegment" },
        { BuiltInCategory.OST_PipeFitting, "IfcPipeFitting" },
        { BuiltInCategory.OST_PipeAccessory, "IfcValve" },
        { BuiltInCategory.OST_CableTray, "IfcCableCarrierSegment" },
        { BuiltInCategory.OST_Conduit, "IfcCableSegment" },
        { BuiltInCategory.OST_SpecialityEquipment, "IfcBuildingElementProxy" },
        { BuiltInCategory.OST_Site, "IfcSite" },
        { BuiltInCategory.OST_Topography, "IfcGeographicElement" },
        { BuiltInCategory.OST_Planting, "IfcBuildingElementProxy" },
        { BuiltInCategory.OST_Parking, "IfcBuildingElementProxy" },
        { BuiltInCategory.OST_Entourage, "IfcBuildingElementProxy" },
        { BuiltInCategory.OST_Mass, "IfcBuildingElementProxy" },
        { BuiltInCategory.OST_CurtainWallPanels, "IfcMember" },
        { BuiltInCategory.OST_CurtainWallMullions, "IfcMember" },
        { BuiltInCategory.OST_Rooms, "IfcSpace" },
        { BuiltInCategory.OST_Areas, "IfcSpace" },
        { BuiltInCategory.OST_Levels, "IfcBuildingStorey" },
        { BuiltInCategory.OST_Grids, "IfcGrid" },
        { BuiltInCategory.OST_Parts, "IfcBuildingElementPart" },
        { BuiltInCategory.OST_Assemblies, "IfcElementAssembly" }
    };

    public static string For(Element element)
    {
        var category = element.Category;
        if (category == null)
        {
            return "IfcBuildingElementProxy";
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
            // Non-built-in categories stay proxies. RevitCategory still records the name.
        }

        return "IfcBuildingElementProxy";
    }
}
