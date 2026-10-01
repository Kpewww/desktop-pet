using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Aly.Core;
using Aly.Core.Schedule;

namespace Aly.App
{
    /// <summary>
    /// Tray icon plus the one menu shared by the tray and a right-click on her. The menu holds
    /// the everyday things; everything else is in 小窝 · 设置.
    /// </summary>
    sealed class Tray : IDisposable
    {
        readonly PetApp app;
        readonly NotifyIcon icon = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly ToolStripMenuItem sleepItem;
        readonly ToolStripMenuItem focusItem;
        readonly ToolStripMenuItem nestItem;
        readonly ToolStripMenuItem quietItem;
        readonly ToolStripMenuItem showItem;
        readonly ToolStripMenuItem overlayItem;
        readonly ToolStripMenuItem fastItem;
        readonly ToolStripMenuItem shortIdleItem;
        readonly ToolStripMenuItem[] signModeItems;
        readonly ToolStripMenuItem outfitMenu;  // 换装 (pets with a wardrobe)
        readonly ToolStripMenuItem hairItem;    // 喂他生发药水 / 给他剪头发
        readonly ToolStripMenuItem calmMenu;    // 防干扰模式 (pets that have it)
        readonly ToolStripMenuItem[] calmItems; // 关闭, 边缘探头, 安静陪伴, 完全隐身
        readonly ToolStripMenuItem superviseItem, calmAutoItem;

