namespace Fragments.Revit.Installer;

internal sealed class SetupForm : Form
{
    private readonly CheckedListBox _years = new();
    private readonly TextBox _log = new();
    private readonly Button _install = new();
    private readonly Button _uninstall = new();

    public SetupForm()
    {
        Text = "Fragments Revit Add-in Setup";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 420);
        Font = new Font("Segoe UI", 9.75f);

        var intro = new Label
        {
            AutoSize = false,
            Location = new Point(16, 16),
            Size = new Size(488, 64),
            Text = "Installs the Fragments exporter for the current Windows user only. " +
                   "No administrator password and no Autodesk account login are required."
        };

        var yearLabel = new Label
        {
            AutoSize = true,
            Location = new Point(16, 88),
            Text = "Revit versions"
        };

        _years.Location = new Point(16, 112);
        _years.Size = new Size(488, 90);
        _years.CheckOnClick = true;

        _install.Text = "Install";
        _install.Location = new Point(16, 214);
        _install.Size = new Size(120, 32);
        _install.Click += (_, _) => Run(install: true);

        _uninstall.Text = "Uninstall";
        _uninstall.Location = new Point(144, 214);
        _uninstall.Size = new Size(120, 32);
        _uninstall.Click += (_, _) => Run(install: false);

        var close = new Button
        {
            Text = "Close",
            Location = new Point(384, 214),
            Size = new Size(120, 32),
            DialogResult = DialogResult.Cancel
        };
        close.Click += (_, _) => Close();
        CancelButton = close;

        _log.Location = new Point(16, 258);
        _log.Size = new Size(488, 142);
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;

        Controls.Add(intro);
        Controls.Add(yearLabel);
        Controls.Add(_years);
        Controls.Add(_install);
        Controls.Add(_uninstall);
        Controls.Add(close);
        Controls.Add(_log);

        Load += (_, _) => Populate();
    }

    private void Populate()
    {
        var bundled = InstallerEngine.BundledYears();
        var installed = InstallerEngine.DetectInstalledRevitYears();
        if (bundled.Count == 0)
        {
            _log.Text = "This EXE does not contain add-in binaries. Build it with tools\\publish-revit-installer.ps1.";
            _install.Enabled = false;
            return;
        }

        _years.Items.Clear();
        foreach (var year in bundled)
        {
            var item = new YearItem(year, installed.Contains(year)
                ? $"Revit {year}  —  detected on this PC"
                : $"Revit {year}");
            var index = _years.Items.Add(item);
            _years.SetItemChecked(index, installed.Contains(year) || installed.Count == 0);
        }

        if (InstallerEngine.RevitIsRunning())
        {
            _log.Text = "Revit is currently running. Close it before installing so the add-in can load on the next launch.";
        }
        else
        {
            _log.Text = "Ready. Files are copied into your user profile (AppData), not Program Files.";
        }
    }

    private void Run(bool install)
    {
        var years = _years.CheckedItems.Cast<YearItem>().Select(item => item.Year).ToList();
        if (years.Count == 0)
        {
            _log.Text = "Select at least one Revit year.";
            return;
        }

        if (InstallerEngine.RevitIsRunning())
        {
            var proceed = MessageBox.Show(
                this,
                "Revit is running. The add-in will not appear until Revit is restarted. Continue anyway?",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (proceed != DialogResult.Yes)
            {
                return;
            }
        }

        try
        {
            var result = install ? InstallerEngine.Install(years) : InstallerEngine.Uninstall(years);
            _log.Text = result.Message;
            MessageBox.Show(this, result.Message, result.Success ? Text : "Setup failed",
                MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            _log.Text = ex.ToString();
            MessageBox.Show(this, ex.Message, "Setup failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private sealed class YearItem
    {
        public YearItem(int year, string label)
        {
            Year = year;
            Label = label;
        }

        public int Year { get; }
        public string Label { get; }
        public override string ToString() => Label;
    }
}
