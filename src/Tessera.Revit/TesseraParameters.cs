using System.Globalization;
using Autodesk.Revit.DB;
using Tessera.Core;

namespace Tessera.Revit;

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

    public static void Collect(Element element, IDictionary<string, string> target)
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

    private static void AddParameters(Element element, IDictionary<string, string> target, string prefix)
    {
        var added = 0;
        foreach (Parameter parameter in element.Parameters)
        {
            if (added >= MaxParameters || parameter == null || !parameter.HasValue)
            {
                continue;
            }

            var name = prefix + parameter.Definition?.Name;
            if (string.IsNullOrWhiteSpace(name) || name == prefix + "Type Id")
            {
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

            if (Add(target, name, value))
            {
                added++;
            }
        }
    }

    private static bool Add(IDictionary<string, string> target, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value) || target.ContainsKey(key))
        {
            return false;
        }

        target[key] = value;
        return true;
    }
}
