using System;

namespace Aly.Core.Engine
{
    /// <summary>Relative chances of each idle choice (exposed for tests and tuning).</summary>
    public struct Weights
    {
        public double Walk, Sit, Yawn, Sleep, Stretch, Glance, Bark, Hungry, SelfFeed, Toilet, Fart, Visit, Stay;

        public double Total
        {
            get { return Walk + Sit + Yawn + Sleep + Stretch + Glance + Bark + Hungry + SelfFeed + Toilet + Fart + Visit + Stay; }
        }
    }

    /// <summary>
    /// Picks what she does next when idle, by weighted chance. Weights follow her needs
    /// and the time of day; tweak them here.
    /// </summary>
    public static class Brain
    {
        public static Weights Compute(Pet p)
        {
            Stats s = p.Stats;
            Config c = p.Cfg;
            bool tired = s.Energy < c.TiredEnergy;
            bool night = p.IsNight;
            int silly = p.EffectiveSilly;
            double lively = p.Activity <= 0 ? 0.45 : p.Activity >= 2 ? 1.7 : 1.0; // 活跃度
            var w = new Weights();
            w.Walk = (tired ? 1.2 : 3.0) * lively;
            w.Sit = (1.0 + (s.Energy < 50 ? 1.5 : 0) + (night ? 0.5 : 0)) / Math.Sqrt(lively);
            w.Yawn = s.Energy < 45 ? 1.0 : 0.12;
            w.Sleep = s.Energy < c.ExhaustedEnergy ? 5 : (tired ? 1.2 : 0) + (night ? 1.0 : 0);
            w.Stretch = p.Time - p.JustWokeAt < 120 ? 2.0 : 0.15;
            w.Glance = 0.4;
            w.Bark = s.Mood > 75 && !p.Quiet ? 0.25 : 0;
            w.Hungry = s.Fullness < c.HungryFullness && p.Time - p.LastHungryAt > 180 ? 0.8 : 0;
            w.SelfFeed = s.Fullness < c.StarvingFullness && p.Time - p.LastMealAt > c.SelfFeedAfter ? 3 : 0;
            w.Toilet = silly > 0 && s.Digestion >= c.ToiletUrge ? 4 : 0;
            w.Fart = silly > 0 ? c.FartWeight * silly * (p.Time - p.GassyMealAt < c.FartGassyWindow ? c.FartGassyBoost : 1) : 0;
            // the other pet on the desktop: a visit now and then (every quarter of an hour or so at most)
            w.Visit = p.PartnerPresent && !tired && p.Time - p.LastVisitAt > c.VisitEvery ? 0.6 * lively : 0;
            w.Stay = 2.0;
            return w;
        }

        public static void Decide(Pet p)
        {
            Stats s = p.Stats;
            Config c = p.Cfg;
            // With the silliness switched off she still "goes" — just off screen.
            if (p.EffectiveSilly == 0 && s.Digestion >= c.ToiletUrge) s.Digestion = 0;

            Weights w = Compute(p);
            double r = p.Rng.NextDouble() * w.Total;
            if ((r -= w.Walk) < 0) { p.StartWalk(); return; }
            if ((r -= w.Sit) < 0) { p.ChangeState(p.Sit); return; }
            if ((r -= w.Yawn) < 0)
            {
                p.React("yawn");
                if (s.Energy < c.TiredEnergy) p.Say("sleepy");
                return;
            }
            if ((r -= w.Sleep) < 0) { p.Sleep.Begin(p, false); return; }
            if ((r -= w.Stretch) < 0) { p.React("stretch"); return; }
            if ((r -= w.Glance) < 0) { p.React("sassy_glance"); return; }
            if ((r -= w.Bark) < 0)
            {
                p.React("bark");
                p.Say("bark");
                return;
            }
            if ((r -= w.Hungry) < 0) { p.ShowHunger(); return; }
            if ((r -= w.SelfFeed) < 0)
            {
                if (p.SelfFood == null || !p.Feed(p.SelfFood, true)) p.ShowHunger();
                return;
            }
            if ((r -= w.Toilet) < 0) { p.Toilet(); return; }
            if ((r -= w.Fart) < 0) { p.Fart(); return; }
            if ((r -= w.Visit) < 0) { p.RequestVisit(); return; }
            if (p.ChatChance(0.08)) p.Say("idle");
        }
    }
}
