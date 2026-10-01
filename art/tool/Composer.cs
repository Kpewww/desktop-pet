using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using Aly.Core;

namespace Aly.ArtBuild
{
    sealed class BuildResult
    {
        public Atlas Atlas;
        public readonly List<string> Warnings = new List<string>();
        public int ComposedFrames;
    }

    /// <summary>A part placed on the canvas: (X, Y) is where its origin pixel lands.</summary>
    sealed class Placed
    {
        public Part Part;
        public int X;
        public int Y;
    }

    static class Composer
    {
        /// <param name="skipLayers">Layers left out of every frame (the variant without dog ears and tail); null = none.</param>
        /// <param name="swap">Parts drawn in place of others in this look (Aly's short hair); null = none.</param>
        public static BuildResult Build(ClipSpec spec, PartLibrary parts, Palette pal, ICollection<string> skipLayers = null,
            IDictionary<string, string> swap = null)
        {
            var res = new BuildResult();
            var atlas = new Atlas();
            atlas.CanvasWidth = spec.CanvasW;
            atlas.CanvasHeight = spec.CanvasH;
            atlas.OriginX = spec.OriginX;
            atlas.OriginY = spec.OriginY;
            atlas.Palette = pal.ToArgb();
            int outline = pal.IndexOf(spec.OutlineKey);
            if (outline <= 0) throw new ArtException("outline key '" + spec.OutlineKey + "' is not in the palette");

            var interned = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (ClipDef c in spec.Clips)
            {
                bool mirrored = c.MirrorOf != null;
                ClipDef src = mirrored ? spec.Find(c.MirrorOf) : c;
                var clip = new AtlasClip();
                clip.Name = c.Name;
                clip.Loop = src.Loop || c.Loop;
                clip.Frames = new int[src.Frames.Count];
                clip.Durations = new int[src.Frames.Count];
                for (int i = 0; i < src.Frames.Count; i++)
                {
                    string where = c.Name + " frame " + i;
                    AtlasFrame f = Compose(spec, parts, pal, atlas, src.Frames[i], mirrored, outline, where, res.Warnings, skipLayers, swap);
                    res.ComposedFrames++;
                    clip.Frames[i] = Intern(atlas, interned, f);
                    clip.Durations[i] = src.Frames[i].Duration;
                }
                atlas.AddClip(clip);
            }
            res.Atlas = atlas;
            return res;
        }

        static AtlasFrame Compose(ClipSpec spec, PartLibrary parts, Palette pal, Atlas atlas, FrameSpec fs,
            bool mirrored, int outline, string where, List<string> warnings, ICollection<string> skipLayers,
            IDictionary<string, string> swap)
        {
            int w = spec.CanvasW, h = spec.CanvasH;
            var canvas = new byte[w * h];
            var placed = new Dictionary<string, Placed>(StringComparer.Ordinal);
            var resolving = new HashSet<string>(StringComparer.Ordinal);
            foreach (string layer in fs.Layers.Keys) Resolve(layer, spec, parts, fs, mirrored, placed, resolving, where, swap);

            bool clipped = false;
            foreach (string layer in spec.PreLayers)
            {
                Placed p;
                if (placed.TryGetValue(layer, out p) && (skipLayers == null || !skipLayers.Contains(layer))) clipped |= Draw(canvas, w, h, p);
            }
            ApplyOutline(canvas, w, h, pal, outline);
            foreach (string layer in spec.PostLayers)
            {
                Placed p;
                if (placed.TryGetValue(layer, out p) && (skipLayers == null || !skipLayers.Contains(layer))) clipped |= Draw(canvas, w, h, p);
            }
            if (clipped) warnings.Add(where + ": pixels fall outside the canvas");

            var anchors = new List<AtlasAnchor>();
            foreach (var kv in placed)
            {
                foreach (var a in kv.Value.Part.Anchors)
                {
                    var aa = new AtlasAnchor();
                    aa.NameIndex = atlas.InternAnchor(kv.Key + "." + a.Key);
                    aa.X = kv.Value.X - kv.Value.Part.OriginX + a.Value.X;
                    aa.Y = kv.Value.Y - kv.Value.Part.OriginY + a.Value.Y;
                    anchors.Add(aa);
                }
            }
            if (atlas.AnchorNames.Count > 255) throw new ArtException("more than 255 distinct anchor names");
            return Trim(canvas, w, h, anchors.OrderBy(a => a.NameIndex).ToArray());
        }

