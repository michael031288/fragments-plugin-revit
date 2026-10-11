using System.Globalization;
using Autodesk.Revit.DB;
using Tessera.Core;

namespace Tessera.Revit;

internal sealed class TesseraParameterValue
{
    public required string Set { get; init; }
    public required string Name { get; init; }
    public string? Text { get; init; }
    public double? Si { get; init; }
    public string? IfcType { get; init; }
}

/// <summary>
/// Revit parameters, laid out as Revit's IFC exporter writes them with "Export Revit
/// property sets" on: instance parameters in <c>PSet_Revit_&lt;group&gt;</c>, type parameters
/// in <c>PSet_Revit_Type_&lt;group&gt;</c> on the type object. Tessera's mapping recognises a
/// Revit export by these sets and leaves them out of clean IFC.
/// </summary>
internal static class TesseraParameters
{
    public const string InstancePrefix = "PSet_Revit_";
    public const string TypePrefix = "PSet_Revit_Type_";
    private const string OtherGroup = "Other";

    private static readonly string[] LevelParameterNames =
    {
        "Level",
        "Reference Level",
        "Base Level",
        "Base Constraint",
        "Schedule Level"
    };

    /// <summary>An element's own parameters, plus its Revit identity.</summary>
    public static void Collect(Element element, ICollection<TesseraParameterValue> target)
    {
        var other = InstancePrefix + OtherGroup;
        Add(target, other, TesseraKeys.RevitUniqueId, element.UniqueId);
        Add(target, other, TesseraKeys.RevitElementId, element.Id.Value.ToString(CultureInfo.InvariantCulture));
        if (element.Category != null)
        {
            Add(target, other, TesseraKeys.RevitCategory, element.Category.Name);
        }

        AddParameters(element, target, InstancePrefix);
    }

    /// <summary>A type's parameters, for its type object.</summary>
    public static void CollectType(ElementType type, ICollection<TesseraParameterValue> target)
    {
        Add(target, TypePrefix + OtherGroup, TesseraKeys.RevitUniqueId, type.UniqueId);
        Add(target, TypePrefix + OtherGroup, TesseraKeys.RevitElementId, type.Id.Value.ToString(CultureInfo.InvariantCulture));
        AddParameters(type, target, TypePrefix);
    }

    /// <summary>The element's type (family symbol or system family type), if it has one.</summary>
    public static ElementType? TypeOf(Element element)
    {
        try
        {
            if (element is FamilyInstance instance && instance.Symbol != null)
            {
                return instance.Symbol;
            }

            var id = element.GetTypeId();
            return id == null || id == ElementId.InvalidElementId ? null : element.Document.GetElement(id) as ElementType;
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

    private static void AddParameters(Element element, ICollection<TesseraParameterValue> target, string prefix)
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

            var set = prefix + GroupLabel(parameter);
            if (TryMeasure(parameter, out var si, out var ifcType))
            {
                if (!target.Any(item => item.Set == set && item.Name == name))
                {
                    target.Add(new TesseraParameterValue { Set = set, Name = name, Si = si, IfcType = ifcType });
                }

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

    /// <summary>The parameter's group as Revit shows it ("Identity Data", "Dimensions", ...), "Other" if none.</summary>
    private static string GroupLabel(Parameter parameter)
    {
        try
        {
            var group = parameter.Definition?.GetGroupTypeId();
            if (group == null || string.IsNullOrEmpty(group.TypeId))
            {
                return OtherGroup;
            }

            var label = LabelUtils.GetLabelForGroup(group);
            return string.IsNullOrWhiteSpace(label) ? OtherGroup : label;
        }
        catch
        {
            return OtherGroup;
        }
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
