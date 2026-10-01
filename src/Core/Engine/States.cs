using System;

namespace Aly.Core.Engine
{
    public abstract class State
    {
        public readonly string Name;

        protected State(string name)
        {
            Name = name;
        }

        public virtual void Enter(Pet pet) { }
        public virtual void Exit(Pet pet) { }
        public abstract void Update(Pet pet, double dt);

        /// <summary>True while the body moves every frame, so vsync ticks are needed.</summary>
        public virtual bool Continuous { get { return false; } }

        /// <summary>Seconds until the state next needs an update (when not continuous).</summary>
        public virtual double NextWake(Pet pet) { return double.PositiveInfinity; }

        /// <summary>Standing or sitting quietly: her eyes may follow the cursor.</summary>
        public virtual bool Calm { get { return false; } }

        /// <summary>Asleep (energy restores; a poke wakes her up cross).</summary>
        public virtual bool Sleeping { get { return false; } }
    }

    // ------------------------------------------------------------------ idle

    public sealed class IdleState : State
    {
        double decideAt;
        double glanceAt;
        bool glancing;

        public IdleState() : base("idle") { }

        public override bool Calm { get { return true; } }

        public override void Enter(Pet pet)
        {
            pet.Anim.Play("idle");
            decideAt = pet.Time + pet.IdlePause();
            glancing = false;
            glanceAt = pet.Time + pet.Rng.Range(1.0, 3.0);
        }

        public override void Update(Pet pet, double dt)
        {
            if (pet.ShowPendingSign()) return;
            if (pet.Calm == CalmStyle.Peek || pet.Calm == CalmStyle.Hidden)
            {
                pet.ChangeState(pet.Calming); // 防干扰: off to the edge of the screen
                return;
            }
            if (pet.Calm == CalmStyle.Quiet)
            {
                pet.GoQuiet(); // 安静陪伴: a corner and the laptop
                return;
            }
            if (pet.Focusing)
            {
                pet.ChangeState(pet.Work); // keeps you company for the whole pomodoro, even if you step away
                return;
            }
            if (pet.Watching)
            {
                pet.ChangeState(pet.Watch); // 陪看 (no keyboard or mouse for a while is just watching, not away)
                return;
            }
            if (pet.UserAway)
            {
                pet.Sleep.Begin(pet, true);
                return;
            }
            if (!pet.Gaze.Engaged && pet.Time >= glanceAt) Glance(pet);
            if (pet.Time >= decideAt && pet.Time >= pet.HoldUntil) // (holding still while the other pet visits)
            {
                decideAt = pet.Time + pet.IdlePause();
                Brain.Decide(pet);
            }
        }

        /// <summary>Idle eyes wander now and then: a look to one side, then back.</summary>
        void Glance(Pet pet)
        {
            if (glancing)
            {
                pet.Eyes.LookAt(0, 0, pet.Time);
                glancing = false;
                glanceAt = pet.Time + pet.Rng.Range(2.5, 6.0);
                return;
            }
            int h = pet.Rng.Chance(0.5) ? -1 : 1;
            if (pet.Rng.Chance(0.5)) h *= 2;
            pet.Eyes.LookAt(h, pet.Rng.Chance(0.25) ? -1 : 0, pet.Time);
            glancing = true;
            glanceAt = pet.Time + pet.Rng.Range(0.8, 2.0);
        }

        public override double NextWake(Pet pet)
        {
            if (pet.HasPendingSign || pet.Focusing || pet.Watching || pet.Calm != CalmStyle.Off) return 0;
            double decide = Math.Max(decideAt, pet.HoldUntil);
            return Math.Max(0, Math.Min(Math.Min(decide, glanceAt), pet.StickyWakeAt) - pet.Time);
        }
    }

    // ------------------------------------------------------------------ walking / running

    public sealed class WalkState : State
    {
        public double Target;
        public bool Running;
        /// <summary>Where to go on arrival (default idle, via the stop animation).</summary>
        public State Then;
        /// <summary>Called once on arrival (a visit to the other pet); cleared when any walk ends.</summary>
        public Action<Pet> Arrived;
        double speed;
        int dir;
        bool steps;

