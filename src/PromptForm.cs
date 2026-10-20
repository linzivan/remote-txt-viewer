using System;
using System.Drawing;
using System.Windows.Forms;

namespace RemoteTxtViewer
{
    /// <summary>一个简单的单行输入对话框。</summary>
    public class PromptForm : Form
    {
        public string Value;
        private TextBox _txt;
        private readonly float _scale;

        public PromptForm(string title, string label, string initial, float scale)
        {
            _scale = scale;
            Build(title, label, initial);
        }

        private int S(int v) { return (int)Math.Round(v * _scale); }

        private Font MakeFont()
        {
            try { return new Font("Microsoft YaHei UI", 9f * _scale); }
            catch { return SystemFonts.DefaultFont; }
        }

        private void Build(string title, string label, string initial)
        {
            Font f = MakeFont();

            AutoScaleMode = AutoScaleMode.None;
            Font = f;
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(250, 251, 253);

            int pad = S(16);
            int w = S(420);
            int labelH = S(22);
            int boxH = S(26);
            int btnH = S(30);
            int h = pad + labelH + S(6) + boxH + S(16) + btnH + S(12);

            ClientSize = new Size(w, h);

            Label lb = new Label();
            lb.Text = label;
            lb.Bounds = new Rectangle(pad, pad, w - pad * 2, labelH);
            lb.TextAlign = ContentAlignment.MiddleLeft;
            lb.ForeColor = Color.FromArgb(58, 64, 72);
            Controls.Add(lb);

            _txt = new TextBox();
            _txt.Text = initial == null ? "" : initial;
            _txt.Bounds = new Rectangle(pad, pad + labelH + S(6), w - pad * 2, boxH);
            _txt.BorderStyle = BorderStyle.FixedSingle;
            _txt.AutoSize = false;
            Controls.Add(_txt);

            int by = pad + labelH + S(6) + boxH + S(16);
            int bw = S(86);

            Button ok = new Button();
            ok.Text = "确定";
            ok.Bounds = new Rectangle(w - pad - bw, by, bw, btnH);
            ok.Click += delegate { Accept(); };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Bounds = new Rectangle(w - pad - bw * 2 - S(8), by, bw, btnH);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            Shown += delegate
            {
                _txt.Focus();
                _txt.SelectAll();
            };
        }

        private void Accept()
        {
            Value = _txt.Text.Trim();   // 允许空，由调用方决定空值如何解释
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
