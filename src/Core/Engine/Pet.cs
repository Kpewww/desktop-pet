using System;
using System.Collections.Generic;

namespace Aly.Core.Engine
{
    public enum SpeechStyle
    {
        Normal,
        Shout,
    }

    public sealed class Speech
    {
        public string Text;
        public SpeechStyle Style;
        public double Until;
        public int Serial;
    }

    public enum SignStyle
    {
        /// <summary>What you just copied.</summary>
        Clipboard,
        /// <summary>A reminder note with answer buttons.</summary>
        Note,
    }

    /// <summary>What is written on the sign she holds up. A new object for every new content.</summary>
    public sealed class SignContent
    {
        public string Text;
        /// <summary>Changes with every new text, so the app knows to lay the board out again.</summary>
        public int Serial;
        /// <summary>When the board flipped over to this text; NegativeInfinity = it was raised with it.</summary>
        public double FlippedAt = double.NegativeInfinity;
        public SignStyle Style;
        /// <summary>常驻显示: she keeps it up, only taking a break now and then (see Pet.SetStickySign).</summary>
        public bool Sticky;
        /// <summary>A note's answer buttons (the first one is also what a click on her means).</summary>
        public string[] Buttons;
        /// <summary>Whatever the app wants back with the answer.</summary>
        public object Tag;
        public bool Answered;
    }

    /// <summary>A reminder for her to hold up: an optional little scene first, then the note.</summary>
    public sealed class NoteRequest
    {
        public string Text;
        public string[] Buttons;
        /// <summary>Clip played before raising the note (drinking water, stretching…).</summary>
        public string Intro;
        public Cue[] IntroCues;
        public object Tag;
    }

    /// <summary>How a note went away: a button index, or one of these.</summary>
    public static class NoteAnswer
    {
        /// <summary>Taken away before anyone answered (picked up, user left, she was hidden).</summary>
        public const int Dismissed = -1;
        /// <summary>Held up for its full time with no answer.</summary>
        public const int Ignored = -2;
    }

    /// <summary>
    /// The pet's brain and body. Knows nothing about windows or pixels on screen:
    /// all coordinates are screen positions divided by the display scale (u).
    /// </summary>
    public sealed class Pet
    {
        public static readonly string[] RequiredClips = { "idle", "walk_l", "walk_r", "drag", "fall", "land" };

        public Atlas Atlas { get; private set; }
        public readonly Config Cfg;
        public readonly Rng Rng;
        public readonly Body Body = new Body();
        public readonly Animator Anim;
        public readonly Eyes Eyes;
        public readonly Stats Stats = new Stats();
        public readonly PettingDetector Petting = new PettingDetector();
        public readonly GazeTracker Gaze = new GazeTracker();
        /// <summary>What she can say; null = silent.</summary>
        public Lines Lines;
        /// <summary>Her name as the user calls her, for "{name}" in lines.</summary>
        public string Name;
        /// <summary>Called for "@name" cues in a scene (the wardrobe swaps looks behind the screen).</summary>
        public Action<string> CueAction;

        // ---- the other pet (two pets on one desktop), fed by the app
        /// <summary>The other pet is running.</summary>
        public bool PartnerPresent;
        /// <summary>Where the other pet last settled (u, its feet); NaN = not known. Her strolls keep clear of it.</summary>
        public double PartnerX = double.NaN;
        public double LastVisitAt = double.NegativeInfinity;
        /// <summary>Her brain picked a visit; the app asks the other pet where it is.</summary>
        public Action<Pet> VisitRequested;
        /// <summary>She blew up (the other pet may stand up for her).</summary>
        public Action<Pet> Exploded;
        /// <summary>Stay put until then (hosting a visit): no wandering off.</summary>
        public double HoldUntil = double.NegativeInfinity;

        public readonly IdleState Idle = new IdleState();
        public readonly WalkState Walk = new WalkState();
        public readonly DragState Drag = new DragState();
        public readonly FallState Fall = new FallState();
        public readonly LandState Land = new LandState();
        public readonly ReactState Reacting = new ReactState();
        public readonly SitState Sit = new SitState();
        public readonly SleepState Sleep = new SleepState();
        public readonly LoopState Annoyed = new LoopState("annoyed", "annoyed", true);
        public readonly LoopState Angry = new LoopState("angry", "angry", false);
        public readonly LoopState Dizzy = new LoopState("dizzy", "dizzy", false);
        public readonly SulkState Sulk = new SulkState();
        public readonly PettedState Petted = new PettedState();
        public readonly LoopState Hungry = new LoopState("hungry", "hungry", true);
        public readonly SignState Signing = new SignState();
        public readonly WorkState Work = new WorkState();
        public readonly CalmState Calming = new CalmState();
        public readonly WatchState Watch = new WatchState();

        /// <summary>陪看: a video or live stream is on in front (set by the app); she sits and watches along.</summary>
        public bool Watching;

        /// <summary>防干扰模式 in effect (set by the app); Off = her normal life.</summary>
        public CalmStyle Calm;
        /// <summary>Where 边缘探头 peeks from.</summary>
        public PeekEdge PeekEdge;
        /// <summary>She only hides along with the other pet: she takes the spot next to its corner.</summary>
        public bool CalmBeside;
        /// <summary>How far she is tucked past the screen edge right now (u); the app draws her shifted by it.</summary>
        public double TuckX, TuckY;
        /// <summary>The edge she is tucked behind (the app cuts her off there).</summary>
        public PeekEdge TuckEdge;
        /// <summary>完全隐身 and all the way down: the app hides the window.</summary>
        public bool TuckedAway;
        /// <summary>Tuck depths measured from the atlas (u): down to the eyes, all the way, and sideways.</summary>
        public double PeekDepth, HideDepth, SidePeekDepth, SideHideDepth;

