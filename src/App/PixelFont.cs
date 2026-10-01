using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Aly.Core;

namespace Aly.App
{
    /// <summary>
    /// 1-bit bitmap font (Fusion Pixel 12px, converted by ArtBuild). Drawn pixel by pixel at
    /// an integer scale, so text is always crisp. Missing characters become a small box.
    /// </summary>
    sealed unsafe class PixelFont
    {
        const int Magic = 0x46594C41;

        public readonly int Ascent;
        public readonly int Descent;
        readonly int[] codes;
        readonly byte[] adv;
        readonly sbyte[] xo, yo;
        readonly byte[] gw, gh;
        readonly int[] offs;
        readonly byte[] bits;

        public int LineHeight { get { return Ascent + Descent; } }

        PixelFont(int ascent, int descent, int n)
        {
            Ascent = ascent;
            Descent = descent;
            codes = new int[n];
            adv = new byte[n];
            xo = new sbyte[n];
            yo = new sbyte[n];
            gw = new byte[n];
            gh = new byte[n];
            offs = new int[n];
            bits = new byte[0];
        }

        PixelFont(PixelFont header, byte[] data)
        {
            Ascent = header.Ascent;
            Descent = header.Descent;
            codes = header.codes;
            adv = header.adv;
            xo = header.xo;
            yo = header.yo;
            gw = header.gw;
            gh = header.gh;
            offs = header.offs;
            bits = data;
        }

        public static PixelFont Load(Stream stream)
        {
            using (var gz = new GZipStream(stream, CompressionMode.Decompress, true))
            using (var r = new BinaryReader(gz, Encoding.UTF8))
            {
                if (r.ReadInt32() != Magic || r.ReadInt32() != 1) throw new InvalidDataException("not a pixel font");
                int ascent = r.ReadInt16(), descent = r.ReadInt16();
                int n = r.ReadInt32();
                var f = new PixelFont(ascent, descent, n);
                for (int i = 0; i < n; i++)
                {
                    f.codes[i] = r.ReadInt32();
                    f.adv[i] = r.ReadByte();
                    f.xo[i] = r.ReadSByte();
                    f.yo[i] = r.ReadSByte();
                    f.gw[i] = r.ReadByte();
                    f.gh[i] = r.ReadByte();
                    f.offs[i] = r.ReadInt32();
                }
                int len = r.ReadInt32();
                byte[] data = r.ReadBytes(len);
                if (data.Length != len) throw new InvalidDataException("truncated font");
                return new PixelFont(f, data);
            }
        }

        int Find(int cp)
        {
            int lo = 0, hi = codes.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int c = codes[mid];
                if (c == cp) return mid;
                if (c < cp) lo = mid + 1;
                else hi = mid - 1;
            }
            return -1;
        }

        static bool Wide(int cp) { return cp >= 0x2E80; }

        public bool Has(int cp) { return Find(cp) >= 0; }

        /// <summary>
        /// Makes text safe to draw: a perceived character the font lacks (an emoji, even a
        /// five-part family) becomes one box, and invisible joiners and variation selectors
        /// that would otherwise draw as boxes of their own are dropped.
        /// </summary>
        public string Displayable(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            foreach (string g in ClipText.Graphemes(text))
            {
                int i = 0;
                int first = NextCodePoint(g, ref i);
                if (!Has(first))
                {
                    if (first == '\n' || !Invisible(g, 0)) sb.Append(g, 0, i); // a line break, or one box
                    continue;
                }
                sb.Append(g, 0, i);
                while (i < g.Length)
                {
                    int start = i;
                    int cp = NextCodePoint(g, ref i);
                    if (Has(cp)) sb.Append(g, start, i - start);
                }
            }
            return sb.ToString();
        }

        static bool Invisible(string s, int index)
        {
            switch (CharUnicodeInfo.GetUnicodeCategory(s, index))
            {
                case UnicodeCategory.Format:
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.EnclosingMark:
                case UnicodeCategory.Control:
                    return true;
                default:
                    return false;
            }
        }

        public int Advance(int cp)
        {
            int i = Find(cp);
            if (i >= 0) return adv[i];
            return Wide(cp) ? 12 : 6;
        }

