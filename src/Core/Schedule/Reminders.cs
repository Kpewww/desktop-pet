using System;
using System.Collections.Generic;

namespace Aly.Core.Schedule
{
    /// <summary>
    /// 健康提醒: drink water every so often, get up after sitting too long, go to bed. Each one is
    /// "in flight" from the moment it fires until it is answered, so it never fires twice.
    /// </summary>
    public sealed class HealthReminders
    {
        public bool WaterOn = true;
        public int WaterMinutes = 60;
        public bool SitOn = true;
        public int SitMinutes = 50;
        public bool BedtimeOn = true;
        /// <summary>Minutes after midnight (23:30 = 1410).</summary>
        public int BedtimeMinute = 23 * 60 + 30;

        /// <summary>This long away from the keyboard counts as having got up.</summary>
        public const int SitBreakMinutes = 5;
        /// <summary>A longer absence restarts the water timer (no "drink!" the moment you sit back down).</summary>
        public const int AwayResetMinutes = 30;
        public const int SnoozeMinutes = 10;
        public const int BedtimeRepeatMinutes = 30;
        /// <summary>Bedtime nagging stops at 05:00.</summary>
        public const int MorningMinute = 5 * 60;

        // saved state
        /// <summary>MinValue = not running; it starts once the user is at the computer.</summary>
        public DateTime NextWaterUtc = DateTime.MinValue;
        /// <summary>When the current stretch of computer use began; null while away.</summary>
        public DateTime? ActiveSinceUtc;
        public DateTime SitNotBeforeUtc = DateTime.MinValue;
        /// <summary>MinValue = work it out from BedtimeMinute.</summary>
        public DateTime NextBedtimeUtc = DateTime.MinValue;
        public int BedtimeLevel;

        bool waterOut, sitOut, bedOut;

        /// <summary>Feeds how long the user has been idle; called every few seconds, also while she is hidden.</summary>
        public void Track(Moment now, double idleSeconds)
        {
            if (idleSeconds >= SitBreakMinutes * 60) ActiveSinceUtc = null;
            else if (ActiveSinceUtc == null) ActiveSinceUtc = now.Utc.AddSeconds(-idleSeconds);
            if (idleSeconds >= AwayResetMinutes * 60) NextWaterUtc = DateTime.MinValue;
            else if (NextWaterUtc == DateTime.MinValue && idleSeconds < Planner.PresentSeconds)
                NextWaterUtc = now.Utc.AddMinutes(Math.Max(1, WaterMinutes));
        }

        public DateTime? NextDueUtc(Moment now)
        {
            DateTime? best = null;
            if (WaterOn && !waterOut && NextWaterUtc != DateTime.MinValue) best = NextWaterUtc;
            if (SitOn && !sitOut && ActiveSinceUtc != null) best = Earlier(best, SitDue);
            if (BedtimeOn && !bedOut) best = Earlier(best, BedtimeDue(now));
            return best;
        }

        static DateTime? Earlier(DateTime? a, DateTime b)
        {
            return a == null || b < a.Value ? b : a;
        }

        DateTime SitDue
        {
            get
            {
                DateTime d = ActiveSinceUtc.Value.AddMinutes(Math.Max(1, SitMinutes));
                return d > SitNotBeforeUtc ? d : SitNotBeforeUtc;
            }
        }

        DateTime BedtimeDue(Moment now)
        {
            if (NextBedtimeUtc == DateTime.MinValue) NextBedtimeUtc = FirstBedtime(now);
            return NextBedtimeUtc;
        }

        internal List<DueEvent> Poll(Moment now)
        {
            var due = new List<DueEvent>();
            if (WaterOn && !waterOut && NextWaterUtc != DateTime.MinValue && now.Utc >= NextWaterUtc)
            {
                waterOut = true;
                due.Add(Event(DueKind.Water, 0));
            }
            if (SitOn && !sitOut && ActiveSinceUtc != null && now.Utc >= SitDue)
            {
                sitOut = true;
                due.Add(Event(DueKind.Sit, 0));
            }
            if (BedtimeOn && !bedOut && now.Utc >= BedtimeDue(now))
            {
                if (InNight(now))
                {
                    bedOut = true;
                    due.Add(Event(DueKind.Bedtime, BedtimeLevel));
                }
                else
                {
                    // Morning came without an answer: start over tonight.
                    BedtimeLevel = 0;
                    NextBedtimeUtc = BedtimeAfterMorning(now);
                }
            }
            return due;
        }

        static DueEvent Event(DueKind kind, int level)
        {
            var e = new DueEvent();
            e.Kind = kind;
            e.Level = level;
            return e;
        }

        internal void Answer(DueKind kind, Answer answer, Moment now)
        {
            switch (kind)
            {
                case DueKind.Water:
                    waterOut = false;
                    NextWaterUtc = answer == Schedule.Answer.Yes ? now.Utc.AddMinutes(Math.Max(1, WaterMinutes))
                        : answer == Schedule.Answer.Later ? now.Utc.AddMinutes(SnoozeMinutes) : now.Utc + Planner.Retry;
                    break;
                case DueKind.Sit:
                    sitOut = false;
                    if (answer == Schedule.Answer.Yes) ActiveSinceUtc = now.Utc; // count again from here
                    else SitNotBeforeUtc = now.Utc + (answer == Schedule.Answer.Later ? TimeSpan.FromMinutes(SnoozeMinutes) : Planner.Retry);
                    break;
                case DueKind.Bedtime:
                    bedOut = false;
                    if (answer == Schedule.Answer.Yes)
                    {
                        BedtimeLevel = 0;
                        NextBedtimeUtc = BedtimeAfterMorning(now);
                    }
                    else if (answer == Schedule.Answer.Later)
                    {
                        BedtimeLevel++;
                        NextBedtimeUtc = now.Utc.AddMinutes(BedtimeRepeatMinutes);
                    }
                    else NextBedtimeUtc = now.Utc + Planner.Retry;
                    break;
            }
        }

        /// <summary>Settings changed: timers start over.</summary>
        public void Reset()
        {
            NextWaterUtc = DateTime.MinValue;
            SitNotBeforeUtc = DateTime.MinValue;
            NextBedtimeUtc = DateTime.MinValue;
            BedtimeLevel = 0;
        }

        // ---------------------------------------------------------------- the night window

        public bool InNight(Moment now)
        {
            int t = now.Local.Hour * 60 + now.Local.Minute;
            int b = BedtimeMinute;
            return b > MorningMinute ? t >= b || t < MorningMinute : t >= b && t < MorningMinute;
        }

        /// <summary>Now, if it is already past bedtime; otherwise the coming bedtime.</summary>
        DateTime FirstBedtime(Moment now)
        {
            if (InNight(now)) return now.Utc;
            DateTime bed = now.Local.Date.AddMinutes(BedtimeMinute);
            if (bed <= now.Local) bed = bed.AddDays(1);
            return now.UtcOf(bed);
        }

        /// <summary>
        /// "Not again tonight": during the night, the first bedtime after the coming 05:00;
        /// in the daytime, simply tonight's.
        /// </summary>
        DateTime BedtimeAfterMorning(Moment now)
        {
            if (!InNight(now)) return FirstBedtime(now);
            DateTime morning = now.Local.Date.AddMinutes(MorningMinute);
            if (morning <= now.Local) morning = morning.AddDays(1);
            DateTime bed = morning.Date.AddMinutes(BedtimeMinute);
            if (bed <= morning) bed = bed.AddDays(1);
            return now.UtcOf(bed);
        }
    }
}
