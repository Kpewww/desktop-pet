using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Aly.Core;

namespace Aly.App
{
    /// <summary>
    /// 小窝 · 台词: pick a moment (被戳, 早上打招呼…), see what she says then, and add your own
    /// sentences. They are saved in the 台词 folder (我的台词.txt), apart from the built-in ones,
    /// and count at once — no restart.
    /// </summary>
    sealed class LinesPage : UserControl, INestPage
    {
        /// <summary>Scene key, what to call it, and a hint about its placeholders.</summary>
        static readonly string[][] Known =
        {
            new[] { "greet_morning", "早上打招呼", "" },
            new[] { "greet_afternoon", "下午打招呼", "" },
            new[] { "greet_evening", "晚上打招呼", "" },
            new[] { "greet_night", "深夜打招呼", "" },
            new[] { "unlock", "解锁屏幕时", "" },
            new[] { "welcome_back", "离开回来时", "" },
            new[] { "idle", "自言自语", "" },
            new[] { "petted", "被摸头", "" },
            new[] { "poke", "被戳", "" },
            new[] { "poke_annoyed", "戳烦了", "" },
            new[] { "angry", "生气", "" },
            new[] { "explode", "发火大喊", "戳到最烦时大喊的一句" },
            new[] { "sulk_flee", "冷战跑开", "" },
            new[] { "sulk_over", "冷战消气", "" },
            new[] { "forgive", "被哄好", "生气时摸头哄好" },
            new[] { "grabbed", "被拎起来", "" },
            new[] { "drag_long", "拎太久", "" },
            new[] { "bite_warn", "要咬人了", "" },
            new[] { "bite", "咬人", "" },
            new[] { "dizzy", "摔晕", "" },
            new[] { "wake_grumpy", "被吵醒", "" },
            new[] { "wake", "睡醒", "" },
            new[] { "sleepy", "困了", "" },
            new[] { "stare", "被盯着看", "鼠标一直停在身上" },
            new[] { "bark", "叫一声", "" },
            new[] { "hungry", "饿了", "" },
            new[] { "full", "吃不下", "" },
            new[] { "busy", "正忙着", "喂的时候在忙别的" },
            new[] { "yummy", "好吃", "" },
            new[] { "smoothie", "喝冰沙", "" },
            new[] { "freeze", "冻到脑袋", "" },
            new[] { "self_smoothie", "自己去买冰沙", "饿太久没人喂" },
            new[] { "yakiniku", "吃烤肉", "" },
            new[] { "pomegranate", "吃石榴", "" },
            new[] { "chicken", "吃鸡肉棒", "" },
            new[] { "cola", "喝可乐", "" },
            new[] { "burp", "打嗝", "" },
            new[] { "self_cola", "自己开可乐", "饿太久没人喂" },
            new[] { "feichang", "吃烤肥肠", "" },
            new[] { "pizza", "吃披萨", "" },
            new[] { "bbq", "吃烧烤", "" },
            new[] { "fart", "放屁", "" },
            new[] { "fart_blame", "放屁甩锅", "鼠标就在旁边时" },
            new[] { "toilet_go", "去厕所", "" },
            new[] { "toilet_strain", "用力中", "" },
            new[] { "toilet_done", "上完厕所", "" },
            new[] { "flush", "冲水", "" },
            new[] { "focus_start", "开始专注", "" },
            new[] { "focus_shh", "专注时被戳", "{0} 会换成还剩几分钟" },
            new[] { "focus_stop", "停止专注", "" },
            new[] { "note_focus_done", "专注完成（牌子）", "写在牌子上，别太长；{0} 会换成今天第几个" },
            new[] { "break_start", "开始休息", "" },
            new[] { "note_break_done", "休息结束（牌子）", "写在牌子上，别太长" },
            new[] { "focus_rest", "多休息会儿", "" },
            new[] { "note_water", "喝水提醒（牌子）", "写在牌子上，别太长" },
            new[] { "water_ok", "喝完水", "" },
            new[] { "note_sit", "久坐提醒（牌子）", "写在牌子上，别太长" },
            new[] { "sit_ok", "活动完", "" },
            new[] { "note_bedtime1", "催睡觉·第一次（牌子）", "写在牌子上，别太长；{0} 会换成现在几点" },
            new[] { "note_bedtime2", "催睡觉·第二次（牌子）", "写在牌子上，别太长；{0} 会换成现在几点" },
            new[] { "note_bedtime3", "催睡觉·第三次（牌子）", "写在牌子上，别太长；{0} 会换成现在几点" },
            new[] { "bedtime_ok", "去睡觉了", "" },
            new[] { "bedtime_later", "说好再玩一会", "" },
            new[] { "snooze", "待会儿再提醒", "" },
            new[] { "todo_added", "记下待办", "" },
            new[] { "todo_done", "完成待办", "" },
            new[] { "outfit_start", "开始换衣服", "" },
            new[] { "outfit_done", "换好衣服", "" },
            new[] { "potion", "喝生发药水", "" },
            new[] { "hair_long", "变长发", "" },
            new[] { "haircut", "剪头发", "" },
            new[] { "hair_short", "变短发", "" },
            new[] { "calm_on", "开始躲起来", "防干扰模式" },
            new[] { "calm_shh", "躲着时被戳", "防干扰模式，没在专注的时候" },
            new[] { "break_out", "休息时出来", "防干扰模式" },
            new[] { "distract_1", "抓到分心·第一次", "{0} 会换成网站或程序的名字" },
            new[] { "distract_2", "抓到分心·第二次", "{0} 会换成网站或程序的名字" },
            new[] { "distract_3", "抓到分心·第三次", "{0} 会换成网站或程序的名字" },
            new[] { "distract_back", "回去学习了", "" },
            new[] { "meet", "见到另一只", "" },
            new[] { "visit", "串门", "" },
            new[] { "defend", "护着另一只", "" },
            new[] { "partner_asleep", "另一只睡着了", "" },
            new[] { "watch_start", "坐下一起看视频", "看视频、看直播时" },
            new[] { "watch", "看视频时的感想", "隔几分钟说一句；别催人哦" },
            new[] { "watch_laugh", "看视频笑出来", "" },
            new[] { "watch_wow", "看视频惊呼", "" },
            new[] { "watch_shh", "看视频时被戳", "" },
        };

