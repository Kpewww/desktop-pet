using System;
using System.Collections.Generic;

namespace Aly.Core
{
    public enum ClipKind
    {
        Text,
        Image,
        Files,
    }

    /// <summary>One clipboard copy. Images keep only a small thumbnail (owned by the app).</summary>
    public sealed class ClipItem
    {
        public int Id;
        public ClipKind Kind;
        public string Text;          // text copies
        public string[] Files;       // file copies
        public int ImageWidth, ImageHeight;
        public object Thumbnail;     // app-side bitmap for images
        public string Signature;     // what makes two copies "the same"
        public DateTime Time;
        public bool Pinned;
        /// <summary>Looks like a password or key: masked on the sign and in the list, never saved to disk.</summary>
        public bool Secret;
        /// <summary>Very long text: only the beginning was kept.</summary>
        public bool Truncated;

        public static ClipItem FromText(string text, bool truncated)
        {
            var i = new ClipItem();
            i.Kind = ClipKind.Text;
            i.Text = text;
            i.Truncated = truncated;
            i.Secret = Privacy.LooksSecret(text);
            i.Signature = "T:" + (text.Length <= 1024 ? text : text.Length + ":" + Fnv(text).ToString("x8"));
            i.Time = DateTime.Now;
            return i;
        }

        /// <summary>A picture; the app attaches its own thumbnail bitmap made from dib.Thumb.</summary>
        public static ClipItem FromImage(DibInfo dib)
        {
            var i = new ClipItem();
            i.Kind = ClipKind.Image;
            i.ImageWidth = dib.Width;
            i.ImageHeight = dib.Height;
            i.Signature = string.Format("I:{0}x{1}:{2:x8}", dib.Width, dib.Height, dib.Hash);
            i.Time = DateTime.Now;
            return i;
        }

        public static ClipItem FromFiles(string[] files)
        {
            var i = new ClipItem();
            i.Kind = ClipKind.Files;
            i.Files = files;
            i.Signature = "F:" + string.Join("\n", files);
            i.Time = DateTime.Now;
            return i;
        }

        static uint Fnv(string s)
        {
            uint h = 2166136261;
            foreach (char c in s) h = (h ^ c) * 16777619;
            return h;
        }

        /// <summary>The short line shown on her sign.</summary>
        public string SignText
        {
            get
            {
                if (Secret) return "（已隐藏）";
                switch (Kind)
                {
                    case ClipKind.Image:
                        return string.Format("图片 {0}×{1}", ImageWidth, ImageHeight);
                    case ClipKind.Files:
                        if (Files == null || Files.Length == 0) return "文件";
                        string name = System.IO.Path.GetFileName(Files[0].TrimEnd('\\', '/'));
                        if (string.IsNullOrEmpty(name)) name = Files[0];
                        name = ClipText.ForSign(name);
                        return Files.Length == 1 ? name : name + " 等 " + Files.Length + " 个文件";
                    default:
                        string s = ClipText.ForSign(Text);
                        return s.Length > 0 ? s : "（空白）";
                }
            }
        }

        /// <summary>A longer one-line preview for the history list (which shows the kind separately).</summary>
        public string Preview(int maxChars)
        {
            if (Secret) return "（疑似密码，已隐藏）";
            switch (Kind)
            {
                case ClipKind.Image: return string.Format("{0}×{1}", ImageWidth, ImageHeight);
                case ClipKind.Files: return string.Join("；", Files ?? new string[0]);
                default: return ClipText.Truncate(ClipText.Collapse(Text), maxChars);
            }
        }
    }

    /// <summary>
    /// Newest first. Copying something already in the list moves it back to the top instead
    /// of adding it twice; pinned items are never pushed out by the size cap.
    /// </summary>
    public sealed class ClipHistory
    {
        readonly List<ClipItem> items = new List<ClipItem>();
        int nextId = 1;
        public int Capacity = 50;
        public bool Paused;
        /// <summary>Called for every item that leaves the list (so the app can free thumbnails).</summary>
        public Action<ClipItem> Removed;

        public IList<ClipItem> Items { get { return items.AsReadOnly(); } }
        public int Count { get { return items.Count; } }
        public ClipItem Latest { get { return items.Count > 0 ? items[0] : null; } }

        /// <summary>
        /// Returns the stored item — the new one, or the earlier copy of the same content (then
        /// the passed item is not kept) — or null when paused.
        /// </summary>
        public ClipItem Add(ClipItem item)
        {
            if (Paused) return null;
            if (items.Count > 0 && items[0].Signature == item.Signature)
            {
                items[0].Time = item.Time;
                return items[0];
            }
            int existing = items.FindIndex(i => i.Signature == item.Signature);
            if (existing >= 0)
            {
                ClipItem old = items[existing];
                items.RemoveAt(existing);
                old.Time = item.Time;
                items.Insert(0, old);
                return old;
            }
            item.Id = nextId++;
            items.Insert(0, item);
            Trim();
            return item;
        }

        public void Remove(int id)
        {
            int k = items.FindIndex(i => i.Id == id);
            if (k >= 0) RemoveAt(k);
        }

        /// <summary>Puts back items saved earlier (oldest last), keeping ids unique.</summary>
        public void Restore(IEnumerable<ClipItem> saved)
        {
            foreach (ClipItem i in saved)
            {
                i.Id = nextId++;
                items.Add(i);
            }
            Trim();
        }

        public void SetPinned(int id, bool pinned)
        {
            ClipItem it = Find(id);
            if (it != null) it.Pinned = pinned;
        }

        public ClipItem Find(int id)
        {
            return items.Find(i => i.Id == id);
        }

        /// <summary>Clears everything except pinned items.</summary>
        public void Clear()
        {
            for (int k = items.Count - 1; k >= 0; k--)
            {
                if (!items[k].Pinned) RemoveAt(k);
            }
        }

        void RemoveAt(int k)
        {
            ClipItem it = items[k];
            items.RemoveAt(k);
            if (Removed != null) Removed(it);
        }

        void Trim()
        {
            int unpinned = 0;
            foreach (ClipItem i in items) if (!i.Pinned) unpinned++;
            for (int k = items.Count - 1; k >= 0 && unpinned > Capacity; k--)
            {
                if (items[k].Pinned) continue;
                RemoveAt(k);
                unpinned--;
            }
        }
    }
}
