using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Aly.ArtBuild
{
    /// <summary>
    /// Converts a BDF bitmap font into the compact gzip'd format the app renders:
    ///   "ALYF" v1, ascent, descent, glyph table sorted by code point, packed 1-bit rows.
    /// </summary>
    static class FontBuilder
    {
        public const int Magic = 0x46594C41; // "ALYF"

        sealed class Glyph
        {
            public int Code;
            public int Advance;
            public int XOff, YOff, W, H;
            public byte[] Bits;
        }

        /// <summary>
        /// Keeps what bubbles and the clipboard sign realistically show — Latin, punctuation,
        /// symbols, kana and the 20 902 common CJK ideographs — and drops Hangul syllables and the
        /// rare CJK extensions to stay small. Anything missing is drawn as a box.
        /// </summary>
        static bool Wanted(int c)
        {
            return (c >= 0x20 && c <= 0x7E)
                || (c >= 0xA0 && c <= 0x24F)      // Latin-1, Latin extended A/B
                || (c >= 0x370 && c <= 0x4FF)     // Greek, Cyrillic
                || (c >= 0x2000 && c <= 0x2BFF)   // punctuation, arrows, maths, shapes, symbols
                || (c >= 0x3000 && c <= 0x33FF)   // CJK punctuation, kana, bopomofo, enclosed
                || (c >= 0x4E00 && c <= 0x9FFF)   // CJK unified ideographs
                || (c >= 0xFE30 && c <= 0xFE4F)   // CJK compatibility forms
                || (c >= 0xFF00 && c <= 0xFFEF);  // half/full-width forms
        }

        public static void Build(string bdfPath, string outPath, out int glyphs, out long bytes)
        {
            int ascent = 0, descent = 0;
            var list = new List<Glyph>();
            Glyph g = null;
            int bitmapRow = -1;
            foreach (string raw in File.ReadLines(bdfPath))
            {
                string line = raw.Trim();
                if (bitmapRow >= 0 && g != null)
                {
                    if (line == "ENDCHAR")
                    {
                        if (g.Code >= 0) list.Add(g);
                        g = null;
                        bitmapRow = -1;
                        continue;
                    }
                    int stride = (g.W + 7) / 8;
                    for (int b = 0; b < stride && b * 2 + 1 < line.Length; b++)
                        g.Bits[bitmapRow * stride + b] = byte.Parse(line.Substring(b * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    bitmapRow++;
                    continue;
                }
                string[] t = line.Split(' ');
                switch (t[0])
                {
                    case "FONT_ASCENT": ascent = int.Parse(t[1], CultureInfo.InvariantCulture); break;
                    case "FONT_DESCENT": descent = int.Parse(t[1], CultureInfo.InvariantCulture); break;
                    case "STARTCHAR": g = new Glyph(); g.Code = -1; break;
                    case "ENCODING": if (g != null) g.Code = int.Parse(t[1], CultureInfo.InvariantCulture); break;
                    case "DWIDTH": if (g != null) g.Advance = int.Parse(t[1], CultureInfo.InvariantCulture); break;
                    case "BBX":
                        if (g != null)
                        {
                            g.W = int.Parse(t[1], CultureInfo.InvariantCulture);
                            g.H = int.Parse(t[2], CultureInfo.InvariantCulture);
                            g.XOff = int.Parse(t[3], CultureInfo.InvariantCulture);
                            g.YOff = int.Parse(t[4], CultureInfo.InvariantCulture);
                            g.Bits = new byte[((g.W + 7) / 8) * g.H];
                        }
                        break;
                    case "BITMAP": if (g != null) bitmapRow = 0; break;
                }
            }
            if (ascent == 0) throw new ArtException(bdfPath + ": no FONT_ASCENT");
            list.RemoveAll(x => !Wanted(x.Code));
            list.Sort((a, b) => a.Code.CompareTo(b.Code));

            using (var fs = File.Create(outPath))
            using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
            using (var w = new BinaryWriter(gz, Encoding.UTF8))
            {
                w.Write(Magic);
                w.Write(1);
                w.Write((short)ascent);
                w.Write((short)descent);
                w.Write(list.Count);
                int offset = 0;
                foreach (Glyph x in list)
                {
                    w.Write(x.Code);
                    w.Write((byte)x.Advance);
                    w.Write((sbyte)x.XOff);
                    w.Write((sbyte)x.YOff);
                    w.Write((byte)x.W);
                    w.Write((byte)x.H);
                    w.Write(offset);
                    offset += x.Bits.Length;
                }
                w.Write(offset);
                foreach (Glyph x in list) w.Write(x.Bits);
            }
            glyphs = list.Count;
            bytes = new FileInfo(outPath).Length;
        }
    }
}