        sealed class SceneItem
        {
            public string Key, Label, Hint;
            public int Builtin, Mine;
            public override string ToString() { return Label + "  " + Builtin + (Mine > 0 ? " + " + Mine : ""); }
        }

        /// <summary>A sentence in one of the lists (Source: null = built-in, else the file it came from).</summary>
        sealed class LineItem
        {
            public string Text, Source;

            public override string ToString()
            {
                string shown = Text.Replace("{name}", Profile.Name); // as she will say it
                return Source == null ? shown : shown + "    （" + Source + "）";
            }
        }

        readonly PetApp app;
        readonly ListBox scenes = new ListBox();
        readonly Label title = new Label();
        readonly Label hint = Ui.Label("", true);
        readonly ListBox mine = new ListBox();
        readonly ListBox builtin = new ListBox();
        readonly TextBox input = new TextBox();
        readonly CheckBox onlyMine = new CheckBox();
        readonly Button removeButton;
        readonly Label status = Ui.Label("", true);
        readonly Font titleFont;
        readonly List<SceneItem> items = new List<SceneItem>();
        LineBook book;
        string shownKey;
        bool syncing;

        public LinesPage(PetApp app, Font font)
        {
            this.app = app;
            AutoScaleMode = AutoScaleMode.Inherit;
            Dock = DockStyle.Fill;
            Font = font;
            titleFont = new Font(font.FontFamily, font.Size * 1.15f, FontStyle.Bold);

            var grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 2;
            grid.RowCount = 1;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));

            scenes.Dock = DockStyle.Fill;
            scenes.IntegralHeight = false;
            scenes.Margin = new Padding(0, 0, 8, 0);
            scenes.SelectedIndexChanged += delegate { if (!syncing) ShowScene(); };
            grid.Controls.Add(scenes, 0, 0);

            var right = new TableLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.ColumnCount = 1;
            right.Margin = new Padding(0);
            float[] rows = { -1, -1, -1, 50, -1, -1, -1, 50, -1 };
            right.RowCount = rows.Length;
            foreach (float r in rows) right.RowStyles.Add(r < 0 ? new RowStyle(SizeType.AutoSize) : new RowStyle(SizeType.Percent, r));

            title.AutoSize = true;
            title.Font = titleFont;
            title.Margin = new Padding(0, 2, 0, 0);
            right.Controls.Add(title, 0, 0);
            hint.Margin = new Padding(0, 2, 0, 4);
            right.Controls.Add(hint, 0, 1);

            right.Controls.Add(Ui.Label("你加的："), 0, 2);
            mine.Dock = DockStyle.Fill;
            mine.IntegralHeight = false;
            mine.SelectedIndexChanged += delegate { if (mine.SelectedIndex >= 0) builtin.ClearSelected(); UpdateButtons(); };
            mine.DoubleClick += delegate { TryIt(); };
            right.Controls.Add(mine, 0, 3);

