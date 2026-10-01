using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Aly.App
{
    /// <summary>
    /// 小窝 — her panel: 剪贴板 / 待办 / 专注 / 台词 / (衣柜) / 设置. Built when opened and disposed
    /// when closed, so it costs nothing while it isn't open.
    /// </summary>
    sealed class NestForm : Form
    {
        readonly TabControl tabs = new TabControl();
        readonly ClipboardPage clipboard;
        readonly List<INestPage> pages = new List<INestPage>();     // in tab order
        readonly List<NestPage> kinds = new List<NestPage>();       // which NestPage each tab is

        /// <summary>Opened with the hotkey: close again right after copying something.</summary>
        public bool CloseAfterCopy;

        public NestForm(PetApp app, Icon icon)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            Text = "小窝 · " + Profile.Name;
            Icon = icon;
            ClientSize = new Size(660, 450);
            MinimumSize = new Size(500, 360);
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;

            clipboard = new ClipboardPage(app, Font);
            clipboard.CopiedBack += delegate { if (CloseAfterCopy) Close(); };
            tabs.Dock = DockStyle.Fill;
            AddPage(NestPage.Clipboard, "剪贴板", clipboard);
            AddPage(NestPage.Todos, "待办", new TodoPage(app, Font));
            AddPage(NestPage.Focus, "专注", new FocusPage(app, Font));
            AddPage(NestPage.Lines, "台词", new LinesPage(app, Font));
            if (app.Wardrobe != null) AddPage(NestPage.Wardrobe, "衣柜", new WardrobePage(app, Font));
            AddPage(NestPage.Settings, "设置", new SettingsPage(app, Font));
            tabs.SelectedIndexChanged += delegate { Current.Shown(); };
            Controls.Add(tabs);
            ResumeLayout(false);
        }

        void AddPage<T>(NestPage kind, string title, T content) where T : Control, INestPage
        {
            var page = new TabPage(title);
            page.Padding = new Padding(8);
            page.UseVisualStyleBackColor = true;
            page.Controls.Add(content);
            tabs.TabPages.Add(page);
            pages.Add(content);
            kinds.Add(kind);
        }

        INestPage Current
        {
            get
            {
                int i = tabs.SelectedIndex;
                return i >= 0 && i < pages.Count ? pages[i] : clipboard;
            }
        }

        public void ShowPage(NestPage page)
        {
            int index = Math.Max(0, kinds.IndexOf(page));
            if (tabs.SelectedIndex != index) tabs.SelectedIndex = index;
            else Current.Shown();
        }

        /// <summary>Opens beside the cursor, kept inside the screen.</summary>
        public void PlaceNear(Point p)
        {
            Rectangle wa = Screen.FromPoint(p).WorkingArea;
            int x = p.X - Width / 2;
            int y = p.Y - Height - 24;
            if (y < wa.Top) y = p.Y + 24;
            x = Math.Max(wa.Left, Math.Min(wa.Right - Width, x));
            y = Math.Max(wa.Top, Math.Min(wa.Bottom - Height, y));
            Location = new Point(x, y);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            if (Current.HandleKey(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
