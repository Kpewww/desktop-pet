using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Aly.Core;

namespace Aly.ArtBuild
{
    /// <summary>
    /// Flattens atlas frames back onto the full canvas, adding the runtime-drawn eyes
    /// (default gaze) wherever a frame carries the eyes.gaze anchor — exactly as the app does.
    /// </summary>
    sealed class FrameFlattener
    {
        public const string GazeAnchor = "eyes.gaze";
        public const string DefaultEyes = "gaze_open_c";

        readonly Atlas atlas;
        readonly int gazeAnchor;
        readonly AtlasClip eyes;

        public FrameFlattener(Atlas atlas)
        {
            this.atlas = atlas;
            gazeAnchor = atlas.AnchorIndex(GazeAnchor);
            eyes = atlas.GetClip(DefaultEyes);
        }

        public byte[] Flatten(int frame)
        {
            var c = new byte[atlas.CanvasWidth * atlas.CanvasHeight];
            Stamp(c, atlas.Frames[frame], 0, 0);
            int ax, ay;
            if (eyes != null && gazeAnchor >= 0 && atlas.TryGetAnchor(frame, gazeAnchor, out ax, out ay))
                Stamp(c, atlas.Frames[eyes.Frames[0]], ax - atlas.OriginX, ay - atlas.OriginY);
            return c;
        }

        void Stamp(byte[] c, AtlasFrame f, int dx, int dy)
        {
            int W = atlas.CanvasWidth, H = atlas.CanvasHeight;
            for (int y = 0; y < f.Height; y++)
                for (int x = 0; x < f.Width; x++)
                {
                    byte v = f.Pixels[y * f.Width + x];
                    if (v == 0) continue;
                    int cx = f.OffsetX + x + dx, cy = f.OffsetY + y + dy;
                    if (cx >= 0 && cy >= 0 && cx < W && cy < H) c[cy * W + cx] = v;
                }
        }
    }

    /// <summary>Review images under art/out/preview — never shipped, never committed.</summary>
    static class Preview
    {
        const int LightA = unchecked((int)0xFFF4F1F6), LightB = unchecked((int)0xFFE4DEE9);
        const int DarkA = unchecked((int)0xFF2A2530), DarkB = unchecked((int)0xFF1E1A23);
        const int Paper = unchecked((int)0xFFFFFFFF);

        public static void Reset(string dir)
        {
            Directory.CreateDirectory(dir);
            foreach (string f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
            foreach (string f in Directory.GetFiles(dir, "*.gif")) File.Delete(f);
        }

        /// <summary>One image per clip: every frame on a light and a dark background.</summary>
        public static void WriteClips(string dir, Atlas atlas, int zoom)
        {
            const int gap = 6, label = 18, title = 22;
            var flat = new FrameFlattener(atlas);
            int cw = atlas.CanvasWidth * zoom, ch = atlas.CanvasHeight * zoom;
            foreach (AtlasClip clip in atlas.ClipList)
            {
                int n = clip.Count, cols = Math.Min(n, 8), rows = (n + cols - 1) / cols;
                int cellW = cw + gap, cellH = ch * 2 + label + gap;
                int W = cols * cellW + gap, H = title + rows * cellH + gap;
                var img = NewImage(W, H);
                for (int i = 0; i < n; i++)
                {
                    int cx = gap + (i % cols) * cellW, cy = title + (i / cols) * cellH;
                    byte[] canvas = flat.Flatten(clip.Frames[i]);
                    Checker(img, W, cx, cy, cw, ch, zoom * 4, LightA, LightB);
                    Blit(img, W, atlas, canvas, cx, cy, zoom);
                    Checker(img, W, cx, cy + ch, cw, ch, zoom * 4, DarkA, DarkB);
                    Blit(img, W, atlas, canvas, cx, cy + ch, zoom);
                }
                using (Bitmap bmp = ToBitmap(img, W, H))
                using (Graphics g = Graphics.FromImage(bmp))
                using (var font = new Font("Consolas", 9f))
                {
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.DrawString(string.Format("{0}{1}   {2} frames, {3} ms", clip.Name, clip.Loop ? " (loop)" : "", n, clip.TotalDuration),
                        font, Brushes.Black, gap, 4);
                    for (int i = 0; i < n; i++)
                    {
                        int cx = gap + (i % cols) * cellW, cy = title + (i / cols) * cellH;
                        g.DrawString(string.Format("{0}: {1} ms  #{2}", i, clip.Durations[i], clip.Frames[i]),
                            font, Brushes.DimGray, cx, cy + 2 * ch + 2);
                    }
                    bmp.Save(Path.Combine(dir, "clip_" + clip.Name + ".png"), ImageFormat.Png);
                }
            }
        }

        /// <summary>First frame of every clip, for a quick overall look.</summary>
        public static void WriteSheet(string dir, Atlas atlas, int zoom, Func<AtlasClip, bool> include, string fileName)
        {
            const int gap = 8, label = 16;
            var flat = new FrameFlattener(atlas);
            var clips = new List<AtlasClip>();
            foreach (AtlasClip c in atlas.ClipList) if (include(c)) clips.Add(c);
            int n = clips.Count;
            if (n == 0) return;
            int cw = atlas.CanvasWidth * zoom, ch = atlas.CanvasHeight * zoom;
            int cols = Math.Min(n, 8), rows = (n + cols - 1) / cols;
            int W = cols * (cw + gap) + gap, H = rows * (ch + label + gap) + gap;
            var img = NewImage(W, H);
            for (int i = 0; i < n; i++)
            {
                int cx = gap + (i % cols) * (cw + gap), cy = gap + (i / cols) * (ch + label + gap);
                Checker(img, W, cx, cy, cw, ch, zoom * 4, LightA, LightB);
                Blit(img, W, atlas, flat.Flatten(clips[i].Frames[0]), cx, cy, zoom);
            }
            using (Bitmap bmp = ToBitmap(img, W, H))
            using (Graphics g = Graphics.FromImage(bmp))
            using (var font = new Font("Consolas", 8.5f))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                for (int i = 0; i < n; i++)
                {
                    int cx = gap + (i % cols) * (cw + gap), cy = gap + (i / cols) * (ch + label + gap);
                    g.DrawString(clips[i].Name, font, Brushes.Black, cx, cy + ch + 1);
                }
                bmp.Save(Path.Combine(dir, fileName), ImageFormat.Png);
            }
        }

