using System.Globalization;
using Autodesk.Revit.DB;
using Tessera.Core;

namespace Tessera.Revit;

internal sealed class TesseraParameterValue
{
    /// <summary>The property set: <c>PSet_Revit_&lt;group&gt;</c> or <c>PSet_Revit_Type_&lt;group&gt;</c>, as Revit's IFC exporter names them.</summary>
    public required string Set { get; init; }
    public required string Name { get; init; }
    public string? Text { get; init; }
    public double? Si { get; init; }
    public string? IfcType { get; init; }
}

internal static class TesseraParameters
{
    /// <summary>The add-in's own bookkeeping (UniqueId, ElementId, category): not a Revit parameter group.</summary>
    public const string TesseraSet = "PSet_Revit_Tessera";

    private static readonly string[] LevelParameterNames =
    {
        "Level",
        "Reference Level",
        "Base Level",
        "Base Constraint",
        "Schedule Level"
    };

    /// <summary>
    /// An element's own parameters, in <c>PSet_Revit_&lt;group&gt;</c> sets. Type parameters are not
    /// copied here: they go on the type object (<see cref="CollectType"/>), which elements inherit
    /// from in Tessera, as they do in an IFC export.
    /// </summary>
    public static void Collect(Element element, ICollection<TesseraParameterValue> target)
    {
        Add(target, TesseraSet, TesseraKeys.RevitUniqueId, element.UniqueId);
        Add(target, TesseraSet, TesseraKeys.RevitElementId, element.Id.Value.ToString(CultureInfo.InvariantCulture));
        if (element.Category != null)
        {
            Add(target, TesseraSet, TesseraKeys.RevitCategory, element.Category.Name);
        }

        AddParameters(element, target, "PSet_Revit_");
    }

    /// <summary>A type's parameters, in <c>PSet_Revit_Type_&lt;group&gt;</c> sets.</summary>
    public static void CollectType(ElementType type, ICollection<TesseraParameterValue> target)
    {
        Add(target, TesseraSet, TesseraKeys.RevitUniqueId, type.UniqueId);
        Add(target, TesseraSet, TesseraKeys.RevitElementId, type.Id.Value.ToString(CultureInfo.InvariantCulture));
        AddParameters(type, target, "PSet_Revit_Type_");
    }

    /// <summary>The element's type: a family symbol, or a system family's type (walls, floors, roofs, ...).</summary>
    public static ElementType? TypeOf(Element element)
    {
        try
        {
            if (element is FamilyInstance instance && instance.Symbol != null)
            {
                return instance.Symbol;
            }

            return element.Document.GetElement(element.GetTypeId()) as ElementType;
        }
        catch
        {
            return null;
        }
    }

    public static Level? FindLevel(Element element)
    {
        try
        {
            if (element.LevelId != null
                && element.LevelId != ElementId.InvalidElementId
                && element.Document.GetElement(element.LevelId) is Level direct)
            {
                return direct;
            }

            foreach (var name in LevelParameterNames)
            {
                var parameter = element.LookupParameter(name);
                if (parameter == null || parameter.StorageType != StorageType.ElementId)
                {
                    continue;
                }

                var id = parameter.AsElementId();
                if (id != null
                    && id != ElementId.InvalidElementId
                    && element.Document.GetElement(id) is Level level)
                {
                    return level;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static void AddParameters(Element element, ICollection<TesseraParameterValue> target, string setPrefix)
    {
        foreach (Parameter parameter in element.Parameters)
        {
            if (parameter == null || !parameter.HasValue)
            {
                continue;
            }

            var name = parameter.Definition?.Name;
            if (string.IsNullOrWhiteSpace(name) || name == "Type Id")
            {
                continue;
            }

            var set = setPrefix + GroupLabel(parameter);
            if (target.Any(item => item.Set == set && item.Name == name))
            {
                continue;
            }

            if (TryMeasure(parameter, out var si, out var ifcType))
            {
                target.Add(new TesseraParameterValue { Set = set, Name = name, Si = si, IfcType = ifcType });
                continue;
            }

            string? value = null;
            try
            {
                value = parameter.AsValueString();
                if (string.IsNullOrWhiteSpace(value) && parameter.StorageType == StorageType.String)
                {
                    value = parameter.AsString();
                }
            }
            catch
            {
                // Skip parameters Revit refuses to read.
            }

            Add(target, set, name, value);
        }
    }

    /// <summary>The parameter's group as Revit shows it ("Identity Data", "Constraints", ...); "Other" without one.</summary>
    private static string GroupLabel(Parameter parameter)
    {
        try
        {
            var group = parameter.Definition?.GetGroupTypeId();
            if (group != null && !string.IsNullOrEmpty(group.TypeId))
            {
                var label = LabelUtils.GetLabelForGroup(group);
                if (!string.IsNullOrWhiteSpace(label))
                {
                    return label;
                }
            }
        }
        catch
        {
            // Fall through to "Other".
        }

        return "Other";
    }

    private static bool TryMeasure(Parameter parameter, out double si, out string ifcType)
    {
        si = 0;
        ifcType = "";
        try
        {
            if (parameter.StorageType != StorageType.Double || parameter.Definition == null)
            {
                return false;
            }

            var typeId = parameter.Definition.GetDataType()?.TypeId;
            if (typeId == SpecTypeId.Length.TypeId)
            {
                si = UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.Meters);
                ifcType = "IFCLENGTHMEASURE";
                return true;
            }

            if (typeId == SpecTypeId.Area.TypeId)
            {
                si = UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.SquareMeters);
                ifcType = "IFCAREAMEASURE";
                return true;
            }

            if (typeId == SpecTypeId.Volume.TypeId)
            {
                si = UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.CubicMeters);
                ifcType = "IFCVOLUMEMEASURE";
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static void Add(ICollection<TesseraParameterValue> target, string set, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value) || target.Any(item => item.Set == set && item.Name == key))
        {
            return;
        }

        target.Add(new TesseraParameterValue { Set = set, Name = key, Text = value });
    }
}
