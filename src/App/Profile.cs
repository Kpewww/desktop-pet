using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Aly.Core;

namespace Aly.App
{
    /// <summary>
    /// Which pet this exe is: its character file and its embedded assets ("Pet.name").
    /// Loaded first thing in Main; everything else may use <see cref="Me"/>.
    /// </summary>
    static class Profile
    {
        static Character me;

        public static Character Me
        {
            get
            {
                if (me == null) me = LoadCharacter();
                return me;
            }
        }

        /// <summary>The name the user gave her in 设置 (null = the character's own).</summary>
        public static string NameOverride;

        /// <summary>Her name as shown everywhere: the user's choice, else the character's.</summary>
        public static string Name
        {
            get { return string.IsNullOrEmpty(NameOverride) ? Me.Name : NameOverride; }
        }

        /// <summary>The name to show, even if the character file could not be read.</summary>
        public static string SafeName
        {
            get
            {
                try { return Name; }
                catch (Exception) { return "桌宠"; }
            }
        }

        /// <summary>
        /// Caption of her (invisible) window, fixed by the character id so the other pet can
        /// find her whatever she is called.
        /// </summary>
        public static string WindowCaption { get { return "DesktopPet:" + Me.Id; } }

        /// <summary>她 / 他.</summary>
        public static string Pronoun { get { return Me.Pronoun; } }

        /// <summary>
        /// An embedded asset. A development build without it falls back to assets\(key)\name,
        /// then assets\name (shared ones like the font), next to the out\ folder.
        /// </summary>
        public static Stream Open(string name)
        {
            Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Pet." + name);
            if (s != null) return s;
            string assets = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "..", "assets");
            if (me != null)
            {
                string own = Path.Combine(assets, me.Key, name);
                if (File.Exists(own)) return File.OpenRead(own);
            }
            string shared = Path.Combine(assets, name);
            return File.Exists(shared) ? File.OpenRead(shared) : null;
        }

        static Character LoadCharacter()
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Pet.character.txt"))
            {
                if (s == null) throw new FileNotFoundException("程序里缺少角色档 character.txt");
                using (var r = new StreamReader(s, Encoding.UTF8)) return Character.Parse(r);
            }
        }
    }
}
