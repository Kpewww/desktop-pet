using System;
using System.Collections.Generic;

namespace Aly.Core
{
    /// <summary>
    /// A list of sites and programs, one per line: "显示名: 关键词, 关键词" (or just a keyword).
    /// A window counts when its title or its program name contains a keyword (case doesn't
    /// matter) — browsers put the page title in the window title, so sites work too. One list
    /// holds 专注监督's distractions, another 陪看's videos and live streams.
    /// </summary>
    public sealed class SiteList
    {
        /// <summary>专注监督: what he nudges you about. No videos or live streams: those he watches along with you.</summary>
        public const string DistractionDefaults =
            "小红书: 小红书, xiaohongshu\r\n" +
            "微博: 微博, weibo\r\n" +
            "淘宝: 淘宝, taobao\r\n" +
            "Steam: steam\r\n" +
            "原神: 原神, genshin, yuanshen\r\n" +
            "星穹铁道: 星穹铁道, starrail\r\n" +
            "英雄联盟: 英雄联盟, league of legends, leagueclient\r\n";

        /// <summary>The distraction list 1.2.0 first came with (video sites included); an untouched copy gets the new one.</summary>
        public const string OldDistractionDefaults =
            "B站: bilibili, 哔哩哔哩\r\n" +
            "小红书: 小红书, xiaohongshu\r\n" +
            "抖音: 抖音, douyin\r\n" +
            "微博: 微博, weibo\r\n" +
            "YouTube: youtube\r\n" +
            "Netflix: netflix\r\n" +
            "爱奇艺: 爱奇艺, iqiyi\r\n" +
            "腾讯视频: 腾讯视频\r\n" +
            "优酷: 优酷, youku\r\n" +
            "斗鱼: 斗鱼, douyu\r\n" +
            "虎牙: 虎牙直播, huya\r\n" +
            "淘宝: 淘宝, taobao\r\n" +
            "Steam: steam\r\n" +
            "原神: 原神, genshin, yuanshen\r\n" +
            "星穹铁道: 星穹铁道, starrail\r\n" +
            "英雄联盟: 英雄联盟, league of legends, leagueclient\r\n";

        /// <summary>陪看: videos and live streams (sites, and a few players).</summary>
        public const string VideoDefaults =
            "B站: bilibili, 哔哩哔哩\r\n" +
            "YouTube: youtube\r\n" +
            "抖音: 抖音, douyin\r\n" +
            "快手: 快手, kuaishou\r\n" +
            "西瓜视频: 西瓜视频, ixigua\r\n" +
            "爱奇艺: 爱奇艺, iqiyi\r\n" +
            "腾讯视频: 腾讯视频\r\n" +
            "优酷: 优酷, youku\r\n" +
            "芒果TV: 芒果tv, mgtv\r\n" +
            "Netflix: netflix\r\n" +
            "斗鱼: 斗鱼, douyu\r\n" +
            "虎牙: 虎牙直播, huya\r\n" +
            "Twitch: twitch\r\n" +
            "播放器: potplayer, vlc, mpv, 迅雷影音, 电影和电视, 媒体播放器\r\n";

        readonly List<KeyValuePair<string, string[]>> entries = new List<KeyValuePair<string, string[]>>();

        public int Count { get { return entries.Count; } }

        public static SiteList Parse(string text)
        {
            var list = new SiteList();
            if (text == null) return list;
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string name = null, words = line;
                int colon = line.IndexOfAny(new[] { ':', '：' });
                if (colon > 0)
                {
                    name = line.Substring(0, colon).Trim();
                    words = line.Substring(colon + 1);
                }
                var keys = new List<string>();
                foreach (string w in words.Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string k = w.Trim().ToLowerInvariant();
                    if (k.Length > 0) keys.Add(k);
                }
                if (keys.Count == 0) continue;
                if (string.IsNullOrEmpty(name)) name = words.Trim();
                list.entries.Add(new KeyValuePair<string, string[]>(name, keys.ToArray()));
            }
            return list;
        }

