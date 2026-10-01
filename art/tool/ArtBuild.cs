using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Aly.Core;

namespace Aly.ArtBuild
{
    /// <summary>
    /// Turns one pet's plain-text art sources (art/&lt;pet&gt;/) into assets/&lt;pet&gt;/*.atlas +
    /// icon.ico, builds the shared pixel font, and writes review images to art/out/&lt;pet&gt;/preview.
    ///   ArtBuild &lt;root&gt; &lt;pet&gt; [--zoom n] [--part name]... [--all-parts]
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("usage: ArtBuild <projectRoot> <pet> [--zoom n] [--part name]... [--all-parts]");
                return 2;
            }
            string root = args[0], pet = args[1];
            int zoom = 4;
            var partPreviews = new List<string>();
            bool allParts = false;
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--zoom" && i + 1 < args.Length) zoom = int.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (args[i] == "--part" && i + 1 < args.Length) partPreviews.Add(args[++i]);
                else if (args[i] == "--all-parts") allParts = true;
                else { Console.Error.WriteLine("unknown option " + args[i]); return 2; }
            }

            try
            {
                string art = Path.Combine(root, "art", pet);
                Palette palette = Palette.Load(Path.Combine(art, "palette.txt"));
                PartLibrary parts = PartLibrary.Load(Path.Combine(art, "parts"), palette);
                ClipSpec spec = ClipSpec.Load(Path.Combine(art, "clips.txt"), parts);
                List<Look> looks = Look.Load(Path.Combine(art, "variants.txt"), spec, parts);

                string assets = Path.Combine(root, "assets", pet);
                Directory.CreateDirectory(assets);
                foreach (string stale in Directory.GetFiles(assets, "*.atlas")) File.Delete(stale);
                var built = new List<KeyValuePair<Look, BuildResult>>();
                var sizes = new List<string>();
                foreach (Look look in looks)
                {
                    BuildResult full = Composer.Build(spec, parts, palette, look.Skip, look.Swap);
                    // The same clips without the ears and tail, for the 设置 switch.
                    var plainSkip = new HashSet<string>(look.Skip, StringComparer.Ordinal) { "ears", "tail" };
                    BuildResult plain = Composer.Build(spec, parts, palette, plainSkip, look.Swap);
                    long a = Save(full.Atlas, Path.Combine(assets, look.Name + ".atlas"));
                    long b = Save(plain.Atlas, Path.Combine(assets, look.Name + "_plain.atlas"));
                    built.Add(new KeyValuePair<Look, BuildResult>(look, full));
                    sizes.Add(string.Format("{0} {1:N1} KB (plain {2:N1} KB)", look.Name, a / 1024.0, b / 1024.0));
                }
                if (!parts.Has("icon")) throw new ArtException("a part named 'icon' is required for the app icon");
                IconWriter.Write(Path.Combine(assets, "icon.ico"), parts.Get("icon"), palette);

                // The wardrobe: outfits recolour the palette (the app reads assets/<pet>/outfits.txt).
                string outfitSource = Path.Combine(art, "outfits.txt"), outfitOut = Path.Combine(assets, "outfits.txt");
                List<OutfitSource> outfits = null;
                var outfitWarnings = new List<string>();
                if (File.Exists(outfitSource))
                {
                    outfits = OutfitSource.Load(outfitSource, palette, outfitWarnings);
                    foreach (OutfitSource o in outfits)
                        if (o.Coat && !built.Any(b => b.Key.Name == built[0].Key.Name + "_coat"))
                            throw new ArtException("outfit " + o.Id + " wears the coat but there is no " + built[0].Key.Name + "_coat look");
                    OutfitSource.Write(outfitOut, outfits, palette);
                }
                else if (File.Exists(outfitOut)) File.Delete(outfitOut);

                string bdf = Path.Combine(root, "art", "fonts", "fusion-pixel-12px-proportional-zh_hans.bdf");
                string fontOut = Path.Combine(root, "assets", "pixel12.font");
                if (File.Exists(bdf) && (!File.Exists(fontOut) || File.GetLastWriteTimeUtc(bdf) > File.GetLastWriteTimeUtc(fontOut)))
                {
                    int glyphs;
                    long fontBytes;
                    FontBuilder.Build(bdf, fontOut, out glyphs, out fontBytes);
                    Console.WriteLine("font {0} glyphs, {1:N1} KB", glyphs, fontBytes / 1024.0);
                }

                string preview = Path.Combine(root, "art", "out", pet, "preview");
                Preview.Reset(preview);
                BuildResult main = built[0].Value;
                Preview.WriteClips(preview, main.Atlas, zoom);
                Preview.WriteSheet(preview, main.Atlas, 3, c => !c.Name.StartsWith("gaze_"), "_sheet.png");
                Preview.WriteSheet(preview, main.Atlas, 6, c => c.Name.StartsWith("gaze_"), "_eyes.png");
                for (int i = 1; i < built.Count; i++)
                    Preview.WriteSheet(preview, built[i].Value.Atlas, 3, c => !c.Name.StartsWith("gaze_"), "_sheet_" + built[i].Key.Name + ".png");
                GifWriter.WriteClips(preview, main.Atlas, 3, 0xFFF4F1F6);
                if (outfits != null)
                {
                    string baseLook = built[0].Key.Name;
                    PreviewOutfits.Write(preview, outfits, palette, o =>
                    {
                        var hit = built.FirstOrDefault(b => b.Key.Name == (o.Coat ? baseLook + "_coat" : baseLook));
                        return (hit.Value ?? main).Atlas;
                    }, 3);
                    Console.WriteLine("  outfits: " + string.Join(", ", outfits.Select(o => o.Name)));
                    foreach (string w in outfitWarnings) Console.WriteLine("warning (outfits): " + w);
                }
                IEnumerable<string> names = allParts ? parts.All.Select(p => p.Name) : partPreviews;
                foreach (string name in names) Preview.WritePart(preview, parts.Get(name), palette, 12);

                Console.WriteLine("{0}: palette {1} colours, {2} parts, {3} clips, {4} frames ({5} unique)",
                    pet, palette.Count - 1, parts.Count, main.Atlas.ClipList.Count, main.ComposedFrames, main.Atlas.Frames.Count);
                Console.WriteLine("  atlases: " + string.Join(", ", sizes));
                foreach (var kv in built)
                    foreach (string w in kv.Value.Warnings) Console.WriteLine("warning ({0}): {1}", kv.Key.Name, w);
                return 0;
            }
            catch (ArtException ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                return 1;
            }
        }

        static long Save(Atlas atlas, string path)
        {
            using (var fs = File.Create(path)) atlas.Save(fs);
            return new FileInfo(path).Length;
        }
    }

    /// <summary>
    /// One look of a pet — a hairstyle, a coat… art/&lt;pet&gt;/variants.txt lists them, one per
    /// line: "name item item…" where an item is a layer this look leaves out, or "part=other"
    /// to draw another part in its place ('-' = nothing to change). Without the file there is a
    /// single look called "base". The first look is the default.
    /// </summary>
    sealed class Look
    {
        public string Name;
        public HashSet<string> Skip = new HashSet<string>(StringComparer.Ordinal);
        public Dictionary<string, string> Swap = new Dictionary<string, string>(StringComparer.Ordinal);

        public static List<Look> Load(string path, ClipSpec spec, PartLibrary parts)
        {
            var looks = new List<Look>();
            if (!File.Exists(path))
            {
                var only = new Look();
                only.Name = "base";
                looks.Add(only);
                return looks;
            }
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = Palette.StripComment(lines[i]);
                if (line.Length == 0) continue;
                string[] t = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                var look = new Look();
                look.Name = t[0];
                if (!System.Text.RegularExpressions.Regex.IsMatch(look.Name, "^[a-z][a-z0-9]*(_[a-z0-9]+)*$") || look.Name.EndsWith("_plain"))
                    throw new ArtException(path, i + 1, "look names are lower-case words joined by '_' (not ending in _plain)");
                if (looks.Any(l => l.Name == look.Name)) throw new ArtException(path, i + 1, "duplicate look " + look.Name);
                for (int k = 1; k < t.Length; k++)
                {
                    if (t[k] == "-") continue;
                    int eq = t[k].IndexOf('=');
                    if (eq > 0)
                    {
                        string from = t[k].Substring(0, eq), to = t[k].Substring(eq + 1);
                        if (!parts.Has(from)) throw new ArtException(path, i + 1, "unknown part " + from);
                        if (!parts.Has(to)) throw new ArtException(path, i + 1, "unknown part " + to);
                        look.Swap[from] = to;
                        continue;
                    }
                    if (!spec.IsLayer(t[k])) throw new ArtException(path, i + 1, "unknown layer " + t[k]);
                    look.Skip.Add(t[k]);
                }
                looks.Add(look);
            }
            if (looks.Count == 0) throw new ArtException(path, 1, "no looks listed");
            return looks;
        }
    }
}