        public WalkState() : base("walk") { }

        /// <summary>Smooth walking is refresh-driven; in pixel steps she only needs a wake per step.</summary>
        public override bool Continuous { get { return !steps; } }

        public override double NextWake(Pet pet)
        {
            if (!steps) return double.PositiveInfinity;
            // When her position rounds to the next whole sprite pixel.
            double x = pet.Body.X;
            double edge = dir > 0 ? Math.Floor(x + 0.5) + 0.5 : Math.Ceiling(x - 0.5) - 0.5;
            double d = Math.Abs(edge - x);
            if (d < 1e-6) d = 1;
            double v = Math.Max(speed, (Running ? pet.Cfg.RunSpeed : pet.Cfg.WalkSpeed) * 0.3);
            return d / v + 0.0005;
        }

        public override void Enter(Pet pet)
        {
            speed = 0;
            steps = pet.PixelSteps;
            dir = Target >= pet.Body.X ? 1 : -1;
            pet.Facing = dir;
            string side = dir > 0 ? "r" : "l";
            if (!(Running && pet.Anim.Play("run_" + side))) pet.Anim.Play("walk_" + side);
        }

        public override void Update(Pet pet, double dt)
        {
            Config c = pet.Cfg;
            Body b = pet.Body;
            double max = Running ? c.RunSpeed : c.WalkSpeed;
            double dist = (Target - b.X) * dir;
            if (dist <= 0.05)
            {
                b.X = Target;
                Finish(pet);
                return;
            }
            double brakeDist = speed * speed / (2 * c.WalkAccel);
            if (dist <= brakeDist) speed = Math.Max(max * 0.3, speed - c.WalkAccel * dt);
            else speed = Math.Min(max, speed + c.WalkAccel * dt);
            double step = Math.Min(dist, speed * dt);
            double intended = b.X + step * dir;
            b.X = intended;
            b.Y = pet.World.Bottom;
            Physics.ClampX(b, pet.World, c);
            if (Math.Abs(b.X - intended) > 1e-6 || step >= dist) Finish(pet);
        }

        void Finish(Pet pet)
        {
            State next = Then ?? pet.Idle;
            bool ran = Running;
            Action<Pet> arrived = Arrived;
            Then = null;
            Running = false;
            Arrived = null;
            string stop = dir > 0 ? "walk_stop_r" : "walk_stop_l";
            if (!ran && next == pet.Idle && pet.Atlas.HasClip(stop)) pet.React(stop);
            else pet.ChangeState(next);
            if (arrived != null) arrived(pet);
        }

        public override void Exit(Pet pet)
        {
            // Picked up (or anything else) on the way: the visit is off. (Finish has already
            // taken the callback it is about to call.)
            Arrived = null;
        }
    }

    // ------------------------------------------------------------------ one-shot and timed loops

    /// <summary>A line to say when a one-shot clip reaches a given frame.</summary>
    public struct Cue
    {
        public readonly int Frame;
        public readonly string Trigger;

        public Cue(int frame, string trigger)
        {
            Frame = frame;
            Trigger = trigger;
        }
    }

    /// <summary>
    /// Plays one clip once, standing still, then moves on (default: idle). Can speak at cue
    /// frames and apply an effect when it completes (a meal only counts if she finishes it).
    /// </summary>
    public sealed class ReactState : State
    {
        public string Clip;
        public State Then;
        public Cue[] Cues;
        public Action<Pet> OnDone;
        /// <summary>Called instead of OnDone when something cuts the clip short (set after Perform).</summary>
        public Action<Pet> OnAbort;
        int nextCue;
        bool completed;

        public ReactState() : base("react") { }

        public override void Enter(Pet pet)
        {
            nextCue = 0;
            completed = false;
            if (!pet.Anim.Play(Clip, true)) Done(pet);
        }

