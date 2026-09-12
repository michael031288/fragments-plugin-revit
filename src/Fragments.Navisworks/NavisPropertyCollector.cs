using Autodesk.Navisworks.Api;
using Fragments.Core;

namespace Fragments.Navisworks;

internal static class NavisPropertyCollector
{
    public static List<FragmentAttribute> Collect(ModelItem item)
    {
        var attributes = new List<FragmentAttribute>
        {
            new FragmentAttribute("Name", item.DisplayName ?? string.Empty, "IFCLABEL"),
            new FragmentAttribute("Class", item.ClassDisplayName ?? string.Empty, "IFCLABEL")
        };

        foreach (PropertyCategory category in item.PropertyCategories)
        {
            foreach (DataProperty property in category.Properties)
            {
                var value = ToClr(property.Value);
                if (value == null)
                {
                    continue;
                }

                var name = category.DisplayName + "." + property.DisplayName;
                attributes.Add(new FragmentAttribute(name, value, InferType(value)));
            }
        }

        return attributes;
    }

    public static string? FindGuid(ModelItem item)
    {
        foreach (PropertyCategory category in item.PropertyCategories)
        {
            foreach (DataProperty property in category.Properties)
            {
                var display = property.DisplayName ?? string.Empty;
                if (display.IndexOf("guid", StringComparison.OrdinalIgnoreCase) < 0
                    && display.IndexOf("globalid", StringComparison.OrdinalIgnoreCase) < 0
                    && !string.Equals(display, "UniqueId", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = ToClr(property.Value) as string;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        try
        {
            return item.InstanceGuid.ToString();
        }
        catch
        {
            return null;
        }
    }

    public static string CategoryOf(ModelItem item)
    {
        foreach (PropertyCategory category in item.PropertyCategories)
        {
            foreach (DataProperty property in category.Properties)
            {
                var name = property.DisplayName ?? string.Empty;
                if (name.Equals("IfcClass", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("IFC Class", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Element Type", StringComparison.OrdinalIgnoreCase))
                {
                    var value = ToClr(property.Value) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.StartsWith("IFC", StringComparison.OrdinalIgnoreCase)
                            ? value.ToUpperInvariant()
                            : "IFC" + value.ToUpperInvariant();
                    }
                }
            }
        }

        var className = item.ClassName ?? item.ClassDisplayName ?? "IFCBUILDINGELEMENTPROXY";
        if (className.StartsWith("IFC", StringComparison.OrdinalIgnoreCase))
        {
            return className.ToUpperInvariant();
        }

        return "IFCBUILDINGELEMENTPROXY";
    }

    private static string InferType(object value)
    {
        return value switch
        {
            bool => "IFCBOOLEAN",
            sbyte or byte or short or ushort or int or uint or long or ulong => "IFCINTEGER",
            float or double or decimal => "IFCREAL",
            _ => "IFCLABEL"
        };
    }

    private static object? ToClr(VariantData data)
    {
        try
        {
            switch (data.DataType)
            {
                case VariantDataType.Boolean:
                    return data.ToBoolean();
                case VariantDataType.DisplayString:
                case VariantDataType.IdentifierString:
                    var text = data.ToDisplayString();
                    return string.IsNullOrWhiteSpace(text) ? null : text;
                case VariantDataType.Double:
                case VariantDataType.DoubleLength:
                case VariantDataType.DoubleAngle:
                case VariantDataType.DoubleArea:
                case VariantDataType.DoubleVolume:
                    return data.ToDouble();
                case VariantDataType.Int32:
                    return data.ToInt32();
                default:
                    var fallback = data.ToString();
                    return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
            }
        }
        catch
        {
            return null;
        }
    }
}