        /// <summary>The display name of the first entry this window matches, or null.</summary>
        public string Match(string processName, string title)
        {
            string p = (processName ?? "").ToLowerInvariant(), t = (title ?? "").ToLowerInvariant();
            if (p.Length == 0 && t.Length == 0) return null;
            foreach (var e in entries)
                foreach (string k in e.Value)
                    if (t.Contains(k) || p.Contains(k)) return e.Key;
            return null;
        }
    }

    /// <summary>
    /// When to nudge: after a grace period on a distraction, then again every so often,
    /// sterner each time (three levels). Coming back earns a "这就对了～". What you say is for
    /// study (学习用的) is left alone until the mode ends. Times are in seconds.
    /// </summary>
    public sealed class DistractionWatch
    {
        public enum Verdict { None, Nudge, Back }

        public double Grace = 15;
        public double Every = 120;
        string site;
        int level;
        double nextAt = double.PositiveInfinity;
        readonly HashSet<string> allowed = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>What he is nudging about now (null = nothing).</summary>
        public string Site { get { return site; } }
        /// <summary>0 = not yet, 1–3 = how stern the last nudge was.</summary>
        public int Level { get { return level; } }

        /// <summary>Feed the current match (null = nothing distracting in front). Call whenever it may have changed, and when NextIn runs out.</summary>
        public Verdict Update(string match, double now)
        {
            if (match != null && allowed.Contains(match)) match = null;
            if (match == null)
            {
                bool nudged = level > 0;
                site = null;
                level = 0;
                nextAt = double.PositiveInfinity;
                return nudged ? Verdict.Back : Verdict.None;
            }
            if (site == null) nextAt = now + Grace; // a new episode (switching between distractions keeps the clock)
            site = match;
            if (now < nextAt) return Verdict.None;
            level = Math.Min(3, level + 1);
            nextAt = now + Every;
            return Verdict.Nudge;
        }

        /// <summary>Seconds until the next nudge is due (Infinity when nothing is going on).</summary>
        public double NextIn(double now)
        {
            return site == null ? double.PositiveInfinity : Math.Max(0, nextAt - now);
        }

        /// <summary>学习用的: this one is fine until Reset.</summary>
        public void Allow(string name)
        {
            if (name == null) return;
            allowed.Add(name);
            if (site == name)
            {
                site = null;
                level = 0;
                nextAt = double.PositiveInfinity;
            }
        }

        /// <summary>The mode ended: forget everything, including what was allowed.</summary>
        public void Reset()
        {
            site = null;
            level = 0;
            nextAt = double.PositiveInfinity;
            allowed.Clear();
        }
    }

    /// <summary>
    /// 陪看's timing: watching starts once a video or live stream has been in front for a few
    /// seconds, and ends only after nothing like it has been in front for a while — a quick look
    /// at another window doesn't make her get up. Times are in seconds.
    /// </summary>
    public sealed class WatchDetector
    {
        public double OnAfter = 4;
        public double OffAfter = 25;
        bool inFront;                                   // a video is in front right now
        double since;                                   // …since then
        double lastSeen = double.NegativeInfinity;      // when one last was
        bool watching;

        public bool Watching { get { return watching; } }

        /// <summary>Feed whether a video is in front now; call whenever that may have changed, and every few seconds. True when Watching changed.</summary>
        public bool Update(bool video, double now)
        {
            if (video && !inFront) since = now;
            if (video || inFront) lastSeen = now; // (one that just went was there until now)
            inFront = video;
            bool next = watching ? inFront || now - lastSeen < OffAfter : inFront && now - since >= OnAfter;
            if (next == watching) return false;
            watching = next;
            return true;
        }

        /// <summary>Stops watching straight away (陪看 switched off).</summary>
        public void Reset()
        {
            inFront = watching = false;
            lastSeen = double.NegativeInfinity;
        }
    }
}
