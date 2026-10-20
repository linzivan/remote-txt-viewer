using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace RemoteTxtViewer
{
    public class MainForm : Form
    {
        // ---------- 状态 ----------
        private AppConfig _cfg;
        private SftpFetcher _fetcher;
        private readonly object _netLock = new object();

        private RemoteStat _lastStat;
        private Snapshot _lastSnap;
        private string _lastText = "";
        private bool _busy;
        private bool _closing;

        private readonly ChangeLog _changeLog = new ChangeLog();
        private readonly FavoritesStore _favs = new FavoritesStore();

        private ChangeEntry _viewing;        // 正在看的历史快照（null = 实时）
        private string _pinnedLabel = "";    // 正在看收藏时显示的名字
        private bool _suppressPaneSelect;

        private float _scale = 1f;
        private Font _uiFont;
        private Font _contentFont;
        private Font _monoFont;
        private System.Windows.Forms.Timer _timer;

        // ---------- 控件 ----------
        private ToolStrip _ts;
        private ToolStripButton _btnSettings;
        private ToolStripButton _btnRefresh;
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

        private Panel _host;
        private RichTextBox _rtb;

        private Panel _pane;
        private Label _paneTitle;
        private ListBox _paneList;
        private Button _paneBack;

        private StatusStrip _st;
        private ToolStripStatusLabel _lblStatus;
        private ToolStripStatusLabel _lblTime;
        private ToolStripStatusLabel _lblChanged;
        private ToolStripStatusLabel _lblSize;
        private ToolStripStatusLabel _lblNew;
        private ToolStripStatusLabel _lblSpring;
        private ToolStripStatusLabel _lblRight;

        private static readonly int[] Intervals = new int[] { 1, 2, 3, 5, 10, 30, 60 };

        private class EntryItem
        {
            public ChangeEntry Entry;
            public string Label;
            public EntryItem(ChangeEntry e, string label) { Entry = e; Label = label; }
            public override string ToString() { return Label; }
        }

        // 自动化测试用：设置 RTV_NO_ACTIVATE=1 时窗口不激活自身、并移出屏幕，
        // 这样测试脚本可以用 PrintWindow 抓图而不打扰正在用电脑的人。
        private readonly bool _headless;

        protected override bool ShowWithoutActivation { get { return _headless; } }

        public MainForm()
        {
            _headless = (Environment.GetEnvironmentVariable("RTV_NO_ACTIVATE") == "1");
            _cfg = AppConfig.Load();

            InitScale();
            CreateFonts();
            BuildUi();
            ApplyConfigToUi();
            RestoreWindowBounds();

            Shown += OnShown;
            FormClosing += OnFormClosing;

            if (_headless)
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(-6000, 50, Width, Height);
            }
        }

        // ================= 基础 =================

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
            // 不 Dispose 旧字体：控件可能仍在用
            _uiFont = SafeFont("Microsoft YaHei UI", 9f, FontStyle.Regular);
            _contentFont = SafeFont("Consolas", _cfg.FontSize, FontStyle.Regular);
            _monoFont = SafeFont("Consolas", 9f, FontStyle.Regular);
        }

        // ================= 界面 =================

        private void BuildUi()
        {
            SuspendLayout();

            AutoScaleMode = AutoScaleMode.None;
            Text = "远程文本监视器";
            Font = _uiFont;
            BackColor = Color.FromArgb(245, 246, 249);
            ClientSize = new Size(S(1180), S(680));
            MinimumSize = new Size(S(760), S(340));
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            BuildToolbar();
            BuildContent();
            BuildPane();
            BuildStatus();

            // 停靠顺序：后添加的先占边
            Controls.Add(_host);
            Controls.Add(_pane);
            Controls.Add(_ts);
            Controls.Add(_st);

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 3000;
            _timer.Tick += delegate { ScheduleRefresh(false); };

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

            _btnSettings = MakeButton("设置...", "修改连接信息 (Ctrl+O)");
            _btnSettings.Click += delegate { OpenSettingsAndReload(); };

            _btnRefresh = MakeButton("刷新", "立即刷新一次 (F5)");
            _btnRefresh.Click += delegate { ScheduleRefresh(true); };

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
                _cfg.Follow = _chkFollow.Checked;
                _cfg.Save();
            };

            _chkWrap = MakeButton("自动换行", "长行折行显示（否则出现横向滚动条）");
            _chkWrap.CheckOnClick = true;
            _chkWrap.CheckedChanged += delegate { ApplyWrap(); };

            _chkPane = MakeButton("时间线", "显示/隐藏右侧变更时间线 (Ctrl+H)");
            _chkPane.CheckOnClick = true;
            _chkPane.CheckedChanged += delegate
            {
                _pane.Visible = _chkPane.Checked;
                _cfg.ShowTimeline = _chkPane.Checked;
                _cfg.Save();
            };

            _btnFav = MakeButton("收藏", "把当前显示的内容存进收藏夹 (Ctrl+D)");
            _btnFav.Click += delegate { AddFavorite(); };

            _btnFavList = MakeButton("收藏夹", "查看/管理已收藏的内容 (Ctrl+B)");
            _btnFavList.Click += delegate { OpenFavorites(); };

            _btnFontMinus = MakeButton("A-", "缩小内容字号");
            _btnFontMinus.Click += delegate { ChangeFontSize(-1); };

            _btnFontPlus = MakeButton("A+", "放大内容字号");
            _btnFontPlus.Click += delegate { ChangeFontSize(1); };

            _lblHeader = new ToolStripLabel("");
            _lblHeader.Alignment = ToolStripItemAlignment.Right;
            _lblHeader.ForeColor = Color.FromArgb(112, 118, 128);
            _lblHeader.Font = _uiFont;

            _ts.Items.AddRange(new ToolStripItem[]
            {
                _btnSettings,
                new ToolStripSeparator(),
                _btnRefresh,
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
            _rtb.ShowSelectionMargin = true;
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

            _host.Controls.Add(_rtb);
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
            _paneBack = back;

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
            _lblStatus.IsLink = false;
            _lblStatus.Click += delegate { ReturnToLive(); };

            _lblTime = new ToolStripStatusLabel("");
            _lblChanged = new ToolStripStatusLabel("");
            _lblChanged.ForeColor = Color.FromArgb(90, 96, 106);
            _lblSize = new ToolStripStatusLabel("");
            _lblNew = new ToolStripStatusLabel("");
            _lblNew.ForeColor = Color.FromArgb(190, 108, 0);

            _lblSpring = new ToolStripStatusLabel();
            _lblSpring.Spring = true;

            _lblRight = new ToolStripStatusLabel("");
            _lblRight.ForeColor = Color.FromArgb(140, 146, 156);
            _lblRight.Margin = new Padding(0, 0, S(10), 0);

            ToolStripStatusLabel gap1 = new ToolStripStatusLabel("   ");
            ToolStripStatusLabel gap2 = new ToolStripStatusLabel("   ");
            ToolStripStatusLabel gap3 = new ToolStripStatusLabel("   ");
            ToolStripStatusLabel gap4 = new ToolStripStatusLabel("   ");

            _st.Items.AddRange(new ToolStripItem[]
            {
                _lblStatus, gap1, _lblTime, gap2, _lblChanged, gap3, _lblSize, gap4, _lblNew,
                _lblSpring, _lblRight
            });
        }

        // ================= 配置应用 =================

        private void ApplyConfigToUi()
        {
            _chkFollow.Checked = _cfg.Follow;
            _chkWrap.Checked = _cfg.WordWrap;
            _chkPane.Checked = _cfg.ShowTimeline;
            _pane.Visible = _cfg.ShowTimeline;
            ApplyWrap();

            FillIntervals();

            _rtb.Font = _contentFont;
            _lblHeader.Text = _cfg.DisplayName;
            _lblRight.Text = _cfg.DisplayName;

            RefreshTimeline();
        }

        private bool _suppressInterval;

        private void FillIntervals()
        {
            _suppressInterval = true;
            _cmbInterval.Items.Clear();

            bool found = false;
            for (int i = 0; i < Intervals.Length; i++)
            {
                _cmbInterval.Items.Add(Intervals[i] + " 秒");
                if (Intervals[i] == _cfg.IntervalSec)
                {
                    _cmbInterval.SelectedIndex = i;
                    found = true;
                }
            }
            if (!found)
            {
                _cmbInterval.Items.Add(_cfg.IntervalSec + " 秒");
                _cmbInterval.SelectedIndex = _cmbInterval.Items.Count - 1;
            }
            _suppressInterval = false;
        }

        private void ApplyWrap()
        {
            bool wrap = _chkWrap.Checked;
            _rtb.WordWrap = wrap;
            _rtb.ScrollBars = wrap ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both;
            _cfg.WordWrap = wrap;
        }

        private void OnIntervalChanged()
        {
            if (_suppressInterval) return;
            string txt = _cmbInterval.Text;
            int sp = txt.IndexOf(' ');
            if (sp > 0) txt = txt.Substring(0, sp);
            int sec;
            if (!int.TryParse(txt, out sec) || sec < 1) return;
            _cfg.IntervalSec = sec;
            _timer.Interval = sec * 1000;
            _cfg.Save();
        }

        private void ApplyAutoRefresh()
        {
            if (_chkAuto.Checked)
            {
                _timer.Interval = Math.Max(1, _cfg.IntervalSec) * 1000;
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        }

        private void RestoreWindowBounds()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;

            bool hasSaved = (_cfg.WinW > 200 && _cfg.WinH > 150);
            int w = hasSaved ? _cfg.WinW : S(1180);
            int h = hasSaved ? _cfg.WinH : S(680);

            if (w > wa.Width) w = wa.Width - S(40);
            if (h > wa.Height) h = wa.Height - S(40);
            if (w < S(760)) w = Math.Min(S(760), wa.Width);
            if (h < S(340)) h = Math.Min(S(340), wa.Height);

            int x = wa.Left + (wa.Width - w) / 2;
            int y = wa.Top + (wa.Height - h) / 2;

            if (_cfg.WinX >= -10000 && _cfg.WinY >= -10000 && hasSaved)
            {
                Rectangle r = new Rectangle(_cfg.WinX, _cfg.WinY, w, h);
                bool visible = false;
                for (int i = 0; i < Screen.AllScreens.Length; i++)
                {
                    Rectangle inter = Rectangle.Intersect(Screen.AllScreens[i].WorkingArea, r);
                    if (inter.Width >= S(120) && inter.Height >= S(60)) { visible = true; break; }
                }
                if (visible) { x = _cfg.WinX; y = _cfg.WinY; }
            }

            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(x, y, w, h);

            if (_cfg.WinMax) WindowState = FormWindowState.Maximized;
        }

        // ================= 生命周期 =================

        private void OnShown(object sender, EventArgs e)
        {
            if (!_cfg.IsReady)
            {
                if (!OpenSettings())
                {
                    SetStatus("● 未配置 — 请点「设置...」", Color.FromArgb(150, 150, 155));
                    return;
                }
            }
            ResetConnection();
            if (_chkAuto.Checked) _timer.Start();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            _closing = true;
            if (_timer != null) _timer.Stop();

            if (WindowState == FormWindowState.Normal)
            {
                _cfg.WinX = Left;
                _cfg.WinY = Top;
                _cfg.WinW = Width;
                _cfg.WinH = Height;
                _cfg.WinMax = false;
            }
            else
            {
                _cfg.WinMax = (WindowState == FormWindowState.Maximized);
            }
            _cfg.Save();

            SftpFetcher f = _fetcher;
            _fetcher = null;
            if (f != null) { try { f.Dispose(); } catch { } }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5) { ScheduleRefresh(true); return true; }
            if (keyData == Keys.Escape) { if (_viewing != null || _pinnedLabel.Length > 0) { ReturnToLive(); return true; } }
            if (keyData == (Keys.Control | Keys.O)) { OpenSettingsAndReload(); return true; }
            if (keyData == (Keys.Control | Keys.D)) { AddFavorite(); return true; }
            if (keyData == (Keys.Control | Keys.B)) { OpenFavorites(); return true; }
            if (keyData == (Keys.Control | Keys.H))
            {
                _chkPane.Checked = !_chkPane.Checked;
                return true;
            }
            if (keyData == (Keys.Control | Keys.Add)) { ChangeFontSize(1); return true; }
            if (keyData == (Keys.Control | Keys.Subtract)) { ChangeFontSize(-1); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ================= 设置 =================

        private bool OpenSettings()
        {
            string sigOld = Signature();

            using (SettingsForm dlg = new SettingsForm(_cfg, _scale))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return false;

                AppConfig nc = dlg.Result;
                nc.WinX = _cfg.WinX;
                nc.WinY = _cfg.WinY;
                nc.WinW = _cfg.WinW;
                nc.WinH = _cfg.WinH;
                nc.WinMax = _cfg.WinMax;
                nc.ShowTimeline = _cfg.ShowTimeline;
                _cfg = nc;
                _cfg.Save();

                CreateFonts();
                Font = _uiFont;
                _ts.Font = _uiFont;
                _st.Font = _uiFont;
                _rtb.Font = _contentFont;
                _paneList.Font = _monoFont;
                ApplyConfigToUi();

                if (sigOld != Signature())
                {
                    // 换了服务器或文件，旧历史不再适用
                    _changeLog.Clear();
                    RefreshTimeline();
                }
                return true;
            }
        }

        private string Signature()
        {
            return _cfg.Host + "|" + _cfg.Port + "|" + _cfg.User + "|" + _cfg.RemotePath;
        }

        private void OpenSettingsAndReload()
        {
            if (OpenSettings()) ResetConnection();
        }

        private void ChangeFontSize(int delta)
        {
            float nf = _cfg.FontSize + delta;
            if (nf < 7f) nf = 7f;
            if (nf > 30f) nf = 30f;
            if (nf == _cfg.FontSize) return;

            _cfg.FontSize = nf;
            _contentFont = SafeFont("Consolas", _cfg.FontSize, FontStyle.Regular);
            _rtb.Font = _contentFont;
            _cfg.Save();
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

        private string CurrentVisibleText()
        {
            if (_viewing != null) return _changeLog.GetText(_viewing);
            return _lastText;
        }

        // ================= 时间线与收藏 =================

        private void RefreshTimeline()
        {
            List<ChangeEntry> all = _changeLog.Snapshot();

            _suppressPaneSelect = true;
            _paneList.BeginUpdate();
            try
            {
                _paneList.Items.Clear();
                for (int i = all.Count - 1; i >= 0; i--)   // 新的在上
                    _paneList.Items.Add(new EntryItem(all[i], FormatEntryLabel(all[i])));

                if (_viewing != null)
                {
                    for (int i = 0; i < _paneList.Items.Count; i++)
                    {
                        EntryItem it = (EntryItem)_paneList.Items[i];
                        if (it.Entry.Id == _viewing.Id) { _paneList.SelectedIndex = i; break; }
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
            if (e.IsBaseline) what = "初始 " + e.TotalLines.ToString() + "行";
            else if (e.NewLines > 0) what = "+" + e.NewLines.ToString() + "行";
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

        private void ShowHistoryEntry(ChangeEntry e)
        {
            if (e == null) { ReturnToLive(); return; }

            _viewing = e;
            _pinnedLabel = "";

            _rtb.BackColor = Color.FromArgb(255, 252, 238);
            _rtb.Text = _changeLog.GetText(e);

            _lblStatus.Text = "◀ 历史 " + e.LocalTime.ToString("HH:mm:ss") + " · 点此返回实时";
            _lblStatus.ForeColor = Color.FromArgb(190, 108, 0);
            _lblStatus.ToolTipText = "正在查看 " + e.LocalTime.ToString("yyyy-MM-dd HH:mm:ss")
                + " 的快照，点这里或按 Esc 返回实时";

            _lblChanged.Text = "更改 " + FormatMtime(e.ServerMtime);
            _lblChanged.ToolTipText = "该快照对应的服务器文件修改时间";
            _lblSize.Text = FormatSize(e.Bytes) + " · " + e.TotalLines + " 行";
            _lblNew.Text = e.IsBaseline ? "初始快照" : (e.NewLines > 0 ? "当时新增 " + e.NewLines + " 行" : "内容变化");
            _lblNew.ForeColor = Color.FromArgb(150, 120, 40);

            ScrollToBottom();
        }

        private void ShowPinnedText(string label, string text)
        {
            _viewing = null;
            _pinnedLabel = label;

            _rtb.BackColor = Color.FromArgb(245, 248, 255);
            _rtb.Text = text ?? "";
            _rtb.SelectionStart = 0;
            _rtb.ScrollToCaret();

            _lblStatus.Text = "★ 收藏：" + label + " · 点此返回实时";
            _lblStatus.ForeColor = Color.FromArgb(40, 90, 190);
            _lblStatus.ToolTipText = "正在查看收藏内容，点这里或按 Esc 返回实时";

            _lblChanged.Text = "";
            _lblSize.Text = FormatSize(Encoding.UTF8.GetByteCount(text ?? "")) + " · " + CountLines(text ?? "") + " 行";
            _lblNew.Text = "";
        }

        private void ReturnToLive()
        {
            if (_viewing == null && _pinnedLabel.Length == 0) return;

            _viewing = null;
            _pinnedLabel = "";
            _rtb.BackColor = Color.White;

            SetStatus("● 已连接", Color.FromArgb(22, 130, 62));
            _lblStatus.ToolTipText = "";

            _rtb.Text = _lastText;
            ScrollToBottom();

            // 状态栏要回到"实时"的样子，别留着历史视图的残留文字
            _lblNew.ForeColor = Color.FromArgb(190, 108, 0);
            _lblNew.Text = "";

            if (_lastSnap != null)
            {
                _lblChanged.Text = "更改 " + FormatMtime(_lastSnap.ModifiedUtc);
                _lblChanged.ToolTipText = "服务器文件最后修改时间："
                    + _lastSnap.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                UpdateSizeLabel(_lastText, _lastSnap.Size);
            }
            else
            {
                _lblChanged.Text = "";
                UpdateSizeLabel(_lastText, Encoding.UTF8.GetByteCount(_lastText));
            }

            _suppressPaneSelect = true;
            _paneList.ClearSelected();
            _suppressPaneSelect = false;
        }

        private static string FormatMtime(DateTime utc)
        {
            DateTime t = utc.ToLocalTime();
            if (t.Date == DateTime.Now.Date) return t.ToString("HH:mm:ss");
            return t.ToString("MM-dd HH:mm:ss");
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
            if (_viewing != null) guess = _viewing.LocalTime.ToString("yyyy-MM-dd HH:mm:ss");

            using (PromptForm dlg = new PromptForm("收藏当前内容",
                       "收藏名称（可直接用时间，也可自己起名）：", guess, _scale))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                string name = (dlg.Value == null) ? "" : dlg.Value.Trim();
                if (name.Length == 0) name = guess;

                string src = _cfg.DisplayName;
                if (_viewing != null) src += " @ " + _viewing.LocalTime.ToString("HH:mm:ss");
                else if (_pinnedLabel.Length > 0) src += " @ 收藏";

                FavoriteItem f = _favs.Add(name, text, src, CountLines(text));
                _lblNew.ForeColor = Color.FromArgb(40, 140, 70);
                _lblNew.Text = "★ 已收藏：" + f.Name;
            }
        }

        private void OpenFavorites()
        {
            using (FavoritesForm dlg = new FavoritesForm(_favs, _scale, _cfg.DisplayName))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.LastViewed != null)
                    ShowPinnedText(dlg.LastViewed.Name, dlg.LastViewedText);
            }
        }

        // ================= 刷新核心 =================

        private void ResetConnection()
        {
            if (_viewing != null || _pinnedLabel.Length > 0) ReturnToLive();

            _lastStat = null;
            _lastText = "";
            _rtb.Clear();
            _lblNew.Text = "";
            _lblSize.Text = "";
            _lblTime.Text = "";
            _lblChanged.Text = "";
            _lblHeader.Text = _cfg.DisplayName;
            _lblRight.Text = _cfg.DisplayName;
            _lblRight.ForeColor = Color.FromArgb(140, 146, 156);

            SftpFetcher old = null;
            lock (_netLock) { old = _fetcher; _fetcher = null; }
            if (old != null) { try { old.Dispose(); } catch { } }

            _busy = false;
            ScheduleRefresh(true);
        }

        private void ScheduleRefresh(bool manual)
        {
            if (!_cfg.IsReady) return;
            if (_closing) return;
            if (_busy) return;

            _busy = true;
            AppConfig cfg = _cfg;
            bool force = manual;

            if (manual) SetStatus("● 正在刷新…", Color.FromArgb(176, 122, 20));

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                Snapshot snap = null;
                string err = null;
                bool changed = false;
                string learnedFp = null;

                lock (_netLock)
                {
                    try
                    {
                        if (_fetcher == null) _fetcher = new SftpFetcher(cfg);

                        RemoteStat st = _fetcher.Stat();
                        bool needRead = force
                                        || _lastStat == null
                                        || st.Size != _lastStat.Size
                                        || st.ModifiedUtc != _lastStat.ModifiedUtc;

                        if (needRead)
                        {
                            snap = _fetcher.Read();
                            RemoteStat ns = new RemoteStat();
                            ns.Size = snap.Size;
                            ns.ModifiedUtc = snap.ModifiedUtc;
                            _lastStat = ns;
                            changed = true;
                        }

                        if (_fetcher.LearnedHostKeyFp.Length > 0)
                        {
                            learnedFp = _fetcher.LearnedHostKeyFp;
                            _fetcher.LearnedHostKeyFp = "";
                        }
                    }
                    catch (Exception ex)
                    {
                        err = SftpFetcher.DescribeError(ex);
                        if (_fetcher != null) { try { _fetcher.Dispose(); } catch { } _fetcher = null; }
                        _lastStat = null;
                    }
                }

                try
                {
                    BeginInvoke(new Action(delegate { OnRefreshDone(snap, changed, err, learnedFp); }));
                }
                catch { }
            });
        }

        private void OnRefreshDone(Snapshot snap, bool changed, string err, string learnedFp)
        {
            _busy = false;
            if (_closing) return;

            _lblTime.Text = "刷新 " + DateTime.Now.ToString("HH:mm:ss");

            if (err != null)
            {
                SetStatus("● 连接失败", Color.FromArgb(198, 40, 40));
                _lblStatus.ToolTipText = err;
                _lblRight.Text = err;
                _lblRight.ForeColor = Color.FromArgb(198, 40, 40);
                _lblHeader.Text = _cfg.DisplayName + "  (离线)";
                return;
            }

            if (!string.IsNullOrEmpty(learnedFp))
            {
                // 首次连接记录服务器指纹，之后每次连接都会比对
                _cfg.HostKeyFp = learnedFp;
                _cfg.Save();
            }

            if (_viewing == null && _pinnedLabel.Length == 0)
            {
                SetStatus("● 已连接", Color.FromArgb(22, 130, 62));
                _lblStatus.ToolTipText = "";
            }
            _lblRight.Text = _cfg.DisplayName;
            _lblRight.ForeColor = Color.FromArgb(140, 146, 156);
            _lblHeader.Text = _cfg.DisplayName;

            if (changed && snap != null) ApplySnapshot(snap);
        }

        private void SetStatus(string text, Color color)
        {
            _lblStatus.Text = text;
            _lblStatus.ForeColor = color;
        }

        private void ApplySnapshot(Snapshot snap)
        {
            string oldText = _lastText;
            string newText = snap.Text ?? "";

            bool same = (newText == oldText);
            bool pureAppend = false;
            int oldLines = 0;

            if (oldText.Length > 0 && newText.Length > oldText.Length
                && string.CompareOrdinal(newText, 0, oldText, 0, oldText.Length) == 0)
            {
                pureAppend = true;
                oldLines = CountLines(oldText);
            }

            int lines = CountLines(newText);
            bool baseline = (oldText.Length == 0);

            bool atBottom = _chkFollow.Checked || IsLastLineVisible();

            if (!same)
            {
                // 只在"实时视图"下刷新正文；用户正在看历史/收藏时别打断他
                if (_viewing == null && _pinnedLabel.Length == 0)
                {
                    _rtb.Text = newText;
                    if (pureAppend && oldLines > 0) HighlightFromLine(oldLines);
                    if (atBottom) ScrollToBottom();
                }
            }

            _lastText = newText;
            _lastSnap = snap;

            UpdateSizeLabel(newText, snap.Size);

            _lblChanged.Text = "更改 " + FormatMtime(snap.ModifiedUtc);
            _lblChanged.ToolTipText = "服务器文件最后修改时间："
                + snap.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                + "   编码 " + snap.EncodingName;

            if (_viewing == null && _pinnedLabel.Length == 0)
            {
                if (!same && pureAppend && lines > oldLines)
                {
                    _lblNew.ForeColor = Color.FromArgb(190, 108, 0);
                    _lblNew.Text = "▲ 新增 " + (lines - oldLines) + " 行";
                }
                else if (!same && !baseline)
                {
                    _lblNew.ForeColor = Color.FromArgb(190, 108, 0);
                    _lblNew.Text = "内容已变化";
                }
                else if (baseline)
                {
                    _lblNew.Text = "";
                }
            }

            if (!same)
            {
                // 重启后第一次拉取时，若内容和历史最后一条一样，就不必再记一条"初始"
                bool skipRecord = false;
                if (baseline)
                {
                    List<ChangeEntry> hist = _changeLog.Snapshot();
                    if (hist.Count > 0)
                    {
                        ChangeEntry lastEntry = hist[hist.Count - 1];
                        if (_changeLog.GetText(lastEntry) == newText) skipRecord = true;
                    }
                }

                if (!skipRecord)
                {
                    int delta = baseline ? -1 : (pureAppend ? (lines - oldLines) : -1);
                    _changeLog.Add(newText, snap.ModifiedUtc, delta, lines, baseline);
                    RefreshTimeline();
                }
            }
        }

        private void UpdateSizeLabel(string text, long bytes)
        {
            _lblSize.Text = FormatSize(bytes) + " · " + CountLines(text) + " 行";
        }

        private void HighlightFromLine(int lineIndex)
        {
            if (lineIndex <= 0) return;
            int ch = _rtb.GetFirstCharIndexFromLine(lineIndex);
            if (ch < 0) return;
            int len = _rtb.TextLength - ch;
            if (len <= 0) return;

            _rtb.Select(ch, len);
            _rtb.SelectionBackColor = Color.FromArgb(255, 243, 160);
            _rtb.SelectionLength = 0;
            _rtb.SelectionStart = ch;
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

        private void ScrollToBottom()
        {
            _rtb.SelectionStart = _rtb.TextLength;
            _rtb.SelectionLength = 0;
            _rtb.ScrollToCaret();
        }

        private static int CountLines(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 1;
            for (int i = 0; i < s.Length; i++)
                if (s[i] == '\n') n++;
            if (s[s.Length - 1] == '\n') n--;
            if (n < 0) n = 0;
            return n;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("0.00") + " MB";
        }
    }
}
