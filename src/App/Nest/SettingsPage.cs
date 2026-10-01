using System;
using System.Drawing;
using System.Windows.Forms;

namespace Aly.App
{
    /// <summary>小窝 · 设置: everything about how she looks and behaves, in one place.</summary>
    sealed class SettingsPage : UserControl, INestPage
    {
        static readonly string[] ScaleLabels = { "自动（跟屏幕缩放）", "1 倍", "2 倍", "3 倍", "4 倍" };
        static readonly string[] ThreeLevels = { "少", "正常", "多" };
        static readonly int[] SignSeconds = { 5, 8, 15 };
        static readonly string[] SignModes =
        {
            "常驻显示：一直举着最新复制的内容",
            "复制时举一会儿",
            "平时不显示：点「看看剪贴板」才显示",
        };

        readonly PetApp app;
        readonly ComboBox scale = Combo(ScaleLabels);
        readonly ComboBox activity = Combo(new[] { "安静", "正常", "活泼" });
        readonly ComboBox silly = Combo(new[] { "关", "正常", "经常" });
        readonly ComboBox chatty = Combo(ThreeLevels);
        readonly CheckBox dogEars = Check(Profile.Me.EarsLabel);
        readonly CheckBox follow = Check("鼠标靠近时看着鼠标");
        readonly ComboBox signMode = Combo(SignModes, 250);
        readonly Label signSecondsLabel = Ui.Label("举多久", true);
        readonly ComboBox signSeconds = Combo(new[] { "5 秒", "8 秒", "15 秒" }, 70);
        readonly Label signHint = Ui.Label("", true);
        readonly CheckBox hotkeyOn = Check("快捷键打开小窝");
        readonly TextBox hotkeyBox = new TextBox();
        readonly Label hotkeyNote = Ui.Label("", true);
        readonly CheckBox quiet = Check("正经模式（不说话、不搞怪、不举牌）");
        readonly CheckBox autostart = Check("开机自动启动");
        readonly CheckBox fullscreen = Check("全屏程序或放幻灯片时躲起来");
        readonly TextBox nameBox = new TextBox();
        readonly Label nameNote = Ui.Label("", true);
        // 防干扰 (pets that have it)
        static readonly int[] Opacities = { 100, 80, 60, 40 };
        readonly ComboBox calmStyle = Combo(new[] { "边缘探头", "安静陪伴", "完全隐身" });
        readonly ComboBox peekEdge = Combo(new[] { "任务栏后面", "屏幕左边", "屏幕右边" });
        readonly ComboBox quietOpacity = Combo(new[] { "不透明", "80%", "60%", "40%" }, 80);
        readonly CheckBox calmAuto = Check("开始专注时自动打开");
        readonly CheckBox supervise = Check("专注监督：在分心的网站或程序上待满");
        readonly NumericUpDown grace = Ui.Number(0, 600, 15);
        readonly TextBox distractBox = new TextBox();
        readonly Label distractNote = Ui.Label("", true);
        // 陪看 (both pets)
        readonly CheckBox watchAlong = Check("看视频、看直播时陪你一起看");
        readonly TextBox videoBox = new TextBox();
        readonly Label videoNote = Ui.Label("", true);
        readonly Label about = Ui.Label("", true);
        readonly Font headingFont;
        bool syncing;

