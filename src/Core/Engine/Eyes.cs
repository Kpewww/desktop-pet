using System;

namespace Aly.Core.Engine
{
    public enum EyeSprite
    {
        OpenCenter,
        OpenLeft,
        OpenRight,
        Blink,
    }

    /// <summary>
    /// The runtime-drawn eyes: random (sometimes double) blinks and a gaze that walks one
    /// step at a time toward its target, so the eyes never jump.
    /// Gaze grid: H in -2..2 (left..right), V in -1..1 (up..down).
    /// </summary>
    public sealed class Eyes
    {
        public const double StepSeconds = 0.1;
        const double ReactionSeconds = 0.06;

        readonly Rng rng;
        readonly int[] blinkDurations;
        int targetH, targetV;
        int h, v;
        double stepAt = double.NegativeInfinity;
        double nextBlinkAt;
        int blinkFrame = -1;
        double blinkElapsed;
        bool secondBlinkPending;

        /// <summary>Set whenever what should be drawn changes; the renderer clears it.</summary>
        public bool Changed;

        public Eyes(Rng rng, int[] blinkDurations, double now)
        {
            this.rng = rng;
            this.blinkDurations = blinkDurations != null && blinkDurations.Length > 0 ? blinkDurations : new[] { 40, 70, 40 };
            nextBlinkAt = now + rng.Range(1.5, 4.0);
        }

        public int H { get { return h; } }
        public int V { get { return v; } }
        public int TargetH { get { return targetH; } }
        public int TargetV { get { return targetV; } }
        public bool Blinking { get { return blinkFrame >= 0; } }
        public int BlinkFrame { get { return blinkFrame; } }

        public EyeSprite Sprite
        {
            get
            {
                if (blinkFrame >= 0) return EyeSprite.Blink;
                if (h <= -2) return EyeSprite.OpenLeft;
                if (h >= 2) return EyeSprite.OpenRight;
                return EyeSprite.OpenCenter;
            }
        }

        /// <summary>Whole-eye shift in sprite pixels.</summary>
        public int OffsetX { get { return Math.Max(-1, Math.Min(1, h)); } }
        public int OffsetY { get { return v; } }

        public void LookAt(int hTarget, int vTarget, double now)
        {
            hTarget = Math.Max(-2, Math.Min(2, hTarget));
            vTarget = Math.Max(-1, Math.Min(1, vTarget));
            if (hTarget == targetH && vTarget == targetV) return;
            bool wasSettled = targetH == h && targetV == v;
            targetH = hTarget;
            targetV = vTarget;
            if (wasSettled) stepAt = now + ReactionSeconds;
        }

        public void Update(double now, double dt)
        {
            if ((h != targetH || v != targetV) && now >= stepAt)
            {
                h += Math.Sign(targetH - h);
                v += Math.Sign(targetV - v);
                stepAt = now + StepSeconds;
                Changed = true;
            }

            if (blinkFrame < 0)
            {
                if (now >= nextBlinkAt)
                {
                    blinkFrame = 0;
                    blinkElapsed = 0;
                    Changed = true;
                }
                return;
            }
            blinkElapsed += dt * 1000;
            while (blinkFrame >= 0 && blinkElapsed >= blinkDurations[blinkFrame])
            {
                blinkElapsed -= blinkDurations[blinkFrame];
                blinkFrame++;
                Changed = true;
                if (blinkFrame >= blinkDurations.Length)
                {
                    blinkFrame = -1;
                    ScheduleBlink(now);
                }
            }
        }

        void ScheduleBlink(double now)
        {
            if (!secondBlinkPending && rng.Chance(0.2))
            {
                secondBlinkPending = true;
                nextBlinkAt = now + 0.12;
            }
            else
            {
                secondBlinkPending = false;
                nextBlinkAt = now + rng.Range(2.0, 6.0);
            }
        }

        /// <summary>Seconds until the eyes next change on their own.</summary>
        public double NextWake(double now)
        {
            double t;
            if (blinkFrame >= 0) t = Math.Max(0, (blinkDurations[blinkFrame] - blinkElapsed) / 1000.0);
            else t = Math.Max(0, nextBlinkAt - now);
            if (h != targetH || v != targetV) t = Math.Min(t, Math.Max(0, stepAt - now));
            return t;
        }
    }
}
