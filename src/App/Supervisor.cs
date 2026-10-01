using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Aly.App
{
    /// <summary>
    /// 专注监督's eyes: tells the app when the foreground window changes (an out-of-context
    /// WinEvent hook — no polling, nothing injected into other programs), and reads the
    /// foreground window's program name and title on request. Hooked only while supervising.
    /// Nothing it sees is stored or sent anywhere; the app only matches it against the list.
    /// </summary>
    sealed class Supervisor : IDisposable
    {
        const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;

        delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);
        [DllImport("user32.dll")]
        static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);

        readonly WinEventProc proc; // kept alive as long as the hook
        readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;
        IntPtr hook;
        uint cachedPid;
        string cachedName = "";

        /// <summary>The foreground window changed (raised on the UI thread).</summary>
        public event Action Changed;

        public Supervisor()
        {
            proc = OnEvent;
        }

        public bool Enabled { get { return hook != IntPtr.Zero; } }

        public void Enable(bool on)
        {
            if (on && hook == IntPtr.Zero)
                hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, proc, 0, 0,
                    WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            else if (!on && hook != IntPtr.Zero)
            {
                UnhookWinEvent(hook);
                hook = IntPtr.Zero;
            }
        }

        void OnEvent(IntPtr h, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (Changed != null) Changed();
        }

        /// <summary>The foreground window's program name (without .exe) and title; ours = one of this pet's own windows.</summary>
        public void Foreground(out string program, out string title, out bool ours)
        {
            program = "";
            title = "";
            ours = false;
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == ownPid)
            {
                ours = true;
                return;
            }
            var sb = new StringBuilder(512);
            GetWindowText(hwnd, sb, sb.Capacity);
            title = sb.ToString();
            if (pid != cachedPid)
            {
                cachedPid = pid;
                try
                {
                    using (Process p = Process.GetProcessById((int)pid)) cachedName = p.ProcessName;
                }
                catch (Exception)
                {
                    cachedName = "";
                }
            }
            program = cachedName;
        }

        public void Dispose()
        {
            Enable(false);
        }
    }
}
