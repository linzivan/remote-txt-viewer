using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RemoteTxtViewer
{
    public class RemotePickerForm : Form
    {
        public string SelectedPath;

        private readonly AppConfig _cfg;
        private readonly float _scale;
        private Font _uiFont;

        private TextBox _txtPath;
        private ListBox _list;
        private Label _lblInfo;
        private Button _btnUp, _btnOk, _btnCancel;

        private SftpFetcher _fetcher;
        private readonly object _lock = new object();
        private string _current = "/";
        private bool _loading;

        private class EntryItem
        {
            public DirEntry Entry;
            public EntryItem(DirEntry e) { Entry = e; }
            public override string ToString()
            {
                if (Entry.IsDir) return "[ " + Entry.Name + " ]";
                return "    " + Entry.Name + "        " + FmtSize(Entry.Size);
            }
        }

        public RemotePickerForm(AppConfig cfg, float scale, string startPath)
        {
            _cfg = cfg.Clone();
            _scale = scale;
            Build();
            if (!string.IsNullOrEmpty(startPath))
            {
                int slash = startPath.LastIndexOf('/');
                if (slash > 0) _current = startPath.Substring(0, slash);
                else if (slash == 0) _current = "/";
            }
        }

        private int S(int v) { return (int)Math.Round(v * _scale); }

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
            BackColor = Color.FromArgb(250, 251, 253);
            ClientSize = new Size(S(640), S(460));
            MinimumSize = new Size(S(440), S(320));

            // ---- 列表（填充） ----
            _list = new ListBox();
            _list.Dock = DockStyle.Fill;
            _list.Font = new Font("Consolas", 9.75f * _scale);
            _list.IntegralHeight = false;
            _list.BorderStyle = BorderStyle.None;
            _list.DoubleClick += delegate
            {
                EntryItem it = _list.SelectedItem as EntryItem;
                if (it == null) return;
                if (it.Entry.IsDir) Navigate(Combine(_current, it.Entry.Name));
                else Accept(it.Entry.Name);
            };

            // ---- 状态行 ----
            _lblInfo = new Label();
            _lblInfo.Dock = DockStyle.Bottom;
            _lblInfo.Height = S(26);
            _lblInfo.TextAlign = ContentAlignment.MiddleLeft;
            _lblInfo.Padding = new Padding(S(12), 0, S(12), 0);
            _lblInfo.AutoEllipsis = true;
            _lblInfo.ForeColor = Color.FromArgb(120, 124, 130);
            _lblInfo.BackColor = Color.FromArgb(250, 251, 253);

            // ---- 底部按钮 ----
            FlowLayoutPanel bp = new FlowLayoutPanel();
            bp.Dock = DockStyle.Bottom;
            bp.Height = S(46);
            bp.FlowDirection = FlowDirection.RightToLeft;
            bp.Padding = new Padding(S(10), S(8), S(10), S(8));
            bp.BackColor = Color.FromArgb(250, 251, 253);

            _btnCancel = new Button();
            _btnCancel.Text = "取消";
            _btnCancel.Size = new Size(S(82), S(28));
            _btnCancel.Margin = new Padding(S(8), 0, 0, 0);
            _btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            _btnOk = new Button();
            _btnOk.Text = "选择";
            _btnOk.Size = new Size(S(82), S(28));
            _btnOk.Margin = new Padding(S(8), 0, 0, 0);
            _btnOk.Click += delegate
            {
                EntryItem it = _list.SelectedItem as EntryItem;
                if (it != null && !it.Entry.IsDir)
                {
                    Accept(it.Entry.Name);
                }
                else
                {
                    string p = _txtPath.Text.Trim();
                    if (p.Length > 0 && p != _current)
                    {
                        SelectedPath = p;
                        DialogResult = DialogResult.OK;
                        Close();
                    }
                    else
                    {
                        _lblInfo.ForeColor = Color.FromArgb(198, 40, 40);
                        _lblInfo.Text = "请双击列表中的一个文件来选择它。";
                    }
                }
            };

            Button bNew = new Button();
            bNew.Text = "新建文件...";
            bNew.Size = new Size(S(96), S(28));
            bNew.Margin = new Padding(S(8), 0, 0, 0);
            bNew.Click += delegate { NewRemoteFile(); };

            bp.Controls.Add(_btnCancel);   // RightToLeft：先加的在最右
            bp.Controls.Add(bNew);
            bp.Controls.Add(_btnOk);

            // ---- 顶部路径行 ----
            Panel pp = new Panel();
            pp.Dock = DockStyle.Top;
            pp.Height = S(40);
            pp.Padding = new Padding(S(10), S(8), S(10), S(6));
            pp.BackColor = Color.FromArgb(250, 251, 253);

            _btnUp = new Button();
            _btnUp.Text = "上一层";
            _btnUp.Dock = DockStyle.Right;
            _btnUp.Width = S(86);
            _btnUp.Click += delegate { GoUp(); };

            _txtPath = new TextBox();
            _txtPath.Dock = DockStyle.Fill;
            _txtPath.AutoSize = false;
            _txtPath.BorderStyle = BorderStyle.FixedSingle;
            _txtPath.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    Navigate(_txtPath.Text.Trim());
                }
            };

            pp.Controls.Add(_txtPath);
            pp.Controls.Add(_btnUp);

            // 停靠顺序：Fill 最先添加，其余后添加者优先占边
            Controls.Add(_list);
            Controls.Add(_lblInfo);
            Controls.Add(bp);
            Controls.Add(pp);

            CancelButton = _btnCancel;

            Shown += delegate { Navigate(_current); };
            FormClosing += delegate
            {
                SftpFetcher f = _fetcher;
                _fetcher = null;
                if (f != null) { try { f.Dispose(); } catch { } }
            };
        }

        /// <summary>在当前目录下新建一个文件（返回路径，实际创建由调用方在服务器上完成）。</summary>
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

        private void Accept(string fileName)
        {
            SelectedPath = Combine(_current, fileName);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void GoUp()
        {
            string p = _current.TrimEnd('/');
            if (p.Length == 0) return;
            int i = p.LastIndexOf('/');
            string up = i <= 0 ? "/" : p.Substring(0, i);
            Navigate(up);
        }

        private static string Combine(string dir, string name)
        {
            if (string.IsNullOrEmpty(dir)) dir = "/";
            if (dir.EndsWith("/")) return dir + name;
            return dir + "/" + name;
        }

        private void Navigate(string path)
        {
            if (_loading) return;
            if (string.IsNullOrEmpty(path)) path = "/";
            path = path.Trim();
            if (path.Length == 0) path = "/";

            _loading = true;
            _lblInfo.ForeColor = Color.FromArgb(120, 124, 130);
            _lblInfo.Text = "正在读取 " + path + " …";
            _list.Items.Clear();

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
                    BeginInvoke(new Action(delegate { OnListed(target, entries, err); }));
                }
                catch { }
            });
        }

        private void OnListed(string path, List<DirEntry> entries, string err)
        {
            _loading = false;
            if (IsDisposed) return;

            if (err != null)
            {
                _lblInfo.ForeColor = Color.FromArgb(198, 40, 40);
                _lblInfo.Text = err;
                return;
            }

            _current = path;
            _txtPath.Text = path;

            _list.BeginUpdate();
            _list.Items.Clear();
            for (int i = 0; i < entries.Count; i++)
                _list.Items.Add(new EntryItem(entries[i]));
            _list.EndUpdate();

            int dirs = 0;
            for (int i = 0; i < entries.Count; i++) if (entries[i].IsDir) dirs++;
            _lblInfo.ForeColor = Color.FromArgb(120, 124, 130);
            _lblInfo.Text = path + "    （" + dirs + " 个目录，" + (entries.Count - dirs) + " 个文件）";
        }

        private static string FmtSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("0.00") + " MB";
        }
    }
}
