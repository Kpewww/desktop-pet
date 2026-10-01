using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using Aly.Core;

namespace Aly.App
{
    /// <summary>
    /// The optional on-disk clipboard history (off by default). Keeps text and file lists
    /// only — never images, never anything that looks like a password — encrypted with the
    /// Windows account's own key (DPAPI), so nobody else can read the file.
    /// </summary>
    static class ClipVault
    {
        // The pet's id is the salt: change the id and the old history can no longer be read.
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes(Profile.Me.Id + ".clipboard.v1");

        static string FilePath { get { return Path.Combine(Store.Dir, "clipboard", "history.dat"); } }

        public static void Save(IEnumerable<ClipItem> items)
        {
            var list = new List<SavedClip>();
            foreach (ClipItem it in items)
            {
                if (it.Secret || it.Kind == ClipKind.Image) continue;
                var s = new SavedClip();
                s.Kind = (int)it.Kind;
                s.Text = it.Text;
                s.Files = it.Files;
                s.Time = it.Time.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                s.Pinned = it.Pinned;
                s.Truncated = it.Truncated;
                list.Add(s);
            }
            byte[] plain;
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(List<SavedClip>)).WriteObject(ms, list);
                plain = ms.ToArray();
            }
            byte[] sealedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            Array.Clear(plain, 0, plain.Length);
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, sealedBytes);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        /// <summary>The saved items, newest first; empty when there is no file or it can't be read.</summary>
        public static List<ClipItem> Load()
        {
            var result = new List<ClipItem>();
            try
            {
                string path = FilePath;
                if (!File.Exists(path)) return result;
                byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
                List<SavedClip> list;
                using (var ms = new MemoryStream(plain))
                {
                    list = (List<SavedClip>)new DataContractJsonSerializer(typeof(List<SavedClip>)).ReadObject(ms);
                }
                Array.Clear(plain, 0, plain.Length);
                foreach (SavedClip s in list ?? new List<SavedClip>())
                {
                    ClipItem it;
                    if (s.Kind == (int)ClipKind.Files && s.Files != null && s.Files.Length > 0) it = ClipItem.FromFiles(s.Files);
                    else if (s.Kind == (int)ClipKind.Text && !string.IsNullOrEmpty(s.Text)) it = ClipItem.FromText(s.Text, s.Truncated);
                    else continue;
                    DateTime t;
                    if (DateTime.TryParse(s.Time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t)) it.Time = t.ToLocalTime();
                    it.Pinned = s.Pinned;
                    result.Add(it);
                }
            }
            catch (Exception)
            {
                // unreadable (another account's file, corrupted…): start empty, it will be rewritten
            }
            return result;
        }

        public static void Delete()
        {
            try
            {
                string path = FilePath;
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception) { }
        }
    }

    [DataContract]
    sealed class SavedClip
    {
        [DataMember] public int Kind;
        [DataMember] public string Text;
        [DataMember] public string[] Files;
        [DataMember] public string Time;
        [DataMember] public bool Pinned;
        [DataMember] public bool Truncated;
    }
}
