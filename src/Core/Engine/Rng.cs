using System;

namespace Aly.Core.Engine
{
    /// <summary>xorshift64* — deterministic when seeded, so tests are repeatable.</summary>
    public sealed class Rng
    {
        ulong state;

        public Rng(ulong seed)
        {
            state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        }

        public ulong NextULong()
        {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            return state * 0x2545F4914F6CDD1DUL;
        }

        /// <summary>[0, 1)</summary>
        public double NextDouble()
        {
            return (NextULong() >> 11) * (1.0 / 9007199254740992.0);
        }

        public double Range(double min, double max)
        {
            return min + (max - min) * NextDouble();
        }

        /// <summary>[0, n)</summary>
        public int Next(int n)
        {
            if (n <= 0) throw new ArgumentOutOfRangeException("n");
            return (int)(NextDouble() * n);
        }

        public bool Chance(double p)
        {
            return NextDouble() < p;
        }
    }
}