        public override void Update(Pet pet, double dt)
        {
            if (Cues != null)
            {
                while (nextCue < Cues.Length && pet.Anim.Index >= Cues[nextCue].Frame)
                {
                    string trigger = Cues[nextCue].Trigger;
                    nextCue++;
                    // "@name" is for the app (the wardrobe swaps looks there), anything else is a line
                    if (trigger.Length > 1 && trigger[0] == '@')
                    {
                        if (pet.CueAction != null) pet.CueAction(trigger.Substring(1));
                    }
                    else pet.Say(trigger);
                }
            }
            if (pet.Anim.Finished) Done(pet);
        }

        public override void Exit(Pet pet)
        {
            // Interrupted (picked up mid-meal…): the effect is lost, nothing fires later.
            Action<Pet> abort = completed ? null : OnAbort;
            Cues = null;
            OnDone = null;
            OnAbort = null;
            if (abort != null) abort(pet);
        }

        void Done(Pet pet)
        {
            Action<Pet> done = OnDone;
            State next = Then ?? pet.Idle;
            Then = null;
            OnDone = null;
            completed = true;
            if (done != null) done(pet);
            pet.ChangeState(next);
        }
    }

    /// <summary>Loops a clip for a while (annoyed, angry, dizzy), then moves on.</summary>
    public class LoopState : State
    {
        readonly string clip;
        readonly bool calm;
        public double Duration = 2.5;
        public State Then;
        double until;

        public LoopState(string name, string clip, bool calm) : base(name)
        {
            this.clip = clip;
            this.calm = calm;
        }

        public override bool Calm { get { return calm; } }

        public override void Enter(Pet pet)
        {
            pet.Anim.Play(clip, true);
            until = pet.Time + Duration;
        }

        public void Extend(Pet pet, double seconds)
        {
            until = Math.Max(until, pet.Time + seconds);
        }

        public override void Update(Pet pet, double dt)
        {
            if (pet.Time < until) return;
            State next = Then ?? pet.Idle;
            Then = null;
            pet.ChangeState(next);
        }

        public override double NextWake(Pet pet)
        {
            return Math.Max(0, until - pet.Time);
        }
    }

    // ------------------------------------------------------------------ sitting & sleeping

    public sealed class SitState : State
    {
        enum Phase { Down, Loop, Up }
        Phase phase;
        double until;

        public SitState() : base("sit") { }

        public override bool Calm { get { return phase == Phase.Loop; } }
        public bool Seated { get { return phase == Phase.Loop; } }

        public override void Enter(Pet pet)
        {
            phase = Phase.Down;
            if (!pet.Anim.Play("sit_down", true)) EnterLoop(pet);
        }

        void EnterLoop(Pet pet)
        {
            phase = Phase.Loop;
            pet.Anim.Play("sit");
            until = pet.Time + pet.Rng.Range(pet.Cfg.SitMin, pet.Cfg.SitMax);
        }

        public void StandUp(Pet pet)
        {
            if (phase == Phase.Up) return;
            phase = Phase.Up;
            if (!pet.Anim.Play("stand_up", true)) pet.ChangeState(pet.Idle);
        }

        public override void Update(Pet pet, double dt)
        {
            switch (phase)
            {
                case Phase.Down:
                    if (pet.Anim.Finished) EnterLoop(pet);
                    break;
                case Phase.Loop:
                    if (pet.UserAway || pet.Stats.Energy < pet.Cfg.ExhaustedEnergy)
                    {
                        pet.Sleep.Begin(pet, pet.UserAway);
                        return;
                    }
                    if (pet.Time >= until)
                    {
                        if (pet.Stats.Energy < pet.Cfg.TiredEnergy && pet.Rng.Chance(0.6)) pet.Sleep.Begin(pet, false);
                        else StandUp(pet);
                    }
                    break;
                case Phase.Up:
                    if (pet.Anim.Finished) pet.ChangeState(pet.Idle);
                    break;
            }
        }

        public override double NextWake(Pet pet)
        {
            return phase == Phase.Loop ? Math.Max(0, until - pet.Time) : double.PositiveInfinity;
        }
    }