        /// <summary>A single part, big, with a pixel grid, its origin (red) and anchors (blue).</summary>
        public static void WritePart(string dir, Part part, Palette pal, int zoom)
        {
            int pad = zoom * 2;
            int W = part.Width * zoom + pad * 2, H = part.Height * zoom + pad * 2 + 20;
            var img = NewImage(W, H);
            Checker(img, W, pad, pad, part.Width * zoom, part.Height * zoom, zoom, LightA, LightB);
            for (int y = 0; y < part.Height; y++)
                for (int x = 0; x < part.Width; x++)
                {
                    int c = part.Pixels[y * part.Width + x];
                    if (c != 0) Fill(img, W, pad + x * zoom, pad + y * zoom, zoom, zoom, (int)pal.Colors[c]);
                }
            using (Bitmap bmp = ToBitmap(img, W, H))
            using (Graphics g = Graphics.FromImage(bmp))
            using (var grid = new Pen(Color.FromArgb(40, 0, 0, 0)))
            using (var font = new Font("Consolas", 9f))
            {
                for (int x = 0; x <= part.Width; x++) g.DrawLine(grid, pad + x * zoom, pad, pad + x * zoom, pad + part.Height * zoom);
                for (int y = 0; y <= part.Height; y++) g.DrawLine(grid, pad, pad + y * zoom, pad + part.Width * zoom, pad + y * zoom);
                using (var red = new Pen(Color.Red, 2))
                    g.DrawRectangle(red, pad + part.OriginX * zoom, pad + part.OriginY * zoom, zoom, zoom);
                using (var blue = new Pen(Color.RoyalBlue, 2))
                {
                    foreach (var a in part.Anchors)
                    {
                        g.DrawRectangle(blue, pad + a.Value.X * zoom + 2, pad + a.Value.Y * zoom + 2, zoom - 4, zoom - 4);
                        g.DrawString(a.Key, font, Brushes.RoyalBlue, pad + a.Value.X * zoom + zoom, pad + a.Value.Y * zoom);
                    }
                }
                g.DrawString(string.Format("{0}  {1}x{2}", part.Name, part.Width, part.Height), font, Brushes.Black, pad, H - 18);
                bmp.Save(Path.Combine(dir, "part_" + part.Name + ".png"), ImageFormat.Png);
            }
        }

        internal static int[] NewImage(int w, int h)
        {
            var img = new int[w * h];
            for (int i = 0; i < img.Length; i++) img[i] = Paper;
            return img;
        }

