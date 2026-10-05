using System.Globalization;
using Autodesk.Revit.DB;
using Tessera.Core;

namespace Tessera.Revit;

internal sealed class TesseraExportContext : IExportContext
{
    private readonly TesseraModel _model;
    private readonly Stack<Document> _documents = new Stack<Document>();
    private readonly Stack<Transform> _transforms = new Stack<Transform>();
    private readonly int _levelOfDetail;
    private readonly Dictionary<string, TesseraMesh> _meshes = new Dictionary<string, TesseraMesh>(StringComparer.Ordinal);

    private Element? _element;
    private Transform _elementFrame = Transform.Identity;
    private RevitLinkInstance? _pendingLink;
    private int _linkDepth;
    private byte _red = 180;
    private byte _green = 180;
    private byte _blue = 180;
    private byte _alpha = 255;
    private string? _materialName;
    private bool _hasMaterial;

    public TesseraExportContext(Document document, TesseraModel model, Transform baseTransform, int levelOfDetail)
    {
        _model = model;
        _levelOfDetail = levelOfDetail;
        _documents.Push(document);
        _transforms.Push(baseTransform);
    }

    public int TriangleCount { get; private set; }

    public bool IsCanceled() => false;

    public bool Start() => true;

    public void Finish()
    {
    }

    public RenderNodeAction OnViewBegin(ViewNode node)
    {
        if (_levelOfDetail >= 0)
        {
            node.LevelOfDetail = _levelOfDetail;
        }

        return RenderNodeAction.Proceed;
    }

    public void OnViewEnd(ElementId elementId)
    {
    }

    public RenderNodeAction OnElementBegin(ElementId elementId)
    {
        var document = _documents.Peek();
        var element = document.GetElement(elementId);
        _pendingLink = element as RevitLinkInstance;
        _element = null;
        _meshes.Clear();
        _hasMaterial = false;

        if (element == null || _pendingLink != null || element is Autodesk.Revit.DB.View)
        {
            return RenderNodeAction.Proceed;
        }

        _element = element;
        _elementFrame = _transforms.Peek();
        var color = TesseraClassPalette.ForClass(IfcCategoryMap.For(element));
        _red = color.R;
        _green = color.G;
        _blue = color.B;
        _alpha = 255;
        _materialName = null;
        return RenderNodeAction.Proceed;
    }

    public void OnElementEnd(ElementId elementId)
    {
        if (_element != null && _meshes.Count > 0)
        {
            var ifcClass = IfcCategoryMap.For(_element);
            var level = TesseraParameters.FindLevel(_element);
            var tesseraElement = new TesseraElement
            {
                Name = SafeName(_element),
                IfcClass = ifcClass,
                Storey = level?.Name ?? "Unassigned"
            };
            TesseraParameters.Collect(_element, tesseraElement.UserStrings);
            if (level != null)
            {
                var elevation = _elementFrame.OfPoint(new XYZ(0, 0, level.Elevation));
                tesseraElement.UserStrings[TesseraKeys.StoreyElevation] = TesseraRevitExporter.ToMeters(elevation.Z)
                    .ToString("G17", CultureInfo.InvariantCulture);
            }

            foreach (var mesh in _meshes.Values)
            {
                if (mesh.TriangleCount > 0)
                {
                    tesseraElement.Meshes.Add(mesh);
                }
            }

            if (tesseraElement.Meshes.Count > 0)
            {
                _model.Elements.Add(tesseraElement);
            }
        }

        _element = null;
        _pendingLink = null;
        _meshes.Clear();
    }

    public RenderNodeAction OnInstanceBegin(InstanceNode node)
    {
        _transforms.Push(_transforms.Peek().Multiply(node.GetTransform()));
        return RenderNodeAction.Proceed;
    }

    public void OnInstanceEnd(InstanceNode node)
    {
        if (_transforms.Count > 1)
        {
            _transforms.Pop();
        }
    }

    public RenderNodeAction OnLinkBegin(LinkNode node)
    {
        var linkDocument = _pendingLink?.GetLinkDocument();
        if (linkDocument == null)
        {
            return RenderNodeAction.Skip;
        }

        _transforms.Push(_transforms.Peek().Multiply(node.GetTransform()));
        _documents.Push(linkDocument);
        _linkDepth++;
        return RenderNodeAction.Proceed;
    }

    public void OnLinkEnd(LinkNode node)
    {
        if (_linkDepth == 0)
        {
            return;
        }

        _linkDepth--;
        if (_documents.Count > 1)
        {
            _documents.Pop();
        }

        if (_transforms.Count > 1)
        {
            _transforms.Pop();
        }
    }

    public RenderNodeAction OnFaceBegin(FaceNode node) => RenderNodeAction.Proceed;

    public void OnFaceEnd(FaceNode node)
    {
    }

    public void OnMaterial(MaterialNode node)
    {
        var color = node.Color;
        _red = color.Red;
        _green = color.Green;
        _blue = color.Blue;
        _alpha = (byte)Math.Max(0, Math.Min(255, (int)Math.Round((1.0 - node.Transparency) * 255.0)));
        _hasMaterial = true;
        _materialName = null;

        try
        {
            if (node.MaterialId != null && node.MaterialId != ElementId.InvalidElementId)
            {
                _materialName = (_documents.Peek().GetElement(node.MaterialId) as Material)?.Name;
            }
        }
        catch
        {
            _materialName = null;
        }
    }

    public void OnPolymesh(PolymeshTopology polymesh)
    {
        if (_element == null || polymesh.NumberOfFacets == 0)
        {
            return;
        }

        var key = (_hasMaterial ? _materialName ?? "material" : "class") + ":" + _red + "," + _green + "," + _blue + "," + _alpha;
        if (!_meshes.TryGetValue(key, out var mesh))
        {
            mesh = new TesseraMesh
            {
                R = _red,
                G = _green,
                B = _blue,
                A = _alpha,
                MaterialName = _materialName
            };
            _meshes[key] = mesh;
        }

        var transform = _transforms.Peek();
        var points = polymesh.GetPoints();
        var pointCount = points.Count;
        foreach (var facet in polymesh.GetFacets())
        {
            if ((uint)facet.V1 >= (uint)pointCount || (uint)facet.V2 >= (uint)pointCount || (uint)facet.V3 >= (uint)pointCount)
            {
                continue;
            }

            var a = ToMeters(transform.OfPoint(points[facet.V1]));
            var b = ToMeters(transform.OfPoint(points[facet.V2]));
            var c = ToMeters(transform.OfPoint(points[facet.V3]));
            mesh.AddTriangle(a.X, a.Y, a.Z, b.X, b.Y, b.Z, c.X, c.Y, c.Z);
            TriangleCount++;
        }
    }

    public void OnRPC(RPCNode node)
    {
    }

    public void OnLight(LightNode node)
    {
    }

    private static XYZ ToMeters(XYZ feet)
    {
        return new XYZ(
            TesseraRevitExporter.ToMeters(feet.X),
            TesseraRevitExporter.ToMeters(feet.Y),
            TesseraRevitExporter.ToMeters(feet.Z));
    }

    private static string SafeName(Element element)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(element.Name))
            {
                return element.Name;
            }
        }
        catch
        {
            // Some elements refuse to report a name.
        }

        return element.Category?.Name ?? "Element";
    }
}
