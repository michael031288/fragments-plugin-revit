using System.Windows.Forms;
using Tessera.Core;

namespace Tessera.Revit;

internal sealed class ExportTesseraForm : Form
{
    private readonly ComboBox _layers = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly ComboBox _quality = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly ComboBox _split = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly CheckBox _shared = new CheckBox
    {
        Text = "Bake shared coordinates (survey). Leave off to keep the model near Rhino's origin.",
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

        _layers.Items.AddRange(new object[]
        {
            "Class and storey",
            "IFC class",
            "Building storey"
        });
        _layers.SelectedIndex = 0;

        _quality.Items.AddRange(new object[]
        {
            "Medium — Revit detail 8",
            "Coarse — Revit detail 2",
            "Fine — Revit detail 15",
            "Match the active view"
        });
        _quality.SelectedIndex = 0;

        _split.Items.AddRange(new object[]
        {
            "Single file",
            "One file per storey",
            "One file per IFC class"
        });
        _split.SelectedIndex = 0;

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
        AddRow(layout, "Layers", _layers);
        AddRow(layout, "Mesh detail", _quality);
        AddRow(layout, "Files", _split);
        layout.Controls.Add(_shared, 0, 3);
        layout.SetColumnSpan(_shared, 2);
        layout.Controls.Add(buttons, 0, 4);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);
    }

    public TesseraExportSettings Settings => new TesseraExportSettings
    {
        LayerMode = _layers.SelectedIndex switch
        {
            1 => TesseraLayerMode.Class,
            2 => TesseraLayerMode.Storey,
            _ => TesseraLayerMode.ClassAndStorey
        },
        LevelOfDetail = _quality.SelectedIndex switch
        {
            1 => 2,
            2 => 15,
            3 => -1,
            _ => 8
        },
        Split = _split.SelectedIndex switch
        {
            1 => TesseraSplitMode.ByStorey,
            2 => TesseraSplitMode.ByClass,
            _ => TesseraSplitMode.None
        },
        UseSharedCoordinates = _shared.Checked
    };

    private static void AddRow(TableLayoutPanel layout, string label, Control editor)
    {
        var row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 12, 0) }, 0, row);
        editor.Margin = new Padding(0, 4, 0, 0);
        layout.Controls.Add(editor, 1, row);
    }
}
