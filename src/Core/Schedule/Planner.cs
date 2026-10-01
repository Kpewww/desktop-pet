using System;
using System.Collections.Generic;

namespace Aly.Core.Schedule
{
    /// <summary>A point in time, in UTC (for durations) and local time (for clock times like bedtime).</summary>
    public struct Moment
    {
        public readonly DateTime Utc;
        public readonly DateTime Local;

        public Moment(DateTime utc, DateTime local)
        {
            Utc = utc;
            Local = local;
        }

        public static Moment Now
        {
            get
            {
                DateTime utc = DateTime.UtcNow;
                return new Moment(utc, utc.ToLocalTime());
            }
        }

        /// <summary>For tests: local time equal to UTC.</summary>
        public static Moment At(DateTime t)
        {
            return new Moment(DateTime.SpecifyKind(t, DateTimeKind.Utc), DateTime.SpecifyKind(t, DateTimeKind.Local));
        }

        public TimeSpan UtcOffset { get { return Local - Utc; } }

        public Moment Plus(TimeSpan d)
        {
            return new Moment(Utc + d, Local + d);
        }

        /// <summary>The UTC instant of a local wall-clock time, at today's offset.</summary>
        public DateTime UtcOf(DateTime local)
        {
            return DateTime.SpecifyKind(local - UtcOffset, DateTimeKind.Utc);
        }
    }

    public enum DueKind
    {
        FocusDone,
        BreakDone,
        Water,
        Sit,
        Bedtime,
        Todo,
    }

    /// <summary>Something that needs her to speak up. It stays "in flight" until answered.</summary>
    public sealed class DueEvent
    {
        public DueKind Kind;
        /// <summary>Todo: which one.</summary>
        public int TodoId;
        /// <summary>Bedtime: how many nags already (0, 1, 2…). FocusDone: pomodoros done today.</summary>
        public int Level;

        public override string ToString() { return Kind + (Kind == DueKind.Todo ? "#" + TodoId : "") + "/" + Level; }
    }

    /// <summary>How an event was answered.</summary>
    public enum Answer
    {
        /// <summary>The note went away unanswered (she was picked up, the user left…): ask again soon.</summary>
        None = -1,
        /// <summary>First button, or a click on her: done / drank / going to bed / start.</summary>
        Yes = 0,
        /// <summary>Second button: later / not now.</summary>
        Later = 1,
    }

    /// <summary>
    /// The one scheduler behind the pomodoro timer, the health reminders and the to-dos.
    /// It never ticks by itself: the app asks for NextDue, sets a single timer for it, and
    /// polls when it fires (or whenever she wakes up anyway).
    /// </summary>
    public sealed class Planner
    {
        /// <summary>Reminders wait until the user has touched the keyboard or mouse this recently.</summary>
        public const double PresentSeconds = 60;
        /// <summary>An unanswered note is asked again after this long.</summary>
        public static readonly TimeSpan Retry = TimeSpan.FromMinutes(1);

        public readonly Pomodoro Focus = new Pomodoro();
        public readonly HealthReminders Health = new HealthReminders();
        public readonly TodoList Todos = new TodoList();

        /// <summary>Earliest moment anything may become due; null when nothing is scheduled.</summary>
        public DateTime? NextDueUtc(Moment now)
        {
            DateTime? best = Focus.NextDueUtc;
            best = Min(best, Todos.NextDueUtc);
            if (Focus.Phase != FocusPhase.Focus) best = Min(best, Health.NextDueUtc(now));
            return best;
        }

        static DateTime? Min(DateTime? a, DateTime? b)
        {
            if (a == null) return b;
            if (b == null) return a;
            return a.Value <= b.Value ? a : b;
        }

        /// <summary>
        /// Everything that is due now. Timer ends are reported even with nobody there (the next
        /// phase has to start); reminders wait for the user to be at the computer, and health
        /// reminders also wait out a pomodoro.
        /// </summary>
        public List<DueEvent> Poll(Moment now, double userIdleSeconds)
        {
            var due = new List<DueEvent>();
            DueEvent e = Focus.Poll(now);
            if (e != null) due.Add(e);
            if (userIdleSeconds > PresentSeconds) return due;
            foreach (Todo t in Todos.TakeDue(now.Utc))
            {
                var te = new DueEvent();
                te.Kind = DueKind.Todo;
                te.TodoId = t.Id;
                due.Add(te);
            }
            if (Focus.Phase != FocusPhase.Focus) due.AddRange(Health.Poll(now));
            return due;
        }

        public void Answer(DueEvent e, Answer answer, Moment now)
        {
            switch (e.Kind)
            {
                case DueKind.FocusDone:
                    break; // the break started when it fired
                case DueKind.BreakDone:
                    if (answer == Schedule.Answer.Yes) Focus.Start(now);
                    break;
                case DueKind.Todo:
                    Todos.Answer(e.TodoId, answer, now.Utc);
                    break;
                default:
                    Health.Answer(e.Kind, answer, now);
                    break;
            }
        }
    }
}
