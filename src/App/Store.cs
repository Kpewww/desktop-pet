using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Aly.App
{
    /// <summary>Small JSON files under %APPDATA%\(character id), written atomically.</summary>
    static class Store
    {
        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Profile.Me.Id);

        static DataContractJsonSerializer Serializer(Type t)
        {
            var s = new DataContractJsonSerializerSettings();
            s.UseSimpleDictionaryFormat = true;
            return new DataContractJsonSerializer(t, s);
        }

        public static T Load<T>(string file) where T : class
        {
            string path = Path.Combine(Dir, file);
            try
            {
                if (!File.Exists(path)) return null;
                using (var fs = File.OpenRead(path))
                {
                    return (T)Serializer(typeof(T)).ReadObject(fs);
                }
            }
            catch (Exception)
            {
                // A damaged file must never stop her from starting; keep it for inspection.
                try { File.Copy(path, path + ".bad", true); } catch (Exception) { }
                return null;
            }
        }

        public static void Save<T>(string file, T value)
        {
            Directory.CreateDirectory(Dir);
            string path = Path.Combine(Dir, file);
            string tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(fs, Encoding.UTF8, false, true))
                {
                    Serializer(typeof(T)).WriteObject(writer, value);
                    writer.Flush();
                }
                fs.Flush(true);
            }
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
    }

    [DataContract]
    sealed class Settings
    {
        /// <summary>The name she goes by (设置 · 名字); empty = the character's own.</summary>
        [DataMember] public string Name;
        /// <summary>0 = pick automatically from the monitor DPI, otherwise 1–4.</summary>
        [DataMember] public int Scale;
        [DataMember] public bool Debug;
        /// <summary>Hide (and stop all work) while a full-screen app or presentation runs.</summary>
        [DataMember] public bool HideInFullscreen = true;
        /// <summary>正经模式: no speech bubbles, no silliness.</summary>
        [DataMember] public bool Quiet;
        /// <summary>Her eyes follow the cursor when it comes near.</summary>
        [DataMember] public bool FollowCursor = true;
        /// <summary>搞怪频率: 0 off, 1 normal, 2 often.</summary>
        [DataMember] public int Silly = 1;
        /// <summary>活跃度: 0 calm, 1 normal, 2 lively.</summary>
        [DataMember] public int Activity = 1;
        /// <summary>说话频率: 0 rarely, 1 normal, 2 chatty.</summary>
        [DataMember] public int Chatty = 1;
        /// <summary>Ears and tail (whatever the pet has); the name is kept for older files.</summary>
        [DataMember] public bool DogEars = true;

        // wardrobe (pets that have one)
        /// <summary>Outfit id from outfits.txt; null = the first (default) one.</summary>
        [DataMember] public string Outfit;
        /// <summary>Hairstyle look ("short", "long"); null = the character's default look.</summary>
        [DataMember] public string Hair;
        /// <summary>每天早上自己挑一套衣服.</summary>
        [DataMember] public bool AutoOutfit;
        /// <summary>The day (yyyy-MM-dd) he last picked his own outfit.</summary>
        [DataMember] public string AutoOutfitDay;

        // 防干扰模式 (pets that have it)
        /// <summary>Switched on by hand (the tray menu); it also comes on by itself during a pomodoro when CalmAuto.</summary>
        [DataMember] public bool CalmOn;
        /// <summary>1 边缘探头, 2 安静陪伴, 3 完全隐身 (Aly.Core.Engine.CalmStyle).</summary>
        [DataMember] public int CalmStyle = 1;
        /// <summary>开始专注时自动打开.</summary>
        [DataMember] public bool CalmAuto = true;
        /// <summary>0 任务栏后面, 1 屏幕左边, 2 屏幕右边.</summary>
        [DataMember] public int PeekEdge;
        /// <summary>安静陪伴时的透明度, percent.</summary>
        [DataMember] public int QuietOpacity = 100;
        /// <summary>专注监督: nudges when a distracting site or program is in front.</summary>
        [DataMember] public bool Supervise = true;
        [DataMember] public int SuperviseGrace = 15;
        /// <summary>分心名单, one "显示名: 关键词, 关键词" per line.</summary>
        [DataMember] public string DistractList = Aly.Core.SiteList.DistractionDefaults;

        // 陪看 (both pets)
        /// <summary>A video or live stream in front: she sits at her side and watches along, and nothing gets in the way.</summary>
        [DataMember] public bool WatchAlong = true;
        /// <summary>陪看名单, same format as the 分心名单.</summary>
        [DataMember] public string VideoList = Aly.Core.SiteList.VideoDefaults;

        // clipboard
        /// <summary>举牌方式: 0 常驻显示, 1 复制时举一会儿, 2 平时不显示（点「看看剪贴板」才显示）; -1 = older file, work it out.</summary>
        [DataMember] public int SignMode = 1;
        /// <summary>Older switch, superseded by SignMode (kept so old files still read right).</summary>
        [DataMember] public bool ClipSign = true;
        /// <summary>How long she holds it when copying (SignMode 1): 5, 8 or 15 seconds.</summary>
        [DataMember] public int SignSeconds = 8;
        [DataMember] public bool ClipPaused;
        /// <summary>Keep the history across restarts (encrypted file). Off: memory only.</summary>
        [DataMember] public bool ClipPersist;
        [DataMember] public bool ClipHotkeyOn = true;
        [DataMember] public string ClipHotkey = Profile.Me.Hotkey;

        // focus & reminders
        [DataMember] public int FocusMinutes = 25;
        [DataMember] public int BreakMinutes = 5;
        [DataMember] public bool WaterOn = true;
        [DataMember] public int WaterMinutes = 60;
        [DataMember] public bool SitOn = true;
        [DataMember] public int SitMinutes = 50;
        [DataMember] public bool BedtimeOn = true;
        /// <summary>Minutes after midnight; 23:30 = 1410.</summary>
        [DataMember] public int BedtimeMinute = 23 * 60 + 30;

        [OnDeserializing]
        void Defaults(StreamingContext context)
        {
            HideInFullscreen = true;
            FollowCursor = true;
            Silly = 1;
            Activity = 1;
            Chatty = 1;
            DogEars = true;
            SignMode = -1;
            ClipSign = true;
            SignSeconds = 8;
            ClipHotkeyOn = true;
            ClipHotkey = Profile.Me.Hotkey;
            FocusMinutes = 25;
            BreakMinutes = 5;
            WaterOn = true;
            WaterMinutes = 60;
            SitOn = true;
            SitMinutes = 50;
            BedtimeOn = true;
            BedtimeMinute = 23 * 60 + 30;
            CalmStyle = 1;
            CalmAuto = true;
            QuietOpacity = 100;
            Supervise = true;
            SuperviseGrace = 15;
            DistractList = Aly.Core.SiteList.DistractionDefaults;
            WatchAlong = true;
            VideoList = Aly.Core.SiteList.VideoDefaults;
        }
    }

    [DataContract]
    sealed class SavedState
    {
        /// <summary>Feet position as a fraction of the work area width; -1 = not saved yet.</summary>
        [DataMember] public double PosX = -1;
        [DataMember] public string Monitor;
        [DataMember] public bool Hidden;

        // needs & feelings; -1 = never saved
        [DataMember] public double Energy = -1;
        [DataMember] public double Mood = -1;
        [DataMember] public double Affection;
        [DataMember] public double Fullness = -1;
        [DataMember] public double Digestion;
        [DataMember] public double Stomach;
        [DataMember] public string LastSeenUtc;
        [DataMember] public string LastGreetDate;

        // the pomodoro and reminder timers (ISO 8601 UTC strings; null = not set)
        [DataMember] public int FocusPhase;
        [DataMember] public string FocusEndsUtc;
        [DataMember] public int FocusCount;
        [DataMember] public string FocusDay;
        [DataMember] public string NextWaterUtc;
        [DataMember] public string SitNotBeforeUtc;
        [DataMember] public string NextBedtimeUtc;
        [DataMember] public int BedtimeLevel;

        // 专注监督: 今天被抓包几次
        [DataMember] public string CaughtDay;
        [DataMember] public int CaughtCount;

        [OnDeserializing]
        void Defaults(StreamingContext context)
        {
            PosX = -1;
            Energy = -1;
            Mood = -1;
            Fullness = -1;
        }
    }

    /// <summary>todos.json: 便签 / 待办.</summary>
    [DataContract]
    sealed class SavedTodos
    {
        [DataMember] public System.Collections.Generic.List<SavedTodo> Items = new System.Collections.Generic.List<SavedTodo>();

        [OnDeserializing]
        void Defaults(StreamingContext context)
        {
            Items = new System.Collections.Generic.List<SavedTodo>();
        }
    }

    [DataContract]
    sealed class SavedTodo
    {
        [DataMember] public string Text;
        [DataMember] public string DueUtc;
        [DataMember] public bool Done;
        [DataMember] public string CreatedUtc;
        [DataMember] public string DoneUtc;
    }

    /// <summary>Round-trips DateTime values as ISO 8601 UTC strings.</summary>
    static class Iso
    {
        public static string Write(DateTime? utc)
        {
            if (utc == null || utc.Value == DateTime.MinValue || utc.Value == DateTime.MaxValue) return null;
            return DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static DateTime? Read(string s)
        {
            DateTime t;
            if (string.IsNullOrEmpty(s) || !DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out t)) return null;
            return t.ToUniversalTime();
        }
    }
}
