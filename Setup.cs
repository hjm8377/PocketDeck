using System;
using System.Drawing;
using System.Windows.Forms;

internal static class Setup
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            int pid;
            if (args.Length == 2 && args[0] == "--update" && Int32.TryParse(args[1], out pid))
            {
                InstallPaths.WaitFor(pid);
                InstallPaths.Install(null);
                InstallPaths.Start(InstallPaths.ProgramPath, "");
                return;
            }
            Application.Run(new SetupWindow());
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Pocket Deck 설치", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal sealed class SetupWindow : Form
{
    private readonly TextBox migration = new TextBox();
    private readonly CheckBox launch = new CheckBox();

    public SetupWindow()
    {
        Text = "Pocket Deck " + BuildInfo.Version + " 설치";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(565, 220);
        Font = new Font("Malgun Gothic", 9F);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16),
            ColumnCount = 2, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);
        AddLine(layout, 0, new Label { Text = "설치 위치", AutoSize = true });
        AddLine(layout, 1, new TextBox { Text = InstallPaths.ProgramDirectory, ReadOnly = true,
            Dock = DockStyle.Top, TabStop = false });
        AddLine(layout, 2, new Label { Text = "기존 배치와 디자인을 가져올 압축판 폴더 (선택)", AutoSize = true });
        layout.Controls.Add(migration, 0, 3);
        migration.Dock = DockStyle.Top;
        var pick = new Button { Text = "폴더 선택…", Dock = DockStyle.Top, Height = 27 };
        pick.Click += (s, e) => {
            using (var dialog = new FolderBrowserDialog { ShowNewFolderButton = false })
                if (dialog.ShowDialog(this) == DialogResult.OK) migration.Text = dialog.SelectedPath;
        };
        layout.Controls.Add(pick, 1, 3);
        launch.Text = "설치 후 Pocket Deck 열기";
        launch.Checked = true;
        launch.AutoSize = true;
        AddLine(layout, 4, launch);
        var install = new Button { Text = "설치", Dock = DockStyle.Fill };
        layout.Controls.Add(install, 1, 5);
        AcceptButton = install;
        install.Click += (s, e) => {
            try
            {
                InstallPaths.Install(migration.Text.Trim());
                if (launch.Checked) InstallPaths.Start(InstallPaths.ProgramPath, "");
                Close();
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
    }

    private static void AddLine(TableLayoutPanel panel, int row, Control control)
    {
        panel.Controls.Add(control, 0, row);
        panel.SetColumnSpan(control, 2);
    }
}
