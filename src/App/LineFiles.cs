using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aly.Core;

namespace Aly.App
{
    /// <summary>
    /// Lines the user adds live in their own folder, %APPDATA%\(id)\台词\, never inside the exe:
    /// an update brings new built-in lines and leaves hers alone. 我的台词.txt is what 小窝's
    /// 台词 page edits; any other .txt dropped into the folder (lines someone sent) is read too.
    /// </summary>
    static class LineFiles
    {
        public const string MineName = "我的台词.txt";

        public static string Folder { get { return Path.Combine(Store.Dir, "台词"); } }
        public static string Mine { get { return Path.Combine(Folder, MineName); } }

        static string Header()
        {
            return "这里是你自己加给" + Profile.Name + "的台词（小窝 → 台词 页加的都在这里）。\n" +
                   "格式：[场景名] 下面一行一句；写成 [场景名]! 就只说这里的，不说内置的。\n" +
                   "{name} 会换成" + Profile.Pronoun + "的名字。这个文件夹里别的 .txt 也会读进来，\n" +
                   "别人发给你的台词文件直接放进来就行。更新程序不会动这个文件夹。";
        }

        /// <summary>
        /// Makes sure the folder and 我的台词.txt exist. The old single file (lines.txt next to the
        /// settings) moves in here if anything was written in it; an untouched template is removed.
        /// </summary>
        public static void Prepare()
        {
            Directory.CreateDirectory(Folder);
            string legacy = Path.Combine(Store.Dir, "lines.txt");
            if (File.Exists(legacy))
            {
                bool written = File.ReadAllLines(legacy, Encoding.UTF8).Any(l => l.Trim().Length > 0 && !l.Trim().StartsWith("#"));
                if (written)
                {
                    string target = Path.Combine(Folder, "以前的台词.txt");
                    if (File.Exists(target)) target = Path.Combine(Folder, "以前的台词 " + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                    File.Move(legacy, target);
                }
                else File.Delete(legacy);
            }
            if (!File.Exists(Mine)) WriteMine(new LineBook());
        }

        /// <summary>Every lines file in reading order: the others by name, 我的台词.txt last (so its [scene]! wins).</summary>
        public static List<string> Files()
        {
            var list = new List<string>();
            if (!Directory.Exists(Folder)) return list;
            foreach (string f in Directory.GetFiles(Folder, "*.txt").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                if (!string.Equals(Path.GetFileName(f), MineName, StringComparison.OrdinalIgnoreCase)) list.Add(f);
            if (File.Exists(Mine)) list.Add(Mine);
            return list;
        }

        public static LineBook Read(string path)
        {
            try
            {
                using (var r = new StreamReader(path, Encoding.UTF8)) return LineBook.Parse(r);
            }
            catch (Exception)
            {
                return new LineBook();
            }
        }

        public static LineBook ReadMine() { return Read(Mine); }

        /// <summary>Saves 我的台词.txt (written to a temporary file first, so it is never half-written).</summary>
        public static void WriteMine(LineBook book)
        {
            Directory.CreateDirectory(Folder);
            string tmp = Mine + ".tmp";
            using (var w = new StreamWriter(tmp, false, new UTF8Encoding(true)))
            {
                w.NewLine = "\r\n";
                book.Write(w, Header());
            }
            if (File.Exists(Mine)) File.Replace(tmp, Mine, null);
            else File.Move(tmp, Mine);
        }
    }
}