    /// <summary>
    /// 专注: she sits down with her little laptop and types away for the whole pomodoro — no
    /// wandering, no silliness. Stands up again when the pomodoro is over (or stopped).
    /// </summary>
    public sealed class WorkState : State
    {
        enum Phase { Down, Loop, Up }
        Phase phase;

        public WorkState() : base("work") { }

        public bool Working { get { return phase == Phase.Loop; } }

        public override void Enter(Pet pet)
        {
            phase = Phase.Down;
            if (!pet.Anim.Play("work_start", true)) EnterLoop(pet);
        }

        void EnterLoop(Pet pet)
        {
            phase = Phase.Loop;
            if (!pet.Anim.Play("work")) pet.Anim.Play("sit");
        }

        public void StandUp(Pet pet)
        {
            if (phase == Phase.Up) return;
            phase = Phase.Up;
            if (!pet.Anim.Play("work_end", true)) pet.ChangeState(pet.Idle);
        }

        public override void Update(Pet pet, double dt)
        {
            switch (phase)
            {
                case Phase.Down:
                    if (pet.Anim.Finished) EnterLoop(pet);
                    break;
                case Phase.Loop:
                    // 安静陪伴 keeps him at it even without a pomodoro; a note gets him up (then back)
                    if (!pet.Focusing && pet.Calm != CalmStyle.Quiet || pet.HasPendingNote) StandUp(pet);
                    break;
                case Phase.Up:
                    if (pet.Anim.Finished) pet.ChangeState(pet.Idle);
                    break;
            }
        }
    }

    public sealed class SleepState : State
    {
        enum Phase { SitDown, Down, Loop, Wake, Up }
        Phase phase;
        bool forUserAway;
        bool grumpy;
        bool welcome;

        public SleepState() : base("sleep") { }

        public override bool Sleeping { get { return phase == Phase.Down || phase == Phase.Loop; } }
        public bool AwayNap { get { return forUserAway; } }

        public void Begin(Pet pet, bool userAway)
        {
            forUserAway = userAway;
            pet.ChangeState(this);
        }

        public override void Enter(Pet pet)
        {
            grumpy = false;
            welcome = false;
            if (pet.Previous == pet.Sit && pet.Sit.Seated || pet.Previous == pet.Watch && pet.Watch.Seated)
            {
                phase = Phase.Down;
                if (!pet.Anim.Play("sleep_down", true)) EnterLoop(pet);
            }
            else
            {
                phase = Phase.SitDown;
                if (!pet.Anim.Play("sit_down", true))
                {
                    phase = Phase.Down;
                    pet.Anim.Play("sleep_down", true);
                }
            }
        }

        void EnterLoop(Pet pet)
        {
            phase = Phase.Loop;
            pet.Anim.Play("sleep");
        }

        public void WakeUp(Pet pet, bool cross)
        {
            if (phase == Phase.Wake || phase == Phase.Up) return;
            grumpy = cross;
            phase = Phase.Wake;
            if (!pet.Anim.Play("wake", true)) AfterWake(pet);
        }

        void AfterWake(Pet pet)
        {
            phase = Phase.Up;
            if (welcome) pet.Say("welcome_back");
            else if (grumpy) pet.Say("wake_grumpy");
            else pet.Say("wake");
            if (!pet.Anim.Play("stand_up", true)) Finish(pet);
        }

        void Finish(Pet pet)
        {
            pet.JustWokeAt = pet.Time;
            forUserAway = false;
            if (grumpy)
            {
                pet.Annoyed.Duration = 2.5;
                pet.ChangeState(pet.Annoyed);
            }
            else pet.ChangeState(pet.Idle);
        }

        public override void Update(Pet pet, double dt)
        {
            switch (phase)
            {
                case Phase.SitDown:
                    if (pet.Anim.Finished)
                    {
                        phase = Phase.Down;
                        if (!pet.Anim.Play("sleep_down", true)) EnterLoop(pet);
                    }
                    break;
                case Phase.Down:
                    if (pet.Anim.Finished) EnterLoop(pet);
                    break;
                case Phase.Loop:
                    if (forUserAway)
                    {
                        if (pet.UserIdleSeconds < 2)
                        {
                            welcome = true;
                            WakeUp(pet, false);
                        }
                    }
                    else if (pet.Stats.Energy >= 98 && !pet.IsNight) WakeUp(pet, false);
                    break;
                case Phase.Wake:
                    if (pet.Anim.Finished) AfterWake(pet);
                    break;
                case Phase.Up:
                    if (pet.Anim.Finished) Finish(pet);
                    break;
            }
        }
    }