            var add = new TableLayoutPanel();
            add.Dock = DockStyle.Fill;
            add.AutoSize = true;
            add.ColumnCount = 2;
            add.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            add.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            add.Margin = new Padding(0, 4, 0, 0);
            input.Dock = DockStyle.Fill;
            input.Margin = new Padding(0, 3, 6, 3);
            input.MaxLength = LineBook.MaxLength;
            Ui.CueBanner(input, "写一句新的…（Enter 加上）");
            add.Controls.Add(input, 0, 0);
            add.Controls.Add(Ui.Button("加上", Add), 1, 0);
            right.Controls.Add(add, 0, 4);

            removeButton = Ui.Button("删掉选中的", Remove);
            onlyMine.Text = "不说内置的，只说你加的";
            onlyMine.AutoSize = true;
            onlyMine.Margin = new Padding(8, 6, 0, 0);
            onlyMine.CheckedChanged += delegate { if (!syncing) SetOnlyMine(onlyMine.Checked); };
            right.Controls.Add(Ui.Row(removeButton, Ui.Button("说一句试试", TryIt), onlyMine), 0, 5);

            right.Controls.Add(Ui.Label("本来就有的："), 0, 6);
            builtin.Dock = DockStyle.Fill;
            builtin.IntegralHeight = false;
            builtin.ForeColor = Ui.Muted;
            builtin.SelectedIndexChanged += delegate { if (builtin.SelectedIndex >= 0) mine.ClearSelected(); UpdateButtons(); };
            builtin.DoubleClick += delegate { TryIt(); };
            right.Controls.Add(builtin, 0, 7);

            right.Controls.Add(Ui.Row(Ui.Button("打开台词文件夹", delegate { app.OpenLinesFolder(); }), status), 0, 8);
            grid.Controls.Add(right, 1, 0);
            Controls.Add(grid);

