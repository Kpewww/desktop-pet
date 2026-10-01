using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Aly.Core.Engine;

namespace Aly.Core
{
    /// <summary>Something to feed her, as listed in the character file.</summary>
    public sealed class Food
    {
        public string Id;
        /// <summary>Menu text (冰沙).</summary>
        public string Label;
        public string Clip;
        /// <summary>Lines at clip frames when someone feeds her.</summary>
        public Cue[] Cues = new Cue[0];
        /// <summary>Lines when she got it herself (the self line is said first); null = same as Cues.</summary>
        public Cue[] SelfCues;
        /// <summary>Said as she goes to get it herself because nobody fed her.</summary>
        public string SelfLine;
        /// <summary>Fullness added once she has finished it.</summary>
        public double Amount = 25;
        public double Mood = 8;
        /// <summary>Farts come more often for a while afterwards.</summary>
        public bool Gassy;
    }

    /// <summary>
    /// Everything that makes one pet different from another while sharing the engine: names,
    /// the ids its files and system objects use, its foods and optional features. Read from
    /// assets/&lt;key&gt;/character.txt, embedded in the exe:
    ///   key = value            (see the fields below)
    ///   [food id]              then label / clip / cues / self / selfline / amount / mood / gassy
    /// cues look like "1:smoothie 6:freeze" (frame:line).
    /// </summary>
    public sealed class Character
    {
        /// <summary>Folder name under art/ and assets/ (aly, kpew).</summary>
        public string Key;
        /// <summary>Data folder, single-instance and quit names, autostart value, vault salt. Never change it once shipped.</summary>
        public string Id;
        /// <summary>Shown everywhere: 小柴.</summary>
        public string Name;
        /// <summary>Short name (小柴).</summary>
        public string Nick;
        /// <summary>她 / 他 in menus and hints.</summary>
        public string Pronoun = "她";
        /// <summary>What the ears-and-tail switch is called (狗耳朵和尾巴).</summary>
        public string EarsLabel = "耳朵和尾巴";
        /// <summary>Default shortcut for 小窝.</summary>
        public string Hotkey = "Ctrl+Alt+V";
        /// <summary>The look (atlas) to start with: "base" (no variants), or a variants.txt look like "long".</summary>
        public string Look = "base";
        /// <summary>The other pet's id (they find and talk to each other when both run); null = none.</summary>
        public string Partner;
        /// <summary>What she gets herself when starving and nobody feeds her; null = she just complains.</summary>
        public string SelfFood;
        /// <summary>With two pets running, the higher duty holds the clipboard sign and gives the reminders.</summary>
        public int Duty;
        /// <summary>Where she first appears, as a share of the screen width (after that: wherever she was).</summary>
        public double Start = 0.85;
        public readonly List<Food> Foods = new List<Food>();
        readonly HashSet<string> features = new HashSet<string>(StringComparer.Ordinal);

        static readonly Regex IdPattern = new Regex("^[A-Za-z][A-Za-z0-9]{0,39}$");
        static readonly Regex KeyPattern = new Regex("^[a-z][a-z0-9_]{0,19}$");

        /// <summary>Optional features: wardrobe (衣柜), calm (防干扰模式).</summary>
        public bool Has(string feature) { return features.Contains(feature); }

        public IEnumerable<string> Features { get { return features; } }

        public Food FindFood(string id)
        {
            foreach (Food f in Foods)
                if (f.Id == id) return f;
            return null;
        }

        public static Character Parse(TextReader reader)
        {
            var c = new Character();
            Food food = null;
            string raw;
            int n = 0;
            while ((raw = reader.ReadLine()) != null)
            {
                n++;
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("["))
                {
                    if (!line.EndsWith("]")) throw Error(n, "section must look like [food id]");
                    string[] t = line.Substring(1, line.Length - 2).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (t.Length != 2 || t[0] != "food") throw Error(n, "unknown section " + line);
                    if (c.FindFood(t[1]) != null) throw Error(n, "duplicate food " + t[1]);
                    food = new Food();
                    food.Id = t[1];
                    c.Foods.Add(food);
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) throw Error(n, "expected key = value");
                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                if (food != null) SetFood(food, key, value, n);
                else c.Set(key, value, n);
            }
            c.Validate();
            return c;
        }

