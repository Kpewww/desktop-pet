using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Aly.Core;

namespace Aly.App
{
    /// <summary>
    /// Developer check: (exe) --snapshot &lt;dir&gt; [scale] [look] [plain] renders every clip through
    /// the real renderer into PNGs, so rendering can be verified without looking at the screen.
    /// </summary>
    static class Snapshot
    {
        public static void Run(string dir, int scale, string look, bool plain, string outfit = null)
        {
            Directory.CreateDirectory(dir);
            Atlas atlas = PetApp.LoadAtlas(look, plain);
            var renderer = new Renderer(atlas);
            if (outfit != null)
            {
                // the same recolouring the app does for the wardrobe
                using (Stream s = Profile.Open("outfits.txt"))
                {
                    Outfit o = s == null ? null : Wardrobe.Parse(new StreamReader(s, System.Text.Encoding.UTF8)).Find(outfit);
                    if (o == null) throw new ArgumentException("no outfit " + outfit);
                    renderer.UsePalette(o.Apply(atlas.Palette));
                }
            }
            var eyeFrames = new EyeFrames(atlas);
            var eyes = new Aly.Core.Engine.Eyes(new Aly.Core.Engine.Rng(1), null, 1e9); // open, looking ahead
            int margin = scale;
            int w = atlas.CanvasWidth * scale + 2 * margin;
            int h = atlas.CanvasHeight * scale + 2 * margin;
            var buf = new int[w * h];
            long halo = 0, opaque = 0;
            foreach (AtlasClip clip in atlas.ClipList)
            {
                using (var sheet = new Bitmap(w * clip.Count, h * 2, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(sheet))
                {
                    g.Clear(Color.FromArgb(244, 241, 246));
                    using (var dark = new SolidBrush(Color.FromArgb(34, 30, 40))) g.FillRectangle(dark, 0, h, sheet.Width, h);
                    for (int i = 0; i < clip.Count; i++)
                    {
                        Array.Clear(buf, 0, buf.Length);
                        GCHandle pin = GCHandle.Alloc(buf, GCHandleType.Pinned);
                        try
                        {
                            renderer.DrawFrame(pin.AddrOfPinnedObject(), w, w, h, atlas.Frames[clip.Frames[i]], margin, margin, scale, true);
                            int eye, dx, dy;
                            eyeFrames.Resolve(clip.Frames[i], eyes, out eye, out dx, out dy);
                            if (eye >= 0)
                                renderer.DrawFrame(pin.AddrOfPinnedObject(), w, w, h, atlas.Frames[eye], margin + dx * scale, margin + dy * scale, scale, false);
                        }
                        finally
                        {
                            pin.Free();
                        }
                        foreach (int px in buf)
                        {
                            uint a = (uint)px >> 24;
                            if (a == 1) halo++;
                            else if (a == 255) opaque++;
                        }
                        using (var frame = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
                        {
                            BitmapData data = frame.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                            for (int y = 0; y < h; y++) Marshal.Copy(buf, y * w, data.Scan0 + y * data.Stride, w);
                            frame.UnlockBits(data);
                            g.DrawImageUnscaled(frame, i * w, 0);
                            g.DrawImageUnscaled(frame, i * w, h);
                        }
                    }
                    sheet.Save(Path.Combine(dir, "snap_" + clip.Name + ".png"), ImageFormat.Png);
                }
            }
            File.WriteAllText(Path.Combine(dir, "snap_stats.txt"),
                string.Format("clips {0}, opaque px {1}, halo px {2}, scale {3}\r\n", atlas.ClipList.Count, opaque, halo, scale));
            WriteBubbles(dir);
            WriteSigns(dir, atlas, renderer, eyeFrames, eyes, scale);
            WritePeeks(dir, atlas, renderer, eyeFrames, eyes, scale);
        }

        /// <summary>
        /// snap_peek.png: 边缘探头 as it looks on screen — sunk by the depth the engine measures, cut
        /// off at the edge, a grey bar standing in for the taskbar (and the side-edge peeks).
        /// </summary>
        static void WritePeeks(string dir, Atlas atlas, Renderer renderer, EyeFrames eyeFrames, Aly.Core.Engine.Eyes eyes, int scale)
        {
            var pet = new Aly.Core.Engine.Pet(atlas, new Config(), new Aly.Core.Engine.Rng(1));
            AtlasClip peek = atlas.GetClip("peek") ?? atlas.GetClip("idle");
            AtlasClip left = atlas.GetClip("peek_side_r"), right = atlas.GetClip("peek_side_l");
            int cw = atlas.CanvasWidth * scale, h = atlas.CanvasHeight * scale;
            int floor = atlas.OriginY * scale, edge = cw / 2;
            File.AppendAllText(Path.Combine(dir, "snap_stats.txt"), string.Format(CultureInfo.InvariantCulture,
                "peek depth {0}, hide depth {1}, side peek depth {2}, side hide depth {3}\r\n",
                pet.PeekDepth, pet.HideDepth, pet.SidePeekDepth, pet.SideHideDepth));
            var cells = new System.Collections.Generic.List<Bitmap>();
            // behind the taskbar: sunk by PeekDepth, cut at the feet line
            cells.Add(PeekCell(atlas, renderer, eyeFrames, eyes, scale, peek.Frames[0], 0, (int)Math.Round(pet.PeekDepth * scale), 0, 0, cw, floor));
            if (left != null && right != null)
            {
                // at a screen edge (the middle of the cell): his middle SidePeekDepth past it, and only the
                // part on the screen side of the edge shows
                int d = (int)Math.Round(pet.SidePeekDepth * scale);
                cells.Add(PeekCell(atlas, renderer, eyeFrames, eyes, scale, left.Frames[0], edge - d - atlas.OriginX * scale, 0, edge, 0, cw, h));
                cells.Add(PeekCell(atlas, renderer, eyeFrames, eyes, scale, right.Frames[0], edge + d - atlas.OriginX * scale, 0, 0, 0, edge, h));
            }
            using (var sheet = new Bitmap(cw * cells.Count, h))
            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.FromArgb(120, 150, 180));
                using (var dark = new SolidBrush(Color.FromArgb(34, 34, 42)))
                {
                    g.FillRectangle(dark, 0, floor, cw, h - floor);          // the taskbar
                    if (cells.Count > 1)
                    {
                        g.FillRectangle(dark, cw, 0, edge, h);               // off the left edge of the screen
                        g.FillRectangle(dark, 2 * cw + edge, 0, cw - edge, h); // off the right edge
                    }
                }
                for (int i = 0; i < cells.Count; i++)
                {
                    g.DrawImageUnscaled(cells[i], i * cw, 0);
                    cells[i].Dispose();
                }
                sheet.Save(Path.Combine(dir, "snap_peek.png"), ImageFormat.Png);
            }
        }