        /// <summary>A pomodoro is running: she works alongside you (set by the app).</summary>
        public bool Focusing;
        /// <summary>For "嘘——在专注呢（还剩 N 分钟）" (set by the app while focusing).</summary>
        public int FocusMinutesLeft;

        /// <summary>搞怪频率: 0 off, 1 normal, 2 often (toilet and farts).</summary>
        public int Silly = 1;
        /// <summary>活跃度: 0 calm (sits and watches more), 1 normal, 2 lively (wanders more, decides faster).</summary>
        public int Activity = 1;
        /// <summary>说话频率: 0 rarely, 1 normal, 2 chatty — only for optional chatter, never for replies.</summary>
        public int Chatty = 1;
        /// <summary>How long she holds a sign up; 0 = until you click her.</summary>
        public double SignSeconds = 8;
        public double LastMealAt = double.NegativeInfinity;
        public double GassyMealAt = double.NegativeInfinity;
        public double LastHungryAt = double.NegativeInfinity;
        /// <summary>What she can be fed (from the character file).</summary>
        public Food[] Foods = new Food[0];
        /// <summary>The food she gets herself when starving and unfed; null = she only complains.</summary>
        public string SelfFood;

        static readonly Cue[] ToiletCues = { new Cue(0, "toilet_go"), new Cue(4, "toilet_strain"), new Cue(10, "toilet_done"), new Cue(12, "flush") };

        public Bounds World;
        public Bounds DragBounds;
        public double Time;
        public int Facing = 1;
        public double LastImpactSpeed;
        public double JustWokeAt = double.NegativeInfinity;

        /// <summary>Pointer travel (u) before a press turns into a drag.</summary>
        public double DragThreshold = 2;
        /// <summary>Set by the app from the system double-click time.</summary>
        public double DoubleClickSeconds = 0.5;

        // ---- environment, fed by the app
        public double LocalHour = 12;
        public double UserIdleSeconds;
        public bool Quiet;
        /// <summary>
        /// Power saving (on battery): she walks in whole sprite-pixel steps, woken once per
        /// step, instead of gliding pixel by pixel on every display refresh.
        /// </summary>
        public bool PixelSteps;
        /// <summary>Debug: makes needs and moods pass faster.</summary>
        public double TimeScale = 1;

        /// <summary>Set when she bites free: the app must release the mouse capture.</summary>
        public bool ReleasePointer;

        State state;
        State previous;
        Speech speech;
        int speechSerial;
        SignContent sign;
        int signSerial;
        string pendingSign;
        double pendingSignAt;
        NoteRequest pendingNote;
        string stickyText;       // 常驻: what she keeps holding up; null = off
        bool stickyPaused;       // clicked away: stays down until something new is copied
        double stickyResumeAt;   // after a break, back up from this time on
        SignContent answeredNote;
        int noteAnswer;
        string afterClip, afterLine;
        double afterAt;

        bool pointerDown;
        bool dragging;
        bool ignoreUntilUp;
        double downX, downY, grabDx, grabDy;
        double lastClickAt = double.NegativeInfinity;
        readonly VelocityTracker tracker = new VelocityTracker();

        bool hasCursor;
        double cursorX, cursorY;
        bool hovering;
        double hoverX, hoverY, hoverStillSince, hoverStart;
        double lastStareAt = double.NegativeInfinity;
        int eyesAnchor;

        public Pet(Atlas atlas, Config cfg, Rng rng)
        {
            Atlas = atlas;
            Cfg = cfg;
            Rng = rng;
            Anim = new Animator(atlas);
            AtlasClip blink = atlas.GetClip("gaze_blink");
            Eyes = new Eyes(rng, blink == null ? null : blink.Durations, 0);
            eyesAnchor = atlas.AnchorIndex("head.eyes");
            MeasureTuck();
            ChangeState(Idle);
        }

        /// <summary>Switches to another atlas with the same clips (dog ears and tail on/off) without a hitch.</summary>
        public void UseAtlas(Atlas atlas)
        {
            Atlas = atlas;
            Anim.SwapAtlas(atlas);
            eyesAnchor = atlas.AnchorIndex("head.eyes");
            MeasureTuck();
        }

        /// <summary>
        /// How far she tucks in for 边缘探头 / 完全隐身: down until the line just under her eyes is the
        /// edge (so her eyes, hair and ears show), all the way, or sideways until the edge runs just
        /// short of the middle of her face (half a face, one eye, peeks in).
        /// </summary>
        void MeasureTuck()
        {
            AtlasClip idle = Atlas.GetClip("idle");
            int f = idle != null && idle.Count > 0 ? idle.Frames[0] : -1;
            if (f < 0)
            {
                PeekDepth = HideDepth = 40;
                SidePeekDepth = SideHideDepth = 30;
                return;
            }
            AtlasFrame frame = Atlas.Frames[f];
            HideDepth = Atlas.OriginY - frame.OffsetY + 2;
            int ex, ey;
            PeekDepth = eyesAnchor >= 0 && Atlas.TryGetAnchor(f, eyesAnchor, out ex, out ey)
                ? Math.Max(0, Atlas.OriginY - (ey + 4)) : HideDepth * 0.6;
            AtlasClip side = Atlas.GetClip("peek_side_r");
            int sf = side != null && side.Count > 0 ? side.Frames[0] : f;
            AtlasFrame s = Atlas.Frames[sf];
            int far = s.OffsetX + s.Width - Atlas.OriginX; // from her feet to her far edge, towards the screen
            SidePeekDepth = eyesAnchor >= 0 && Atlas.TryGetAnchor(sf, eyesAnchor, out ex, out ey)
                ? Math.Max(0, ex - Atlas.OriginX - 1) : Math.Max(0, far - 9);
            SideHideDepth = far + 2;
        }

        /// <summary>Chance to say an optional line, scaled by 说话频率.</summary>
        public bool ChatChance(double normal)
        {
            double k = Chatty <= 0 ? 0.3 : Chatty >= 2 ? 2.0 : 1.0;
            return Rng.Chance(Math.Min(1, normal * k));
        }

