using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Aly.Core;
using Aly.Core.Engine;
using Aly.Core.Schedule;

namespace Aly.App
{
    /// <summary>
    /// Wires the engine to the window, tray, clipboard and system. Runs entirely on the UI
    /// thread; the pacer wakes it in step with the display while she moves, and exactly at
    /// the next frame or decision while she doesn't. Hidden = no ticks at all.
    /// </summary>
    sealed class PetApp : ApplicationContext
    {
        const double HousekeepingSeconds = 5;
        const double SaveEverySeconds = 300;
        // 举牌方式
        public const int SignSticky = 0, SignTimed = 1, SignManual = 2;
        // flipping the sign over to new content: widths per 50 ms step
        const double FlipStep = 0.05;
        static readonly double[] FlipWidths = { 0.66, 0.33, 0.08, 0.33, 0.66 };

        public static PetApp Current;

        readonly Config cfg = new Config();
        Atlas atlas;
        readonly Pet pet;
        readonly PetWindow window;
        readonly Renderer renderer;
        EyeFrames eyeFrames;
        readonly PixelFont font;
        readonly Pacer pacer;
        readonly Tray tray;
        readonly Icon appIcon;
        readonly System.Windows.Forms.Timer fullscreenTimer = new System.Windows.Forms.Timer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Settings settings;
        readonly SavedState saved;
        readonly double defaultUserIdleSleep;
        readonly SynchronizationContext ui;

        // wardrobe (pets that have one): outfits recolour the palette, hair and coat pick the look
        readonly Wardrobe wardrobe;
        string pendingOutfit, pendingHair;  // the change a scene is playing out, applied at its @swap cue
        Atlas pendingAtlas;                 // the next look's atlas, loaded before the scene starts
        System.Windows.Forms.Timer autoOutfitTimer;

        // 防干扰模式 and 专注监督 (pets that have them)
        readonly Supervisor supervisor = new Supervisor();
        readonly DistractionWatch distraction = new DistractionWatch();
        SiteList distractions;
        readonly System.Windows.Forms.Timer nudgeTimer = new System.Windows.Forms.Timer();  // the next nudge is due
        readonly System.Windows.Forms.Timer titleTimer = new System.Windows.Forms.Timer();  // browser tabs change titles, not windows
        bool fullscreenOverride; // showing a nudge over a full-screen browser or video
        bool shownClipped;       // the last frame drawn was cut off at a screen edge

        // 陪看 (both pets): a video or live stream in front — the same foreground check, another list
        SiteList videos;
        readonly WatchDetector videoWatch = new WatchDetector();
        bool demoVideo;          // DemoWatch: pretend a video is in front

        // the other pet on the desktop (when two pets name each other as partner)
        Partner partner;
        bool partnerPresent;
        int partnerDuty;
        CalmStyle partnerCalm;          // its own 防干扰 style (pets without the mode follow it)
        bool partnerFocus;              // its pomodoro is running (counts for 开始专注时自动打开)
        CalmStyle sentCalm = CalmStyle.Off;
        bool sentFocus;
        int partnerInfo;                // its CalmInfo: whether it nudges, its style, its peek edge
        int sentInfo;
        int toldSpot;                   // where she last told it she stands (screen px)
        bool spotTold;

        /// <summary>What a 专注监督 note is about (the Tag of its NoteRequest).</summary>
        sealed class DistractNote
        {
            public string Site;
            public int Level;
        }

        // lines: built into the exe, plus the user's own in the 台词 folder (watched for changes)
        LineBook builtinLines;
        FileSystemWatcher linesWatch;
        readonly System.Windows.Forms.Timer linesReload = new System.Windows.Forms.Timer();

        // clipboard
        readonly ClipHistory clips = new ClipHistory();
        readonly ClipboardWatch clipWatch;
        readonly GlobalHotkey hotkey;
        readonly System.Windows.Forms.Timer clipSaveTimer = new System.Windows.Forms.Timer();
        int signAnchor;
        NestForm nest;

        // focus, reminders, to-dos: one scheduler, polled only when something can be due
        readonly Planner planner = new Planner();
        readonly List<DueEvent> dueQueue = new List<DueEvent>();
        DueEvent noteOut;                          // the reminder she is showing (or about to)
        DateTime plannerCheckUtc = DateTime.MinValue;

        DebugOverlay overlay;
        int scale;
        int uiScale = 1;
        Rectangle workArea;
        double lastTick = -1;
        double lastHousekeeping = double.NegativeInfinity;
        double lastSave;

        // what is currently on screen
        int shownFrame = -1, shownX = int.MinValue, shownY = int.MinValue;
        int shownOffX, shownOffY; // window corner relative to the canvas corner
        int shownEye = -1, shownEyeDx, shownEyeDy;
        int shownSpeech = -1;
        int shownBoard = -1;
        Bubble bubble;
        bool shaking;
        SignBoard board;
        Rectangle boardRect;      // on screen; empty while no board is up
        Rectangle[] boardButtons = new Rectangle[0]; // a note's answer buttons, on screen
        bool boardPress;          // the left button went down on the board
        int buttonPress = -1;     // …on this button
        int hotButton = -1;       // the button under the cursor

        bool hidden;      // hidden by the user
        bool suppressed;  // hidden because a full-screen app or presentation is running
        bool exiting;

        bool Showing { get { return !hidden && !suppressed && !exiting; } }

        // perf counters for the overlay
        int framesDrawn, ticksRun;
        double worstTickMs;
        TimeSpan lastCpu;
        double lastPerfTime;
        Process process;

        public PetApp()
        {
            Current = this;
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            settings = Store.Load<Settings>("settings.json") ?? new Settings();
            saved = Store.Load<SavedState>("state.json") ?? new SavedState();
            if (settings.Name != null && settings.Name.Trim().Length == 0) settings.Name = null;
            Profile.NameOverride = settings.Name == null ? null : settings.Name.Trim();
            if (settings.SignMode < 0 || settings.SignMode > 2)
            {
                // from before 举牌方式 existed: "off" and "until clicked" map onto the new choices
                settings.SignMode = !settings.ClipSign ? SignManual : settings.SignSeconds == 0 ? SignSticky : SignTimed;
            }
            if (settings.SignSeconds <= 0) settings.SignSeconds = 8;
            wardrobe = LoadWardrobe();
            // the first 1.2.0 list had the video sites on it; those are for watching along now
            if (SameList(settings.DistractList, SiteList.OldDistractionDefaults)) settings.DistractList = SiteList.DistractionDefaults;
            distractions = SiteList.Parse(settings.DistractList);
            videos = SiteList.Parse(settings.VideoList);
            distraction.Grace = Math.Max(0, settings.SuperviseGrace);

            atlas = LoadAtlas(CurrentLook, !settings.DogEars);
            foreach (string clip in Pet.RequiredClips)
            {
                if (!atlas.HasClip(clip)) throw new InvalidDataException("图集缺少动作 " + clip);
            }
            pet = new Pet(atlas, cfg, new Rng((ulong)DateTime.Now.Ticks));
            pet.DoubleClickSeconds = SystemInformation.DoubleClickTime / 1000.0;
            pet.Foods = Profile.Me.Foods.ToArray();
            pet.SelfFood = Profile.Me.SelfFood;
            pet.Name = Profile.Name;
            pet.Lines = LoadLines();
            linesReload.Interval = 600; // a burst of file events settles into one re-read
            linesReload.Tick += delegate { ReloadLines(); };
            WatchLinesFolder();
            pet.Quiet = settings.Quiet;
            pet.Silly = Math.Max(0, Math.Min(2, settings.Silly));
            pet.Activity = Math.Max(0, Math.Min(2, settings.Activity));
            pet.Chatty = Math.Max(0, Math.Min(2, settings.Chatty));
            pet.Gaze.Enabled = settings.FollowCursor;
            pet.SignSeconds = settings.SignSeconds >= 0 ? settings.SignSeconds : 8;
            pet.LocalHour = DateTime.Now.TimeOfDay.TotalHours;
            defaultUserIdleSleep = cfg.UserIdleSleep;
            RestoreStats();

            renderer = new Renderer(atlas);
            if (CurrentOutfit != null) renderer.UsePalette(CurrentOutfit.Apply(atlas.Palette));
            pet.CueAction = OnSceneCue;
            eyeFrames = new EyeFrames(atlas);
            font = LoadFont();
            signAnchor = atlas.AnchorIndex("prop.sign");

            window = new PetWindow();
            window.LeftDown += OnLeftDown;
            window.MouseMoved += OnMouseMoved;
            window.LeftUp += OnLeftUp;
            window.CaptureLost += OnCaptureLost;
            window.MouseLeft += delegate { pet.PointerLeave(); SetHotButton(-1); };
            window.RightUp += delegate { tray.ShowMenu(); };
            window.VsyncTick += delegate { pacer.TickConsumed(); Tick(); };
            window.DisplayChanged += OnDisplayChanged;

            pacer = new Pacer(window.Handle);

            appIcon = LoadIcon();
            tray = new Tray(this, appIcon);

            clips.Paused = settings.ClipPaused;
            clips.Removed = DisposeThumbnail;
            if (settings.ClipPersist) clips.Restore(ClipVault.Load());
            clipSaveTimer.Interval = 3000;
            clipSaveTimer.Tick += delegate { clipSaveTimer.Stop(); SaveClips(); };
            clipWatch = new ClipboardWatch();
            clipWatch.Copied += OnCopied;
            hotkey = new GlobalHotkey();
            hotkey.Pressed += OnHotkey;
            ApplyHotkey();

            ApplyPlannerSettings();
            RestorePlanner();

            PlaceInitially();
            hidden = saved.Hidden;
            if (settings.Debug) ShowOverlay();

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.SessionEnding += OnSessionEnding;
            SystemEvents.SessionSwitch += OnSessionSwitch;

            suppressed = FullscreenBusy();
            fullscreenTimer.Interval = 3000;
            fullscreenTimer.Tick += delegate { CheckFullscreen(); };
            fullscreenTimer.Start();

            // 防干扰模式 / 专注监督
            supervisor.Changed += delegate
            {
                if (exiting) return;
                CheckDistraction();
                CheckWatching();
            };
            nudgeTimer.Tick += delegate { nudgeTimer.Stop(); CheckDistraction(); };
            titleTimer.Interval = 5000; // only runs while supervising
            titleTimer.Tick += delegate { CheckDistraction(); };

            // the other pet, if it is running too
            partner = new Partner(window.Handle, Profile.Me.Partner);
            window.PartnerMessage += partner.Handle;
            partner.Received += OnPartner;
            pet.Exploded = delegate { if (partnerPresent) partner.Send(Partner.Code.Exploded, 0); };
            pet.VisitRequested = delegate(Pet p)
            {
                if (partnerPresent && Showing && !demo) partner.Send(Partner.Code.VisitAsk, (int)Math.Round(p.Body.X * scale));
            };
            ApplyCalm();

            constructed = true;
            GreetOnStart();
            Tick();
            // say hello to the other pet once settled (demo runs, which start right after this, don't)
            var hello = new System.Windows.Forms.Timer();
            hello.Interval = 1500;
            hello.Tick += delegate
            {
                hello.Dispose();
                if (!demo && !exiting) partner.Announce(HelloArg);
            };
            hello.Start();

            if (settings.SignMode == SignSticky)
            {
                // 常驻显示: after saying hello, hold up what is already on the clipboard
                var later = new System.Windows.Forms.Timer();
                later.Interval = 2500;
                later.Tick += delegate
                {
                    later.Dispose();
                    if (exiting || settings.SignMode != SignSticky || pet.StickySign != null) return;
                    string text = ClipboardSignText(false);
                    if (text == null) return;
                    Tick();
                    pet.SetStickySign(text);
                    Tick();
                };
                later.Start();
            }
        }

        // ---------------------------------------------------------------- public API (tray)

        public bool PetVisible { get { return !hidden; } }
        public int ScaleSetting { get { return settings.Scale; } }
        public bool OverlayOn { get { return overlay != null; } }
        public bool HideInFullscreen { get { return settings.HideInFullscreen; } }
        public bool Quiet { get { return settings.Quiet; } }
        public bool FollowCursor { get { return settings.FollowCursor; } }
        public bool TimeFast { get { return pet.TimeScale > 1; } }
        public bool ShortIdle { get { return cfg.UserIdleSleep < defaultUserIdleSleep; } }
        public int SillyLevel { get { return settings.Silly; } }
        public bool Sleeping { get { return pet.State == pet.Sleep; } }

        public void Feed(string kind)
        {
            if (!Showing) return;
            Tick(); // bring her up to now first, so the new clip starts at its first frame
            if (!pet.Feed(kind) && pet.Stats.Fullness <= cfg.FullRefuse) pet.Say("busy");
            Tick();
        }

        public void PetHer()
        {
            if (!Showing) return;
            Tick();
            pet.PetFromMenu();
            Tick();
        }

        public void ToggleSleep()
        {
            if (!Showing) return;
            Tick();
            pet.ToggleSleep();
            Tick();
        }

