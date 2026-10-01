using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Aly.ArtBuild
{
    sealed class ArtException : Exception
    {
        public ArtException(string message) : base(message) { }

        public ArtException(string file, int line, string message)
            : base(string.Format("{0}({1}): {2}", Path.GetFileName(file), line, message)) { }
    }

    // ------------------------------------------------------------------ palette

    /// <summary>
    /// art/palette.txt — one colour per line: key hex name [>outlineKey].
    /// '.' must be first and means transparent. ">X" picks the outline colour drawn next to
    /// this colour ("selective outline"); ">." means no outline.
    /// </summary>
    sealed class Palette
    {
        public const int NoOutline = -2;
        public const int DefaultOutline = -1;

        public readonly List<char> Keys = new List<char>();
        public readonly List<uint> Colors = new List<uint>();
        public readonly List<string> Names = new List<string>();
        public readonly List<int> OutlineOf = new List<int>();

        public int Count { get { return Keys.Count; } }

        public int IndexOf(char key) { return Keys.IndexOf(key); }

        public uint[] ToArgb() { return Colors.ToArray(); }

        public static Palette Load(string path)
        {
            var p = new Palette();
            var pendingOutline = new List<KeyValuePair<int, char>>();
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = StripComment(lines[i]);
                if (line.Length == 0) continue;
                string[] t = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (t.Length < 3) throw new ArtException(path, i + 1, "expected: key hex name [>outline]");
                if (t[0].Length != 1) throw new ArtException(path, i + 1, "palette key must be one character");
                char key = t[0][0];
                if (key == '#' || char.IsWhiteSpace(key)) throw new ArtException(path, i + 1, "invalid key");
                if (p.Keys.Contains(key)) throw new ArtException(path, i + 1, "duplicate key '" + key + "'");
                uint color;
                if (p.Count == 0)
                {
                    if (key != '.' || t[1] != "-") throw new ArtException(path, i + 1, "first entry must be: . - transparent");
                    color = 0;
                }
                else
                {
                    string hex = t[1];
                    if (hex.Length == 6) hex = "FF" + hex;
                    if (hex.Length != 8 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color))
                        throw new ArtException(path, i + 1, "bad colour " + t[1]);
                }
                int index = p.Count;
                p.Keys.Add(key);
                p.Colors.Add(color);
                p.Names.Add(t[2]);
                p.OutlineOf.Add(DefaultOutline);
                if (t.Length > 3)
                {
                    if (t[3].Length != 2 || t[3][0] != '>') throw new ArtException(path, i + 1, "outline must look like >K");
                    pendingOutline.Add(new KeyValuePair<int, char>(index, t[3][1]));
                }
            }
            foreach (var kv in pendingOutline)
            {
                if (kv.Value == '.') { p.OutlineOf[kv.Key] = NoOutline; continue; }
                int o = p.IndexOf(kv.Value);
                if (o <= 0) throw new ArtException("palette: unknown outline key '" + kv.Value + "'");
                p.OutlineOf[kv.Key] = o;
            }
            if (p.Count > 256) throw new ArtException("palette has more than 256 colours");
            return p;
        }

        internal static string StripComment(string line)
        {
            int h = line.IndexOf('#');
            if (h >= 0) line = line.Substring(0, h);
            return line.Trim();
        }

        public double Luma(int index)
        {
            uint c = Colors[index];
            return 0.299 * ((c >> 16) & 255) + 0.587 * ((c >> 8) & 255) + 0.114 * (c & 255);
        }
    }

    // ------------------------------------------------------------------ parts

    sealed class Part
    {
        public string Name;
        public int Width;
        public int Height;
        public byte[] Pixels;
        public int OriginX;
        public int OriginY;
        public readonly Dictionary<string, Point> Anchors = new Dictionary<string, Point>();
        /// <summary>Part drawn (unflipped) instead of flipping this one in mirrored clips.</summary>
        public string MirrorName;
        public string File;
        public int Line;
        Part flipped;

        public byte At(int x, int y) { return Pixels[y * Width + x]; }

        public Part Flipped()
        {
            if (flipped != null) return flipped;
            var f = new Part();
            f.Name = Name + "(flipped)";
            f.Width = Width;
            f.Height = Height;
            f.Pixels = new byte[Pixels.Length];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    f.Pixels[y * Width + (Width - 1 - x)] = Pixels[y * Width + x];
            f.OriginX = Width - 1 - OriginX;
            f.OriginY = OriginY;
            foreach (var kv in Anchors) f.Anchors[kv.Key] = new Point(Width - 1 - kv.Value.X, kv.Value.Y);
            f.File = File;
            f.Line = Line;
            flipped = f;
            return f;
        }
    }

    /// <summary>
    /// art/parts/*.txt — blocks of:
    ///   part name [origin=x,y] [mirror=otherPart] [anchorName=x,y ...] [sym=N] [from=base [flip]]
    ///   ..grid rows of palette keys..
    ///   end
    /// sym=N   rows hold only columns 0..N (N = axis); the right half is mirrored in.
    /// from=b  start from part b (its origin and anchors too); flip mirrors it first.
    ///         Rows are then a patch: '_' keeps the base pixel, '.' erases, anything else paints.
    /// Origin defaults to the bottom-centre pixel.
    /// </summary>
    sealed class PartLibrary
    {
        const char Keep = '_';

        sealed class RawPart
        {
            public string Name;
            public string File;
            public int Line;
            public readonly List<KeyValuePair<string, string>> Attrs = new List<KeyValuePair<string, string>>();
            public readonly List<string> Rows = new List<string>();
            public bool Flip;
            public string From;
            public int Sym = -1;
        }

        readonly Dictionary<string, Part> parts = new Dictionary<string, Part>(StringComparer.Ordinal);
        readonly Dictionary<string, RawPart> raw = new Dictionary<string, RawPart>(StringComparer.Ordinal);
        readonly HashSet<string> resolving = new HashSet<string>(StringComparer.Ordinal);

        public IEnumerable<Part> All { get { return parts.Values; } }
        public int Count { get { return parts.Count; } }
        public bool Has(string name) { return parts.ContainsKey(name); }

        public Part Get(string name)
        {
            Part p;
            if (!parts.TryGetValue(name, out p)) throw new ArtException("unknown part '" + name + "'");
            return p;
        }

        public static PartLibrary Load(string dir, Palette palette)
        {
            var lib = new PartLibrary();
            if (!Directory.Exists(dir)) throw new ArtException("missing folder " + dir);
            foreach (string file in Directory.GetFiles(dir, "*.txt", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                lib.ReadFile(file);
            foreach (string name in lib.raw.Keys.ToList()) lib.Resolve(name, palette);
            foreach (Part p in lib.parts.Values)
            {
                if (p.MirrorName != null && !lib.Has(p.MirrorName))
                    throw new ArtException(p.File, p.Line, "mirror part '" + p.MirrorName + "' does not exist");
            }
            return lib;
        }

        void ReadFile(string file)
        {
            string[] lines = System.IO.File.ReadAllLines(file);
            int i = 0;
            while (i < lines.Length)
            {
                string header = Palette.StripComment(lines[i]);
                i++;
                if (header.Length == 0) continue;
                string[] t = header.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (t[0] != "part" || t.Length < 2) throw new ArtException(file, i, "expected 'part <name> ...'");
                var r = new RawPart();
                r.Name = t[1];
                r.File = file;
                r.Line = i;
                if (raw.ContainsKey(r.Name)) throw new ArtException(file, i, "duplicate part " + r.Name);
                for (int k = 2; k < t.Length; k++)
                {
                    if (t[k] == "flip") { r.Flip = true; continue; }
                    int eq = t[k].IndexOf('=');
                    if (eq <= 0) throw new ArtException(file, i, "bad attribute " + t[k]);
                    string key = t[k].Substring(0, eq), value = t[k].Substring(eq + 1);
                    if (key == "from") r.From = value;
                    else if (key == "sym")
                    {
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out r.Sym) || r.Sym < 0)
                            throw new ArtException(file, i, "sym needs the axis column");
                    }
                    else r.Attrs.Add(new KeyValuePair<string, string>(key, value));
                }
                if (r.Flip && r.From == null) throw new ArtException(file, i, "'flip' needs from=");
                while (true)
                {
                    if (i >= lines.Length) throw new ArtException(file, r.Line, "part " + r.Name + " has no 'end'");
                    string row = lines[i].TrimEnd();
                    i++;
                    if (row.Trim() == "end") break;
                    r.Rows.Add(row);
                }
                if (r.Rows.Count == 0 && r.From == null) throw new ArtException(file, r.Line, "part " + r.Name + " is empty");
                raw.Add(r.Name, r);
            }
        }

        Part Resolve(string name, Palette palette)
        {
            Part done;
            if (parts.TryGetValue(name, out done)) return done;
            RawPart r;
            if (!raw.TryGetValue(name, out r)) throw new ArtException("unknown part '" + name + "'");
            if (!resolving.Add(name)) throw new ArtException(r.File, r.Line, "part " + name + " derives from itself");

            List<string> rows = r.Rows;
            char pad = r.From != null ? Keep : '.';
            if (r.Sym >= 0) rows = rows.Select((row, k) => ExpandSym(row, r.Sym, pad, r, k)).ToList();

            var p = new Part();
            p.Name = r.Name;
            p.File = r.File;
            p.Line = r.Line;
            bool inherited = false;
            if (r.From != null)
            {
                Part b = Resolve(r.From, palette);
                if (r.Flip) b = b.Flipped();
                p.Width = b.Width;
                p.Height = b.Height;
                p.Pixels = (byte[])b.Pixels.Clone();
                p.OriginX = b.OriginX;
                p.OriginY = b.OriginY;
                foreach (var kv in b.Anchors) p.Anchors[kv.Key] = kv.Value;
                inherited = true;
                if (rows.Count > p.Height || rows.Any(x => x.Length > p.Width))
                    throw new ArtException(r.File, r.Line, "patch for " + name + " is larger than its base " + r.From);
            }
            else
            {
                p.Width = rows.Max(x => x.Length);
                p.Height = rows.Count;
                p.Pixels = new byte[p.Width * p.Height];
            }

            for (int y = 0; y < rows.Count; y++)
            {
                for (int x = 0; x < rows[y].Length; x++)
                {
                    char ch = rows[y][x];
                    if (ch == Keep)
                    {
                        if (!inherited) throw new ArtException(r.File, r.Line + y + 1, "'_' only works in a from= patch");
                        continue;
                    }
                    int idx = palette.IndexOf(ch);
                    if (idx < 0)
                        throw new ArtException(r.File, r.Line + y + 1, string.Format("unknown palette key '{0}' at column {1}", ch, x + 1));
                    p.Pixels[y * p.Width + x] = (byte)idx;
                }
            }

            bool originSet = inherited;
            foreach (var kv in r.Attrs)
            {
                if (kv.Key == "mirror") { p.MirrorName = kv.Value; continue; }
                Point pt = ParsePoint(kv.Value, r.File, r.Line);
                if (kv.Key == "origin") { p.OriginX = pt.X; p.OriginY = pt.Y; originSet = true; }
                else p.Anchors[kv.Key] = pt;
            }
            if (!originSet)
            {
                p.OriginX = p.Width / 2;
                p.OriginY = p.Height - 1;
            }
            resolving.Remove(name);
            parts.Add(name, p);
            return p;
        }

        static string ExpandSym(string row, int axis, char pad, RawPart r, int index)
        {
            if (row.Length > axis + 1)
                throw new ArtException(r.File, r.Line + index + 1, "row is wider than the sym axis (" + (axis + 1) + " columns)");
            string left = row.PadRight(axis + 1, pad);
            var full = new char[axis * 2 + 1];
            for (int i = 0; i <= axis; i++) full[i] = left[i];
            for (int i = 0; i < axis; i++) full[axis * 2 - i] = left[i];
            return new string(full);
        }

        internal static Point ParsePoint(string s, string file, int line)
        {
            string[] xy = s.Split(',');
            int x, y;
            if (xy.Length != 2 || !int.TryParse(xy[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x)
                || !int.TryParse(xy[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y))
                throw new ArtException(file, line, "expected x,y but got '" + s + "'");
            return new Point(x, y);
        }
    }

    // ------------------------------------------------------------------ clips

    sealed class LayerSpec
    {
        public string Part;
        /// <summary>null = position is relative to the canvas origin.</summary>
        public string AnchorLayer;
        public string AnchorName;
        public int X;
        public int Y;

        public LayerSpec Clone() { return (LayerSpec)MemberwiseClone(); }
    }

    sealed class FrameSpec
    {
        public int Duration;
        public readonly Dictionary<string, LayerSpec> Layers = new Dictionary<string, LayerSpec>(StringComparer.Ordinal);
        public int Line;
    }

    sealed class ClipDef
    {
        public string Name;
        public bool Loop;
        public string MirrorOf;
        public readonly List<FrameSpec> Frames = new List<FrameSpec>();
        public int Line;
    }

    /// <summary>
    /// art/clips.txt:
    ///   canvas 80 80 / origin 40 77 / outline K / layers a b c | d e   (header)
    ///   clip name [loop] [mirror=otherClip]
    ///     f ms [~] layer=part[@pos] layer=@pos layer=- ...
    /// pos is either dx,dy (from the canvas origin) or layer.anchor[:dx,dy].
    /// '~' copies the previous frame's layers so only changes need writing.
    /// Layers left of '|' get the automatic outline, layers right of it are drawn after.
    /// </summary>
    sealed class ClipSpec
    {
        public int CanvasW = 80, CanvasH = 80, OriginX = 40, OriginY = 77;
        public char OutlineKey = 'K';
        public readonly List<string> PreLayers = new List<string>();
        public readonly List<string> PostLayers = new List<string>();
        public readonly List<ClipDef> Clips = new List<ClipDef>();
        /// <summary>Named layer sets ("pose name tokens...") pasted into frames with +name.</summary>
        public readonly Dictionary<string, FrameSpec> Poses = new Dictionary<string, FrameSpec>(StringComparer.Ordinal);
        public string File;

        public ClipDef Find(string name) { return Clips.FirstOrDefault(c => c.Name == name); }

        public bool IsLayer(string name) { return PreLayers.Contains(name) || PostLayers.Contains(name); }

        public static ClipSpec Load(string path, PartLibrary parts)
        {
            var s = new ClipSpec();
            s.File = path;
            ClipDef current = null;
            string[] lines = System.IO.File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = Palette.StripComment(lines[i]);
                if (line.Length == 0) continue;
                string[] t = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                int ln = i + 1;
                switch (t[0])
                {
                    case "canvas":
                        s.CanvasW = Int(t, 1, path, ln); s.CanvasH = Int(t, 2, path, ln);
                        break;
                    case "origin":
                        s.OriginX = Int(t, 1, path, ln); s.OriginY = Int(t, 2, path, ln);
                        break;
                    case "outline":
                        if (t.Length != 2 || t[1].Length != 1) throw new ArtException(path, ln, "outline <key>");
                        s.OutlineKey = t[1][0];
                        break;
                    case "layers":
                        bool post = false;
                        for (int k = 1; k < t.Length; k++)
                        {
                            if (t[k] == "|") { post = true; continue; }
                            (post ? s.PostLayers : s.PreLayers).Add(t[k]);
                        }
                        break;
                    case "clip":
                        if (t.Length < 2) throw new ArtException(path, ln, "clip <name> [loop] [mirror=clip]");
                        if (s.Find(t[1]) != null) throw new ArtException(path, ln, "duplicate clip " + t[1]);
                        current = new ClipDef();
                        current.Name = t[1];
                        current.Line = ln;
                        for (int k = 2; k < t.Length; k++)
                        {
                            if (t[k] == "loop") current.Loop = true;
                            else if (t[k].StartsWith("mirror=")) current.MirrorOf = t[k].Substring(7);
                            else throw new ArtException(path, ln, "unknown clip option " + t[k]);
                        }
                        s.Clips.Add(current);
                        break;
                    case "f":
                        if (current == null) throw new ArtException(path, ln, "frame outside a clip");
                        if (current.MirrorOf != null) throw new ArtException(path, ln, "mirrored clips take their frames from the source");
                        current.Frames.Add(ParseFrame(s, current, t, path, ln));
                        break;
                    case "pose":
                        if (t.Length < 3) throw new ArtException(path, ln, "pose <name> tokens...");
                        if (s.Poses.ContainsKey(t[1])) throw new ArtException(path, ln, "duplicate pose " + t[1]);
                        var pose = new FrameSpec();
                        pose.Line = ln;
                        ParseTokens(s, null, t, 2, pose, path, ln);
                        s.Poses.Add(t[1], pose);
                        break;
                    default:
                        throw new ArtException(path, ln, "unknown directive " + t[0]);
                }
            }
            s.Validate(parts);
            return s;
        }

        static FrameSpec ParseFrame(ClipSpec s, ClipDef clip, string[] t, string path, int ln)
        {
            var f = new FrameSpec();
            f.Line = ln;
            f.Duration = Int(t, 1, path, ln);
            if (f.Duration < 1 || f.Duration > 30000) throw new ArtException(path, ln, "duration must be 1..30000 ms");
            ParseTokens(s, clip, t, 2, f, path, ln);
            return f;
        }

        static void ParseTokens(ClipSpec s, ClipDef clip, string[] t, int start, FrameSpec f, string path, int ln)
        {
            for (int k = start; k < t.Length; k++)
            {
                string tok = t[k];
                if (tok == "~")
                {
                    if (clip == null || clip.Frames.Count == 0) throw new ArtException(path, ln, "'~' needs a previous frame");
                    foreach (var kv in clip.Frames[clip.Frames.Count - 1].Layers) f.Layers[kv.Key] = kv.Value.Clone();
                    continue;
                }
                if (tok.StartsWith("+"))
                {
                    FrameSpec pose;
                    if (!s.Poses.TryGetValue(tok.Substring(1), out pose)) throw new ArtException(path, ln, "unknown pose " + tok);
                    foreach (var kv in pose.Layers) f.Layers[kv.Key] = kv.Value.Clone();
                    continue;
                }
                int eq = tok.IndexOf('=');
                if (eq <= 0) throw new ArtException(path, ln, "bad token " + tok);
                string layer = tok.Substring(0, eq), value = tok.Substring(eq + 1);
                if (!s.IsLayer(layer)) throw new ArtException(path, ln, "unknown layer " + layer);
                if (value == "-") { f.Layers.Remove(layer); continue; }

                LayerSpec spec;
                int at = value.IndexOf('@');
                string partName = at < 0 ? value : value.Substring(0, at);
                if (partName.Length == 0)
                {
                    if (!f.Layers.TryGetValue(layer, out spec)) throw new ArtException(path, ln, "'" + tok + "' has no part to reposition");
                    spec = spec.Clone();
                }
                else
                {
                    spec = new LayerSpec();
                    spec.Part = partName;
                }
                spec.AnchorLayer = null;
                spec.AnchorName = null;
                spec.X = 0;
                spec.Y = 0;
                if (at >= 0)
                {
                    string pos = value.Substring(at + 1);
                    if (pos.Length > 0 && (char.IsLetter(pos[0]) || pos[0] == '_'))
                    {
                        string anchor = pos, offset = null;
                        int colon = pos.IndexOf(':');
                        if (colon >= 0) { anchor = pos.Substring(0, colon); offset = pos.Substring(colon + 1); }
                        int dot = anchor.IndexOf('.');
                        if (dot <= 0) throw new ArtException(path, ln, "anchor must be layer.name in " + tok);
                        spec.AnchorLayer = anchor.Substring(0, dot);
                        spec.AnchorName = anchor.Substring(dot + 1);
                        if (offset != null)
                        {
                            Point o = PartLibrary.ParsePoint(offset, path, ln);
                            spec.X = o.X; spec.Y = o.Y;
                        }
                    }
                    else
                    {
                        Point o = PartLibrary.ParsePoint(pos, path, ln);
                        spec.X = o.X; spec.Y = o.Y;
                    }
                }
                f.Layers[layer] = spec;
            }
        }

        void Validate(PartLibrary parts)
        {
            if (PreLayers.Count == 0) throw new ArtException(File, 1, "missing 'layers' line");
            foreach (ClipDef c in Clips)
            {
                if (c.MirrorOf != null)
                {
                    ClipDef src = Find(c.MirrorOf);
                    if (src == null) throw new ArtException(File, c.Line, "mirror source '" + c.MirrorOf + "' not found");
                    if (src.MirrorOf != null) throw new ArtException(File, c.Line, "cannot mirror a mirrored clip");
                    continue;
                }
                if (c.Frames.Count == 0) throw new ArtException(File, c.Line, "clip " + c.Name + " has no frames");
                foreach (FrameSpec f in c.Frames)
                {
                    foreach (var kv in f.Layers)
                    {
                        if (!parts.Has(kv.Value.Part)) throw new ArtException(File, f.Line, "unknown part '" + kv.Value.Part + "'");
                        if (kv.Value.AnchorLayer == null) continue;
                        if (!IsLayer(kv.Value.AnchorLayer)) throw new ArtException(File, f.Line, "unknown anchor layer " + kv.Value.AnchorLayer);
                        if (!f.Layers.ContainsKey(kv.Value.AnchorLayer))
                            throw new ArtException(File, f.Line, kv.Key + " anchors to layer '" + kv.Value.AnchorLayer + "' which this frame does not use");
                        if (!parts.Get(f.Layers[kv.Value.AnchorLayer].Part).Anchors.ContainsKey(kv.Value.AnchorName))
                            throw new ArtException(File, f.Line, "part '" + f.Layers[kv.Value.AnchorLayer].Part + "' has no anchor '" + kv.Value.AnchorName + "'");
                    }
                }
            }
        }

        public int LayerOrder(string layer)
        {
            int i = PreLayers.IndexOf(layer);
            return i >= 0 ? i : PreLayers.Count + PostLayers.IndexOf(layer);
        }

        static int Int(string[] t, int i, string path, int ln)
        {
            int v;
            if (i >= t.Length || !int.TryParse(t[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                throw new ArtException(path, ln, "expected a number");
            return v;
        }
    }
}
