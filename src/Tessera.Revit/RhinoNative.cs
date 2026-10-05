using System.Reflection;
using System.Runtime.InteropServices;

namespace Tessera.Revit;

/// <summary>
/// Loads librhino3dm_native.dll from the add-in folder. Revit does not probe
/// that folder for native dependencies of Rhino3dm.dll.
/// </summary>
internal static class RhinoNative
{
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (string.IsNullOrEmpty(directory))
        {
            throw new FileNotFoundException("Tessera could not locate its add-in folder.");
        }

        var native = Path.Combine(directory, "librhino3dm_native.dll");
        if (!File.Exists(native))
        {
            throw new FileNotFoundException(
                "Tessera could not find librhino3dm_native.dll next to Tessera.Revit.dll.",
                native);
        }

        NativeLibrary.Load(native);
        _loaded = true;
    }
}
