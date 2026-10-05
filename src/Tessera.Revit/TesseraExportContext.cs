using Autodesk.Revit.DB;
using Tessera.Core;

namespace Tessera.Revit;

internal sealed class TesseraExportContext : IExportContext
{
    private readonly TsraModel _model;
    private readonly string _projectName;
    private readonly Stack<Document> _documents = new();
    private readonly Stack<Transform> _transforms = new();
    private readonly int _levelOfDetail;
    private readonly Dictionary<string, uint> _storeys = new(StringComparer.Ordinal);
    private readonly List<MeshDraft> _meshes = new();
    private readonly List<TesseraParameterValue> _parameters = new();

    private uint _project;
    private uint _site;
    private uint _building;
    private uint _step = 1;
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

    public TesseraExportContext(Document document, TsraModel model, string projectName, Transform baseTransform, int levelOfDetail)
    {
        _model = model;
        _projectName = string.IsNullOrWhiteSpace(projectName) ? "Project" : projectName;
        _levelOfDetail = levelOfDetail;
        _documents.Push(document);
        _transforms.Push(baseTransform);
    }

    public uint Project => _project;

    public int ElementCount { get; private set; }

    public bool IsCanceled() => false;

    public bool Start()
    {
        _project = _model.AddEntity("IfcProject", _projectName, TsraModel.StableId("project:" + _projectName), NextStep(), null, TsraModel.NoEntity, null);
        _site = _model.AddEntity("IfcSite", "Site", TsraModel.StableId("site:" + _projectName), NextStep(), null, TsraModel.NoEntity, null);
        _building = _model.AddEntity("IfcBuilding", "Building", TsraModel.StableId("building:" + _projectName), NextStep(), null, TsraModel.NoEntity, null);
        _model.AddAggregate(_project, _site);
        _model.AddAggregate(_site, _building);
        return true;
    }

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
        _parameters.Clear();
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
        if (_element != null && _meshes.Exists(mesh => mesh.Coordinates.Count > 0))
        {
            var ifcClass = IfcCategoryMap.For(_element);
            var level = TesseraParameters.FindLevel(_element);
            double? elevation = null;
            var storeyName = "Unassigned";
            if (level != null)
            {
                storeyName = string.IsNullOrWhiteSpace(level.Name) ? "Unassigned" : level.Name;
                var point = _elementFrame.OfPoint(new XYZ(0, 0, level.Elevation));
                elevation = TesseraRevitExporter.ToMeters(point.Z);
            }

            var storey = Storey(storeyName, elevation);
            TesseraParameters.Collect(_element, _parameters);
            var typeName = _parameters.Find(item => item.Name == TesseraKeys.TypeName)?.Text;
            var material = _meshes.Select(mesh => mesh.MaterialName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
            var entity = _model.AddEntity(
                ifcClass,
                SafeName(_element),
                GlobalId(_element.UniqueId),
                NextStep(),
                typeName,
                storey,
                material);
            _model.AddContainedIn(storey, entity);
            foreach (var parameter in _parameters)
            {
                if (parameter.Si is double si)
                {
                    _model.AddRealProperty(entity, "Revit", parameter.Name, si, parameter.IfcType);
                }
                else
                {
                    _model.AddTextProperty(entity, "Revit", parameter.Name, parameter.Text);
                }
            }

            foreach (var mesh in _meshes)
            {
                if (mesh.Coordinates.Count == 0)
                {
                    continue;
                }

                var style = _model.AddStyle(mesh.R, mesh.G, mesh.B, mesh.A);
                _model.AddMesh(entity, style, mesh.Coordinates);
            }

            ElementCount++;
        }

        _element = null;
        _pendingLink = null;
        _meshes.Clear();
        _parameters.Clear();
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
        var mesh = _meshes.Find(item => item.Key == key);
        if (mesh == null)
        {
            mesh = new MeshDraft
            {
                Key = key,
                R = _red,
                G = _green,
                B = _blue,
                A = _alpha,
                MaterialName = _materialName
            };
            _meshes.Add(mesh);
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

            Add(mesh, transform.OfPoint(points[facet.V1]));
            Add(mesh, transform.OfPoint(points[facet.V2]));
            Add(mesh, transform.OfPoint(points[facet.V3]));
        }
    }

    public void OnRPC(RPCNode node)
    {
    }

    public void OnLight(LightNode node)
    {
    }

    private uint Storey(string name, double? elevation)
    {
        if (_storeys.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var entity = _model.AddEntity("IfcBuildingStorey", name, TsraModel.StableId("storey:" + _projectName + ":" + name), NextStep(), null, TsraModel.NoEntity, null);
        _storeys[name] = entity;
        _model.AddAggregate(_building, entity);
        if (elevation is double metres)
        {
            _model.AddRealProperty(entity, "Revit", TesseraKeys.StoreyElevation, metres, "IFCLENGTHMEASURE");
        }

        return entity;
    }

    private uint NextStep() => _step++;

    private static void Add(MeshDraft mesh, XYZ feet)
    {
        mesh.Coordinates.Add(TesseraRevitExporter.ToMeters(feet.X));
        mesh.Coordinates.Add(TesseraRevitExporter.ToMeters(feet.Y));
        mesh.Coordinates.Add(TesseraRevitExporter.ToMeters(feet.Z));
    }

    private static byte[] GlobalId(string? uniqueId)
    {
        if (!string.IsNullOrEmpty(uniqueId) && uniqueId.Length >= 36 && Guid.TryParse(uniqueId.AsSpan(0, 36), out var guid))
        {
            return guid.ToByteArray();
        }

        return TsraModel.StableId(uniqueId ?? "element");
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

    private sealed class MeshDraft
    {
        public string Key = "";
        public byte R;
        public byte G;
        public byte B;
        public byte A;
        public string? MaterialName;
        public List<double> Coordinates { get; } = new();
    }
}
