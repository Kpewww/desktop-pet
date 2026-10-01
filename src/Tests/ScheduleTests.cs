using System;
using System.Collections.Generic;
using Aly.Core.Schedule;

namespace Aly.Tests
{
    public static class ScheduleTests
    {
        static readonly DateTime Day = new DateTime(2026, 9, 24, 14, 0, 0);

        static Moment At(double minutes)
        {
            return Moment.At(Day.AddMinutes(minutes));
        }

        static Moment AtClock(int hour, int minute, int dayOffset = 0)
        {
            return Moment.At(new DateTime(2026, 9, 24, hour, minute, 0).AddDays(dayOffset));
        }

        static List<DueEvent> Poll(Planner p, Moment m, double idle = 0)
        {
            p.Health.Track(m, idle);
            return p.Poll(m, idle);
        }

        static bool Has(List<DueEvent> list, DueKind kind)
        {
            return list.Exists(e => e.Kind == kind);
        }

        static Planner NoHealth()
        {
            var p = new Planner();
            p.Health.WaterOn = p.Health.SitOn = p.Health.BedtimeOn = false;
            return p;
        }

        [Test]
        public static void APomodoroRunsFocusThenBreak()
        {
            Planner p = NoHealth();
            p.Focus.Start(At(0));
            Assert.Equal(At(25).Utc, p.NextDueUtc(At(0)).Value);
            Assert.Equal(0, Poll(p, At(24.9)).Count);
            Assert.Equal(25, p.Focus.MinutesLeft(At(0)));

            List<DueEvent> done = Poll(p, At(25));
            Assert.True(Has(done, DueKind.FocusDone));
            Assert.Equal(1, done[0].Level, "first pomodoro today");
            Assert.Equal(FocusPhase.Break, p.Focus.Phase);
            Assert.Equal(1, p.Focus.DoneToday(At(25)));

            List<DueEvent> rest = Poll(p, At(30));
            Assert.True(Has(rest, DueKind.BreakDone));
            Assert.Equal(FocusPhase.Off, p.Focus.Phase);
            p.Answer(rest[0], Answer.Yes, At(31));
            Assert.Equal(FocusPhase.Focus, p.Focus.Phase, "start the next one");
        }

        [Test]
        public static void ASessionThePcSleptThroughIsDropped()
        {
            Planner p = NoHealth();
            p.Focus.Start(At(0));
            Assert.Equal(0, Poll(p, At(180)).Count);
            Assert.Equal(FocusPhase.Off, p.Focus.Phase);
            Assert.Equal(0, p.Focus.DoneToday(At(180)));
        }

        [Test]
        public static void TheCountStartsOverTomorrow()
        {
            Planner p = NoHealth();
            p.Focus.Start(At(0));
            Poll(p, At(25));
            Assert.Equal(1, p.Focus.DoneToday(At(26)));
            Assert.Equal(0, p.Focus.DoneToday(At(24 * 60)));
        }

        [Test]
        public static void WaterComesEveryHourAndCanBePutOff()
        {
            var p = new Planner();
            p.Health.SitOn = p.Health.BedtimeOn = false;
            Assert.Equal(0, Poll(p, At(0)).Count);
            Assert.Equal(At(60).Utc, p.NextDueUtc(At(1)).Value);
            List<DueEvent> due = Poll(p, At(60));
            Assert.True(Has(due, DueKind.Water));
            Assert.Equal(0, Poll(p, At(61)).Count, "not again while the note is up");
            p.Answer(due[0], Answer.Later, At(62));
            Assert.Equal(At(72).Utc, p.NextDueUtc(At(62)).Value, "snoozed ten minutes");
            due = Poll(p, At(72));
            p.Answer(due[0], Answer.Yes, At(73));
            Assert.Equal(At(133).Utc, p.NextDueUtc(At(73)).Value, "a full hour after drinking");
        }

        [Test]
        public static void ALongAbsenceRestartsTheWaterTimer()
        {
            var p = new Planner();
            p.Health.SitOn = p.Health.BedtimeOn = false;
            Poll(p, At(0));
            Poll(p, At(50), 31 * 60);                  // away for half an hour
            Assert.Equal(0, Poll(p, At(61), 31 * 60).Count);
            Poll(p, At(62));                           // back
            Assert.Equal(At(122).Utc, p.NextDueUtc(At(62)).Value);
        }

        [Test]
        public static void SittingTooLongButABreakResetsIt()
        {
            var p = new Planner();
            p.Health.WaterOn = p.Health.BedtimeOn = false;
            Poll(p, At(0));
            Poll(p, At(30), 6 * 60);                   // got up for six minutes
            Poll(p, At(31));
            Assert.Equal(0, Poll(p, At(50)).Count, "the clock restarted at the break");
            List<DueEvent> due = Poll(p, At(81));
            Assert.True(Has(due, DueKind.Sit));
            p.Answer(due[0], Answer.Yes, At(82));
            Assert.Equal(0, Poll(p, At(100)).Count);
            Assert.True(Has(Poll(p, At(132)), DueKind.Sit), "fifty minutes after answering");
        }

