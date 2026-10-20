using System;
using System.Drawing;
using System.Windows.Forms;

namespace RemoteTxtViewer
{
    public class SettingsForm : Form
    {
        public AppConfig Result;

        private readonly AppConfig _cfg;
        private readonly float _scale;
        private Font _uiFont;

        private TextBox _txtHost, _txtPort, _txtUser, _txtPass, _txtKey, _txtKeyPass, _txtRemote;
        private RadioButton _rbPass, _rbKey;
        private CheckBox _chkShowPass, _chkWrap;
        private ComboBox _cmbInterval, _cmbEncoding;
        private Label _lblTest;
        private Button _btnTest, _btnOk, _btnCancel, _btnResetFp;
        private string _hostKeyFp = "";

        private static readonly int[] Intervals = new int[] { 1, 2, 3, 5, 10, 30, 60 };

        public SettingsForm(AppConfig cfg, float scale)
        {
            _cfg = cfg;
            _scale = scale;
            Build();
        }

        private int S(int v) { return (int)Math.Round(v * _scale); }

        private Font MakeUiFont()
        {
            try { return new Font("Microsoft YaHei UI", 9f * _scale); }
            catch
            {
                try { return new Font(FontFamily.GenericSansSerif, 9f * _scale); }
                catch { return SystemFonts.DefaultFont; }
            }
        }

        // ---------- 布局助手 ----------

        private void AddLabel(string text, int x, int y, int w, int h)
        {
            Label l = new Label();
            l.Text = text;
            l.TextAlign = ContentAlignment.MiddleRight;
            l.Bounds = new Rectangle(x, y, w, h);
            l.ForeColor = Color.FromArgb(52, 58, 66);
            Controls.Add(l);
        }

        private void AddSmallLabel(string text, int x, int y, int w, int h)
        {
            Label l = new Label();
            l.Text = text;
            l.TextAlign = ContentAlignment.MiddleRight;
            l.Bounds = new Rectangle(x, y, w, h);
            l.ForeColor = Color.FromArgb(90, 96, 104);
            Controls.Add(l);
        }

        private TextBox MakeText(string value, bool password)
        {
            TextBox t = new TextBox();
            t.Text = value ?? "";
            t.AutoSize = false;
            t.BorderStyle = BorderStyle.FixedSingle;
            if (password) t.UseSystemPasswordChar = true;
            return t;
        }

        private void PlaceField(Control c, int x, int y, int w, int rowH)
        {
            int h = Math.Min(S(25), rowH - S(6));
            c.Bounds = new Rectangle(x, y + (rowH - h) / 2, w, h);
            Controls.Add(c);
        }

        private Button MakeButton(string text, int x, int y, int w, int h, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.Bounds = new Rectangle(x, y, w, h);
            if (onClick != null) b.Click += onClick;
            Controls.Add(b);
            return b;
        }

        // ---------- 界面 ----------

