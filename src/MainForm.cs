using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace RemoteTxtViewer
{
    public class MainForm : Form
    {
        // ==================== 字段 ====================

        private SessionStore _store;
        private List<MonitorSession> _sessions = new List<MonitorSession>();
        private MonitorSession _cur;
        private readonly FavoritesStore _favs = new FavoritesStore();
        // 非活动标签只做「轻量探测」：定期 stat 比较大小/时间，发现变了就打红点，
        // 不下载内容、不解析、不写时间线。切回该标签时才完整读一次。
        private const int InactiveProbeSec = 20;

        private float _scale = 1f;
        private Font _uiFont;
        private Font _contentFont;
        private Font _monoFont;
        private Font _gutterFont;
        private System.Windows.Forms.Timer _timer;
        private bool _closing;
        private bool _suppressTab;

        // ---- 工具栏 ----
        private ToolStrip _ts;
        private ToolStripButton _btnNew;
        private ToolStripButton _btnSettings;
        private ToolStripButton _btnRefresh;
        private ToolStripButton _btnEdit;
        private ToolStripButton _btnSave;
        private ToolStripButton _chkAuto;
        private ToolStripButton _chkFollow;
        private ToolStripButton _chkWrap;
        private ToolStripButton _chkPane;
        private ToolStripButton _btnFav;
        private ToolStripButton _btnFavList;
        private ToolStripButton _btnFontMinus;
        private ToolStripButton _btnFontPlus;
        private ToolStripComboBox _cmbInterval;
        private ToolStripLabel _lblHeader;

        // ---- 标签 ----
        private TabControl _tabs;

        // ---- 正文 ----
        private Panel _host;
        private RichTextBox _rtb;
        private Panel _gutter;
        private int _gutterDigits = 3;

        // ---- 时间线 ----
        private Panel _pane;
        private Label _paneTitle;
        private ListBox _paneList;
        private bool _suppressPaneSelect;

        // ---- 状态栏 ----
        private StatusStrip _st;
        private ToolStripStatusLabel _lblStatus;
        private ToolStripStatusLabel _lblTime;
        private ToolStripStatusLabel _lblChanged;
        private ToolStripStatusLabel _lblSize;
        private ToolStripStatusLabel _lblNew;
        private ToolStripStatusLabel _lblRight;

        private static readonly int[] Intervals = new int[] { 1, 2, 3, 5, 10, 30, 60 };

        private class EntryItem
        {
            public ChangeEntry Entry;
            public string Label;
            public EntryItem(ChangeEntry e, string label) { Entry = e; Label = label; }
            public override string ToString() { return Label; }
        }

        // 滚动位置（RichTextBox 没有托管 API）
        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessagePt(IntPtr hWnd, int msg, IntPtr wParam, ref Point lParam);
        private const int EM_GETSCROLLPOS = 0x04DD;
        private const int EM_SETSCROLLPOS = 0x04DE;

        // ==================== 构造 ====================

        public MainForm()
        {
            _store = SessionStore.Load();

            InitScale();
            CreateFonts();
            BuildUi();

            for (int i = 0; i < _store.Items.Count; i++)
                _sessions.Add(new MonitorSession(_store.BuildConn(i), _store.Items[i]));

            RebuildTabs();

            ApplyGlobalToUi();
            RestoreWindowBounds();

            if (_sessions.Count > 0)
            {
                int idx = _store.ActiveIndex;
                if (idx < 0 || idx >= _sessions.Count) idx = 0;
                _tabs.SelectedIndex = idx;
                OnTabChanged();
            }

            Shown += OnShown;
            FormClosing += OnFormClosing;
        }

        // ==================== 基础 ====================

        private void InitScale()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    _scale = g.DpiX / 96f;
            }
            catch { _scale = 1f; }
            if (_scale < 1f) _scale = 1f;
            if (_scale > 4f) _scale = 4f;
        }

        private int S(int v) { return (int)Math.Round(v * _scale); }

        private float SF(float v) { return v * _scale; }

        private Font SafeFont(string family, float size, FontStyle style)
        {
            try { return new Font(family, SF(size), style); }
            catch
            {
                try { return new Font(FontFamily.GenericMonospace, SF(size), style); }
                catch { return new Font(FontFamily.GenericSansSerif, SF(size), style); }
            }
        }

        private void CreateFonts()
        {
            float fs = _store.Ui.FontSize;
            if (fs < 7f) fs = 10f;
            _uiFont = SafeFont("Microsoft YaHei UI", 9f, FontStyle.Regular);
            _contentFont = SafeFont("Consolas", fs, FontStyle.Regular);
            _monoFont = SafeFont("Consolas", 9f, FontStyle.Regular);
            _gutterFont = SafeFont("Consolas", Math.Max(7f, fs - 1f), FontStyle.Regular);
        }

        // ==================== 界面 ====================

        private void BuildUi()
        {
            SuspendLayout();

            AutoScaleMode = AutoScaleMode.None;
            Text = "远程文本监视器";
            Font = _uiFont;
            BackColor = Color.FromArgb(245, 246, 249);
            ClientSize = new Size(S(1180), S(700));
            MinimumSize = new Size(S(760), S(360));
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            BuildToolbar();
            BuildTabs();
            BuildContent();
            BuildPane();
            BuildStatus();

            Controls.Add(_host);
            Controls.Add(_pane);
            Controls.Add(_tabs);
            Controls.Add(_ts);
            Controls.Add(_st);

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1000;
            _timer.Tick += delegate { OnTimerTick(); };

            ResumeLayout(true);
        }

        private ToolStripButton MakeButton(string text, string tip)
        {
            ToolStripButton b = new ToolStripButton(text);
            b.DisplayStyle = ToolStripItemDisplayStyle.Text;
            b.ToolTipText = tip;
            b.Font = _uiFont;
            b.AutoSize = true;
            b.Padding = new Padding(S(3), 0, S(3), 0);
            return b;
        }

        private void BuildToolbar()
        {
            _ts = new ToolStrip();
            _ts.GripStyle = ToolStripGripStyle.Hidden;
            _ts.RenderMode = ToolStripRenderMode.System;
            _ts.Font = _uiFont;
            _ts.Padding = new Padding(S(6), S(3), S(6), S(3));
            _ts.Dock = DockStyle.Top;

            _btnNew = MakeButton("新建", "新建一个监控项 (Ctrl+T)");
            _btnNew.Click += delegate { NewSession(); };

            _btnSettings = MakeButton("设置", "修改当前监控项的连接信息 (Ctrl+O)");
            _btnSettings.Click += delegate { EditCurrentSettings(); };

            _btnRefresh = MakeButton("刷新", "立即刷新一次 (F5)");
            _btnRefresh.Click += delegate { if (_cur != null) ScheduleRefresh(_cur, true); };

            _btnEdit = MakeButton("编辑", "切换编辑模式 (Ctrl+E)");
            _btnEdit.CheckOnClick = true;
            _btnEdit.CheckedChanged += delegate { OnEditToggle(); };

            _btnSave = MakeButton("保存", "保存到服务器 (Ctrl+S)");
            _btnSave.Enabled = false;
            _btnSave.Click += delegate { SaveEdit(); };

            _chkAuto = MakeButton("自动刷新", "按间隔自动获取最新内容");
            _chkAuto.CheckOnClick = true;
            _chkAuto.Checked = true;
            _chkAuto.CheckedChanged += delegate { ApplyAutoRefresh(); };

            _cmbInterval = new ToolStripComboBox();
            _cmbInterval.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbInterval.Font = _uiFont;
            _cmbInterval.AutoSize = false;
            _cmbInterval.Width = S(74);
            _cmbInterval.SelectedIndexChanged += delegate { OnIntervalChanged(); };

            _chkFollow = MakeButton("跟随末尾", "有新内容时自动滚到最新一行");
            _chkFollow.CheckOnClick = true;
            _chkFollow.Checked = true;
            _chkFollow.CheckedChanged += delegate
            {
                _store.Ui.Follow = _chkFollow.Checked;
                _store.Save();
            };

            _chkWrap = MakeButton("自动换行", "长行折行显示");
            _chkWrap.CheckOnClick = true;
            _chkWrap.CheckedChanged += delegate { ApplyWrap(); };

            _chkPane = MakeButton("时间线", "显示/隐藏右侧变更时间线 (Ctrl+H)");
            _chkPane.CheckOnClick = true;
            _chkPane.CheckedChanged += delegate
            {
                _pane.Visible = _chkPane.Checked;
                _store.Ui.ShowTimeline = _chkPane.Checked;
                _store.Save();
            };

            _btnFav = MakeButton("收藏", "把当前内容存进收藏夹 (Ctrl+D)");
            _btnFav.Click += delegate { AddFavorite(); };

            _btnFavList = MakeButton("收藏夹", "查看/管理收藏 (Ctrl+B)");
            _btnFavList.Click += delegate { OpenFavorites(); };

            _btnFontMinus = MakeButton("A-", "缩小字号");
            _btnFontMinus.Click += delegate { ChangeFontSize(-1); };

            _btnFontPlus = MakeButton("A+", "放大字号");
            _btnFontPlus.Click += delegate { ChangeFontSize(1); };

            _lblHeader = new ToolStripLabel("");
            _lblHeader.Alignment = ToolStripItemAlignment.Right;
            _lblHeader.ForeColor = Color.FromArgb(112, 118, 128);
            _lblHeader.Font = _uiFont;

            _ts.Items.AddRange(new ToolStripItem[]
            {
                _btnNew,
                _btnSettings,
                new ToolStripSeparator(),
                _btnRefresh,
                _btnEdit,
                _btnSave,
                new ToolStripSeparator(),
                _chkAuto,
                _cmbInterval,
                new ToolStripSeparator(),
                _chkFollow,
                _chkWrap,
                _chkPane,
                new ToolStripSeparator(),
                _btnFav,
                _btnFavList,
                new ToolStripSeparator(),
                _btnFontMinus,
                _btnFontPlus,
                _lblHeader
            });
        }

        private void BuildTabs()
        {
            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Top;
            _tabs.Height = S(28);
            _tabs.Font = _uiFont;
            _tabs.Padding = new Point(S(12), S(4));
            _tabs.SelectedIndexChanged += delegate { OnTabChanged(); };
            _tabs.MouseUp += OnTabsMouseUp;
        }

        private void BuildContent()
        {
            _host = new Panel();
            _host.Dock = DockStyle.Fill;
            _host.BackColor = Color.White;
            _host.Padding = new Padding(S(4), S(4), S(4), S(4));

            _rtb = new RichTextBox();
            _rtb.Dock = DockStyle.Fill;
            _rtb.BorderStyle = BorderStyle.None;
            _rtb.ReadOnly = true;
            _rtb.WordWrap = false;
            _rtb.ScrollBars = RichTextBoxScrollBars.Both;
            _rtb.DetectUrls = false;
            _rtb.HideSelection = false;
            _rtb.ShowSelectionMargin = false;
            _rtb.BackColor = Color.White;
            _rtb.ForeColor = Color.FromArgb(28, 32, 38);
            _rtb.Font = _contentFont;
            _rtb.ShortcutsEnabled = true;

            ContextMenuStrip cm = new ContextMenuStrip();
            cm.Font = _uiFont;
            cm.Items.Add("复制 (&C)", null, delegate { if (_rtb.SelectionLength > 0) _rtb.Copy(); });
            cm.Items.Add("全选 (&A)", null, delegate { _rtb.SelectAll(); });
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add("收藏当前内容... (&D)", null, delegate { AddFavorite(); });
            cm.Items.Add("另存为本地文件...", null, delegate { SaveSnapshotLocal(); });
            _rtb.ContextMenuStrip = cm;

            _gutter = new Panel();
            _gutter.Dock = DockStyle.Left;
            _gutter.Width = S(38);
            _gutter.BackColor = Color.FromArgb(248, 249, 251);
            _gutter.Paint += OnGutterPaint;

            _rtb.VScroll += delegate { if (_gutter != null) _gutter.Invalidate(); };
            _rtb.TextChanged += delegate
            {
                if (_gutter == null) return;
                UpdateGutterWidth();
                _gutter.Invalidate();
            };
            _rtb.Resize += delegate { if (_gutter != null) _gutter.Invalidate(); };
            _rtb.FontChanged += delegate { if (_gutter != null) _gutter.Invalidate(); };

            _host.Controls.Add(_rtb);
            _host.Controls.Add(_gutter);
        }

        private void BuildPane()
        {
            _pane = new Panel();
            _pane.Dock = DockStyle.Right;
            _pane.Width = S(232);
            _pane.BackColor = Color.FromArgb(247, 248, 250);
            _pane.Padding = new Padding(S(1), 0, 0, 0);

            _paneTitle = new Label();
            _paneTitle.Dock = DockStyle.Top;
            _paneTitle.Height = S(32);
            _paneTitle.Text = "变更时间线 · 近 24 小时";
            _paneTitle.TextAlign = ContentAlignment.MiddleLeft;
            _paneTitle.Padding = new Padding(S(10), 0, 0, 0);
            _paneTitle.ForeColor = Color.FromArgb(92, 98, 108);
            _paneTitle.BackColor = Color.FromArgb(240, 242, 246);

            Button back = new Button();
            back.Text = "返回实时";
            back.Dock = DockStyle.Bottom;
            back.Height = S(32);
            back.FlatStyle = FlatStyle.System;
            back.Click += delegate { ReturnToLive(); };

            _paneList = new ListBox();
            _paneList.Dock = DockStyle.Fill;
            _paneList.BorderStyle = BorderStyle.None;
            _paneList.Font = _monoFont;
            _paneList.IntegralHeight = false;
            _paneList.BackColor = Color.FromArgb(247, 248, 250);
            _paneList.SelectedIndexChanged += delegate { OnPaneSelect(); };

            _pane.Controls.Add(_paneList);
            _pane.Controls.Add(back);
            _pane.Controls.Add(_paneTitle);
        }

        private void BuildStatus()
        {
            _st = new StatusStrip();
            _st.Font = _uiFont;
            _st.SizingGrip = false;
            _st.ShowItemToolTips = true;
            _st.Dock = DockStyle.Bottom;

            _lblStatus = new ToolStripStatusLabel("● 未连接");
            _lblStatus.ForeColor = Color.FromArgb(150, 150, 155);
            _lblStatus.Click += delegate { ReturnToLive(); };

            _lblTime = new ToolStripStatusLabel("");
            _lblChanged = new ToolStripStatusLabel("");
            _lblChanged.ForeColor = Color.FromArgb(90, 96, 106);
            _lblSize = new ToolStripStatusLabel("");
            _lblNew = new ToolStripStatusLabel("");
            _lblNew.ForeColor = Color.FromArgb(190, 108, 0);

            ToolStripStatusLabel spring = new ToolStripStatusLabel();
            spring.Spring = true;

            _lblRight = new ToolStripStatusLabel("");
            _lblRight.ForeColor = Color.FromArgb(140, 146, 156);
            _lblRight.Margin = new Padding(0, 0, S(10), 0);

            _st.Items.AddRange(new ToolStripItem[]
            {
                _lblStatus,
                new ToolStripStatusLabel("   "),
                _lblTime,
                new ToolStripStatusLabel("   "),
                _lblChanged,
                new ToolStripStatusLabel("   "),
                _lblSize,
                new ToolStripStatusLabel("   "),
                _lblNew,
                spring,
                _lblRight
            });
        }

        private void ApplyGlobalToUi()
        {
            _chkFollow.Checked = _store.Ui.Follow;
            _chkWrap.Checked = _store.Ui.WordWrap;
            _chkPane.Checked = _store.Ui.ShowTimeline;
            _pane.Visible = _store.Ui.ShowTimeline;
            ApplyWrap();
            FillIntervals();
            _rtb.Font = _contentFont;
            _gutterDigits = 0;
            UpdateGutterWidth();
        }

        private bool _suppressInterval;

        private void FillIntervals()
        {
            _suppressInterval = true;
            _cmbInterval.Items.Clear();
            int want = _cur != null ? _cur.Conn.IntervalSec : 3;

            bool found = false;
            for (int i = 0; i < Intervals.Length; i++)
            {
                _cmbInterval.Items.Add(Intervals[i] + " 秒");
                if (Intervals[i] == want) { _cmbInterval.SelectedIndex = i; found = true; }
            }
            if (!found)
            {
                _cmbInterval.Items.Add(want + " 秒");
                _cmbInterval.SelectedIndex = _cmbInterval.Items.Count - 1;
            }
            _suppressInterval = false;
        }

        private void ApplyWrap()
        {
            bool wrap = _chkWrap.Checked;
            _rtb.WordWrap = wrap;
            _rtb.ScrollBars = wrap ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both;
            _store.Ui.WordWrap = wrap;
        }

        private void OnIntervalChanged()
        {
            if (_suppressInterval || _cur == null) return;
            string txt = _cmbInterval.Text;
            int sp = txt.IndexOf(' ');
            if (sp > 0) txt = txt.Substring(0, sp);
            int sec;
            if (!int.TryParse(txt, out sec) || sec < 1) return;
            _cur.Conn.IntervalSec = sec;
            if (_cur.Item != null) _cur.Item.IntervalSec = sec;
            _store.Save();
        }

        private void ApplyAutoRefresh()
        {
            if (_chkAuto.Checked) _timer.Start();
            else _timer.Stop();
        }

        private void RestoreWindowBounds()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            AppConfig ui = _store.Ui;

            bool hasSaved = (ui.WinW > 200 && ui.WinH > 150);
            int w = hasSaved ? ui.WinW : S(1180);
            int h = hasSaved ? ui.WinH : S(700);

            if (w > wa.Width) w = wa.Width - S(40);
            if (h > wa.Height) h = wa.Height - S(40);
            if (w < S(760)) w = Math.Min(S(760), wa.Width);
            if (h < S(360)) h = Math.Min(S(360), wa.Height);

            int x = wa.Left + (wa.Width - w) / 2;
            int y = wa.Top + (wa.Height - h) / 2;

            if (ui.WinX >= -10000 && ui.WinY >= -10000 && hasSaved)
            {
                Rectangle r = new Rectangle(ui.WinX, ui.WinY, w, h);
                for (int i = 0; i < Screen.AllScreens.Length; i++)
                {
                    Rectangle inter = Rectangle.Intersect(Screen.AllScreens[i].WorkingArea, r);
                    if (inter.Width >= S(120) && inter.Height >= S(60)) { x = ui.WinX; y = ui.WinY; break; }
                }
            }

            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(x, y, w, h);

            if (ui.WinMax) WindowState = FormWindowState.Maximized;
        }

        // ==================== 会话（标签）管理 ====================

        private void RebuildTabs()
        {
            _suppressTab = true;
            _tabs.TabPages.Clear();
            for (int i = 0; i < _sessions.Count; i++)
            {
                TabPage tp = new TabPage(_sessions[i].Title);
                tp.ToolTipText = _sessions[i].Tooltip;
                _tabs.TabPages.Add(tp);
            }
            _suppressTab = false;
        }

        private void OnTabChanged()
        {
            if (_suppressTab) return;
            int idx = _tabs.SelectedIndex;
            if (idx < 0 || idx >= _sessions.Count) return;

            MonitorSession next = _sessions[idx];
            if (ReferenceEquals(next, _cur)) return;

            if (_cur != null) SaveUiState(_cur);

            _cur = next;
            _cur.HasUnseenChange = false;
            _store.ActiveIndex = idx;
            _store.Save();

            RestoreUiState(_cur);
            RefreshTimeline();
            FillIntervals();
            UpdateTabTitle(_cur);

            if (_cur.Conn != null && _cur.Conn.IsReady)
                ScheduleRefresh(_cur, true);       // 切过去立刻拉一次
        }

        private void SaveUiState(MonitorSession s)
        {
            if (s == null) return;
            try
            {
                s.ScrollPos = GetScrollY();
                s.SelectionStart = _rtb.SelectionStart;
            }
            catch { }
        }

        private void RestoreUiState(MonitorSession s)
        {
            if (s == null)
            {
                _rtb.Clear();
                _rtb.ReadOnly = true;
                SetContentBackColor(Color.White, Color.FromArgb(248, 249, 251));
                _lblHeader.Text = "";
                _lblRight.Text = "";
                SetStatus("● 没有监控项 — 点「新建」", Color.FromArgb(150, 150, 155));
                return;
            }

            _lblHeader.Text = s.Conn.DisplayName;
            _lblRight.Text = s.Conn.DisplayName;
            _lblRight.ForeColor = Color.FromArgb(140, 146, 156);
            _lblHeader.ToolTipText = s.Tooltip;

            // 编辑态优先：未保存的内容必须原样恢复
            if (s.Editing)
            {
                _rtb.ReadOnly = false;
                _btnSave.Enabled = true;
                SetContentBackColor(Color.FromArgb(255, 253, 244), Color.FromArgb(251, 247, 235));
                _rtb.Text = s.LastText;
                _suppressTabSet(_btnEdit, true);
                SetStatus("✎ 编辑中 · 未保存", Color.FromArgb(176, 100, 20));
            }
            else
            {
                _rtb.ReadOnly = true;
                _btnSave.Enabled = false;
                _suppressTabSet(_btnEdit, false);
                SetContentBackColor(Color.White, Color.FromArgb(248, 249, 251));
                _rtb.Text = s.LastText;

                if (s.ViewingEntry != null)
                    ApplyHistoryUi(s.ViewingEntry);      // 恢复"正在看某个历史快照"的状态
                else if (s.PinnedLabel.Length > 0)
                    _lblStatus.Text = "★ 收藏：" + s.PinnedLabel + " · 点此返回实时";
                else
                {
                    SetStatus(s.HasLoaded ? "● 已连接" : "● 连接中…",
                              s.HasLoaded ? Color.FromArgb(22, 130, 62) : Color.FromArgb(176, 122, 20));
                    _lblNew.Text = "";
                }
            }

            // 恢复滚动位置
            try
            {
                int y = s.ScrollPos;
                if (y > 0) SetScrollY(y);
            }
            catch { }

            UpdateStatusFromSession(s);
        }

        private void _suppressTabSet(ToolStripButton btn, bool value)
        {
            // 防止程序性设置 Checked 触发用户的切换逻辑
            bool old = _suppressEditToggle;
            _suppressEditToggle = true;
            btn.Checked = value;
            _suppressEditToggle = old;
        }

        private void UpdateStatusFromSession(MonitorSession s)
        {
            if (s == null) return;
            if (s.LastSnap != null)
            {
                _lblChanged.Text = "更改 " + FormatMtime(s.LastSnap.ModifiedUtc);
                UpdateSizeLabel(s.LastText, s.LastSnap.Size);
            }
            else
            {
                _lblChanged.Text = "";
                UpdateSizeLabel(s.LastText, Encoding.UTF8.GetByteCount(s.LastText));
            }
            if (s.LastError != null && s == _cur)
            {
                SetStatus("● 连接失败", Color.FromArgb(198, 40, 40));
                _lblStatus.ToolTipText = s.LastError;
            }
        }

        private void UpdateTabTitle(MonitorSession s)
        {
            int idx = _sessions.IndexOf(s);
            if (idx < 0 || idx >= _tabs.TabPages.Count) return;

            string t = s.Title;
            if (s != _cur && s.HasUnseenChange) t = "● " + t;   // 后台文件有变化（仅轻量探测得知）
            if (s.HasUnsavedEdit) t = t + " *";
            if (_tabs.TabPages[idx].Text != t) _tabs.TabPages[idx].Text = t;
        }

        // ---------- 新建 / 关闭 ----------

        /// <summary>新建监控项：直接在这台服务器上选文件（或新建文件），不再让人重填服务器。</summary>
        private void NewSession()
        {
            if (!_store.HasServer)
            {
                if (MessageBox.Show(this, "还没有配置服务器连接。\r\n\r\n先去配置吗？", "新建监控项",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                    return;
                EditServerSettings();
                if (!_store.HasServer) return;
            }

            // 起点：当前文件所在目录（没有当前文件就从根目录）
            string startDir = _cur == null ? "/" : DirOf(_cur.Conn.RemotePath);

            AppConfig probeItem = new AppConfig();
            probeItem.RemotePath = startDir;
            AppConfig probe = _store.BuildConn(probeItem);

            using (RemotePickerForm dlg = new RemotePickerForm(probe, _scale, startDir))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (string.IsNullOrEmpty(dlg.SelectedPath)) return;
                AddSessionForPath(dlg.SelectedPath);
            }
        }

        /// <summary>为指定远程路径加一个标签；已存在则不重复添加。</summary>
        private void AddSessionForPath(string remotePath)
        {
            for (int i = 0; i < _sessions.Count; i++)
            {
                if (_sessions[i].Conn.RemotePath == remotePath)
                {
                    _tabs.SelectedIndex = i;
                    return;
                }
            }

            AppConfig item = new AppConfig();
            item.RemotePath = remotePath;
            item.IntervalSec = _cur != null && _cur.Conn.IntervalSec > 0 ? _cur.Conn.IntervalSec : 3;
            item.EncodingPref = (_cur != null && !string.IsNullOrEmpty(_cur.Conn.EncodingPref))
                                ? _cur.Conn.EncodingPref : "auto";

            AppConfig conn = _store.BuildConn(item);
            MonitorSession s = new MonitorSession(conn, item);

            _sessions.Add(s);
            _store.Items.Add(item);

            TabPage tp = new TabPage(s.Title);
            tp.ToolTipText = s.Tooltip;
            _tabs.TabPages.Add(tp);

            _store.ActiveIndex = _sessions.Count - 1;
            _store.Save();

            _tabs.SelectedIndex = _sessions.Count - 1;   // 触发 OnTabChanged，会立即读一次
        }

        private void CloseCurrentSession()
        {
            int idx = _tabs.SelectedIndex;
            if (idx >= 0) CloseSessionAt(idx);
        }

        private void CloseSessionAt(int idx)
        {
            if (idx < 0 || idx >= _sessions.Count) return;

            MonitorSession s = _sessions[idx];
            if (s.HasUnsavedEdit)
            {
                if (MessageBox.Show(this, "「" + s.Title + "」还有未保存的修改，确定关闭吗？",
                        "关闭监控项", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
            }

            s.DisposeFetcher();

            if (ReferenceEquals(s, _cur)) _cur = null;

            _sessions.RemoveAt(idx);
            _store.Items.RemoveAt(idx);
            _tabs.TabPages.RemoveAt(idx);

            if (_sessions.Count == 0)
            {
                _store.ActiveIndex = 0;
                _store.Save();
                RestoreUiState(null);
                RefreshTimeline();
                return;
            }

            int next = idx >= _sessions.Count ? _sessions.Count - 1 : idx;
            _store.ActiveIndex = next;
            _store.Save();
            _suppressTab = true;
            _tabs.SelectedIndex = next;
            _suppressTab = false;

            _cur = _sessions[next];
            _cur.HasUnseenChange = false;
            RestoreUiState(_cur);
            RefreshTimeline();
            FillIntervals();
        }

        private void OnTabsMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle && _tabs.TabCount > 0) { CloseCurrentSession(); return; }
            if (e.Button == MouseButtons.Right && _tabs.TabCount > 0) ShowTabMenu(e.Location);
        }

        private void ShowTabMenu(Point pt)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = _uiFont;

            menu.Items.Add("新建监控项...", null, delegate { NewSession(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("关闭当前标签", null, delegate { CloseCurrentSession(); });
            menu.Items.Add("关闭其他标签", null, delegate
            {
                int keep = _tabs.SelectedIndex;
                for (int i = _sessions.Count - 1; i >= 0; i--)
                    if (i != keep) CloseSessionAt(i);
            });
            menu.Show(_tabs, pt);
        }

        /// <summary>设置：改全局服务器连接（改完所有标签重连）+ 当前文件的路径/间隔/编码。</summary>
        private void EditCurrentSettings()
        {
            if (_cur == null) { EditServerSettings(); return; }

            string oldPath = _cur.Conn.RemotePath;
            string oldServerSig = ServerSig(_store.Server);

            using (SettingsForm dlg = new SettingsForm(_cur.Conn.Clone(), _scale, false))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                AppConfig nc = dlg.Result;
                bool serverChanged = (ServerSig(nc) != oldServerSig);
                bool pathChanged = (nc.RemotePath != oldPath);

                ApplyServerFields(nc);

                _cur.Item.RemotePath = nc.RemotePath;
                _cur.Item.IntervalSec = nc.IntervalSec;
                _cur.Item.EncodingPref = nc.EncodingPref;
                _cur.Conn = _store.BuildConn(_cur.Item);

                _store.Save();

                if (serverChanged) RebuildAllConnections();

                if (serverChanged || pathChanged)
                {
                    _cur.Log.Clear();
                    _cur.LastText = "";
                    _cur.HasLoaded = false;
                    _cur.LastStat = null;
                    _cur.ViewingEntry = null;
                    RestoreUiState(_cur);
                    RefreshTimeline();
                }

                FillIntervals();
                UpdateTabTitle(_cur);
                ScheduleRefresh(_cur, true);
            }
        }

        /// <summary>只改服务器连接（没有打开任何文件时用）。</summary>
        private void EditServerSettings()
        {
            AppConfig baseCfg = _cur != null ? _cur.Conn.Clone() : _store.Server.Clone();
            if (_cur == null) baseCfg.RemotePath = "";
            if (string.IsNullOrEmpty(baseCfg.Host)) baseCfg.Port = 22;

            string oldServerSig = ServerSig(_store.Server);

            using (SettingsForm dlg = new SettingsForm(baseCfg, _scale, false))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                AppConfig nc = dlg.Result;
                ApplyServerFields(nc);
                _store.Save();

                if (ServerSig(nc) != oldServerSig)
                {
                    RebuildAllConnections();
                    if (_cur != null)
                    {
                        _cur.Log.Clear();
                        _cur.LastText = "";
                        _cur.HasLoaded = false;
                        RestoreUiState(_cur);
                        RefreshTimeline();
                        ScheduleRefresh(_cur, true);
                    }
                }
            }
        }

        private void ApplyServerFields(AppConfig src)
        {
            _store.Server.Host = src.Host;
            _store.Server.Port = src.Port;
            _store.Server.User = src.User;
            _store.Server.Password = src.Password;
            _store.Server.UseKey = src.UseKey;
            _store.Server.KeyFile = src.KeyFile;
            _store.Server.KeyPass = src.KeyPass;
            _store.Server.HostKeyFp = src.HostKeyFp;
        }

        /// <summary>服务器变了：所有标签的连接都要重建（旧连接指向老机器）。</summary>
        private void RebuildAllConnections()
        {
            for (int i = 0; i < _sessions.Count; i++)
            {
                MonitorSession s = _sessions[i];
                s.DisposeFetcher();
                s.Conn = _store.BuildConn(s.Item);
                s.LastStat = null;
                s.LastText = "";
                s.HasLoaded = false;
                s.ViewingEntry = null;
            }
        }

        private static string ServerSig(AppConfig c)
        {
            return (c.Host == null ? "" : c.Host) + "|" + c.Port.ToString() + "|" +
                   (c.User == null ? "" : c.User) + "|" +
                   (c.UseKey ? "key:" + c.KeyFile : "pwd:" + c.Password);
        }

        private static string Sig(AppConfig c)
        {
            return c.Host + "|" + c.Port.ToString() + "|" + c.User + "|" + c.RemotePath;
        }

        private static string DirOf(string remotePath)
        {
            if (string.IsNullOrEmpty(remotePath)) return "/";
            int i = remotePath.LastIndexOf('/');
            if (i < 0) return "/";
            if (i == 0) return "/";
            return remotePath.Substring(0, i + 1);
        }

        // ==================== 轮询 ====================

        private void OnTimerTick()
        {
            if (!_chkAuto.Checked) return;

            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < _sessions.Count; i++)
            {
                MonitorSession s = _sessions[i];
                if (s.Conn == null || !s.Conn.IsReady) continue;

                bool active = ReferenceEquals(s, _cur);
                int sec = active ? Math.Max(1, s.Conn.IntervalSec) : InactiveProbeSec;

                if ((now - s.LastPollUtc).TotalSeconds >= sec)
                    ScheduleRefresh(s, false, !active);
            }
        }

        private void ScheduleRefresh(MonitorSession s, bool manual)
        {
            ScheduleRefresh(s, manual, false);
        }

        /// <summary>statOnly=true 时只比较文件大小/修改时间，不下载内容（用于非活动标签的轻量探测）。</summary>
        private void ScheduleRefresh(MonitorSession s, bool manual, bool statOnly)
        {
            if (s == null || s.Conn == null || !s.Conn.IsReady) return;
            if (_closing || s.Busy) return;

            s.Busy = true;
            s.LastPollUtc = DateTime.UtcNow;

            AppConfig cfg = s.Conn;
            bool force = manual;
            bool isCur = ReferenceEquals(s, _cur);
            bool probeOnly = statOnly;

            if (manual && isCur) SetStatus("● 正在刷新…", Color.FromArgb(176, 122, 20));

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                Snapshot snap = null;
                string err = null;
                bool changed = false;
                string learnedFp = null;
                bool notFound = false;

                try
                {
                    if (s.Fetcher == null) s.Fetcher = new SftpFetcher(cfg);

                    RemoteStat st = s.Fetcher.Stat();
                    bool diff = (s.LastStat == null)
                                || st.Size != s.LastStat.Size
                                || st.ModifiedUtc != s.LastStat.ModifiedUtc;

                    if (probeOnly)
                    {
                        // 轻量探测：只判断"有没有变"，不下载内容
                        if (s.LastStat == null)
                        {
                            // 这个标签还没被打开过（比如刚启动），先把当前状态记成基线，
                            // 之后的变化才会触发红点
                            RemoteStat ns0 = new RemoteStat();
                            ns0.Size = st.Size;
                            ns0.ModifiedUtc = st.ModifiedUtc;
                            s.LastStat = ns0;
                        }
                        else if (diff)
                        {
                            changed = true;

                            RemoteStat ns = new RemoteStat();
                            ns.Size = st.Size;
                            ns.ModifiedUtc = st.ModifiedUtc;
                            s.LastStat = ns;      // 更新基线，避免同一变化反复触发红点
                        }
                    }
                    else if (force || diff)
                    {
                        snap = s.Fetcher.Read();
                        changed = true;

                        // 必须更新基线，否则下次比较永远"有变化"，会反复下载全文，
                        // 非活跃标签的轻量探测也就永远拿不到比较基准
                        RemoteStat ns2 = new RemoteStat();
                        ns2.Size = snap.Size;
                        ns2.ModifiedUtc = snap.ModifiedUtc;
                        s.LastStat = ns2;
                    }

                    if (s.Fetcher.LearnedHostKeyFp.Length > 0)
                    {
                        learnedFp = s.Fetcher.LearnedHostKeyFp;
                        s.Fetcher.LearnedHostKeyFp = "";
                    }
                }
                catch (Exception ex)
                {
                    err = SftpFetcher.DescribeError(ex);
                    notFound = SftpFetcher.IsNotFound(ex);
                    s.DisposeFetcher();
                }

                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        OnRefreshDone(s, snap, changed, err, learnedFp, notFound, probeOnly);
                    }));
                }
                catch { }
            });
        }

        private void OnRefreshDone(MonitorSession s, Snapshot snap, bool changed,
                                   string err, string learnedFp, bool notFound, bool probeOnly)
        {
            s.Busy = false;
            if (_closing) return;

            bool isCur = ReferenceEquals(s, _cur);

            // 非活动标签的轻量探测：只点红点，不动 UI、不写时间线
            if (probeOnly)
            {
                if (changed && !isCur)
                {
                    s.HasUnseenChange = true;
                    UpdateTabTitle(s);
                }
                return;
            }

            if (!string.IsNullOrEmpty(learnedFp))
            {
                s.Conn.HostKeyFp = learnedFp;
                _store.Server.HostKeyFp = learnedFp;   // 指纹是服务器级别的，写全局
                _store.Save();
            }

            if (err != null)
            {
                s.LastError = err;
                if (isCur)
                {
                    SetStatus("● 连接失败", Color.FromArgb(198, 40, 40));
                    _lblStatus.ToolTipText = err;
                    _lblRight.Text = err;
                    _lblRight.ForeColor = Color.FromArgb(198, 40, 40);
                    _lblHeader.Text = s.Conn.DisplayName + "  (离线)";
                }

                if (notFound && !s.HasLoaded && !s.CreateAsked)
                {
                    s.CreateAsked = true;
                    if (isCur) AskCreateMissingFile(s);
                }

                UpdateTabTitle(s);
                return;
            }

            s.LastError = null;

            if (isCur)
            {
                _lblTime.Text = "刷新 " + DateTime.Now.ToString("HH:mm:ss");
                _lblRight.Text = s.Conn.DisplayName;
                _lblRight.ForeColor = Color.FromArgb(140, 146, 156);
                _lblHeader.Text = s.Conn.DisplayName;

                if (s.ViewingEntry == null && s.PinnedLabel.Length == 0 && !s.Editing)
                {
                    SetStatus("● 已连接", Color.FromArgb(22, 130, 62));
                    _lblStatus.ToolTipText = "";
                }
            }
            if (changed && snap != null) ApplySnapshot(s, snap);
            UpdateTabTitle(s);
        }

        private void AskCreateMissingFile(MonitorSession s)
        {
            string msg = "服务器上找不到这个文件：\r\n\r\n" + s.Conn.RemotePath +
                         "\r\n\r\n要创建一个空文件吗？（适合拿它当两台机器之间的共享笔记）";

            if (MessageBox.Show(this, msg, "文件不存在",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                SetStatus("● 文件不存在", Color.FromArgb(198, 40, 40));
                return;
            }

            AppConfig cfg = s.Conn;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string err = null;
                try
                {
                    if (s.Fetcher == null) s.Fetcher = new SftpFetcher(cfg);
                    s.Fetcher.WriteAtomic("", "UTF-8");
                }
                catch (Exception ex) { err = SftpFetcher.DescribeError(ex); }

                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (err != null)
                        {
                            MessageBox.Show(this, "创建失败：\r\n\r\n" + err, "创建文件",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        else
                        {
                            s.CreateAsked = false;
                            ScheduleRefresh(s, true);
                        }
                    }));
                }
                catch { }
            });
        }

        // ==================== 呈现 ====================

        private void ApplySnapshot(MonitorSession s, Snapshot snap)
        {
            bool isCur = ReferenceEquals(s, _cur);

            string newText = snap.Text == null ? "" : snap.Text;

            bool firstLoad = !s.HasLoaded;                    // 本次运行第一次读到
            bool baseline = firstLoad && s.Log.Count == 0;    // 时间线里真的一条都没有，才算"初始"

            string oldText = s.LastText;

            if (firstLoad && !baseline)
            {
                // 重启后首次加载：用历史最后一条作为比较基准，这样新增行数才算得准，
                // 也不会把"重启后看到了一个更新过的文件"误标成"初始"
                List<ChangeEntry> hist = s.Log.Snapshot();
                if (hist.Count > 0) oldText = s.Log.GetText(hist[hist.Count - 1]);
            }

            bool same = (newText == oldText);
            bool pureAppend = false;
            int oldLines = CountLines(oldText);

            if (newText.Length > oldText.Length
                && string.CompareOrdinal(newText, 0, oldText, 0, oldText.Length) == 0)
            {
                pureAppend = true;
            }

            int lines = CountLines(newText);
            s.HasLoaded = true;

            bool atBottom = isCur && (_chkFollow.Checked || IsLastLineVisible());

            if (!isCur)
            {
                // 后台标签：只更新数据，不碰 UI
                s.LastText = newText;
                s.LastSnap = snap;
                if (!same)
                {
                    int dl0 = baseline ? -1 : (pureAppend ? (lines - oldLines) : -1);
                    s.Log.Add(newText, snap.ModifiedUtc, dl0, lines, baseline, false);
                }
                return;
            }

            if (s.Editing)
            {
                s.LastText = newText;
                s.LastSnap = snap;
                if (!same)
                {
                    s.RemoteChangedDuringEdit = true;
                    UpdateSizeLabel(newText, snap.Size);
                    _lblChanged.Text = "更改 " + FormatMtime(snap.ModifiedUtc);
                    _lblNew.ForeColor = Color.FromArgb(198, 40, 40);
                    _lblNew.Text = "⚠ 远端已更新（保存时会提示冲突）";

                    int dl = baseline ? -1 : (pureAppend ? (lines - oldLines) : -1);
                    s.Log.Add(newText, snap.ModifiedUtc, dl, lines, baseline, false);
                    RefreshTimeline();
                }
                return;
            }

            // 正文重绘必须和"内容有没有变"解耦：
            // 启动时若文件内容与历史最后一条相同，same 会为 true，
            // 若把赋值锁在 !same 里，窗口打开就是空白（必须点一下时间线才出来）。
            bool needPaint = firstLoad || !same;

            if (needPaint && s.ViewingEntry == null && s.PinnedLabel.Length == 0)
            {
                _rtb.Text = newText;
                if (pureAppend && !firstLoad) HighlightFromLine(oldLines);
                if (atBottom) ScrollToBottom();
            }

            s.LastText = newText;
            s.LastSnap = snap;

            UpdateSizeLabel(newText, snap.Size);
            _lblChanged.Text = "更改 " + FormatMtime(snap.ModifiedUtc);
            _lblChanged.ToolTipText = "服务器文件最后修改时间："
                + snap.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                + "   编码 " + snap.EncodingName;

            if (s.ViewingEntry == null && s.PinnedLabel.Length == 0)
            {
                if (!same && !firstLoad && pureAppend && lines > oldLines)
                {
                    _lblNew.ForeColor = Color.FromArgb(190, 108, 0);
                    _lblNew.Text = "▲ 新增 " + (lines - oldLines) + " 行";
                }
                else if (!same && !firstLoad)
                {
                    _lblNew.ForeColor = Color.FromArgb(190, 108, 0);
                    _lblNew.Text = "内容已变化";
                }
                else if (firstLoad)
                {
                    _lblNew.Text = "";
                }
            }

            if (!same)
            {
                bool skip = false;
                if (baseline)
                {
                    List<ChangeEntry> hist = s.Log.Snapshot();
                    if (hist.Count > 0 && s.Log.GetText(hist[hist.Count - 1]) == newText) skip = true;
                }

                if (!skip)
                {
                    int dl = baseline ? -1 : (pureAppend ? (lines - oldLines) : -1);
                    s.Log.Add(newText, snap.ModifiedUtc, dl, lines, baseline, false);
                    RefreshTimeline();
                }
            }
        }

        private void ReturnToLive()
        {
            if (_cur == null) return;
            if (_cur.ViewingEntry == null && _cur.PinnedLabel.Length == 0) return;

            _cur.ViewingEntry = null;
            _cur.PinnedLabel = "";

            SetContentBackColor(Color.White, Color.FromArgb(248, 249, 251));
            SetStatus("● 已连接", Color.FromArgb(22, 130, 62));
            _lblStatus.ToolTipText = "";

            _rtb.Text = _cur.LastText;
            ScrollToBottom();

            _lblNew.ForeColor = Color.FromArgb(190, 108, 0);
            _lblNew.Text = "";
            UpdateStatusFromSession(_cur);

            _suppressPaneSelect = true;
            _paneList.ClearSelected();
            _suppressPaneSelect = false;
        }

        private void ShowHistoryEntry(ChangeEntry e)
        {
            if (_cur == null) return;
            if (e == null) { ReturnToLive(); return; }

            _cur.ViewingEntry = e;
            _cur.PinnedLabel = "";

            SetContentBackColor(Color.FromArgb(255, 252, 238), Color.FromArgb(251, 247, 232));
            _rtb.Text = _cur.Log.GetText(e);
            ApplyHistoryUi(e);
            ScrollToBottom();
        }

        private void ApplyHistoryUi(ChangeEntry e)
        {
            _lblStatus.Text = "◀ 历史 " + e.LocalTime.ToString("HH:mm:ss") + " · 点此返回实时";
            _lblStatus.ForeColor = Color.FromArgb(190, 108, 0);
            _lblStatus.ToolTipText = "正在查看 " + e.LocalTime.ToString("yyyy-MM-dd HH:mm:ss")
                + " 的快照，点这里或按 Esc 返回实时";

            _lblChanged.Text = "更改 " + FormatMtime(e.ServerMtime);
            _lblChanged.ToolTipText = "该快照对应的服务器文件修改时间";
            UpdateSizeLabel(e.Text == null ? "" : e.Text, e.Bytes);
            _lblNew.ForeColor = Color.FromArgb(150, 120, 40);
            _lblNew.Text = e.IsBaseline ? "初始快照"
                          : (e.NewLines > 0 ? "当时新增 " + e.NewLines + " 行" : "内容变化");
        }

        private void ShowPinnedText(string label, string text)
        {
            if (_cur == null) return;

            _cur.ViewingEntry = null;
            _cur.PinnedLabel = label;

            SetContentBackColor(Color.FromArgb(245, 248, 255), Color.FromArgb(236, 241, 250));
            _rtb.Text = text == null ? "" : text;
            _rtb.SelectionStart = 0;
            _rtb.ScrollToCaret();

            _lblStatus.Text = "★ 收藏：" + label + " · 点此返回实时";
            _lblStatus.ForeColor = Color.FromArgb(40, 90, 190);
            _lblStatus.ToolTipText = "正在查看收藏内容，点这里或按 Esc 返回实时";

            _lblChanged.Text = "";
            UpdateSizeLabel(text == null ? "" : text, Encoding.UTF8.GetByteCount(text == null ? "" : text));
            _lblNew.Text = "";
        }

        // ==================== 时间线 ====================

        private void RefreshTimeline()
        {
            if (_cur == null)
            {
                _paneList.Items.Clear();
                _paneTitle.Text = "变更时间线 · 近 24 小时";
                return;
            }

            List<ChangeEntry> all = _cur.Log.Snapshot();

            _suppressPaneSelect = true;
            _paneList.BeginUpdate();
            try
            {
                _paneList.Items.Clear();
                for (int i = all.Count - 1; i >= 0; i--)
                    _paneList.Items.Add(new EntryItem(all[i], FormatEntryLabel(all[i])));

                if (_cur.ViewingEntry != null)
                {
                    for (int i = 0; i < _paneList.Items.Count; i++)
                    {
                        EntryItem it = (EntryItem)_paneList.Items[i];
                        if (it.Entry.Id == _cur.ViewingEntry.Id) { _paneList.SelectedIndex = i; break; }
                    }
                }
            }
            finally
            {
                _paneList.EndUpdate();
                _suppressPaneSelect = false;
            }

            _paneTitle.Text = "变更时间线 · 近 24 小时（" + all.Count + "）";
        }

        private string FormatEntryLabel(ChangeEntry e)
        {
            DateTime today = DateTime.Now.Date;
            string when;
            if (e.LocalTime.Date == today) when = e.LocalTime.ToString("HH:mm:ss");
            else if (e.LocalTime.Date == today.AddDays(-1)) when = "昨天 " + e.LocalTime.ToString("HH:mm");
            else when = e.LocalTime.ToString("MM-dd HH:mm");

            string what;
            if (e.IsLocalEdit)
            {
                // 显示保存者的 Windows 用户名，这样两台机器的时间线能区分是谁存的
                string who = string.IsNullOrEmpty(e.Editor) ? "本机" : e.Editor;
                if (who.Length > 14) who = who.Substring(0, 13) + "…";
                what = "✎ " + who + " " + e.TotalLines + "行";
            }
            else if (e.IsBaseline) what = "初始 " + e.TotalLines + "行";
            else if (e.NewLines > 0) what = "+" + e.NewLines + "行";
            else what = "内容变化";

            return when + "  " + what;
        }

        private void OnPaneSelect()
        {
            if (_suppressPaneSelect) return;
            EntryItem it = _paneList.SelectedItem as EntryItem;
            if (it == null) return;
            ShowHistoryEntry(it.Entry);
        }

        // ==================== 行号栏 ====================

        private void SetContentBackColor(Color body, Color gutterColor)
        {
            _rtb.BackColor = body;
            if (_gutter != null)
            {
                _gutter.BackColor = gutterColor;
                _gutter.Invalidate();
            }
        }

        private void OnGutterPaint(object sender, PaintEventArgs e)
        {
            if (_rtb == null || _gutterFont == null) return;

            Graphics g = e.Graphics;
            g.Clear(_gutter.BackColor);

            using (Pen pen = new Pen(Color.FromArgb(208, 213, 222)))
                g.DrawLine(pen, _gutter.Width - 1, 0, _gutter.Width - 1, _gutter.Height);

            string text = _rtb.Text;
            int len = text == null ? 0 : text.Length;

            int firstLine, lastLine;
            try
            {
                int firstChar = _rtb.GetCharIndexFromPosition(new Point(1, 1));
                int lastChar = _rtb.GetCharIndexFromPosition(
                    new Point(1, Math.Max(1, _rtb.ClientSize.Height - 1)));
                firstLine = _rtb.GetLineFromCharIndex(firstChar);
                lastLine = _rtb.GetLineFromCharIndex(lastChar);
            }
            catch { return; }

            if (firstLine < 0) firstLine = 0;
            if (lastLine < firstLine) lastLine = firstLine;

            int startCh = 0;
            try { startCh = _rtb.GetFirstCharIndexFromLine(firstLine); }
            catch { }
            if (startCh < 0) startCh = 0;

            int lineNo = 1;
            for (int i = 0; i < startCh && i < len; i++)
                if (text[i] == '\n') lineNo++;

            int dy = (_rtb.Font.Height - _gutterFont.Height) / 2;
            if (dy < 0) dy = 0;

            bool wrap = _rtb.WordWrap;

            for (int ln = firstLine; ln <= lastLine; ln++)
            {
                int ch;
                try { ch = _rtb.GetFirstCharIndexFromLine(ln); }
                catch { break; }
                if (ch < 0 || ch > len) break;

                Point pos;
                try { pos = _rtb.GetPositionFromCharIndex(ch); }
                catch { break; }

                bool isLogicalStart = (ch == 0) || (ch > 0 && text[ch - 1] == '\n');
                if (isLogicalStart || !wrap)
                    DrawLineNo(g, lineNo, pos.Y + dy);

                int nextCh;
                try { nextCh = _rtb.GetFirstCharIndexFromLine(ln + 1); }
                catch { nextCh = -1; }
                if (nextCh < 0 || nextCh > len) nextCh = len;

                for (int i = ch; i < nextCh; i++)
                    if (text[i] == '\n') lineNo++;
            }

            if (len == 0) DrawLineNo(g, 1, dy);
        }

        private void DrawLineNo(Graphics g, int number, int y)
        {
            string label = number.ToString();
            Size sz = TextRenderer.MeasureText(label, _gutterFont);
            int x = _gutter.Width - sz.Width - S(8);
            if (x < S(2)) x = S(2);

            TextRenderer.DrawText(g, label, _gutterFont, new Point(x, y),
                Color.FromArgb(152, 158, 168), TextFormatFlags.NoPadding);
        }

        private void UpdateGutterWidth()
        {
            if (_gutter == null || _gutterFont == null) return;

            int lines = CountLines(_rtb.Text);
            if (lines < 1) lines = 1;

            int digits = lines.ToString().Length;
            if (digits < 3) digits = 3;
            if (digits == _gutterDigits) return;

            _gutterDigits = digits;
            string sample = new string('9', digits);
            Size sz = TextRenderer.MeasureText(sample, _gutterFont);

            int w = sz.Width + S(18);
            if (w < S(34)) w = S(34);
            _gutter.Width = w;
        }

        // ==================== 编辑 ====================

        private bool _suppressEditToggle;

        private void OnEditToggle()
        {
            if (_suppressEditToggle) return;
            if (_btnEdit.Checked) EnterEditMode();
            else ExitEditMode(false);
        }

        private void EnterEditMode()
        {
            if (_cur == null) { WarnNoEdit("还没有监控项"); return; }
            if (!_cur.HasLoaded) { WarnNoEdit("还没成功读取到文件内容，稍后再试"); return; }
            if (_cur.ViewingEntry != null || _cur.PinnedLabel.Length > 0) ReturnToLive();

            _cur.Editing = true;
            _cur.EditBaselineText = _cur.LastText;
            _cur.EditBaselineHash = SftpFetcher.HashText(_cur.LastText);
            _cur.RemoteChangedDuringEdit = false;

            _btnSave.Enabled = true;
            _rtb.ReadOnly = false;
            SetContentBackColor(Color.FromArgb(255, 253, 244), Color.FromArgb(251, 247, 235));
            _rtb.Focus();
            _rtb.SelectionStart = _rtb.TextLength;

            SetStatus("✎ 编辑中 · 未保存", Color.FromArgb(176, 100, 20));
            _lblNew.ForeColor = Color.FromArgb(150, 120, 40);
            _lblNew.Text = "Ctrl+S 保存    Esc 取消";
            UpdateTabTitle(_cur);
        }

        private void WarnNoEdit(string msg)
        {
            _suppressEditToggle = true;
            _btnEdit.Checked = false;
            _suppressEditToggle = false;
            SetStatus("● " + msg, Color.FromArgb(150, 150, 155));
        }

        private void ExitEditMode(bool afterSave)
        {
            if (_cur == null || !_cur.Editing) return;

            if (!afterSave && _rtb.Text != _cur.EditBaselineText)
            {
                if (MessageBox.Show(this, "有未保存的修改，确定放弃吗？",
                        "放弃修改", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    _suppressEditToggle = true;
                    _btnEdit.Checked = true;
                    _suppressEditToggle = false;
                    return;
                }
            }

            _cur.Editing = false;
            _cur.RemoteChangedDuringEdit = false;

            _suppressEditToggle = true;
            _btnEdit.Checked = false;
            _suppressEditToggle = false;

            _btnSave.Enabled = false;
            _rtb.ReadOnly = true;
            SetContentBackColor(Color.White, Color.FromArgb(248, 249, 251));
            _rtb.Text = _cur.LastText;
            ScrollToBottom();

            SetStatus("● 已连接", Color.FromArgb(22, 130, 62));
            _lblNew.ForeColor = Color.FromArgb(190, 108, 0);
            _lblNew.Text = "";
            UpdateStatusFromSession(_cur);
            UpdateTabTitle(_cur);
        }

        private void SaveEdit()
        {
            if (_cur == null || !_cur.Editing) return;

            string myText = _rtb.Text;
            if (myText == _cur.EditBaselineText)
            {
                SetStatus("✎ 没有改动，无需保存", Color.FromArgb(120, 124, 130));
                return;
            }
            DoSave(_cur, myText, false);
        }

        private void DoSave(MonitorSession s, string text, bool force)
        {
            SetStatus("✎ 正在保存…", Color.FromArgb(176, 122, 20));

            AppConfig cfg = s.Conn;
            bool forceFlag = force;
            string baselineHash = s.EditBaselineHash;

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string err = null;
                bool conflict = false;
                string remoteText = null;
                string remoteEnc = null;
                long newSize = 0;
                DateTime newMtime = DateTime.MinValue;

                try
                {
                    if (s.Fetcher == null) s.Fetcher = new SftpFetcher(cfg);

                    Snapshot cur = s.Fetcher.Read();
                    remoteText = cur.Text;
                    remoteEnc = cur.EncodingName;

                    if (!forceFlag && SftpFetcher.HashText(cur.Text) != baselineHash)
                    {
                        conflict = true;
                    }
                    else
                    {
                        s.Fetcher.WriteAtomic(text, cur.EncodingName);
                        RemoteStat st = s.Fetcher.Stat();
                        newSize = st.Size;
                        newMtime = st.ModifiedUtc;
                    }
                }
                catch (Exception ex)
                {
                    err = SftpFetcher.DescribeError(ex);
                    s.DisposeFetcher();
                }

                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        OnSaveDone(s, text, err, conflict, remoteText, remoteEnc, newSize, newMtime);
                    }));
                }
                catch { }
            });
        }

        private void OnSaveDone(MonitorSession s, string savedText, string err, bool conflict,
                                string remoteText, string remoteEnc, long newSize, DateTime newMtime)
        {
            if (_closing) return;

            if (err != null)
            {
                SetStatus("✎ 保存失败", Color.FromArgb(198, 40, 40));
                _lblStatus.ToolTipText = err;
                MessageBox.Show(this, "保存失败：\r\n\r\n" + err, "保存失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (conflict)
            {
                HandleConflict(s, remoteText, savedText, remoteEnc);
                return;
            }

            s.LastText = savedText;

            RemoteStat ns = new RemoteStat();
            ns.Size = newSize;
            ns.ModifiedUtc = newMtime;
            s.LastStat = ns;

            Snapshot fake = new Snapshot();
            fake.Text = savedText;
            fake.Size = newSize;
            fake.ModifiedUtc = newMtime;
            fake.EncodingName = remoteEnc;
            s.LastSnap = fake;

            s.Log.Add(savedText, newMtime, -1, CountLines(savedText), false, true, Environment.UserName);

            if (ReferenceEquals(s, _cur))
            {
                RefreshTimeline();
                ExitEditMode(true);
                SetStatus("✔ 已保存到服务器", Color.FromArgb(22, 130, 62));
                _lblNew.ForeColor = Color.FromArgb(40, 140, 70);
                _lblNew.Text = "✔ 已保存 " + DateTime.Now.ToString("HH:mm:ss");
            }
            else
            {
                s.Editing = false;
                s.HasUnseenChange = true;
            }

            UpdateTabTitle(s);
        }

        private void HandleConflict(MonitorSession s, string remoteText, string myText, string remoteEnc)
        {
            using (ConflictForm dlg = new ConflictForm(remoteText, myText, "", _scale))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    SetStatus("✎ 编辑中 · 未保存（远端已改动）", Color.FromArgb(198, 40, 40));
                    return;
                }

                if (dlg.Choice == ConflictChoice.Overwrite)
                {
                    DoSave(s, myText, true);
                }
                else if (dlg.Choice == ConflictChoice.UseRemote)
                {
                    s.LastText = remoteText;
                    s.EditBaselineText = remoteText;
                    ExitEditMode(true);
                    SetStatus("✔ 已载入服务器版本", Color.FromArgb(22, 130, 62));
                }
                else if (dlg.Choice == ConflictChoice.SaveLocal)
                {
                    SetStatus("✎ 已另存到本地 · 仍在编辑", Color.FromArgb(176, 100, 20));
                }
            }
        }

        // ==================== 收藏 ====================

        private string CurrentVisibleText()
        {
            if (_cur == null) return "";
            if (_cur.ViewingEntry != null) return _cur.Log.GetText(_cur.ViewingEntry);
            return _cur.LastText;
        }

        private void AddFavorite()
        {
            string text = CurrentVisibleText();
            if (text.Length == 0)
            {
                SetStatus("● 没有可收藏的内容", Color.FromArgb(150, 120, 40));
                return;
            }

            string guess = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            if (_cur != null && _cur.ViewingEntry != null)
                guess = _cur.ViewingEntry.LocalTime.ToString("yyyy-MM-dd HH:mm:ss");

            using (PromptForm dlg = new PromptForm("收藏当前内容",
                       "收藏名称（可直接用时间，也可自己起名）：", guess, _scale))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                string name = dlg.Value == null ? "" : dlg.Value.Trim();
                if (name.Length == 0) name = guess;

                string src = _cur == null ? "" : _cur.Conn.DisplayName;
                if (_cur != null && _cur.ViewingEntry != null)
                    src += " @ " + _cur.ViewingEntry.LocalTime.ToString("HH:mm:ss");

                FavoriteItem f = _favs.Add(name, text, src, CountLines(text));
                _lblNew.ForeColor = Color.FromArgb(40, 140, 70);
                _lblNew.Text = "★ 已收藏：" + f.Name;
            }
        }

        private void OpenFavorites()
        {
            using (FavoritesForm dlg = new FavoritesForm(_favs, _scale, _cur == null ? "" : _cur.Conn.DisplayName))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.LastViewed != null)
                    ShowPinnedText(dlg.LastViewed.Name, dlg.LastViewedText);
            }
        }

        // ==================== 其它 ====================

        private void ChangeFontSize(int delta)
        {
            float nf = _store.Ui.FontSize + delta;
            if (nf < 7f) nf = 7f;
            if (nf > 30f) nf = 30f;
            if (Math.Abs(nf - _store.Ui.FontSize) < 0.01f) return;

            _store.Ui.FontSize = nf;
            _contentFont = SafeFont("Consolas", nf, FontStyle.Regular);
            _gutterFont = SafeFont("Consolas", Math.Max(7f, nf - 1f), FontStyle.Regular);
            _rtb.Font = _contentFont;
            _gutterDigits = 0;
            UpdateGutterWidth();
            if (_gutter != null) _gutter.Invalidate();
            _store.Save();
        }

        private void SaveSnapshotLocal()
        {
            string text = CurrentVisibleText();
            if (text.Length == 0) return;

            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
                d.FileName = DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    try { File.WriteAllText(d.FileName, text, new UTF8Encoding(false)); }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "保存失败：" + ex.Message, "提示",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        private void OnShown(object sender, EventArgs e)
        {
            if (_sessions.Count == 0)
            {
                SetStatus("● 没有监控项 — 点「新建」", Color.FromArgb(150, 150, 155));
                NewSession();
            }
            if (_chkAuto.Checked) _timer.Start();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            _closing = true;
            if (_timer != null) _timer.Stop();

            // 有未保存的编辑，先确认
            for (int i = 0; i < _sessions.Count; i++)
            {
                if (_sessions[i].HasUnsavedEdit)
                {
                    if (MessageBox.Show(this,
                            "「" + _sessions[i].Title + "」还有未保存的修改，确定退出吗？",
                            "退出", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        _closing = false;
                        e.Cancel = true;
                        if (_timer != null) _timer.Start();
                        return;
                    }
                    break;
                }
            }

            if (WindowState == FormWindowState.Normal)
            {
                _store.Ui.WinX = Left;
                _store.Ui.WinY = Top;
                _store.Ui.WinW = Width;
                _store.Ui.WinH = Height;
                _store.Ui.WinMax = false;
            }
            else
            {
                _store.Ui.WinMax = (WindowState == FormWindowState.Maximized);
            }

            _store.ActiveIndex = _tabs.SelectedIndex < 0 ? 0 : _tabs.SelectedIndex;
            _store.Save();

            for (int i = 0; i < _sessions.Count; i++)
                _sessions[i].DisposeFetcher();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5) { if (_cur != null) ScheduleRefresh(_cur, true); return true; }

            if (keyData == Keys.Escape)
            {
                if (_cur != null && _cur.Editing) { _btnEdit.Checked = false; return true; }
                if (_cur != null && (_cur.ViewingEntry != null || _cur.PinnedLabel.Length > 0)) { ReturnToLive(); return true; }
            }

            if (keyData == (Keys.Control | Keys.T)) { NewSession(); return true; }
            if (keyData == (Keys.Control | Keys.W)) { CloseCurrentSession(); return true; }
            if (keyData == (Keys.Control | Keys.O)) { EditCurrentSettings(); return true; }
            if (keyData == (Keys.Control | Keys.S)) { SaveEdit(); return true; }
            if (keyData == (Keys.Control | Keys.E)) { _btnEdit.Checked = !_btnEdit.Checked; return true; }
            if (keyData == (Keys.Control | Keys.D)) { AddFavorite(); return true; }
            if (keyData == (Keys.Control | Keys.B)) { OpenFavorites(); return true; }
            if (keyData == (Keys.Control | Keys.H)) { _chkPane.Checked = !_chkPane.Checked; return true; }
            if (keyData == (Keys.Control | Keys.Add)) { ChangeFontSize(1); return true; }
            if (keyData == (Keys.Control | Keys.Subtract)) { ChangeFontSize(-1); return true; }

            if (keyData == (Keys.Control | Keys.Tab))
            {
                int n = _tabs.TabCount;
                if (n > 1) _tabs.SelectedIndex = (_tabs.SelectedIndex + 1) % n;
                return true;
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.Tab))
            {
                int n = _tabs.TabCount;
                if (n > 1) _tabs.SelectedIndex = (_tabs.SelectedIndex - 1 + n) % n;
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ==================== 小工具 ====================

        private void SetStatus(string text, Color color)
        {
            _lblStatus.Text = text;
            _lblStatus.ForeColor = color;
        }

        private void UpdateSizeLabel(string text, long bytes)
        {
            _lblSize.Text = FormatSize(bytes) + " · " + CountLines(text) + " 行";
        }

        private void ScrollToBottom()
        {
            _rtb.SelectionStart = _rtb.TextLength;
            _rtb.SelectionLength = 0;
            _rtb.ScrollToCaret();
        }

        private bool IsLastLineVisible()
        {
            if (_rtb.TextLength == 0) return true;
            try
            {
                Point pt = _rtb.GetPositionFromCharIndex(_rtb.TextLength - 1);
                return pt.Y >= 0 && pt.Y < _rtb.ClientSize.Height;
            }
            catch { return true; }
        }

        private void HighlightFromLine(int lineIndex)
        {
            if (lineIndex < 0) return;
            int ch = _rtb.GetFirstCharIndexFromLine(lineIndex);
            if (ch < 0) return;
            int len = _rtb.TextLength - ch;
            if (len <= 0) return;

            _rtb.Select(ch, len);
            _rtb.SelectionBackColor = Color.FromArgb(255, 243, 160);
            _rtb.SelectionLength = 0;
            _rtb.SelectionStart = ch;
        }

        private int GetScrollY()
        {
            try
            {
                Point p = new Point(0, 0);
                SendMessagePt(_rtb.Handle, EM_GETSCROLLPOS, IntPtr.Zero, ref p);
                return p.Y;
            }
            catch { return 0; }
        }

        private void SetScrollY(int y)
        {
            try
            {
                Point p = new Point(0, y);
                SendMessagePt(_rtb.Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref p);
            }
            catch { }
        }

        private static int CountLines(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 1;
            for (int i = 0; i < s.Length; i++)
                if (s[i] == '\n') n++;
            if (s[s.Length - 1] == '\n') n--;
            return n < 0 ? 0 : n;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("0.00") + " MB";
        }

        private static string FormatMtime(DateTime utc)
        {
            DateTime t = utc.ToLocalTime();
            if (t.Date == DateTime.Now.Date) return t.ToString("HH:mm:ss");
            return t.ToString("MM-dd HH:mm:ss");
        }
    }
}
