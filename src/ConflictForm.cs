using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace RemoteTxtViewer
{
    public enum ConflictChoice
    {
        Cancel,
        Overwrite,     // 用我的版本覆盖服务器
        UseRemote,     // 放弃我的修改，载入服务器版本
        SaveLocal      // 我的修改另存为本地文件
    }

    /// <summary>
    /// 保存冲突：服务器上的文件在编辑期间被改过了。
    /// 左右并排显示两份内容，滚动联动，由用户决定怎么处理。
    /// </summary>
    public class ConflictForm : Form
    {
        public ConflictChoice Choice = ConflictChoice.Cancel;
        public string SavedLocalPath = "";

        private readonly string _remoteText;
        private readonly string _myText;
        private readonly float _scale;
        private Font _uiFont;
        private Font _monoFont;

        private RichTextBox _left;
        private RichTextBox _right;
        private bool _syncing;

        public ConflictForm(string remoteText, string myText, string remoteInfo, float scale)
        {
            _remoteText = remoteText == null ? "" : remoteText;
            _myText = myText == null ? "" : myText;
            _scale = scale;
            Build(remoteInfo);
        }

        private int S(int v) { return (int)Math.Round(v * _scale); }

        private void Build(string remoteInfo)
        {
            try { _uiFont = new Font("Microsoft YaHei UI", 9f * _scale); }
            catch { _uiFont = SystemFonts.DefaultFont; }
            try { _monoFont = new Font("Consolas", 9.5f * _scale); }
            catch { _monoFont = _uiFont; }

            AutoScaleMode = AutoScaleMode.None;
            Font = _uiFont;
            Text = "文件已被修改";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            BackColor = Color.FromArgb(250, 251, 253);
            ClientSize = new Size(S(960), S(600));
            MinimumSize = new Size(S(700), S(420));

            // ---- 顶部说明 ----
            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = S(64);
            top.Padding = new Padding(S(16), S(10), S(16), S(6));
            top.BackColor = Color.FromArgb(255, 246, 232);

            Label warn = new Label();
            warn.Dock = DockStyle.Fill;
            warn.TextAlign = ContentAlignment.MiddleLeft;
            warn.ForeColor = Color.FromArgb(160, 84, 0);
            warn.Font = new Font(_uiFont.FontFamily, 9.5f * _scale, FontStyle.Bold);
            warn.Text = "⚠  服务器上的文件在你编辑期间被修改了，直接保存会覆盖对方的改动。\r\n"
                      + "    请对比下面两份内容后选择怎么处理。" + (string.IsNullOrEmpty(remoteInfo) ? "" : "    " + remoteInfo);
            top.Controls.Add(warn);

            // ---- 两栏内容 ----
            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 2;
            grid.RowCount = 2;
            grid.Padding = new Padding(S(12), S(6), S(12), S(6));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, S(26)));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            Label l1 = MakeColTitle("服务器当前版本（" + CountLines(_remoteText) + " 行）", Color.FromArgb(30, 110, 190));
            Label l2 = MakeColTitle("我的修改（" + CountLines(_myText) + " 行）", Color.FromArgb(180, 108, 0));
            grid.Controls.Add(l1, 0, 0);
            grid.Controls.Add(l2, 1, 0);

            _left = MakeBox(_remoteText, Color.FromArgb(245, 249, 255));
            _right = MakeBox(_myText, Color.FromArgb(255, 251, 240));

            Panel leftHost = Wrap(_left);
            Panel rightHost = Wrap(_right);
            leftHost.Margin = new Padding(0, 0, S(6), 0);
            rightHost.Margin = new Padding(S(6), 0, 0, 0);

            grid.Controls.Add(leftHost, 0, 1);
            grid.Controls.Add(rightHost, 1, 1);

            // 滚动联动，方便左右对照
            _left.VScroll += delegate { SyncScroll(_left, _right); };
            _right.VScroll += delegate { SyncScroll(_right, _left); };

            // ---- 底部按钮 ----
            FlowLayoutPanel bottom = new FlowLayoutPanel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = S(52);
            bottom.FlowDirection = FlowDirection.RightToLeft;
            bottom.Padding = new Padding(S(12), S(10), S(12), S(10));
            bottom.BackColor = Color.FromArgb(250, 251, 253);

            Button bCancel = NewButton("取消（继续编辑）");
            bCancel.Click += delegate { Choice = ConflictChoice.Cancel; DialogResult = DialogResult.Cancel; Close(); };

            Button bLocal = NewButton("我的另存为...");
            bLocal.Click += delegate { SaveLocal(); };

            Button bUseRemote = NewButton("用服务器版本");
            bUseRemote.Click += delegate { Choice = ConflictChoice.UseRemote; DialogResult = DialogResult.OK; Close(); };

            Button bOverwrite = NewButton("用我的覆盖");
            bOverwrite.ForeColor = Color.FromArgb(180, 40, 40);
            bOverwrite.Click += delegate
            {
                if (MessageBox.Show(this,
                        "会用你的版本覆盖服务器上的文件，对方的改动将丢失。\r\n确定吗？",
                        "确认覆盖", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
                Choice = ConflictChoice.Overwrite;
                DialogResult = DialogResult.OK;
                Close();
            };

            bottom.Controls.Add(bCancel);
            bottom.Controls.Add(bLocal);
            bottom.Controls.Add(bUseRemote);
            bottom.Controls.Add(bOverwrite);

            Controls.Add(grid);
            Controls.Add(bottom);
            Controls.Add(top);

            CancelButton = bCancel;
        }

        private Label MakeColTitle(string text, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.Dock = DockStyle.Fill;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.ForeColor = color;
            l.Font = new Font(_uiFont.FontFamily, 9f * _scale, FontStyle.Bold);
            return l;
        }

        private RichTextBox MakeBox(string text, Color back)
        {
            RichTextBox r = new RichTextBox();
            r.Dock = DockStyle.Fill;
            r.ReadOnly = true;
            r.WordWrap = false;
            r.ScrollBars = RichTextBoxScrollBars.Both;
            r.DetectUrls = false;
            r.BorderStyle = BorderStyle.None;
            r.BackColor = back;
            r.ForeColor = Color.FromArgb(30, 34, 40);
            r.Font = _monoFont;
            r.Text = text;
            return r;
        }

        private Panel Wrap(RichTextBox box)
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Fill;
            p.Padding = new Padding(S(6));
            p.BackColor = box.BackColor;
            p.Controls.Add(box);
            return p;
        }

        private Button NewButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = false;
            b.Size = new Size(S(120), S(30));
            b.Margin = new Padding(S(8), 0, 0, 0);
            return b;
        }

        private void SyncScroll(RichTextBox from, RichTextBox to)
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                int first = from.GetCharIndexFromPosition(new Point(1, 1));
                int line = from.GetLineFromCharIndex(first);
                int ch = to.GetFirstCharIndexFromLine(line);
                if (ch >= 0)
                {
                    to.SelectionStart = ch;
                    to.SelectionLength = 0;
                    to.ScrollToCaret();
                }
            }
            catch { }
            finally { _syncing = false; }
        }

        private void SaveLocal()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
                d.FileName = "我的修改_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(d.FileName, _myText, new UTF8Encoding(false));
                    SavedLocalPath = d.FileName;
                    Choice = ConflictChoice.SaveLocal;
                    DialogResult = DialogResult.OK;
                    Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "保存失败：" + ex.Message, "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private static int CountLines(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 1;
            for (int i = 0; i < s.Length; i++) if (s[i] == '\n') n++;
            if (s[s.Length - 1] == '\n') n--;
            return n < 0 ? 0 : n;
        }
    }
}
