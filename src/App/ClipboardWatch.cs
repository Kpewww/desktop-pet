using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Aly.Core;

namespace Aly.App
{
    /// <summary>
    /// Watches the clipboard through WM_CLIPBOARDUPDATE on a message-only window — Windows
    /// tells us about each copy, nothing is polled. Copies that password managers mark as
    /// private are skipped without reading them. Text, file lists and images (only a small
    /// thumbnail is kept) become ClipItems.
    /// </summary>
    sealed unsafe class ClipboardWatch : NativeWindow, IDisposable
    {
        public const int MaxTextChars = 32 * 1024;
        const int MaxFiles = 500;
        const int ThumbW = 160, ThumbH = 120;

        enum ReadResult { Ok, Busy, Private }

        /// <summary>A new copy: the item and whether it was made by this app (copy-back from the list).</summary>
        public event Action<ClipItem, bool> Copied;

        readonly uint fmtExclude, fmtIgnore, fmtHistory;
        readonly System.Windows.Forms.Timer settle = new System.Windows.Forms.Timer();
        readonly uint ownPid;
        readonly bool listening;
        uint lastSeq;
        int retries;

        public ClipboardWatch()
        {
            var cp = new CreateParams();
            cp.Parent = Win32.HWND_MESSAGE;
            CreateHandle(cp);
            // The formats password managers (and Windows' own clipboard history) use to opt out.
            fmtExclude = Win32.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
            fmtIgnore = Win32.RegisterClipboardFormat("Clipboard Viewer Ignore");
            fmtHistory = Win32.RegisterClipboardFormat("CanIncludeInClipboardHistory");
            using (Process p = Process.GetCurrentProcess()) ownPid = (uint)p.Id;
            settle.Tick += delegate { settle.Stop(); OnSettled(); };
            lastSeq = Win32.GetClipboardSequenceNumber(); // what was there before she started is old news
            listening = Win32.AddClipboardFormatListener(Handle);
        }

