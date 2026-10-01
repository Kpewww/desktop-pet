using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Aly.Core;

namespace Aly.App
{
    /// <summary>小窝 · 剪贴板: the copy history — search, pin, delete, copy back, save as a note.</summary>
    sealed class ClipboardPage : UserControl, INestPage
    {
        readonly PetApp app;
        readonly TextBox search = new TextBox();
        readonly ListBox list = new ListBox();
        readonly TextBox detail = new TextBox();
        readonly PictureBox picture = new PictureBox();
        readonly Label info = new Label();
        readonly LinkLabel reveal = new LinkLabel();
        readonly Label status = new Label();
        readonly Button copyButton, pinButton, deleteButton, noteButton, pauseButton, clearButton;
        readonly CheckBox persistBox = new CheckBox();
        readonly SplitContainer split = new SplitContainer();
        readonly List<ClipItem> shown = new List<ClipItem>();
        readonly Font smallFont;
        bool revealed;
        bool syncing;
        bool splitPlaced;

        /// <summary>Something was copied back to the clipboard (the form may close).</summary>
        public event Action CopiedBack;

        public ClipboardPage(PetApp app, Font font)
        {
            this.app = app;
            AutoScaleMode = AutoScaleMode.Inherit;
            Dock = DockStyle.Fill;
            Font = font;
            smallFont = new Font(font.FontFamily, font.Size * 0.9f);
            copyButton = Ui.Button("复制 (Enter)", Copy);
            pinButton = Ui.Button("置顶", TogglePin);
            deleteButton = Ui.Button("删除", DeleteSelected);
            noteButton = Ui.Button("存成便签", SaveAsNote);
            pauseButton = Ui.Button("", delegate { app.SetClipPaused(!app.Clips.Paused); });
            clearButton = Ui.Button("清空", ClearAll);

            var grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 1;
            grid.RowCount = 3;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var top = new TableLayoutPanel();
            top.Dock = DockStyle.Fill;
            top.AutoSize = true;
            top.ColumnCount = 3;
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            search.Dock = DockStyle.Fill;
            search.Margin = new Padding(0, 3, 6, 3);
            search.TextChanged += delegate { RefreshList(); };
            Ui.CueBanner(search, "搜索记录…   ↑↓ 选择 · Enter 复制 · Esc 关闭");
            top.Controls.Add(search, 0, 0);
            top.Controls.Add(pauseButton, 1, 0);
            top.Controls.Add(clearButton, 2, 0);
            grid.Controls.Add(top, 0, 0);

            split.Dock = DockStyle.Fill;
            split.Margin = new Padding(0, 4, 0, 4);
            list.Dock = DockStyle.Fill;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = Math.Max(22, Font.Height * 2);
            list.IntegralHeight = false;
            list.DrawItem += DrawItem;
            list.SelectedIndexChanged += delegate { revealed = false; ShowDetail(); };
            list.DoubleClick += delegate { Copy(); };
            split.Panel1.Controls.Add(list);

            var right = new TableLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.ColumnCount = 2;
            right.RowCount = 2;
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            right.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            info.AutoSize = true;
            info.ForeColor = Ui.Muted;
            info.Margin = new Padding(6, 2, 3, 4);
            reveal.AutoSize = true;
            reveal.Margin = new Padding(3, 2, 3, 4);
            reveal.LinkClicked += delegate { revealed = !revealed; ShowDetail(); };
            detail.Dock = DockStyle.Fill;
            detail.Multiline = true;
            detail.ReadOnly = true;
            detail.BackColor = SystemColors.Window;
            detail.ScrollBars = ScrollBars.Vertical;
            detail.Margin = new Padding(6, 0, 0, 0);
            picture.Dock = DockStyle.Fill;
            picture.SizeMode = PictureBoxSizeMode.Zoom;
            picture.Margin = new Padding(6, 0, 0, 0);
            picture.Visible = false;
            right.Controls.Add(info, 0, 0);
            right.Controls.Add(reveal, 1, 0);
            right.Controls.Add(detail, 0, 1);
            right.SetColumnSpan(detail, 2);
            right.Controls.Add(picture, 0, 1);
            right.SetColumnSpan(picture, 2);
            split.Panel2.Controls.Add(right);
            grid.Controls.Add(split, 0, 1);

            var bottom = new TableLayoutPanel();
            bottom.Dock = DockStyle.Fill;
            bottom.AutoSize = true;
            bottom.ColumnCount = 6;
            for (int i = 0; i < 4; i++) bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            status.AutoSize = true;
            status.ForeColor = Ui.Muted;
            status.Anchor = AnchorStyles.Left;
            status.Margin = new Padding(8, 0, 8, 0);
            persistBox.AutoSize = true;
            persistBox.Text = "保存到磁盘（加密）";
            persistBox.Anchor = AnchorStyles.Right;
            persistBox.CheckedChanged += delegate { if (!syncing) app.SetClipPersist(persistBox.Checked); };
            bottom.Controls.Add(copyButton, 0, 0);
            bottom.Controls.Add(pinButton, 1, 0);
            bottom.Controls.Add(deleteButton, 2, 0);
            bottom.Controls.Add(noteButton, 3, 0);
            bottom.Controls.Add(status, 4, 0);
            bottom.Controls.Add(persistBox, 5, 0);
            grid.Controls.Add(bottom, 0, 2);
            Controls.Add(grid);

            app.ClipsChanged += OnClipsChanged;
            RefreshList();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (!splitPlaced && split.Width > 0)
            {
                split.SplitterDistance = Math.Max(split.Panel1MinSize, (int)(split.Width * 0.55));
                splitPlaced = true;
            }
        }

