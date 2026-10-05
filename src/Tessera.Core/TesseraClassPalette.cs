using System.Drawing;

namespace Tessera.Core;

/// <summary>
/// Stable layer colors so a Tessera model opens already sorted:
/// walls orange, windows red, roofs magenta, stairs green.
/// </summary>
public static class TesseraClassPalette
{
    private static readonly Dictionary<string, Color> Colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
    {
        ["IfcWall"] = Color.FromArgb(230, 126, 34),
        ["IfcSlab"] = Color.FromArgb(149, 165, 166),
        ["IfcRoof"] = Color.FromArgb(192, 57, 163),
        ["IfcCovering"] = Color.FromArgb(189, 195, 199),
        ["IfcDoor"] = Color.FromArgb(211, 84, 0),
        ["IfcWindow"] = Color.FromArgb(231, 76, 60),
        ["IfcColumn"] = Color.FromArgb(52, 73, 94),
        ["IfcBeam"] = Color.FromArgb(41, 128, 185),
        ["IfcFooting"] = Color.FromArgb(127, 140, 141),
        ["IfcStair"] = Color.FromArgb(39, 174, 96),
        ["IfcStairFlight"] = Color.FromArgb(39, 174, 96),
        ["IfcRailing"] = Color.FromArgb(46, 204, 113),
        ["IfcRamp"] = Color.FromArgb(26, 188, 156),
        ["IfcFurnishingElement"] = Color.FromArgb(155, 89, 182),
        ["IfcBuildingElementProxy"] = Color.FromArgb(180, 180, 180),
        ["IfcSanitaryTerminal"] = Color.FromArgb(52, 152, 219),
        ["IfcElectricDistributionBoard"] = Color.FromArgb(241, 196, 15),
        ["IfcLightFixture"] = Color.FromArgb(243, 156, 18),
        ["IfcDuctSegment"] = Color.FromArgb(22, 160, 133),
        ["IfcDuctFitting"] = Color.FromArgb(26, 188, 156),
        ["IfcPipeSegment"] = Color.FromArgb(41, 128, 185),
        ["IfcPipeFitting"] = Color.FromArgb(52, 152, 219),
        ["IfcValve"] = Color.FromArgb(142, 68, 173),
        ["IfcCableCarrierSegment"] = Color.FromArgb(243, 156, 18),
        ["IfcCableSegment"] = Color.FromArgb(230, 126, 34),
        ["IfcSite"] = Color.FromArgb(39, 174, 96),
        ["IfcGeographicElement"] = Color.FromArgb(46, 204, 113),
        ["IfcMember"] = Color.FromArgb(52, 73, 94),
        ["IfcSpace"] = Color.FromArgb(174, 214, 241),
        ["IfcGrid"] = Color.FromArgb(149, 165, 166),
        ["IfcBuildingElementPart"] = Color.FromArgb(127, 140, 141),
        ["IfcElementAssembly"] = Color.FromArgb(44, 62, 80)
    };

    public static readonly Color Storey = Color.FromArgb(90, 110, 130);
    public static readonly Color Fallback = Color.FromArgb(180, 180, 180);

    public static Color ForClass(string? ifcClass)
    {
        if (!string.IsNullOrWhiteSpace(ifcClass) && Colors.TryGetValue(ifcClass, out var color))
        {
            return color;
        }

        return Fallback;
    }
}
