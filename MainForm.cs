using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ProjectStructureSummary;


public class FormDesignerPreventionDummy { }
public sealed  class MainForm : Form {
    private readonly CheckBox _cbAddAcknowledge = new();

    private readonly TextBox _txtFolder = new();
    private readonly Button _btnBrowse = new();
    private readonly Button _btnScan = new();

    private readonly SplitContainer _split = new();
    private readonly TreeView _tree = new();
    private readonly RichTextBox _rtbOutput = new();
    private readonly ImageList _treeImages = new();
    private readonly Button _btnCopy = new();
    private List<ProjectNode> _solutions = new();
    private bool _isUpdatingChecks;
    public MainForm() {
        Text = "C# Solution Explorer + Structure/Code Viewer";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1200, 720);
        ClientSize = new Size(1280, 800); // important: adequate starting size

        BuildUi();
        WireEvents();
    }


    private void BuildUi() {
        var top = new TableLayoutPanel {
            Dock = DockStyle.Top,
            Height = 44,
            ColumnCount = 5,
            Padding = new Padding(8, 8, 8, 4)
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _txtFolder.Dock = DockStyle.Fill;
        _txtFolder.PlaceholderText = "Path to folder with solution / project";

        _btnBrowse.Text = "Open folder...";
        _btnBrowse.AutoSize = true;
        _btnBrowse.Margin = new Padding(8, 0, 0, 0);

        _btnScan.Text = "Scan";
        _btnScan.AutoSize = true;
        _btnScan.Margin = new Padding(8, 0, 0, 0);

        _btnCopy.Text = "Copy";
        _btnCopy.AutoSize = true;
        _btnCopy.Margin = new Padding(8, 0, 0, 0);
        _btnCopy.Enabled = false; // пока пусто — выключена


        _cbAddAcknowledge.Text = "Acknowledge Phrase";
        _cbAddAcknowledge.AutoSize = true;
        _cbAddAcknowledge.Checked = true;
        _cbAddAcknowledge.Margin = new Padding(8, 0, 0, 0);


        top.Controls.Add(_txtFolder, 0, 0);
        top.Controls.Add(_btnBrowse, 1, 0);
        top.Controls.Add(_btnScan, 2, 0);
        top.Controls.Add(_btnCopy, 3, 0);
        top.Controls.Add(_cbAddAcknowledge, 4, 0);


        _split.Dock = DockStyle.Fill;
        _split.FixedPanel = FixedPanel.Panel1;
        // min-size and splitter will be set later, when width is valid

        ConfigureTree();
        ConfigureOutput();

        _split.Panel1.Controls.Add(_tree);
        _split.Panel2.Controls.Add(_rtbOutput);

        Controls.Add(_split);
        Controls.Add(top);
    }


    private const int PreferredLeftPanelWidth = 360;

    private void InitSplitLayoutSafe() {
        // first min-sizes
        _split.Panel1MinSize = 260;
        _split.Panel2MinSize = 400;

        SetSplitterDistanceSafe(PreferredLeftPanelWidth);
    }

    private void SetSplitterDistanceSafe(int preferred) {
        int available = _split.ClientSize.Width - _split.SplitterWidth;
        if (available <= 0)
            return;

        // if the window is suddenly too narrow for the current mins — just don't touch the splitter
        if (available < _split.Panel1MinSize + _split.Panel2MinSize)
            return;

        int min = _split.Panel1MinSize;
        int max = available - _split.Panel2MinSize;
        int target = Math.Clamp(preferred, min, max);

        if (_split.SplitterDistance != target)
            _split.SplitterDistance = target;
    }

    private void ConfigureTree() {
        _tree.Dock = DockStyle.Fill;
        _tree.HideSelection = false;
        _tree.FullRowSelect = true;
        _tree.ShowLines = true;
        _tree.ShowPlusMinus = true;
        _tree.ShowRootLines = true;
        _tree.CheckBoxes = true;
            
        SetupTreeIcons();
        _tree.ImageList = _treeImages;
    }

    private void ConfigureOutput() {
        _rtbOutput.Dock = DockStyle.Fill;
        _rtbOutput.Font = new Font("Consolas", 10f);
        _rtbOutput.WordWrap = false;
        _rtbOutput.DetectUrls = false;
    }
    private void TryAutoScanOnStartup() {
        var folder = _txtFolder.Text.Trim();
        if (string.IsNullOrWhiteSpace(folder))
            return;

        if (!Directory.Exists(folder))
            return;

        ReloadTree();
    }

    private void CopyOutputToClipboard() {
        var text = 
            ((_cbAddAcknowledge.Checked)?"Acknowledge and wait for further instructions: \r\n":"") 
            + _rtbOutput.Text;
        if (string.IsNullOrWhiteSpace(text))
            return;

        try {
            Clipboard.SetText(text);
        }
        catch (Exception ex) {
            MessageBox.Show(
                this,
                $"Failed to copy text to clipboard.\n{ex.Message}",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void WireEvents() {
        Load += (_, _) => {
            var state = AppStateStore.Load();
            if (!string.IsNullOrWhiteSpace(state.LastFolder))
                _txtFolder.Text = state.LastFolder;

            BeginInvoke((Action)(() => {
                InitSplitLayoutSafe();
                TryAutoScanOnStartup();
            }));
        };

        _btnCopy.Click += (_, _) => CopyOutputToClipboard();

        _rtbOutput.TextChanged += (_, _) => {
            _btnCopy.Enabled = !string.IsNullOrWhiteSpace(_rtbOutput.Text);
        };

        SizeChanged += (_, _) => {
            SetSplitterDistanceSafe(_split.SplitterDistance > 0 ? _split.SplitterDistance : PreferredLeftPanelWidth);
        };

        _btnBrowse.Click += (_, _) => {
            using var dlg = new FolderBrowserDialog {
                Description = "Выберите папку с C# solution / project",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };

            if (Directory.Exists(_txtFolder.Text))
                dlg.SelectedPath = _txtFolder.Text;

            if (dlg.ShowDialog(this) == DialogResult.OK) {
                _txtFolder.Text = dlg.SelectedPath;
                AppStateStore.SaveLastFolder(dlg.SelectedPath);
            }
        };

        _btnScan.Click += (_, _) => ReloadTree();

        _tree.AfterSelect += (_, _) => RefreshOutputForCurrentSelection();
        _tree.AfterCheck += TreeAfterCheck;
    }


    private void TreeAfterCheck(object? sender, TreeViewEventArgs e) {
        if (_isUpdatingChecks)
            return;

        try {
            _isUpdatingChecks = true;

            // user toggled node -> cascade same val down
            SetCheckedRecursive(e.Node, e.Node.Checked);

            // update parents upward
            UpdateParentCheckState(e.Node.Parent);
        }
        finally {
            _isUpdatingChecks = false;
        }

        RefreshOutputForCurrentSelection();
    }

    private void SetCheckedRecursive(TreeNode node, bool isChecked) {
        foreach (TreeNode child in node.Nodes) {
            if (child.Checked != isChecked)
                child.Checked = isChecked;

            SetCheckedRecursive(child, isChecked);
        }
    }

    private void UpdateParentCheckState(TreeNode? node) {
        while (node is not null) {
            bool anyChecked = false;

            foreach (TreeNode child in node.Nodes) {
                if (child.Checked) {
                    anyChecked = true;
                    break;
                }
            }

            if (node.Checked != anyChecked)
                node.Checked = anyChecked;

            node = node.Parent;
        }
    }

    private HashSet<ProjectNode> GetCheckedModelNodes() {
        var result = new HashSet<ProjectNode>();

        foreach (TreeNode root in _tree.Nodes)
            CollectCheckedModelNodes(root, result);

        return result;
    }

    private void CollectCheckedModelNodes(TreeNode treeNode, HashSet<ProjectNode> acc) {
        if (treeNode.Checked && treeNode.Tag is ProjectNode model)
            acc.Add(model);

        foreach (TreeNode child in treeNode.Nodes)
            CollectCheckedModelNodes(child, acc);
    }

    private void RefreshOutputForCurrentSelection() {
        if (_tree.SelectedNode?.Tag is not ProjectNode node)
            return;

        try {
            _rtbOutput.Clear();

            var includedNodes = GetCheckedModelNodes();
            _rtbOutput.Text = ProjectScanner.BuildReportForSelection(node, includedNodes);

            _rtbOutput.SelectionStart = 0;
            _rtbOutput.SelectionLength = 0;
        }
        catch (Exception ex) {
            MessageBox.Show(
                this,
                $"Не получилось построить отчёт по выбранному узлу.\n{ex.Message}",
                "Ошибка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ReloadTree() {
        var folder = _txtFolder.Text.Trim();

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) {
            MessageBox.Show(
                this,
                "Папка не найдена. Выбери корректный путь.",
                "Ошибка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try {
            _solutions = ProjectScanner.BuildTree(folder);

            _tree.BeginUpdate();
            _tree.Nodes.Clear();

            foreach (var solution in _solutions)
                _tree.Nodes.Add(CreateTreeNode(solution));

            _tree.EndUpdate();

            if (_tree.Nodes.Count > 0) {
                _tree.Nodes[0].Expand();
                _tree.SelectedNode = _tree.Nodes[0];
            }
            else {
                _rtbOutput.Clear();
                _rtbOutput.Text = "_No solutions/projects found._";
            }

            AppStateStore.SaveLastFolder(folder);
        }
        catch (Exception ex) {
            MessageBox.Show(
                this,
                $"Не получилось просканировать структуру.\n{ex.Message}",
                "Ошибка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private TreeNode CreateTreeNode(ProjectNode model) {
        var key = GetImageKey(model.Kind);

        var tn = new TreeNode(GetNodeText(model)) {
            Tag = model,
            ImageKey = key,
            SelectedImageKey = key,
            ToolTipText = model.FullPath,
            Checked = true
        };

        foreach (var child in model.Children)
            tn.Nodes.Add(CreateTreeNode(child));

        return tn;
    }

    private static string GetNodeText(ProjectNode n) => n.Kind switch {
        NodeKind.Solution => $"[solution] {n.Name}",
        NodeKind.Project => $"[project] {n.Name}",
        NodeKind.Folder => $"[folder] {n.Name}",
        _ => n.Name
    };

    private static string GetImageKey(NodeKind kind) => kind switch {
        NodeKind.Solution => "solution",
        NodeKind.Project => "project",
        NodeKind.Folder => "folder",
        _ => "file"
    };

    private void SetupTreeIcons() {
        _treeImages.ColorDepth = ColorDepth.Depth32Bit;
        _treeImages.ImageSize = new Size(16, 16);

        _treeImages.Images.Clear();
        _treeImages.Images.Add("solution", CreateBadgeIcon("S", Color.FromArgb(63, 95, 168)));
        _treeImages.Images.Add("project", CreateBadgeIcon("P", Color.FromArgb(73, 126, 87)));
        _treeImages.Images.Add("folder", CreateBadgeIcon("D", Color.FromArgb(166, 124, 47)));
        _treeImages.Images.Add("file", CreateBadgeIcon("C", Color.FromArgb(118, 92, 172)));
    }

    private static Bitmap CreateBadgeIcon(string text, Color bg) {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);

        using var b = new SolidBrush(bg);
        g.FillRectangle(b, 0, 0, 15, 15);

        using var border = new Pen(Color.FromArgb(30, 30, 30));
        g.DrawRectangle(border, 0, 0, 15, 15);

        using var font = new Font("Segoe UI", 8f, FontStyle.Bold, GraphicsUnit.Pixel);
        var size = g.MeasureString(text, font);

        g.DrawString(
            text,
            font,
            Brushes.White,
            (16f - size.Width) / 2f,
            (16f - size.Height) / 2f - 1f);

        return bmp;
    }
}