        void Set(string key, string value, int n)
        {
            switch (key)
            {
                case "key": Key = value; break;
                case "id": Id = value; break;
                case "name": Name = value; break;
                case "nick": Nick = value; break;
                case "pronoun": Pronoun = value; break;
                case "ears": EarsLabel = value; break;
                case "hotkey": Hotkey = value; break;
                case "look": Look = value; break;
                case "partner": Partner = value.Length == 0 ? null : value; break;
                case "selffood": SelfFood = value.Length == 0 ? null : value; break;
                case "duty": Duty = Int(value, n); break;
                case "start": Start = Number(value, n); break;
                case "features":
                    features.Clear();
                    foreach (string f in value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)) features.Add(f);
                    break;
                default: throw Error(n, "unknown key " + key);
            }
        }

        static void SetFood(Food f, string key, string value, int n)
        {
            switch (key)
            {
                case "label": f.Label = value; break;
                case "clip": f.Clip = value; break;
                case "cues": f.Cues = Cues(value, n); break;
                case "self": f.SelfCues = Cues(value, n); break;
                case "selfline": f.SelfLine = value.Length == 0 ? null : value; break;
                case "amount": f.Amount = Number(value, n); break;
                case "mood": f.Mood = Number(value, n); break;
                case "gassy": f.Gassy = value == "yes" || value == "true" || value == "1"; break;
                default: throw Error(n, "unknown food key " + key);
            }
        }

        void Validate()
        {
            if (Key == null || !KeyPattern.IsMatch(Key)) throw new FormatException("character: key must be lower-case letters/digits");
            if (Id == null || !IdPattern.IsMatch(Id)) throw new FormatException("character: id must be ASCII letters/digits");
            if (string.IsNullOrEmpty(Name)) throw new FormatException("character: name is missing");
            if (string.IsNullOrEmpty(Nick)) Nick = Name;
            if (string.IsNullOrEmpty(Look) || !KeyPattern.IsMatch(Look)) throw new FormatException("character: look must be a look name like short");
            if (Start < 0 || Start > 1) throw new FormatException("character: start must be between 0 and 1");
            foreach (Food f in Foods)
            {
                if (string.IsNullOrEmpty(f.Label) || string.IsNullOrEmpty(f.Clip))
                    throw new FormatException("character: food " + f.Id + " needs a label and a clip");
                if (f.Amount <= 0) throw new FormatException("character: food " + f.Id + " needs a positive amount");
            }
            if (SelfFood != null && FindFood(SelfFood) == null)
                throw new FormatException("character: selffood " + SelfFood + " is not one of the foods");
        }

        static Cue[] Cues(string value, int n)
        {
            var list = new List<Cue>();
            foreach (string item in value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = item.IndexOf(':');
                if (colon <= 0 || colon == item.Length - 1) throw Error(n, "cue must look like frame:line, got " + item);
                list.Add(new Cue(Int(item.Substring(0, colon), n), item.Substring(colon + 1)));
            }
            for (int i = 1; i < list.Count; i++)
                if (list[i].Frame < list[i - 1].Frame) throw Error(n, "cues must be in frame order");
            return list.ToArray();
        }

        static int Int(string s, int n)
        {
            int v;
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) throw Error(n, "expected a whole number, got " + s);
            return v;
        }

        static double Number(string s, int n)
        {
            double v;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) throw Error(n, "expected a number, got " + s);
            return v;
        }

        static FormatException Error(int line, string message)
        {
            return new FormatException("character.txt(" + line + "): " + message);
        }
    }
}
