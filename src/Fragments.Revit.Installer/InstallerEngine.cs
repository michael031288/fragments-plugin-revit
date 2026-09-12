using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;

namespace Fragments.Revit.Installer;

internal static class InstallerEngine
{
    public const string ProductName = "Fragments Exporter";
    private const string AddinFileName = "Fragments.Revit.addin";
    private const string AddInId = "a7e5c4b1-9d2f-4c8a-b1e3-0f1a9c000001";

    public static string ProductRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FragmentsExporter");

    public static string AddinsRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins");

    public static IReadOnlyList<int> BundledYears()
    {
        using var zip = OpenPayload();
        if (zip == null)
        {
            return Array.Empty<int>();
        }

        var years = new SortedSet<int>();
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            var slash = name.IndexOf('/');
            if (slash <= 0)
            {
                continue;
            }

            if (int.TryParse(name[..slash], out var year) && year is >= 2024 and <= 2030)
            {
                years.Add(year);
            }
        }

        return years.ToList();
    }

    public static IReadOnlyList<int> DetectInstalledRevitYears()
    {
        var years = new SortedSet<int>();
        foreach (var year in Enumerable.Range(2024, 7))
        {
            var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Autodesk", $"Revit {year}", "Revit.exe");
            var addins = Path.Combine(AddinsRoot, year.ToString());
            if (File.Exists(exe) || Directory.Exists(addins))
            {
                years.Add(year);
            }
        }

        return years.ToList();
    }

    public static bool RevitIsRunning()
    {
        return Process.GetProcessesByName("Revit").Length > 0;
    }

    public static InstallResult Install(IEnumerable<int> years)
    {
        var selected = years.Distinct().OrderBy(y => y).ToList();
        if (selected.Count == 0)
        {
            return InstallResult.Fail("Select at least one Revit year.");
        }

        using var zip = OpenPayload();
        if (zip == null)
        {
            return InstallResult.Fail("This setup EXE was built without add-in files. Run tools\\publish-revit-installer.ps1 on a development machine first.");
        }

        var installed = new List<int>();
        var notes = new List<string>();
        foreach (var year in selected)
        {
            var prefix = year + "/";
            var files = zip.Entries.Where(e =>
                e.FullName.Replace('\\', '/').StartsWith(prefix, StringComparison.Ordinal)
                && !string.IsNullOrEmpty(e.Name)).ToList();
            if (files.Count == 0)
            {
                notes.Add($"Revit {year}: no matching add-in is bundled in this installer.");
                continue;
            }

            var addinDir = Path.Combine(AddinsRoot, year.ToString());
            var target = Path.Combine(addinDir, "Fragments.Revit");
            Directory.CreateDirectory(target);
            foreach (var entry in files)
            {
                var destination = Path.Combine(target, entry.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }

            var assemblyPath = Path.Combine(target, "Fragments.Revit.dll");
            if (!File.Exists(assemblyPath))
            {
                notes.Add($"Revit {year}: bundle is missing Fragments.Revit.dll.");
                continue;
            }

            Directory.CreateDirectory(addinDir);
            File.WriteAllText(Path.Combine(addinDir, AddinFileName), BuildAddinXml(year));
            installed.Add(year);
            notes.Add($"Revit {year}: installed for the current Windows user ({addinDir}).");
        }

        if (installed.Count == 0)
        {
            return InstallResult.Fail(string.Join(Environment.NewLine, notes));
        }

        notes.Add(string.Empty);
        notes.Add("No administrator password and no Autodesk login were required.");
        notes.Add("Restart Revit. The button appears on the Add-Ins tab under panel 'Fragments'.");
        notes.Add("If Revit asks about an unsigned add-in, choose Always Load.");
        return InstallResult.Ok(string.Join(Environment.NewLine, notes), installed);
    }

    public static InstallResult Uninstall(IEnumerable<int>? years = null)
    {
        var selected = (years ?? DetectInstalledRevitYears().Concat(BundledYears()).Distinct()).ToList();
        var notes = new List<string>();
        foreach (var year in selected)
        {
            var addinDir = Path.Combine(AddinsRoot, year.ToString());
            var addin = Path.Combine(addinDir, AddinFileName);
            if (File.Exists(addin))
            {
                File.Delete(addin);
                notes.Add($"Removed {addin}");
            }

            var pluginDir = Path.Combine(addinDir, "Fragments.Revit");
            if (Directory.Exists(pluginDir))
            {
                Directory.Delete(pluginDir, recursive: true);
                notes.Add($"Removed {pluginDir}");
            }

            // Also clean up any legacy ProductRoot location from previous versions
            var legacy = Path.Combine(ProductRoot, year.ToString());
            if (Directory.Exists(legacy))
            {
                Directory.Delete(legacy, recursive: true);
                notes.Add($"Removed {legacy}");
            }
        }

        if (Directory.Exists(ProductRoot) && Directory.GetFileSystemEntries(ProductRoot).Length == 0)
        {
            Directory.Delete(ProductRoot);
        }

        if (notes.Count == 0)
        {
            notes.Add("Nothing to remove.");
        }

        return InstallResult.Ok(string.Join(Environment.NewLine, notes), selected);
    }

    private static ZipArchive? OpenPayload()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream("payload.zip");
        if (stream == null)
        {
            var sidecar = Path.Combine(AppContext.BaseDirectory, "payload.zip");
            if (File.Exists(sidecar))
            {
                return ZipFile.OpenRead(sidecar);
            }

            return null;
        }

        var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;
        return new ZipArchive(copy, ZipArchiveMode.Read);
    }

    private static string BuildAddinXml(int year)
    {
        var addInElement = new XElement("AddIn", new XAttribute("Type", "Application"),
            new XElement("Name", ProductName),
            new XElement("Description", $"{ProductName} for Revit"),
            new XElement("Assembly", @"Fragments.Revit\Fragments.Revit.dll"),
            new XElement("FullClassName", "Fragments.Revit.App"),
            new XElement("ClientId", AddInId),
            new XElement("VendorId", "FRAG"),
            new XElement("VendorDescription", "That Open Fragments exporter for Revit"));

        var root = new XElement("RevitAddIns", addInElement);

        if (year >= 2026)
        {
            root.Add(new XElement("ManifestSettings",
                new XElement("UseRevitContext", "False"),
                new XElement("ContextName", "FragmentsExporter")));
        }

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            root);
        return document.Declaration + Environment.NewLine + document;
    }
}

internal sealed class InstallResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<int> Years { get; init; } = Array.Empty<int>();

    public static InstallResult Ok(string message, IReadOnlyList<int> years) =>
        new() { Success = true, Message = message, Years = years };

    public static InstallResult Fail(string message) =>
        new() { Success = false, Message = message };
}