        /// <summary>One frame drawn at (x, y) with its eyes, then everything outside the visible rectangle cleared.</summary>
        static Bitmap PeekCell(Atlas atlas, Renderer renderer, EyeFrames eyeFrames, Aly.Core.Engine.Eyes eyes, int scale,
            int frame, int x, int y, int visLeft, int visTop, int visRight, int visBottom)
        {
            int w = atlas.CanvasWidth * scale, h = atlas.CanvasHeight * scale;
            var buf = new int[w * h];
            GCHandle pin = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                IntPtr p = pin.AddrOfPinnedObject();
                renderer.DrawFrame(p, w, w, h, atlas.Frames[frame], x, y, scale, false);
                int eye, ex, ey;
                eyeFrames.Resolve(frame, eyes, out eye, out ex, out ey);
                if (eye >= 0) renderer.DrawFrame(p, w, w, h, atlas.Frames[eye], x + ex * scale, y + ey * scale, scale, false);
                Renderer.ClearRect(p, w, w, h, 0, 0, w, visTop);
                Renderer.ClearRect(p, w, w, h, 0, visBottom, w, h);
                Renderer.ClearRect(p, w, w, h, 0, 0, visLeft, h);
                Renderer.ClearRect(p, w, w, h, visRight, 0, w, h);
            }
            finally
            {
                pin.Free();
            }
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            for (int r = 0; r < h; r++) Marshal.Copy(buf, r * w, data.Scan0 + r * data.Stride, w);
            bmp.UnlockBits(data);
            return bmp;
        }

