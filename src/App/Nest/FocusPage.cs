using System;
using System.Drawing;
using System.Windows.Forms;
using Aly.Core.Schedule;

namespace Aly.App
{
    /// <summary>小窝 · 专注: the pomodoro timer and the health reminders' settings.</summary>
    sealed class FocusPage : UserControl, INestPage
    {
        readonly PetApp app;
        readonly Label clock = new Label();
        readonly Label phase = new Label();
        readonly Label count = new Label();
        readonly Button startButton, stopButton;
        readonly NumericUpDown focusMinutes, breakMinutes, waterMinutes, sitMinutes;
        readonly CheckBox waterOn = new CheckBox(), sitOn = new CheckBox(), bedOn = new CheckBox();
        readonly DateTimePicker bedTime = new DateTimePicker();
        readonly Timer ticker = new Timer();
        readonly Font clockFont;
        bool syncing;

        public FocusPage(PetApp app, Font font)
        {
            this.app = app;
            AutoScaleMode = AutoScaleMode.Inherit;
            Dock = DockStyle.Fill;
            Font = font;
            clockFont = new Font(font.FontFamily, font.Size * 3.2f, FontStyle.Bold);
            Settings s = app.Prefs;

            var grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 1;
            grid.AutoScroll = true;
            grid.Padding = new Padding(4);

            // ---- the pomodoro
            clock.Font = clockFont;
            clock.AutoSize = true;
            clock.Anchor = AnchorStyles.None;
            clock.ForeColor = Ui.Accent;
            clock.Margin = new Padding(0, 6, 0, 0);
            phase.AutoSize = true;
            phase.Anchor = AnchorStyles.None;
            phase.ForeColor = Ui.Muted;
            startButton = Ui.Button("开始专注", delegate { app.StartFocus(); });
            stopButton = Ui.Button("停止", delegate { app.StopFocus(); });
            var buttons = Ui.Row(startButton, stopButton);
            buttons.Anchor = AnchorStyles.None;
            buttons.Dock = DockStyle.None;
            count.AutoSize = true;
            count.Anchor = AnchorStyles.None;
            count.ForeColor = Ui.Muted;
            count.Margin = new Padding(0, 4, 0, 8);

            focusMinutes = Ui.Number(1, 180, s.FocusMinutes);
            breakMinutes = Ui.Number(1, 60, s.BreakMinutes);
            EventHandler lengths = delegate { if (!syncing) app.SetFocusLengths((int)focusMinutes.Value, (int)breakMinutes.Value); };
            focusMinutes.ValueChanged += lengths;
            breakMinutes.ValueChanged += lengths;
            var lengthRow = Ui.Row(Ui.Label("专注"), focusMinutes, Ui.Label("分钟    休息"), breakMinutes, Ui.Label("分钟"));
            lengthRow.Anchor = AnchorStyles.None;
            lengthRow.Dock = DockStyle.None;

            grid.Controls.Add(clock);
            grid.Controls.Add(phase);
            grid.Controls.Add(buttons);
            grid.Controls.Add(count);
            grid.Controls.Add(lengthRow);

            // ---- health reminders
            var health = new GroupBox();
            health.Text = "健康提醒";
            health.Dock = DockStyle.Fill;
            health.AutoSize = true;
            health.Margin = new Padding(0, 12, 0, 0);
            health.Padding = new Padding(10, 6, 10, 8);
            var rows = new TableLayoutPanel();
            rows.Dock = DockStyle.Fill;
            rows.AutoSize = true;
            rows.ColumnCount = 1;

            waterOn.Text = "喝水，每";
            waterOn.AutoSize = true;
            waterOn.Anchor = AnchorStyles.Left;
            waterOn.Checked = s.WaterOn;
            waterMinutes = Ui.Number(5, 480, s.WaterMinutes);
            EventHandler water = delegate { if (!syncing) app.SetWater(waterOn.Checked, (int)waterMinutes.Value); };
            waterOn.CheckedChanged += water;
            waterMinutes.ValueChanged += water;
            rows.Controls.Add(Ui.Row(waterOn, waterMinutes, Ui.Label("分钟提醒一次，" + Profile.Pronoun + "会陪你一起喝", true)));

            sitOn.Text = "久坐，连续用电脑";
            sitOn.AutoSize = true;
            sitOn.Anchor = AnchorStyles.Left;
            sitOn.Checked = s.SitOn;
            sitMinutes = Ui.Number(10, 240, s.SitMinutes);
            EventHandler sit = delegate { if (!syncing) app.SetSit(sitOn.Checked, (int)sitMinutes.Value); };
            sitOn.CheckedChanged += sit;
            sitMinutes.ValueChanged += sit;
            rows.Controls.Add(Ui.Row(sitOn, sitMinutes, Ui.Label("分钟就提醒（离开 5 分钟重新计时）", true)));

            bedOn.Text = "熬夜，过了";
            bedOn.AutoSize = true;
            bedOn.Anchor = AnchorStyles.Left;
            bedOn.Checked = s.BedtimeOn;
            bedTime.Format = DateTimePickerFormat.Custom;
            bedTime.CustomFormat = "HH:mm";
            bedTime.ShowUpDown = true;
            bedTime.Width = 70;
            bedTime.Anchor = AnchorStyles.Left;
            bedTime.Margin = new Padding(0, 3, 6, 3);
            bedTime.Value = DateTime.Today.AddMinutes(s.BedtimeMinute);
            EventHandler bed = delegate
            {
                if (!syncing) app.SetBedtime(bedOn.Checked, bedTime.Value.Hour * 60 + bedTime.Value.Minute);
            };
            bedOn.CheckedChanged += bed;
            bedTime.ValueChanged += bed;
            rows.Controls.Add(Ui.Row(bedOn, bedTime, Ui.Label("还不睡，每半小时催一次，越晚越生气", true)));
            rows.Controls.Add(Ui.Label("专注时这些提醒会等到休息再说；正经模式、全屏时也会先攒着。", true));
            health.Controls.Add(rows);
            grid.Controls.Add(health);
            Controls.Add(grid);

            ticker.Interval = 1000;
            ticker.Tick += delegate { RefreshClock(); };
            app.PlannerChanged += OnPlannerChanged;
            RefreshClock();
        }

