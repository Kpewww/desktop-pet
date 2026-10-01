using System;

namespace Aly.Core.Engine
{
    /// <summary>
    /// Recognises a head pat: the pointer sweeping back and forth over her head.
    /// Petting starts after enough direction changes in a short window and continues
    /// while the strokes keep coming.
    /// </summary>
    public sealed class PettingDetector
    {
        public double Window = 1.5;        // seconds in which the reversals must happen
        public int ReversalsToStart = 3;
        public double KeepAlive = 1.2;     // petting stops this long after the last reversal
        public double MinStroke = 2.0;     // u of travel before a direction counts

        readonly double[] reversals = new double[8];
        int count;
        int dir;
        double anchorX;
        bool hasAnchor;
        double lastReversal = double.NegativeInfinity;
        bool active;

        public bool Active { get { return active; } }

        public void Reset()
        {
            count = 0;
            dir = 0;
            hasAnchor = false;
            active = false;
        }

        /// <summary>Petting that lasts a fixed time without any strokes (menu "摸摸").</summary>
        public void Force(double t, double seconds)
        {
            active = true;
            lastReversal = t + seconds - KeepAlive;
        }

        /// <summary>Feed a pointer position that is over her head. Returns true when petting begins.</summary>
        public bool Feed(double x, double t)
        {
            if (!hasAnchor)
            {
                anchorX = x;
                hasAnchor = true;
                return false;
            }
            double dx = x - anchorX;
            if (Math.Abs(dx) < MinStroke) return false;
            int d = Math.Sign(dx);
            anchorX = x;
            if (dir != 0 && d != dir)
            {
                Push(t);
                lastReversal = t;
            }
            dir = d;
            if (!active && Recent(t) >= ReversalsToStart)
            {
                active = true;
                return true;
            }
            return false;
        }

        /// <summary>Returns true when petting just ended.</summary>
        public bool Update(double t)
        {
            if (active && t - lastReversal > KeepAlive)
            {
                active = false;
                count = 0;
                return true;
            }
            return false;
        }

        void Push(double t)
        {
            if (count == reversals.Length)
            {
                Array.Copy(reversals, 1, reversals, 0, reversals.Length - 1);
                count--;
            }
            reversals[count++] = t;
        }

        int Recent(double t)
        {
            int n = 0;
            for (int i = 0; i < count; i++) if (t - reversals[i] <= Window) n++;
            return n;
        }
    }

    /// <summary>
    /// Decides where her eyes look when the cursor is near, and when she loses interest.
    /// Interest starts full when she notices the cursor and drains (faster while the
    /// cursor sits still); at zero she looks away and ignores it for a cooldown, until the
    /// cursor leaves and comes back or someone interacts with her.
    /// </summary>
    public sealed class GazeTracker
    {
        public bool Enabled = true;
        public double Radius = 110;          // u around her head (≈220 px at 2×)
        public double DrainMoving = 0.08;    // interest/s while the cursor moves
        public double DrainStill = 0.3;      // interest/s while it rests
        public double CooldownMin = 20, CooldownMax = 40;

        double interest;
        bool engaged;
        double cooldownUntil = double.NegativeInfinity;
        bool leftSinceCooldown = true;
        double lastX, lastY, lastMoveAt;
        bool hasCursor;
        int hDisplay;

        public bool Engaged { get { return engaged; } }
        public double Interest { get { return interest; } }
        public bool CoolingDown(double now) { return now < cooldownUntil; }
        /// <summary>Set for one update when interest runs out, so the pet can react.</summary>
        public bool JustLostInterest;

        public void Poke(double now)
        {
            cooldownUntil = double.NegativeInfinity;
            leftSinceCooldown = true;
            if (engaged) interest = 1;
        }

        /// <summary>
        /// Cursor relative to her head centre (u, +x right, +y down). Returns the gaze the
        /// eyes should aim for, or null when she should look ahead on her own.
        /// </summary>
        public bool Track(double dx, double dy, double now, double dt, Rng rng, out int h, out int v)
        {
            h = 0; v = 0;
            JustLostInterest = false;
            if (!Enabled) { engaged = false; return false; }

            if (!hasCursor || Math.Abs(dx - lastX) > 1.5 || Math.Abs(dy - lastY) > 1.5)
            {
                lastMoveAt = now;
                lastX = dx;
                lastY = dy;
                hasCursor = true;
            }

            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist > Radius)
            {
                engaged = false;
                interest = 0;
                leftSinceCooldown = true;
                return false;
            }
            if (!engaged)
            {
                if (now < cooldownUntil && !leftSinceCooldown) return false;
                if (now < cooldownUntil) cooldownUntil = double.NegativeInfinity;
                engaged = true;
                interest = 1;
            }

            bool moving = now - lastMoveAt < 0.4;
            interest -= (moving ? DrainMoving : DrainStill) * dt;
            if (interest <= 0)
            {
                engaged = false;
                interest = 0;
                cooldownUntil = now + rng.Range(CooldownMin, CooldownMax);
                leftSinceCooldown = false;
                JustLostInterest = true;
                return false;
            }

            // Horizontal level with hysteresis so the eyes don't flicker at boundaries.
            double raw = dx / 22.0;
            int target = (int)Math.Round(Math.Max(-2, Math.Min(2, raw)));
            if (target != hDisplay && Math.Abs(raw - hDisplay) > 0.75) hDisplay = target;
            h = hDisplay;
            v = dy < -28 ? -1 : (dy > 14 ? 1 : 0);
            return true;
        }
    }
}
