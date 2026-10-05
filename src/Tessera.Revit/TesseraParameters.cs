using System.Globalization;
using Autodesk.Revit.DB;
using Tessera.Core;

namespace Tessera.Revit;

internal sealed class TesseraParameterValue
{
    public required string Name { get; init; }
    public string? Text { get; init; }
    public double? Si { get; init; }
    public string? IfcType { get; init; }
}

internal static class TesseraParameters
{
    private const int MaxParameters = 48;

    private static readonly string[] LevelParameterNames =
    {
        "Level",
        "Reference Level",
        "Base Level",
        "Base Constraint",
        "Schedule Level"
    };

    public static void Collect(Element element, ICollection<TesseraParameterValue> target)
    {
        Add(target, TesseraKeys.RevitUniqueId, element.UniqueId);
        Add(target, TesseraKeys.RevitElementId, element.Id.Value.ToString(CultureInfo.InvariantCulture));
        if (element.Category != null)
        {
            Add(target, TesseraKeys.RevitCategory, element.Category.Name);
        }

        if (element is FamilyInstance instance && instance.Symbol != null)
        {
            Add(target, TesseraKeys.TypeName, instance.Symbol.Name);
            AddParameters(instance.Symbol, target, "Type.");
        }

        AddParameters(element, target, string.Empty);
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
        var added = target.Count;
        foreach (Parameter parameter in element.Parameters)
        {
            if (target.Count - added >= MaxParameters || parameter == null || !parameter.HasValue)
            {
                continue;
            }

            var name = prefix + parameter.Definition?.Name;
            if (string.IsNullOrWhiteSpace(name) || name == prefix + "Type Id" || target.Any(item => item.Name == name))
            {
                continue;
            }

            if (TryMeasure(parameter, out var si, out var ifcType))
            {
                target.Add(new TesseraParameterValue { Name = name, Si = si, IfcType = ifcType });
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

            Add(target, name, value);
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

    private static void Add(ICollection<TesseraParameterValue> target, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value) || target.Any(item => item.Name == key))
        {
            return;
        }

        target.Add(new TesseraParameterValue { Name = key, Text = value });
    }
}
