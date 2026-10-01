using System;
using System.Text.RegularExpressions;

namespace Aly.Core
{
    /// <summary>
    /// Guesses whether copied text is a password or secret key, so it is masked on the sign
    /// and never written to disk. Copies that password managers mark as private are skipped
    /// before this ever runs (see ClipboardWatch); this catches the ones copied by hand.
    /// It errs on the side of hiding: a masked file name is harmless, a shown password is not.
    /// </summary>
    public static class Privacy
    {
        const RegexOptions Opts = RegexOptions.CultureInvariant;

        static readonly Regex KnownKey = new Regex(
            @"^(sk-[A-Za-z0-9_\-]{16,}|gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}" +
            @"|AKIA[0-9A-Z]{16}|AIza[0-9A-Za-z_\-]{30,}|xox[baprs]-[A-Za-z0-9\-]{10,}" +
            @"|eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+)$", Opts);

        static readonly Regex FileName = new Regex(
            @"^[^\s\\/:*?""<>|]+\.(txt|md|pdf|docx?|xlsx?|pptx?|csv|log|ini|cfg|json|xml|ya?ml|html?|css|js|ts|py|cs|cpp|c|h|java|go|rs|sh|bat|ps1" +
            @"|jpe?g|png|gif|bmp|webp|heic|svg|psd|ai|mp3|wav|flac|m4a|mp4|mov|avi|mkv|zip|rar|7z|exe|msi|dll|apk|iso)$",
            Opts | RegexOptions.IgnoreCase);

        static readonly Regex Version = new Regex(@"^[vV]?\d+(\.\d+){1,3}([-+.][0-9A-Za-z][0-9A-Za-z.\-]*)?$", Opts);

        static readonly Regex Date = new Regex(
            @"^\d{4}[-/.]\d{1,2}[-/.]\d{1,2}([T ]\d{1,2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:?\d{2})?)?$", Opts);

        /// <summary>Runs of word-like pieces: "getElementById" = get|Element|By|Id.</summary>
        static readonly Regex Piece = new Regex(@"[A-Z]?[a-z]+|[A-Z]+(?![a-z])|\d+", Opts);

        public static bool LooksSecret(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.Trim();
            if (t.Length < 8 || t.Length > 200) return false;
            if (KnownKey.IsMatch(t)) return true;
            if (t.Length > 64) return false;
            foreach (char c in t) if (c <= ' ' || c > '~') return false;               // a single ASCII token only
            if (t.Contains("://") || t.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) return false;
            int at = t.IndexOf('@');
            if (at > 0 && t.IndexOf('.', at) > at + 1) return false;                   // e-mail address
            if (t.Contains("\\") || t.StartsWith("/")) return false;                   // a path
            if (FileName.IsMatch(t) || Version.IsMatch(t) || Date.IsMatch(t)) return false;

            int lower = 0, upper = 0, digit = 0, symbol = 0;
            foreach (char c in t)
            {
                if (c >= 'a' && c <= 'z') lower = 1;
                else if (c >= 'A' && c <= 'Z') upper = 1;
                else if (c >= '0' && c <= '9') digit = 1;
                else symbol = 1;
            }
            int classes = lower + upper + digit + symbol;
            if (classes >= 3) return true;
            if (classes < 2) return false;                                             // a plain word or number

            // Random-looking: words are long runs ("user_name"), keys are short ones ("xK9mP2").
            int alnum = 0, pieces = 0;
            foreach (Match m in Piece.Matches(t))
            {
                alnum += m.Length;
                pieces++;
            }
            if (pieces == 0) return false;
            if (t.Length >= 12 && classes >= 2 && alnum / (double)pieces <= 2.2) return true;
            return t.Length >= 24 && classes >= 2;                                     // long hex / base64 tokens
        }
    }
}