        /// <summary>How long she stands idle before deciding what to do next, scaled by 活跃度.</summary>
        public double IdlePause()
        {
            double k = Activity <= 0 ? 1.8 : Activity >= 2 ? 0.6 : 1.0;
            return Rng.Range(Cfg.IdleMin * k, Cfg.IdleMax * k);
        }

        public State State { get { return state; } }
        public State Previous { get { return previous; } }
        public string StateName { get { return state.Name; } }
        public bool IsDragging { get { return dragging; } }
        public bool Continuous { get { return state.Continuous || dragging; } }
        public Speech Speech { get { return speech; } }
        public SignContent Sign { get { return sign; } }
        public bool IsNight { get { return LocalHour >= 23.5 || LocalHour < 7; } }
        public bool UserAway { get { return UserIdleSeconds >= Cfg.UserIdleSleep; } }

        /// <summary>Seconds the pointer has been over her (0 when it isn't).</summary>
        public double HoveredFor { get { return hovering ? Time - hoverStart : 0; } }

        /// <summary>Tucked behind a screen edge right now (the app shifts and cuts her).</summary>
        public bool Tucked { get { return TuckX != 0 || TuckY != 0; } }

        /// <summary>Walks to x (u) and calls arrived there — unless something interrupts the walk.</summary>
        public void WalkTo(double x, Action<Pet> arrived)
        {
            if (Math.Abs(x - Body.X) <= 2)
            {
                if (arrived != null) arrived(this);
                return;
            }
            Walk.Target = x;
            Walk.Running = false;
            Walk.Then = null;
            ChangeState(Walk);
            Walk.Arrived = arrived; // after ChangeState: leaving a walk clears it
        }

        /// <summary>The brain picked a visit to the other pet (the app takes it from here).</summary>
        public void RequestVisit()
        {
            LastVisitAt = Time;
            if (VisitRequested != null) VisitRequested(this);
        }

        /// <summary>Meeting the other pet: face it, a happy hop and a line.</summary>
        public void Meet(double partnerX, string line)
        {
            if (!Available || Calm != CalmStyle.Off) return;
            if (partnerX != Body.X) Facing = partnerX > Body.X ? 1 : -1;
            if (Atlas.HasClip("happy")) React("happy");
            if (line != null) Say(line);
        }

        /// <summary>The other pet got poked until it exploded: she is cross on its behalf.</summary>
        public void Defend()
        {
            if (!Available || Calm != CalmStyle.Off || Focusing) return;
            Angry.Duration = 2.5;
            ChangeState(Angry);
            Say("defend");
        }

        /// <summary>The other pet is showing a reminder: she does it along with it (drinks water too…).</summary>
        public void JoinIn(string clip)
        {
            if (!Available || Calm != CalmStyle.Off || Focusing || !Atlas.HasClip(clip)) return;
            React(clip);
        }

        /// <summary>安静陪伴: to the nearer bottom corner, then sit down with the laptop.</summary>
        public void GoQuiet()
        {
            double spot = CornerSpot();
            if (Math.Abs(Body.X - spot) > 1.5)
            {
                Walk.Target = spot;
                Walk.Running = Math.Abs(Body.X - spot) > Cfg.TrotOver; // out of the way quickly
                Walk.Then = Work;
                ChangeState(Walk);
            }
            else ChangeState(Work);
        }

        /// <summary>
        /// Where she keeps out of the way (安静陪伴, 边缘探头 at the taskbar): the nearer bottom
        /// corner, as far in as walking goes — or right next to it, when she is only hiding along
        /// with the other pet (CalmBeside), so the two don't end up on top of each other.
        /// </summary>
        public double CornerSpot()
        {
            double m = Cfg.HalfWidth + Cfg.WalkMargin;
            if (World.Right - World.Left <= 2 * m + 4 * Cfg.HalfWidth) return Body.X;
            bool left = Body.X - World.Left < World.Right - Body.X;
            double x = left ? World.Left + m : World.Right - m;
            if (CalmBeside) x += (left ? 1 : -1) * (2 * Cfg.HalfWidth + 8);
            return x;
        }

        /// <summary>True while the app should keep feeding the global cursor position.</summary>
        public bool WantsCursor { get { return Gaze.Enabled && state.Calm; } }

        /// <summary>Seconds until the next update is needed when not continuous.</summary>
        public double NextWake
        {
            get
            {
                double t = Math.Min(Anim.MsUntilNextEvent / 1000.0, state.NextWake(this));
                t = Math.Min(t, Eyes.NextWake(Time));
                if (speech != null) t = Math.Min(t, Math.Max(0, speech.Until - Time));
                if (WantsCursor) t = Math.Min(t, Gaze.Engaged ? 0.1 : 0.25);
                if (Petting.Active || hovering) t = Math.Min(t, 0.2);
                return t;
            }
        }

        public void ChangeState(State next)
        {
            if (state != null) state.Exit(this);
            previous = state;
            state = next;
            if (!next.Calm) Eyes.LookAt(0, 0, Time);
            next.Enter(this);
        }

        public void SetWorld(Bounds world)
        {
            World = world;
            if (dragging) return;
            Physics.ClampX(Body, world, Cfg);
            if (state == Fall) { if (Body.Y > world.Bottom) Body.Y = world.Bottom; }
            else Body.Y = world.Bottom;
        }

        public void Update(double dt)
        {
            if (dt < 0) dt = 0;
            Time += dt;
            Stats.Tick(Time, dt * TimeScale, state.Sleeping, Cfg);
            Anim.Update(dt * 1000);
            Eyes.Update(Time, dt);
            Petting.Update(Time);
            UpdateGaze(dt);
            CheckStare();
            state.Update(this, Math.Min(dt, 0.1));
            if (speech != null && Time >= speech.Until)
            {
                speech = null;
                speechSerial++;
            }
        }