    // ------------------------------------------------------------------ the clipboard sign

    /// <summary>
    /// Holds up a little sign on a stick: raise, hold (it sways), lower. New content while
    /// she holds it just flips the board over (see Pet.ShowSign).
    /// </summary>
    public sealed class SignState : State
    {
        /// <summary>A reminder note nobody answers goes down after this long (and counts as "later").</summary>
        public const double NoteSeconds = 300;

        enum Phase { Up, Hold, Down }
        Phase phase;
        double until;
        bool timedOut;

        public SignState() : base("sign") { }

        public override bool Calm { get { return phase == Phase.Hold; } }
        public bool Holding { get { return phase == Phase.Hold; } }
        public bool Lowering { get { return phase == Phase.Down; } }

        public override void Enter(Pet pet)
        {
            phase = Phase.Up;
            timedOut = false;
            if (!pet.Anim.Play("sign_up", true)) EnterHold(pet);
        }

        void EnterHold(Pet pet)
        {
            phase = Phase.Hold;
            pet.Anim.Play("sign_hold");
            Rearm(pet);
        }

        /// <summary>Starts the hold time again (new content arrived).</summary>
        public void Rearm(Pet pet)
        {
            SignContent s = pet.Sign;
            if (s != null && s.Style == SignStyle.Note) until = pet.Time + NoteSeconds;
            else if (s != null && s.Sticky) until = pet.Time + pet.Rng.Range(pet.Cfg.StickyHoldMin, pet.Cfg.StickyHoldMax);
            else until = pet.SignSeconds > 0 ? pet.Time + pet.SignSeconds : double.PositiveInfinity;
        }

        public void Lower(Pet pet)
        {
            if (phase == Phase.Down) return;
            phase = Phase.Down;
            if (!pet.Anim.Play("sign_down", true)) pet.ChangeState(pet.Idle);
        }

        public override void Update(Pet pet, double dt)
        {
            switch (phase)
            {
                case Phase.Up:
                    if (pet.Anim.Finished) EnterHold(pet);
                    break;
                case Phase.Hold:
                    // "until clicked" still ends when nobody is there to read it
                    if (pet.Time >= until)
                    {
                        timedOut = true;
                        if (pet.Sign != null && pet.Sign.Sticky) pet.StickyBreak(); // a little stroll, then back up
                        Lower(pet);
                    }
                    else if (pet.UserAway) Lower(pet);
                    break;
                case Phase.Down:
                    if (pet.Anim.Finished) pet.ChangeState(pet.Idle);
                    break;
            }
        }

        public override void Exit(Pet pet)
        {
            pet.ClearSign(timedOut);
        }

        public override double NextWake(Pet pet)
        {
            return phase == Phase.Hold ? Math.Max(0, until - pet.Time) : double.PositiveInfinity;
        }
    }

    // ------------------------------------------------------------------ feelings

    /// <summary>Back turned, arms crossed. Ends on its own; petting forgives early.</summary>
    public sealed class SulkState : State
    {
        double until;

        public SulkState() : base("sulk") { }

        /// <summary>Starts (or restarts) the sulk timer; re-entering after fleeing keeps it.</summary>
        public void Arm(Pet pet)
        {
            until = pet.Time + pet.Cfg.SulkSeconds;
        }

        public override void Enter(Pet pet)
        {
            pet.Anim.Play("sulk", true);
            if (until <= pet.Time) Arm(pet);
        }

        public override void Update(Pet pet, double dt)
        {
            if (pet.Time < until) return;
            pet.Stats.Calm(100);
            pet.Say("sulk_over");
            pet.ChangeState(pet.Idle);
        }