        public bool Listening { get { return listening; } }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Win32.WM_CLIPBOARDUPDATE)
            {
                // Give the copying app a moment to put all its formats on the clipboard.
                retries = 0;
                settle.Stop();
                settle.Interval = 120;
                settle.Start();
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        void OnSettled()
        {
            uint seq = Win32.GetClipboardSequenceNumber();
            if (seq == lastSeq) return;
            ClipItem item;
            bool own, isPrivate;
            ReadResult r = TryRead(out item, out own, out isPrivate);
            if (r == ReadResult.Busy && retries++ < 8)
            {
                settle.Interval = 100; // someone else has it open; try again shortly
                settle.Start();
                return;
            }
            lastSeq = seq;
            if (item != null && Copied != null) Copied(item, own);
        }

        /// <summary>
        /// What is on the clipboard right now (for "看看剪贴板"). Null when it is empty, holds
        /// something she can't show, or is private (then isPrivate is set).
        /// </summary>
        public ClipItem ReadNow(out bool isPrivate)
        {
            isPrivate = false;
            for (int i = 0; i < 5; i++)
            {
                ClipItem item;
                bool own;
                ReadResult r = TryRead(out item, out own, out isPrivate);
                if (r != ReadResult.Busy) return item;
                Thread.Sleep(30);
            }
            return null;
        }

        ReadResult TryRead(out ClipItem item, out bool own, out bool isPrivate)
        {
            item = null;
            own = false;
            isPrivate = false;
            if (Available(fmtExclude) || Available(fmtIgnore))
            {
                isPrivate = true;
                return ReadResult.Private;
            }
            own = OwnedByUs();
            if (!Win32.OpenClipboard(Handle)) return ReadResult.Busy;
            try
            {
                if (Available(fmtHistory) && ReadDword(fmtHistory) == 0)
                {
                    isPrivate = true;
                    return ReadResult.Private;
                }
                if (Win32.IsClipboardFormatAvailable(Win32.CF_HDROP)) item = ReadFiles();
                else if (Win32.IsClipboardFormatAvailable(Win32.CF_UNICODETEXT)) item = ReadText();
                else if (Win32.IsClipboardFormatAvailable(Win32.CF_DIB)) item = ReadImage(Win32.CF_DIB);
                else if (Win32.IsClipboardFormatAvailable(Win32.CF_DIBV5)) item = ReadImage(Win32.CF_DIBV5);
            }
            finally
            {
                Win32.CloseClipboard();
            }
            if (item != null) item.Time = DateTime.Now;
            return ReadResult.Ok;
        }

        static bool Available(uint format)
        {
            return format != 0 && Win32.IsClipboardFormatAvailable(format);
        }

        bool OwnedByUs()
        {
            IntPtr owner = Win32.GetClipboardOwner();
            if (owner == IntPtr.Zero) return false;
            uint pid;
            Win32.GetWindowThreadProcessId(owner, out pid);
            return pid == ownPid;
        }

        static int ReadDword(uint format)
        {
            IntPtr h = Win32.GetClipboardData(format);
            if (h == IntPtr.Zero) return -1;
            IntPtr p = Win32.GlobalLock(h);
            if (p == IntPtr.Zero) return -1;
            try
            {
                return (ulong)Win32.GlobalSize(h) >= 4 ? Marshal.ReadInt32(p) : -1;
            }
            finally
            {
                Win32.GlobalUnlock(h);
            }
        }

        // ---------------------------------------------------------------- formats

        static ClipItem ReadText()
        {
            IntPtr h = Win32.GetClipboardData(Win32.CF_UNICODETEXT);
            if (h == IntPtr.Zero) return null;
            IntPtr p = Win32.GlobalLock(h);
            if (p == IntPtr.Zero) return null;
            string s;
            bool truncated = false;
            long capacity = (long)(ulong)Win32.GlobalSize(h) / 2;
            try
            {
                char* c = (char*)p;
                long n = 0, max = Math.Min(capacity, MaxTextChars);
                while (n < max && c[n] != '\0') n++;
                if (n == max && capacity > MaxTextChars && c[n] != '\0') truncated = true;
                if (truncated && n > 0 && char.IsHighSurrogate(c[n - 1])) n--;
                s = new string(c, 0, (int)n);
            }
            finally
            {
                Win32.GlobalUnlock(h);
            }
            return s.Length == 0 ? null : ClipItem.FromText(s, truncated);
        }

        static ClipItem ReadFiles()
        {
            IntPtr drop = Win32.GetClipboardData(Win32.CF_HDROP);
            if (drop == IntPtr.Zero) return null;
            uint count = Win32.DragQueryFile(drop, 0xFFFFFFFF, null, 0);
            var files = new List<string>();
            var sb = new StringBuilder(260);
            for (uint i = 0; i < count && files.Count < MaxFiles; i++)
            {
                uint len = Win32.DragQueryFile(drop, i, null, 0);
                if (len == 0) continue;
                sb.Length = 0;
                sb.EnsureCapacity((int)len + 1);
                if (Win32.DragQueryFile(drop, i, sb, len + 1) > 0) files.Add(sb.ToString());
            }
            return files.Count == 0 ? null : ClipItem.FromFiles(files.ToArray());
        }

        /// <summary>A copied picture: its size and a small preview. The full image is never copied.</summary>
        static ClipItem ReadImage(uint format)
        {
            IntPtr h = Win32.GetClipboardData(format);
            if (h == IntPtr.Zero) return null;
            IntPtr p = Win32.GlobalLock(h);
            if (p == IntPtr.Zero) return null;
            DibInfo dib;
            try
            {
                dib = Dib.Read(p, (long)(ulong)Win32.GlobalSize(h), ThumbW, ThumbH);
            }
            finally
            {
                Win32.GlobalUnlock(h);
            }
            if (dib == null) return null;
            ClipItem item = ClipItem.FromImage(dib);
            if (dib.Thumb != null) item.Thumbnail = ToBitmap(dib);
            return item;
        }

        static Bitmap ToBitmap(DibInfo dib)
        {
            var bmp = new Bitmap(dib.ThumbWidth, dib.ThumbHeight, PixelFormat.Format32bppRgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, dib.ThumbWidth, dib.ThumbHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                for (int y = 0; y < dib.ThumbHeight; y++)
                    Marshal.Copy((int[])(object)dib.Thumb, y * dib.ThumbWidth, data.Scan0 + y * data.Stride, dib.ThumbWidth);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return bmp;
        }

        public void Dispose()
        {
            settle.Dispose();
            if (Handle != IntPtr.Zero)
            {
                if (listening) Win32.RemoveClipboardFormatListener(Handle);
                DestroyHandle();
            }
        }
    }
}