        // ------------------------------------------------------------------ speech

        public void Say(string trigger)
        {
            Say(trigger, SpeechStyle.Normal);
        }

        /// <summary>A line with {0}, {1}… filled in (for the app's note texts, too). Null if there is none.</summary>
        public string Line(string trigger, params string[] args)
        {
            if (Lines == null) return null;
            return Fill(Lines.Pick(trigger, Rng), args);
        }

        /// <summary>"{name}" becomes her current name, {0}, {1}… the arguments.</summary>
        public string Fill(string text, params string[] args)
        {
            if (text == null) return null;
            if (Name != null && text.IndexOf("{name}", StringComparison.Ordinal) >= 0) text = text.Replace("{name}", Name);
            if (args != null)
                for (int i = 0; i < args.Length; i++) text = text.Replace("{" + i + "}", args[i]);
            return text;
        }

        public void SayWith(string trigger, params string[] args)
        {
            if (Quiet) return;
            Speak(Line(trigger, args), SpeechStyle.Normal);
        }

        public void Say(string trigger, SpeechStyle style)
        {
            if (Lines == null || Quiet) return;
            Speak(Fill(Lines.Pick(trigger, Rng)), style);
        }

        /// <summary>Says exactly this (小窝's 台词 page trying a sentence out), even in 正经模式.</summary>
        public void SayText(string text, SpeechStyle style)
        {
            Speak(text, style);
        }

        void Speak(string text, SpeechStyle style)
        {
            if (text == null) return;
            var s = new Speech();
            s.Text = text;
            s.Style = style;
            s.Until = Time + (style == SpeechStyle.Shout ? 2.8 : Math.Min(5.0, 1.6 + 0.12 * text.Length));
            s.Serial = ++speechSerial;
            speech = s;
        }

        /// <summary>Changes whenever the bubble should be redrawn (new line or expiry).</summary>
        public int SpeechSerial { get { return speechSerial; } }

        public void Greet(string trigger)
        {
            if (Calm != CalmStyle.Off) return; // keeping out of the way: no hellos
            Say(trigger);
            if (state == Idle && Atlas.HasClip("wave")) React("wave");
        }

        // ------------------------------------------------------------------ actions

        public void React(string clip)
        {
            React(clip, null);
        }

        public void React(string clip, State then)
        {
            Perform(clip, null, null, then);
        }

        /// <summary>A one-shot clip with lines at cue frames and an effect if she finishes it.</summary>
        public void Perform(string clip, Cue[] cues, Action<Pet> onDone, State then)
        {
            ChangeState(Idle); // leave any current reaction cleanly first
            Reacting.Clip = clip;
            Reacting.Then = then;
            Reacting.Cues = cues;
            Reacting.OnDone = onDone;
            ChangeState(Reacting);
        }

        /// <summary>Standing and free to do something else.</summary>
        public bool Available
        {
            get { return state == Idle || state == Walk && !Walk.Running || state == Hungry || state == Annoyed; }
        }

        public int EffectiveSilly { get { return Quiet ? 0 : Silly; } }

        public Food FindFood(string id)
        {
            foreach (Food f in Foods)
                if (f.Id == id) return f;
            return null;
        }

        /// <summary>
        /// Feed her one of her foods (self = she got it herself because nobody fed her).
        /// Returns false when she can't eat now.
        /// </summary>
        public bool Feed(string id, bool self = false)
        {
            Food food = FindFood(id);
            if (food == null) return false;
            if (state == Watch && Watch.Seated && Atlas.HasClip(food.Clip))
            {
                // a snack in front of the video: up first, and back to watching afterwards
                if (Stats.Fullness > Cfg.FullRefuse)
                {
                    Say("full");
                    return false;
                }
                Watch.StandUp(this, delegate(Pet p) { p.Feed(id, self); });
                return true;
            }
            if (!Available) return false;
            if (Stats.Fullness > Cfg.FullRefuse)
            {
                Say("full");
                return false;
            }
            if (!Atlas.HasClip(food.Clip)) return false;
            Gaze.Poke(Time);
            if (self && food.SelfLine != null) Say(food.SelfLine);
            Perform(food.Clip, self && food.SelfCues != null ? food.SelfCues : food.Cues, delegate(Pet p)
            {
                p.Stats.Eat(food.Amount);
                p.Stats.AddMood(food.Mood);
                p.LastMealAt = p.Time;
                if (food.Gassy) p.GassyMealAt = p.Time;
            }, null);
            return true;
        }

        /// <summary>
        /// Plays a one-off scene (changing clothes, the potion…) if she is free right now; false
        /// if not. onEnd runs exactly once, whether the scene finishes or gets cut short.
        /// </summary>
        public bool PlayScene(string clip, Cue[] cues, Action<Pet> onEnd)
        {
            if (state == Watch && Watch.Seated && Atlas.HasClip(clip))
            {
                // watching: up first (onEnd still runs exactly once, even if that gets cut short)
                Watch.StandUp(this, delegate(Pet p) { if (!p.PlayScene(clip, cues, onEnd) && onEnd != null) onEnd(p); }, onEnd);
                return true;
            }
            if (!Available || !Atlas.HasClip(clip)) return false;
            Gaze.Poke(Time);
            Perform(clip, cues, onEnd, null);
            Reacting.OnAbort = onEnd;
            return true;
        }

        public void Toilet()
        {
            if (!Atlas.HasClip("toilet"))
            {
                Stats.Digestion = 0;
                return;
            }
            Perform("toilet", ToiletCues, delegate(Pet p)
            {
                p.Stats.Digestion = 0;
                p.Stats.AddMood(5);
            }, null);
        }

        public void Fart()
        {
            bool near = hasCursor && Math.Abs(cursorX - Body.X) < 90 && Math.Abs(cursorY - (Body.Y - 30)) < 90;
            Perform("fart", new[] { new Cue(2, near ? "fart_blame" : "fart") }, null, null);
        }