        // ---------------------------------------------------------------- public API (clipboard)

        /// <summary>Fired whenever the history changes (the panel listens while it is open).</summary>
        public event Action ClipsChanged;

        public ClipHistory Clips { get { return clips; } }
        public int SignMode { get { return settings.SignMode; } }
        public int SignSecondsSetting { get { return settings.SignSeconds; } }
        public bool ClipPersist { get { return settings.ClipPersist; } }
        public bool ClipHotkeyOn { get { return settings.ClipHotkeyOn; } }
        public string ClipHotkeyText { get { return settings.ClipHotkey; } }

        /// <summary>看看剪贴板: she holds up whatever is on the clipboard right now.</summary>
        public void ShowClipboardSign()
        {
            if (!Showing) return;
            string text = ClipboardSignText(true);
            Tick();
            if (settings.SignMode == SignSticky) pet.SetStickySign(text); // also brings a clicked-away sign back
            else pet.ShowSign(text);
            Tick();
        }

        /// <summary>What the sign would say for the clipboard as it is now (null if empty and not asked for).</summary>
        string ClipboardSignText(bool asked)
        {
            bool isPrivate;
            ClipItem now = clipWatch.ReadNow(out isPrivate);
            string text = isPrivate ? "（已隐藏）" : now != null ? now.SignText : asked ? "（剪贴板是空的）" : null;
            if (now != null) DisposeThumbnail(now); // only the text was needed
            return text;
        }

        /// <summary>
        /// 举牌方式: 常驻显示 (always holding the latest copy, with breaks), 复制时举一会儿, or
        /// 平时不显示 (only when you pick 看看剪贴板).
        /// </summary>
        public void SetSignMode(int mode)
        {
            mode = Clamp(mode, SignSticky, SignManual);
            if (settings.SignMode == mode) return;
            settings.SignMode = mode;
            settings.ClipSign = mode != SignManual;
            SaveSettings();
            Tick();
            if (mode == SignSticky)
            {
                if (Showing) pet.SetStickySign(ClipboardSignText(false));
            }
            else
            {
                pet.SetStickySign(null);
                pet.LowerSign();
            }
            Tick();
        }

        public void SetSignSeconds(int seconds)
        {
            settings.SignSeconds = seconds > 0 ? seconds : 8;
            pet.SignSeconds = settings.SignSeconds;
            if (pet.State == pet.Signing && pet.Signing.Holding && pet.Sign != null && !pet.Sign.Sticky) pet.Signing.Rearm(pet);
            SaveSettings();
            Tick();
        }

        public void SetClipPaused(bool paused)
        {
            settings.ClipPaused = paused;
            clips.Paused = paused;
            SaveSettings();
            OnClipsChanged();
        }

        public void SetClipPersist(bool persist)
        {
            if (settings.ClipPersist == persist) return;
            settings.ClipPersist = persist;
            SaveSettings();
            if (persist) SaveClips();
            else ClipVault.Delete();
            OnClipsChanged();
        }

        public void PinClip(ClipItem item, bool pinned)
        {
            clips.SetPinned(item.Id, pinned);
            OnClipsChanged();
        }

        public void RemoveClip(ClipItem item)
        {
            clips.Remove(item.Id);
            OnClipsChanged();
        }

        public void ClearClips()
        {
            clips.Clear();
            OnClipsChanged();
        }

        /// <summary>Puts an item from the history back on the clipboard. Images keep only a thumbnail, so they can't.</summary>
        public bool CopyBack(ClipItem item)
        {
            try
            {
                if (item.Kind == ClipKind.Text && !string.IsNullOrEmpty(item.Text))
                {
                    Clipboard.SetText(item.Text, TextDataFormat.UnicodeText);
                    return true;
                }
                if (item.Kind == ClipKind.Files && item.Files != null && item.Files.Length > 0)
                {
                    var files = new System.Collections.Specialized.StringCollection();
                    files.AddRange(item.Files);
                    Clipboard.SetFileDropList(files);
                    return true;
                }
            }
            catch (System.Runtime.InteropServices.ExternalException) { }
            return false;
        }

        /// <summary>Opens 小窝 on a page (or brings it forward).</summary>
        public void OpenNest(NestPage page)
        {
            OpenNest(page, false);
        }

        void OpenNest(NestPage page, bool fromHotkey)
        {
            if (nest == null || nest.IsDisposed)
            {
                nest = new NestForm(this, appIcon);
                nest.FormClosed += delegate { nest = null; };
                nest.PlaceNear(Cursor.Position);
                nest.Show();
            }
            if (nest.WindowState == FormWindowState.Minimized) nest.WindowState = FormWindowState.Normal;
            nest.CloseAfterCopy = fromHotkey;
            nest.Activate();
            nest.ShowPage(page);
        }

        /// <summary>Developer check: holds up each text in turn, 2.5 s apart, as if it had been copied.</summary>
        internal void DemoSigns(string[] texts)
        {
            BeginDemo();
            int next = 0;
            var timer = new System.Windows.Forms.Timer();
            timer.Interval = 2500;
            EventHandler step = delegate
            {
                if (next >= texts.Length || exiting)
                {
                    timer.Dispose();
                    return;
                }
                Tick();
                pet.ShowSign(ClipItem.FromText(texts[next++], false).SignText);
                Tick();
            };
            timer.Tick += step;
            step(null, EventArgs.Empty);
            timer.Start();
        }

