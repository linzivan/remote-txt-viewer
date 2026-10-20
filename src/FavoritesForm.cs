using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace RemoteTxtViewer
{
    /// <summary>收藏列表：左边条目、右边预览，可重命名/删除/另存。</summary>
    public class FavoritesForm : Form
    {
        public bool Changed;

        private readonly FavoritesStore _store;
        private readonly float _scale;
        private readonly string _sourceHint;
        private Font _uiFont;
        private Font _monoFont;

        private ListView _list;
        private RichTextBox _preview;
        private Label _lblInfo;
        private Button _btnOpen, _btnRename, _btnDelete, _btnExport, _btnClose;

        public FavoritesForm(FavoritesStore store, float scale, string sourceHint)
        {
            _store = store;
            _scale = scale;
            _sourceHint = sourceHint;
            Build();
            Reload();
        }

        private int S(int v) { return (int)Math.Round(v * _scale); }

        private void Build()
        {
            try { _uiFont = new Font("Microsoft YaHei UI", 9f * _scale); }
            catch { _uiFont = SystemFonts.DefaultFont; }
            try { _monoFont = new Font("Consolas", 9.5f * _scale); }
            catch { _monoFont = _uiFont; }

            AutoScaleMode = AutoScaleMode.None;
            Font = _uiFont;
            Text = "收藏夹";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(250, 251, 253);
            ClientSize = new Size(S(880), S(520));
            MinimumSize = new Size(S(600), S(360));

            // ---- 顶部 ----
            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = S(34);
            top.Padding = new Padding(S(12), S(9), S(12), 0);
            top.BackColor = Color.FromArgb(250, 251, 253);

            _lblInfo = new Label();
            _lblInfo.Dock = DockStyle.Fill;
            _lblInfo.TextAlign = ContentAlignment.MiddleLeft;
            _lblInfo.ForeColor = Color.FromArgb(110, 116, 124);
            top.Controls.Add(_lblInfo);

            // ---- 底部按钮 ----
            FlowLayoutPanel bottom = new FlowLayoutPanel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = S(48);
            bottom.FlowDirection = FlowDirection.RightToLeft;
            bottom.Padding = new Padding(S(10), S(9), S(10), S(9));
            bottom.BackColor = Color.FromArgb(250, 251, 253);

            _btnClose = NewButton("关闭");
            _btnClose.Click += delegate { Close(); };
            _btnExport = NewButton("另存为...");
            _btnExport.Click += delegate { ExportSelected(); };
            _btnDelete = NewButton("删除");
            _btnDelete.Click += delegate { DeleteSelected(); };
            _btnRename = NewButton("重命名");
            _btnRename.Click += delegate { RenameSelected(); };
            _btnOpen = NewButton("在主窗口查看");
            _btnOpen.Click += delegate { OpenInMain(); };

            bottom.Controls.Add(_btnClose);
            bottom.Controls.Add(_btnExport);
            bottom.Controls.Add(_btnDelete);
            bottom.Controls.Add(_btnRename);
            bottom.Controls.Add(_btnOpen);

            // ---- 预览 ----
            _preview = new RichTextBox();
            _preview.Dock = DockStyle.Fill;
            _preview.ReadOnly = true;
            _preview.WordWrap = false;
            _preview.ScrollBars = RichTextBoxScrollBars.Both;
            _preview.DetectUrls = false;
            _preview.BorderStyle = BorderStyle.None;
            _preview.BackColor = Color.White;
            _preview.ForeColor = Color.FromArgb(28, 32, 38);
            _preview.Font = _monoFont;
            _preview.Padding = new Padding(S(6));

            Panel previewHost = new Panel();
            previewHost.Dock = DockStyle.Fill;
            previewHost.Padding = new Padding(S(4), S(4), S(4), S(4));
            previewHost.BackColor = Color.White;
            previewHost.Controls.Add(_preview);

            // ---- 列表 ----
            _list = new ListView();
            _list.Dock = DockStyle.Left;
            _list.Width = S(330);
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.HideSelection = false;
            _list.BorderStyle = BorderStyle.None;
            _list.Font = _uiFont;
            _list.Columns.Add("名称", S(170));
            _list.Columns.Add("收藏时间", S(110));
            _list.Columns.Add("行数", S(40), HorizontalAlignment.Right);
            _list.SelectedIndexChanged += delegate { PreviewSelected(); };
            _list.DoubleClick += delegate { PreviewSelected(); };

            Controls.Add(previewHost);
            Controls.Add(_list);
            Controls.Add(bottom);
            Controls.Add(top);

            CancelButton = null;
        }

        private Button NewButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = false;
            b.Size = new Size(S(92), S(28));
            b.Margin = new Padding(S(8), 0, 0, 0);
            return b;
        }

        private void Reload()
        {
            List<FavoriteItem> items = _store.Items();

            _list.BeginUpdate();
            _list.Items.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                FavoriteItem f = items[i];
                ListViewItem li = new ListViewItem(f.Name);
                li.SubItems.Add(f.Created.ToString("MM-dd HH:mm:ss"));
                li.SubItems.Add(f.Lines.ToString());
                li.Tag = f;
                if (f.Source.Length > 0)
                    li.ToolTipText = "来源：" + f.Source;
                _list.Items.Add(li);
            }
            _list.EndUpdate();

            _lblInfo.Text = "共 " + items.Count + " 条收藏     存放位置：" + _store.Directory_;

            if (_list.Items.Count > 0)
            {
                _list.Items[0].Selected = true;
            }
            else
            {
                _preview.Clear();
            }
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool has = _list.SelectedItems.Count > 0;
            _btnOpen.Enabled = has;
            _btnRename.Enabled = has;
            _btnDelete.Enabled = has;
            _btnExport.Enabled = has;
        }

        private FavoriteItem Selected()
        {
            if (_list.SelectedItems.Count == 0) return null;
            return _list.SelectedItems[0].Tag as FavoriteItem;
        }

        private void PreviewSelected()
        {
            UpdateButtons();
            FavoriteItem f = Selected();
            if (f == null) { _preview.Clear(); return; }
            _preview.Text = _store.GetText(f);
            _preview.SelectionStart = 0;
            _preview.ScrollToCaret();
        }

        private void RenameSelected()
        {
            FavoriteItem f = Selected();
            if (f == null) return;
            using (PromptForm dlg = new PromptForm("重命名收藏", "新的名称：", f.Name, _scale))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (dlg.Value == null || dlg.Value.Length == 0)
                {
                    MessageBox.Show(this, "名称不能为空。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (_store.Rename(f.Id, dlg.Value))
                {
                    Changed = true;
                    Reload();
                }
            }
        }

        private void DeleteSelected()
        {
            FavoriteItem f = Selected();
            if (f == null) return;
            if (MessageBox.Show(this, "确定删除收藏「" + f.Name + "」吗？此操作不可恢复。",
                    "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            if (_store.Delete(f.Id))
            {
                Changed = true;
                Reload();
            }
        }

        private void ExportSelected()
        {
            FavoriteItem f = Selected();
            if (f == null) return;
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
                d.FileName = SafeFileName(f.Name) + ".txt";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(d.FileName, _store.GetText(f), new UTF8Encoding(false));
                    _lblInfo.Text = "已导出到 " + d.FileName;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导出失败：" + ex.Message, "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void OpenInMain()
        {
            FavoriteItem f = Selected();
            if (f == null) return;
            LastViewed = f;
            LastViewedText = _store.GetText(f);
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>用户在窗口里点了“在主窗口查看”时，这两个字段会被填上。</summary>
        public FavoriteItem LastViewed;
        public string LastViewedText;

        private static string SafeFileName(string name)
        {
            char[] bad = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool isBad = false;
                for (int j = 0; j < bad.Length; j++) if (bad[j] == c) { isBad = true; break; }
                sb.Append(isBad ? '_' : c);
            }
            string s = sb.ToString().Trim();
            if (s.Length == 0) s = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return s;
        }
    }
}
