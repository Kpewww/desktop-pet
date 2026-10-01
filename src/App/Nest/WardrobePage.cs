using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Aly.Core;

namespace Aly.App
{
    /// <summary>
    /// 小窝 · 衣柜: every outfit as a little picture of him wearing it (with his current
    /// hairstyle). Click one and he changes behind a folding screen on the desktop.
    /// </summary>
    sealed class WardrobePage : UserControl, INestPage
    {
        const int Zoom = 2;

        readonly PetApp app;
        readonly FlowLayoutPanel tiles = new FlowLayoutPanel();
        readonly Label hairLabel = Ui.Label("");
        readonly Button hairButton;
        readonly CheckBox auto = new CheckBox();
        readonly Label status = Ui.Label("", true);
        readonly List<Button> buttons = new List<Button>();
        string picturesHair;
        bool syncing;

        public WardrobePage(PetApp app, Font font)
        {
            this.app = app;
            AutoScaleMode = AutoScaleMode.Inherit;
            Dock = DockStyle.Fill;
            Font = font;
            string ta = Profile.Pronoun;

            var grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 1;
            grid.RowCount = 4;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            grid.Controls.Add(Ui.Label("点一套，" + ta + "就躲到屏风后面换上", true), 0, 0);

            tiles.Dock = DockStyle.Fill;
            tiles.AutoScroll = true;
            tiles.Margin = new Padding(0, 2, 0, 6);
            foreach (Outfit o in app.Wardrobe.Outfits)
            {
                string id = o.Id;
                var b = new Button();
                b.Tag = id;
                b.Text = o.Name;
                b.TextImageRelation = TextImageRelation.ImageAboveText;
                b.ImageAlign = ContentAlignment.BottomCenter;
                b.TextAlign = ContentAlignment.TopCenter;
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = SystemColors.Window;
                b.Size = new Size(118, 176);
                b.Margin = new Padding(0, 0, 8, 8);
                b.Click += delegate { Pick(id); };
                buttons.Add(b);
                tiles.Controls.Add(b);
            }
            grid.Controls.Add(tiles, 0, 1);

            hairButton = Ui.Button("", delegate { app.ChangeHair(app.Hair == "long" ? "short" : "long"); });
            grid.Controls.Add(Ui.Row(hairLabel, hairButton), 0, 2);

            auto.Text = "每天早上" + ta + "自己挑一套";
            auto.AutoSize = true;
            auto.Margin = new Padding(0, 6, 12, 4);
            auto.CheckedChanged += delegate { if (!syncing) app.SetAutoOutfit(auto.Checked); };
            grid.Controls.Add(Ui.Row(Ui.Button("随便换一套", delegate { app.RandomOutfit(); Sync(); }), auto, status), 0, 3);
            Controls.Add(grid);

            app.WardrobeChanged += OnWardrobeChanged;
            Sync();
        }

        public void Shown()
        {
            Sync();
        }

        public bool HandleKey(Keys keyData)
        {
            return false;
        }

        void OnWardrobeChanged()
        {
            if (!IsDisposed) Sync();
        }

        void Pick(string id)
        {
            Outfit current = app.CurrentOutfit;
            if (current != null && current.Id == id)
            {
                status.Text = "已经穿着这套啦";
                return;
            }
            app.ChangeOutfit(id);
            status.Text = "换衣服中…";
        }

        void Sync()
        {
            syncing = true;
            string ta = Profile.Pronoun;
            bool longHair = app.Hair == "long";
            hairLabel.Text = "现在是" + (longHair ? "长发" : "短发");
            hairButton.Text = longHair ? "给" + ta + "剪头发（变短发）" : "喂" + ta + "生发药水（变长发）";
            auto.Checked = app.AutoOutfit;
            if (picturesHair != app.Hair)
            {
                picturesHair = app.Hair;
                foreach (Button b in buttons)
                {
                    Image old = b.Image;
                    b.Image = app.OutfitPicture((string)b.Tag, Zoom);
                    if (old != null) old.Dispose();
                }
            }
            Outfit current = app.CurrentOutfit;
            foreach (Button b in buttons)
            {
                bool on = current != null && (string)b.Tag == current.Id;
                b.FlatAppearance.BorderSize = on ? 2 : 1;
                b.FlatAppearance.BorderColor = on ? Ui.Accent : SystemColors.ControlDark;
            }
            if (status.Text == "换衣服中…" && current != null) status.Text = "换好了：" + current.Name;
            syncing = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                app.WardrobeChanged -= OnWardrobeChanged;
                foreach (Button b in buttons)
                    if (b.Image != null) b.Image.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
