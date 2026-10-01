using System;
using System.Runtime.InteropServices;

namespace Aly.Core
{
    /// <summary>A copied image, as far as she cares: its size and a small preview.</summary>
    public sealed class DibInfo
    {
        public int Width, Height;
        /// <summary>Box-filtered preview, 0xAARRGGBB rows top-down; null if the pixel format isn't supported.</summary>
        public uint[] Thumb;
        public int ThumbWidth, ThumbHeight;
        /// <summary>Changes with the picture (size, byte count and preview pixels), for spotting repeats.</summary>
        public uint Hash;
    }

    /// <summary>
    /// Reads a device-independent bitmap (CF_DIB / CF_DIBV5: a BITMAPINFOHEADER or V5 header,
    /// optional masks and palette, then the pixel rows) straight from memory. Only a few
    /// samples per preview pixel are read; the full picture is never copied.
    /// </summary>
    public static class Dib
    {
        const int BI_RGB = 0, BI_BITFIELDS = 3;

        public static DibInfo Read(IntPtr p, long size, int maxThumbW, int maxThumbH)
        {
            if (p == IntPtr.Zero || size < 40) return null;
            int header = Marshal.ReadInt32(p, 0);
            int w = Marshal.ReadInt32(p, 4);
            int rawH = Marshal.ReadInt32(p, 8);
            int bpp = Marshal.ReadInt16(p, 14);
            int compression = Marshal.ReadInt32(p, 16);
            int colorsUsed = Marshal.ReadInt32(p, 32);
            int h = Math.Abs(rawH);
            if (header < 40 || header > size || w <= 0 || h <= 0) return null;

            var info = new DibInfo();
            info.Width = w;
            info.Height = h;
            info.Hash = 2166136261;
            Mix(ref info.Hash, (uint)w);
            Mix(ref info.Hash, (uint)h);
            Mix(ref info.Hash, (uint)size);

            long offset = header;
            if (compression == BI_BITFIELDS && header == 40) offset += 12; // the three colour masks
            if (bpp <= 8) offset += 4L * (colorsUsed != 0 ? colorsUsed : (1 << Math.Max(1, bpp)));
            long stride = ((w * (long)bpp + 31) / 32) * 4;
            bool readable = (bpp == 32 || bpp == 24) && (compression == BI_RGB || compression == BI_BITFIELDS)
                && offset + stride * h <= size;
            if (readable) Thumbnail(p, offset, stride, w, h, rawH > 0, bpp / 8, maxThumbW, maxThumbH, info);
            return info;
        }

        static void Thumbnail(IntPtr p, long offset, long stride, int w, int h, bool bottomUp, int bytesPerPixel,
            int maxW, int maxH, DibInfo info)
        {
            double k = Math.Min(1.0, Math.Min(maxW / (double)w, maxH / (double)h));
            int tw = Math.Max(1, (int)Math.Round(w * k)), th = Math.Max(1, (int)Math.Round(h * k));
            var thumb = new uint[tw * th];
            for (int ty = 0; ty < th; ty++)
            {
                long y0 = (long)ty * h / th, y1 = Math.Max(y0 + 1, (long)(ty + 1) * h / th);
                long sy = Math.Max(1, (y1 - y0) / 4);
                for (int tx = 0; tx < tw; tx++)
                {
                    long x0 = (long)tx * w / tw, x1 = Math.Max(x0 + 1, (long)(tx + 1) * w / tw);
                    long sx = Math.Max(1, (x1 - x0) / 4);
                    int r = 0, g = 0, b = 0, n = 0;
                    for (long y = y0; y < y1; y += sy)
                    {
                        long line = offset + (bottomUp ? h - 1 - y : y) * stride;
                        for (long x = x0; x < x1; x += sx)
                        {
                            IntPtr px = new IntPtr(p.ToInt64() + line + x * bytesPerPixel);
                            b += Marshal.ReadByte(px, 0);
                            g += Marshal.ReadByte(px, 1);
                            r += Marshal.ReadByte(px, 2);
                            n++;
                        }
                    }
                    uint c = 0xFF000000u | (uint)(r / n) << 16 | (uint)(g / n) << 8 | (uint)(b / n);
                    thumb[ty * tw + tx] = c;
                    Mix(ref info.Hash, c);
                }
            }
            info.Thumb = thumb;
            info.ThumbWidth = tw;
            info.ThumbHeight = th;
        }

        static void Mix(ref uint hash, uint v)
        {
            hash = (hash ^ v) * 16777619;
        }
    }
}
