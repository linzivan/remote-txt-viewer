using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RemoteTxtViewer
{
    /// <summary>
    /// 资源管理器样式的远程文件选择器：左侧目录树（懒加载）、右侧详细列表、
    /// 顶部导航（后退/前进/上层/刷新 + 地址栏）。
    /// </summary>
    public class RemotePickerForm : Form
    {
        public string SelectedPath;

        private readonly AppConfig _cfg;
        private readonly float _scale;
        private Font _uiFont;

        private TextBox _txtPath;
        private TreeView _tree;
        private ListView _list;
        private Label _lblInfo;
        private Button _btnBack, _btnFwd, _btnUp, _btnRefresh, _btnOk, _btnCancel, _btnNew;
        private ImageList _icons;

        private SftpFetcher _fetcher;
        private readonly object _lock = new object();

        private string _current = "/";
        private readonly List<string> _hist = new List<string>();
        private int _histPos = -1;
        private bool _suppressTree;
        private bool _loading;
        private readonly Dictionary<string, List<DirEntry>> _cache = new Dictionary<string, List<DirEntry>>();

        private static readonly Color BgTop = Color.FromArgb(252, 253, 254);
        private static readonly Color BgPanel = Color.FromArgb(255, 255, 255);
        private static readonly Color Ink = Color.FromArgb(52, 58, 64);
        private static readonly Color InkDim = Color.FromArgb(126, 134, 142);
        private static readonly Color LineC = Color.FromArgb(226, 230, 235);

        public RemotePickerForm(AppConfig cfg, float scale, string startPath)
        {
            _cfg = cfg.Clone();
            _scale = scale;

            if (!string.IsNullOrEmpty(startPath))
            {
                string p = Normalize(startPath);
                int slash = p.LastIndexOf('/');
                if (slash > 0) _current = p.Substring(0, slash);
                else _current = "/";
            }

            Build();
        }

        private int S(int v) { return (int)Math.Round(v * _scale); }

        // ================= 构建界面 =================

        private void Build()
        {
            try { _uiFont = new Font("Microsoft YaHei UI", 9f * _scale); }
            catch { _uiFont = SystemFonts.DefaultFont; }

            AutoScaleMode = AutoScaleMode.None;
            Font = _uiFont;
            Text = "选择远程文件";
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = BgTop;
            ClientSize = new Size(S(780), S(520));
            MinimumSize = new Size(S(560), S(360));

            _icons = new ImageList();
            _icons.ImageSize = new Size(S(18), S(18));
            _icons.ColorDepth = ColorDepth.Depth32Bit;
            _icons.Images.Add(MakeFolderIcon(S(18)));
            _icons.Images.Add(MakeFileIcon(S(18)));

            // ---------- 顶部导航条 ----------
            TableLayoutPanel nav = new TableLayoutPanel();
            nav.Dock = DockStyle.Top;
            nav.Height = S(42);
            nav.ColumnCount = 2;
            nav.RowCount = 1;
            nav.Padding = new Padding(S(8), S(7), S(10), S(6));
            nav.BackColor = BgTop;
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(146)));
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            FlowLayoutPanel navBtns = new FlowLayoutPanel();
            navBtns.Dock = DockStyle.Fill;
            navBtns.Margin = new Padding(0);
            navBtns.FlowDirection = FlowDirection.LeftToRight;
            navBtns.WrapContents = false;

            _btnBack = NavButton("\u25C0", "后退");
            _btnFwd = NavButton("\u25B6", "前进");
            _btnUp = NavButton("\u25B2", "上一层");
            _btnRefresh = NavButton("\u27F3", "刷新");

            _btnBack.Click += delegate { GoHistory(-1); };
            _btnFwd.Click += delegate { GoHistory(1); };
            _btnUp.Click += delegate { GoUp(); };
            _btnRefresh.Click += delegate { RefreshCurrent(); };

            navBtns.Controls.Add(_btnBack);
            navBtns.Controls.Add(_btnFwd);
            navBtns.Controls.Add(_btnUp);
            navBtns.Controls.Add(_btnRefresh);

            _txtPath = new TextBox();
            _txtPath.Dock = DockStyle.Fill;
            _txtPath.BorderStyle = BorderStyle.FixedSingle;
            _txtPath.Margin = new Padding(S(6), S(1), 0, 0);
            _txtPath.BackColor = Color.White;
            _txtPath.ForeColor = Ink;
            _txtPath.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    Navigate(_txtPath.Text, true);
                }
            };

            nav.Controls.Add(navBtns, 0, 0);
            nav.Controls.Add(_txtPath, 1, 0);

            // ---------- 左树 + 右列表 ----------
            _tree = new TreeView();
            _tree.Dock = DockStyle.Fill;
            _tree.BorderStyle = BorderStyle.None;
            _tree.HideSelection = false;
            _tree.ShowLines = true;
            _tree.ShowPlusMinus = true;
            _tree.ShowRootLines = false;
            _tree.FullRowSelect = true;
            _tree.ItemHeight = S(22);
            _tree.BackColor = Color.FromArgb(241, 243, 246);   // 浅灰，和白色列表区分开
            _tree.ForeColor = Ink;
            _tree.ImageList = _icons;
            _tree.BeforeExpand += delegate(object s, TreeViewCancelEventArgs e) { EnsureTreeChildren(e.Node); };
            _tree.AfterSelect += delegate(object s, TreeViewEventArgs e)
            {
                if (_suppressTree) return;
                string p = e.Node.Tag as string;
                if (!string.IsNullOrEmpty(p)) Navigate(p, true);
            };

            TreeNode root = new TreeNode("/", 0, 0);
            root.Tag = "/";
            root.Nodes.Add(Placeholder());
            _tree.Nodes.Add(root);
            _tree.SelectedNode = root;

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.BorderStyle = BorderStyle.None;
            _list.HideSelection = false;
            _list.SmallImageList = _icons;
            _list.BackColor = BgPanel;
            _list.ForeColor = Ink;
            _list.Columns.Add("名称", S(330), HorizontalAlignment.Left);
            _list.Columns.Add("大小", S(90), HorizontalAlignment.Right);
            _list.Columns.Add("修改时间", S(150), HorizontalAlignment.Left);
            _list.DoubleClick += delegate
            {
                ListViewItem it = _list.SelectedItems.Count > 0 ? _list.SelectedItems[0] : null;
                if (it == null) return;
                DirEntry e = it.Tag as DirEntry;
                if (e == null) return;
                if (e.IsDir) Navigate(Combine(_current, e.Name), true);
                else Accept(e.Name);
            };
            _list.SelectedIndexChanged += delegate { UpdateButtons(); };

            // 用 Splitter 控件而不是 SplitContainer：后者在 AutoScaleMode.None +
            // 手写 ClientSize 的组合下，SplitterDistance 会在布局阶段被重置成默认值。
            Splitter sp = new Splitter();
            sp.Dock = DockStyle.Left;
            sp.Width = S(5);
            sp.MinSize = S(120);
            sp.MinExtra = S(220);
            sp.BackColor = Color.FromArgb(214, 219, 225);

            _tree.Dock = DockStyle.Left;
            _tree.Width = S(200);

            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.BackColor = BgPanel;
            body.Controls.Add(_list);    // Fill 先加（处于最底层）
            body.Controls.Add(sp);       // 再 Left
            body.Controls.Add(_tree);    // 最后 Left → 落在最左侧

            // ---------- 状态行 ----------
            _lblInfo = new Label();
            _lblInfo.Dock = DockStyle.Bottom;
            _lblInfo.Height = S(26);
            _lblInfo.TextAlign = ContentAlignment.MiddleLeft;
            _lblInfo.Padding = new Padding(S(12), 0, S(12), 0);
            _lblInfo.AutoEllipsis = true;
            _lblInfo.ForeColor = InkDim;
            _lblInfo.BackColor = BgTop;

            // ---------- 底部按钮 ----------
            FlowLayoutPanel bp = new FlowLayoutPanel();
            bp.Dock = DockStyle.Bottom;
            bp.Height = S(48);
            bp.FlowDirection = FlowDirection.RightToLeft;
            bp.Padding = new Padding(S(10), S(9), S(10), S(9));
            bp.BackColor = BgTop;

            _btnCancel = new Button();
            _btnCancel.Text = "取消";
            _btnCancel.Size = new Size(S(84), S(30));
            _btnCancel.Margin = new Padding(S(8), 0, 0, 0);
            _btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            _btnOk = new Button();
            _btnOk.Text = "选择";
            _btnOk.Size = new Size(S(84), S(30));
            _btnOk.Margin = new Padding(S(8), 0, 0, 0);
            _btnOk.Click += delegate { Confirm(); };

            _btnNew = new Button();
            _btnNew.Text = "新建文件...";
            _btnNew.Size = new Size(S(100), S(30));
            _btnNew.Margin = new Padding(S(8), 0, 0, 0);
            _btnNew.Click += delegate { NewRemoteFile(); };

            bp.Controls.Add(_btnCancel);
            bp.Controls.Add(_btnOk);
            bp.Controls.Add(_btnNew);

            Controls.Add(body);        // Fill 最底层
            Controls.Add(_lblInfo);
            Controls.Add(bp);
            Controls.Add(nav);

            CancelButton = _btnCancel;

            Shown += delegate
            {
                Navigate(_current, true);
                if (_tree.Nodes.Count > 0) _tree.Nodes[0].Expand();   // 展开根，一级目录直接可见
            };

            FormClosing += delegate
            {
                SftpFetcher f = _fetcher;
                _fetcher = null;
                if (f != null) { try { f.Dispose(); } catch { } }
            };
        }

        private Button NavButton(string glyph, string tip)
        {
            Button b = new Button();
            b.Text = glyph;
            b.Size = new Size(S(30), S(26));
            b.Margin = new Padding(0, 0, S(4), 0);
            b.FlatStyle = FlatStyle.System;
            try { b.Font = new Font("Segoe UI Symbol", 9f * _scale); } catch { }
            ToolTip tt = new ToolTip();
            tt.SetToolTip(b, tip);
            return b;
        }

        // ================= 图标 =================

        private static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2f;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static Image MakeFolderIcon(int s)
        {
            Bitmap bmp = new Bitmap(s, s);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float u = s / 18f;
                float lw = Math.Max(1f, 1.1f * u);

                // 单据形的"文件夹"：深色后页 + 亮色前盖，18px 下也能看出层次
                RectangleF back = new RectangleF(1.5f * u, 3f * u, 15f * u, 10f * u);
                RectangleF front = new RectangleF(1.5f * u, 6.5f * u, 15f * u, 9.5f * u);

                using (SolidBrush b0 = new SolidBrush(Color.FromArgb(226, 158, 32)))
                using (Pen p0 = new Pen(Color.FromArgb(178, 120, 16), lw))
                {
                    using (GraphicsPath gp = RoundRect(back, 2.2f * u)) { g.FillPath(b0, gp); g.DrawPath(p0, gp); }
                }
                using (SolidBrush b1 = new SolidBrush(Color.FromArgb(255, 199, 82)))
                using (Pen p1 = new Pen(Color.FromArgb(198, 136, 22), lw))
                {
                    using (GraphicsPath gp = RoundRect(front, 2.2f * u)) { g.FillPath(b1, gp); g.DrawPath(p1, gp); }
                }
                // 前盖上方一道浅色高光，避免看起来像纯色块
                using (Pen ph = new Pen(Color.FromArgb(255, 226, 160), Math.Max(1f, 1.2f * u)))
                {
                    g.DrawLine(ph, 4f * u, 8.2f * u, 14f * u, 8.2f * u);
                }
            }
            return bmp;
        }

        private static Image MakeFileIcon(int s)
        {
            Bitmap bmp = new Bitmap(s, s);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float u = s / 18f;
                float lw = Math.Max(1f, 1.1f * u);

                PointF[] pts = new PointF[]
                {
                    new PointF(3.5f * u, 2f * u),
                    new PointF(11f * u, 2f * u),
                    new PointF(14.5f * u, 5.5f * u),
                    new PointF(14.5f * u, 16f * u),
                    new PointF(3.5f * u, 16f * u)
                };

                using (SolidBrush b = new SolidBrush(Color.White))
                using (Pen p = new Pen(Color.FromArgb(158, 168, 180), lw))
                {
                    g.FillPolygon(b, pts);
                    g.DrawPolygon(p, pts);
                }
                using (Pen p = new Pen(Color.FromArgb(178, 188, 200), lw))
                {
                    g.DrawLine(p, 5.5f * u, 8f * u, 12.5f * u, 8f * u);
                    g.DrawLine(p, 5.5f * u, 10.5f * u, 12.5f * u, 10.5f * u);
                    g.DrawLine(p, 5.5f * u, 13f * u, 10f * u, 13f * u);
                }
            }
            return bmp;
        }

        // ================= 导航 =================

        private static string Normalize(string p)
        {
            if (string.IsNullOrEmpty(p)) return "/";
            p = p.Trim();
            if (p.Length == 0) return "/";
            while (p.Length > 1 && p.EndsWith("/")) p = p.Substring(0, p.Length - 1);
            if (!p.StartsWith("/")) p = "/" + p;
            return p;
        }

        private static string Combine(string dir, string name)
        {
            if (string.IsNullOrEmpty(dir)) dir = "/";
            if (dir.EndsWith("/")) return dir + name;
            return dir + "/" + name;
        }

        private void Navigate(string path, bool record)
        {
            string p = Normalize(path);
            if (p.Length == 0) p = "/";

            if (record)
            {
                if (_histPos >= 0 && _histPos < _hist.Count - 1)
                    _hist.RemoveRange(_histPos + 1, _hist.Count - _histPos - 1);
                if (_hist.Count == 0 || _hist[_hist.Count - 1] != p) _hist.Add(p);
                _histPos = _hist.Count - 1;
            }

            LoadDir(p);
        }

        private void GoHistory(int delta)
        {
            int np = _histPos + delta;
            if (np < 0 || np >= _hist.Count) return;
            _histPos = np;
            LoadDir(_hist[np]);
        }

        private void GoUp()
        {
            string p = _current.TrimEnd('/');
            if (p.Length == 0) return;
            int i = p.LastIndexOf('/');
            Navigate(i <= 0 ? "/" : p.Substring(0, i), true);
        }

        private void RefreshCurrent()
        {
            _cache.Remove(_current);
            _tree.Nodes.Clear();
            TreeNode root = new TreeNode("/", 0, 0);
            root.Tag = "/";
            root.Nodes.Add(Placeholder());
            _tree.Nodes.Add(root);
            _tree.SelectedNode = root;
            LoadDir(_current);
        }

        private void LoadDir(string path)
        {
            path = Normalize(path);

            List<DirEntry> cached;
            if (_cache.TryGetValue(path, out cached))
            {
                ApplyListing(path, cached, null);
                return;
            }

            _loading = true;
            _lblInfo.ForeColor = InkDim;
            _lblInfo.Text = "正在读取 " + path + " …";
            _list.Items.Clear();
            UpdateButtons();

            ListDirAsync(path, delegate(List<DirEntry> entries, string err) { ApplyListing(path, entries, err); });
        }

        private void ApplyListing(string path, List<DirEntry> entries, string err)
        {
            _loading = false;
            if (IsDisposed) return;

            if (err != null)
            {
                _lblInfo.ForeColor = Color.FromArgb(198, 40, 40);
                _lblInfo.Text = err;
                UpdateButtons();
                return;
            }

            _current = path;
            _cache[path] = entries;
            _txtPath.Text = path;

            FillList(entries);
            SyncTree(path);

            int dirs = 0;
            for (int i = 0; i < entries.Count; i++) if (entries[i].IsDir) dirs++;
            _lblInfo.ForeColor = InkDim;
            _lblInfo.Text = path + "   ·   " + dirs + " 个文件夹，" + (entries.Count - dirs) + " 个文件";
            UpdateButtons();
        }

        private void FillList(List<DirEntry> entries)
        {
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                for (int i = 0; i < entries.Count; i++)
                {
                    DirEntry e = entries[i];
                    ListViewItem it = new ListViewItem(e.Name, e.IsDir ? 0 : 1);
                    it.SubItems.Add(e.IsDir ? "" : FmtSize(e.Size));
                    it.SubItems.Add(e.ModifiedUtc == DateTime.MinValue
                        ? "" : e.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                    it.Tag = e;
                    _list.Items.Add(it);
                }
            }
            finally { _list.EndUpdate(); }

            if (_list.Items.Count > 0) _list.Items[0].Selected = false;
        }

        private void UpdateButtons()
        {
            _btnBack.Enabled = !_loading && _histPos > 0;
            _btnFwd.Enabled = !_loading && _histPos >= 0 && _histPos < _hist.Count - 1;
            _btnUp.Enabled = !_loading && _current != "/";
            _btnRefresh.Enabled = !_loading;
        }

        // ================= 目录树 =================

        private static TreeNode Placeholder()
        {
            TreeNode t = new TreeNode("…");
            t.Tag = "placeholder";
            return t;
        }

        private void EnsureTreeChildren(TreeNode node)
        {
            if (node == null) return;
            if (node.Nodes.Count != 1) return;
            TreeNode ph = node.Nodes[0];
            if (!(ph.Tag is string) || (string)ph.Tag != "placeholder") return;

            string path = node.Tag as string;
            if (string.IsNullOrEmpty(path)) return;

            ListDirAsync(path, delegate(List<DirEntry> entries, string err)
            {
                if (IsDisposed || node.TreeView == null) return;
                node.Nodes.Clear();
                if (err != null || entries == null) return;

                for (int i = 0; i < entries.Count; i++)
                {
                    DirEntry e = entries[i];
                    if (!e.IsDir) continue;
                    string child = Combine(path, e.Name);
                    TreeNode cn = new TreeNode(e.Name, 0, 0);
                    cn.Tag = child;
                    cn.Nodes.Add(Placeholder());
                    node.Nodes.Add(cn);
                }
            });
        }

        private void SyncTree(string path)
        {
            if (path == "/")
            {
                if (_tree.Nodes.Count > 0) SetTreeSelection(_tree.Nodes[0]);
                return;
            }

            _suppressTree = true;
            try
            {
                string[] parts = path.Trim('/').Split('/');
                TreeNode cur = _tree.Nodes.Count > 0 ? _tree.Nodes[0] : null;
                if (cur == null) return;

                string acc = "/";
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].Length == 0) continue;

                    EnsureTreeChildren(cur);
                    TreeNode found = null;
                    for (int k = 0; k < cur.Nodes.Count; k++)
                    {
                        string cp = cur.Nodes[k].Tag as string;
                        if (cp != null && cp == (acc == "/" ? "/" + parts[i] : acc + "/" + parts[i]))
                        {
                            found = cur.Nodes[k];
                            break;
                        }
                    }
                    if (found == null) return;

                    acc = found.Tag as string;
                    found.Expand();
                    cur = found;
                }

                SetTreeSelection(cur);
            }
            finally { _suppressTree = false; }
        }

        private void SetTreeSelection(TreeNode node)
        {
            try { _tree.SelectedNode = node; } catch { }
        }

        // ================= 异步列目录 =================

        private void ListDirAsync(string path, Action<List<DirEntry>, string> done)
        {
            string target = path;

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<DirEntry> entries = null;
                string err = null;

                lock (_lock)
                {
                    try
                    {
                        if (_fetcher == null) _fetcher = new SftpFetcher(_cfg);
                        entries = _fetcher.ListDir(target);
                    }
                    catch (Exception ex)
                    {
                        err = SftpFetcher.DescribeError(ex);
                        if (_fetcher != null) { try { _fetcher.Dispose(); } catch { } _fetcher = null; }
                    }
                }

                try
                {
                    BeginInvoke(new Action(delegate { if (!IsDisposed) done(entries, err); }));
                }
                catch { }
            });
        }

        // ================= 动作 =================

        private void Confirm()
        {
            ListViewItem it = _list.SelectedItems.Count > 0 ? _list.SelectedItems[0] : null;
            if (it != null)
            {
                DirEntry e = it.Tag as DirEntry;
                if (e != null && !e.IsDir) { Accept(e.Name); return; }
            }

            string typed = Normalize(_txtPath.Text.Trim());
            if (typed.Length > 0 && typed != _current)
            {
                SelectedPath = typed;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            _lblInfo.ForeColor = Color.FromArgb(198, 40, 40);
            _lblInfo.Text = "请选中一个文件，或在上方地址栏输入完整路径。";
        }

        private void Accept(string fileName)
        {
            SelectedPath = Combine(_current, fileName);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void NewRemoteFile()
        {
            using (PromptForm pf = new PromptForm("新建文件", "文件名（例如 notes.txt）：", "", _scale))
            {
                if (pf.ShowDialog(this) != DialogResult.OK) return;

                string name = pf.Value == null ? "" : pf.Value.Trim();
                if (name.Length == 0) return;

                if (name.IndexOf('/') >= 0)
                {
                    MessageBox.Show(this, "只需填文件名，不要带路径。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                SelectedPath = Combine(_current, name);
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private static string FmtSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
            return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.00") + " GB";
        }
    }
}