        public void ShowHunger()
        {
            LastHungryAt = Time;
            Hungry.Duration = 3;
            ChangeState(Hungry);
            Say("hungry");
        }

        /// <summary>摸摸 from the menu: a couple of seconds of petting.</summary>
        public void PetFromMenu()
        {
            Petting.Force(Time, 2.5);
            StartPetting();
        }

        /// <summary>睡觉 / 叫醒 from the menu.</summary>
        public void ToggleSleep()
        {
            if (state == Sleep) Sleep.WakeUp(this, false);
            else if (Available || state == Sit || state == Watch && Watch.Seated) Sleep.Begin(this, false);
        }

        // ------------------------------------------------------------------ the sign

        /// <summary>
        /// Hold up a sign with this text. New text while she holds one flips the board over.
        /// While she is busy (asleep, eating, being carried…) only the newest text waits, and
        /// goes up once she is free — unless it has gone stale by then. Returns true if the
        /// sign is up (or on its way up) now.
        /// </summary>
        public bool ShowSign(string text)
        {
            return ShowSign(text, false);
        }

        bool ShowSign(string text, bool sticky)
        {
            if (text == null || !Atlas.HasClip("sign_hold")) return false;
            if (state == Signing && !Signing.Lowering)
            {
                if (sign != null && sign.Style == SignStyle.Note) return false; // a reminder is more important
                SetSign(text, Signing.Holding);
                sign.Sticky = sticky;
                Signing.Rearm(this);
                return true;
            }
            if (!sticky)
            {
                pendingSign = text;
                pendingSignAt = Time;
            }
            if (!MakeFree()) return false; // a sticky sign goes up by itself once she is free
            pendingSign = null;
            ChangeState(Signing);
            SetSign(text, false);
            sign.Sticky = sticky;
            return true;
        }

        /// <summary>
        /// 常驻显示: she keeps holding this text up whenever she is free. Every minute or two she
        /// puts it down for a little stroll and then raises it again; a click puts it away until
        /// something new is copied. Null turns it off.
        /// </summary>
        public void SetStickySign(string text)
        {
            stickyText = text;
            stickyPaused = false;
            stickyResumeAt = Time;
            if (text == null)
            {
                if (state == Signing && sign != null && sign.Sticky) Signing.Lower(this);
                return;
            }
            if (StickyDue) ShowSign(text, true);
        }

        public string StickySign { get { return stickyText; } }

        bool StickyDue
        {
            get { return stickyText != null && !stickyPaused && Time >= stickyResumeAt && !Focusing && !Watching && !Quiet && !UserAway && Calm == CalmStyle.Off; }
        }

        /// <summary>When a sticky sign on a break wants to go up again (for waking her in time).</summary>
        public double StickyWakeAt
        {
            get { return stickyText != null && !stickyPaused ? stickyResumeAt : double.PositiveInfinity; }
        }

        /// <summary>Time for a break from holding the sticky sign.</summary>
        internal void StickyBreak()
        {
            stickyResumeAt = Time + Rng.Range(Cfg.StickyBreakMin, Cfg.StickyBreakMax);
        }

        /// <summary>
        /// Hold up a reminder note (after its little scene, if any). It replaces a clipboard sign
        /// that is up; otherwise it waits until she is free — waking her from an ordinary nap, but
        /// not for as long as you are away. The answer comes back through TakeNoteAnswer.
        /// </summary>
        public bool ShowNote(NoteRequest note)
        {
            if (note == null || !Atlas.HasClip("sign_hold")) return false;
            if (state == Signing && !Signing.Lowering && (sign == null || sign.Style == SignStyle.Clipboard))
            {
                SetNote(note, Signing.Holding);
                Signing.Rearm(this);
                return true;
            }
            pendingNote = note;
            if (state == Sleep && state.Sleeping && !Sleep.AwayNap)
            {
                Sleep.WakeUp(this, false);
                return false;
            }
            if (!MakeFree()) return false;
            pendingNote = null;
            return RaiseNote(note);
        }

        /// <summary>
        /// True if she can raise something right now. Otherwise starts wrapping up what she is
        /// doing, if that can be done gracefully (stop walking, stand up), and returns false.
        /// </summary>
        bool MakeFree()
        {
            if (state == Walk && !Walk.Running)
            {
                Walk.Target = Body.X; // stop (with the stop animation) and raise it right after
                return false;
            }
            if (state == Sit)
            {
                Sit.StandUp(this);
                return false;
            }
            if (state == Work)
            {
                Work.StandUp(this);
                return false;
            }
            if (state == Watch)
            {
                Watch.StandUp(this, null);
                return false;
            }
            return state == Signing || Available;
        }

        bool RaiseNote(NoteRequest note)
        {
            if (note.Intro != null && Atlas.HasClip(note.Intro))
            {
                Perform(note.Intro, note.IntroCues, delegate(Pet p) { p.SetNote(note, false); }, Signing);
                Reacting.OnAbort = delegate(Pet p) { if (p.pendingNote == null) p.pendingNote = note; };
                return true;
            }
            ChangeState(Signing);
            SetNote(note, false);
            return true;
        }

        public bool HasPendingSign
        {
            get
            {
                return afterClip != null || pendingNote != null && !UserAway
                    || pendingSign != null && Time - pendingSignAt <= Cfg.SignPendingSeconds
                    || StickyDue;
            }
        }

        /// <summary>A reminder note is waiting (clipboard signs don't count: they wait while she works or hides).</summary>
        public bool HasPendingNote { get { return pendingNote != null && !UserAway; } }

