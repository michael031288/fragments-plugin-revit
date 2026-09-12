using Fragments.Schema;
using Google.FlatBuffers;

namespace Fragments.Core;

public static class FragmentsWriter
{
    private const int UShortMax = 65535;

    public static byte[] Write(FragmentsModelBuilder model)
    {
        if (model == null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        var builder = new FlatBufferBuilder(1024);
        var nextEntityId = model.MaxLocalIdHint;

        var shellOffsets = WriteShells(builder, model.Shells);
        var shellsVector = Meshes.CreateShellsVector(builder, shellOffsets);
        var circleExtrusions = Meshes.CreateCircleExtrusionsVector(builder, Array.Empty<Offset<CircleExtrusion>>());

        var representationIds = new uint[model.Shells.Count];
        Meshes.StartRepresentationsVector(builder, model.Shells.Count);
        for (var i = model.Shells.Count - 1; i >= 0; i--)
        {
            var shell = model.Shells[i];
            representationIds[i] = nextEntityId++;
            Representation.CreateRepresentation(
                builder,
                (uint)i,
                shell.Min.X, shell.Min.Y, shell.Min.Z,
                shell.Max.X, shell.Max.Y, shell.Max.Z,
                RepresentationClass.SHELL);
        }

        var representations = builder.EndVector();

        var materialIds = new uint[model.Materials.Count];
        Meshes.StartMaterialsVector(builder, model.Materials.Count);
        for (var i = model.Materials.Count - 1; i >= 0; i--)
        {
            var material = model.Materials[i];
            materialIds[i] = nextEntityId++;
            Material.CreateMaterial(
                builder,
                material.R, material.G, material.B, material.A,
                material.DoubleSided ? RenderedFaces.TWO : RenderedFaces.ONE,
                Stroke.DEFAULT);
        }

        var materials = builder.EndVector();

        var sampleIds = new uint[model.Samples.Count];
        Meshes.StartSamplesVector(builder, model.Samples.Count);
        for (var i = model.Samples.Count - 1; i >= 0; i--)
        {
            var sample = model.Samples[i];
            sampleIds[i] = nextEntityId++;
            Sample.CreateSample(builder, sample.ItemId, sample.MaterialIndex, sample.RepresentationIndex, sample.LocalTransformIndex);
        }

        var samples = builder.EndVector();

        var localTransformIds = new uint[model.LocalTransformList.Count];
        Meshes.StartLocalTransformsVector(builder, model.LocalTransformList.Count);
        for (var i = model.LocalTransformList.Count - 1; i >= 0; i--)
        {
            localTransformIds[i] = nextEntityId++;
            WriteTransform(builder, model.LocalTransformList[i]);
        }

        var localTransforms = builder.EndVector();

        var globalTransformIds = new uint[model.GlobalTransforms.Count];
        Meshes.StartGlobalTransformsVector(builder, model.GlobalTransforms.Count);
        for (var i = model.GlobalTransforms.Count - 1; i >= 0; i--)
        {
            globalTransformIds[i] = nextEntityId++;
            WriteTransform(builder, model.GlobalTransforms[i]);
        }

        var globalTransforms = builder.EndVector();

        var meshesItems = Meshes.CreateMeshesItemsVector(builder, ToArray(model.MeshesItems));
        var representationIdsVector = Meshes.CreateRepresentationIdsVector(builder, representationIds);
        var sampleIdsVector = Meshes.CreateSampleIdsVector(builder, sampleIds);
        var materialIdsVector = Meshes.CreateMaterialIdsVector(builder, materialIds);
        var localTransformIdsVector = Meshes.CreateLocalTransformIdsVector(builder, localTransformIds);
        var globalTransformIdsVector = Meshes.CreateGlobalTransformIdsVector(builder, globalTransformIds);
        var coordinates = WriteTransform(builder, model.Coordinates);

        Meshes.StartMeshes(builder);
        Meshes.AddCoordinates(builder, coordinates);
        Meshes.AddMeshesItems(builder, meshesItems);
        Meshes.AddSamples(builder, samples);
        Meshes.AddRepresentations(builder, representations);
        Meshes.AddMaterials(builder, materials);
        Meshes.AddCircleExtrusions(builder, circleExtrusions);
        Meshes.AddShells(builder, shellsVector);
        Meshes.AddLocalTransforms(builder, localTransforms);
        Meshes.AddGlobalTransforms(builder, globalTransforms);
        Meshes.AddMaterialIds(builder, materialIdsVector);
        Meshes.AddRepresentationIds(builder, representationIdsVector);
        Meshes.AddSampleIds(builder, sampleIdsVector);
        Meshes.AddLocalTransformIds(builder, localTransformIdsVector);
        Meshes.AddGlobalTransformIds(builder, globalTransformIdsVector);
        var meshes = Meshes.EndMeshes(builder);

        var uniqueAttributeSet = new HashSet<string>(StringComparer.Ordinal);
        var uniqueRelationNames = new HashSet<string>(StringComparer.Ordinal);
        var attributeOffsets = new Offset<Fragments.Schema.Attribute>[model.Items.Count];
        var localIds = new uint[model.Items.Count];
        var categoryOffsets = new StringOffset[model.Items.Count];
        var guidList = new List<StringOffset>();
        var guidItems = new List<uint>();
        var relationOffsets = new List<Offset<Relation>>();
        var relationItems = new List<int>();

        for (var i = 0; i < model.Items.Count; i++)
        {
            var item = model.Items[i];
            localIds[i] = item.LocalId;
            categoryOffsets[i] = builder.CreateSharedString(item.Category);

            var attrStrings = new StringOffset[item.Attributes.Count];
            for (var a = 0; a < item.Attributes.Count; a++)
            {
                var attr = item.Attributes[a];
                var encoded = JsonUtil.EncodeAttribute(attr.Name, attr.Value, attr.IfcType);
                uniqueAttributeSet.Add(encoded);
                attrStrings[a] = builder.CreateSharedString(encoded);
            }

            attributeOffsets[i] = Fragments.Schema.Attribute.CreateAttribute(builder, Fragments.Schema.Attribute.CreateDataVector(builder, attrStrings));

            if (!string.IsNullOrEmpty(item.Guid))
            {
                guidList.Add(builder.CreateString(item.Guid));
                guidItems.Add(item.LocalId);
            }

            if (item.Relations.Count > 0)
            {
                var relStrings = new StringOffset[item.Relations.Count];
                for (var r = 0; r < item.Relations.Count; r++)
                {
                    var rel = item.Relations[r];
                    uniqueRelationNames.Add(rel.Name);
                    relStrings[r] = builder.CreateSharedString(JsonUtil.EncodeRelation(rel.Name, rel.RelatedLocalIds));
                }

                relationOffsets.Add(Relation.CreateRelation(builder, Relation.CreateDataVector(builder, relStrings)));
                relationItems.Add((int)item.LocalId);
            }
        }

        var uniqueAttrOffsets = new StringOffset[uniqueAttributeSet.Count];
        var ua = 0;
        foreach (var value in uniqueAttributeSet)
        {
            uniqueAttrOffsets[ua++] = builder.CreateSharedString(value);
        }

        var uniqueRelNameOffsets = new StringOffset[uniqueRelationNames.Count];
        var ur = 0;
        foreach (var name in uniqueRelationNames)
        {
            uniqueRelNameOffsets[ur++] = builder.CreateSharedString(name);
        }

        var attributesVector = Model.CreateAttributesVector(builder, attributeOffsets);
        var uniqueAttributesVector = Model.CreateUniqueAttributesVector(builder, uniqueAttrOffsets);
        var relationNamesVector = Model.CreateRelationNamesVector(builder, uniqueRelNameOffsets);
        var localIdsVector = Model.CreateLocalIdsVector(builder, localIds);
        var categoriesVector = Model.CreateCategoriesVector(builder, categoryOffsets);
        var guidsVector = Model.CreateGuidsVector(builder, guidList.ToArray());
        var guidsItemsVector = Model.CreateGuidsItemsVector(builder, guidItems.ToArray());
        var relationsVector = Model.CreateRelationsVector(builder, relationOffsets.ToArray());
        var relationsItemsVector = Model.CreateRelationsItemsVector(builder, relationItems.ToArray());
        var spatial = WriteSpatial(builder, model.SpatialStructure);
        var metadata = builder.CreateString(model.Metadata ?? "{}");
        var guid = builder.CreateString(model.ModelGuid);

        var modelOffset = Model.CreateModel(
            builder,
            metadata,
            guidsVector,
            guidsItemsVector,
            nextEntityId,
            localIdsVector,
            categoriesVector,
            meshes,
            attributesVector,
            relationsVector,
            relationsItemsVector,
            guid,
            spatial,
            uniqueAttributesVector,
            relationNamesVector);

        Model.FinishModelBuffer(builder, modelOffset);
        return builder.SizedByteArray();
    }

    public static Model Read(byte[] bytes)
    {
        var raw = ZlibCodec.Decompress(bytes);
        var buffer = new ByteBuffer(raw);
        if (!Model.VerifyModel(buffer))
        {
            throw new InvalidDataException("The buffer is not a valid Fragments Model with identifier 0001.");
        }

        return Model.GetRootAsModel(buffer);
    }

    private static Offset<Shell>[] WriteShells(FlatBufferBuilder builder, IReadOnlyList<FragmentsModelBuilder.ShellRecord> shells)
    {
        var offsets = new Offset<Shell>[shells.Count];
        for (var s = 0; s < shells.Count; s++)
        {
            var shell = shells[s];
            var isBig = shell.Points.Length > UShortMax;

            Shell.StartPointsVector(builder, shell.Points.Length);
            for (var i = shell.Points.Length - 1; i >= 0; i--)
            {
                var p = shell.Points[i];
                FloatVector.CreateFloatVector(builder, p.X, p.Y, p.Z);
            }

            var points = builder.EndVector();

            var profileOffsets = new List<Offset<ShellProfile>>();
            var bigProfileOffsets = new List<Offset<BigShellProfile>>();
            for (var p = 0; p < shell.Profiles.Length; p++)
            {
                var indices = shell.Profiles[p];
                if (isBig)
                {
                    var uints = new uint[indices.Length];
                    for (var i = 0; i < indices.Length; i++)
                    {
                        uints[i] = (uint)indices[i];
                    }

                    bigProfileOffsets.Add(BigShellProfile.CreateBigShellProfile(builder, BigShellProfile.CreateIndicesVector(builder, uints)));
                }
                else
                {
                    var shorts = new ushort[indices.Length];
                    for (var i = 0; i < indices.Length; i++)
                    {
                        shorts[i] = (ushort)indices[i];
                    }

                    profileOffsets.Add(ShellProfile.CreateShellProfile(builder, ShellProfile.CreateIndicesVector(builder, shorts)));
                }
            }

            var profiles = Shell.CreateProfilesVector(builder, profileOffsets.ToArray());
            var holes = Shell.CreateHolesVector(builder, Array.Empty<Offset<ShellHole>>());
            var bigProfiles = Shell.CreateBigProfilesVector(builder, bigProfileOffsets.ToArray());
            var bigHoles = Shell.CreateBigHolesVector(builder, Array.Empty<Offset<BigShellHole>>());
            var faceIds = Shell.CreateProfilesFaceIdsVector(builder, shell.FaceIds);

            offsets[s] = Shell.CreateShell(
                builder,
                profiles,
                holes,
                points,
                bigProfiles,
                bigHoles,
                isBig ? ShellType.BIG : ShellType.NONE,
                faceIds);
        }

        return offsets;
    }

    private static Offset<Transform> WriteTransform(FlatBufferBuilder builder, FragmentTransform transform)
    {
        transform ??= FragmentTransform.Identity;
        return Transform.CreateTransform(
            builder,
            transform.Px, transform.Py, transform.Pz,
            transform.Xx, transform.Xy, transform.Xz,
            transform.Yx, transform.Yy, transform.Yz);
    }

    private static Offset<SpatialStructure> WriteSpatial(FlatBufferBuilder builder, FragmentSpatialNode? node)
    {
        if (node == null)
        {
            return default;
        }

        var childOffsets = new Offset<SpatialStructure>[node.Children.Count];
        for (var i = 0; i < node.Children.Count; i++)
        {
            childOffsets[i] = WriteSpatial(builder, node.Children[i]);
        }

        var children = SpatialStructure.CreateChildrenVector(builder, childOffsets);
        var category = node.Category == null ? default : builder.CreateSharedString(node.Category);
        return SpatialStructure.CreateSpatialStructure(builder, node.LocalId, category, children);
    }

    private static uint[] ToArray(IReadOnlyList<uint> values)
    {
        var array = new uint[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            array[i] = values[i];
        }

        return array;
    }
}
