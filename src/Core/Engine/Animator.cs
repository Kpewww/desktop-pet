using System;

namespace Aly.Core.Engine
{
    /// <summary>
    /// Plays atlas clips by real elapsed time (never by counting ticks), so timing
    /// stays exact however irregularly it is updated.
    /// </summary>
    public sealed class Animator
    {
        Atlas atlas;
        AtlasClip clip;
        int index;
        double elapsed;
        bool finished;

        /// <summary>Set whenever the displayed frame changes; the renderer clears it.</summary>
        public bool Changed;

        public Animator(Atlas atlas)
        {
            this.atlas = atlas;
        }

        public AtlasClip Clip { get { return clip; } }
        public string ClipName { get { return clip == null ? null : clip.Name; } }
        public int Index { get { return index; } }
        public bool Finished { get { return finished; } }
        public int Frame { get { return clip == null ? -1 : clip.Frames[index]; } }

        public bool Play(string name)
        {
            return Play(name, false);
        }

        public bool Play(string name, bool restart)
        {
            AtlasClip next = atlas.GetClip(name);
            if (next == null) return false;
            if (next == clip && !restart) return true;
            int before = Frame;
            clip = next;
            index = 0;
            elapsed = 0;
            finished = false;
            if (Frame != before) Changed = true;
            return true;
        }

        /// <summary>
        /// Switches to a clip of the same length while keeping the current frame index and
        /// phase — used for sync groups such as idle facing front / turned towards the cursor.
        /// </summary>
        public bool PlaySynced(string name)
        {
            AtlasClip next = atlas.GetClip(name);
            if (next == null) return false;
            if (next == clip) return true;
            if (clip == null || finished || next.Count != clip.Count) return Play(name);
            int before = Frame;
            clip = next;
            if (Frame != before) Changed = true;
            return true;
        }

        public void Update(double dtMs)
        {
            if (clip == null || finished || dtMs <= 0) return;
            int total = clip.TotalDuration;
            if (clip.Loop && total > 0 && dtMs > total * 2) dtMs = dtMs % total + total;
            int before = Frame;
            elapsed += dtMs;
            while (true)
            {
                int d = clip.Durations[index];
                if (elapsed < d) break;
                if (index + 1 < clip.Count)
                {
                    elapsed -= d;
                    index++;
                }
                else if (clip.Loop)
                {
                    elapsed -= d;
                    index = 0;
                }
                else
                {
                    elapsed = d;
                    finished = true;
                    break;
                }
            }
            if (Frame != before) Changed = true;
        }

        /// <summary>
        /// Continues the same clip, frame and timing in another atlas with the same clips (the
        /// variant without dog ears and tail).
        /// </summary>
        public void SwapAtlas(Atlas next)
        {
            atlas = next;
            if (clip == null) return;
            AtlasClip c = next.GetClip(clip.Name);
            clip = c;
            if (c == null) return;
            if (index >= c.Count) index = c.Count - 1;
            Changed = true;
        }

        /// <summary>Time until the displayed frame changes or a one-shot clip finishes.</summary>
        public double MsUntilNextEvent
        {
            get
            {
                if (clip == null || finished) return double.PositiveInfinity;
                if (clip.Loop && clip.Count == 1) return double.PositiveInfinity;
                double r = clip.Durations[index] - elapsed;
                return r < 0 ? 0 : r;
            }
        }
    }
}