        /// <summary>
        /// Called when she is free again: first the reaction to an answered note, then a waiting
        /// note, then the clipboard sign that waited (if still fresh).
        /// </summary>
        public bool ShowPendingSign()
        {
            if (afterClip != null)
            {
                string clip = afterClip, line = afterLine;
                afterClip = afterLine = null;
                if (Time - afterAt < 10)
                {
                    React(clip);
                    if (line != null) Say(line);
                    return true;
                }
            }
            if (pendingNote != null && !UserAway)
            {
                NoteRequest note = pendingNote;
                pendingNote = null;
                return RaiseNote(note);
            }
            if (pendingSign != null)
            {
                string text = pendingSign;
                bool fresh = Time - pendingSignAt <= Cfg.SignPendingSeconds;
                pendingSign = null;
                if (fresh && ShowSign(text)) return true;
            }
            return StickyDue && ShowSign(stickyText, true);
        }

        /// <summary>Puts the clipboard sign down (a click on her, or from the app). Notes need an answer.</summary>
        public void LowerSign()
        {
            pendingSign = null;
            if (state == Signing && (sign == null || sign.Style == SignStyle.Clipboard)) Signing.Lower(this);
        }

        /// <summary>Takes away whatever is up or waiting, notes included (she is being hidden).</summary>
        public void DropSigns()
        {
            pendingSign = null;
            if (pendingNote != null)
            {
                var c = new SignContent();
                c.Style = SignStyle.Note;
                c.Tag = pendingNote.Tag;
                pendingNote = null;
                Record(c, NoteAnswer.Dismissed);
            }
            if (state == Signing) ChangeState(Idle);
            else if (state == Reacting && Reacting.OnAbort != null) ChangeState(Idle); // a note's intro
            if (pendingNote != null) DropSigns();
        }

        /// <summary>A button on the note (0 = the first, which is also what a click on her means).</summary>
        public void AnswerNote(int button)
        {
            if (state != Signing || sign == null || sign.Style != SignStyle.Note || sign.Answered) return;
            Record(sign, button);
            Signing.Lower(this);
        }

        /// <summary>After a note is answered: how she reacts, once the note is down.</summary>
        public void AfterNote(string clip, string line)
        {
            if (state == Signing || state == Reacting && Reacting.OnAbort != null)
            {
                afterClip = clip;
                afterLine = line;
                afterAt = Time;
                return;
            }
            if (clip != null && Available) React(clip);
            if (line != null) Say(line);
        }

        /// <summary>The newest answered (or dismissed) note since the last call, for the app to act on.</summary>
        public bool TakeNoteAnswer(out SignContent note, out int answer)
        {
            note = answeredNote;
            answer = noteAnswer;
            answeredNote = null;
            return note != null;
        }

        void Record(SignContent note, int answer)
        {
            note.Answered = true;
            answeredNote = note;
            noteAnswer = answer;
        }

        /// <summary>The board is gone (called when the sign state ends).</summary>
        public void ClearSign(bool timedOut)
        {
            if (sign != null && sign.Style == SignStyle.Note && !sign.Answered)
                Record(sign, timedOut ? NoteAnswer.Ignored : NoteAnswer.Dismissed);
            sign = null;
        }

        void SetSign(string text, bool flip)
        {
            var s = new SignContent();
            s.Text = text;
            s.Serial = ++signSerial;
            s.FlippedAt = flip ? Time : double.NegativeInfinity;
            if (sign != null && sign.Style == SignStyle.Note && !sign.Answered) Record(sign, NoteAnswer.Dismissed);
            sign = s;
        }

        void SetNote(NoteRequest note, bool flip)
        {
            SetSign(note.Text, flip);
            sign.Style = SignStyle.Note;
            sign.Buttons = note.Buttons;
            sign.Tag = note.Tag;
        }

        public void StartWalk()
        {
            double target = PickWalkTarget();
            if (Math.Abs(target - Body.X) <= 2) return;
            Walk.Target = target;
            Walk.Running = false;
            Walk.Then = null;
            ChangeState(Walk);
        }

        /// <summary>
        /// Where to stroll to next. She keeps to the sides of the screen, out of the way of what
        /// you are doing in the middle: short strolls about her own side, now and then over to
        /// the other side, and from the middle (dropped there, say) back to the nearer side.
        /// She keeps clear of the other pet's spot.
        /// </summary>
        public double PickWalkTarget()
        {
            double lo, hi;
            if (!SideZone(Body.X, false, out lo, out hi)) return Body.X;
            if (Body.X >= lo && Body.X <= hi && Rng.Chance(Cfg.CrossChance)) SideZone(Body.X, true, out lo, out hi);
            // not just a shuffle on the spot, and not on top of the other pet; failing that, at
            // least not on top of it; failing that, anywhere
            var holes = PartnerHoles();
            holes.Add(Body.X - Cfg.MinStroll);
            holes.Add(Body.X + Cfg.MinStroll);
            for (int n = holes.Count; n >= 0; n -= 2)
            {
                double t;
                if (PickOutside(lo, hi, holes, n, out t)) return t;
            }
            return Body.X;
        }

        /// <summary>陪看: where to sit — right here if she is on her side and clear of the other pet, else somewhere on the nearer side.</summary>
        public double WatchSpot()
        {
            double lo, hi;
            if (!SideZone(Body.X, false, out lo, out hi)) return Body.X;
            List<double> holes = PartnerHoles();
            bool clear = holes.Count == 0 || Body.X <= holes[0] || Body.X >= holes[1];
            if (Body.X >= lo && Body.X <= hi && clear) return Body.X;
            double t;
            return PickOutside(lo, hi, holes, holes.Count, out t) || PickOutside(lo, hi, holes, 0, out t) ? t : Body.X;
        }

