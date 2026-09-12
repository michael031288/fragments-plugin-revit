using System.Text.Json;
using Xunit;

namespace Fragments.Core.Tests;

public class ReferenceArtifactTests
{
    [Fact]
    public void WritesCubeReferenceFiles()
    {
        var repo = FindRepoRoot();
        var dir = Path.Combine(repo, "tools", "reference");
        Directory.CreateDirectory(dir);

        var raw = CubeGeometry.CreateUnitCube().Build(false);
        var compressed = CubeGeometry.CreateUnitCube().Build(true);
        File.WriteAllBytes(Path.Combine(dir, "cube.raw.frag"), raw);
        File.WriteAllBytes(Path.Combine(dir, "cube.frag"), compressed);

        var model = FragmentsWriter.Read(compressed);
        var summary = new
        {
            items = model.LocalIdsLength,
            shells = model.Meshes!.Value.ShellsLength,
            samples = model.Meshes.Value.SamplesLength,
            points = model.Meshes.Value.Shells(0)!.Value.PointsLength,
            identifier = "0001",
            compressedBytes = compressed.Length,
            rawBytes = raw.Length
        };
        File.WriteAllText(Path.Combine(dir, "cube.summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));

        Assert.True(File.Exists(Path.Combine(dir, "cube.frag")));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "schema", "index.fbs")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
