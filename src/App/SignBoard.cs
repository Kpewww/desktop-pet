using System;
using System.Collections.Generic;
using System.Drawing;
using Aly.Core.Engine;

namespace Aly.App
{
    /// <summary>
    /// The board on her sign stick, laid out once per content and drawn into the pet window's
    /// buffer. The frame uses her pixel size so it matches the sprite; the text (and a note's
    /// buttons) use the UI scale like the speech bubbles, so they stay crisp and readable.
    /// Clipboard signs are cream with a pink frame; reminder notes are a yellow sticky note with
    /// a red pin and answer buttons.
    /// </summary>
    sealed unsafe class SignBoard
    {
        struct Colors
        {
            public uint Frame, Shade, Paper, Ink, Button, ButtonHot, ButtonEdge;
        }

        const uint Outline = 0xFF2B1D2E, Pin = 0xFFE0455A, PinLight = 0xFFFF9AA8;
        static readonly Colors ClipColors = new Colors
        {
            Frame = 0xFFE8AEA6, Shade = 0xFFCC8F89, Paper = 0xFFFFFDF6, Ink = 0xFF3B2D44,
        };
        static readonly Colors NoteColors = new Colors
        {
            Frame = 0xFFF2C45A, Shade = 0xFFD9A441, Paper = 0xFFFFF6C2, Ink = 0xFF5B4526,
            Button = 0xFFFFE38A, ButtonHot = 0xFFFFCF4D, ButtonEdge = 0xFFC98E2C,
        };

        /// <summary>Font pixels per line: 11 CJK characters, so 20 of them plus "..." make two lines.</summary>
        const int MaxLineWidth = 132;
        const int MaxLines = 3;
        const int PadX = 3, PadY = 2;
        const int MinPaperArt = 10;
        const int ButtonPadX = 4, ButtonGap = 5, ButtonsTop = 3;

        public readonly int Serial;
        public readonly int ArtScale, UiScale;
        /// <summary>Full size in physical pixels.</summary>
        public readonly int Width, Height;
        public readonly bool IsNote;

        readonly PixelFont font;
        readonly Colors colors;
        readonly List<string> lines;
        readonly string[] buttons;
        readonly int[] buttonW;      // font pixels
        readonly int textW, textH;   // font pixels
        readonly int buttonsW, buttonH;
        readonly int paperW, paperH; // physical pixels

        public SignBoard(PixelFont font, SignContent content, int artScale, int uiScale)
        {
            this.font = font;
            Serial = content.Serial;
            ArtScale = artScale;
            UiScale = uiScale;
            IsNote = content.Style == SignStyle.Note;
            colors = IsNote ? NoteColors : ClipColors;
            lines = Layout(font, font.Displayable(content.Text));
            foreach (string l in lines) textW = Math.Max(textW, font.Measure(l));
            textH = lines.Count * font.LineHeight;

            buttons = content.Buttons ?? new string[0];
            buttonW = new int[buttons.Length];
            buttonH = font.LineHeight + 2;
            for (int i = 0; i < buttons.Length; i++)
            {
                buttonW[i] = font.Measure(buttons[i]) + 2 * ButtonPadX + 2;
                buttonsW += buttonW[i] + (i > 0 ? ButtonGap : 0);
            }
            int contentW = Math.Max(textW, buttonsW), contentH = textH + (buttons.Length > 0 ? ButtonsTop + buttonH : 0);

            int a = artScale;
            paperW = RoundUp(Math.Max((contentW + 2 * PadX) * uiScale, MinPaperArt * a), 2 * a); // even: centred on the stick
            paperH = RoundUp((contentH + 2 * PadY) * uiScale, a);
            Width = paperW + 4 * a;
            Height = paperH + 4 * a;
        }

        static int RoundUp(int v, int step) { return (v + step - 1) / step * step; }

        /// <summary>
        /// Greedy wrapping, then narrowed as far as possible without adding a line, so two
        /// lines come out about equally long instead of one full and one stub.
        /// </summary>
        static List<string> Layout(PixelFont font, string text)
        {
            int total = font.Measure(text);
            if (total <= MaxLineWidth) return new List<string> { text };
            List<string> best = font.Wrap(text, MaxLineWidth);
            int n = best.Count;
            for (int w = (total + n - 1) / n; w < MaxLineWidth; w++)
            {
                List<string> l = font.Wrap(text, w);
                if (l.Count <= n)
                {
                    best = l;
                    break;
                }
            }
            if (n == 2)
            {
                List<string> nice = SplitAtPunctuation(font, text, Widest(font, best) + 30);
                if (nice != null) best = nice;
            }
            if (best.Count > MaxLines) best = best.GetRange(0, MaxLines);
            return best;
        }

        const string Breaks = "！!？?，,。、；;：:～~… ";

        /// <summary>
        /// Two lines split right after punctuation or a space ("专注完成！/ 今天第 3 个番茄") instead
        /// of mid-phrase — as long as neither line gets wider than limit.
        /// </summary>
        static List<string> SplitAtPunctuation(PixelFont font, string text, int limit)
        {
            List<string> best = null;
            int bestWidth = int.MaxValue;
            for (int i = 1; i < text.Length - 1; i++)
            {
                if (Breaks.IndexOf(text[i]) < 0) continue;
                string a = text.Substring(0, i + 1).TrimEnd(), b = text.Substring(i + 1).TrimStart();
                if (a.Length == 0 || b.Length == 0) continue;
                int w = Math.Max(font.Measure(a), font.Measure(b));
                if (w <= MaxLineWidth && w <= limit && w < bestWidth)
                {
                    bestWidth = w;
                    best = new List<string> { a, b };
                }
            }
            return best;
        }