        public SettingsPage(PetApp app, Font font)
        {
            this.app = app;
            AutoScaleMode = AutoScaleMode.Inherit;
            Dock = DockStyle.Fill;
            Font = font;
            headingFont = new Font(font, FontStyle.Bold);

            var grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.AutoScroll = true;
            grid.ColumnCount = 2;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.Padding = new Padding(4);

            AddHeading(grid, headingFont, "样子");
            nameBox.Width = 130;
            nameBox.MaxLength = PetApp.MaxNameLength;
            nameBox.Margin = new Padding(0, 3, 6, 3);
            Ui.CueBanner(nameBox, Profile.Me.Name);
            AddRow(grid, "名字", Ui.Row(nameBox, Ui.Button("改名", delegate { Rename(nameBox.Text); }),
                Ui.Button("用回原名", delegate { Rename(""); }), nameNote));
            AddRow(grid, "大小", scale);
            AddRow(grid, "", dogEars);

            AddHeading(grid, headingFont, "性格");
            AddRow(grid, "活跃度", Ui.Row(activity, Ui.Label("走来走去的次数", true)));
            AddRow(grid, "搞怪频率", Ui.Row(silly, Ui.Label("上厕所、放屁这些", true)));
            AddRow(grid, "说话频率", Ui.Row(chatty, Ui.Label("自言自语的次数；回应你的话照说", true)));
            AddRow(grid, "", follow);

            string who = Profile.Pronoun;
            AddHeading(grid, headingFont, "看视频");
            AddRow(grid, "", watchAlong);
            AddRow(grid, "", Ui.Label(who + "坐到屏幕一边陪你看，偶尔说两句感想，从不催你；不乱走、不举牌，喝水和久坐提醒等你看完再说。", true));
            ListBox(videoBox);
            AddRow(grid, "视频名单", videoBox);
            AddRow(grid, "", Ui.Row(Ui.Button("保存名单", SaveVideoList),
                Ui.Button("恢复默认", delegate { videoBox.Text = Aly.Core.SiteList.VideoDefaults; SaveVideoList(); }), videoNote));
            watchAlong.CheckedChanged += delegate { if (!syncing) app.SetWatchAlong(watchAlong.Checked); };

            AddHeading(grid, headingFont, "剪贴板");
            AddRow(grid, "举牌", Ui.Row(signMode, signSecondsLabel, signSeconds));
            AddRow(grid, "", signHint);
            hotkeyBox.ReadOnly = true;
            hotkeyBox.Width = 130;
            hotkeyBox.BackColor = SystemColors.Window;
            hotkeyBox.Margin = new Padding(0, 3, 6, 3);
            hotkeyBox.KeyDown += OnHotkeyKeyDown;
            hotkeyBox.PreviewKeyDown += delegate(object s, PreviewKeyDownEventArgs e) { e.IsInputKey = true; };
            AddRow(grid, "", Ui.Row(hotkeyOn, hotkeyBox, hotkeyNote));

            if (app.HasCalm)
            {
                string ta = Profile.Pronoun;
                AddHeading(grid, headingFont, "防干扰");
                AddRow(grid, "方式", Ui.Row(calmStyle, Ui.Label("菜单里「防干扰模式」随时开关", true)));
                AddRow(grid, "探头位置", peekEdge);
                AddRow(grid, "安静陪伴", Ui.Row(quietOpacity, Ui.Label("安静陪伴时" + ta + "的透明度", true)));
                AddRow(grid, "", calmAuto);
                AddRow(grid, "", Ui.Row(supervise, grace, Ui.Label("秒就被抓包", true)));
                ListBox(distractBox);
                AddRow(grid, "分心名单", distractBox);
                AddRow(grid, "", Ui.Row(Ui.Button("保存名单", SaveDistractList),
                    Ui.Button("恢复默认", delegate { distractBox.Text = Aly.Core.SiteList.DistractionDefaults; SaveDistractList(); }), distractNote));
                AddRow(grid, "", Ui.Label("视频和直播不算分心，" + ta + "会陪你看（见上面「看视频」）。", true));

                calmStyle.SelectedIndexChanged += delegate { if (!syncing) app.SetCalmStyle(calmStyle.SelectedIndex + 1); };
                peekEdge.SelectedIndexChanged += delegate { if (!syncing) app.SetPeekEdge(peekEdge.SelectedIndex); };
                quietOpacity.SelectedIndexChanged += delegate { if (!syncing) app.SetQuietOpacity(Opacities[quietOpacity.SelectedIndex]); };
                calmAuto.CheckedChanged += delegate { if (!syncing) app.SetCalmAuto(calmAuto.Checked); };
                supervise.CheckedChanged += delegate { if (!syncing) app.SetSupervise(supervise.Checked); };
                grace.ValueChanged += delegate { if (!syncing) app.SetSuperviseGrace((int)grace.Value); };
            }

            AddHeading(grid, headingFont, "其他");
            AddRow(grid, "", quiet);
            AddRow(grid, "", autostart);
            AddRow(grid, "", fullscreen);
            AddRow(grid, "", Ui.Row(Ui.Button("打开数据文件夹", delegate { app.OpenDataFolder(); }),
                Ui.Label("设置、待办和「台词」文件夹都在这里", true)));
            AddRow(grid, "", about);
            Controls.Add(grid);

            scale.SelectedIndexChanged += delegate { if (!syncing) app.SetScaleSetting(scale.SelectedIndex); };
            activity.SelectedIndexChanged += delegate { if (!syncing) app.SetActivity(activity.SelectedIndex); };
            silly.SelectedIndexChanged += delegate { if (!syncing) app.SetSilly(silly.SelectedIndex); };
            chatty.SelectedIndexChanged += delegate { if (!syncing) app.SetChatty(chatty.SelectedIndex); };
            dogEars.CheckedChanged += delegate { if (!syncing) app.SetDogEars(dogEars.Checked); };
            follow.CheckedChanged += delegate { if (!syncing) app.SetFollowCursor(follow.Checked); };
            signMode.SelectedIndexChanged += delegate { if (!syncing) { app.SetSignMode(signMode.SelectedIndex); Sync(); } };
            signSeconds.SelectedIndexChanged += delegate { if (!syncing) app.SetSignSeconds(SignSeconds[signSeconds.SelectedIndex]); };
            hotkeyOn.CheckedChanged += delegate { if (!syncing) ApplyHotkey(Keys.None); };
            quiet.CheckedChanged += delegate { if (!syncing) app.SetQuiet(quiet.Checked); };
            autostart.CheckedChanged += delegate { if (!syncing) { app.SetAutostart(autostart.Checked); Sync(); } };
            fullscreen.CheckedChanged += delegate { if (!syncing) app.SetHideInFullscreen(fullscreen.Checked); };
            Sync();
        }

