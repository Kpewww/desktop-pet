using System;

namespace Aly.Core.Engine
{
    /// <summary>
    /// Her needs and feelings. All 0..100 except Affection, which only grows.
    /// Annoyance is short-term (seconds); the others move over minutes and hours.
    /// Food goes into the stomach, is digested slowly and turns into the need for the toilet.
    /// </summary>
    public sealed class Stats
    {
        public double Energy = 80;
        public double Mood = 70;
        public double Affection;
        public double Annoyance;
        public double Fullness = 70;
        /// <summary>Food eaten but not yet digested.</summary>
        public double Stomach;
        /// <summary>How badly she needs the toilet.</summary>
        public double Digestion;

        double annoyanceHoldUntil;

        public void Tick(double now, double dt, bool sleeping, Config c)
        {
            if (sleeping) Energy += c.EnergyRestorePerSecond * dt;
            else Energy -= c.EnergyDrainPerSecond * dt;
            Energy = Clamp(Energy);

            double toward = c.MoodBaseline - Mood;
            double step = c.MoodDriftPerSecond * dt;
            Mood += Math.Abs(toward) <= step ? toward : Math.Sign(toward) * step;
            if (Fullness < c.StarvingFullness) Mood = Clamp(Mood - c.MoodDriftPerSecond * dt);

            if (now >= annoyanceHoldUntil)
                Annoyance = Math.Max(0, Annoyance - c.AnnoyanceDecayPerSecond * dt);

            Fullness = Clamp(Fullness - c.FullnessDrainPerSecond * dt * (sleeping ? 0.5 : 1));
            double digested = Math.Min(Stomach, c.DigestPerSecond * dt);
            Stomach -= digested;
            Digestion = Clamp(Digestion + digested * c.DigestToUrge);
        }

        /// <summary>Adds annoyance and pauses its decay briefly, so quick pokes stack up.</summary>
        public void Annoy(double amount, double now, Config c)
        {
            Annoyance = Clamp(Annoyance + amount);
            annoyanceHoldUntil = now + c.AnnoyanceHoldSeconds;
        }

        public void Calm(double amount)
        {
            Annoyance = Math.Max(0, Annoyance - amount);
        }

        public void AddMood(double amount) { Mood = Clamp(Mood + amount); }

        public void Eat(double amount)
        {
            Fullness = Clamp(Fullness + amount);
            Stomach = Clamp(Stomach + amount);
        }

        /// <summary>
        /// Time passed while the app was closed: she slept through it, got hungrier, and
        /// dealt with the toilet on her own.
        /// </summary>
        public void Offline(double seconds, Config c)
        {
            if (seconds <= 0) return;
            seconds = Math.Min(seconds, 7 * 24 * 3600);
            Energy = Clamp(Energy + c.EnergyRestorePerSecond * seconds);
            Mood = c.MoodBaseline;
            Annoyance = 0;
            Fullness = Math.Max(Math.Min(Fullness, 5), Fullness - c.FullnessDrainPerSecond * seconds * 0.5);
            Stomach = 0;
            Digestion = 0;
        }

        internal static double Clamp(double v) { return v < 0 ? 0 : (v > 100 ? 100 : v); }
    }
}