        public Tray(PetApp app, Icon trayIcon)
        {
            this.app = app;
            Character me = Profile.Me;
            var feedMenu = new ToolStripMenuItem("喂" + me.Pronoun);
            foreach (Food food in me.Foods)
            {
                string id = food.Id;
                feedMenu.DropDownItems.Add(new ToolStripMenuItem(food.Label, null, delegate { app.Feed(id); }));
            }
            feedMenu.Visible = me.Foods.Count > 0;
            var petItem = new ToolStripMenuItem("摸摸", null, delegate { app.PetHer(); });
            sleepItem = new ToolStripMenuItem("让" + me.Pronoun + "睡觉", null, delegate { app.ToggleSleep(); });

            outfitMenu = new ToolStripMenuItem("换装");
            hairItem = new ToolStripMenuItem("", null, delegate { app.ChangeHair(app.Hair == "long" ? "short" : "long"); });
            if (app.Wardrobe != null)
            {
                foreach (Outfit o in app.Wardrobe.Outfits)
                {
                    string id = o.Id;
                    var item = new ToolStripMenuItem(o.Name, null, delegate { app.ChangeOutfit(id); });
                    item.Tag = id;
                    outfitMenu.DropDownItems.Add(item);
                }
                outfitMenu.DropDownItems.Add(new ToolStripSeparator());
                outfitMenu.DropDownItems.Add(new ToolStripMenuItem("随便换一套", null, delegate { app.RandomOutfit(); }));
                outfitMenu.DropDownItems.Add(new ToolStripMenuItem("打开衣柜…", null, delegate { app.OpenNest(NestPage.Wardrobe); }));
            }
            outfitMenu.Visible = hairItem.Visible = app.Wardrobe != null;

            focusItem = new ToolStripMenuItem("开始专注", null, delegate
            {
                if (app.Planner.Focus.Phase == FocusPhase.Off) app.StartFocus();
                else app.StopFocus();
            });
            calmMenu = new ToolStripMenuItem("防干扰模式");
            string[] calmNames = { "关闭", "边缘探头", "安静陪伴", "完全隐身" };
            calmItems = new ToolStripMenuItem[calmNames.Length];
            for (int i = 0; i < calmNames.Length; i++)
            {
                int style = i;
                calmItems[i] = new ToolStripMenuItem(calmNames[i], null, delegate { app.SetCalmOn(style > 0, style); });
                calmMenu.DropDownItems.Add(calmItems[i]);
            }
            superviseItem = new ToolStripMenuItem("专注监督（刷分心网站会被抓包）", null, delegate { app.SetSupervise(!app.Prefs.Supervise); });
            calmAutoItem = new ToolStripMenuItem("开始专注时自动打开", null, delegate { app.SetCalmAuto(!app.Prefs.CalmAuto); });
            calmMenu.DropDownItems.Add(new ToolStripSeparator());
            calmMenu.DropDownItems.Add(superviseItem);
            calmMenu.DropDownItems.Add(calmAutoItem);
            calmMenu.DropDownItems.Add(new ToolStripMenuItem("更多设置…", null, delegate { app.OpenNest(NestPage.Settings); }));
            calmMenu.Visible = app.HasCalm;
            var noteItem = new ToolStripMenuItem("记一下…", null, delegate { app.OpenNest(NestPage.Todos); });
            var linesItem = new ToolStripMenuItem("教" + me.Pronoun + "说话…", null, delegate { app.OpenNest(NestPage.Lines); });

            var lookItem = new ToolStripMenuItem("看看剪贴板", null, delegate { app.ShowClipboardSign(); });
            var signMenu = new ToolStripMenuItem("举牌方式");
            string[] modes = { "常驻显示", "复制时举一会儿", "平时不显示" };
            signModeItems = new ToolStripMenuItem[modes.Length];
            for (int i = 0; i < modes.Length; i++)
            {
                int mode = i;
                signModeItems[i] = new ToolStripMenuItem(modes[i], null, delegate { app.SetSignMode(mode); });
                signMenu.DropDownItems.Add(signModeItems[i]);
            }
            nestItem = new ToolStripMenuItem("打开小窝…", null, delegate { app.OpenNest(NestPage.Clipboard); });

            quietItem = new ToolStripMenuItem("正经模式", null, delegate { app.ToggleQuiet(); });
            showItem = new ToolStripMenuItem("隐藏 " + Profile.Name, null, delegate { app.SetVisible(!app.PetVisible); });
            var settingsItem = new ToolStripMenuItem("设置…", null, delegate { app.OpenNest(NestPage.Settings); });

            var debugMenu = new ToolStripMenuItem("调试");
            overlayItem = new ToolStripMenuItem("状态浮层", null, delegate { app.ToggleOverlay(); });
            var forceMenu = new ToolStripMenuItem("播放动作");
            foreach (string clip in app.ClipNames)
            {
                string c = clip;
                forceMenu.DropDownItems.Add(new ToolStripMenuItem(c, null, delegate { app.ForceClip(c); }));
            }
            fastItem = new ToolStripMenuItem("时间加速 ×10", null, delegate { app.ToggleTimeFast(); });
            shortIdleItem = new ToolStripMenuItem("离开判定改为 20 秒", null, delegate { app.ToggleShortIdle(); });
            debugMenu.DropDownItems.AddRange(new ToolStripItem[] { overlayItem, forceMenu, fastItem, shortIdleItem });

            var exitItem = new ToolStripMenuItem("退出", null, delegate { app.Exit(); });

            menu.Items.AddRange(new ToolStripItem[]
            {
                feedMenu, petItem, sleepItem, outfitMenu, hairItem,
                new ToolStripSeparator(), focusItem, calmMenu, noteItem, linesItem,
                new ToolStripSeparator(), lookItem, signMenu, nestItem,
                new ToolStripSeparator(), quietItem, showItem, settingsItem,
                new ToolStripSeparator(), debugMenu,
                new ToolStripSeparator(), exitItem,
            });
            menu.Opening += delegate { RefreshItems(); };

            icon.Icon = trayIcon;
            icon.Text = Profile.Name;
            icon.ContextMenuStrip = menu;
            icon.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) app.SetVisible(!app.PetVisible);
            };
            icon.Visible = true;
        }

        void RefreshItems()
        {
            string ta = Profile.Pronoun;
            sleepItem.Text = app.Sleeping ? "叫醒" + ta : "让" + ta + "睡觉";
            if (app.Wardrobe != null)
            {
                string current = app.CurrentOutfit == null ? null : app.CurrentOutfit.Id;
                foreach (ToolStripItem item in outfitMenu.DropDownItems)
                {
                    var mi = item as ToolStripMenuItem;
                    if (mi != null && mi.Tag is string) mi.Checked = (string)mi.Tag == current;
                }
                hairItem.Text = app.Hair == "long" ? "给" + ta + "剪头发" : "喂" + ta + "生发药水";
            }
            if (app.HasCalm)
            {
                Settings s = app.Prefs;
                for (int i = 0; i < calmItems.Length; i++)
                    calmItems[i].Checked = i == 0 ? !s.CalmOn : s.CalmOn && s.CalmStyle == i;
                superviseItem.Checked = s.Supervise;
                calmAutoItem.Checked = s.CalmAuto;
                bool autoOn = !s.CalmOn && app.EffectiveCalm != Aly.Core.Engine.CalmStyle.Off;
                calmMenu.Text = app.CalmFollowing ? "防干扰模式（跟着另一只一起躲）"
                    : autoOn ? "防干扰模式（专注中，已自动打开）" : "防干扰模式";
            }
            switch (app.Planner.Focus.Phase)
            {
                case FocusPhase.Focus: focusItem.Text = "停止专注"; break;
                case FocusPhase.Break: focusItem.Text = "结束休息"; break;
                default: focusItem.Text = "开始专注（" + app.Prefs.FocusMinutes + " 分钟）"; break;
            }
            nestItem.ShortcutKeyDisplayString = app.ClipHotkeyOn && app.HotkeyWorking ? app.ClipHotkeyText : null;
            for (int i = 0; i < signModeItems.Length; i++) signModeItems[i].Checked = app.SignMode == i;
            quietItem.Checked = app.Quiet;
            showItem.Text = (app.PetVisible ? "隐藏 " : "显示 ") + Profile.Name;
            overlayItem.Checked = app.OverlayOn;
            fastItem.Checked = app.TimeFast;
            shortIdleItem.Checked = app.ShortIdle;
        }

        /// <summary>The tray tooltip shows what she is up to (the pomodoro countdown, open to-dos).</summary>
        public void RefreshTip()
        {
            string tip = app.StatusLine;
            if (tip.Length > 63) tip = tip.Substring(0, 63); // NotifyIcon's limit
            if (icon.Text != tip) icon.Text = tip;
        }

        /// <summary>A balloon from the tray icon, for things she can't say herself.</summary>
        public void Notify(string title, string text)
        {
            icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);
        }

        /// <summary>
        /// Shows the menu at the cursor. NotifyIcon's own routine makes the menu's window
        /// foreground first, which is what lets a click elsewhere dismiss it even though
        /// her window never activates.
        /// </summary>
        public void ShowMenu()
        {
            MethodInfo mi = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
            if (mi != null) mi.Invoke(icon, null);
            else menu.Show(Cursor.Position);
        }

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
            menu.Dispose();
        }
    }
}
