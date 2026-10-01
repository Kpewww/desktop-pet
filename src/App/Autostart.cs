using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Aly.App
{
    /// <summary>Per-user "run at sign-in" entry. The registry is the source of truth.</summary>
    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static string ValueName { get { return Profile.Me.Id; } }

        static string Command { get { return "\"" + Application.ExecutablePath + "\""; } }

        public static bool IsEnabled()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                string v = k == null ? null : k.GetValue(ValueName) as string;
                return v != null && string.Equals(v.Trim('"'), Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static void Set(bool enabled)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) k.SetValue(ValueName, Command);
                else if (k.GetValue(ValueName) != null) k.DeleteValue(ValueName);
            }
        }
    }
}
