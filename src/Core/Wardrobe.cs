using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Aly.Core
{
    /// <summary>One outfit: new colours for some palette entries, and whether it wears the coat.</summary>
    public sealed class Outfit
    {
        public string Id;
        public string Name;
        /// <summary>Switches to the *_coat looks (the draped coat is a layer of its own).</summary>
        public bool Coat;
        /// <summary>Palette index → ARGB.</summary>
        public readonly Dictionary<int, uint> Colors = new Dictionary<int, uint>();

        /// <summary>A copy of the palette with this outfit's colours in it.</summary>
        public uint[] Apply(uint[] palette)
        {
            var p = (uint[])palette.Clone();
            foreach (var kv in Colors)
                if (kv.Key > 0 && kv.Key < p.Length) p[kv.Key] = kv.Value;
            return p;
        }
    }

    /// <summary>
    /// The outfits of a pet with a wardrobe: assets/(pet)/outfits.txt, written by ArtBuild from
    /// art/(pet)/outfits.txt with the palette keys already turned into indices:
    ///   [id]  name = 黑衬衫+黑牛仔裤  coat = yes|no  (index) = AARRGGBB ...
    /// The first outfit is the default one (its colours are the atlas palette's own).
    /// </summary>
    public sealed class Wardrobe
    {
        public readonly List<Outfit> Outfits = new List<Outfit>();

        public Outfit Default { get { return Outfits.Count > 0 ? Outfits[0] : null; } }

        public Outfit Find(string id)
        {
            foreach (Outfit o in Outfits)
                if (o.Id == id) return o;
            return null;
        }

        public static Wardrobe Parse(TextReader reader)
        {
            var w = new Wardrobe();
            Outfit current = null;
            string raw;
            int n = 0;
            while ((raw = reader.ReadLine()) != null)
            {
                n++;
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    string id = line.Substring(1, line.Length - 2).Trim();
                    if (id.Length == 0 || w.Find(id) != null) throw Error(n, "missing or duplicate outfit id");
                    current = new Outfit();
                    current.Id = id;
                    current.Name = id;
                    w.Outfits.Add(current);
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0 || current == null) throw Error(n, "expected [id] or key = value");
                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                if (key == "name") current.Name = value;
                else if (key == "coat") current.Coat = value == "yes" || value == "true" || value == "1";
                else
                {
                    int index;
                    uint color;
                    if (!int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out index) || index <= 0 || index > 255)
                        throw Error(n, "unknown key " + key);
                    if (value.Length == 6) value = "FF" + value;
                    if (value.Length != 8 || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color))
                        throw Error(n, "bad colour " + value);
                    current.Colors[index] = color;
                }
            }
            return w;
        }

        static FormatException Error(int line, string message)
        {
            return new FormatException("outfits.txt(" + line + "): " + message);
        }
    }
}