        static PixelFont LoadFont()
        {
            using (Stream s = Profile.Open("pixel12.font"))
            {
                return s == null ? null : PixelFont.Load(s);
            }
        }

        /// <summary>Her holding up the sign with sample texts, plus the unfold frames, in snap_sign.png.</summary>
        static void WriteSigns(string dir, Atlas atlas, Renderer renderer, EyeFrames eyeFrames, Aly.Core.Engine.Eyes eyes, int scale)
        {
            PixelFont font = LoadFont();
            int anchor = atlas.AnchorIndex("prop.sign");
            AtlasClip hold = atlas.GetClip("sign_hold"), up = atlas.GetClip("sign_up");
            if (font == null || anchor < 0 || hold == null || up == null) return;
            int ui = Math.Max(1, scale / 2);
            var shots = new System.Collections.Generic.List<Tuple<int, string, double>>();
            string[] samples =
            {
                "你好", "气死我啦！", "abcdefghijklmnopqrstuvwxyz", "一二三四五六七八九十一二三四五六七八九十二十一",
                "图片 1920×1080", "（已隐藏）", "旅行照片.zip 等 3 个文件", "https://github.com/TakWolf/fusion-pixel-font",
                "好耶\U0001F600\U0001F44D\U0001F3FD \U0001F468‍\U0001F469‍\U0001F467 ok", "第一行\r\n第二行",
            };
            foreach (string t in samples) shots.Add(Tuple.Create(hold.Frames[0], t, 1.0));
            double[] unfold = { 0.34, 0.67, 1.0 };
            for (int i = 0; i < up.Count; i++) shots.Add(Tuple.Create(up.Frames[i], "你好呀", unfold[Math.Min(i, 2)]));
            // reminder notes: "text|button|button"
            shots.Add(Tuple.Create(hold.Frames[0], "note:该喝水啦～ 你也喝一口吧|喝了|稍后", 1.0));
            shots.Add(Tuple.Create(hold.Frames[0], "note:交作业（周五之前）|完成|推迟10分钟", 1.0));
            shots.Add(Tuple.Create(hold.Frames[0], "note:专注完成！今天第 3 个番茄|好", 1.0));
            shots.Add(Tuple.Create(hold.Frames[0], "note:都 00:30 了！还不睡？|去睡了|再玩一会", 1.0));

            const int Cols = 5;
            int cellW = 0, cellH = 0;
            var boards = new System.Collections.Generic.List<SignBoard>();
            foreach (var s in shots)
            {
                var content = new Aly.Core.Engine.SignContent();
                content.Serial = boards.Count;
                if (s.Item2.StartsWith("note:"))
                {
                    string[] parts = s.Item2.Substring(5).Split('|');
                    content.Style = Aly.Core.Engine.SignStyle.Note;
                    content.Text = parts[0];
                    content.Buttons = new string[parts.Length - 1];
                    Array.Copy(parts, 1, content.Buttons, 0, content.Buttons.Length);
                }
                else content.Text = ClipItemText(s.Item2);
                var b = new SignBoard(font, content, scale, ui);
                boards.Add(b);
                cellW = Math.Max(cellW, Math.Max(atlas.CanvasWidth * scale, b.Width + 40 * scale));
                cellH = Math.Max(cellH, atlas.CanvasHeight * scale + b.Height);
            }
            cellW += 2 * scale;
            cellH += 4 * scale;
            int rows = (shots.Count + Cols - 1) / Cols;
            int w = cellW * Cols, h = cellH * rows;
            var buf = new int[w * h];
            GCHandle pin = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                IntPtr p = pin.AddrOfPinnedObject();
                for (int i = 0; i < shots.Count; i++)
                {
                    int frame = shots[i].Item1;
                    int canvasX = (i % Cols) * cellW + (cellW - atlas.CanvasWidth * scale) / 2 - 20 * scale;
                    int canvasY = (i / Cols) * cellH + cellH - atlas.CanvasHeight * scale - 2 * scale;
                    int ax, ay;
                    if (atlas.TryGetAnchor(frame, anchor, out ax, out ay))
                        boards[i].Draw(p, w, w, h, canvasX + (ax + 1) * scale, canvasY + ay * scale, shots[i].Item3, i == shots.Count - 3 ? 0 : -1);
                    renderer.DrawFrame(p, w, w, h, atlas.Frames[frame], canvasX, canvasY, scale, true);
                    int eye, dx, dy;
                    eyeFrames.Resolve(frame, eyes, out eye, out dx, out dy);
                    if (eye >= 0) renderer.DrawFrame(p, w, w, h, atlas.Frames[eye], canvasX + dx * scale, canvasY + dy * scale, scale, false);
                }
            }
            finally
            {
                pin.Free();
            }
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
            {
                BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                for (int y = 0; y < h; y++) Marshal.Copy(buf, y * w, data.Scan0 + y * data.Stride, w);
                bmp.UnlockBits(data);
                using (var sheet = new Bitmap(w, h))
                using (Graphics g = Graphics.FromImage(sheet))
                {
                    g.Clear(Color.FromArgb(58, 84, 110));
                    g.DrawImageUnscaled(bmp, 0, 0);
                    sheet.Save(Path.Combine(dir, "snap_sign.png"), ImageFormat.Png);
                }
            }
        }

        /// <summary>What the sign would say for this copied text.</summary>
        static string ClipItemText(string copied)
        {
            if (copied.StartsWith("图片 ") || copied.StartsWith("（") || copied.Contains(" 等 ")) return copied;
            return Aly.Core.ClipItem.FromText(copied, false).SignText;
        }

        /// <summary>Renders sample bubbles (normal and shout) with the embedded pixel font.</summary>
        static void WriteBubbles(string dir)
        {
            PixelFont font = LoadFont();
            if (font == null) return;
            string[] texts = { "嘿嘿～再摸摸～", "再不放我下来我咬你了！", "Hello " + Profile.Me.Nick + "! 你复制了：https://example.com/abc" };
            var bubbles = new System.Collections.Generic.List<Bubble>();
            int serial = 0;
            foreach (string t in texts)
            {
                var sp = new Aly.Core.Engine.Speech();
                sp.Text = t;
                sp.Serial = ++serial;
                bubbles.Add(new Bubble(font, sp, 2));
            }
            var shout = new Aly.Core.Engine.Speech();
            shout.Text = "气死我啦！";
            shout.Style = Aly.Core.Engine.SpeechStyle.Shout;
            shout.Serial = ++serial;
            bubbles.Add(new Bubble(font, shout, 2));

            int w = 20, h = 20;
            foreach (Bubble b in bubbles) { w = Math.Max(w, b.Width + 20); h += b.Height + 16; }
            var buf = new int[w * h];
            GCHandle pin = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                int y = 10;
                foreach (Bubble b in bubbles)
                {
                    b.Draw(pin.AddrOfPinnedObject(), w, w, h, 10, y, 10 + b.Width / 2);
                    y += b.Height + 16;
                }
            }
            finally
            {
                pin.Free();
            }
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
            {
                BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                for (int y = 0; y < h; y++) Marshal.Copy(buf, y * w, data.Scan0 + y * data.Stride, w);
                bmp.UnlockBits(data);
                using (var sheet = new Bitmap(w, h))
                using (Graphics g = Graphics.FromImage(sheet))
                {
                    g.Clear(Color.FromArgb(120, 140, 160));
                    g.DrawImageUnscaled(bmp, 0, 0);
                    sheet.Save(Path.Combine(dir, "snap_bubbles.png"), ImageFormat.Png);
                }
            }
        }
    }
}
