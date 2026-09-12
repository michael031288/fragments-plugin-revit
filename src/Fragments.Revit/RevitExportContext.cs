using System.Collections.Generic;
using Autodesk.Revit.DB;
using Fragments.Core;

namespace Fragments.Revit;

internal sealed class RevitExportContext : IExportContext
{
    private readonly FragmentsModelBuilder _builder;
    private readonly Stack<Document> _documents = new Stack<Document>();
    private readonly Stack<Transform> _transforms = new Stack<Transform>();
    private readonly Dictionary<string, uint> _itemsByUniqueId = new Dictionary<string, uint>(StringComparer.Ordinal);
    private readonly HashSet<uint> _exported = new HashSet<uint>();
    private readonly Dictionary<uint, ElementId> _elementLevels = new Dictionary<uint, ElementId>();

    private uint? _currentLocalId;
    private int _currentMaterial;
    private RevitLinkInstance? _pendingLink;

    public RevitExportContext(Document document, FragmentsModelBuilder builder, Dictionary<ElementId, uint> levelLocalIds)
    {
        _builder = builder;
        _documents.Push(document);
        _transforms.Push(Transform.Identity);
        _currentMaterial = _builder.AddMaterial(180, 180, 180);
    }

    public IReadOnlyCollection<uint> ExportedLocalIds => _exported;
    public Dictionary<uint, ElementId> ElementLevels => _elementLevels;
    public int TriangleCount { get; private set; }

    public bool IsCanceled() => false;

    public bool Start() => true;

    public void Finish()
    {
    }

    public RenderNodeAction OnViewBegin(ViewNode node) => RenderNodeAction.Proceed;

    public void OnViewEnd(ElementId elementId)
    {
    }

    public RenderNodeAction OnElementBegin(ElementId elementId)
    {
        var document = _documents.Peek();
        var element = document.GetElement(elementId);
        _pendingLink = element as RevitLinkInstance;
        _currentLocalId = null;

        if (element == null || _pendingLink != null || element is Autodesk.Revit.DB.View)
        {
            return RenderNodeAction.Proceed;
        }

        _currentLocalId = GetOrAddItem(element);
        return RenderNodeAction.Proceed;
    }

    public void OnElementEnd(ElementId elementId)
    {
        _currentLocalId = null;
        _pendingLink = null;
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
        _transforms.Push(_transforms.Peek().Multiply(node.GetTransform()));
        var linkDocument = _pendingLink?.GetLinkDocument();
        _documents.Push(linkDocument ?? _documents.Peek());
        return RenderNodeAction.Proceed;
    }

    public void OnLinkEnd(LinkNode node)
    {
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
        var alpha = (byte)Math.Max(0, Math.Min(255, (int)Math.Round((1.0 - node.Transparency) * 255.0)));
        _currentMaterial = _builder.AddMaterial(color.Red, color.Green, color.Blue, alpha);
    }

    public void OnPolymesh(PolymeshTopology polymesh)
    {
        if (_currentLocalId == null)
        {
            return;
        }

        var transform = _transforms.Peek();
        var points = new List<Vec3>(polymesh.NumberOfPoints);
        foreach (var point in polymesh.GetPoints())
        {
            points.Add(new Vec3(
                (float)RevitFragmentExporter.ToMeters(point.X),
                (float)RevitFragmentExporter.ToMeters(point.Y),
                (float)RevitFragmentExporter.ToMeters(point.Z)));
        }

        var indices = new List<int>(polymesh.NumberOfFacets * 3);
        foreach (var facet in polymesh.GetFacets())
        {
            indices.Add(facet.V1);
            indices.Add(facet.V2);
            indices.Add(facet.V3);
        }

        if (indices.Count < 3)
        {
            return;
        }

        TriangleCount += indices.Count / 3;
        var shell = _builder.AddTriangleShell(points, indices);
        var world = new FragmentTransform
        {
            Px = RevitFragmentExporter.ToMeters(transform.Origin.X),
            Py = RevitFragmentExporter.ToMeters(transform.Origin.Y),
            Pz = RevitFragmentExporter.ToMeters(transform.Origin.Z),
            Xx = (float)transform.BasisX.X,
            Xy = (float)transform.BasisX.Y,
            Xz = (float)transform.BasisX.Z,
            Yx = (float)transform.BasisY.X,
            Yy = (float)transform.BasisY.Y,
            Yz = (float)transform.BasisY.Z
        };
        _builder.AddInstance(_currentLocalId.Value, shell, _currentMaterial, world);
    }

    public void OnRPC(RPCNode node)
    {
    }

    public void OnLight(LightNode node)
    {
    }

    private uint GetOrAddItem(Element element)
    {
        if (_itemsByUniqueId.TryGetValue(element.UniqueId, out var existing))
        {
            return existing;
        }

        var localId = _builder.AddItem(IfcCategoryMap.For(element), element.UniqueId, ParameterCollector.Collect(element));
        _itemsByUniqueId[element.UniqueId] = localId;
        _exported.Add(localId);

        var levelId = element.LevelId;
        if (levelId != ElementId.InvalidElementId)
        {
            _elementLevels[localId] = levelId;
        }

        return localId;
    }
}