        /// <summary>
        /// The side of the screen nearer to x (or the other side): the outer SideZone of the
        /// walkable width, where her middle may go (all of it on a narrow screen). False if
        /// there is no room to walk at all.
        /// </summary>
        bool SideZone(double x, bool other, out double lo, out double hi)
        {
            double minX = World.Left + Cfg.HalfWidth + Cfg.WalkMargin;
            double maxX = World.Right - Cfg.HalfWidth - Cfg.WalkMargin;
            lo = minX;
            hi = maxX;
            if (maxX <= minX) return false;
            double zone = Math.Max(Cfg.SideZoneMin, (maxX - minX) * Cfg.SideZone);
            if (2 * zone >= maxX - minX) return true;
            bool left = (x - minX < maxX - x) != other;
            lo = left ? minX : maxX - zone;
            hi = left ? minX + zone : maxX;
            return true;
        }

        /// <summary>The stretch around the other pet to keep out of (a from, to pair), if she knows where it is.</summary>
        List<double> PartnerHoles()
        {
            var holes = new List<double>(4);
            if (PartnerPresent && !double.IsNaN(PartnerX))
            {
                double gap = 2 * Cfg.HalfWidth + 8;
                holes.Add(PartnerX - gap);
                holes.Add(PartnerX + gap);
            }
            return holes;
        }

        /// <summary>A random point of [lo, hi] outside the first n/2 holes (from, to pairs); false if none is left.</summary>
        bool PickOutside(double lo, double hi, List<double> holes, int n, out double t)
        {
            var parts = new List<double> { lo, hi };
            for (int h = 0; h < n; h += 2)
            {
                var cut = new List<double>(parts.Count + 2);
                for (int i = 0; i < parts.Count; i += 2)
                {
                    double a = parts[i], b = parts[i + 1];
                    double l = Math.Min(b, holes[h]), r = Math.Max(a, holes[h + 1]);
                    if (l > a) { cut.Add(a); cut.Add(l); }
                    if (b > r) { cut.Add(r); cut.Add(b); }
                }
                parts = cut;
            }
            double total = 0;
            for (int i = 0; i < parts.Count; i += 2) total += parts[i + 1] - parts[i];
            t = double.NaN;
            if (total < 1e-6) return false;
            double pick = Rng.NextDouble() * total;
            for (int i = 0; i < parts.Count; i += 2)
            {
                double len = parts[i + 1] - parts[i];
                t = parts[i] + Math.Min(pick, len);
                if (pick <= len) break;
                pick -= len;
            }
            return true;
        }

        void Explode()
        {
            Stats.AddMood(-10);
            Stats.Annoyance = 100;
            Say("explode", SpeechStyle.Shout);
            Sulk.Arm(this);
            React("explode", Sulk);
            if (Exploded != null) Exploded(this);
        }

        /// <summary>Poked while sulking: she runs to the far side, away from the cursor.</summary>
        void FleeFromCursor()
        {
            double minX = World.Left + Cfg.HalfWidth + Cfg.WalkMargin;
            double maxX = World.Right - Cfg.HalfWidth - Cfg.WalkMargin;
            double cursor = hasCursor ? cursorX : Body.X;
            double target = cursor < Body.X ? maxX : minX;
            if (Math.Abs(target - Body.X) < 30) target = cursor < Body.X ? minX : maxX;
            Say("sulk_flee");
            Walk.Target = target;
            Walk.Running = true;
            Walk.Then = Sulk;
            ChangeState(Walk);
        }

        /// <summary>Held too long: she bites the hand and drops.</summary>
        public void BiteAndEscape()
        {
            dragging = false;
            pointerDown = false;
            ignoreUntilUp = true;
            ReleasePointer = true;
            Stats.Annoy(30, Time, Cfg);
            Stats.AddMood(-5);
            Say("bite");
            Body.Vx = 0;
            Body.Vy = 0;
            React("bite", Fall);
        }

        // ------------------------------------------------------------------ pointer

        public void PointerDown(double x, double y, double t)
        {
            pointerDown = true;
            dragging = false;
            ignoreUntilUp = false;
            downX = x;
            downY = y;
            grabDx = x - Body.X;
            grabDy = y - Body.Y;
            tracker.Reset();
            tracker.Add(t, x, y);
        }

        public void PointerMove(double x, double y, double t)
        {
            if (!pointerDown || ignoreUntilUp) return;
            tracker.Add(t, x, y);
            if (!dragging && (Math.Abs(x - downX) > DragThreshold || Math.Abs(y - downY) > DragThreshold))
            {
                dragging = true;
                Petting.Reset();
                Gaze.Poke(Time);
                ChangeState(Drag);
            }
            if (dragging)
            {
                Drag.TargetX = x - grabDx;
                Drag.TargetY = y - grabDy;
            }
        }

        public void PointerUp(double x, double y, double t)
        {
            if (!pointerDown) return;
            pointerDown = false;
            if (ignoreUntilUp)
            {
                ignoreUntilUp = false;
                return;
            }
            tracker.Add(t, x, y);
            if (dragging)
            {
                dragging = false;
                double vx, vy;
                tracker.Velocity(Cfg.ThrowWindow, out vx, out vy);
                double speed = Math.Sqrt(vx * vx + vy * vy);
                if (speed > Cfg.MaxThrowSpeed)
                {
                    vx *= Cfg.MaxThrowSpeed / speed;
                    vy *= Cfg.MaxThrowSpeed / speed;
                }
                Body.Vx = vx;
                Body.Vy = vy;
                ChangeState(Fall);
            }
            else
            {
                OnClick(t);
            }
        }

        public void PointerCancel()
        {
            if (!pointerDown) return;
            pointerDown = false;
            ignoreUntilUp = false;
            if (dragging)
            {
                dragging = false;
                Body.Vx = 0;
                Body.Vy = 0;
                ChangeState(Fall);
            }
        }

