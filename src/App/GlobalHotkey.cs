using System;
using System.Windows.Forms;

namespace Aly.App
{
    /// <summary>One system-wide shortcut (RegisterHotKey) on a message-only window.</summary>
    sealed class GlobalHotkey : NativeWindow, IDisposable
    {
        const int Id = 1;
        public event Action Pressed;
        bool registered;

        public GlobalHotkey()
        {
            var cp = new CreateParams();
            cp.Parent = Win32.HWND_MESSAGE;
            CreateHandle(cp);
        }

        public bool Registered { get { return registered; } }

        /// <summary>Parses "Ctrl+Alt+V"-style text; Keys.None if it isn't a usable shortcut.</summary>
        public static Keys Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return Keys.None;
            try
            {
                var keys = (Keys)new KeysConverter().ConvertFromInvariantString(text);
                bool hasKey = (keys & Keys.KeyCode) != Keys.None && (keys & Keys.KeyCode) != Keys.ControlKey
                    && (keys & Keys.KeyCode) != Keys.Menu && (keys & Keys.KeyCode) != Keys.ShiftKey;
                return hasKey && (keys & Keys.Modifiers) != Keys.None ? keys : Keys.None;
            }
            catch (Exception)
            {
                return Keys.None;
            }
        }

        public static string Describe(Keys keys)
        {
            return new KeysConverter().ConvertToInvariantString(keys);
        }

        /// <summary>False when another program already owns the shortcut.</summary>
        public bool Register(Keys keys)
        {
            Unregister();
            if (keys == Keys.None) return false;
            uint mods = Win32.MOD_NOREPEAT;
            if ((keys & Keys.Control) != 0) mods |= Win32.MOD_CONTROL;
            if ((keys & Keys.Alt) != 0) mods |= Win32.MOD_ALT;
            if ((keys & Keys.Shift) != 0) mods |= Win32.MOD_SHIFT;
            registered = Win32.RegisterHotKey(Handle, Id, mods, (uint)(keys & Keys.KeyCode));
            return registered;
        }

        public void Unregister()
        {
            if (!registered) return;
            Win32.UnregisterHotKey(Handle, Id);
            registered = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Win32.WM_HOTKEY)
            {
                if (Pressed != null) Pressed();
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Unregister();
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }
}