        /// <summary>
        /// Developer check: 小窝 with made-up history items and to-dos (kept in memory only); the
        /// real clipboard is not read or touched.
        /// </summary>
        internal void DemoNest(string page)
        {
            BeginDemo();
            DateTime utc = DateTime.UtcNow;
            planner.Todos.Add("交作业（周五之前）", utc.AddHours(3), utc);
            planner.Todos.Add("给妈妈打电话", utc.AddMinutes(-5), utc.AddMinutes(-60));
            planner.Todos.Add("买冰沙的店周二休息", null, utc.AddMinutes(-30));
            Todo done = planner.Todos.Add("预约理发", null, utc.AddHours(-5));
            planner.Todos.SetDone(done.Id, true, utc.AddHours(-1));
            var samples = new List<ClipItem>();
            samples.Add(ClipItem.FromText("今天晚上吃日式烧烤！七点在老地方见～", false));
            samples.Add(ClipItem.FromText("https://github.com/TakWolf/fusion-pixel-font", false));
            samples.Add(ClipItem.FromText("Summer2026!", false));
            samples.Add(ClipItem.FromFiles(new[] { @"C:\Users\Me\Desktop\旅行照片.zip", @"C:\Users\Me\Desktop\行程.docx" }));
            var thumb = new Bitmap(160, 90);
            using (Graphics g = Graphics.FromImage(thumb))
            using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 160, 90), Color.FromArgb(255, 180, 190), Color.FromArgb(120, 140, 220), 30f))
            {
                g.FillRectangle(brush, 0, 0, 160, 90);
            }
            var img = new ClipItem();
            img.Kind = ClipKind.Image;
            img.ImageWidth = 1920;
            img.ImageHeight = 1080;
            img.Thumbnail = thumb;
            img.Signature = "demo-image";
            img.Time = DateTime.Now;
            samples.Add(img);
            samples.Add(ClipItem.FromText("第一行：会议纪要\r\n第二行：下周三交初稿\r\n第三行：记得带电脑", false));
            for (int i = 0; i < samples.Count; i++) samples[i].Time = DateTime.Now.AddMinutes(-7 * (samples.Count - i));
            foreach (ClipItem it in samples) clips.Add(it);
            clips.SetPinned(clips.Items[clips.Count - 1].Id, true);
            OnClipsChanged();
            OpenNest(page == "todos" ? NestPage.Todos : page == "focus" ? NestPage.Focus
                : page == "lines" ? NestPage.Lines : page == "wardrobe" ? NestPage.Wardrobe
                : page == "settings" ? NestPage.Settings : NestPage.Clipboard);
        }

        bool forceSteps;
        /// <summary>Developer demo running: nothing is saved, and the user counts as present.</summary>
        bool demo;

        void BeginDemo()
        {
            demo = true;
            cfg.UserIdleSleep = 1e9; // stay awake even with nobody at the keyboard
            pet.UserIdleSeconds = 0;
            if (pet.State != pet.Idle) pet.ChangeState(pet.Idle);
        }

        /// <summary>Developer check: an 8-second pomodoro, so the work pose, the stretch and the "done" note show.</summary>
        internal void DemoFocus()
        {
            BeginDemo();
            Moment now = Moment.Now;
            planner.Focus.Start(now.Plus(TimeSpan.FromSeconds(8) - TimeSpan.FromMinutes(Math.Max(1, planner.Focus.FocusMinutes))));
            plannerCheckUtc = DateTime.MinValue;
            Tick();
        }

        /// <summary>
        /// Developer check: 防干扰模式 on in the given style ("peek", "quiet", "hidden") and edge
        /// ("left", "right"; default the taskbar). Settings are changed in memory only.
        /// </summary>
        internal void DemoCalm(string style, string edge)
        {
            BeginDemo();
            QuietRemindersForDemo();
            settings.CalmOn = true;
            settings.CalmStyle = style == "quiet" ? 2 : style == "hidden" ? 3 : 1;
            settings.PeekEdge = edge == "left" ? 1 : edge == "right" ? 2 : 0;
            ApplyCalm();
        }

        /// <summary>
        /// Developer check: 专注监督 as if this were in front of you (a site name from the list, e.g.
        /// "bilibili"), with a 3-second grace and a 10-second escalation. Nothing real is opened.
        /// </summary>
        internal void DemoDistract(string title)
        {
            BeginDemo();
            QuietRemindersForDemo();
            demoForeground = title;
            distraction.Grace = 3;
            distraction.Every = 10;
            settings.CalmOn = true;
            settings.Supervise = true;
            ApplyCalm();
        }

        string demoForeground; // DemoDistract: pretend this window title is in front

        /// <summary>
        /// Developer check: 陪看 as if a video were in front — she goes to her side, sits down and
        /// watches, with a remark or a laugh every ten seconds or so. Nothing is opened or saved.
        /// </summary>
        internal void DemoWatch()
        {
            BeginDemo();
            QuietRemindersForDemo();
            demoVideo = true;
            settings.WatchAlong = true;
            cfg.WatchCommentMin = 6;
            cfg.WatchCommentMax = 14;
            videoWatch.OnAfter = 1;
            ApplyCalm();
            CheckWatching();
            var again = new System.Windows.Forms.Timer(); // housekeeping would, a few seconds later
            again.Interval = 1200;
            again.Tick += delegate
            {
                again.Dispose();
                CheckWatching();
            };
            again.Start();
            // what she does and says, for checking without looking: %TEMP%\<id>_watch_demo.txt
            string log = Path.Combine(Path.GetTempPath(), Profile.Me.Id + "_watch_demo.txt");
            File.WriteAllText(log, "");
            string seen = null;
            var watchLog = new System.Windows.Forms.Timer();
            watchLog.Interval = 250;
            watchLog.Tick += delegate
            {
                if (exiting) return;
                string now = pet.StateName + " " + pet.Anim.ClipName + (pet.Speech != null ? " 「" + pet.Speech.Text + "」" : "");
                if (now == seen) return;
                seen = now;
                File.AppendAllText(log, string.Format(CultureInfo.InvariantCulture, "{0,6:F1}s x={1:F0} {2}\r\n", pet.Time, pet.Body.X, now), Encoding.UTF8);
            };
            watchLog.Start();
        }

        /// <summary>No water / sitting / bedtime notes during a demo (in memory only), so they don't get in its way.</summary>
        void QuietRemindersForDemo()
        {
            settings.WaterOn = settings.SitOn = settings.BedtimeOn = false;
            ApplyPlannerSettings();
            dueQueue.RemoveAll(e => e.Kind == DueKind.Water || e.Kind == DueKind.Sit || e.Kind == DueKind.Bedtime);
            if (pet.Sign != null && pet.Sign.Style == SignStyle.Note) pet.DropSigns();
        }

        /// <summary>Developer check: she walks from one spot to the next without pause, logging tick rates.</summary>
        internal void DemoWalk(bool steps)
        {
            BeginDemo();
            forceSteps = steps;
            pet.PixelSteps = steps;
            var timer = new System.Windows.Forms.Timer();
            timer.Interval = 100;
            timer.Tick += delegate
            {
                if (exiting || pet.State != pet.Idle) return;
                Tick();
                pet.StartWalk();
                Tick();
            };
            timer.Start();
            string log = Path.Combine(Path.GetTempPath(), "aly_perf.txt");
            File.WriteAllText(log, "");
            var perf = new System.Windows.Forms.Timer();
            perf.Interval = 3000;
            int lastTicks = 0, lastFrames = 0;
            double lastAt = Now;
            perf.Tick += delegate
            {
                double span = Now - lastAt;
                File.AppendAllText(log, string.Format(CultureInfo.InvariantCulture,
                    "ticks/s {0:F1}  redraws/s {1:F1}  in Tick {2:F2} ms/s  worst {3:F2} ms\r\n",
                    (demoTicks - lastTicks) / span, (demoFrames - lastFrames) / span, demoTickMs / span, worstTickMs));
                lastTicks = demoTicks;
                lastFrames = demoFrames;
                demoTickMs = 0;
                lastAt = Now;
            };
            perf.Start();
        }

        // counters for the developer demos (cheap enough to keep always on)
        int demoTicks, demoFrames;
        double demoTickMs;

        void OnHotkey()
        {
            if (nest != null && !nest.IsDisposed && Form.ActiveForm == nest) nest.Close();
            else OpenNest(NestPage.Clipboard, true);
        }

        void ApplyHotkey()
        {
            hotkey.Unregister();
            if (!settings.ClipHotkeyOn) return;
            Keys keys = GlobalHotkey.Parse(settings.ClipHotkey);
            if (keys == Keys.None || !hotkey.Register(keys))
            {
                tray.Notify("快捷键没有生效", (settings.ClipHotkey ?? "") + " 已被别的程序占用了。可以在「设置」里换一个或者关掉。");
            }
        }

        void OnCopied(ClipItem item, bool madeByUs)
        {
            ClipItem stored = clips.Add(item);
            if (stored != item) DisposeThumbnail(item); // a repeat (or paused): the earlier copy stays
            OnClipsChanged();
            if (PartnerTakesDuty) return; // both pets are out: one sign is enough, and the other one holds it
            string text = item.SignText;
            if (settings.SignMode == SignSticky)
            {
                // always the newest clipboard content; she raises it herself when free
                // (not during a pomodoro or in 正经模式, and after a game she comes back holding it)
                Tick();
                pet.SetStickySign(text);
                Tick();
                return;
            }
            // No sign while she works with you, keeps out of the way or watches a video with you.
            if (settings.SignMode != SignTimed || madeByUs || settings.Quiet || !Showing || pet.Focusing || pet.Watching || pet.Calm != CalmStyle.Off) return;
            Tick();
            pet.ShowSign(text);
            Tick();
        }

        void OnClipsChanged()
        {
            if (settings.ClipPersist)
            {
                clipSaveTimer.Stop();
                clipSaveTimer.Start();
            }
            if (ClipsChanged != null) ClipsChanged();
        }

        void SaveClips()
        {
            if (demo) return;
            if (!settings.ClipPersist) return;
            try { ClipVault.Save(clips.Items); } catch (Exception) { }
        }

        static void DisposeThumbnail(ClipItem item)
        {
            var bmp = item.Thumbnail as IDisposable;
            item.Thumbnail = null;
            if (bmp != null) bmp.Dispose();
        }

        // ---------------------------------------------------------------- public API (focus, reminders, to-dos)

        /// <summary>Fired whenever the to-dos or the pomodoro change (the panel listens while it is open).</summary>
        public event Action PlannerChanged;

        public Planner Planner { get { return planner; } }
        public Settings Prefs { get { return settings; } }

        public void StartFocus()
        {
            Moment now = Moment.Now;
            Tick();
            planner.Focus.Start(now);
            dueQueue.RemoveAll(e => e.Kind == DueKind.BreakDone);
            SyncFocus(now);
            pet.SayWith("focus_start");
            PlannerTouched();
        }

        public void StopFocus()
        {
            bool wasFocusing = planner.Focus.Phase == FocusPhase.Focus;
            Tick();
            planner.Focus.Stop();
            SyncFocus(Moment.Now);
            if (wasFocusing) pet.SayWith("focus_stop");
            PlannerTouched();
        }

        public void SetFocusLengths(int focusMinutes, int breakMinutes)
        {
            settings.FocusMinutes = Clamp(focusMinutes, 1, 180);
            settings.BreakMinutes = Clamp(breakMinutes, 1, 60);
            ApplyPlannerSettings();
            SaveSettings();
            PlannerTouched();
        }

        public void SetWater(bool on, int minutes)
        {
            bool changed = settings.WaterMinutes != minutes || settings.WaterOn != on;
            settings.WaterOn = on;
            settings.WaterMinutes = Clamp(minutes, 5, 480);
            ApplyPlannerSettings();
            if (changed) planner.Health.NextWaterUtc = DateTime.MinValue; // start counting from now
            SaveSettings();
            PlannerTouched();
        }

        public void SetSit(bool on, int minutes)
        {
            settings.SitOn = on;
            settings.SitMinutes = Clamp(minutes, 10, 240);
            ApplyPlannerSettings();
            SaveSettings();
            PlannerTouched();
        }

        public void SetBedtime(bool on, int minuteOfDay)
        {
            settings.BedtimeOn = on;
            settings.BedtimeMinute = Clamp(minuteOfDay, 0, 24 * 60 - 1);
            ApplyPlannerSettings();
            planner.Health.NextBedtimeUtc = DateTime.MinValue;
            planner.Health.BedtimeLevel = 0;
            SaveSettings();
            PlannerTouched();
        }

        static int Clamp(int v, int min, int max) { return v < min ? min : v > max ? max : v; }

        public Todo AddTodo(string text, DateTime? dueUtc)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            Todo t = planner.Todos.Add(text, dueUtc, DateTime.UtcNow);
            SaveTodos();
            pet.SayWith("todo_added");
            Tick();
            PlannerTouched();
            return t;
        }

        public void SetTodoDone(int id, bool done)
        {
            planner.Todos.SetDone(id, done, DateTime.UtcNow);
            SaveTodos();
            if (done)
            {
                Tick();
                pet.AfterNote("happy", "todo_done");
                Tick();
            }
            PlannerTouched();
        }

        public void SnoozeTodo(int id)
        {
            planner.Todos.SetDue(id, DateTime.UtcNow.AddMinutes(TodoList.SnoozeMinutes));
            SaveTodos();
            PlannerTouched();
        }

        public void RemoveTodo(int id)
        {
            planner.Todos.Remove(id);
            dueQueue.RemoveAll(e => e.Kind == DueKind.Todo && e.TodoId == id);
            SaveTodos();
            PlannerTouched();
        }

        public void ClearDoneTodos()
        {
            if (planner.Todos.ClearDone() > 0) SaveTodos();
            PlannerTouched();
        }

        /// <summary>存成便签: a clipboard item becomes a to-do without a reminder.</summary>
        public bool SaveClipAsTodo(ClipItem item)
        {
            string text = item.Kind == ClipKind.Text ? item.Text
                : item.Kind == ClipKind.Files ? string.Join("\r\n", item.Files) : null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text.Length > 2000) text = text.Substring(0, 2000);
            planner.Todos.Add(text, null, DateTime.UtcNow);
            SaveTodos();
            PlannerTouched();
            return true;
        }

        /// <summary>Something changed from outside (menu, panel): save, re-check what is due, tell the panel.</summary>
        void PlannerTouched()
        {
            SaveState();
            plannerCheckUtc = DateTime.MinValue;
            Tick();
            if (PlannerChanged != null) PlannerChanged();
            tray.RefreshTip();
        }

        /// <summary>Tray tooltip: what she is up to (the pomodoro countdown).</summary>
        public string StatusLine
        {
            get
            {
                Moment now = Moment.Now;
                string name = Profile.Name;
                switch (planner.Focus.Phase)
                {
                    case FocusPhase.Focus: return name + " · 专注中，还剩 " + planner.Focus.MinutesLeft(now) + " 分钟";
                    case FocusPhase.Break: return name + " · 休息中，还剩 " + planner.Focus.MinutesLeft(now) + " 分钟";
                    default:
                        int open = planner.Todos.OpenCount;
                        if (pet.Watching) name += " · 陪你看视频中";
                        return open > 0 ? name + " · " + open + " 件待办" : name;
                }
            }
        }

        void ApplyPlannerSettings()
        {
            planner.Focus.FocusMinutes = settings.FocusMinutes;
            planner.Focus.BreakMinutes = settings.BreakMinutes;
            HealthReminders h = planner.Health;
            h.WaterOn = settings.WaterOn;
            h.WaterMinutes = settings.WaterMinutes;
            h.SitOn = settings.SitOn;
            h.SitMinutes = settings.SitMinutes;
            h.BedtimeOn = settings.BedtimeOn;
            h.BedtimeMinute = settings.BedtimeMinute;
        }

        void PollPlanner()
        {
            Moment now = Moment.Now;
            foreach (DueEvent e in planner.Poll(now, pet.UserIdleSeconds))
            {
                if (!dueQueue.Exists(q => q.Kind == e.Kind && q.TodoId == e.TodoId)) dueQueue.Add(e);
                if (e.Kind == DueKind.FocusDone || e.Kind == DueKind.BreakDone)
                {
                    SaveState();
                    if (PlannerChanged != null) PlannerChanged();
                }
            }
            SyncFocus(now);
            DeliverNext(now);
            DateTime? next = planner.NextDueUtc(now);
            // Overdue but held back (user away, quiet mode…): housekeeping looks again every few seconds.
            plannerCheckUtc = next == null ? DateTime.MaxValue : next.Value > now.Utc ? next.Value : now.Utc.AddSeconds(HousekeepingSeconds);
        }

        void SyncFocus(Moment now)
        {
            pet.Focusing = planner.Focus.Phase == FocusPhase.Focus;
            pet.FocusMinutesLeft = planner.Focus.MinutesLeft(now);
            ApplyCalm(); // 开始专注时自动打开, and out he comes for the break
        }

        // ---------------------------------------------------------------- 防干扰模式 & 专注监督

        /// <summary>This pet has 防干扰模式 (its menu and settings).</summary>
        public bool HasCalm { get { return Profile.Me.Has("calm"); } }

        /// <summary>
        /// The mode in effect now: switched on by hand, by itself during a pomodoro (hers or the
        /// other pet's), or because the other pet's is on — the two hide together, each in its own
        /// style. A pet without the mode does what the other pet does.
        /// </summary>
        public CalmStyle EffectiveCalm
        {
            get
            {
                if (!HasCalm) return partnerPresent ? partnerCalm : CalmStyle.Off;
                bool focus = planner.Focus.Phase == FocusPhase.Focus || partnerPresent && partnerFocus;
                bool on = settings.CalmOn || settings.CalmAuto && focus || partnerPresent && partnerCalm != CalmStyle.Off;
                return on ? OwnStyle : CalmStyle.Off;
            }
        }

        /// <summary>
        /// Her own mode (on by hand, or by her own pomodoro) — what she tells the other pet. Not the
        /// mode in effect: that includes following the other pet, and telling it that back would
        /// keep both on for ever.
        /// </summary>
        CalmStyle OwnCalm
        {
            get
            {
                if (!HasCalm) return CalmStyle.Off;
                bool on = settings.CalmOn || settings.CalmAuto && planner.Focus.Phase == FocusPhase.Focus;
                return on ? OwnStyle : CalmStyle.Off;
            }
        }

        CalmStyle OwnStyle { get { return (CalmStyle)Math.Max(1, Math.Min(3, settings.CalmStyle)); } }

        /// <summary>Hiding only because the other pet is (for the menu).</summary>
        public bool CalmFollowing { get { return HasCalm && EffectiveCalm != CalmStyle.Off && !settings.CalmOn && partnerPresent && partnerCalm != CalmStyle.Off; } }

        /// <summary>Both pets are on the desktop and the other one has the clipboard sign and the reminders.</summary>
        bool PartnerTakesDuty { get { return partnerPresent && partnerDuty > Profile.Me.Duty; } }

        /// <summary>Both are out and the other one keeps an eye on distractions: one nudge is enough.</summary>
        bool PartnerSupervises { get { return partnerPresent && (partnerInfo & 1) != 0 && partnerDuty > Profile.Me.Duty; } }

        /// <summary>For the other pet (Partner.Code.CalmInfo): whether she nudges, her style, her peek edge.</summary>
        int CalmInfo
        {
            get
            {
                if (!HasCalm) return 0;
                return (settings.Supervise && !hidden ? 1 : 0) | ((int)OwnStyle << 1) | (Math.Max(0, Math.Min(2, settings.PeekEdge)) << 3);
            }
        }

        /// <summary>Hello's argument: duty, own 防干扰 style, pomodoro running, and CalmInfo.</summary>
        int HelloArg
        {
            get
            {
                return (Profile.Me.Duty & 0xFF) | ((int)OwnCalm << 8)
                    | (planner.Focus.Phase == FocusPhase.Focus ? 1 << 12 : 0) | (CalmInfo << 13);
            }
        }

        void OnPartner(Partner.Code code, int arg)
        {
            if (exiting) return;
            switch (code)
            {
                case Partner.Code.Hello:
                case Partner.Code.HelloBack:
                    bool first = !partnerPresent;
                    partnerPresent = true;
                    partnerDuty = arg & 0xFF;
                    partnerCalm = (CalmStyle)Math.Max(0, Math.Min(3, (arg >> 8) & 0xF));
                    partnerFocus = ((arg >> 12) & 1) != 0;
                    partnerInfo = (arg >> 13) & 0x1F;
                    pet.PartnerPresent = true;
                    sentCalm = OwnCalm;
                    sentInfo = CalmInfo;
                    sentFocus = planner.Focus.Phase == FocusPhase.Focus;
                    spotTold = false; // it's new: tell it where she stands at the next tick
                    if (code == Partner.Code.Hello) partner.Send(Partner.Code.HelloBack, HelloArg);
                    ApplyCalm();
                    if (first && Showing)
                    {
                        Tick();
                        pet.Greet("meet");
                        Tick();
                    }
                    break;
                case Partner.Code.Bye:
                    partnerPresent = false;
                    pet.PartnerPresent = false;
                    pet.PartnerX = double.NaN;
                    partnerCalm = CalmStyle.Off;
                    partnerFocus = false;
                    partnerInfo = 0;
                    ApplyCalm();
                    break;
                case Partner.Code.Calm:
                    partnerCalm = (CalmStyle)Math.Max(0, Math.Min(3, arg));
                    ApplyCalm();
                    break;
                case Partner.Code.CalmInfo:
                    partnerInfo = arg & 0x1F;
                    ApplyCalm();
                    break;
                case Partner.Code.Focus:
                    partnerFocus = arg != 0;
                    ApplyCalm();
                    break;
                case Partner.Code.Exploded:
                    if (!Showing) break;
                    Tick();
                    pet.Defend();
                    Tick();
                    break;
                case Partner.Code.VisitAsk:
                    HostVisit(arg);
                    break;
                case Partner.Code.VisitOk:
                    GoVisit(arg);
                    break;
                case Partner.Code.VisitNo:
                    if (arg == 1 && Showing) pet.Say("partner_asleep");
                    break;
                case Partner.Code.Arrived:
                    if (!Showing) break;
                    pet.HoldUntil = pet.Time + 3;
                    Tick();
                    pet.Meet(arg / (double)scale, "visit");
                    Tick();
                    break;
                case Partner.Code.Reminder:
                    if (!Showing) break;
                    Tick();
                    pet.JoinIn(arg == 1 ? "drink_water" : arg == 2 ? "stretch" : "yawn");
                    Tick();
                    break;
                case Partner.Code.Spot:
                    pet.PartnerX = arg / (double)scale;
                    break;
            }
        }

        /// <summary>The other pet wants to come over: stay put and tell it where she stands, if free.</summary>
        void HostVisit(int visitorX)
        {
            bool free = Showing && pet.Available && pet.Calm == CalmStyle.Off && !pet.Focusing
                && visitorX >= workArea.Left && visitorX <= workArea.Right;
            if (!free)
            {
                partner.Send(Partner.Code.VisitNo, pet.State.Sleeping ? 1 : 0);
                return;
            }
            pet.HoldUntil = pet.Time + 20;
            pet.Facing = visitorX > pet.Body.X * scale ? 1 : -1;
            partner.Send(Partner.Code.VisitOk, (int)Math.Round(pet.Body.X * scale));
        }

        /// <summary>The other pet said yes: walk up next to it, then a happy hello on both sides.</summary>
        void GoVisit(int hostX)
        {
            if (!Showing || !pet.Available || pet.Calm != CalmStyle.Off || hostX < workArea.Left || hostX > workArea.Right) return;
            double hx = hostX / (double)scale;
            double gap = cfg.HalfWidth * 2 + 6;
            double target = pet.Body.X < hx ? hx - gap : hx + gap;
            target = Math.Max(pet.World.Left + cfg.HalfWidth, Math.Min(pet.World.Right - cfg.HalfWidth, target));
            Tick();
            pet.WalkTo(target, delegate(Pet p)
            {
                partner.Send(Partner.Code.Arrived, (int)Math.Round(p.Body.X * scale));
                p.Meet(hx, "visit");
            });
            Tick();
        }

        /// <summary>Tells the other pet where she has settled after moving (its strolls keep clear of her).</summary>
        void TellSpot()
        {
            if (demo || pet.Continuous || pet.State == pet.Walk) return;
            int x = (int)Math.Round(pet.Body.X * scale);
            if (spotTold && Math.Abs(x - toldSpot) < 12 * scale) return;
            spotTold = true;
            toldSpot = x;
            partner.Send(Partner.Code.Spot, x);
        }

        /// <summary>Tells the other pet when this one's own 防干扰, its settings or its pomodoro change.</summary>
        void TellPartner()
        {
            if (!partnerPresent) return;
            CalmStyle own = OwnCalm;
            if (own != sentCalm)
            {
                partner.Send(Partner.Code.Calm, (int)own);
                sentCalm = own;
            }
            int info = CalmInfo;
            if (info != sentInfo)
            {
                partner.Send(Partner.Code.CalmInfo, info);
                sentInfo = info;
            }
            bool focus = planner.Focus.Phase == FocusPhase.Focus;
            if (focus != sentFocus)
            {
                partner.Send(Partner.Code.Focus, focus ? 1 : 0);
                sentFocus = focus;
            }
        }

        public int CaughtToday
        {
            get { return saved.CaughtDay == DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ? saved.CaughtCount : 0; }
        }

        /// <summary>Fired when the mode's settings or state change (menus and pages refresh).</summary>
        public event Action CalmChanged;

        public void SetCalmOn(bool on, int style)
        {
            settings.CalmOn = on;
            if (style >= 1 && style <= 3) settings.CalmStyle = style;
            SaveSettings();
            bool was = pet.Calm != CalmStyle.Off;
            ApplyCalm();
            if (on && !was && pet.Calm != CalmStyle.Off && !pet.Focusing) pet.SayWith("calm_on");
        }

        public void SetCalmStyle(int style)
        {
            settings.CalmStyle = Math.Max(1, Math.Min(3, style));
            SaveSettings();
            ApplyCalm();
        }

        public void SetCalmAuto(bool on)
        {
            settings.CalmAuto = on;
            SaveSettings();
            ApplyCalm();
        }

        public void SetPeekEdge(int edge)
        {
            settings.PeekEdge = Math.Max(0, Math.Min(2, edge));
            SaveSettings();
            if (pet.State == pet.Calming) pet.ChangeState(pet.Idle); // go round again to the new edge
            ApplyCalm();
        }

        public void SetQuietOpacity(int percent)
        {
            settings.QuietOpacity = Math.Max(20, Math.Min(100, percent));
            SaveSettings();
            ApplyCalm();
        }

        public void SetSupervise(bool on)
        {
            settings.Supervise = on;
            SaveSettings();
            ApplyCalm();
        }

        public void SetSuperviseGrace(int seconds)
        {
            settings.SuperviseGrace = Math.Max(0, Math.Min(600, seconds));
            distraction.Grace = settings.SuperviseGrace;
            SaveSettings();
        }

        public void SetDistractList(string text)
        {
            settings.DistractList = text ?? "";
            distractions = SiteList.Parse(settings.DistractList);
            SaveSettings();
            CheckDistraction();
        }

        static bool SameList(string a, string b)
        {
            return (a ?? "").Replace("\r\n", "\n").Trim() == (b ?? "").Replace("\r\n", "\n").Trim();
        }

        // ---------------------------------------------------------------- 陪看

        public bool WatchAlong { get { return settings.WatchAlong; } }
        public bool WatchingNow { get { return pet.Watching; } }

        public void SetWatchAlong(bool on)
        {
            settings.WatchAlong = on;
            SaveSettings();
            if (!on) videoWatch.Reset();
            ApplyCalm(); // (the foreground hook stays only if something still needs it)
            CheckWatching();
        }

        public void SetVideoList(string text)
        {
            settings.VideoList = text ?? "";
            videos = SiteList.Parse(settings.VideoList);
            SaveSettings();
            CheckWatching();
        }

        /// <summary>
        /// 陪看: is a video or live stream in front? Called when the foreground window changes and
        /// every few seconds (a browser changes its title, not its window, from tab to tab). Only
        /// the program name and title are matched against the list, here and now; nothing is kept.
        /// </summary>
        void CheckWatching()
        {
            if (exiting) return;
            bool video = false;
            if (settings.WatchAlong)
            {
                string program, title;
                bool ours;
                supervisor.Foreground(out program, out title, out ours);
                if (ours && !demoVideo) return; // her own 小窝 in front: nothing has changed
                video = demoVideo || videos.Match(program, title) != null;
            }
            videoWatch.Update(video, Now);
            bool on = settings.WatchAlong && videoWatch.Watching;
            if (pet.Watching == on) return;
            pet.Watching = on;
            if (on) pet.LowerSign(); // out of the way: the clipboard sign goes down (copies still go into 小窝)
            else plannerCheckUtc = DateTime.MinValue; // the reminders that waited for the video
            tray.RefreshTip();
            if (CalmChanged != null) CalmChanged();
            if (!ticking && constructed) Tick();
        }

        bool Supervising { get { return pet.Calm != CalmStyle.Off && settings.Supervise && HasCalm && !PartnerSupervises; } }

        /// <summary>Brings the pet, the window and the supervisor in line with EffectiveCalm.</summary>
        void ApplyCalm()
        {
            CalmStyle c = EffectiveCalm;
            // Two pets hiding together must not end up on top of each other: the one with the
            // lower duty takes the spot next to the other's corner, and the other screen edge
            // when both would peek from the same side.
            pet.CalmBeside = partnerPresent && (!HasCalm || Profile.Me.Duty < partnerDuty);
            var edge = (PeekEdge)Math.Max(0, Math.Min(2, settings.PeekEdge));
            var partnerEdge = (PeekEdge)Math.Max(0, Math.Min(2, (partnerInfo >> 3) & 3));
            bool partnerPeeks = ((partnerInfo >> 1) & 3) == (int)CalmStyle.Peek;
            if (pet.CalmBeside && edge != PeekEdge.Bottom && partnerPeeks && partnerEdge == edge)
                edge = edge == PeekEdge.Left ? PeekEdge.Right : PeekEdge.Left;
            if (edge != pet.PeekEdge && pet.State == pet.Calming) pet.ChangeState(pet.Idle); // round to the new edge
            pet.PeekEdge = edge;
            bool entering = pet.Calm == CalmStyle.Off && c != CalmStyle.Off;
            pet.Calm = c;
            if (entering) pet.LowerSign(); // the clipboard sign goes down; copies still go into 小窝
            byte opacity = c == CalmStyle.Quiet ? (byte)(Math.Max(20, Math.Min(100, settings.QuietOpacity)) * 255 / 100) : (byte)255;
            if (window.Opacity != opacity)
            {
                window.Opacity = opacity;
                shownFrame = -1;
            }
            bool watch = Supervising;
            supervisor.Enable(watch || settings.WatchAlong); // (陪看 needs to know what's in front too)
            if (watch)
            {
                if (!titleTimer.Enabled) titleTimer.Start();
                CheckDistraction();
            }
            else
            {
                titleTimer.Stop();
                nudgeTimer.Stop();
                distraction.Reset();
                DropDistractNote();
            }
            TellPartner();
            if (CalmChanged != null) CalmChanged();
            if (!ticking && constructed) Tick(); // (from inside a tick the schedule at its end picks it up)
        }

        bool constructed;

        /// <summary>Looks at what is in front now and nudges, praises, or waits (the timer comes back when due).</summary>
        void CheckDistraction()
        {
            nudgeTimer.Stop();
            if (!Supervising || exiting) return;
            string program, title;
            bool ours;
            supervisor.Foreground(out program, out title, out ours);
            if (demoForeground != null)
            {
                program = "demo";
                title = demoForeground;
                ours = false;
            }
            string match = ours ? null : distractions.Match(program, title);
            DistractionWatch.Verdict v = distraction.Update(match, Now);
            if (v == DistractionWatch.Verdict.Nudge) Nudge(distraction.Site, distraction.Level);
            else if (v == DistractionWatch.Verdict.Back) Praise();
            double next = distraction.NextIn(Now);
            if (!double.IsInfinity(next))
            {
                nudgeTimer.Interval = Math.Max(200, (int)Math.Min(int.MaxValue / 2, next * 1000) + 50);
                nudgeTimer.Start();
            }
        }

        void Nudge(string site, int level)
        {
            if (level == 1)
            {
                string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (saved.CaughtDay != today)
                {
                    saved.CaughtDay = today;
                    saved.CaughtCount = 0;
                }
                saved.CaughtCount++;
                SaveState();
                if (PlannerChanged != null) PlannerChanged(); // the 专注 page shows the count
            }
            string text = pet.Line("distract_" + level, site) ?? "在看" + site + "吗？回去学习！";
            if (suppressed)
            {
                int state;
                bool game = false;
                try { game = Win32.SHQueryUserNotificationState(out state) == 0 && state == 3; } catch (EntryPointNotFoundException) { }
                if (game)
                {
                    tray.Notify(Profile.Name, text); // never over an exclusive full-screen game
                    return;
                }
                // a full-screen browser or video: he may come out over it for this
                fullscreenOverride = true;
                suppressed = false;
                lastTick = -1;
                shownFrame = -1;
                UpdateWorld();
            }
            if (pet.HasPendingNote || pet.Sign != null && pet.Sign.Style == SignStyle.Note) return; // one note at a time
            Tick();
            var note = new NoteRequest();
            note.Text = text;
            note.Buttons = new[] { "好啦好啦", "学习用的" };
            note.Intro = level >= 3 ? "explode" : level == 2 ? "sassy_glance" : "poked";
            note.Tag = new DistractNote { Site = site, Level = level };
            pet.ShowNote(note);
            Tick();
        }

        void Praise()
        {
            DropDistractNote();
            Tick();
            pet.SayWith("distract_back");
            Tick();
        }

        /// <summary>Takes a 专注监督 note away (back to work, or the mode ended).</summary>
        void DropDistractNote()
        {
            fullscreenOverride = false;
            SignContent s = pet.Sign;
            if (s != null && s.Tag is DistractNote) pet.DropSigns();
        }

        /// <summary>The answer to a 专注监督 note: 学习用的 leaves that site alone until the mode ends.</summary>
        void AnswerDistractNote(DistractNote n, int answer)
        {
            fullscreenOverride = false;
            if (answer == 1) distraction.Allow(n.Site);
            CheckDistraction();
        }

        /// <summary>One reminder at a time, and only with somebody there to see it.</summary>
        void DeliverNext(Moment now)
        {
            while (noteOut == null && dueQueue.Count > 0)
            {
                if (settings.Quiet || !Showing || pet.UserAway || pet.UserIdleSeconds > Planner.PresentSeconds) return;
                // 陪看: water and getting up wait until the video is over (bedtime, to-dos and the pomodoro don't)
                int i = 0;
                while (pet.Watching && i < dueQueue.Count && (dueQueue[i].Kind == DueKind.Water || dueQueue[i].Kind == DueKind.Sit)) i++;
                if (i == dueQueue.Count) return;
                DueEvent e = dueQueue[i];
                dueQueue.RemoveAt(i);
                bool health = e.Kind == DueKind.Water || e.Kind == DueKind.Sit || e.Kind == DueKind.Bedtime;
                if (health && PartnerTakesDuty)
                {
                    // the other pet reminds (and she joins in when it does): no second note
                    planner.Answer(e, Answer.None, now);
                    continue;
                }
                if (health && partnerPresent)
                    partner.Send(Partner.Code.Reminder, e.Kind == DueKind.Water ? 1 : e.Kind == DueKind.Sit ? 2 : 3);
                NoteRequest note = BuildNote(e, now);
                if (note == null)
                {
                    planner.Answer(e, Answer.None, now); // e.g. the to-do was finished in the meantime
                    continue;
                }
                noteOut = e;
                pet.ShowNote(note);
            }
        }

        NoteRequest BuildNote(DueEvent e, Moment now)
        {
            var n = new NoteRequest();
            n.Tag = e;
            switch (e.Kind)
            {
                case DueKind.FocusDone:
                    n.Text = pet.Line("note_focus_done", e.Level.ToString()) ?? "专注完成！休息一下吧";
                    n.Buttons = new[] { "好" };
                    n.Intro = "stretch";
                    break;
                case DueKind.BreakDone:
                    n.Text = pet.Line("note_break_done") ?? "休息结束～ 开始下一个番茄吗？";
                    n.Buttons = new[] { "开始", "不了" };
                    break;
                case DueKind.Water:
                    n.Text = pet.Line("note_water") ?? "该喝水啦～";
                    n.Buttons = new[] { "喝了", "稍后" };
                    n.Intro = "drink_water";
                    break;
                case DueKind.Sit:
                    n.Text = pet.Line("note_sit") ?? "坐太久啦，起来动一动吧";
                    n.Buttons = new[] { "好", "稍后" };
                    n.Intro = "stretch";
                    break;
                case DueKind.Bedtime:
                    int level = Math.Min(e.Level, 2);
                    string clock = now.Local.ToString("HH:mm", CultureInfo.InvariantCulture);
                    n.Text = pet.Line("note_bedtime" + (level + 1), clock) ?? clock + " 了，该睡觉啦";
                    n.Buttons = new[] { "去睡了", "再玩一会" };
                    n.Intro = level == 0 ? "yawn" : level == 1 ? "poked" : "explode";
                    break;
                case DueKind.Todo:
                    Todo t = planner.Todos.Find(e.TodoId);
                    if (t == null || t.Done) return null;
                    n.Text = ClipText.Truncate(ClipText.Collapse(t.Text), 30);
                    n.Buttons = new[] { "完成", "推迟10分钟" };
                    n.Intro = "wave";
                    break;
            }
            return n;
        }

        /// <summary>Acts on whatever note was answered (or went away unanswered) since the last look.</summary>
        void HandleNoteAnswers()
        {
            SignContent note;
            int answer;
            if (!pet.TakeNoteAnswer(out note, out answer)) return;
            var dn = note.Tag as DistractNote;
            if (dn != null)
            {
                AnswerDistractNote(dn, answer);
                return;
            }
            var e = note.Tag as DueEvent;
            if (e == null) return;
            if (e == noteOut) noteOut = null;
            Moment now = Moment.Now;
            Answer a = answer == 0 ? Answer.Yes : answer == 1 || answer == NoteAnswer.Ignored ? Answer.Later : Answer.None;
            planner.Answer(e, a, now);
            ReactTo(e, a);
            if (e.Kind == DueKind.Todo) SaveTodos();
            SaveState();
            SyncFocus(now);
            plannerCheckUtc = DateTime.MinValue; // next one, if any
            if (PlannerChanged != null) PlannerChanged();
        }

        void ReactTo(DueEvent e, Answer a)
        {
            if (a == Answer.None) return;
            bool yes = a == Answer.Yes;
            switch (e.Kind)
            {
                case DueKind.Todo:
                    if (yes) pet.Stats.AddMood(5);
                    pet.AfterNote(yes ? "happy" : null, yes ? "todo_done" : "snooze");
                    break;
                case DueKind.Water:
                case DueKind.Sit:
                    if (yes) pet.Stats.Affection += 1; // 点她确认会加好感
                    pet.AfterNote(yes ? "happy" : null, yes ? (e.Kind == DueKind.Water ? "water_ok" : "sit_ok") : "snooze");
                    break;
                case DueKind.Bedtime:
                    if (yes) pet.Stats.Affection += 1;
                    pet.AfterNote(yes ? "wave" : null, yes ? "bedtime_ok" : "bedtime_later");
                    break;
                case DueKind.FocusDone:
                    pet.AfterNote(null, "break_start");
                    break;
                case DueKind.BreakDone:
                    pet.AfterNote(null, yes ? "focus_start" : "focus_rest");
                    break;
            }
        }

        void RestorePlanner()
        {
            DateTime? ends = Iso.Read(saved.FocusEndsUtc);
            DateTime day;
            if (!DateTime.TryParse(saved.FocusDay, CultureInfo.InvariantCulture, DateTimeStyles.None, out day)) day = DateTime.MinValue;
            if (ends != null && saved.FocusPhase > 0 && saved.FocusPhase <= 2)
                planner.Focus.Restore((FocusPhase)saved.FocusPhase, ends.Value, saved.FocusCount, day);
            else planner.Focus.Restore(FocusPhase.Off, DateTime.MinValue, saved.FocusCount, day);
            HealthReminders h = planner.Health;
            h.NextWaterUtc = Iso.Read(saved.NextWaterUtc) ?? DateTime.MinValue;
            h.SitNotBeforeUtc = Iso.Read(saved.SitNotBeforeUtc) ?? DateTime.MinValue;
            h.NextBedtimeUtc = Iso.Read(saved.NextBedtimeUtc) ?? DateTime.MinValue;
            h.BedtimeLevel = saved.BedtimeLevel;

            SavedTodos todos = Store.Load<SavedTodos>("todos.json");
            if (todos != null && todos.Items != null)
            {
                var list = new List<Todo>();
                foreach (SavedTodo s in todos.Items)
                {
                    if (s == null || string.IsNullOrWhiteSpace(s.Text)) continue;
                    var t = new Todo();
                    t.Text = s.Text;
                    t.DueUtc = Iso.Read(s.DueUtc);
                    t.Done = s.Done;
                    t.CreatedUtc = Iso.Read(s.CreatedUtc) ?? DateTime.UtcNow;
                    t.DoneUtc = Iso.Read(s.DoneUtc);
                    list.Add(t);
                }
                planner.Todos.Restore(list);
            }
            SyncFocus(Moment.Now);
        }

        void SavePlannerState()
        {
            Pomodoro f = planner.Focus;
            saved.FocusPhase = (int)f.Phase;
            saved.FocusEndsUtc = f.Phase == FocusPhase.Off ? null : Iso.Write(f.EndsUtc);
            saved.FocusCount = f.SavedCount;
            saved.FocusDay = f.SavedDay == DateTime.MinValue ? null : f.SavedDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            HealthReminders h = planner.Health;
            saved.NextWaterUtc = Iso.Write(h.NextWaterUtc);
            saved.SitNotBeforeUtc = Iso.Write(h.SitNotBeforeUtc);
            saved.NextBedtimeUtc = Iso.Write(h.NextBedtimeUtc);
            saved.BedtimeLevel = h.BedtimeLevel;
        }

        void SaveTodos()
        {
            if (demo) return;
            try
            {
                var s = new SavedTodos();
                foreach (Todo t in planner.Todos.Items)
                {
                    var st = new SavedTodo();
                    st.Text = t.Text;
                    st.DueUtc = Iso.Write(t.DueUtc);
                    st.Done = t.Done;
                    st.CreatedUtc = Iso.Write(t.CreatedUtc);
                    st.DoneUtc = Iso.Write(t.DoneUtc);
                    s.Items.Add(st);
                }
                Store.Save("todos.json", s);
            }
            catch (Exception) { }
        }

        public void SetSilly(int level)
        {
            settings.Silly = Math.Max(0, Math.Min(2, level));
            pet.Silly = settings.Silly;
            SaveSettings();
        }

        // ---------------------------------------------------------------- public API (设置)

        public void SetActivity(int level)
        {
            settings.Activity = Math.Max(0, Math.Min(2, level));
            pet.Activity = settings.Activity;
            SaveSettings();
        }

        public void SetChatty(int level)
        {
            settings.Chatty = Math.Max(0, Math.Min(2, level));
            pet.Chatty = settings.Chatty;
            SaveSettings();
        }

        /// <summary>Dog ears and tail on/off: swaps to the other atlas mid-animation, no restart.</summary>
        public void SetDogEars(bool on)
        {
            if (settings.DogEars == on) return;
            settings.DogEars = on;
            SaveSettings();
            atlas = LoadAtlas(CurrentLook, !on);
            pet.UseAtlas(atlas);
            eyeFrames = new EyeFrames(atlas);
            signAnchor = atlas.AnchorIndex("prop.sign");
            shownFrame = -1;
            board = null;
            Tick();
        }

        public void SetQuiet(bool on)
        {
            if (settings.Quiet != on) ToggleQuiet();
        }

        public void SetFollowCursor(bool on)
        {
            if (settings.FollowCursor != on) ToggleFollowCursor();
        }

        public void SetHideInFullscreen(bool on)
        {
            if (settings.HideInFullscreen != on) ToggleHideInFullscreen();
        }

        public bool Autostarts
        {
            get
            {
                try { return Autostart.IsEnabled(); } catch (Exception) { return false; }
            }
        }

        public void SetAutostart(bool on)
        {
            if (Autostarts != on) ToggleAutostart();
        }

        /// <summary>Changes the 小窝 shortcut. False if the keys aren't a shortcut or another program has them.</summary>
        public bool SetHotkey(Keys keys, bool on)
        {
            if (on && keys == Keys.None) return false;
            if (keys != Keys.None) settings.ClipHotkey = GlobalHotkey.Describe(keys);
            settings.ClipHotkeyOn = on;
            SaveSettings();
            hotkey.Unregister();
            return !on || hotkey.Register(GlobalHotkey.Parse(settings.ClipHotkey));
        }

        public bool HotkeyWorking { get { return hotkey.Registered; } }

        public void OpenDataFolder()
        {
            try
            {
                Directory.CreateDirectory(Store.Dir);
                Process.Start("explorer.exe", "\"" + Store.Dir + "\"");
            }
            catch (Exception) { }
        }

        public static string Version
        {
            get
            {
                System.Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor + (v.Build > 0 ? "." + v.Build : "");
            }
        }

        public IEnumerable<string> ClipNames
        {
            get
            {
                foreach (AtlasClip c in atlas.ClipList)
                    if (!c.Name.StartsWith("gaze_")) yield return c.Name;
            }
        }

        public void SetVisible(bool visible)
        {
            if (visible == !hidden) return;
            hidden = !visible;
            if (hidden)
            {
                DropSign();
                window.Hide();
            }
            else
            {
                lastTick = -1;
                shownFrame = -1;
                UpdateWorld();
            }
            SaveState();
            TellPartner(); // (hidden, she can't nudge: the other pet takes over)
            Tick();
        }

        public void SetScaleSetting(int value)
        {
            settings.Scale = value;
            SaveSettings();
            ApplyScale();
        }

        public void ToggleAutostart()
        {
            try { Autostart.Set(!Autostart.IsEnabled()); }
            catch (Exception ex)
            {
                MessageBox.Show("设置开机自启失败：" + ex.Message, Profile.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public void ToggleHideInFullscreen()
        {
            settings.HideInFullscreen = !settings.HideInFullscreen;
            SaveSettings();
            CheckFullscreen();
        }

        public void ToggleQuiet()
        {
            settings.Quiet = !settings.Quiet;
            pet.Quiet = settings.Quiet;
            if (settings.Quiet) pet.LowerSign(); // no signs in 正经模式; reminders wait until it ends
            SaveSettings();
            plannerCheckUtc = DateTime.MinValue;
            Tick();
        }

        /// <summary>
        /// While she is hidden her clock stands still, so a raised or waiting sign would still be
        /// there hours later: put it away for good.
        /// </summary>
        void DropSign()
        {
            pet.DropSigns();
            HandleNoteAnswers(); // a reminder that was up comes back once she is visible again
            boardRect = Rectangle.Empty;
            boardButtons = new Rectangle[0];
        }

        public void ToggleFollowCursor()
        {
            settings.FollowCursor = !settings.FollowCursor;
            pet.Gaze.Enabled = settings.FollowCursor;
            if (!settings.FollowCursor) pet.Eyes.LookAt(0, 0, pet.Time);
            SaveSettings();
            Tick();
        }

        public void ToggleOverlay()
        {
            if (overlay != null)
            {
                overlay.Dispose();
                overlay = null;
            }
            else ShowOverlay();
            settings.Debug = overlay != null;
            SaveSettings();
        }

        public void ToggleTimeFast()
        {
            pet.TimeScale = pet.TimeScale > 1 ? 1 : 10;
        }

        public void ToggleShortIdle()
        {
            cfg.UserIdleSleep = ShortIdle ? defaultUserIdleSleep : 20;
        }

        /// <summary>Debug: play any clip now (loops play for a few seconds).</summary>
        public void ForceClip(string clip)
        {
            AtlasClip c = atlas.GetClip(clip);
            if (c == null) return;
            Tick();
            if (c.Loop)
            {
                var show = new LoopState("debug", clip, false);
                show.Duration = 4;
                pet.ChangeState(show);
            }
            else pet.React(clip);
            Tick();
        }

        public void Exit()
        {
            if (exiting) return;
            exiting = true;
            SaveState();
            clipSaveTimer.Stop();
            SaveClips();
            pacer.Stop();
            pacer.CancelWake();
            if (nest != null && !nest.IsDisposed) nest.Close();
            ExitThread();
        }

        /// <summary>Last-ditch cleanup when crashing, so no ghost tray icon is left behind.</summary>
        public void EmergencyCleanup()
        {
            try { tray.Dispose(); } catch (Exception) { }
        }

        // ---------------------------------------------------------------- loop

        double Now { get { return clock.Elapsed.TotalSeconds; } }

        bool ticking;

        void Tick()
        {
            if (exiting || ticking) return;
            ticking = true;
            try
            {
                TickNow();
            }
            finally
            {
                ticking = false;
            }
        }

        void TickNow()
        {
            long start = clock.ElapsedTicks;
            double now = Now;
            double dt = lastTick < 0 ? 0 : now - lastTick;
            lastTick = now;
            if (now - lastHousekeeping >= HousekeepingSeconds) Housekeeping(now);
            if (Showing)
            {
                if (pet.WantsCursor)
                {
                    Win32.POINT p;
                    if (Win32.GetCursorPos(out p)) pet.SetCursor(p.X / (double)scale, p.Y / (double)scale);
                }
                if (pet.State == pet.Sleep && pet.Sleep.AwayNap) pet.UserIdleSeconds = demo ? 0 : Win32.UserIdleSeconds();
                pet.Update(dt);
                if (pet.ReleasePointer)
                {
                    pet.ReleasePointer = false;
                    window.ReleaseMouse();
                }
                HandleNoteAnswers();
                if (DateTime.UtcNow >= plannerCheckUtc) PollPlanner();
                RenderIfNeeded();
                if (partnerPresent) TellSpot();
            }
            double ms = (clock.ElapsedTicks - start) * 1000.0 / Stopwatch.Frequency;
            ticksRun++;
            demoTicks++;
            demoTickMs += ms;
            if (ms > worstTickMs) worstTickMs = ms;
            Schedule();
        }

        void Schedule()
        {
            if (!Showing)
            {
                pacer.Stop();
                pacer.CancelWake();
                return;
            }
            if (pet.Continuous || shaking)
            {
                pacer.FullRate = pet.IsDragging || pet.State == pet.Fall;
                pacer.Start();
                return;
            }
            pacer.Stop();
            double s = Math.Min(pet.NextWake, BoardWake());
            double house = HousekeepingSeconds - (Now - lastHousekeeping);
            if (house < s) s = house;
            double plan = (plannerCheckUtc - DateTime.UtcNow).TotalSeconds; // housekeeping bounds it anyway
            if (plan < s) s = plan;
            pacer.WakeIn(Math.Max(0.001, Math.Min(60, s)));
        }

        void RenderIfNeeded()
        {
            if (pet.TuckedAway)
            {
                // 完全隐身, all the way down: nothing to draw until he comes back up
                if (window.Visible) window.Hide();
                shownFrame = -1;
                boardRect = Rectangle.Empty;
                boardButtons = new Rectangle[0];
                return;
            }
            int frame = pet.Anim.Frame;
            double feetX = pet.PixelSteps && pet.State == pet.Walk ? Math.Round(pet.Body.X) * scale : pet.Body.X * scale;
            // 防干扰模式: tucked behind a screen edge, drawn shifted and cut off at the edge
            bool clip = pet.Tucked;
            int cx = (int)Math.Round(feetX + pet.TuckX * scale) - atlas.OriginX * scale;
            int cy = (int)Math.Round((pet.Body.Y + pet.TuckY) * scale) - atlas.OriginY * scale;
            int margin = scale; // room for the 1-pixel halo around the canvas
            int eye, eyeDx, eyeDy;
            eyeFrames.Resolve(frame, pet.Eyes, out eye, out eyeDx, out eyeDy);
            pet.Eyes.Changed = false;

            Speech sp = pet.Speech;
            int speechSerial = sp == null ? -1 : sp.Serial;
            if (speechSerial != shownSpeech) bubble = sp != null && font != null ? new Bubble(font, sp, uiScale) : null;
            shaking = bubble != null && bubble.Shout && pet.Time < sp.Until - 2.0;

            // The sign board hangs on the top of the stick (prop.sign) in frames that have one.
            SignContent sign = pet.Sign;
            int sax = 0, say = 0, boardKey = -1;
            double boardWidth = 1;
            if (sign != null && font != null && signAnchor >= 0 && atlas.TryGetAnchor(frame, signAnchor, out sax, out say))
            {
                if (board == null || board.Serial != sign.Serial || board.ArtScale != scale || board.UiScale != uiScale)
                {
                    board = new SignBoard(font, sign, scale, uiScale);
                    hotButton = -1;
                }
                int step;
                boardWidth = BoardWidthFactor(sign, out step);
                if (step != 0) hotButton = -1;
                boardKey = (board.Serial * 16 + step) * 8 + hotButton + 1;
            }

            bool sameLook = window.Visible && frame == shownFrame && eye == shownEye && eyeDx == shownEyeDx
                && eyeDy == shownEyeDy && speechSerial == shownSpeech && boardKey == shownBoard && !shaking;
            if (sameLook)
            {
                if (cx == shownX && cy == shownY) return;
                if (bubble == null && !clip && !shownClipped) // a cut-off frame must be cut again where it lands
                {
                    // Walking/dragging: most vsyncs only move her; the pixels stay as they are.
                    window.Move(cx + shownOffX, cy + shownOffY);
                    if (!boardRect.IsEmpty) boardRect.Offset(cx - shownX, cy - shownY);
                    for (int i = 0; i < boardButtons.Length; i++) boardButtons[i].Offset(cx - shownX, cy - shownY);
                    shownX = cx;
                    shownY = cy;
                    return;
                }
            }

            int left = cx - margin, top = cy - margin;
            int right = left + atlas.CanvasWidth * scale + 2 * margin;
            int bottom = top + atlas.CanvasHeight * scale + 2 * margin;
            int boardCx = 0, boardBottom = 0, boardW = 0;
            if (boardKey >= 0)
            {
                boardCx = cx + (sax + 1) * scale; // centred on the two-pixel stick
                boardBottom = cy + say * scale;
                boardW = board.WidthAt(boardWidth);
                left = Math.Min(left, boardCx - boardW / 2);
                right = Math.Max(right, boardCx - boardW / 2 + boardW);
                top = Math.Min(top, boardBottom - board.Height - board.PinHeight);
            }
            int bx = 0, by = 0, tipX = 0;
            if (bubble != null)
            {
                int petX = (int)Math.Round(pet.Body.X * scale);
                int headTop = cy + (frame >= 0 ? atlas.Frames[frame].OffsetY : 20) * scale;
                if (boardKey >= 0) headTop = Math.Min(headTop, boardBottom - board.Height - board.PinHeight); // talk above the sign
                bx = petX - bubble.Width / 2;
                bx = Math.Max(workArea.Left + 2, Math.Min(workArea.Right - 2 - bubble.Width, bx));
                by = Math.Max(workArea.Top, headTop - bubble.Height - 2 * uiScale);
                if (shaking)
                {
                    int phase = (int)(pet.Time * 30);
                    bx += ((phase & 1) == 0 ? -1 : 1) * uiScale;
                    by += ((phase & 2) == 0 ? -1 : 1) * uiScale;
                }
                tipX = petX;
                left = Math.Min(left, bx);
                top = Math.Min(top, by);
                right = Math.Max(right, bx + bubble.Width);
                bottom = Math.Max(bottom, by + bubble.Height);
            }

            int w = right - left, h = bottom - top;
            window.EnsureBuffer(w, h);
            Renderer.Clear(window.Bits, window.Stride, w, h);
            if (boardKey >= 0)
            {
                // behind her, so while it goes up it seems to come from behind her head
                board.Draw(window.Bits, window.Stride, w, h, boardCx - left, boardBottom - top, boardWidth, hotButton);
                boardRect = new Rectangle(boardCx - boardW / 2, boardBottom - board.Height, boardW, board.Height);
                boardButtons = new Rectangle[boardWidth >= 1 ? board.ButtonCount : 0];
                for (int i = 0; i < boardButtons.Length; i++)
                {
                    Rectangle r = board.ButtonRect(i);
                    r.Offset(boardRect.Left, boardRect.Top);
                    boardButtons[i] = r;
                }
            }
            else
            {
                boardRect = Rectangle.Empty;
                boardButtons = new Rectangle[0];
            }
            if (frame >= 0)
                renderer.DrawFrame(window.Bits, window.Stride, w, h, atlas.Frames[frame], cx - left, cy - top, scale, true);
            if (eye >= 0)
                renderer.DrawFrame(window.Bits, window.Stride, w, h, atlas.Frames[eye], cx - left + eyeDx * scale, cy - top + eyeDy * scale, scale, false);
            if (bubble != null)
                bubble.Draw(window.Bits, window.Stride, w, h, bx - left, by - top, tipX - left);
            if (clip)
            {
                // everything past the edge goes: invisible, and clicks there fall through to the taskbar
                switch (pet.TuckEdge)
                {
                    case PeekEdge.Left: Renderer.ClearRect(window.Bits, window.Stride, w, h, 0, 0, workArea.Left - left, h); break;
                    case PeekEdge.Right: Renderer.ClearRect(window.Bits, window.Stride, w, h, workArea.Right - left, 0, w, h); break;
                    default: Renderer.ClearRect(window.Bits, window.Stride, w, h, 0, workArea.Bottom - top, w, h); break;
                }
            }
            shownClipped = clip;
            window.Present(left, top, w, h);

            shownFrame = frame;
            shownEye = eye;
            shownEyeDx = eyeDx;
            shownEyeDy = eyeDy;
            shownSpeech = speechSerial;
            shownBoard = boardKey;
            shownX = cx;
            shownY = cy;
            shownOffX = left - cx;
            shownOffY = top - cy;
            pet.Anim.Changed = false;
            framesDrawn++;
            demoFrames++;
        }

        /// <summary>
        /// How wide the board is drawn (1 = full, with text): it unfolds on the way up, folds on
        /// the way down, and flips over edge-on when new content arrives. step identifies the look.
        /// </summary>
        double BoardWidthFactor(SignContent sign, out int step)
        {
            string clip = pet.Anim.ClipName;
            int index = pet.Anim.Index;
            if (clip == "sign_up" && index < 2)
            {
                step = 1 + index;
                return index == 0 ? 0.34 : 0.67;
            }
            if (clip == "sign_down" && index < 2)
            {
                step = 3 + index;
                return index == 0 ? 0.67 : 0.34;
            }
            double t = pet.Time - sign.FlippedAt;
            if (t >= 0 && t < FlipWidths.Length * FlipStep)
            {
                int k = (int)(t / FlipStep);
                step = 5 + k;
                return FlipWidths[k];
            }
            step = 0;
            return 1;
        }

        /// <summary>Seconds until the flip animation needs its next step.</summary>
        double BoardWake()
        {
            SignContent sign = pet.Sign;
            if (sign == null) return double.PositiveInfinity;
            double t = pet.Time - sign.FlippedAt;
            if (t < 0 || t >= FlipWidths.Length * FlipStep) return double.PositiveInfinity;
            return FlipStep - t % FlipStep + 0.002;
        }

        void Housekeeping(double now)
        {
            lastHousekeeping = now;
            if (!Showing) return;
            pet.LocalHour = DateTime.Now.TimeOfDay.TotalHours;
            if (!forceSteps) pet.PixelSteps = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
            UpdateWorld();
            window.ReassertTopmost();
            pet.UserIdleSeconds = demo ? 0 : Win32.UserIdleSeconds();
            CheckWatching(); // (tabs change titles, not windows)
            plannerCheckUtc = DateTime.MinValue; // presence may have changed: see what can be delivered
            tray.RefreshTip();
            if (now - lastSave >= SaveEverySeconds)
            {
                lastSave = now;
                SaveState();
            }
        }

        bool FullscreenBusy()
        {
            if (!settings.HideInFullscreen) return false;
            int state;
            try
            {
                if (Win32.SHQueryUserNotificationState(out state) != 0) return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            return state == 2 || state == 3 || state == 4;
        }

        void CheckFullscreen()
        {
            if (exiting) return;
            // Sitting time counts while she is hidden too (a long game is a long sit).
            planner.Health.Track(Moment.Now, Win32.UserIdleSeconds());
            bool busy = FullscreenBusy() && !fullscreenOverride; // (out over a full-screen video for a nudge)
            if (busy == suppressed) return;
            suppressed = busy;
            if (suppressed)
            {
                DropSign();
                window.Hide();
            }
            else
            {
                lastTick = -1;
                shownFrame = -1;
                UpdateWorld();
            }
            Tick();
        }

        void GreetOnStart()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (saved.LastGreetDate == today) return;
            saved.LastGreetDate = today;
            double hour = DateTime.Now.TimeOfDay.TotalHours;
            string trigger = hour >= 5 && hour < 11 ? "greet_morning"
                : hour >= 11 && hour < 18 ? "greet_afternoon"
                : hour >= 18 && hour < 23 ? "greet_evening" : "greet_night";
            pet.Greet(trigger);
            MaybePickOutfitForToday();
        }

        // ---------------------------------------------------------------- input

        int ButtonAt(int x, int y)
        {
            for (int i = 0; i < boardButtons.Length; i++) if (boardButtons[i].Contains(x, y)) return i;
            return -1;
        }

        void SetHotButton(int i)
        {
            if (i == hotButton) return;
            hotButton = i;
            Tick(); // redraw the highlight
        }

        void OnLeftDown(int x, int y)
        {
            // A press on the sign board is about the board (a button, or 小窝), not a poke.
            boardPress = !boardRect.IsEmpty && boardRect.Contains(x, y);
            buttonPress = boardPress ? ButtonAt(x, y) : -1;
            if (boardPress) return;
            pet.PointerDown(x / (double)scale, y / (double)scale, Now);
            Tick();
        }

        void OnMouseMoved(int x, int y)
        {
            if (boardPress) return;
            if (!window.LeftButtonDown)
            {
                if (!boardRect.IsEmpty && boardRect.Contains(x, y))
                {
                    pet.PointerLeave(); // reading the sign is not staring at her
                    SetHotButton(ButtonAt(x, y));
                    return;
                }
                SetHotButton(-1);
                State before = pet.State;
                int speech = pet.SpeechSerial;
                pet.PointerHover(x / (double)scale, y / (double)scale, Now);
                if (pet.State != before || pet.SpeechSerial != speech) Tick();
                return;
            }
            pet.PointerMove(x / (double)scale, y / (double)scale, Now);
            if (pet.Continuous && !pacer.Running)
            {
                pacer.FullRate = pet.IsDragging;
                pacer.Start();
            }
        }

        void OnLeftUp(int x, int y)
        {
            if (boardPress)
            {
                boardPress = false;
                int button = buttonPress;
                buttonPress = -1;
                if (!boardRect.Contains(x, y)) return;
                SignContent sign = pet.Sign;
                bool note = sign != null && sign.Style == SignStyle.Note;
                Tick();
                if (note && button >= 0 && button == ButtonAt(x, y)) pet.AnswerNote(button);
                else if (!note)
                {
                    if (!sign.Sticky) pet.LowerSign(); // a 常驻 sign stays up
                    OpenNest(NestPage.Clipboard);
                }
                else OpenNest(sign.Tag is DueEvent && ((DueEvent)sign.Tag).Kind == DueKind.Todo ? NestPage.Todos : NestPage.Focus);
                Tick();
                return;
            }
            Tick(); // catch up first, so a reaction starts at its first frame
            pet.PointerUp(x / (double)scale, y / (double)scale, Now);
            UpdateWorld();
            Tick();
        }

        void OnCaptureLost()
        {
            if (boardPress)
            {
                boardPress = false;
                buttonPress = -1;
                return;
            }
            pet.PointerCancel();
            UpdateWorld();
            Tick();
        }

        // ---------------------------------------------------------------- geometry

        Point FeetPhysical()
        {
            return new Point((int)Math.Round(pet.Body.X * scale), (int)Math.Round(pet.Body.Y * scale) - 1);
        }

        void PlaceInitially()
        {
            Screen screen = Screen.PrimaryScreen;
            if (saved.Monitor != null)
            {
                foreach (Screen s in Screen.AllScreens)
                {
                    if (s.DeviceName == saved.Monitor) screen = s;
                }
            }
            Rectangle wa = screen.WorkingArea;
            double fx = saved.PosX >= 0 && saved.PosX <= 1 ? saved.PosX : Profile.Me.Start;
            int px = wa.Left + (int)(fx * wa.Width);
            scale = ResolveScale(new Point(px, wa.Bottom - 1));
            pet.Body.X = px / (double)scale;
            pet.Body.Y = wa.Bottom / (double)scale;
            UpdateWorld();
        }

        void UpdateWorld()
        {
            workArea = Screen.FromPoint(FeetPhysical()).WorkingArea;
            double s = scale;
            pet.SetWorld(new Bounds(workArea.Left / s, workArea.Top / s, workArea.Right / s, workArea.Bottom / s));
            Rectangle vs = SystemInformation.VirtualScreen;
            pet.DragBounds = new Bounds(vs.Left / s, vs.Top / s, vs.Right / s, vs.Bottom / s);
            pet.DragThreshold = Math.Max(1, Win32.GetSystemMetrics(Win32.SM_CXDRAG)) / s;
        }

        static uint DpiAt(Point physical)
        {
            try
            {
                IntPtr mon = Win32.MonitorFromPoint(new Win32.POINT(physical.X, physical.Y), Win32.MONITOR_DEFAULTTONEAREST);
                uint dx, dy;
                if (Win32.GetDpiForMonitor(mon, Win32.MDT_EFFECTIVE_DPI, out dx, out dy) == 0 && dx > 0) return dx;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return 96;
        }

        int ResolveScale(Point physical)
        {
            uint dpi = DpiAt(physical);
            uiScale = Math.Max(1, (int)Math.Round(dpi / 96.0, MidpointRounding.AwayFromZero));
            if (settings.Scale >= 1 && settings.Scale <= 4) return settings.Scale;
            int s = (int)Math.Round(2.0 * dpi / 96.0, MidpointRounding.AwayFromZero);
            return Math.Max(1, Math.Min(6, s));
        }

        void ApplyScale()
        {
            int next = ResolveScale(FeetPhysical());
            if (next != scale)
            {
                double px = pet.Body.X * scale;
                double py = pet.Body.Y * scale;
                scale = next;
                pet.Body.X = px / scale;
                pet.Body.Y = py / scale;
            }
            UpdateWorld();
            shownFrame = -1;
            shownSpeech = -2;
            board = null;
            Tick();
        }

        void OnDisplayChanged()
        {
            ApplyScale();
        }

        void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            ApplyScale();
        }

        void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            SaveState();
        }

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason != SessionSwitchReason.SessionUnlock || !Showing) return;
            if (pet.State.Sleeping) return; // the welcome-back wake-up will greet instead
            Tick();
            pet.Greet("unlock");
            Tick();
            MaybePickOutfitForToday(); // the first unlock of a new day
        }

        // ---------------------------------------------------------------- persistence & resources

        void RestoreStats()
        {
            Stats s = pet.Stats;
            if (saved.Energy >= 0) s.Energy = saved.Energy;
            if (saved.Mood >= 0) s.Mood = saved.Mood;
            if (saved.Fullness >= 0) s.Fullness = saved.Fullness;
            s.Affection = saved.Affection;
            s.Digestion = saved.Digestion;
            s.Stomach = saved.Stomach;
            DateTime last;
            if (saved.LastSeenUtc != null
                && DateTime.TryParse(saved.LastSeenUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out last))
            {
                s.Offline((DateTime.UtcNow - last.ToUniversalTime()).TotalSeconds, cfg);
            }
        }

        void SaveSettings()
        {
            if (demo) return; // developer demos leave the real files alone
            try { Store.Save("settings.json", settings); } catch (Exception) { }
        }

        void SaveState()
        {
            if (demo) return;
            try
            {
                Screen screen = Screen.FromPoint(FeetPhysical());
                Rectangle wa = screen.WorkingArea;
                saved.PosX = wa.Width > 0 ? Math.Max(0, Math.Min(1, (pet.Body.X * scale - wa.Left) / wa.Width)) : 0.85;
                saved.Monitor = screen.DeviceName;
                saved.Hidden = hidden;
                SavePlannerState();
                Stats s = pet.Stats;
                saved.Energy = s.Energy;
                saved.Mood = s.Mood;
                saved.Affection = s.Affection;
                saved.Fullness = s.Fullness;
                saved.Digestion = s.Digestion;
                saved.Stomach = s.Stomach;
                saved.LastSeenUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                Store.Save("state.json", saved);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// The sprites for one look — one per hairstyle (the example: long/short),
        /// plus a coat look if an outfit has one. plain = without ears and tail. Falls back to the full look, then "base".
        /// </summary>
        internal static Atlas LoadAtlas(string look, bool plain)
        {
            string first = Profile.Me.Look;
            string[] names = plain ? new[] { look + "_plain", look, first + "_plain", first } : new[] { look, first };
            foreach (string name in names)
            {
                using (Stream s = Profile.Open(name + ".atlas"))
                {
                    if (s != null) return Atlas.Load(s);
                }
            }
            throw new FileNotFoundException("找不到图集 " + look + ".atlas");
        }

        /// <summary>Which look's atlas to use: the hairstyle, plus "_coat" when the outfit wears the coat.</summary>
        string CurrentLook
        {
            get { return wardrobe == null ? Profile.Me.Look : LookFor(CurrentOutfit, Hair); }
        }

        static string LookFor(Outfit outfit, string hair)
        {
            return outfit != null && outfit.Coat ? hair + "_coat" : hair;
        }

        static Wardrobe LoadWardrobe()
        {
            if (!Profile.Me.Has("wardrobe")) return null;
            using (Stream s = Profile.Open("outfits.txt"))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s, Encoding.UTF8))
                {
                    Wardrobe w = Wardrobe.Parse(r);
                    return w.Outfits.Count > 0 ? w : null;
                }
            }
        }

        // ---------------------------------------------------------------- public API (衣柜)

        /// <summary>Null for pets without a wardrobe.</summary>
        public Wardrobe Wardrobe { get { return wardrobe; } }

        public Outfit CurrentOutfit
        {
            get { return wardrobe == null ? null : wardrobe.Find(settings.Outfit) ?? wardrobe.Default; }
        }

        /// <summary>The hairstyle look: "short" or "long".</summary>
        public string Hair
        {
            get { return settings.Hair == "long" || settings.Hair == "short" ? settings.Hair : Profile.Me.Look; }
        }

        public bool AutoOutfit { get { return settings.AutoOutfit; } }

        /// <summary>Fired after an outfit or hairstyle change has happened.</summary>
        public event Action WardrobeChanged;

        static readonly Cue[] ChangeCues = { new Cue(1, "outfit_start"), new Cue(4, "@swap"), new Cue(9, "outfit_done") };
        static readonly Cue[] PotionCues = { new Cue(3, "potion"), new Cue(6, "@swap"), new Cue(9, "hair_long") };
        static readonly Cue[] HaircutCues = { new Cue(1, "haircut"), new Cue(6, "@swap"), new Cue(12, "hair_short") };

        /// <summary>He changes behind the folding screen (or at once, if he is busy).</summary>
        public void ChangeOutfit(string id)
        {
            if (wardrobe == null || wardrobe.Find(id) == null || pendingOutfit != null || pendingHair != null) return;
            if (CurrentOutfit != null && CurrentOutfit.Id == id) return;
            StartWardrobeScene("change_clothes", ChangeCues, id, Hair);
        }

        /// <summary>Any outfit but the one he has on.</summary>
        public void RandomOutfit()
        {
            if (wardrobe == null || wardrobe.Outfits.Count < 2) return;
            var others = new List<Outfit>();
            foreach (Outfit o in wardrobe.Outfits)
                if (o != CurrentOutfit) others.Add(o);
            ChangeOutfit(others[new Random().Next(others.Count)].Id);
        }

        /// <summary>"long": he drinks the growth potion; "short": a haircut.</summary>
        public void ChangeHair(string hair)
        {
            if (wardrobe == null || hair == Hair || pendingOutfit != null || pendingHair != null) return;
            if (hair != "long" && hair != "short") return;
            StartWardrobeScene(hair == "long" ? "potion" : "haircut", hair == "long" ? PotionCues : HaircutCues,
                CurrentOutfit == null ? null : CurrentOutfit.Id, hair);
        }

        public void SetAutoOutfit(bool on)
        {
            settings.AutoOutfit = on;
            if (on) settings.AutoOutfitDay = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); // starting tomorrow
            SaveSettings();
        }

        void StartWardrobeScene(string clip, Cue[] cues, string outfit, string hair)
        {
            Outfit next = wardrobe.Find(outfit) ?? wardrobe.Default;
            pendingOutfit = next.Id;
            pendingHair = hair;
            string look = LookFor(next, hair);
            pendingAtlas = look == CurrentLook ? null : LoadAtlas(look, !settings.DogEars);
            if (!Showing)
            {
                ApplyPendingLook();
                return;
            }
            Tick(); // bring her up to now, so the scene starts at its first frame
            if (!pet.PlayScene(clip, cues, delegate { ApplyPendingLook(); }))
                ApplyPendingLook(); // busy (asleep, carried, holding a note…): just change
            Tick();
        }

        void OnSceneCue(string name)
        {
            if (name == "swap") ApplyPendingLook();
        }

        /// <summary>Puts on the pending outfit / hairstyle (at the scene's @swap, or when it gets cut short).</summary>
        void ApplyPendingLook()
        {
            if (pendingOutfit == null && pendingHair == null) return;
            settings.Outfit = pendingOutfit;
            settings.Hair = pendingHair;
            pendingOutfit = null;
            pendingHair = null;
            SaveSettings();
            if (pendingAtlas != null)
            {
                atlas = pendingAtlas;
                pendingAtlas = null;
                pet.UseAtlas(atlas);
                eyeFrames = new EyeFrames(atlas);
                signAnchor = atlas.AnchorIndex("prop.sign");
                board = null;
            }
            renderer.UsePalette(CurrentOutfit != null ? CurrentOutfit.Apply(atlas.Palette) : atlas.Palette);
            shownFrame = -1; // redraw with the new colours
            if (WardrobeChanged != null) WardrobeChanged();
        }

        /// <summary>
        /// A still of him in an outfit (standing, the current hairstyle), for the 衣柜 page: a
        /// premultiplied bitmap cropped to the figure.
        /// </summary>
        public Bitmap OutfitPicture(string id, int zoom)
        {
            Outfit outfit = wardrobe == null ? null : wardrobe.Find(id);
            if (outfit == null) return null;
            string look = LookFor(outfit, Hair);
            Atlas a = look == CurrentLook ? atlas : LoadAtlas(look, !settings.DogEars);
            AtlasClip idle = a.GetClip("idle");
            if (idle == null) return null;
            var r = new Renderer(a);
            r.UsePalette(outfit.Apply(a.Palette));
            var eyes = new Eyes(new Rng(1), null, 1e9); // open, looking ahead
            var frames = new EyeFrames(a);
            int w = a.CanvasWidth * zoom, h = a.CanvasHeight * zoom;
            var buf = new int[w * h];
            var pin = System.Runtime.InteropServices.GCHandle.Alloc(buf, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                IntPtr p = pin.AddrOfPinnedObject();
                int f = idle.Frames[0], eye, dx, dy;
                r.DrawFrame(p, w, w, h, a.Frames[f], 0, 0, zoom, false);
                frames.Resolve(f, eyes, out eye, out dx, out dy);
                if (eye >= 0) r.DrawFrame(p, w, w, h, a.Frames[eye], dx * zoom, dy * zoom, zoom, false);
            }
            finally
            {
                pin.Free();
            }
            // crop to the figure
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (((uint)buf[y * w + x] >> 24) == 0) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            if (maxX < 0) return null;
            int cw = maxX - minX + 1, ch = maxY - minY + 1;
            var bmp = new Bitmap(cw, ch, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, cw, ch), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            for (int y = 0; y < ch; y++)
                System.Runtime.InteropServices.Marshal.Copy(buf, (minY + y) * w + minX, data.Scan0 + y * data.Stride, cw);
            bmp.UnlockBits(data);
            return bmp;
        }

        /// <summary>每天早上自己挑一套: a little while after the first hello of a new day.</summary>
        void MaybePickOutfitForToday()
        {
            if (wardrobe == null || !settings.AutoOutfit) return;
            string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (settings.AutoOutfitDay == today) return;
            settings.AutoOutfitDay = today;
            SaveSettings();
            if (autoOutfitTimer != null) autoOutfitTimer.Dispose();
            autoOutfitTimer = new System.Windows.Forms.Timer();
            autoOutfitTimer.Interval = 6000; // after the greeting has been read
            autoOutfitTimer.Tick += delegate
            {
                autoOutfitTimer.Dispose();
                autoOutfitTimer = null;
                if (!exiting) RandomOutfit();
            };
            autoOutfitTimer.Start();
        }

        static PixelFont LoadFont()
        {
            using (Stream s = Profile.Open("pixel12.font"))
            {
                return s == null ? null : PixelFont.Load(s);
            }
        }

        /// <summary>The built-in lines, then every file in the 台词 folder (hers last).</summary>
        static Lines LoadLines()
        {
            var lines = new Lines();
            using (Stream s = Profile.Open("lines.zh.txt"))
            {
                if (s != null)
                    using (var r = new StreamReader(s, Encoding.UTF8)) lines.Load(r);
            }
            try
            {
                LineFiles.Prepare();
                foreach (string file in LineFiles.Files())
                {
                    using (var r = new StreamReader(file, Encoding.UTF8)) lines.Load(r);
                }
            }
            catch (Exception) { }
            return lines;
        }

        /// <summary>The built-in lines as data, for the 台词 page to list.</summary>
        public LineBook BuiltinLines
        {
            get
            {
                if (builtinLines == null)
                {
                    using (Stream s = Profile.Open("lines.zh.txt"))
                    {
                        if (s == null) builtinLines = new LineBook();
                        else using (var r = new StreamReader(s, Encoding.UTF8)) builtinLines = LineBook.Parse(r);
                    }
                }
                return builtinLines;
            }
        }

        /// <summary>Re-reads every lines file (after the 台词 page saved, or a file changed in the folder).</summary>
        public void ReloadLines()
        {
            linesReload.Stop();
            pet.Lines = LoadLines();
            if (LinesChanged != null) LinesChanged();
        }

        /// <summary>Fired after the lines were read again.</summary>
        public event Action LinesChanged;

        /// <summary>Watches the 台词 folder so a file dropped in (or edited by hand) counts at once.</summary>
        void WatchLinesFolder()
        {
            try
            {
                linesWatch = new FileSystemWatcher(LineFiles.Folder, "*.txt");
                linesWatch.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
                FileSystemEventHandler changed = delegate { ui.Post(delegate { if (!exiting) { linesReload.Stop(); linesReload.Start(); } }, null); };
                linesWatch.Changed += changed;
                linesWatch.Created += changed;
                linesWatch.Deleted += changed;
                linesWatch.Renamed += delegate { ui.Post(delegate { if (!exiting) { linesReload.Stop(); linesReload.Start(); } }, null); };
                linesWatch.EnableRaisingEvents = true;
            }
            catch (Exception)
            {
                linesWatch = null; // no folder watching: saving from the page still reloads
            }
        }

        /// <summary>She says this sentence right now (the 台词 page's 试一下).</summary>
        public void TryLine(string text, string scene)
        {
            if (!Showing || string.IsNullOrEmpty(text)) return;
            Tick();
            pet.SayText(pet.Fill(text, SampleArgs(scene)), scene == "explode" ? SpeechStyle.Shout : SpeechStyle.Normal);
            Tick();
        }

        /// <summary>What {0} stands for in a scene, for trying a line out.</summary>
        static string[] SampleArgs(string scene)
        {
            if (scene == null) return new string[0];
            if (scene.StartsWith("note_bedtime")) return new[] { DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture) };
            if (scene.StartsWith("distract_")) return new[] { "B站" };
            if (scene == "note_focus_done") return new[] { "3" };
            return new[] { "10" };
        }

        public void OpenLinesFolder()
        {
            try
            {
                LineFiles.Prepare();
                Process.Start("explorer.exe", "\"" + LineFiles.Folder + "\"");
            }
            catch (Exception) { }
        }

        /// <summary>Renames her (empty = back to the character's own name).</summary>
        public void SetName(string name)
        {
            name = name == null ? "" : name.Trim();
            if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);
            if (name == Profile.Me.Name) name = "";
            settings.Name = name.Length == 0 ? null : name;
            SaveSettings();
            Profile.NameOverride = settings.Name;
            pet.Name = Profile.Name;
            tray.RefreshTip();
            if (nest != null && !nest.IsDisposed) nest.Text = "小窝 · " + Profile.Name;
        }

        public const int MaxNameLength = 12;

        static Icon LoadIcon()
        {
            using (Stream s = Profile.Open("icon.ico"))
            {
                if (s == null) return (Icon)SystemIcons.Application.Clone();
                return new Icon(s, SystemInformation.SmallIconSize);
            }
        }

        // ---------------------------------------------------------------- debug overlay

        void ShowOverlay()
        {
            process = Process.GetCurrentProcess();
            lastCpu = process.TotalProcessorTime;
            lastPerfTime = Now;
            overlay = new DebugOverlay(OverlayText);
            overlay.Show();
        }

        string OverlayText()
        {
            double now = Now;
            double span = Math.Max(0.001, now - lastPerfTime);
            process.Refresh();
            TimeSpan cpu = process.TotalProcessorTime;
            double core = (cpu - lastCpu).TotalSeconds / span * 100;
            Stats st = pet.Stats;
            var sb = new StringBuilder();
            sb.AppendFormat("状态 {0,-8} 动作 {1}\n", pet.StateName, pet.Anim.ClipName);
            sb.AppendFormat("精力 {0:F0}  心情 {1:F0}  好感 {2:F1}  烦躁 {3:F0}\n", st.Energy, st.Mood, st.Affection, st.Annoyance);
            sb.AppendFormat("饱腹 {0:F0}  胃 {1:F0}  便意 {2:F0}  搞怪 {3}\n", st.Fullness, st.Stomach, st.Digestion, pet.EffectiveSilly);
            sb.AppendFormat("看鼠标 {0} 兴趣 {1:F2}  离开 {2:F0}s  {3}\n", pet.Gaze.Engaged ? "是" : "否", pet.Gaze.Interest,
                pet.UserIdleSeconds, pet.TimeScale > 1 ? "×10" : "");
            sb.AppendFormat("绘制 {0:F1} fps  节拍 {1:F1}/s  最差 {2:F2} ms  倍率 {3}\n", framesDrawn / span, ticksRun / span, worstTickMs, scale);
            sb.AppendFormat("CPU {0:F2}% 单核 ({1:F2}% 总)  vsync {2}\n", core, core / Environment.ProcessorCount, pacer.Running ? "开" : "关");
            sb.AppendFormat("内存 工作集 {0:F1} MB  私有 {1:F1} MB", process.WorkingSet64 / 1048576.0, process.PrivateMemorySize64 / 1048576.0);
            framesDrawn = 0;
            ticksRun = 0;
            worstTickMs = 0;
            lastCpu = cpu;
            lastPerfTime = now;
            return sb.ToString();
        }

        // ---------------------------------------------------------------- teardown

        bool disposed;

        protected override void Dispose(bool disposing)
        {
            // WinForms disposes the context itself when Application.Run ends, and Program's
            // using does it again: the second time must be a no-op.
            if (disposed) return;
            disposed = true;
            if (disposing)
            {
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                SystemEvents.SessionEnding -= OnSessionEnding;
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                fullscreenTimer.Dispose();
                clipSaveTimer.Dispose();
                if (linesWatch != null) linesWatch.Dispose();
                linesReload.Dispose();
                if (autoOutfitTimer != null) autoOutfitTimer.Dispose();
                if (partner != null && partnerPresent) partner.Send(Partner.Code.Bye, 0);
                supervisor.Dispose();
                nudgeTimer.Dispose();
                titleTimer.Dispose();
                clipWatch.Dispose();
                hotkey.Dispose();
                if (nest != null) nest.Dispose();
                pacer.Dispose();
                tray.Dispose();
                if (overlay != null) overlay.Dispose();
                window.Dispose();
                appIcon.Dispose();
                if (process != null) process.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
