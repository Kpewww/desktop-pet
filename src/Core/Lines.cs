using System;
using System.Collections.Generic;
using System.IO;

namespace Aly.Core
{
    /// <summary>
    /// What she says. Text format, one line per sentence under [trigger] headers:
    ///   [poke_annoyed]
    ///   别戳啦！
    /// A user file adds lines to the built-in ones; "[trigger]!" replaces them instead.
    /// Picks at random but never repeats the previous line of the same trigger.
    /// </summary>
    public sealed class Lines
    {
        readonly Dictionary<string, List<string>> map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        readonly Dictionary<string, int> last = new Dictionary<string, int>(StringComparer.Ordinal);

        public int Count(string trigger)
        {
            List<string> l;
            return map.TryGetValue(trigger, out l) ? l.Count : 0;
        }

        public void Load(TextReader reader)
        {
            string trigger = null;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && (line.EndsWith("]") || line.EndsWith("]!")))
                {
                    bool replace = line.EndsWith("!");
                    trigger = line.Substring(1, line.LastIndexOf(']') - 1).Trim();
                    if (replace || !map.ContainsKey(trigger)) map[trigger] = new List<string>();
                    continue;
                }
                if (trigger == null) continue;
                map[trigger].Add(line);
            }
        }

        public string Pick(string trigger, Engine.Rng rng)
        {
            List<string> l;
            if (!map.TryGetValue(trigger, out l) || l.Count == 0) return null;
            int prev;
            if (!last.TryGetValue(trigger, out prev)) prev = -1;
            int i;
            if (l.Count == 1) i = 0;
            else
            {
                i = rng.Next(l.Count - (prev >= 0 ? 1 : 0));
                if (prev >= 0 && i >= prev) i++;
            }
            last[trigger] = i;
            return l[i];
        }
    }
}
