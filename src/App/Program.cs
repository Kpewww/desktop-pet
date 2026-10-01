using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Aly.Core;

namespace Aly.App
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Character me;
            try
            {
                me = Profile.Me;
            }
            catch (Exception ex)
            {
                MessageBox.Show("程序文件损坏，请重新下载。\n\n" + ex.Message, "桌宠", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (args.Length >= 2 && args[0] == "--snapshot")
            {
                // --snapshot dir [scale] [look] [plain] [outfit=id]
                string look = me.Look, outfit = null;
                bool plain = false;
                for (int i = 3; i < args.Length; i++)
                {
                    if (args[i] == "plain") plain = true;
                    else if (args[i].StartsWith("outfit=")) outfit = args[i].Substring(7);
                    else look = args[i];
                }
                Snapshot.Run(args[1], args.Length >= 3 ? int.Parse(args[2]) : 2, look, plain, outfit);
                return;
            }
            bool created;
            using (var mutex = new Mutex(true, @"Local\" + me.Id + ".SingleInstance", out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Crash.Report(e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Crash.Report(e.ExceptionObject as Exception);
                };

                PetApp app;
                try
                {
                    app = new PetApp();
                    // Developer check: --demo-sign "text" ["text"…] holds each up in turn, clipboard untouched.
                    if (args.Length >= 2 && args[0] == "--demo-sign")
                    {
                        var texts = new string[args.Length - 1];
                        Array.Copy(args, 1, texts, 0, texts.Length);
                        app.DemoSigns(texts);
                    }
                    // Developer check: --demo-walk keeps her walking, for measuring the cost of moving.
                    if (args.Length >= 1 && args[0] == "--demo-walk") app.DemoWalk(args.Length >= 2 && args[1] == "steps");
                    // Developer check: --demo-nest opens 小窝 with made-up history (the clipboard is left alone).
                    if (args.Length >= 1 && args[0] == "--demo-nest") app.DemoNest(args.Length >= 2 ? args[1] : null);
                    // Developer check: --demo-focus runs an 8-second pomodoro (work pose, stretch, "done" note).
                    if (args.Length >= 1 && args[0] == "--demo-focus") app.DemoFocus();
                    // Developer check: --demo-calm [peek|quiet|hidden] [left|right] turns 防干扰模式 on.
                    if (args.Length >= 1 && args[0] == "--demo-calm")
                        app.DemoCalm(args.Length >= 2 ? args[1] : "peek", args.Length >= 3 ? args[2] : null);
                    // Developer check: --demo-distract "title" pretends that window is in front (专注监督).
                    if (args.Length >= 2 && args[0] == "--demo-distract") app.DemoDistract(args[1]);
                    // Developer check: --demo-watch pretends a video is in front (陪看), remarks every ~10 s.
                    if (args.Length >= 1 && args[0] == "--demo-watch") app.DemoWatch();
                }
                catch (Exception ex)
                {
                    Crash.Report(ex);
                    return;
                }
                // Lets build scripts (and a future installer) ask her to quit and save properly.
                using (var quit = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + me.Id + ".Quit"))
                using (app)
                {
                    quit.Reset(); // a request left over from before she started is not for her
                    SynchronizationContext ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
                    RegisteredWaitHandle wait = ThreadPool.RegisterWaitForSingleObject(quit,
                        delegate { ui.Post(delegate { app.Exit(); }, null); }, null, Timeout.Infinite, true);
                    Application.Run(app);
                    wait.Unregister(null);
                }
            }
        }
    }

    static class Crash
    {
        static int reported;

        public static void Report(Exception ex)
        {
            if (Interlocked.Exchange(ref reported, 1) != 0) return;
            if (PetApp.Current != null) PetApp.Current.EmergencyCleanup();
            string path = null;
            try
            {
                Directory.CreateDirectory(Store.Dir);
                path = Path.Combine(Store.Dir, "crash.log");
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + ex + "\r\n\r\n");
            }
            catch (Exception)
            {
                path = null;
            }
            string name = Profile.SafeName;
            string msg = name + " 遇到了问题，需要退出。\n\n" + (ex == null ? "" : ex.Message);
            if (path != null) msg += "\n\n详细信息已写入：\n" + path;
            MessageBox.Show(msg, name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.Exit(1);
        }
    }
}
