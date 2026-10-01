using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Aly.App
{
    /// <summary>
    /// The link to the other pet on the same desktop (two pets that name each other as partner). It finds the other
    /// pet's window by its fixed caption ("DesktopPet:" + id, the same whatever the user named
    /// it) and posts it small registered window messages when something happens: hello and
    /// bye, 防干扰 and pomodoro changes, a blow-up, a visit, a reminder, a new spot after a stroll.
    /// Nothing is sent while nothing happens, and nothing is polled.
    /// Message: wParam = code in the low 8 bits, a signed 24-bit argument above; lParam = sender.
    /// </summary>
    sealed class Partner
    {
        public enum Code
        {
            Hello = 1, HelloBack = 2, Bye = 3,
            Calm = 4,       // arg: its own 防干扰 style (0 = off; on by hand or by its own pomodoro)
            Focus = 5,      // arg: 1 = a pomodoro is running
            Exploded = 6,
            VisitAsk = 7,   // arg: the visitor's feet x (screen px)
            VisitOk = 8,    // arg: the host's feet x
            VisitNo = 9,    // arg: 1 = asleep
            Arrived = 10,   // arg: the visitor's feet x
            Reminder = 11,  // arg: 1 water, 2 sit, 3 bedtime
            Spot = 12,      // arg: where it has settled, feet x (screen px), so strolls keep clear of it
            CalmInfo = 13,  // arg: bit 0 it keeps an eye on distractions, bits 1-2 its style, bits 3-4 its peek edge
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindow(string cls, string caption);
        [DllImport("user32.dll")]
        static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);

        public static readonly int Message = RegisterWindowMessage("DesktopPets.Partner.v1");

        readonly IntPtr own;
        readonly string caption; // the other pet's window caption; null = this pet has no partner
        IntPtr other;

        public event Action<Code, int> Received;

        public Partner(IntPtr ownWindow, string partnerId)
        {
            own = ownWindow;
            caption = string.IsNullOrEmpty(partnerId) ? null : "DesktopPet:" + partnerId;
        }

        public bool Present
        {
            get { return other != IntPtr.Zero && IsWindow(other); }
        }

        /// <summary>Looks for the other pet and says hello (at start-up; it answers HelloBack).</summary>
        public void Announce(int arg)
        {
            if (caption == null || Message == 0) return;
            IntPtr w = FindWindow(null, caption);
            if (w == IntPtr.Zero) return;
            other = w;
            Send(Code.Hello, arg);
        }

        public void Send(Code code, int arg)
        {
            if (!Present)
            {
                other = IntPtr.Zero;
                return;
            }
            int wp = (int)code | (arg << 8);
            PostMessage(other, Message, new IntPtr(wp), own);
        }

        /// <summary>From PetWindow.PartnerMessage.</summary>
        public void Handle(long wParam, IntPtr from)
        {
            if (caption == null || from == IntPtr.Zero || !IsPartnerWindow(from)) return; // only the other pet
            int wp = unchecked((int)wParam);
            var code = (Code)(wp & 0xFF);
            int arg = wp >> 8; // arithmetic shift: the argument's sign comes along
            other = code == Code.Bye ? IntPtr.Zero : from;
            if (Received != null) Received(code, arg);
        }

        bool IsPartnerWindow(IntPtr w)
        {
            var sb = new StringBuilder(64);
            GetWindowText(w, sb, sb.Capacity);
            return sb.ToString() == caption;
        }
    }
}