        private void Build()
        {
            _uiFont = MakeUiFont();

            int pad = S(16);
            int labelW = S(88);
            int rowH = S(32);
            int fieldX = pad + labelW + S(10);
            int clientW = S(640);
            int rightEdge = clientW - pad;
            int rows = 10;
            int resultH = S(26);
            int btnH = S(30);
            int clientH = pad + rowH * rows + resultH + S(12) + btnH + S(12);

            AutoScaleMode = AutoScaleMode.None;
            Font = _uiFont;
            Text = "连接设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(250, 251, 253);
            ClientSize = new Size(clientW, clientH);

            _hostKeyFp = _cfg.HostKeyFp;

            int y = pad;

            // ---- 0 主机地址 + 端口 ----
            AddLabel("主机地址", pad, y, labelW, rowH);
            int portW = S(58);
            int portLblW = S(36);
            int hostW = rightEdge - portW - portLblW - S(16) - fieldX;
            _txtHost = MakeText(_cfg.Host, false);
            PlaceField(_txtHost, fieldX, y, hostW, rowH);
            AddSmallLabel("端口", rightEdge - portW - portLblW - S(4), y, portLblW, rowH);
            _txtPort = MakeText(_cfg.Port > 0 ? _cfg.Port.ToString() : "22", false);
            PlaceField(_txtPort, rightEdge - portW, y, portW, rowH);
            y += rowH;

            // ---- 1 用户名 ----
            AddLabel("用户名", pad, y, labelW, rowH);
            _txtUser = MakeText(_cfg.User, false);
            PlaceField(_txtUser, fieldX, y, rightEdge - fieldX, rowH);
            y += rowH;

            // ---- 2 认证方式 ----
            AddLabel("认证方式", pad, y, labelW, rowH);
            _rbPass = new RadioButton();
            _rbPass.Text = "密码";
            _rbPass.AutoSize = true;
            _rbPass.Checked = !_cfg.UseKey;
            _rbPass.CheckedChanged += delegate { SyncAuthUi(); };
            Controls.Add(_rbPass);

            _rbKey = new RadioButton();
            _rbKey.Text = "私钥文件";
            _rbKey.AutoSize = true;
            _rbKey.Checked = _cfg.UseKey;
            _rbKey.CheckedChanged += delegate { SyncAuthUi(); };
            Controls.Add(_rbKey);

            _rbPass.Location = new Point(fieldX, y + (rowH - _rbPass.PreferredSize.Height) / 2);
            _rbKey.Location = new Point(fieldX + _rbPass.PreferredSize.Width + S(18),
                                       y + (rowH - _rbKey.PreferredSize.Height) / 2);
            y += rowH;

            // ---- 3 密码 ----
            int bw = S(76);
            AddLabel("密码", pad, y, labelW, rowH);
            _txtPass = MakeText(_cfg.Password, true);
            PlaceField(_txtPass, fieldX, y, rightEdge - bw - S(8) - fieldX, rowH);

            _chkShowPass = new CheckBox();
            _chkShowPass.Text = "显示";
            _chkShowPass.AutoSize = true;
            _chkShowPass.CheckedChanged += delegate
            {
                _txtPass.UseSystemPasswordChar = !_chkShowPass.Checked;
            };
            Controls.Add(_chkShowPass);
            _chkShowPass.Location = new Point(rightEdge - S(52),
                y + (rowH - _chkShowPass.PreferredSize.Height) / 2);
            y += rowH;

            // ---- 4 私钥文件 ----
            AddLabel("私钥文件", pad, y, labelW, rowH);
            _txtKey = MakeText(_cfg.KeyFile, false);
            PlaceField(_txtKey, fieldX, y, rightEdge - bw - S(8) - fieldX, rowH);
            MakeButton("浏览...", rightEdge - bw, y + (rowH - S(25)) / 2, bw, S(25),
                delegate
                {
                    using (OpenFileDialog d = new OpenFileDialog())
                    {
                        d.Title = "选择私钥文件";
                        d.Filter = "私钥文件 (*.pem;*.ppk;*.key;id_*)|*.pem;*.ppk;*.key;id_*|所有文件 (*.*)|*.*";
                        if (d.ShowDialog(this) == DialogResult.OK) _txtKey.Text = d.FileName;
                    }
                });
            y += rowH;

            // ---- 5 私钥口令 ----
            AddLabel("私钥口令", pad, y, labelW, rowH);
            _txtKeyPass = MakeText(_cfg.KeyPass, true);
            PlaceField(_txtKeyPass, fieldX, y, rightEdge - fieldX, rowH);
            y += rowH;

            // ---- 6 远程文件 ----
            AddLabel("远程文件", pad, y, labelW, rowH);
            _txtRemote = MakeText(_cfg.RemotePath, false);
            PlaceField(_txtRemote, fieldX, y, rightEdge - bw - S(8) - fieldX, rowH);
            MakeButton("浏览...", rightEdge - bw, y + (rowH - S(25)) / 2, bw, S(25),
                delegate
                {
                    AppConfig tmp = Collect();
                    if (string.IsNullOrEmpty(tmp.Host) || string.IsNullOrEmpty(tmp.User))
                    {
                        MessageBox.Show(this, "请先填写主机地址和用户名。", "提示",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    using (RemotePickerForm dlg = new RemotePickerForm(tmp, _scale, tmp.RemotePath))
                    {
                        if (dlg.ShowDialog(this) == DialogResult.OK)
                            _txtRemote.Text = dlg.SelectedPath;
                    }
                });
            y += rowH;

            // ---- 7 刷新间隔 ----
            AddLabel("刷新间隔", pad, y, labelW, rowH);
            _cmbInterval = new ComboBox();
            _cmbInterval.DropDownStyle = ComboBoxStyle.DropDownList;
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
            PlaceField(_cmbInterval, fieldX, y, S(150), rowH);
            AddSmallLabel("秒（建议 2~5 秒，文件不大时开销很小）",
                fieldX + S(160), y, rightEdge - fieldX - S(160), rowH);
            y += rowH;

            // ---- 8 文件编码 ----
            AddLabel("文件编码", pad, y, labelW, rowH);
            _cmbEncoding = new ComboBox();
            _cmbEncoding.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbEncoding.Items.Add("自动识别（推荐）");
            _cmbEncoding.Items.Add("UTF-8");
            _cmbEncoding.Items.Add("GBK / ANSI");
            if (_cfg.EncodingPref == "utf-8") _cmbEncoding.SelectedIndex = 1;
            else if (_cfg.EncodingPref == "gbk") _cmbEncoding.SelectedIndex = 2;
            else _cmbEncoding.SelectedIndex = 0;
            PlaceField(_cmbEncoding, fieldX, y, S(150), rowH);
            y += rowH;

            // ---- 9 显示选项 ----
            AddLabel("显示", pad, y, labelW, rowH);
            _chkWrap = new CheckBox();
            _chkWrap.Text = "内容自动换行（不勾选则保留横向滚动条，长行不折行）";
            _chkWrap.AutoSize = true;
            _chkWrap.Checked = _cfg.WordWrap;
            Controls.Add(_chkWrap);
            _chkWrap.Location = new Point(fieldX,
                y + (rowH - _chkWrap.PreferredSize.Height) / 2);
            y += rowH;

            // ---- 测试结果 ----
            _lblTest = new Label();
            _lblTest.Text = "";
            _lblTest.AutoSize = false;
            _lblTest.Bounds = new Rectangle(fieldX, y + S(2), rightEdge - fieldX, resultH);
            _lblTest.AutoEllipsis = true;
            _lblTest.ForeColor = Color.FromArgb(120, 124, 130);
            Controls.Add(_lblTest);

            // ---- 按钮 ----
            int by = y + resultH + S(12);
            int okW = S(80);
            int cancelW = S(80);
            int testW = S(92);

            _btnCancel = MakeButton("取消", rightEdge - cancelW, by, cancelW, btnH,
                delegate { DialogResult = DialogResult.Cancel; Close(); });
            _btnOk = MakeButton("保存", rightEdge - cancelW - okW - S(8), by, okW, btnH,
                delegate { Save(); });
            _btnTest = MakeButton("测试连接",
                rightEdge - cancelW - okW - testW - S(24), by, testW, btnH,
                delegate { TestConnection(); });

            _btnResetFp = MakeButton("重置主机指纹", pad, by, S(104), btnH,
                delegate { ResetFingerprint(); });
            _btnResetFp.Enabled = (_hostKeyFp.Length > 0);

            CancelButton = _btnCancel;
            AcceptButton = _btnOk;

            Shown += delegate { _txtHost.Focus(); };
        }

        private void SyncAuthUi()
        {
            bool useKey = _rbKey.Checked;
            _txtPass.Enabled = !useKey;
            _chkShowPass.Enabled = !useKey;
            _txtKey.Enabled = useKey;
            _txtKeyPass.Enabled = useKey;
        }

        // ---------- 数据 ----------

        private AppConfig Collect()
        {
            AppConfig c = _cfg.Clone();
            c.Host = _txtHost.Text.Trim();

            int port;
            if (int.TryParse(_txtPort.Text.Trim(), out port) && port > 0 && port < 65536)
                c.Port = port;
            else
                c.Port = 22;

            c.User = _txtUser.Text.Trim();
            c.Password = _txtPass.Text;
            c.UseKey = _rbKey.Checked;
            c.KeyFile = _txtKey.Text.Trim();
            c.KeyPass = _txtKeyPass.Text;
            c.RemotePath = _txtRemote.Text.Trim();
            c.WordWrap = _chkWrap.Checked;

            string it = _cmbInterval.Text;
            int sp = it.IndexOf(' ');
            if (sp > 0) it = it.Substring(0, sp);
            int sec;
            c.IntervalSec = (int.TryParse(it, out sec) && sec >= 1) ? sec : 3;

            c.HostKeyFp = _hostKeyFp;

            if (_cmbEncoding.SelectedIndex == 1) c.EncodingPref = "utf-8";
            else if (_cmbEncoding.SelectedIndex == 2) c.EncodingPref = "gbk";
            else c.EncodingPref = "auto";

            return c;
        }

        private void Save()
        {
            AppConfig c = Collect();

            if (string.IsNullOrEmpty(c.Host))
            {
                MessageBox.Show(this, "请填写主机地址。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _txtHost.Focus();
                return;
            }
            if (string.IsNullOrEmpty(c.User))
            {
                MessageBox.Show(this, "请填写用户名。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _txtUser.Focus();
                return;
            }
            if (c.UseKey && string.IsNullOrEmpty(c.KeyFile))
            {
                MessageBox.Show(this, "已选择私钥认证，请指定私钥文件。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                _txtKey.Focus();
                return;
            }
            if (!c.UseKey && c.Password.Length == 0)
            {
                if (MessageBox.Show(this, "密码为空，确定继续吗？", "确认",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    _txtPass.Focus();
                    return;
                }
            }
            if (string.IsNullOrEmpty(c.RemotePath))
            {
                MessageBox.Show(this, "请填写远程文件路径，例如 /var/log/app.log",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _txtRemote.Focus();
                return;
            }

            Result = c;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ResetFingerprint()
        {
            if (_hostKeyFp.Length == 0)
            {
                _lblTest.ForeColor = Color.FromArgb(120, 124, 130);
                _lblTest.Text = "当前还没有记录指纹 —— 下次连接成功时会自动记录。";
                return;
            }

            string msg = "服务器指纹用来防止有人冒充你的服务器（中间人攻击）。\r\n\r\n"
                       + "只有在【服务器确实重装过系统】或【换了 SSH 主机密钥】时才需要重置。\r\n\r\n"
                       + "当前记录的指纹：\r\n" + _hostKeyFp + "\r\n\r\n确定清除吗？";

            if (MessageBox.Show(this, msg, "重置主机指纹",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            _hostKeyFp = "";
            _btnResetFp.Enabled = false;
            _lblTest.ForeColor = Color.FromArgb(30, 120, 60);
            _lblTest.Text = "指纹已清除，下次连接成功后会重新记录（请确认这次连的是真服务器）。";
        }

        private static string ShortFp(string fp)
        {
            if (string.IsNullOrEmpty(fp)) return "(空)";
            if (fp.Length <= 24) return fp;
            return fp.Substring(0, 12) + "…" + fp.Substring(fp.Length - 8);
        }

        private void TestConnection()
        {
            AppConfig tmp = Collect();
            if (string.IsNullOrEmpty(tmp.Host))
            {
                _lblTest.ForeColor = Color.FromArgb(198, 40, 40);
                _lblTest.Text = "请先填写主机地址。";
                return;
            }

            _btnTest.Enabled = false;
            _lblTest.ForeColor = Color.FromArgb(120, 124, 130);
            _lblTest.Text = "正在连接 " + tmp.Host + " …";

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string msg;
                bool ok = false;
                try
                {
                    using (SftpFetcher f = new SftpFetcher(tmp))
                    {
                        RemoteStat st = f.Stat();
                        msg = "连接成功 · " + FmtSize(st.Size) + " · 指纹 " + ShortFp(f.CurrentHostKeyFp)
                              + " · 修改于 " + st.ModifiedUtc.ToLocalTime().ToString("MM-dd HH:mm:ss");
                        ok = true;
                    }
                }
                catch (Exception ex)
                {
                    msg = "失败： " + SftpFetcher.DescribeError(ex);
                }

                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (IsDisposed) return;
                        _btnTest.Enabled = true;
                        _lblTest.ForeColor = ok ? Color.FromArgb(22, 130, 62) : Color.FromArgb(198, 40, 40);
                        _lblTest.Text = msg;
                    }));
                }
                catch { }
            });
        }

        private static string FmtSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("0.00") + " MB";
        }
    }
}