            app.LinesChanged += OnLinesChanged;
            Reload(null);
        }

        public void Shown()
        {
            input.Focus();
        }

        public bool HandleKey(Keys keyData)
        {
            if (keyData == Keys.Enter && input.Focused)
            {
                Add();
                return true;
            }
            if (keyData == Keys.Delete && mine.Focused)
            {
                Remove();
                return true;
            }
            return false;
        }

        SceneItem Current
        {
            get { int i = scenes.SelectedIndex; return i >= 0 && i < items.Count ? items[i] : null; }
        }

        void OnLinesChanged()
        {
            if (!IsDisposed) Reload(Current == null ? null : Current.Key);
        }

        /// <summary>Reads 我的台词.txt again and rebuilds the scene list, keeping the selection.</summary>
        void Reload(string keep)
        {
            book = LineFiles.ReadMine();
            LineBook builtinBook = app.BuiltinLines;
            var others = new List<KeyValuePair<string, LineBook>>();
            foreach (string f in LineFiles.Files())
                if (f != LineFiles.Mine) others.Add(new KeyValuePair<string, LineBook>(Path.GetFileName(f), LineFiles.Read(f)));

            items.Clear();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string[] k in Known)
            {
                if (builtinBook.Find(k[0]) == null && book.Find(k[0]) == null) continue;
                items.Add(Item(k[0], k[1], k[2], builtinBook, others));
                seen.Add(k[0]);
            }
            // scenes nobody gave a friendly name yet (new built-in ones, or ones typed by hand)
            foreach (LineBook b in new[] { builtinBook, book })
                foreach (LineBook.Scene s in b.Scenes)
                    if (seen.Add(s.Key)) items.Add(Item(s.Key, s.Key, "", builtinBook, others));

            syncing = true;
            scenes.BeginUpdate();
            scenes.Items.Clear();
            foreach (SceneItem it in items) scenes.Items.Add(it);
            int sel = keep == null ? 0 : Math.Max(0, items.FindIndex(it => it.Key == keep));
            if (items.Count > 0) scenes.SelectedIndex = sel;
            scenes.EndUpdate();
            syncing = false;
            ShowScene();
        }

        SceneItem Item(string key, string label, string hintText, LineBook builtinBook, List<KeyValuePair<string, LineBook>> others)
        {
            var it = new SceneItem();
            it.Key = key;
            it.Label = label;
            it.Hint = hintText;
            LineBook.Scene b = builtinBook.Find(key), m = book.Find(key);
            it.Builtin = b == null ? 0 : b.Lines.Count;
            foreach (var o in others)
            {
                LineBook.Scene s = o.Value.Find(key);
                if (s != null) it.Builtin += s.Lines.Count;
            }
            it.Mine = m == null ? 0 : m.Lines.Count;
            return it;
        }

        void ShowScene()
        {
            SceneItem it = Current;
            // after a reload of the same scene, keep whatever sentence was selected
            bool same = it != null && it.Key == shownKey;
            var keepMine = same ? mine.SelectedItem as LineItem : null;
            var keepBuiltin = same ? builtin.SelectedItem as LineItem : null;
            shownKey = it == null ? null : it.Key;
            syncing = true;
            mine.BeginUpdate();
            builtin.BeginUpdate();
            mine.Items.Clear();
            builtin.Items.Clear();
            if (it != null)
            {
                title.Text = it.Label;
                hint.Text = (it.Hint.Length > 0 ? it.Hint + "　·　" : "") + "写 {name} 会换成" + Profile.Pronoun + "现在的名字";
                LineBook.Scene m = book.Find(it.Key);
                if (m != null)
                    foreach (string l in m.Lines) mine.Items.Add(new LineItem { Text = l });
                LineBook.Scene b = app.BuiltinLines.Find(it.Key);
                if (b != null)
                    foreach (string l in b.Lines) builtin.Items.Add(new LineItem { Text = l });
                foreach (string f in LineFiles.Files())
                {
                    if (f == LineFiles.Mine) continue;
                    LineBook.Scene s = LineFiles.Read(f).Find(it.Key);
                    if (s != null)
                        foreach (string l in s.Lines) builtin.Items.Add(new LineItem { Text = l, Source = Path.GetFileName(f) });
                }
                onlyMine.Checked = m != null && m.Replace;
            }
            else
            {
                title.Text = "";
                hint.Text = "";
                onlyMine.Checked = false;
            }
            builtin.Enabled = !onlyMine.Checked;
            if (keepMine != null) Reselect(mine, keepMine);
            else if (keepBuiltin != null) Reselect(builtin, keepBuiltin);
            mine.EndUpdate();
            builtin.EndUpdate();
            syncing = false;
            UpdateButtons();
        }

        static void Reselect(ListBox list, LineItem keep)
        {
            for (int i = 0; i < list.Items.Count; i++)
            {
                var l = (LineItem)list.Items[i];
                if (l.Text == keep.Text && l.Source == keep.Source) { list.SelectedIndex = i; return; }
            }
        }

        void UpdateButtons()
        {
            removeButton.Enabled = mine.SelectedIndex >= 0;
            onlyMine.Enabled = Current != null;
        }

        void Save(string message)
        {
            try
            {
                LineFiles.WriteMine(book);
            }
            catch (Exception ex)
            {
                status.Text = "没存上：" + ex.Message;
                status.ForeColor = Ui.Accent;
                return;
            }
            app.ReloadLines(); // she can say it right away; also refreshes this page
            status.Text = message;
            status.ForeColor = Ui.Muted;
        }

        void Add()
        {
            SceneItem it = Current;
            if (it == null) return;
            string problem = LineBook.Problem(input.Text);
            if (problem != null)
            {
                status.Text = problem;
                status.ForeColor = Ui.Accent;
                input.Focus();
                return;
            }
            string text = LineBook.Clean(input.Text);
            if (!book.Add(it.Key, text))
            {
                status.Text = "这句已经加过啦";
                status.ForeColor = Ui.Accent;
                return;
            }
            input.Clear();
            Save("加好啦！「" + it.Label + "」的时候" + Profile.Pronoun + "会说这句");
            SelectMine(text);
            input.Focus();
        }

        void SelectMine(string text)
        {
            for (int i = 0; i < mine.Items.Count; i++)
                if (((LineItem)mine.Items[i]).Text == text) { mine.SelectedIndex = i; return; }
        }

        void Remove()
        {
            SceneItem it = Current;
            var line = mine.SelectedItem as LineItem;
            if (it == null || line == null) return;
            int index = mine.SelectedIndex;
            if (!book.Remove(it.Key, line.Text)) return;
            Save("删掉了");
            if (mine.Items.Count > 0) mine.SelectedIndex = Math.Min(index, mine.Items.Count - 1);
        }

        void SetOnlyMine(bool on)
        {
            SceneItem it = Current;
            if (it == null) return;
            book.SetReplace(it.Key, on);
            LineBook.Scene m = book.Find(it.Key);
            Save(!on ? "内置的也会说" : m == null || m.Lines.Count == 0 ? "现在这个时候" + Profile.Pronoun + "一句都不说，记得加几句" : "只说你加的");
        }

        /// <summary>She says the selected sentence (or what's typed) right now.</summary>
        void TryIt()
        {
            SceneItem it = Current;
            var line = (mine.SelectedItem ?? builtin.SelectedItem) as LineItem;
            string text = line != null ? line.Text : LineBook.Clean(input.Text);
            if (text.Length == 0)
            {
                status.Text = "先选一句，或者写一句";
                status.ForeColor = Ui.Accent;
                return;
            }
            app.TryLine(text, it == null ? null : it.Key);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                app.LinesChanged -= OnLinesChanged;
                titleFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