        public static int NextCodePoint(string s, ref int i)
        {
            char c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                int cp = char.ConvertToUtf32(c, s[i + 1]);
                i += 2;
                return cp;
            }
            i++;
            return c;
        }

        public int Measure(string s)
        {
            int w = 0;
            for (int i = 0; i < s.Length;) w += Advance(NextCodePoint(s, ref i));
            return w;
        }

        /// <summary>
        /// Greedy wrap to maxWidth (font pixels). CJK can break between any two characters;
        /// Latin prefers breaking at spaces.
        /// </summary>
        public List<string> Wrap(string text, int maxWidth)
        {
            var lines = new List<string>();
            var cur = new StringBuilder();
            int width = 0;
            int lastSpace = -1;
            for (int i = 0; i < text.Length;)
            {
                int start = i;
                int cp = NextCodePoint(text, ref i);
                string ch = text.Substring(start, i - start);
                if (cp == '\n')
                {
                    lines.Add(cur.ToString());
                    cur.Length = 0;
                    width = 0;
                    lastSpace = -1;
                    continue;
                }
                int a = Advance(cp);
                if (width + a > maxWidth && cur.Length > 0)
                {
                    if (!Wide(cp) && lastSpace > 0)
                    {
                        string line = cur.ToString(0, lastSpace);
                        string rest = cur.ToString(lastSpace + 1, cur.Length - lastSpace - 1);
                        lines.Add(line);
                        cur.Length = 0;
                        cur.Append(rest);
                        width = Measure(rest);
                    }
                    else
                    {
                        lines.Add(cur.ToString());
                        cur.Length = 0;
                        width = 0;
                    }
                    lastSpace = -1;
                    if (cp == ' ') continue;
                }
                if (cp == ' ') lastSpace = cur.Length;
                cur.Append(ch);
                width += a;
            }
            if (cur.Length > 0) lines.Add(cur.ToString());
            return lines;
        }

        /// <summary>Draws a line with its baseline at y. Colours are premultiplied BGRA.</summary>
        public void Draw(uint* dst, int stride, int bw, int bh, string s, int x, int baseline, int scale, uint color)
        {
            for (int i = 0; i < s.Length;)
            {
                int cp = NextCodePoint(s, ref i);
                int g = Find(cp);
                if (g < 0)
                {
                    if (cp > ' ') Box(dst, stride, bw, bh, x, baseline, scale, color, Wide(cp) ? 10 : 4);
                    x += (Wide(cp) ? 12 : 6) * scale;
                    continue;
                }
                int w = gw[g], h = gh[g], stride8 = (w + 7) / 8, o = offs[g];
                for (int row = 0; row < h; row++)
                {
                    int py = baseline - (yo[g] + h - row) * scale;
                    for (int col = 0; col < w; col++)
                    {
                        if ((bits[o + row * stride8 + (col >> 3)] & (0x80 >> (col & 7))) == 0) continue;
                        Fill(dst, stride, bw, bh, x + (xo[g] + col) * scale, py, scale, scale, color);
                    }
                }
                x += adv[g] * scale;
            }
        }

        void Box(uint* dst, int stride, int bw, int bh, int x, int baseline, int s, uint c, int w)
        {
            int top = baseline - 10 * s;
            Fill(dst, stride, bw, bh, x + s, top, w * s, s, c);
            Fill(dst, stride, bw, bh, x + s, baseline - s, w * s, s, c);
            Fill(dst, stride, bw, bh, x + s, top, s, 10 * s, c);
            Fill(dst, stride, bw, bh, x + w * s, top, s, 10 * s, c);
        }

        internal static void Fill(uint* dst, int stride, int bw, int bh, int x0, int y0, int w, int h, uint c)
        {
            int x1 = x0 + w, y1 = y0 + h;
            if (x0 < 0) x0 = 0;
            if (y0 < 0) y0 = 0;
            if (x1 > bw) x1 = bw;
            if (y1 > bh) y1 = bh;
            for (int y = y0; y < y1; y++)
            {
                uint* row = dst + y * stride;
                for (int x = x0; x < x1; x++) row[x] = c;
            }
        }
    }
}
