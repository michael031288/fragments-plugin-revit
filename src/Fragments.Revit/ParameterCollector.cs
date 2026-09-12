using Autodesk.Revit.DB;
using Fragments.Core;

namespace Fragments.Revit;

internal static class ParameterCollector
{
    public static List<FragmentAttribute> Collect(Element element)
    {
        var attributes = new List<FragmentAttribute>
        {
            new FragmentAttribute("Name", element.Name ?? string.Empty, "IFCLABEL"),
            new FragmentAttribute("ElementId", element.Id.Value, "IFCINTEGER"),
            new FragmentAttribute("UniqueId", element.UniqueId, "IFCLABEL")
        };

        if (element.Category != null)
        {
            attributes.Add(new FragmentAttribute("RevitCategory", element.Category.Name, "IFCLABEL"));
        }

        AddParameters(element, attributes);
        if (element is FamilyInstance instance && instance.Symbol != null)
        {
            attributes.Add(new FragmentAttribute("TypeName", instance.Symbol.Name, "IFCLABEL"));
            AddParameters(instance.Symbol, attributes, "Type.");
        }

        return attributes;
    }

    private static void AddParameters(Element element, List<FragmentAttribute> attributes, string prefix = "")
    {
        foreach (Parameter parameter in element.Parameters)
        {
            if (parameter == null || !parameter.HasValue)
            {
                continue;
            }

            var name = prefix + parameter.Definition?.Name;
            if (string.IsNullOrWhiteSpace(name) || name == prefix + "Type Id")
            {
                continue;
            }

            try
            {
                switch (parameter.StorageType)
                {
                    case StorageType.String:
                        var text = parameter.AsString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            attributes.Add(new FragmentAttribute(name, text, "IFCLABEL"));
                        }

                        break;
                    case StorageType.Integer:
                        attributes.Add(new FragmentAttribute(name, parameter.AsInteger(), "IFCINTEGER"));
                        break;
                    case StorageType.Double:
                        attributes.Add(new FragmentAttribute(name, parameter.AsDouble(), "IFCREAL"));
                        break;
                    case StorageType.ElementId:
                        var id = parameter.AsElementId();
                        if (id != null && id != ElementId.InvalidElementId)
                        {
                            attributes.Add(new FragmentAttribute(name, id.Value, "IFCINTEGER"));
                        }

                        break;
                }
            }
            catch
            {
                // Skip parameters Revit refuses to read.
            }
        }
    }
}
