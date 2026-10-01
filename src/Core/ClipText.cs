using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Aly.Core
{
    /// <summary>
    /// What fits on her little sign: whitespace collapsed, at most 20 characters, then "...".
    /// "Characters" are user-perceived: an emoji built from several code points (ZWJ
    /// families, skin tones, flags, keycaps) counts as one.
    /// </summary>
    public static class ClipText
    {
        public const int SignLimit = 20;
        public const string Ellipsis = "...";

        public static string ForSign(string text)
        {
            return Truncate(Collapse(text), SignLimit);
        }

        /// <summary>Newlines, tabs and runs of spaces become single spaces; ends are trimmed.</summary>
        public static string Collapse(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            bool space = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c))
                {
                    space = sb.Length > 0;
                    continue;
                }
                if (space)
                {
                    sb.Append(' ');
                    space = false;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Truncate(string text, int limit)
        {
            List<string> g = Graphemes(text);
            if (g.Count <= limit) return text;
            var sb = new StringBuilder();
            for (int i = 0; i < limit; i++) sb.Append(g[i]);
            return sb.ToString() + Ellipsis;
        }

        public static int Length(string text)
        {
            return Graphemes(text).Count;
        }

        /// <summary>
        /// Splits into perceived characters. .NET Framework's text elements handle surrogate
        /// pairs and combining marks; on top of that we glue emoji sequences together.
        /// </summary>
        public static List<string> Graphemes(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text)) return result;
            TextElementEnumerator e = StringInfo.GetTextElementEnumerator(text);
            bool joinNext = false;
            while (e.MoveNext())
            {
                string el = e.GetTextElement();
                int cp = char.ConvertToUtf32(el, 0);
                bool attach = result.Count > 0 && (joinNext || IsModifier(cp) || IsTag(cp) || cp == 0x200D || cp == 0xFE0F
                    || cp == 0x20E3 || (IsRegional(cp) && IsSingleRegional(result[result.Count - 1])));
                if (attach) result[result.Count - 1] += el;
                else result.Add(el);
                joinNext = el.EndsWith("\u200D", StringComparison.Ordinal);
            }
            return result;
        }

        static bool IsModifier(int cp) { return cp >= 0x1F3FB && cp <= 0x1F3FF; }

        /// <summary>Tag characters spell out subdivision flags (England, Scotland…).</summary>
        static bool IsTag(int cp) { return cp >= 0xE0020 && cp <= 0xE007F; }

        static bool IsRegional(int cp) { return cp >= 0x1F1E6 && cp <= 0x1F1FF; }

        static bool IsSingleRegional(string s)
        {
            return s.Length == 2 && IsRegional(char.ConvertToUtf32(s, 0));
        }
    }
}
