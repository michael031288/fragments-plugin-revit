namespace Fragments.Revit.Installer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var quiet = args.Any(a => a.Equals("/quiet", StringComparison.OrdinalIgnoreCase)
                                  || a.Equals("/q", StringComparison.OrdinalIgnoreCase)
                                  || a.Equals("--quiet", StringComparison.OrdinalIgnoreCase));
        var install = args.Any(a => a.Equals("/install", StringComparison.OrdinalIgnoreCase)
                                    || a.Equals("--install", StringComparison.OrdinalIgnoreCase));
        var uninstall = args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)
                                      || a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));

        if (install || uninstall)
        {
            var years = ParseYears(args);
            if (years.Count == 0)
            {
                years = InstallerEngine.BundledYears().ToList();
            }

            try
            {
                var result = uninstall ? InstallerEngine.Uninstall(years) : InstallerEngine.Install(years);
                if (!quiet)
                {
                    Console.WriteLine(result.Message);
                }

                return result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new SetupForm());
        return 0;
    }

    private static List<int> ParseYears(string[] args)
    {
        var years = new List<int>();
        for (var i = 0; i < args.Length; i++)
        {
            if ((args[i].Equals("/year", StringComparison.OrdinalIgnoreCase)
                 || args[i].Equals("--year", StringComparison.OrdinalIgnoreCase))
                && i + 1 < args.Length
                && int.TryParse(args[i + 1], out var year))
            {
                years.Add(year);
            }
        }

        return years;
    }
}
