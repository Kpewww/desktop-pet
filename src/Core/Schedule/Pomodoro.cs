using System;

namespace Aly.Core.Schedule
{
    public enum FocusPhase
    {
        Off,
        Focus,
        Break,
    }

    /// <summary>
    /// 番茄钟: focus, then a break. End times are wall-clock (UTC), so the timer keeps running
    /// while she is hidden, and a session the PC slept through is simply dropped.
    /// </summary>
    public sealed class Pomodoro
    {
        /// <summary>A session that ended this long ago (PC asleep, app closed) doesn't count.</summary>
        public static readonly TimeSpan Abandoned = TimeSpan.FromMinutes(30);

        public int FocusMinutes = 25;
        public int BreakMinutes = 5;

        public FocusPhase Phase { get; private set; }
        public DateTime EndsUtc { get; private set; }
        int doneToday;
        DateTime countDay;

        public DateTime? NextDueUtc { get { return Phase == FocusPhase.Off ? (DateTime?)null : EndsUtc; } }

        public void Start(Moment now)
        {
            Phase = FocusPhase.Focus;
            EndsUtc = now.Utc.AddMinutes(Math.Max(1, FocusMinutes));
        }

        public void Stop()
        {
            Phase = FocusPhase.Off;
        }

        public TimeSpan Remaining(Moment now)
        {
            if (Phase == FocusPhase.Off) return TimeSpan.Zero;
            TimeSpan r = EndsUtc - now.Utc;
            return r > TimeSpan.Zero ? r : TimeSpan.Zero;
        }

        /// <summary>Whole minutes left, rounded up (what she says when you poke her).</summary>
        public int MinutesLeft(Moment now)
        {
            return (int)Math.Ceiling(Remaining(now).TotalMinutes);
        }

        public int DoneToday(Moment now)
        {
            return countDay == now.Local.Date ? doneToday : 0;
        }

        /// <summary>Focus over: counted, the break starts. Break over: back to off.</summary>
        internal DueEvent Poll(Moment now)
        {
            if (Phase == FocusPhase.Off || now.Utc < EndsUtc) return null;
            bool stale = now.Utc - EndsUtc > Abandoned;
            var e = new DueEvent();
            if (Phase == FocusPhase.Focus)
            {
                if (stale)
                {
                    Phase = FocusPhase.Off;
                    return null;
                }
                if (countDay != now.Local.Date)
                {
                    countDay = now.Local.Date;
                    doneToday = 0;
                }
                doneToday++;
                Phase = FocusPhase.Break;
                EndsUtc = now.Utc.AddMinutes(Math.Max(1, BreakMinutes));
                e.Kind = DueKind.FocusDone;
                e.Level = doneToday;
                return e;
            }
            Phase = FocusPhase.Off;
            if (stale) return null;
            e.Kind = DueKind.BreakDone;
            return e;
        }

        // ---- persistence
        public void Restore(FocusPhase phase, DateTime endsUtc, int done, DateTime day)
        {
            Phase = phase;
            EndsUtc = endsUtc;
            doneToday = done;
            countDay = day.Date;
        }

        public int SavedCount { get { return doneToday; } }
        public DateTime SavedDay { get { return countDay; } }
    }
}
