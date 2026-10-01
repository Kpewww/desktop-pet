using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using Aly.Core.Schedule;

namespace Aly.App
{
    /// <summary>小窝 · 待办: notes and to-dos, optionally with a time for her to remind you.</summary>
    sealed class TodoPage : UserControl, INestPage
    {
        static readonly string[] WhenLabels =
        {
            "不提醒", "10 分钟后", "30 分钟后", "1 小时后", "2 小时后", "今晚 8 点", "明天早上 9 点", "自定义时间…",
        };
        const int Custom = 7;

        readonly PetApp app;
        readonly TextBox input = new TextBox();
        readonly ComboBox when = new ComboBox();
        readonly DateTimePicker custom = new DateTimePicker();
        readonly ListBox list = new ListBox();
        readonly Button doneButton, snoozeButton, deleteButton;
        readonly Label status = new Label();
        readonly List<Todo> shown = new List<Todo>();
        readonly Font smallFont, strikeFont;

        public TodoPage(PetApp app, Font font)
        {
            this.app = app;
            AutoScaleMode = AutoScaleMode.Inherit;
            Dock = DockStyle.Fill;
            Font = font;
            smallFont = new Font(font.FontFamily, font.Size * 0.9f);
            strikeFont = new Font(font, FontStyle.Strikeout);

            var grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 1;
            grid.RowCount = 3;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // text | when | (custom time) | add
            var top = new TableLayoutPanel();
            top.Dock = DockStyle.Fill;
            top.AutoSize = true;
            top.ColumnCount = 4;
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            input.Dock = DockStyle.Fill;
            input.Margin = new Padding(0, 3, 6, 3);
            Ui.CueBanner(input, "记点什么…（Enter 添加）");
            when.DropDownStyle = ComboBoxStyle.DropDownList;
            when.Items.AddRange(WhenLabels);
            when.SelectedIndex = 0;
            when.Width = 110;
            when.Margin = new Padding(0, 3, 6, 3);
            when.SelectedIndexChanged += delegate { custom.Visible = when.SelectedIndex == Custom; };
            custom.Format = DateTimePickerFormat.Custom;
            custom.CustomFormat = "M月d日 HH:mm";
            custom.Width = 120;
            custom.Margin = new Padding(0, 3, 6, 3);
            custom.Value = DateTime.Now.AddHours(1);
            custom.Visible = false;
            top.Controls.Add(input, 0, 0);
            top.Controls.Add(when, 1, 0);
            top.Controls.Add(custom, 2, 0);
            top.Controls.Add(Ui.Button("添加", Add), 3, 0);
            grid.Controls.Add(top, 0, 0);

            list.Dock = DockStyle.Fill;
            list.Margin = new Padding(0, 4, 0, 4);
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = Math.Max(22, Font.Height * 2);
            list.IntegralHeight = false;
            list.DrawItem += DrawItem;
            list.MouseDown += OnListMouseDown;
            list.DoubleClick += delegate { ToggleDone(); };
            list.SelectedIndexChanged += delegate { UpdateButtons(); };
            grid.Controls.Add(list, 0, 1);

            doneButton = Ui.Button("完成", ToggleDone);
            snoozeButton = Ui.Button("推迟 10 分钟", Snooze);
            deleteButton = Ui.Button("删除", Delete);
            status.AutoSize = true;
            status.ForeColor = Ui.Muted;
            status.Anchor = AnchorStyles.Left;
            status.Margin = new Padding(8, 0, 8, 0);
            var bottom = new TableLayoutPanel();
            bottom.Dock = DockStyle.Fill;
            bottom.AutoSize = true;
            bottom.ColumnCount = 5;
            for (int i = 0; i < 3; i++) bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.Controls.Add(doneButton, 0, 0);
            bottom.Controls.Add(snoozeButton, 1, 0);
            bottom.Controls.Add(deleteButton, 2, 0);
            bottom.Controls.Add(status, 3, 0);
            bottom.Controls.Add(Ui.Button("清除已完成", delegate { app.ClearDoneTodos(); }), 4, 0);
            grid.Controls.Add(bottom, 0, 2);
            Controls.Add(grid);

            app.PlannerChanged += OnPlannerChanged;
            RefreshList();
        }

        public void Shown()
        {
            input.Focus();
        }

        public bool HandleKey(Keys keyData)
        {
            if (keyData == Keys.Enter && (input.Focused || when.Focused || custom.Focused))
            {
                Add();
                return true;
            }
            if (keyData == Keys.Delete && list.Focused)
            {
                Delete();
                return true;
            }
            if (keyData == Keys.Space && list.Focused)
            {
                ToggleDone();
                return true;
            }
            return false;
        }

        void OnPlannerChanged()
        {
            if (!IsDisposed) RefreshList();
        }

        Todo Selected
        {
            get { int i = list.SelectedIndex; return i >= 0 && i < shown.Count ? shown[i] : null; }
        }

