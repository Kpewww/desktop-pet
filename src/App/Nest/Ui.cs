using System;
using System.Drawing;
using System.Windows.Forms;

namespace Aly.App
{
    /// <summary>小窝's tabs, in the order they appear (衣柜 only for pets with a wardrobe).</summary>
    enum NestPage
    {
        Clipboard,
        Todos,
        Focus,
        Lines,
        Wardrobe,
        Settings,
    }

    /// <summary>A page of 小窝.</summary>
    interface INestPage
    {
        /// <summary>The page became the visible one.</summary>
        void Shown();
        /// <summary>Keys the page handles itself (Enter, arrows…); true if handled.</summary>
        bool HandleKey(Keys keyData);
    }

    /// <summary>Shared look for 小窝's pages.</summary>
    static class Ui
    {
        public static readonly Color Accent = Color.FromArgb(0xC8, 0x5A, 0x6C);
        public static readonly Color Muted = Color.FromArgb(0x86, 0x7C, 0x8C);

        public const TextFormatFlags Line = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        public static Button Button(string text, Action onClick)
        {
            var b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(6, 1, 6, 1);
            b.Margin = new Padding(0, 2, 6, 2);
            b.UseVisualStyleBackColor = true;
            b.Click += delegate { onClick(); };
            return b;
        }

        public static Label Label(string text, bool muted = false)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Anchor = AnchorStyles.Left;
            l.Margin = new Padding(0, 6, 6, 6);
            if (muted) l.ForeColor = Muted;
            return l;
        }

        public static NumericUpDown Number(int min, int max, int value)
        {
            var n = new NumericUpDown();
            n.Minimum = min;
            n.Maximum = max;
            n.Value = Math.Max(min, Math.Min(max, value));
            n.Width = 56;
            n.Anchor = AnchorStyles.Left;
            n.Margin = new Padding(0, 3, 6, 3);
            n.TextAlign = HorizontalAlignment.Right;
            return n;
        }

        /// <summary>A row of controls laid out left to right.</summary>
        public static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel();
            row.AutoSize = true;
            row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row.WrapContents = false;
            row.Dock = DockStyle.Fill;
            row.Margin = new Padding(0);
            row.Controls.AddRange(controls);
            return row;
        }

        public static void CueBanner(TextBox box, string text)
        {
            if (box.IsHandleCreated) Win32.SendMessage(box.Handle, Win32.EM_SETCUEBANNER, new IntPtr(1), text);
            else box.HandleCreated += delegate { Win32.SendMessage(box.Handle, Win32.EM_SETCUEBANNER, new IntPtr(1), text); };
        }

        /// <summary>"今天 18:00", "明天 09:00", "9月26日 18:00".</summary>
        public static string When(DateTime local)
        {
            DateTime today = DateTime.Today;
            if (local.Date == today) return "今天 " + local.ToString("HH:mm");
            if (local.Date == today.AddDays(1)) return "明天 " + local.ToString("HH:mm");
            if (local.Date == today.AddDays(-1)) return "昨天 " + local.ToString("HH:mm");
            return local.ToString("M月d日 HH:mm");
        }
    }
}