        public void Shown()
        {
            search.Focus();
            search.SelectAll();
        }

        public bool HandleKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Enter:
                    if (search.Focused || list.Focused)
                    {
                        Copy();
                        return true;
                    }
                    return false;
                case Keys.Down:
                case Keys.Up:
                    if (!search.Focused) return false;
                    MoveSelection(keyData == Keys.Down ? 1 : -1);
                    return true;
                case Keys.Delete:
                    if (!list.Focused) return false;
                    DeleteSelected();
                    return true;
                case Keys.Control | Keys.F:
                    Shown();
                    return true;
            }
            return false;
        }

        ClipItem Selected
        {
            get { int i = list.SelectedIndex; return i >= 0 && i < shown.Count ? shown[i] : null; }
        }

        void OnClipsChanged()
        {
            if (!IsDisposed) RefreshList();
        }

        void RefreshList()
        {
            ClipItem keep = Selected;
            string q = search.Text.Trim();
            shown.Clear();
            IList<ClipItem> all = app.Clips.Items;
            foreach (bool pinnedPass in new[] { true, false })
            {
                foreach (ClipItem it in all)
                {
                    if (it.Pinned == pinnedPass && Matches(it, q)) shown.Add(it);
                }
            }
            list.BeginUpdate();
            list.Items.Clear();
            foreach (ClipItem it in shown) list.Items.Add(it);
            int sel = keep == null ? -1 : shown.IndexOf(keep);
            if (sel < 0 && shown.Count > 0) sel = 0;
            list.SelectedIndex = sel;
            list.EndUpdate();

            syncing = true;
            pauseButton.Text = app.Clips.Paused ? "继续记录" : "暂停记录";
            persistBox.Checked = app.ClipPersist;
            syncing = false;
            status.Text = app.Clips.Paused ? "已暂停记录"
                : string.Format("{0} 条{1}", app.Clips.Count, app.ClipPersist ? "" : " · 只存在内存里");
            ShowDetail();
        }

        static bool Matches(ClipItem it, string q)
        {
            if (q.Length == 0) return true;
            if (it.Secret) return false; // hidden content is not searchable either
            switch (it.Kind)
            {
                case ClipKind.Text:
                    return it.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                case ClipKind.Files:
                    foreach (string f in it.Files) if (f.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    return false;
                default:
                    return "图片".Contains(q);
            }
        }

        static string When(DateTime t)
        {
            return t.Date == DateTime.Today ? t.ToString("HH:mm") : t.ToString("M-d HH:mm");
        }

        void DrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || e.Index >= shown.Count) return;
            ClipItem it = shown[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color fore = selected ? SystemColors.HighlightText : SystemColors.WindowText;
            Color sub = selected ? SystemColors.HighlightText : Ui.Muted;
            Rectangle r = e.Bounds;
            int pad = Math.Max(4, r.Height / 5);

            string time = When(it.Time);
            Size ts = TextRenderer.MeasureText(e.Graphics, time, smallFont, Size.Empty, Ui.Line);
            TextRenderer.DrawText(e.Graphics, time, smallFont, new Rectangle(r.Right - ts.Width - pad, r.Top, ts.Width, r.Height), sub, Ui.Line);

            int x = r.Left + pad;
            if (it.Pinned)
            {
                Size ps = TextRenderer.MeasureText(e.Graphics, "★", Font, Size.Empty, Ui.Line);
                TextRenderer.DrawText(e.Graphics, "★", Font, new Rectangle(x, r.Top, ps.Width, r.Height), selected ? fore : Ui.Accent, Ui.Line);
                x += ps.Width;
            }
            var thumb = it.Thumbnail as Bitmap;
            if (thumb != null)
            {
                int th = r.Height - 6;
                int tw = Math.Max(1, Math.Min(th * 2, thumb.Width * th / Math.Max(1, thumb.Height)));
                e.Graphics.DrawImage(thumb, new Rectangle(x, r.Top + 3, tw, th));
                x += tw + pad;
            }
            else
            {
                string tag = it.Kind == ClipKind.Files ? "文件" : it.Kind == ClipKind.Image ? "图片" : "文字";
                Size gs = TextRenderer.MeasureText(e.Graphics, tag, smallFont, Size.Empty, Ui.Line);
                TextRenderer.DrawText(e.Graphics, tag, smallFont, new Rectangle(x, r.Top, gs.Width, r.Height),
                    selected ? fore : (it.Kind == ClipKind.Text ? Ui.Muted : Ui.Accent), Ui.Line);
                x += gs.Width + pad / 2;
            }
            var box = new Rectangle(x, r.Top, Math.Max(0, r.Right - ts.Width - 2 * pad - x), r.Height);
            TextRenderer.DrawText(e.Graphics, it.Preview(120), Font, box, it.Secret && !selected ? Ui.Muted : fore, Ui.Line | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }

        void ShowDetail()
        {
            ClipItem it = Selected;
            copyButton.Enabled = it != null && it.Kind != ClipKind.Image;
            noteButton.Enabled = it != null && it.Kind != ClipKind.Image && !it.Secret;
            pinButton.Enabled = deleteButton.Enabled = it != null;
            pinButton.Text = it != null && it.Pinned ? "取消置顶" : "置顶";
            reveal.Visible = it != null && it.Secret;
            reveal.Text = revealed ? "隐藏" : "显示";
            if (it == null)
            {
                info.Text = app.Clips.Count == 0 ? "还没有记录。复制点什么试试？" : "没有找到";
                ShowPicture(null);
                detail.Text = "";
                return;
            }
            string when = it.Time.ToString("M月d日 HH:mm:ss");
            switch (it.Kind)
            {
                case ClipKind.Image:
                    info.Text = string.Format("图片 · {0}×{1} · {2} · 只留了缩略图，不能复制回去", it.ImageWidth, it.ImageHeight, when);
                    ShowPicture(it.Thumbnail as Bitmap);
                    break;
                case ClipKind.Files:
                    info.Text = string.Format("文件 · {0} 个 · {1}", it.Files.Length, when);
                    ShowPicture(null);
                    detail.Text = string.Join("\r\n", it.Files);
                    break;
                default:
                    info.Text = string.Format("文字 · {0} 字 · {1}{2}", ClipText.Length(it.Text), when,
                        it.Truncated ? " · 太长了，只保存了开头" : "");
                    ShowPicture(null);
                    detail.Text = it.Secret && !revealed ? "（看起来像密码，已隐藏）" : Normalize(it.Text);
                    break;
            }
        }

        void ShowPicture(Bitmap bmp)
        {
            picture.Image = bmp;
            picture.Visible = bmp != null;
            detail.Visible = bmp == null;
        }

        static string Normalize(string s)
        {
            return s.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
        }

        void Copy()
        {
            ClipItem it = Selected;
            if (it == null || it.Kind == ClipKind.Image) return;
            if (!app.CopyBack(it))
            {
                status.Text = "复制失败：剪贴板正被别的程序占用";
                return;
            }
            status.Text = "已复制，可以粘贴了";
            if (CopiedBack != null) CopiedBack();
        }

        void TogglePin()
        {
            ClipItem it = Selected;
            if (it != null) app.PinClip(it, !it.Pinned);
        }

        void DeleteSelected()
        {
            ClipItem it = Selected;
            if (it == null) return;
            int index = list.SelectedIndex;
            app.RemoveClip(it);
            if (shown.Count > 0) list.SelectedIndex = Math.Min(index, shown.Count - 1);
        }

        void SaveAsNote()
        {
            ClipItem it = Selected;
            if (it != null && app.SaveClipAsTodo(it)) status.Text = "已存成便签，在「待办」里";
        }

        void ClearAll()
        {
            if (app.Clips.Count == 0) return;
            if (MessageBox.Show(FindForm(), "清空所有没有置顶的记录？", "小窝", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                app.ClearClips();
        }

        void MoveSelection(int delta)
        {
            if (shown.Count == 0) return;
            int i = list.SelectedIndex < 0 ? 0 : list.SelectedIndex + delta;
            list.SelectedIndex = Math.Max(0, Math.Min(shown.Count - 1, i));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                app.ClipsChanged -= OnClipsChanged;
                picture.Image = null; // thumbnails belong to the history, not to the panel
                smallFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
