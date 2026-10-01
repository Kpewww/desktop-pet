using System;

namespace Aly.Core.Engine
{
    /// <summary>
    /// 陪看: a video or a live stream is on (the app says so). She goes to her side of the screen,
    /// sits down and watches along: eyes on the screen, now and then a laugh, a "哇" or a thought
    /// about it — and never a word about stopping. No wandering, no silliness, no clipboard signs;
    /// a note that can't wait still gets her up, and she sits back down afterwards.
    /// </summary>
    public sealed class WatchState : State
    {
        enum Phase { Going, Down, Loop, React, Up }
        Phase phase;
        double commentAt, glanceAt;
        bool glancing;
        Action<Pet> after, abort;   // what standing up is for (a snack…), and what to do if it gets cut short

        public WatchState() : base("watch") { }

        public bool Seated { get { return phase == Phase.Loop || phase == Phase.React; } }

        public override void Enter(Pet pet)
        {
            phase = Phase.Going;
            after = abort = null;
        }

        public override void Exit(Pet pet)
        {
            // picked up (or anything else) before she was up: whatever she stood up for is off
            Action<Pet> a = abort;
            after = abort = null;
            if (a != null) a(pet);
        }

        /// <summary>Gets up, then does then (if she gets that far; otherwise cut runs instead).</summary>
        public void StandUp(Pet pet, Action<Pet> then, Action<Pet> cut = null)
        {
            if (then != null || cut != null)
            {
                after = then;
                abort = cut;
            }
            if (phase == Phase.Up) return;
            if (phase == Phase.Going)
            {
                Finish(pet);
                return;
            }
            phase = Phase.Up;
            if (!pet.Anim.Play("stand_up", true)) Finish(pet);
        }

        void Finish(Pet pet)
        {
            Action<Pet> a = after;
            after = abort = null;
            pet.ChangeState(pet.Idle);
            if (a != null) a(pet);
        }

        /// <summary>A click while she watches: a whisper and a quick look at you.</summary>
        public void Poke(Pet pet)
        {
            pet.Say("watch_shh");
            if (phase != Phase.Loop) return;
            pet.Eyes.LookAt(0, 0, pet.Time);
            glancing = true;
            glanceAt = pet.Time + 1.5;
        }

        public override void Update(Pet pet, double dt)
        {
            bool stay = pet.Watching && pet.Calm == CalmStyle.Off && !pet.Focusing && !pet.HasPendingNote;
            switch (phase)
            {
                case Phase.Going:
                    if (!stay)
                    {
                        pet.ChangeState(pet.Idle);
                        return;
                    }
                    double spot = pet.WatchSpot();
                    if (Math.Abs(pet.Body.X - spot) > 1.5)
                    {
                        pet.Walk.Target = spot;
                        pet.Walk.Running = Math.Abs(pet.Body.X - spot) > pet.Cfg.TrotOver; // not a long parade across the video
                        pet.Walk.Then = this;
                        pet.ChangeState(pet.Walk);
                        return;
                    }
                    phase = Phase.Down;
                    if (!pet.Anim.Play("sit_down", true)) EnterLoop(pet, true);
                    if (pet.ChatChance(0.5)) pet.Say("watch_start");
                    break;
                case Phase.Down:
                    if (!stay) StandUp(pet, null);
                    else if (pet.Anim.Finished) EnterLoop(pet, true);
                    break;
                case Phase.Loop:
                    if (!stay) StandUp(pet, null);
                    else if (pet.Stats.Energy < pet.Cfg.ExhaustedEnergy) pet.Sleep.Begin(pet, false); // nodded off in front of it
                    else if (pet.Time >= commentAt) Comment(pet);
                    else if (pet.Time >= glanceAt) Glance(pet);
                    break;
                case Phase.React:
                    if (!stay) StandUp(pet, null);
                    else if (pet.Anim.Finished) EnterLoop(pet, false);
                    break;
                case Phase.Up:
                    if (pet.Anim.Finished) Finish(pet);
                    break;
            }
        }

        void EnterLoop(Pet pet, bool first)
        {
            phase = Phase.Loop;
            if (!pet.Anim.Play("watch")) pet.Anim.Play("sit");
            LookAtScreen(pet);
            glancing = false;
            glanceAt = pet.Time + pet.Rng.Range(20, 60);
            if (first) commentAt = pet.Time + Gap(pet) * 0.6;
        }

        /// <summary>The video is up there, towards the middle of the screen.</summary>
        static void LookAtScreen(Pet pet)
        {
            double mid = (pet.World.Left + pet.World.Right) / 2;
            int toward = pet.Body.X < mid ? 1 : -1;
            pet.Facing = toward;
            pet.Eyes.LookAt(2 * toward, -1, pet.Time);
        }

        /// <summary>Seconds until her next laugh or remark, scaled by 说话频率.</summary>
        static double Gap(Pet pet)
        {
            double k = pet.Chatty <= 0 ? 2.5 : pet.Chatty >= 2 ? 0.5 : 1.0;
            return pet.Rng.Range(pet.Cfg.WatchCommentMin, pet.Cfg.WatchCommentMax) * k;
        }

        /// <summary>A laugh, a "哇", or just a thought about what's on.</summary>
        void Comment(Pet pet)
        {
            commentAt = pet.Time + Gap(pet);
            if (pet.Quiet) return; // 正经模式: she watches in silence
            double r = pet.Rng.NextDouble();
            string clip = r < 0.3 ? "watch_laugh" : r < 0.45 ? "watch_wow" : null;
            if (clip != null && pet.Atlas.HasClip(clip))
            {
                phase = Phase.React;
                pet.Anim.Play(clip, true);
                pet.Say(clip);
            }
            else pet.Say("watch");
        }

        /// <summary>Now and then her eyes leave the screen for a moment (a look at you), then go back.</summary>
        void Glance(Pet pet)
        {
            if (glancing)
            {
                LookAtScreen(pet);
                glancing = false;
                glanceAt = pet.Time + pet.Rng.Range(20, 60);
                return;
            }
            pet.Eyes.LookAt(0, 0, pet.Time);
            glancing = true;
            glanceAt = pet.Time + pet.Rng.Range(1.0, 2.2);
        }

        public override double NextWake(Pet pet)
        {
            return phase == Phase.Loop ? Math.Max(0, Math.Min(commentAt, glanceAt) - pet.Time) : double.PositiveInfinity;
        }
    }
}
