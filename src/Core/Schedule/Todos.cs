using System;
using System.Collections.Generic;

namespace Aly.Core.Schedule
{
    public sealed class Todo
    {
        public int Id;
        public string Text;
        /// <summary>When to remind; null = just a note.</summary>
        public DateTime? DueUtc;
        public bool Done;
        public DateTime CreatedUtc;
        public DateTime? DoneUtc;
        internal bool Alerting;
        internal DateTime NotBeforeUtc;
    }

    /// <summary>便签 / 待办. A due one is shown by her until it is done or put off.</summary>
    public sealed class TodoList
    {
        public const int SnoozeMinutes = 10;

        readonly List<Todo> items = new List<Todo>();
        int nextId = 1;

        public IList<Todo> Items { get { return items.AsReadOnly(); } }

        /// <summary>Open ones first (soonest due first, then newest), finished ones last.</summary>
        public List<Todo> Sorted()
        {
            var list = new List<Todo>(items);
            list.Sort((a, b) =>
            {
                if (a.Done != b.Done) return a.Done ? 1 : -1;
                if (a.Done) return Nullable.Compare(b.DoneUtc, a.DoneUtc);
                if (a.DueUtc.HasValue != b.DueUtc.HasValue) return a.DueUtc.HasValue ? -1 : 1;
                if (a.DueUtc.HasValue && a.DueUtc.Value != b.DueUtc.Value) return a.DueUtc.Value.CompareTo(b.DueUtc.Value);
                return b.CreatedUtc.CompareTo(a.CreatedUtc);
            });
            return list;
        }

        public Todo Add(string text, DateTime? dueUtc, DateTime nowUtc)
        {
            var t = new Todo();
            t.Id = nextId++;
            t.Text = (text ?? "").Trim();
            t.DueUtc = dueUtc;
            t.CreatedUtc = nowUtc;
            items.Add(t);
            return t;
        }

        public Todo Find(int id)
        {
            return items.Find(t => t.Id == id);
        }

        public void SetDone(int id, bool done, DateTime nowUtc)
        {
            Todo t = Find(id);
            if (t == null) return;
            t.Done = done;
            t.DoneUtc = done ? nowUtc : (DateTime?)null;
            t.Alerting = false;
        }

        public void SetDue(int id, DateTime? dueUtc)
        {
            Todo t = Find(id);
            if (t == null) return;
            t.DueUtc = dueUtc;
            t.NotBeforeUtc = DateTime.MinValue;
        }

        public void Remove(int id)
        {
            items.RemoveAll(t => t.Id == id);
        }

        public int ClearDone()
        {
            return items.RemoveAll(t => t.Done);
        }

        public int OpenCount
        {
            get
            {
                int n = 0;
                foreach (Todo t in items) if (!t.Done) n++;
                return n;
            }
        }

        public DateTime? NextDueUtc
        {
            get
            {
                DateTime? best = null;
                foreach (Todo t in items)
                {
                    if (t.Done || t.Alerting || t.DueUtc == null) continue;
                    DateTime d = t.DueUtc.Value > t.NotBeforeUtc ? t.DueUtc.Value : t.NotBeforeUtc;
                    if (best == null || d < best.Value) best = d;
                }
                return best;
            }
        }

        internal List<Todo> TakeDue(DateTime nowUtc)
        {
            var due = new List<Todo>();
            foreach (Todo t in items)
            {
                if (t.Done || t.Alerting || t.DueUtc == null || t.DueUtc.Value > nowUtc || t.NotBeforeUtc > nowUtc) continue;
                t.Alerting = true;
                due.Add(t);
            }
            return due;
        }

        internal void Answer(int id, Answer answer, DateTime nowUtc)
        {
            Todo t = Find(id);
            if (t == null) return;
            t.Alerting = false;
            if (answer == Schedule.Answer.Yes) SetDone(id, true, nowUtc);
            else if (answer == Schedule.Answer.Later) SetDue(id, nowUtc.AddMinutes(SnoozeMinutes));
            else t.NotBeforeUtc = nowUtc + Planner.Retry;
        }

        public void Restore(IEnumerable<Todo> saved)
        {
            foreach (Todo t in saved)
            {
                if (t == null) continue;
                t.Id = nextId++;
                items.Add(t);
            }
        }
    }
}
