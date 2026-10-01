using System;

namespace Aly.Core.Engine
{
    /// <summary>防干扰模式: how he keeps out of the way while you study.</summary>
    public enum CalmStyle
    {
        Off,
        /// <summary>边缘探头: tucked behind the screen edge, only the top of his head showing.</summary>
        Peek,
        /// <summary>安静陪伴: sits in a corner with his laptop, says nothing (WorkState does it).</summary>
        Quiet,
        /// <summary>完全隐身: sinks out of sight until he has something to show you.</summary>
        Hidden,
    }

    /// <summary>Where he peeks from.</summary>
    public enum PeekEdge
    {
        /// <summary>Behind the taskbar (the bottom of the work area).</summary>
        Bottom,
        Left,
        Right,
    }

    /// <summary>
    /// 边缘探头 and 完全隐身: he goes to the nearest bottom corner (or the chosen screen edge) and
    /// slides behind it — until only the top of his head shows, or all the way. The app draws
    /// him shifted by (TuckX, TuckY) and cuts off whatever is past the edge, so what is hidden is
    /// also click-through. Anything he has to show you (a note, the end of the mode) brings him
    /// back up first; a hover or a click lifts him a little for a moment.
    /// </summary>
    public sealed class CalmState : State
    {
        const double Slide = 75;          // u per second going down or coming up
        const double Lift = 9;            // u he comes up to look at you
        const double HoverDelay = 0.8;    // seconds of hovering before he does
        const double PokeLift = 2.5;      // seconds he stays up after a click

        enum Phase { Going, Down, Hold, Up }
        Phase phase;
        double depth;                     // how far in he is now, along the edge's direction (u)
        bool side;                        // tucked sideways (left/right edge) rather than down
        int sideDir;                      // -1 left edge, +1 right edge
        double liftUntil = double.NegativeInfinity;
        bool moving;

        public CalmState() : base("calm") { }

        /// <summary>Sliding needs vsync ticks; holding still needs none (only eye blinks).</summary>
        public override bool Continuous { get { return moving; } }

        /// <summary>Peeking, his eyes follow the cursor.</summary>
        public override bool Calm { get { return phase == Phase.Hold; } }

        public bool Holding { get { return phase == Phase.Hold; } }

        public override void Enter(Pet pet)
        {
            phase = Phase.Going;
            depth = 0;
            moving = false;
            side = pet.PeekEdge != PeekEdge.Bottom && pet.Atlas.HasClip("peek_side_r");
            sideDir = pet.PeekEdge == PeekEdge.Left ? -1 : 1;
            pet.TuckEdge = side ? pet.PeekEdge : PeekEdge.Bottom;
        }

        public override void Exit(Pet pet)
        {
            pet.TuckX = 0;
            pet.TuckY = 0;
            pet.TuckedAway = false;
            moving = false;
        }

        /// <summary>A click on the bit of him that shows: a quiet answer and a moment's peek.</summary>
        public void Poke(Pet pet)
        {
            if (pet.Focusing) pet.SayWith("focus_shh", pet.FocusMinutesLeft.ToString());
            else pet.Say("calm_shh");
            liftUntil = pet.Time + PokeLift;
        }

        public override void Update(Pet pet, double dt)
        {
            bool wanted = pet.Calm == CalmStyle.Peek || pet.Calm == CalmStyle.Hidden;
            if (phase != Phase.Up && (!wanted || pet.HasPendingNote))
            {
                if (phase == Phase.Going)
                {
                    pet.ChangeState(pet.Idle);
                    return;
                }
                phase = Phase.Up;
            }

            if (phase == Phase.Going)
            {
                double spot = Spot(pet);
                if (Math.Abs(pet.Body.X - spot) > 1.5)
                {
                    pet.Walk.Target = spot;
                    pet.Walk.Running = Math.Abs(pet.Body.X - spot) > pet.Cfg.TrotOver; // out of the way quickly
                    pet.Walk.Then = this;
                    pet.ChangeState(pet.Walk);
                    return;
                }
                string clip = side ? (sideDir < 0 ? "peek_side_r" : "peek_side_l") : "peek";
                if (!pet.Anim.Play(clip)) pet.Anim.Play("idle");
                if (side) pet.Facing = -sideDir;
                phase = Phase.Down;
            }

            double target = phase == Phase.Up ? 0 : Full(pet);
            if (phase == Phase.Hold && pet.Calm == CalmStyle.Peek && (pet.HoveredFor >= HoverDelay || pet.Time < liftUntil))
                target = Math.Max(0, target - Lift);
            double step = Slide * dt;
            if (depth < target) depth = Math.Min(target, depth + step);
            else if (depth > target) depth = Math.Max(target, depth - step);
            moving = Math.Abs(depth - target) > 1e-6;

            if (side) pet.TuckX = sideDir * depth;
            else pet.TuckY = depth;
            pet.TuckedAway = pet.Calm == CalmStyle.Hidden && phase == Phase.Hold && !moving;

            if (phase == Phase.Down && !moving) phase = Phase.Hold;
            if (phase == Phase.Up && !moving) pet.ChangeState(pet.Idle);
        }

        public override double NextWake(Pet pet)
        {
            if (moving) return 0;
            // hovering: check again when the lift should start; a click lift: when it ends
            double t = double.PositiveInfinity;
            if (pet.Time < liftUntil) t = liftUntil - pet.Time;
            if (pet.HoveredFor > 0 && pet.HoveredFor < HoverDelay) t = Math.Min(t, HoverDelay - pet.HoveredFor);
            return t;
        }

        /// <summary>How far in he goes (u): to the eyes for a peek, all the way for hidden.</summary>
        double Full(Pet pet)
        {
            bool hide = pet.Calm == CalmStyle.Hidden;
            if (!side) return hide ? pet.HideDepth : pet.PeekDepth;
            double toEdge = Math.Abs(EdgeX(pet) - pet.Body.X);
            return toEdge + (hide ? pet.SideHideDepth : pet.SidePeekDepth);
        }

        double EdgeX(Pet pet)
        {
            return sideDir < 0 ? pet.World.Left : pet.World.Right;
        }

        /// <summary>Where he stands before tucking in: the nearer bottom corner, or the chosen edge.</summary>
        double Spot(Pet pet)
        {
            Bounds w = pet.World;
            // at an edge: as far as walking goes (Physics.ClampX keeps his middle HalfWidth in)
            if (side) return sideDir < 0 ? w.Left + pet.Cfg.HalfWidth : w.Right - pet.Cfg.HalfWidth;
            return pet.CornerSpot();
        }
    }
}