        static ComboBox Combo(string[] items, int width = 130)
        {
            var c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Items.AddRange(items);
            c.Width = width;
            c.Anchor = AnchorStyles.Left;
            c.Margin = new Padding(0, 3, 6, 3);
            return c;
        }

        /// <summary>A list editor: one "显示名: 关键词, 关键词" per line.</summary>
        static void ListBox(TextBox box)
        {
            box.Multiline = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.AcceptsReturn = true;
            box.WordWrap = false;
            box.Width = 300;
            box.Height = 96;
            box.Margin = new Padding(0, 3, 6, 3);
        }

        static CheckBox Check(string text)
        {
            var c = new CheckBox();
            c.Text = text;
            c.AutoSize = true;
            c.Anchor = AnchorStyles.Left;
            c.Margin = new Padding(0, 4, 6, 4);
            return c;
        }

        static void AddHeading(TableLayoutPanel grid, Font bold, string text)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Font = bold;
            l.ForeColor = Ui.Accent;
            l.Margin = new Padding(0, grid.Controls.Count == 0 ? 2 : 12, 0, 2);
            grid.Controls.Add(l);
            grid.SetColumnSpan(l, 2);
        }

        static void AddRow(TableLayoutPanel grid, string label, Control control)
        {
            var l = Ui.Label(label);
            l.Margin = new Padding(12, 6, 12, 6);
            grid.Controls.Add(l);
            grid.Controls.Add(control);
        }

        public void Shown()
        {
            Sync();
        }

        public bool HandleKey(Keys keyData)
        {
            if (keyData == Keys.Enter && nameBox.Focused)
            {
                Rename(nameBox.Text);
                return true;
            }
            return false;
        }

        void SaveDistractList()
        {
            app.SetDistractList(distractBox.Text);
            int n = Aly.Core.SiteList.Parse(distractBox.Text).Count;
            distractNote.Text = "存好了，一共 " + n + " 项";
        }

        void SaveVideoList()
        {
            app.SetVideoList(videoBox.Text);
            int n = Aly.Core.SiteList.Parse(videoBox.Text).Count;
            videoNote.Text = "存好了，一共 " + n + " 项";
        }

        void Rename(string name)
        {
            app.SetName(name);
            Form form = FindForm();
            if (form != null) form.Text = "小窝 · " + Profile.Name;
            Sync();
            nameNote.Text = Profile.NameOverride == null ? "用回了原来的名字" : "改好啦，现在叫「" + Profile.Name + "」";
        }