        public override double NextWake(Pet pet)
        {
            return Math.Max(0, until - pet.Time);
        }
    }

    public sealed class PettedState : State
    {
        public PettedState() : base("petted") { }

        public override void Enter(Pet pet)
        {
            pet.Anim.Play("petted");
        }

        public override void Update(Pet pet, double dt)
        {
            Config c = pet.Cfg;
            pet.Stats.AddMood(c.PetMoodPerSecond * dt);
            pet.Stats.Calm(c.PetCalmPerSecond * dt);
            pet.Stats.Affection += 0.05 * dt;
            if (!pet.Petting.Active) pet.React("happy");
        }
    }

    // ------------------------------------------------------------------ being handled

    public sealed class DragState : State
    {
        public double TargetX;
        public double TargetY;
        double since;
        bool warned;

        public DragState() : base("drag") { }

        public override bool Continuous { get { return true; } }
        public double HeldSeconds(Pet pet) { return pet.Time - since; }

        public override void Enter(Pet pet)
        {
            if (!pet.Anim.Play("grabbed", true)) pet.Anim.Play("drag");
            pet.Body.Vx = 0;
            pet.Body.Vy = 0;
            TargetX = pet.Body.X;
            TargetY = pet.Body.Y;
            since = pet.Time;
            warned = false;
            if (pet.ChatChance(0.5)) pet.Say("grabbed");
        }

        public override void Update(Pet pet, double dt)
        {
            Config c = pet.Cfg;
            double held = pet.Time - since;
            string clip = pet.Anim.ClipName;
            if (clip == "grabbed" && pet.Anim.Finished) pet.Anim.Play("drag");
            else if (clip == "drag" && held > c.StruggleHardAfter)
            {
                pet.Anim.Play("struggle_hard");
                pet.Say("drag_long");
            }
            if (!warned && held > c.BiteWarnAfter)
            {
                warned = true;
                pet.Say("bite_warn");
            }
            if (held > c.BiteAfter)
            {
                pet.BiteAndEscape();
                return;
            }

            Bounds db = pet.DragBounds;
            pet.Body.X = Clamp(TargetX, db.Left + c.HalfWidth, db.Right - c.HalfWidth);
            pet.Body.Y = Clamp(TargetY, db.Top + c.Height, db.Bottom);
        }

        static double Clamp(double v, double min, double max)
        {
            if (max < min) return (min + max) / 2;
            return v < min ? min : (v > max ? max : v);
        }
    }

    public sealed class FallState : State
    {
        public FallState() : base("fall") { }

        public override bool Continuous { get { return true; } }

        public override void Enter(Pet pet)
        {
            pet.Anim.Play("fall");
            pet.LastImpactSpeed = 0;
        }

        public override void Update(Pet pet, double dt)
        {
            StepResult r = Physics.Fly(pet.Body, pet.World, pet.Cfg, dt);
            if (r.FloorSpeed > pet.LastImpactSpeed) pet.LastImpactSpeed = r.FloorSpeed;
            if ((r.Impacts & Impact.Landed) == 0) return;
            if (pet.LastImpactSpeed > pet.Cfg.DizzySpeed && pet.Atlas.HasClip("dizzy"))
            {
                pet.Stats.AddMood(-6);
                pet.Stats.Annoy(25, pet.Time, pet.Cfg);
                pet.Say("dizzy");
                pet.Dizzy.Duration = pet.Cfg.DizzySeconds;
                pet.ChangeState(pet.Dizzy);
            }
            else pet.ChangeState(pet.Land);
        }
    }

    public sealed class LandState : State
    {
        public LandState() : base("land") { }

        public override bool Continuous { get { return true; } }

        public override void Enter(Pet pet)
        {
            pet.Anim.Play("land", true);
        }

        public override void Update(Pet pet, double dt)
        {
            Physics.Slide(pet.Body, pet.World, pet.Cfg, dt);
            if (pet.Anim.Finished && pet.Body.Vx == 0) pet.ChangeState(pet.Idle);
        }
    }
}