        static void Checker(int[] img, int W, int x0, int y0, int w, int h, int cell, int a, int b)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    img[(y0 + y) * W + x0 + x] = (((x / cell) + (y / cell)) & 1) == 0 ? a : b;
        }

        static void Blit(int[] img, int W, Atlas atlas, byte[] canvas, int x0, int y0, int zoom)
        {
            int cw = atlas.CanvasWidth, ch = atlas.CanvasHeight;
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                {
                    int c = canvas[y * cw + x];
                    if (c != 0) Fill(img, W, x0 + x * zoom, y0 + y * zoom, zoom, zoom, (int)atlas.Palette[c]);
                }
        }

        static void Fill(int[] img, int W, int x0, int y0, int w, int h, int color)
        {
            for (int y = y0; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++)
                    img[y * W + x] = color;
        }

        internal static Bitmap ToBitmap(int[] img, int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++) Marshal.Copy(img, y * w, data.Scan0 + y * data.Stride, w);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return bmp;
        }
    }

    /// <summary>
    /// Animated GIFs of every clip, with the real per-frame timing, for reviewing motion
    /// outside the app. GIF indices = atlas palette indices (0 becomes the background).
    /// </summary>
    static class GifWriter
    {
        public static void WriteClips(string dir, Atlas atlas, int zoom, uint background)
        {
            var flat = new FrameFlattener(atlas);
            foreach (AtlasClip clip in atlas.ClipList)
            {
                if (clip.Count < 2) continue;
                var frames = new List<byte[]>();
                var delays = new List<int>();
                for (int i = 0; i < clip.Count; i++)
                {
                    frames.Add(Zoom(flat.Flatten(clip.Frames[i]), atlas.CanvasWidth, atlas.CanvasHeight, zoom));
                    delays.Add(Math.Max(2, (clip.Durations[i] + 5) / 10));
                }
                Write(Path.Combine(dir, "gif_" + clip.Name + ".gif"), atlas.CanvasWidth * zoom, atlas.CanvasHeight * zoom,
                    atlas.Palette, background, frames, delays);
            }
        }

        static byte[] Zoom(byte[] src, int w, int h, int z)
        {
            var dst = new byte[w * z * h * z];
            int W = w * z;
            for (int y = 0; y < h * z; y++)
                for (int x = 0; x < W; x++)
                    dst[y * W + x] = src[(y / z) * w + x / z];
            return dst;
        }

        static void Write(string path, int w, int h, uint[] palette, uint background, List<byte[]> frames, List<int> delays)
        {
            using (var fs = File.Create(path))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(Encoding.ASCII.GetBytes("GIF89a"));
                bw.Write((ushort)w);
                bw.Write((ushort)h);
                bw.Write((byte)0xF7); // global colour table, 256 entries
                bw.Write((byte)0);
                bw.Write((byte)0);
                for (int i = 0; i < 256; i++)
                {
                    uint c = i == 0 ? background : (i < palette.Length ? palette[i] : 0);
                    bw.Write((byte)((c >> 16) & 255));
                    bw.Write((byte)((c >> 8) & 255));
                    bw.Write((byte)(c & 255));
                }
                bw.Write(new byte[] { 0x21, 0xFF, 0x0B });
                bw.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                bw.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 }); // loop forever
                for (int f = 0; f < frames.Count; f++)
                {
                    bw.Write(new byte[] { 0x21, 0xF9, 0x04, 0x00 });
                    bw.Write((ushort)delays[f]);
                    bw.Write((byte)0);
                    bw.Write((byte)0);
                    bw.Write((byte)0x2C);
                    bw.Write((ushort)0); bw.Write((ushort)0);
                    bw.Write((ushort)w); bw.Write((ushort)h);
                    bw.Write((byte)0);
                    Lzw(bw, frames[f]);
                }
                bw.Write((byte)0x3B);
            }
        }

        /// <summary>GIF LZW with 8-bit minimum code size (same scheme as the well-known gif.h).</summary>
        static void Lzw(BinaryWriter bw, byte[] pixels)
        {
            const int minCodeSize = 8, clearCode = 1 << minCodeSize, maxCodeLimit = 4095;
            bw.Write((byte)minCodeSize);
            var bits = new BitPacker(bw);
            var dict = new Dictionary<int, int>();
            int codeSize = minCodeSize + 1, maxCode = clearCode + 1;
            bits.Write(clearCode, codeSize);
            int cur = -1;
            foreach (byte v in pixels)
            {
                if (cur < 0) { cur = v; continue; }
                int next;
                if (dict.TryGetValue(cur * 256 + v, out next)) { cur = next; continue; }
                bits.Write(cur, codeSize);
                dict[cur * 256 + v] = ++maxCode;
                if (maxCode >= (1 << codeSize)) codeSize++;
                if (maxCode == maxCodeLimit)
                {
                    bits.Write(clearCode, codeSize);
                    dict.Clear();
                    codeSize = minCodeSize + 1;
                    maxCode = clearCode + 1;
                }
                cur = v;
            }
            bits.Write(cur, codeSize);
            bits.Write(clearCode, codeSize);
            bits.Write(clearCode + 1, minCodeSize + 1);
            bits.Flush();
            bw.Write((byte)0);
        }

        sealed class BitPacker
        {
            readonly BinaryWriter bw;
            readonly byte[] block = new byte[255];
            int blockLen;
            int acc, accBits;

            public BitPacker(BinaryWriter bw) { this.bw = bw; }

            public void Write(int code, int size)
            {
                acc |= code << accBits;
                accBits += size;
                while (accBits >= 8)
                {
                    Emit((byte)(acc & 255));
                    acc >>= 8;
                    accBits -= 8;
                }
            }

            void Emit(byte b)
            {
                block[blockLen++] = b;
                if (blockLen == 255) FlushBlock();
            }

            void FlushBlock()
            {
                if (blockLen == 0) return;
                bw.Write((byte)blockLen);
                bw.Write(block, 0, blockLen);
                blockLen = 0;
            }

            public void Flush()
            {
                if (accBits > 0) Emit((byte)(acc & 255));
                acc = 0;
                accBits = 0;
                FlushBlock();
            }
        }
    }

    /// <summary>
    /// Writes a multi-size .ico: 16/32/48 as classic 32-bit DIBs (safe for WinForms' Icon
    /// class) and 256 as PNG. Sizes use integer nearest-neighbour scaling to stay crisp.
    /// </summary>
    static class IconWriter
    {
        public static void Write(string path, Part part, Palette pal)
        {
            int[] sizes = { 16, 32, 48, 256 };
            var images = new List<byte[]>();
            foreach (int s in sizes)
            {
                int[] px = Render(part, pal, s);
                images.Add(s == 256 ? Png(px, s) : Dib(px, s));
            }
            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0);
                w.Write((short)1);
                w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((short)1);
                    w.Write((short)32);
                    w.Write(images[i].Length);
                    w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] img in images) w.Write(img);
            }
        }

        static int[] Render(Part part, Palette pal, int size)
        {
            var px = new int[size * size];
            int k = Math.Max(1, size / Math.Max(part.Width, part.Height));
            int ox = (size - part.Width * k) / 2, oy = (size - part.Height * k) / 2;
            for (int y = 0; y < part.Height; y++)
                for (int x = 0; x < part.Width; x++)
                {
                    int c = part.Pixels[y * part.Width + x];
                    if (c == 0) continue;
                    for (int yy = 0; yy < k; yy++)
                        for (int xx = 0; xx < k; xx++)
                        {
                            int dx = ox + x * k + xx, dy = oy + y * k + yy;
                            if (dx >= 0 && dy >= 0 && dx < size && dy < size) px[dy * size + dx] = (int)pal.Colors[c];
                        }
                }
            return px;
        }

        static byte[] Dib(int[] px, int s)
        {
            int maskStride = ((s + 31) / 32) * 4;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(40); w.Write(s); w.Write(s * 2);
                w.Write((short)1); w.Write((short)32);
                w.Write(0); w.Write(s * s * 4 + maskStride * s);
                w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                for (int y = s - 1; y >= 0; y--)
                    for (int x = 0; x < s; x++) w.Write(px[y * s + x]); // BGRA little-endian
                for (int y = s - 1; y >= 0; y--)
                {
                    var row = new byte[maskStride];
                    for (int x = 0; x < s; x++)
                        if (((uint)px[y * s + x] >> 24) == 0) row[x >> 3] |= (byte)(0x80 >> (x & 7));
                    w.Write(row);
                }
                return ms.ToArray();
            }
        }

        static byte[] Png(int[] px, int s)
        {
            using (Bitmap b = Preview.ToBitmap(px, s, s))
            using (var ms = new MemoryStream())
            {
                b.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
    }
}