        public void Shown()
        {
            RefreshClock();
            startButton.Focus();
        }

        public bool HandleKey(Keys keyData)
        {
            return false;
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            ticker.Enabled = Visible; // the countdown only ticks while you look at it
            if (Visible) RefreshClock();
        }

        void OnPlannerChanged()
        {
            if (!IsDisposed) RefreshClock();
        }

        void RefreshClock()
        {
            Pomodoro p = app.Planner.Focus;
            Moment now = Moment.Now;
            TimeSpan left = p.Remaining(now);
            switch (p.Phase)
            {
                case FocusPhase.Focus:
                    clock.Text = Format(left);
                    phase.Text = "专注中 · " + Profile.Pronoun + "在陪你工作";
                    break;
                case FocusPhase.Break:
                    clock.Text = Format(left);
                    phase.Text = "休息中";
                    break;
                default:
                    clock.Text = Format(TimeSpan.FromMinutes(p.FocusMinutes));
                    phase.Text = "还没开始";
                    break;
            }
            startButton.Text = p.Phase == FocusPhase.Break ? "跳过休息，开始专注" : "开始专注";
            startButton.Enabled = p.Phase != FocusPhase.Focus;
            stopButton.Enabled = p.Phase != FocusPhase.Off;
            stopButton.Text = p.Phase == FocusPhase.Break ? "结束休息" : "停止";
            int done = p.DoneToday(now);
            count.Text = done == 0 ? "今天还没有完成番茄" : "今天完成了 " + done + " 个番茄";
            if (app.HasCalm && app.CaughtToday > 0) count.Text += "，被抓包 " + app.CaughtToday + " 次";
            syncing = true;
            Settings s = app.Prefs;
            if (focusMinutes.Value != s.FocusMinutes) focusMinutes.Value = s.FocusMinutes;
            if (breakMinutes.Value != s.BreakMinutes) breakMinutes.Value = s.BreakMinutes;
            syncing = false;
        }

        static string Format(TimeSpan t)
        {
            int total = (int)Math.Ceiling(t.TotalSeconds);
            return string.Format("{0:00}:{1:00}", total / 60, total % 60);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                app.PlannerChanged -= OnPlannerChanged;
                ticker.Dispose();
                clockFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
