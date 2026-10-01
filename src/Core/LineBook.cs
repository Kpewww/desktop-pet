using System;
using System.Collections.Generic;
using System.IO;

namespace Aly.Core
{
    /// <summary>
    /// A lines file as editable data (the format <see cref="Lines"/> reads): scenes in file
    /// order, each with its sentences and whether it replaces what was loaded before it
    /// ("[scene]!"). Used by 小窝's 台词 page to edit 我的台词.txt and to list the built-in ones.
    /// </summary>
    public sealed class LineBook
    {
        public const int MaxLength = 60;

        public sealed class Scene
        {
            public readonly string Key;
            /// <summary>"[scene]!": only these sentences, not the built-in ones.</summary>
            public bool Replace;
            public readonly List<string> Lines = new List<string>();

            public Scene(string key) { Key = key; }
        }

        readonly List<Scene> scenes = new List<Scene>();

        public IList<Scene> Scenes { get { return scenes.AsReadOnly(); } }

        public Scene Find(string key)
        {
            foreach (Scene s in scenes)
                if (s.Key == key) return s;
            return null;
        }

        Scene GetOrAdd(string key)
        {
            Scene s = Find(key);
            if (s != null) return s;
            s = new Scene(key);
            scenes.Add(s);
            return s;
        }

        /// <summary>
        /// Why a sentence can't be used as typed, or null if it can. It must fit on one line and
        /// can't start with '#' (a comment) or '[' (a scene header).
        /// </summary>
        public static string Problem(string sentence)
        {
            string s = Clean(sentence);
            if (s.Length == 0) return "先写点什么吧";
            if (s[0] == '#' || s[0] == '[') return "开头不能是 # 或 [";
            if (s.Length > MaxLength) return "太长啦，" + MaxLength + " 个字以内";
            return null;
        }

        /// <summary>Line breaks become spaces; outer spaces go.</summary>
        public static string Clean(string sentence)
        {
            if (sentence == null) return "";
            return sentence.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        /// <summary>Adds a sentence to a scene; false if it can't be used or is already there.</summary>
        public bool Add(string key, string sentence)
        {
            if (string.IsNullOrEmpty(key) || Problem(sentence) != null) return false;
            string s = Clean(sentence);
            Scene scene = GetOrAdd(key);
            if (scene.Lines.Contains(s)) return false;
            scene.Lines.Add(s);
            return true;
        }

        public bool Remove(string key, string sentence)
        {
            Scene scene = Find(key);
            if (scene == null || !scene.Lines.Remove(sentence)) return false;
            if (scene.Lines.Count == 0 && !scene.Replace) scenes.Remove(scene);
            return true;
        }

        public void SetReplace(string key, bool replace)
        {
            Scene scene = replace ? GetOrAdd(key) : Find(key);
            if (scene == null) return;
            scene.Replace = replace;
            if (!replace && scene.Lines.Count == 0) scenes.Remove(scene);
        }

        public static LineBook Parse(TextReader reader)
        {
            var book = new LineBook();
            Scene current = null;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && (line.EndsWith("]") || line.EndsWith("]!")))
                {
                    string key = line.Substring(1, line.LastIndexOf(']') - 1).Trim();
                    current = book.GetOrAdd(key);
                    if (line.EndsWith("!"))
                    {
                        current.Replace = true;
                        current.Lines.Clear();
                    }
                    continue;
                }
                if (current != null && !current.Lines.Contains(line)) current.Lines.Add(line);
            }
            return book;
        }

        /// <summary>Writes the scenes after an optional comment header (each header line gets "# ").</summary>
        public void Write(TextWriter w, string header)
        {
            if (!string.IsNullOrEmpty(header))
            {
                foreach (string h in header.Replace("\r\n", "\n").Split('\n')) w.WriteLine(h.Length == 0 ? "#" : "# " + h);
                w.WriteLine();
            }
            foreach (Scene s in scenes)
            {
                if (s.Lines.Count == 0 && !s.Replace) continue;
                w.WriteLine("[" + s.Key + "]" + (s.Replace ? "!" : ""));
                foreach (string l in s.Lines) w.WriteLine(l);
                w.WriteLine();
            }
        }
    }
}