        /// <summary>Pointer over her without a button held (for petting and staring).</summary>
        public void PointerHover(double x, double y, double t)
        {
            if (pointerDown) return;
            if (!hovering || Math.Abs(x - hoverX) > 1.5 || Math.Abs(y - hoverY) > 1.5)
            {
                hoverX = x;
                hoverY = y;
                hoverStillSince = Time;
            }
            if (!hovering) hoverStart = Time;
            hovering = true;

            double hx, hy;
            HeadCenter(out hx, out hy);
            double rx = x - (Body.X + hx), ry = y - (Body.Y + hy);
            bool onHead = Math.Abs(rx) <= 12 && ry >= -14 && ry <= 3;
            if (onHead && Petting.Feed(x, t)) StartPetting();
        }

        public void PointerLeave()
        {
            hovering = false;
        }

        /// <summary>Global cursor position in u (fed while WantsCursor).</summary>
        public void SetCursor(double x, double y)
        {
            hasCursor = true;
            cursorX = x;
            cursorY = y;
        }

        void OnClick(double t)
        {
            bool isDouble = t - lastClickAt < DoubleClickSeconds;
            lastClickAt = t;
            Gaze.Poke(Time);
            if (state == Fall || state == Drag || state == Land) return;
            if (state == Signing)
            {
                // a click means "got it": the sign goes down (a note takes it as "yes"), nobody gets annoyed
                if (sign != null && sign.Style == SignStyle.Note) AnswerNote(0);
                else
                {
                    if (sign != null && sign.Sticky) stickyPaused = true; // until something new is copied
                    Signing.Lower(this);
                }
                return;
            }
            if (state == Work)
            {
                if (Focusing) SayWith("focus_shh", FocusMinutesLeft.ToString());
                else Say("calm_shh");
                return;
            }
            if (state == Calming)
            {
                Calming.Poke(this);
                return;
            }
            if (state == Watch)
            {
                Watch.Poke(this); // no fuss in the middle of a video
                return;
            }
            if (state.Sleeping)
            {
                Stats.AddMood(-5);
                Stats.Annoy(Cfg.PokeAnnoyance, Time, Cfg);
                Sleep.WakeUp(this, true);
                return;
            }
            if (state == Sleep) return; // already waking up
            if (state == Sulk)
            {
                FleeFromCursor();
                return;
            }
            if (state == Walk && Walk.Running) return;
            if (isDouble && state == Reacting && Reacting.Clip == "poked" && Stats.Annoyance < Cfg.AngryAt && Atlas.HasClip("bark"))
            {
                React("bark");
                Say("bark");
                return;
            }

            Stats.Annoy(Cfg.PokeAnnoyance, Time, Cfg);
            Stats.AddMood(-1.5);
            double a = Stats.Annoyance;
            if (a >= Cfg.ExplodeAt) Explode();
            else if (a >= Cfg.AngryAt)
            {
                if (state == Angry) Angry.Extend(this, 2);
                else
                {
                    Angry.Duration = 4;
                    ChangeState(Angry);
                }
                Say("angry");
            }
            else if (a >= Cfg.AnnoyedAt)
            {
                if (state == Annoyed) Annoyed.Extend(this, 1.5);
                else
                {
                    Annoyed.Duration = 2.5;
                    ChangeState(Annoyed);
                }
                Say("poke_annoyed");
            }
            else if (Atlas.HasClip("poked"))
            {
                React("poked");
                Say("poke");
            }
            else
            {
                Body.Vx = 0;
                Body.Vy = -Cfg.HopSpeed;
                ChangeState(Fall);
            }
        }

        void StartPetting()
        {
            Gaze.Poke(Time);
            if (state == Sit && Sit.Seated || state == Watch && Watch.Seated)
            {
                Stats.Affection += 0.5;
                Stats.AddMood(4);
                Say("petted");
                return;
            }
            bool cross = state == Angry || state == Annoyed || state == Sulk;
            if (!(state == Idle || cross || state == Petted)) return;
            if (state == Petted) return;
            if (cross || Stats.Annoyance >= Cfg.AnnoyedAt)
            {
                Stats.Calm(100);
                Say("forgive");
            }
            else Say("petted");
            Stats.Affection += 1;
            ChangeState(Petted);
        }

        // ------------------------------------------------------------------ attention

        /// <summary>Head centre relative to her feet, from the current frame when possible.</summary>
        public void HeadCenter(out double hx, out double hy)
        {
            int ax, ay;
            if (eyesAnchor >= 0 && Atlas.TryGetAnchor(Anim.Frame, eyesAnchor, out ax, out ay))
            {
                hx = ax - Atlas.OriginX;
                hy = ay - Atlas.OriginY - 2;
                return;
            }
            hx = 0;
            hy = -46;
        }

        void UpdateGaze(double dt)
        {
            if (!state.Calm || !hasCursor) return;
            double hx, hy;
            HeadCenter(out hx, out hy);
            hx += TuckX; // (peeking from behind an edge, her head is drawn shifted)
            hy += TuckY;
            bool wasEngaged = Gaze.Engaged;
            int h, v;
            if (Gaze.Track(cursorX - (Body.X + hx), cursorY - (Body.Y + hy), Time, dt, Rng, out h, out v))
            {
                Eyes.LookAt(h, v, Time);
            }
            else if (Gaze.JustLostInterest)
            {
                // Bored of the cursor: look the other way (a sassy glance when standing).
                if (state == Idle && Atlas.HasClip("sassy_glance")) React("sassy_glance");
                else Eyes.LookAt(Eyes.H >= 0 ? -2 : 2, 0, Time);
            }
            else if (wasEngaged)
            {
                Eyes.LookAt(0, 0, Time);
            }
        }

        /// <summary>The cursor resting on her for a while earns a "看什么看".</summary>
        void CheckStare()
        {
            if (!hovering || !state.Calm || Petting.Active) return;
            if (Time - hoverStillSince < Cfg.StareSeconds || Time - lastStareAt < 30) return;
            lastStareAt = Time;
            Say("stare");
            Gaze.Poke(Time);
            if (state == Idle && Atlas.HasClip("sassy_glance")) React("sassy_glance");
        }
    }
}