        void RefreshList()
        {
            Todo keep = Selected;
            shown.Clear();
            shown.AddRange(app.Planner.Todos.Sorted());
            list.BeginUpdate();
            list.Items.Clear();
            foreach (Todo t in shown) list.Items.Add(t);
            int sel = keep == null ? -1 : shown.FindIndex(t => t.Id == keep.Id);
            if (sel < 0 && shown.Count > 0) sel = 0;
            list.SelectedIndex = sel;
            list.EndUpdate();
            int open = app.Planner.Todos.OpenCount;
            status.Text = shown.Count == 0 ? "还没有待办。在上面写点什么吧" : open == 0 ? "全部完成啦！" : open + " 件没做完";
            UpdateButtons();
        }

        void UpdateButtons()
        {
            Todo t = Selected;
            doneButton.Enabled = deleteButton.Enabled = t != null;
            doneButton.Text = t != null && t.Done ? "取消完成" : "完成";
            snoozeButton.Enabled = t != null && !t.Done && t.DueUtc != null;
        }

        DateTime? PickedDueUtc()
        {
            DateTime now = DateTime.Now;
            DateTime local;
            switch (when.SelectedIndex)
            {
                case 1: local = now.AddMinutes(10); break;
                case 2: local = now.AddMinutes(30); break;
                case 3: local = now.AddHours(1); break;
                case 4: local = now.AddHours(2); break;
                case 5:
                    local = now.Date.AddHours(20);
                    if (local <= now) local = local.AddDays(1);
                    break;
                case 6: local = now.Date.AddDays(1).AddHours(9); break;
                case Custom: local = custom.Value; break;
                default: return null;
            }
            return DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
        }

        void Add()
        {
            string text = input.Text.Trim();
            if (text.Length == 0) return;
            DateTime? due = PickedDueUtc();
            Todo t = app.AddTodo(text, due);
            if (t == null) return;
            input.Clear();
            when.SelectedIndex = 0;
            int i = shown.FindIndex(x => x.Id == t.Id);
            if (i >= 0) list.SelectedIndex = i;
            status.Text = due == null ? "记下来啦" : "记下来啦，" + Ui.When(due.Value.ToLocalTime()) + " 提醒你";
            input.Focus();
        }

        void ToggleDone()
        {
            Todo t = Selected;
            if (t != null) app.SetTodoDone(t.Id, !t.Done);
        }

        void Snooze()
        {
            Todo t = Selected;
            if (t != null) app.SnoozeTodo(t.Id);
        }

        void Delete()
        {
            Todo t = Selected;
            if (t == null) return;
            int index = list.SelectedIndex;
            app.RemoveTodo(t.Id);
            if (shown.Count > 0) list.SelectedIndex = Math.Min(index, shown.Count - 1);
        }

        Rectangle CheckBoxBounds(Rectangle item)
        {
            int size = Math.Min(item.Height - 6, 16 * DeviceDpi / 96);
            return new Rectangle(item.Left + 6, item.Top + (item.Height - size) / 2, size, size);
        }

        void OnListMouseDown(object sender, MouseEventArgs e)
        {
            int i = list.IndexFromPoint(e.Location);
            if (i < 0 || i >= shown.Count) return;
            if (CheckBoxBounds(list.GetItemRectangle(i)).Contains(e.Location)) app.SetTodoDone(shown[i].Id, !shown[i].Done);
        }

        void DrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || e.Index >= shown.Count) return;
            Todo t = shown[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color fore = selected ? SystemColors.HighlightText : t.Done ? Ui.Muted : SystemColors.WindowText;
            Rectangle r = e.Bounds;
            Rectangle box = CheckBoxBounds(r);
            CheckBoxRenderer.DrawCheckBox(e.Graphics, box.Location,
                t.Done ? CheckBoxState.CheckedNormal : CheckBoxState.UncheckedNormal);

            int right = r.Right - 6;
            if (t.DueUtc != null)
            {
                DateTime local = t.DueUtc.Value.ToLocalTime();
                bool overdue = !t.Done && t.DueUtc.Value <= DateTime.UtcNow;
                string due = (overdue ? "已到 " : "提醒 ") + Ui.When(local);
                Size ds = TextRenderer.MeasureText(e.Graphics, due, smallFont, Size.Empty, Ui.Line);
                right -= ds.Width;
                TextRenderer.DrawText(e.Graphics, due, smallFont, new Rectangle(right, r.Top, ds.Width, r.Height),
                    selected ? fore : overdue ? Ui.Accent : Ui.Muted, Ui.Line);
                right -= 8;
            }
            int x = box.Right + 8;
            string text = t.Text.Replace("\r\n", " ").Replace('\n', ' ');
            TextRenderer.DrawText(e.Graphics, text, t.Done ? strikeFont : Font, new Rectangle(x, r.Top, Math.Max(0, right - x), r.Height),
                fore, Ui.Line | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                app.PlannerChanged -= OnPlannerChanged;
                smallFont.Dispose();
                strikeFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