        static int Widest(PixelFont font, List<string> lines)
        {
            int w = 0;
            foreach (string l in lines) w = Math.Max(w, font.Measure(l));
            return w;
        }

        public int ButtonCount { get { return buttons.Length; } }

        /// <summary>How far the note's pin sticks out above the board, physical pixels.</summary>
        public int PinHeight { get { return IsNote ? ArtScale : 0; } }

        /// <summary>A button's rectangle relative to the board's top-left corner (full width), physical pixels.</summary>
        public Rectangle ButtonRect(int i)
        {
            int s = UiScale;
            int x = 2 * ArtScale + (paperW - buttonsW * s) / 2;
            for (int k = 0; k < i; k++) x += (buttonW[k] + ButtonGap) * s;
            int y = TextTop + (textH + ButtonsTop) * s;
            return new Rectangle(x, y, buttonW[i] * s, buttonH * s);
        }

        int ContentH { get { return textH + (buttons.Length > 0 ? ButtonsTop + buttonH : 0); } }
        int TextTop { get { return 2 * ArtScale + (paperH - ContentH * UiScale) / 2; } }

        /// <summary>Paper width when squeezed sideways (unfolding or flipping over), in whole pixel pairs.</summary>
        int PaperWidth(double widthFactor)
        {
            if (widthFactor >= 1) return paperW;
            int step = 2 * ArtScale;
            return (int)(paperW * Math.Max(0, widthFactor)) / step * step;
        }

        public int WidthAt(double widthFactor)
        {
            return PaperWidth(widthFactor) + 4 * ArtScale;
        }

        /// <summary>
        /// Draws the board centred on cx with its bottom edge at bottom (buffer pixels); below
        /// full width it is drawn without text. Its left edge is at cx - WidthAt / 2.
        /// hot = the button under the cursor (-1 for none).
        /// </summary>
        public void Draw(IntPtr bits, int stride, int bw, int bh, int cx, int bottom, double widthFactor, int hot)
        {
            uint* d = (uint*)bits;
            int a = ArtScale;
            int pw = PaperWidth(widthFactor);
            int width = pw + 4 * a;
            int left = cx - width / 2;
            int top = bottom - Height;
            int wArt = width / a, hArt = Height / a;
            for (int j = 0; j < hArt; j++)
            {
                for (int i = 0; i < wArt; i++)
                {
                    bool edge = i == 0 || j == 0 || i == wArt - 1 || j == hArt - 1;
                    bool corner = (i == 0 || i == wArt - 1) && (j == 0 || j == hArt - 1);
                    if (corner) continue;
                    if (!edge && i > 1 && j > 1 && i < wArt - 2 && j < hArt - 2) continue; // paper, filled below
                    uint c = edge ? Outline : (j == hArt - 2 ? colors.Shade : colors.Frame);
                    PixelFont.Fill(d, stride, bw, bh, left + i * a, top + j * a, a, a, c);
                }
            }
            if (pw > 0) PixelFont.Fill(d, stride, bw, bh, left + 2 * a, top + 2 * a, pw, paperH, colors.Paper);
            if (IsNote)
            {
                // a push pin in the top frame
                int px = cx - a, py = top;
                PixelFont.Fill(d, stride, bw, bh, px - a, py, 4 * a, 2 * a, Outline);
                PixelFont.Fill(d, stride, bw, bh, px, py - a, 2 * a, 4 * a, Outline);
                PixelFont.Fill(d, stride, bw, bh, px, py, 2 * a, 2 * a, Pin);
                PixelFont.Fill(d, stride, bw, bh, px, py, a, a, PinLight);
            }
            if (widthFactor < 1 || pw <= 0) return;

            int s = UiScale;
            int baseline = top + TextTop + font.Ascent * s;
            foreach (string l in lines)
            {
                int lx = left + 2 * a + (paperW - font.Measure(l) * s) / 2;
                font.Draw(d, stride, bw, bh, l, lx, baseline, s, colors.Ink);
                baseline += font.LineHeight * s;
            }
            for (int i = 0; i < buttons.Length; i++)
            {
                Rectangle r = ButtonRect(i);
                int x = left + r.X, y = top + r.Y;
                // 1-px rounded frame
                PixelFont.Fill(d, stride, bw, bh, x + s, y, r.Width - 2 * s, s, colors.ButtonEdge);
                PixelFont.Fill(d, stride, bw, bh, x + s, y + r.Height - s, r.Width - 2 * s, s, colors.ButtonEdge);
                PixelFont.Fill(d, stride, bw, bh, x, y + s, s, r.Height - 2 * s, colors.ButtonEdge);
                PixelFont.Fill(d, stride, bw, bh, x + r.Width - s, y + s, s, r.Height - 2 * s, colors.ButtonEdge);
                PixelFont.Fill(d, stride, bw, bh, x + s, y + s, r.Width - 2 * s, r.Height - 2 * s, i == hot ? colors.ButtonHot : colors.Button);
                int tx = x + (r.Width - font.Measure(buttons[i]) * s) / 2;
                font.Draw(d, stride, bw, bh, buttons[i], tx, y + (1 + font.Ascent) * s, s, colors.Ink);
            }
        }
    }
}