        void Sync()
        {
            syncing = true;
            Settings s = app.Prefs;
            if (!nameBox.Focused) nameBox.Text = Profile.NameOverride ?? "";
            nameNote.Text = "气泡里提到名字的地方也会跟着改";
            about.Text = Profile.Name + " v" + PetApp.Version + " · 像素字体 Fusion Pixel（SIL OFL 1.1）";
            if (app.HasCalm)
            {
                calmStyle.SelectedIndex = Math.Max(0, Math.Min(2, s.CalmStyle - 1));
                peekEdge.SelectedIndex = Math.Max(0, Math.Min(2, s.PeekEdge));
                int oi = Array.IndexOf(Opacities, s.QuietOpacity);
                quietOpacity.SelectedIndex = oi >= 0 ? oi : 0;
                calmAuto.Checked = s.CalmAuto;
                supervise.Checked = s.Supervise;
                grace.Value = Math.Max(grace.Minimum, Math.Min(grace.Maximum, s.SuperviseGrace));
                grace.Enabled = s.Supervise;
                if (!distractBox.Focused) distractBox.Text = s.DistractList ?? "";
            }
            watchAlong.Checked = s.WatchAlong;
            videoBox.Enabled = s.WatchAlong;
            if (!videoBox.Focused) videoBox.Text = s.VideoList ?? "";
            videoNote.Text = "一行一个：显示名: 关键词, 关键词。只在这台电脑上比对窗口标题，不记录、不上传";
            scale.SelectedIndex = Math.Max(0, Math.Min(ScaleLabels.Length - 1, s.Scale));
            activity.SelectedIndex = Clamp(s.Activity);
            silly.SelectedIndex = Clamp(s.Silly);
            chatty.SelectedIndex = Clamp(s.Chatty);
            dogEars.Checked = s.DogEars;
            follow.Checked = s.FollowCursor;
            signMode.SelectedIndex = Math.Max(0, Math.Min(2, s.SignMode));
            signSeconds.SelectedIndex = Math.Max(0, Array.IndexOf(SignSeconds, s.SignSeconds));
            signSeconds.Visible = signSecondsLabel.Visible = s.SignMode == PetApp.SignTimed;
            string ta = Profile.Pronoun;
            signHint.Text = s.SignMode == PetApp.SignSticky ? ta + "会隔一两分钟放下牌子溜达一会儿再举起来；点" + ta + "一下收起来，下次复制再举"
                : s.SignMode == PetApp.SignManual ? "复制的东西照样记在小窝里，右键" + ta + "选「看看剪贴板」就会举牌"
                : "复制文字、图片或文件时，" + ta + "举牌给你看一眼";
            hotkeyOn.Checked = s.ClipHotkeyOn;
            hotkeyBox.Text = s.ClipHotkey;
            hotkeyBox.Enabled = s.ClipHotkeyOn;
            hotkeyNote.Text = !s.ClipHotkeyOn ? "" : app.HotkeyWorking ? "点这里按新的组合键可以改" : "被别的程序占用了，换一个吧";
            hotkeyNote.ForeColor = s.ClipHotkeyOn && !app.HotkeyWorking ? Ui.Accent : Ui.Muted;
            quiet.Checked = s.Quiet;
            autostart.Checked = app.Autostarts;
            fullscreen.Checked = s.HideInFullscreen;
            syncing = false;
        }

        static int Clamp(int v) { return Math.Max(0, Math.Min(2, v)); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) headingFont.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>Records a new shortcut: a modifier or two plus a key.</summary>
        void OnHotkeyKeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            Keys key = e.KeyCode;
            if (key == Keys.ControlKey || key == Keys.Menu || key == Keys.ShiftKey || key == Keys.LWin || key == Keys.RWin)
            {
                hotkeyBox.Text = GlobalHotkey.Describe(e.Modifiers) + "+…";
                return;
            }
            if (key == Keys.Escape || key == Keys.Back)
            {
                Sync();
                return;
            }
            if (e.Modifiers == Keys.None)
            {
                hotkeyNote.Text = "要带上 Ctrl、Alt 或 Shift";
                hotkeyNote.ForeColor = Ui.Accent;
                return;
            }
            ApplyHotkey(key | e.Modifiers);
        }

        void ApplyHotkey(Keys keys)
        {
            app.SetHotkey(keys, hotkeyOn.Checked);
            Sync();
        }
    }
}
