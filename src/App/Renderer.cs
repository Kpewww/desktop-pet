using System;
using Aly.Core;

namespace Aly.App
{
    /// <summary>
    /// Blits palette-indexed atlas frames into a premultiplied BGRA buffer at an integer
    /// scale. No GDI+, no GPU — a few tens of microseconds per frame.
    /// </summary>
    sealed unsafe class Renderer
    {
        /// <summary>Alpha 1: invisible, but still counts as "hit" for the layered window.</summary>
        const uint Halo = 0x01000000u;

        uint[] palette;

        public Renderer(Atlas atlas)
        {
            UsePalette(atlas.Palette);
        }

        /// <summary>Draws with these colours from now on (an outfit is just a different palette).</summary>
        public void UsePalette(uint[] argb)
        {
            var p = new uint[argb.Length];
            for (int i = 1; i < p.Length; i++) p[i] = Premultiply(argb[i]);
            palette = p;
        }

        static uint Premultiply(uint argb)
        {
            uint a = argb >> 24;
            if (a == 255) return argb;
            uint r = ((argb >> 16) & 255) * a / 255;
            uint g = ((argb >> 8) & 255) * a / 255;
            uint b = (argb & 255) * a / 255;
            return (a << 24) | (r << 16) | (g << 8) | b;
        }

        /// <summary>Makes [x0, x1) × [y0, y1) fully transparent (clamped to the buffer).</summary>
        public static void ClearRect(IntPtr bits, int stride, int w, int h, int x0, int y0, int x1, int y1)
        {
            if (x0 < 0) x0 = 0;
            if (y0 < 0) y0 = 0;
            if (x1 > w) x1 = w;
            if (y1 > h) y1 = h;
            uint* p = (uint*)bits;
            for (int y = y0; y < y1; y++)
            {
                uint* row = p + y * stride;
                for (int x = x0; x < x1; x++) row[x] = 0;
            }
        }

        public static void Clear(IntPtr bits, int stride, int w, int h)
        {
            uint* p = (uint*)bits;
            for (int y = 0; y < h; y++)
            {
                uint* row = p + y * stride;
                for (int x = 0; x < w; x++) row[x] = 0;
            }
        }

        /// <summary>
        /// Draws frame f with its canvas top-left at (canvasX, canvasY) in buffer pixels.
        /// With halo, transparent pixels touching the sprite get alpha 1 so thin strands
        /// are easy to grab.
        /// </summary>
        public void DrawFrame(IntPtr bits, int stride, int w, int h, AtlasFrame f,
            int canvasX, int canvasY, int scale, bool halo)
        {
            int fw = f.Width, fh = f.Height;
            if (fw == 0 || fh == 0) return;
            uint* dst = (uint*)bits;
            fixed (byte* src = f.Pixels)
            {
                int lo = halo ? -1 : 0;
                for (int sy = lo; sy < fh - lo; sy++)
                {
                    int dy = canvasY + (f.OffsetY + sy) * scale;
                    if (dy >= h || dy + scale <= 0) continue;
                    for (int sx = lo; sx < fw - lo; sx++)
                    {
                        int idx = (sx >= 0 && sy >= 0 && sx < fw && sy < fh) ? src[sy * fw + sx] : 0;
                        int dx = canvasX + (f.OffsetX + sx) * scale;
                        if (idx != 0)
                        {
                            Fill(dst, stride, w, h, dx, dy, scale, palette[idx], false);
                        }
                        else if (halo && Touches(src, fw, fh, sx, sy))
                        {
                            Fill(dst, stride, w, h, dx, dy, scale, Halo, true);
                        }
                    }
                }
            }
        }

        static bool Touches(byte* s, int fw, int fh, int x, int y)
        {
            return At(s, fw, fh, x - 1, y) || At(s, fw, fh, x + 1, y) || At(s, fw, fh, x, y - 1) || At(s, fw, fh, x, y + 1);
        }

        static bool At(byte* s, int fw, int fh, int x, int y)
        {
            return x >= 0 && y >= 0 && x < fw && y < fh && s[y * fw + x] != 0;
        }

        static void Fill(uint* dst, int stride, int w, int h, int x0, int y0, int n, uint c, bool onlyEmpty)
        {
            int x1 = x0 + n, y1 = y0 + n;
            if (x0 < 0) x0 = 0;
            if (y0 < 0) y0 = 0;
            if (x1 > w) x1 = w;
            if (y1 > h) y1 = h;
            for (int y = y0; y < y1; y++)
            {
                uint* row = dst + y * stride;
                for (int x = x0; x < x1; x++)
                {
                    if (onlyEmpty && row[x] != 0) continue;
                    row[x] = c;
                }
            }
        }
    }
}