        /// <summary>
        /// Placement follows anchor dependencies, independent of draw order — the back hair is
        /// drawn behind the body yet hangs from the head.
        /// </summary>
        static Placed Resolve(string layer, ClipSpec spec, PartLibrary parts, FrameSpec fs, bool mirrored,
            Dictionary<string, Placed> placed, HashSet<string> resolving, string where, IDictionary<string, string> swap)
        {
            Placed done;
            if (placed.TryGetValue(layer, out done)) return done;
            if (!resolving.Add(layer)) throw new ArtException(where + ": anchor cycle through layer " + layer);

            LayerSpec ls = fs.Layers[layer];
            string name;
            if (swap == null || !swap.TryGetValue(ls.Part, out name)) name = ls.Part;
            Part part = parts.Get(name);
            if (mirrored) part = part.MirrorName != null ? parts.Get(part.MirrorName) : part.Flipped();
            int dx = mirrored ? -ls.X : ls.X;
            int x, y;
            if (ls.AnchorLayer == null)
            {
                x = spec.OriginX + dx;
                y = spec.OriginY + ls.Y;
            }
            else
            {
                if (!fs.Layers.ContainsKey(ls.AnchorLayer))
                    throw new ArtException(where + ": " + layer + " anchors to unused layer " + ls.AnchorLayer);
                Placed b = Resolve(ls.AnchorLayer, spec, parts, fs, mirrored, placed, resolving, where, swap);
                Point a;
                if (!b.Part.Anchors.TryGetValue(ls.AnchorName, out a))
                    throw new ArtException(where + ": part '" + b.Part.Name + "' has no anchor '" + ls.AnchorName + "'");
                x = b.X - b.Part.OriginX + a.X + dx;
                y = b.Y - b.Part.OriginY + a.Y + ls.Y;
            }
            var p = new Placed();
            p.Part = part;
            p.X = x;
            p.Y = y;
            resolving.Remove(layer);
            placed[layer] = p;
            return p;
        }

        static bool Draw(byte[] canvas, int w, int h, Placed p)
        {
            bool clipped = false;
            Part part = p.Part;
            int left = p.X - part.OriginX, top = p.Y - part.OriginY;
            for (int y = 0; y < part.Height; y++)
            {
                for (int x = 0; x < part.Width; x++)
                {
                    byte c = part.Pixels[y * part.Width + x];
                    if (c == 0) continue;
                    int cx = left + x, cy = top + y;
                    if (cx < 0 || cy < 0 || cx >= w || cy >= h) { clipped = true; continue; }
                    canvas[cy * w + cx] = c;
                }
            }
            return clipped;
        }

        /// <summary>
        /// Outer outline: every transparent pixel touching the sprite (4-neighbourhood) gets the
        /// darkest outline colour requested by its neighbours.
        /// </summary>
        static void ApplyOutline(byte[] canvas, int w, int h, Palette pal, int defaultOutline)
        {
            byte[] src = (byte[])canvas.Clone();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (src[y * w + x] != 0) continue;
                    int best = -1;
                    double bestLuma = double.MaxValue;
                    Consider(src, w, h, x - 1, y, pal, defaultOutline, ref best, ref bestLuma);
                    Consider(src, w, h, x + 1, y, pal, defaultOutline, ref best, ref bestLuma);
                    Consider(src, w, h, x, y - 1, pal, defaultOutline, ref best, ref bestLuma);
                    Consider(src, w, h, x, y + 1, pal, defaultOutline, ref best, ref bestLuma);
                    if (best > 0) canvas[y * w + x] = (byte)best;
                }
            }
        }

        static void Consider(byte[] src, int w, int h, int x, int y, Palette pal, int def, ref int best, ref double bestLuma)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int c = src[y * w + x];
            if (c == 0) return;
            int o = pal.OutlineOf[c];
            if (o == Palette.NoOutline) return;
            if (o == Palette.DefaultOutline) o = def;
            double l = pal.Luma(o);
            if (l < bestLuma) { bestLuma = l; best = o; }
        }

        static AtlasFrame Trim(byte[] canvas, int w, int h, AtlasAnchor[] anchors)
        {
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (canvas[y * w + x] == 0) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            var f = new AtlasFrame();
            f.Anchors = anchors;
            if (maxX < 0) return f;
            f.OffsetX = minX;
            f.OffsetY = minY;
            f.Width = maxX - minX + 1;
            f.Height = maxY - minY + 1;
            f.Pixels = new byte[f.Width * f.Height];
            for (int y = 0; y < f.Height; y++)
                Buffer.BlockCopy(canvas, (minY + y) * w + minX, f.Pixels, y * f.Width, f.Width);
            return f;
        }

        static int Intern(Atlas atlas, Dictionary<string, int> interned, AtlasFrame f)
        {
            var sb = new StringBuilder();
            sb.Append(f.OffsetX).Append(',').Append(f.OffsetY).Append(',').Append(f.Width).Append(',').Append(f.Height).Append('|');
            sb.Append(Convert.ToBase64String(f.Pixels));
            foreach (AtlasAnchor a in f.Anchors) sb.Append('|').Append(a.NameIndex).Append(',').Append(a.X).Append(',').Append(a.Y);
            string key = sb.ToString();
            int idx;
            if (interned.TryGetValue(key, out idx)) return idx;
            atlas.Frames.Add(f);
            idx = atlas.Frames.Count - 1;
            interned[key] = idx;
            return idx;
        }
    }
}
