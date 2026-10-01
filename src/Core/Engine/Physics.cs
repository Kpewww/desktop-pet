using System;

namespace Aly.Core.Engine
{
    /// <summary>Feet-centre position and velocity, in sprite pixels (u) and u/s.</summary>
    public sealed class Body
    {
        public double X;
        public double Y;
        public double Vx;
        public double Vy;
    }

    /// <summary>Screen-space rectangle in u. Right/Bottom are exclusive edges.</summary>
    public struct Bounds
    {
        public double Left;
        public double Top;
        public double Right;
        public double Bottom;

        public Bounds(double left, double top, double right, double bottom)
        {
            Left = left; Top = top; Right = right; Bottom = bottom;
        }

        public double Width { get { return Right - Left; } }
    }

    [Flags]
    public enum Impact
    {
        None = 0,
        Wall = 1,
        Ceiling = 2,
        Bounce = 4,
        Landed = 8,
    }

    public struct StepResult
    {
        public Impact Impacts;
        /// <summary>Largest downward speed at a floor contact during the step.</summary>
        public double FloorSpeed;
    }

    public static class Physics
    {
        const double SubStep = 0.004;

        /// <summary>Airborne integration with walls, ceiling and a bouncy floor.</summary>
        public static StepResult Fly(Body b, Bounds world, Config cfg, double dt)
        {
            var result = new StepResult();
            int steps = (int)Math.Ceiling(dt / SubStep);
            if (steps < 1) steps = 1;
            if (steps > 60) steps = 60;
            double h = dt / steps;
            double drag = Math.Exp(-cfg.AirDrag * h);
            for (int i = 0; i < steps; i++)
            {
                b.Vy += cfg.Gravity * h;
                b.Vx *= drag;
                b.X += b.Vx * h;
                b.Y += b.Vy * h;

                double minX = world.Left + cfg.HalfWidth;
                double maxX = world.Right - cfg.HalfWidth;
                if (b.X < minX)
                {
                    b.X = minX;
                    if (b.Vx < 0) { b.Vx = -b.Vx * cfg.WallRestitution; result.Impacts |= Impact.Wall; }
                }
                else if (b.X > maxX)
                {
                    b.X = maxX;
                    if (b.Vx > 0) { b.Vx = -b.Vx * cfg.WallRestitution; result.Impacts |= Impact.Wall; }
                }

                double minY = world.Top + cfg.Height;
                if (b.Y < minY)
                {
                    b.Y = minY;
                    if (b.Vy < 0) { b.Vy = -b.Vy * cfg.WallRestitution; result.Impacts |= Impact.Ceiling; }
                }

                if (b.Y >= world.Bottom)
                {
                    b.Y = world.Bottom;
                    if (b.Vy > result.FloorSpeed) result.FloorSpeed = b.Vy;
                    if (b.Vy > cfg.BounceMinSpeed)
                    {
                        b.Vy = -b.Vy * cfg.FloorRestitution;
                        b.Vx *= cfg.BounceFriction;
                        result.Impacts |= Impact.Bounce;
                    }
                    else
                    {
                        b.Vy = 0;
                        result.Impacts |= Impact.Landed;
                        return result;
                    }
                }
            }
            return result;
        }

        /// <summary>Sliding along the floor with exponential friction.</summary>
        public static void Slide(Body b, Bounds world, Config cfg, double dt)
        {
            b.Vx *= Math.Exp(-cfg.GroundFriction * dt);
            if (Math.Abs(b.Vx) < 1) b.Vx = 0;
            b.X += b.Vx * dt;
            ClampX(b, world, cfg);
            b.Y = world.Bottom;
            b.Vy = 0;
        }

        public static void ClampX(Body b, Bounds world, Config cfg)
        {
            double minX = world.Left + cfg.HalfWidth;
            double maxX = world.Right - cfg.HalfWidth;
            if (maxX < minX) { b.X = (world.Left + world.Right) / 2; return; }
            if (b.X < minX) { b.X = minX; if (b.Vx < 0) b.Vx = 0; }
            if (b.X > maxX) { b.X = maxX; if (b.Vx > 0) b.Vx = 0; }
        }
    }

    /// <summary>Estimates release velocity from the last few pointer samples.</summary>
    public sealed class VelocityTracker
    {
        const int Capacity = 32;
        readonly double[] ts = new double[Capacity];
        readonly double[] xs = new double[Capacity];
        readonly double[] ys = new double[Capacity];
        int count;
        int head;

        public void Reset()
        {
            count = 0;
            head = 0;
        }

        public void Add(double t, double x, double y)
        {
            ts[head] = t; xs[head] = x; ys[head] = y;
            head = (head + 1) % Capacity;
            if (count < Capacity) count++;
        }

        /// <summary>Average velocity over the trailing window ending at the newest sample.</summary>
        public void Velocity(double window, out double vx, out double vy)
        {
            vx = 0; vy = 0;
            if (count < 2) return;
            int newest = (head - 1 + Capacity) % Capacity;
            double tNew = ts[newest];
            int oldest = newest;
            for (int i = 1; i < count; i++)
            {
                int k = (newest - i + Capacity) % Capacity;
                if (tNew - ts[k] > window) break;
                oldest = k;
            }
            double dt = tNew - ts[oldest];
            if (dt < 0.008) return;
            vx = (xs[newest] - xs[oldest]) / dt;
            vy = (ys[newest] - ys[oldest]) / dt;
        }
    }
}