        [Test]
        public static void RemindersWaitForTheUserAndForTheBreak()
        {
            var p = new Planner();
            p.Health.SitOn = p.Health.BedtimeOn = false;
            Poll(p, At(0));
            p.Focus.Start(At(40));
            Assert.Equal(0, Poll(p, At(61)).Count, "no water during a pomodoro");
            List<DueEvent> due = Poll(p, At(65), 120);  // pomodoro over, user away
            Assert.True(Has(due, DueKind.FocusDone), "timer ends are always reported");
            Assert.False(Has(due, DueKind.Water), "reminders wait for the user");
            Assert.True(Has(Poll(p, At(66)), DueKind.Water), "back, in the break: now");
        }

        [Test]
        public static void BedtimeNagsGetStrongerUntilSheIsHeard()
        {
            var p = new Planner();
            p.Health.WaterOn = p.Health.SitOn = false;
            Assert.Equal(0, Poll(p, AtClock(23, 0)).Count);
            Assert.Equal(AtClock(23, 30).Utc, p.NextDueUtc(AtClock(23, 0)).Value);
            List<DueEvent> due = Poll(p, AtClock(23, 30));
            Assert.True(Has(due, DueKind.Bedtime));
            Assert.Equal(0, due[0].Level);
            p.Answer(due[0], Answer.Later, AtClock(23, 31));
            due = Poll(p, AtClock(0, 1, 1));
            Assert.Equal(1, due[0].Level, "second nag, angrier");
            p.Answer(due[0], Answer.Yes, AtClock(0, 2, 1));
            Assert.Equal(AtClock(23, 30, 1).Utc, p.NextDueUtc(AtClock(0, 2, 1)).Value, "not again tonight");
        }

        [Test]
        public static void MorningEndsTheNightUnanswered()
        {
            var p = new Planner();
            p.Health.WaterOn = p.Health.SitOn = false;
            Poll(p, AtClock(20, 0));
            Assert.Equal(0, Poll(p, AtClock(9, 0, 1), 0).Count, "slept through it: nothing in the morning");
            Assert.Equal(AtClock(23, 30, 1).Utc, p.NextDueUtc(AtClock(9, 0, 1)).Value);
        }

        [Test]
        public static void ABedtimeAfterMidnightWorksToo()
        {
            var p = new Planner();
            p.Health.WaterOn = p.Health.SitOn = false;
            p.Health.BedtimeMinute = 60;               // 01:00
            Assert.False(p.Health.InNight(AtClock(23, 59)));
            Assert.Equal(0, Poll(p, AtClock(0, 30, 1)).Count);
            Assert.True(Has(Poll(p, AtClock(1, 10, 1)), DueKind.Bedtime));
        }

        [Test]
        public static void TodosRemindAndCanBeDoneOrPutOff()
        {
            Planner p = NoHealth();
            Todo a = p.Todos.Add("交作业", At(10).Utc, At(0).Utc);
            Todo b = p.Todos.Add("买冰沙", null, At(0).Utc);
            Assert.Equal(At(10).Utc, p.NextDueUtc(At(0)).Value);
            List<DueEvent> due = Poll(p, At(10));
            Assert.Equal(1, due.Count);
            Assert.Equal(a.Id, due[0].TodoId);
            Assert.Equal(0, Poll(p, At(11)).Count, "once");
            p.Answer(due[0], Answer.Later, At(12));
            Assert.Equal(At(22).Utc, a.DueUtc.Value);
            due = Poll(p, At(22));
            p.Answer(due[0], Answer.None, At(23));
            Assert.Equal(At(24).Utc, p.NextDueUtc(At(23)).Value, "unanswered: asked again a minute later");
            due = Poll(p, At(24));
            p.Answer(due[0], Answer.Yes, At(25));
            Assert.True(a.Done);
            Assert.Equal(null, p.NextDueUtc(At(25)));
            Assert.Equal(1, p.Todos.OpenCount);
            Assert.Equal(b.Id, p.Todos.Sorted()[0].Id, "open notes before finished ones");
        }

        [Test]
        public static void TodosSortBySoonestDue()
        {
            var list = new TodoList();
            Todo late = list.Add("晚", At(60).Utc, At(0).Utc);
            Todo none = list.Add("随时", null, At(1).Utc);
            Todo soon = list.Add("早", At(5).Utc, At(2).Utc);
            Todo done = list.Add("做完了", At(1).Utc, At(3).Utc);
            list.SetDone(done.Id, true, At(4).Utc);
            List<Todo> s = list.Sorted();
            Assert.Equal(soon.Id, s[0].Id);
            Assert.Equal(late.Id, s[1].Id);
            Assert.Equal(none.Id, s[2].Id);
            Assert.Equal(done.Id, s[3].Id);
        }
    }
}
