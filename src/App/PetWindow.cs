using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Aly.App
{
    /// <summary>
    /// Per-pixel-alpha layered popup. UpdateLayeredWindow moves and repaints it in one
    /// atomic call; pixels with alpha 0 let clicks through to whatever is underneath.
    /// Never takes focus, never shows in the taskbar or Alt+Tab.
    /// </summary>
    sealed class PetWindow : NativeWindow, IDisposable
    {
        public const int WM_TICK = Win32.WM_APP + 1;

        public event Action<int, int> LeftDown;
        public event Action<int, int> MouseMoved;
        public event Action<int, int> LeftUp;
        public event Action<int, int> RightUp;
        public event Action MouseLeft;
        public event Action CaptureLost;
        public event Action VsyncTick;
        public event Action DisplayChanged;
        /// <summary>A message from the other pet's window (wParam, sender window).</summary>
        public event Action<long, IntPtr> PartnerMessage;

        IntPtr screenDC;
        IntPtr memDC;
        IntPtr dib;
        IntPtr oldBitmap;
        IntPtr bits;
        int bufW, bufH;
        readonly IntPtr handCursor;
        bool trackingLeave;
        bool leftDown;
        bool visible;
        int winX, winY;

        public PetWindow()
        {
            handCursor = Win32.LoadCursor(IntPtr.Zero, new IntPtr(Win32.IDC_HAND));
            var cp = new CreateParams();
            cp.Caption = Profile.WindowCaption; // the other pet finds this window by its caption
            cp.Style = Win32.WS_POPUP;
            cp.ExStyle = Win32.WS_EX_LAYERED | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOPMOST;
            cp.X = -32000;
            cp.Y = -32000;
            cp.Width = 1;
            cp.Height = 1;
            CreateHandle(cp);
            screenDC = Win32.GetDC(IntPtr.Zero);
            memDC = Win32.CreateCompatibleDC(screenDC);
        }

        public IntPtr Bits { get { return bits; } }
        /// <summary>Row stride of the back buffer, in pixels.</summary>
        public int Stride { get { return bufW; } }
        public bool Visible { get { return visible; } }
        public bool LeftButtonDown { get { return leftDown; } }

        public void EnsureBuffer(int w, int h)
        {
            if (dib != IntPtr.Zero && w <= bufW && h <= bufH) return;
            int nw = Math.Max(w, bufW);
            int nh = Math.Max(h, bufH);
            FreeBuffer();
            var bmi = new Win32.BITMAPINFOHEADER();
            bmi.biSize = Marshal.SizeOf(typeof(Win32.BITMAPINFOHEADER));
            bmi.biWidth = nw;
            bmi.biHeight = -nh; // top-down rows
            bmi.biPlanes = 1;
            bmi.biBitCount = 32;
            dib = Win32.CreateDIBSection(memDC, ref bmi, 0, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            oldBitmap = Win32.SelectObject(memDC, dib);
            bufW = nw;
            bufH = nh;
        }

        /// <summary>The whole window's opacity (安静陪伴 can make her see-through); applied on the next Present.</summary>
        public byte Opacity = 255;

        /// <summary>Moves the window to (x, y) and shows the top-left w×h of the back buffer.</summary>
        public void Present(int x, int y, int w, int h)
        {
            var dst = new Win32.POINT(x, y);
            var size = new Win32.SIZE(w, h);
            var src = new Win32.POINT(0, 0);
            var blend = new Win32.BLENDFUNCTION();
            blend.BlendOp = Win32.AC_SRC_OVER;
            blend.SourceConstantAlpha = Opacity;
            blend.AlphaFormat = Win32.AC_SRC_ALPHA;
            Win32.UpdateLayeredWindow(Handle, screenDC, ref dst, ref size, memDC, ref src, 0, ref blend, Win32.ULW_ALPHA);
            winX = x;
            winY = y;
            if (!visible)
            {
                Win32.ShowWindow(Handle, Win32.SW_SHOWNOACTIVATE);
                visible = true;
            }
        }

        /// <summary>Moves without touching the pixels — far cheaper than a full Present.</summary>
        public void Move(int x, int y)
        {
            if (x == winX && y == winY) return;
            var dst = new Win32.POINT(x, y);
            if (!Win32.UpdateLayeredWindowPosition(Handle, IntPtr.Zero, ref dst, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero, 0))
            {
                Win32.SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER
                    | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER | Win32.SWP_NOSENDCHANGING);
            }
            winX = x;
            winY = y;
        }

        public void Hide()
        {
            if (!visible) return;
            if (leftDown) Win32.ReleaseCapture();
            Win32.ShowWindow(Handle, Win32.SW_HIDE);
            visible = false;
        }

        /// <summary>Lets go of a drag in progress (she bit free).</summary>
        public void ReleaseMouse()
        {
            if (leftDown) Win32.ReleaseCapture();
        }

        public void ReassertTopmost()
        {
            if (!visible) return;
            Win32.SetWindowPos(Handle, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);
        }

        protected override void WndProc(ref Message m)
        {
            int sx, sy;
            if (m.Msg == Partner.Message && Partner.Message != 0)
            {
                if (PartnerMessage != null) PartnerMessage(m.WParam.ToInt64(), m.LParam);
                return;
            }
            switch (m.Msg)
            {
                case Win32.WM_MOUSEACTIVATE:
                    m.Result = new IntPtr(Win32.MA_NOACTIVATE);
                    return;
                case Win32.WM_SETCURSOR:
                    Win32.SetCursor(handCursor);
                    m.Result = new IntPtr(1);
                    return;
                case Win32.WM_LBUTTONDOWN:
                case Win32.WM_LBUTTONDBLCLK:
                    leftDown = true;
                    Win32.SetCapture(Handle);
                    ToScreen(m.LParam, out sx, out sy);
                    if (LeftDown != null) LeftDown(sx, sy);
                    return;
                case Win32.WM_MOUSEMOVE:
                    if (!trackingLeave) TrackLeave();
                    ToScreen(m.LParam, out sx, out sy);
                    if (MouseMoved != null) MouseMoved(sx, sy);
                    return;
                case Win32.WM_LBUTTONUP:
                    ToScreen(m.LParam, out sx, out sy);
                    if (leftDown)
                    {
                        leftDown = false;
                        Win32.ReleaseCapture();
                        if (LeftUp != null) LeftUp(sx, sy);
                    }
                    return;
                case Win32.WM_RBUTTONUP:
                    ToScreen(m.LParam, out sx, out sy);
                    if (RightUp != null) RightUp(sx, sy);
                    return;
                case Win32.WM_MOUSELEAVE:
                    trackingLeave = false;
                    if (MouseLeft != null) MouseLeft();
                    return;
                case Win32.WM_CAPTURECHANGED:
                    if (leftDown)
                    {
                        leftDown = false;
                        if (CaptureLost != null) CaptureLost();
                    }
                    break;
                case WM_TICK:
                    if (VsyncTick != null) VsyncTick();
                    return;
                case Win32.WM_DISPLAYCHANGE:
                case Win32.WM_DPICHANGED:
                    if (DisplayChanged != null) DisplayChanged();
                    break;
            }
            base.WndProc(ref m);
        }

        void ToScreen(IntPtr lParam, out int x, out int y)
        {
            long v = lParam.ToInt64();
            x = winX + (short)(v & 0xFFFF);
            y = winY + (short)((v >> 16) & 0xFFFF);
        }

        void TrackLeave()
        {
            var tme = new Win32.TRACKMOUSEEVENT();
            tme.cbSize = Marshal.SizeOf(typeof(Win32.TRACKMOUSEEVENT));
            tme.dwFlags = Win32.TME_LEAVE;
            tme.hwndTrack = Handle;
            trackingLeave = Win32.TrackMouseEvent(ref tme);
        }

        void FreeBuffer()
        {
            if (dib == IntPtr.Zero) return;
            Win32.SelectObject(memDC, oldBitmap);
            Win32.DeleteObject(dib);
            dib = IntPtr.Zero;
            bits = IntPtr.Zero;
            bufW = 0;
            bufH = 0;
        }

        public void Dispose()
        {
            FreeBuffer();
            if (memDC != IntPtr.Zero) { Win32.DeleteDC(memDC); memDC = IntPtr.Zero; }
            if (screenDC != IntPtr.Zero) { Win32.ReleaseDC(IntPtr.Zero, screenDC); screenDC = IntPtr.Zero; }
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }
}
