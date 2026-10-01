using System;
using System.Collections.Generic;
using Aly.Core.Engine;

namespace Aly.App
{
    /// <summary>
    /// A speech bubble laid out once per line of speech and drawn into the pet window's
    /// buffer. Everything (box, tail, text) is pixel art at one integer scale.
    /// </summary>
    sealed unsafe class Bubble
    {
        const uint Ink = 0xFF3B2D44, Paper = 0xFFFFFDFB, Edge = 0xFF2B1D2E;
        const uint ShoutInk = 0xFFE0455A, ShoutPaper = 0xFFFFF0F2, ShoutEdge = 0xFF9E2440;
        const int PadX = 4, PadY = 2, Tail = 4;

        public readonly int Serial;
        public readonly bool Shout;
        public readonly int Scale;
        readonly List<string> lines;
        readonly int textW;
        readonly PixelFont font;

        /// <summary>Full size including the tail, in physical pixels.</summary>
        public readonly int Width, Height;

        public Bubble(PixelFont font, Speech speech, int uiScale)
        {
            this.font = font;
            Serial = speech.Serial;
            Shout = speech.Style == SpeechStyle.Shout;
            Scale = uiScale * (Shout ? 2 : 1);
            lines = font.Wrap(font.Displayable(speech.Text), Shout ? 96 : 132);
            textW = 0;
            foreach (string l in lines) textW = Math.Max(textW, font.Measure(l));
            Width = (textW + PadX * 2 + 2) * Scale;
            Height = (lines.Count * font.LineHeight + PadY * 2 + 2 + Tail) * Scale;
        }

        /// <summary>Draws with the box's top-left at (x, y); the tail points at tipX.</summary>
        public void Draw(IntPtr bits, int stride, int bw, int bh, int x, int y, int tipX)
        {
            uint* d = (uint*)bits;
            int s = Scale;
            uint ink = Shout ? ShoutInk : Ink, paper = Shout ? ShoutPaper : Paper, edge = Shout ? ShoutEdge : Edge;
            int boxW = Width / s, boxH = Height / s - Tail;

            // Box with pixel-rounded corners: edge ring, paper inside.
            for (int j = 0; j < boxH; j++)
            {
                for (int i = 0; i < boxW; i++)
                {
                    bool corner = (i == 0 || i == boxW - 1) && (j == 0 || j == boxH - 1);
                    if (corner) continue;
                    bool border = i == 0 || j == 0 || i == boxW - 1 || j == boxH - 1
                        || ((i == 1 || i == boxW - 2) && (j == 1 || j == boxH - 2));
                    PixelFont.Fill(d, stride, bw, bh, x + i * s, y + j * s, s, s, border ? edge : paper);
                }
            }

            // Tail: a small triangle under the box, pointing towards her head.
            int tip = (tipX - x) / s;
            tip = Math.Max(3, Math.Min(boxW - 4, tip));
            for (int k = 0; k < Tail; k++)
            {
                int half = Tail - 1 - k;
                for (int i = -half - 1; i <= half + 1; i++)
                {
                    bool border = i == -half - 1 || i == half + 1;
                    int py = y + (boxH - 1 + k) * s;
                    if (k == 0 && !border) { PixelFont.Fill(d, stride, bw, bh, x + (tip + i) * s, py, s, s, paper); continue; }
                    PixelFont.Fill(d, stride, bw, bh, x + (tip + i) * s, py, s, s, border ? edge : paper);
                }
            }
            PixelFont.Fill(d, stride, bw, bh, x + tip * s, y + (boxH - 1 + Tail) * s, s, s, edge);

            int baseline = y + (1 + PadY + font.Ascent) * s;
            foreach (string l in lines)
            {
                int lx = x + (1 + PadX + (textW - font.Measure(l)) / 2) * s;
                font.Draw(d, stride, bw, bh, l, lx, baseline, s, ink);
                baseline += font.LineHeight * s;
            }
        }
    }
}
