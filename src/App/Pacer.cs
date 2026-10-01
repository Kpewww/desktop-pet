using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Aly.App
{
    /// <summary>
    /// The one clock that wakes the pet window, from a background thread that posts WM_TICK:
    /// <list type="bullet">
    /// <item>Continuous (walking, falling): ticks in step with the display refresh, about 60
    /// times a second even on 120/144/240 Hz monitors — she moves less than a pixel per
    /// refresh, so more would only cost power. FullRate (dragged, thrown) ticks on every
    /// refresh, where it does make the motion smoother.</item>
    /// <item>Otherwise one precise wake at a requested time (next animation frame, next
    /// decision), from a high-resolution waitable timer: exact frame timing without ever
    /// raising the system timer resolution (no timeBeginPeriod).</item>
    /// </list>
    /// Between wakes the thread is blocked in the kernel and costs nothing.
    /// </summary>
    sealed class Pacer : IDisposable
    {
        const double TargetMs = 1000.0 / 60;
        const uint INFINITE = 0xFFFFFFFF, WAIT_OBJECT_0 = 0;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string name, uint flags, uint access);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr completion, IntPtr arg, bool resume);
        [DllImport("kernel32.dll")]
        static extern bool CancelWaitableTimer(IntPtr timer);
        [DllImport("kernel32.dll")]
        static extern uint WaitForMultipleObjects(uint count, IntPtr[] handles, bool waitAll, uint milliseconds);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);

        readonly IntPtr target;
        readonly Thread thread;
        readonly AutoResetEvent signal = new AutoResetEvent(false);
        readonly IntPtr wakeTimer;  // one-shot wakes, armed by the UI thread
        readonly IntPtr napTimer;   // the pacer's own short naps between refreshes
        readonly Stopwatch clock = Stopwatch.StartNew();
        volatile bool running;
        volatile bool fullRate;
        volatile bool disposed;
        int pending;

        public Pacer(IntPtr targetWindow)
        {
            target = targetWindow;
            wakeTimer = CreateTimer();
            napTimer = CreateTimer();
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "pacer";
            thread.Start();
        }

        static IntPtr CreateTimer()
        {
            const uint HighResolution = 0x2, AllAccess = 0x1F0003;
            IntPtr t = CreateWaitableTimerExW(IntPtr.Zero, null, HighResolution, AllAccess);
            if (t == IntPtr.Zero) t = CreateWaitableTimerExW(IntPtr.Zero, null, 0, AllAccess); // before Windows 10 1803
            if (t == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return t;
        }

        public bool Running { get { return running; } }

        /// <summary>Tick on every refresh instead of ~60 Hz (dragged, flying through the air).</summary>
        public bool FullRate
        {
            get { return fullRate; }
            set { fullRate = value; }
        }

        /// <summary>Continuous ticks until Stop.</summary>
        public void Start()
        {
            if (running) return;
            CancelWaitableTimer(wakeTimer);
            running = true;
            signal.Set();
        }

        public void Stop()
        {
            if (!running) return;
            running = false;
            signal.Set();
        }

        /// <summary>One tick after this many seconds, replacing any earlier request (not while running).</summary>
        public void WakeIn(double seconds)
        {
            long due = -Math.Max(1, (long)(seconds * 1e7)); // relative, in 100 ns units
            SetWaitableTimer(wakeTimer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false);
        }

        public void CancelWake()
        {
            CancelWaitableTimer(wakeTimer);
        }

        /// <summary>Called by the UI thread once it has handled a tick.</summary>
        public void TickConsumed()
        {
            Interlocked.Exchange(ref pending, 0);
        }

        void Post()
        {
            if (Interlocked.CompareExchange(ref pending, 1, 0) != 0) return;
            if (!Win32.PostMessage(target, PetWindow.WM_TICK, IntPtr.Zero, IntPtr.Zero)) Interlocked.Exchange(ref pending, 0);
        }

        void Loop()
        {
            var vblank = new VBlank();
            IntPtr signalHandle = signal.SafeWaitHandle.DangerousGetHandle();
            var idle = new[] { wakeTimer, signalHandle };
            var nap = new[] { napTimer, signalHandle };
            double lastTick = double.NegativeInfinity;
            double period = TargetMs, periodAt = double.NegativeInfinity; // refresh interval, measured now and then
            int instant = 0; // refresh waits that returned at once: no working vblank source
            while (!disposed)
            {
                if (!running)
                {
                    uint r = WaitForMultipleObjects(2, idle, false, INFINITE);
                    if (!disposed && r == WAIT_OBJECT_0 && !running) Post();
                    continue;
                }

                double now = clock.Elapsed.TotalMilliseconds;
                if (instant < 3 && now - periodAt > 5000)
                {
                    // Two back-to-back waits give the refresh interval (60, 144, 240 Hz…).
                    bool a = vblank.Wait();
                    double ta = clock.Elapsed.TotalMilliseconds;
                    bool b = vblank.Wait();
                    now = clock.Elapsed.TotalMilliseconds;
                    double d = now - ta;
                    period = a && b && d > 2 && d < 50 ? d : TargetMs;
                    periodAt = now;
                }

                // Nap until shortly before the refresh this tick belongs to, then catch that refresh.
                double napMs = fullRate ? 0 : lastTick + TargetMs - period * 0.75 - now;
                if (napMs > 1 && !Nap(nap, napMs)) continue; // Start/Stop/Dispose: look again
                double t0 = clock.Elapsed.TotalMilliseconds;
                bool ok = instant < 3 && vblank.Wait();
                double t1 = clock.Elapsed.TotalMilliseconds;
                instant = ok && t1 - t0 >= 0.25 ? 0 : Math.Min(instant + 1, 50);
                if (instant >= 3)
                {
                    // Display off, remote session…: plain ~60 Hz from the timer alone.
                    double rest = lastTick + TargetMs - t1;
                    if (rest > 1 && !Nap(nap, rest)) continue;
                    if (instant == 50) instant = 0; // try the display again now and then
                    t1 = clock.Elapsed.TotalMilliseconds;
                }
                else if (!fullRate && t1 - lastTick < TargetMs - period * 0.5)
                {
                    continue; // that refresh came early; the tick belongs to a later one
                }
                lastTick = t1;
                if (running) Post();
            }
            vblank.Dispose();
        }

        /// <summary>Sleeps on the high-resolution timer; false if woken early by Start/Stop/Dispose.</summary>
        bool Nap(IntPtr[] handles, double ms)
        {
            long due = -(long)(ms * 1e4);
            SetWaitableTimer(napTimer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false);
            if (WaitForMultipleObjects(2, handles, false, INFINITE) == WAIT_OBJECT_0) return true;
            CancelWaitableTimer(napTimer);
            return false;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            running = false;
            signal.Set();
            thread.Join(200);
            signal.Close();
            CloseHandle(wakeTimer);
            CloseHandle(napTimer);
        }

        /// <summary>
        /// Waits for the primary display's vertical blank with the kernel's own event
        /// (D3DKMTWaitForVerticalBlankEvent) — a few times cheaper than DwmFlush, which is the
        /// fallback when the adapter can't be opened.
        /// </summary>
        sealed class VBlank : IDisposable
        {
            [StructLayout(LayoutKind.Sequential)]
            struct OpenAdapterFromHdc
            {
                public IntPtr Hdc;
                public uint Adapter;
                public uint LuidLow;
                public int LuidHigh;
                public uint VidPnSourceId;
            }

            [StructLayout(LayoutKind.Sequential)]
            struct WaitForVerticalBlankEvent
            {
                public uint Adapter;
                public uint Device;
                public uint VidPnSourceId;
            }

            [StructLayout(LayoutKind.Sequential)]
            struct CloseAdapter
            {
                public uint Adapter;
            }

            [DllImport("gdi32.dll")] static extern int D3DKMTOpenAdapterFromHdc(ref OpenAdapterFromHdc args);
            [DllImport("gdi32.dll")] static extern int D3DKMTWaitForVerticalBlankEvent(ref WaitForVerticalBlankEvent args);
            [DllImport("gdi32.dll")] static extern int D3DKMTCloseAdapter(ref CloseAdapter args);

            WaitForVerticalBlankEvent wait;
            bool open;
            int failures;

            public VBlank()
            {
                Open();
            }

            void Open()
            {
                IntPtr dc = Win32.GetDC(IntPtr.Zero);
                try
                {
                    var args = new OpenAdapterFromHdc();
                    args.Hdc = dc;
                    if (D3DKMTOpenAdapterFromHdc(ref args) == 0)
                    {
                        wait.Adapter = args.Adapter;
                        wait.VidPnSourceId = args.VidPnSourceId;
                        open = true;
                    }
                }
                catch (EntryPointNotFoundException) { }
                finally
                {
                    Win32.ReleaseDC(IntPtr.Zero, dc);
                }
            }

            public bool Wait()
            {
                if (open)
                {
                    if (D3DKMTWaitForVerticalBlankEvent(ref wait) == 0) return true;
                    Close(); // the adapter went away (driver update, display change): reopen later
                    failures++;
                }
                else if (failures < 100 && failures++ % 10 == 0)
                {
                    Open();
                }
                return Win32.DwmFlush() == 0;
            }

            void Close()
            {
                if (!open) return;
                var args = new CloseAdapter();
                args.Adapter = wait.Adapter;
                D3DKMTCloseAdapter(ref args);
                open = false;
            }

            public void Dispose()
            {
                Close();
            }
        }
    }
}
