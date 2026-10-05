using System.Windows.Forms;

namespace Tessera.Revit;

internal sealed class ExportTesseraForm : Form
{
    private readonly ComboBox _quality = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly CheckBox _shared = new()
    {
        Text = "Bake shared coordinates into the model. Leave off to keep geometry in project coordinates.",
        AutoSize = true,
        MaximumSize = new Size(460, 0)
    };

    public ExportTesseraForm()
    {
        Text = "Export Tessera model";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        _quality.Items.AddRange(new object[]
        {
            "Medium — Revit detail 8",
            "Coarse — Revit detail 2",
            "Fine — Revit detail 15",
            "Match the active view"
        });
        _quality.SelectedIndex = 0;

        var ok = new Button { Text = "Export...", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        AcceptButton = ok;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Dock = DockStyle.Fill
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "Mesh detail", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 12, 0) }, 0, row);
        _quality.Margin = new Padding(0, 4, 0, 0);
        layout.Controls.Add(_quality, 1, row);
        layout.Controls.Add(_shared, 0, 1);
        layout.SetColumnSpan(_shared, 2);
        layout.Controls.Add(buttons, 0, 2);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);
    }

    public TesseraExportSettings Settings => new()
    {
        LevelOfDetail = _quality.SelectedIndex switch
        {
            1 => 2,
            2 => 15,
            3 => -1,
            _ => 8
        },
        UseSharedCoordinates = _shared.Checked
    };
}
