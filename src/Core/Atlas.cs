using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Aly.Core
{
    public struct AtlasAnchor
    {
        public int NameIndex;
        public int X;
        public int Y;
    }

    public sealed class AtlasFrame
    {
        public int OffsetX;
        public int OffsetY;
        public int Width;
        public int Height;
        public byte[] Pixels = new byte[0];
        public AtlasAnchor[] Anchors = new AtlasAnchor[0];
    }

    public sealed class AtlasClip
    {
        public string Name;
        public bool Loop;
        public int[] Frames;
        public int[] Durations;

        public int Count { get { return Frames.Length; } }

        public int TotalDuration
        {
            get
            {
                int t = 0;
                for (int i = 0; i < Durations.Length; i++) t += Durations[i];
                return t;
            }
        }
    }

    /// <summary>
    /// Palette-indexed sprite atlas. Index 0 is always transparent.
    /// Frame pixels are trimmed; OffsetX/Y locate them on the shared canvas.
    /// </summary>
    public sealed class Atlas
    {
        public const int Magic = 0x41594C41; // "ALYA"
        public const int FormatVersion = 1;

        public int CanvasWidth;
        public int CanvasHeight;
        public int OriginX;
        public int OriginY;
        public uint[] Palette = new uint[] { 0 };
        public readonly List<string> AnchorNames = new List<string>();
        public readonly List<AtlasFrame> Frames = new List<AtlasFrame>();
        public readonly List<AtlasClip> ClipList = new List<AtlasClip>();
        readonly Dictionary<string, AtlasClip> clips = new Dictionary<string, AtlasClip>(StringComparer.Ordinal);

        public void AddClip(AtlasClip clip)
        {
            if (clips.ContainsKey(clip.Name)) throw new ArgumentException("duplicate clip " + clip.Name);
            clips.Add(clip.Name, clip);
            ClipList.Add(clip);
        }

        public AtlasClip GetClip(string name)
        {
            AtlasClip c;
            return name != null && clips.TryGetValue(name, out c) ? c : null;
        }

        public bool HasClip(string name) { return name != null && clips.ContainsKey(name); }

        public int AnchorIndex(string name) { return AnchorNames.IndexOf(name); }

        public int InternAnchor(string name)
        {
            int i = AnchorNames.IndexOf(name);
            if (i >= 0) return i;
            AnchorNames.Add(name);
            return AnchorNames.Count - 1;
        }

        public bool TryGetAnchor(int frameIndex, int nameIndex, out int x, out int y)
        {
            x = 0; y = 0;
            if (frameIndex < 0 || frameIndex >= Frames.Count || nameIndex < 0) return false;
            AtlasAnchor[] a = Frames[frameIndex].Anchors;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].NameIndex == nameIndex) { x = a[i].X; y = a[i].Y; return true; }
            }
            return false;
        }

        public void Save(Stream output)
        {
            using (var gz = new GZipStream(output, CompressionLevel.Optimal, true))
            using (var w = new BinaryWriter(gz, Encoding.UTF8))
            {
                w.Write(Magic);
                w.Write(FormatVersion);
                w.Write((short)CanvasWidth); w.Write((short)CanvasHeight);
                w.Write((short)OriginX); w.Write((short)OriginY);
                w.Write(Palette.Length);
                for (int i = 0; i < Palette.Length; i++) w.Write(Palette[i]);
                w.Write(AnchorNames.Count);
                foreach (string n in AnchorNames) WriteString(w, n);
                w.Write(Frames.Count);
                foreach (AtlasFrame f in Frames)
                {
                    w.Write((short)f.OffsetX); w.Write((short)f.OffsetY);
                    w.Write((short)f.Width); w.Write((short)f.Height);
                    w.Write(f.Pixels, 0, f.Width * f.Height);
                    w.Write((byte)f.Anchors.Length);
                    foreach (AtlasAnchor a in f.Anchors)
                    {
                        w.Write((byte)a.NameIndex); w.Write((short)a.X); w.Write((short)a.Y);
                    }
                }
                w.Write(ClipList.Count);
                foreach (AtlasClip c in ClipList)
                {
                    WriteString(w, c.Name);
                    w.Write((byte)(c.Loop ? 1 : 0));
                    w.Write((short)c.Count);
                    for (int i = 0; i < c.Count; i++)
                    {
                        w.Write(c.Frames[i]);
                        w.Write((short)c.Durations[i]);
                    }
                }
            }
        }

        public static Atlas Load(Stream input)
        {
            using (var gz = new GZipStream(input, CompressionMode.Decompress, true))
            using (var r = new BinaryReader(gz, Encoding.UTF8))
            {
                if (r.ReadInt32() != Magic) throw new InvalidDataException("not an atlas file");
                int version = r.ReadInt32();
                if (version != FormatVersion) throw new InvalidDataException("unsupported atlas version " + version);
                var a = new Atlas();
                a.CanvasWidth = r.ReadInt16(); a.CanvasHeight = r.ReadInt16();
                a.OriginX = r.ReadInt16(); a.OriginY = r.ReadInt16();
                int pc = r.ReadInt32();
                if (pc < 1 || pc > 256) throw new InvalidDataException("bad palette size");
                a.Palette = new uint[pc];
                for (int i = 0; i < pc; i++) a.Palette[i] = r.ReadUInt32();
                int ac = r.ReadInt32();
                for (int i = 0; i < ac; i++) a.AnchorNames.Add(ReadString(r));
                int fc = r.ReadInt32();
                for (int i = 0; i < fc; i++)
                {
                    var f = new AtlasFrame();
                    f.OffsetX = r.ReadInt16(); f.OffsetY = r.ReadInt16();
                    f.Width = r.ReadInt16(); f.Height = r.ReadInt16();
                    f.Pixels = r.ReadBytes(f.Width * f.Height);
                    if (f.Pixels.Length != f.Width * f.Height) throw new InvalidDataException("truncated frame");
                    for (int p = 0; p < f.Pixels.Length; p++)
                    {
                        if (f.Pixels[p] >= pc) throw new InvalidDataException("pixel outside palette");
                    }
                    int n = r.ReadByte();
                    f.Anchors = new AtlasAnchor[n];
                    for (int k = 0; k < n; k++)
                    {
                        f.Anchors[k].NameIndex = r.ReadByte();
                        f.Anchors[k].X = r.ReadInt16();
                        f.Anchors[k].Y = r.ReadInt16();
                    }
                    a.Frames.Add(f);
                }
                int cc = r.ReadInt32();
                for (int i = 0; i < cc; i++)
                {
                    var c = new AtlasClip();
                    c.Name = ReadString(r);
                    c.Loop = (r.ReadByte() & 1) != 0;
                    int n = r.ReadInt16();
                    c.Frames = new int[n];
                    c.Durations = new int[n];
                    for (int k = 0; k < n; k++)
                    {
                        c.Frames[k] = r.ReadInt32();
                        c.Durations[k] = Math.Max(1, (int)r.ReadInt16());
                        if (c.Frames[k] < 0 || c.Frames[k] >= fc) throw new InvalidDataException("clip frame out of range");
                    }
                    a.AddClip(c);
                }
                return a;
            }
        }

        static void WriteString(BinaryWriter w, string s)
        {
            byte[] b = Encoding.UTF8.GetBytes(s);
            w.Write((ushort)b.Length);
            w.Write(b);
        }

        static string ReadString(BinaryReader r)
        {
            int n = r.ReadUInt16();
            return Encoding.UTF8.GetString(r.ReadBytes(n));
        }
    }
}
