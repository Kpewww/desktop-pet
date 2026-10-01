using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Aly.Core;
using Aly.Core.Engine;

namespace Aly.Tests
{
    /// <summary>The project folder, for tests that check the shipped character and lines files.</summary>
    static class Repo
    {
        public static string Root
        {
            get
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                while (dir != null && !File.Exists(Path.Combine(dir, "build.ps1"))) dir = Path.GetDirectoryName(dir.TrimEnd('\\', '/'));
                if (dir == null) throw new InvalidOperationException("project folder not found above " + AppDomain.CurrentDomain.BaseDirectory);
                return dir;
            }
        }

        public static string[] Pets
        {
            get
            {
                var list = new List<string>();
                foreach (string d in Directory.GetDirectories(Path.Combine(Root, "assets")))
                    if (File.Exists(Path.Combine(d, "character.txt"))) list.Add(Path.GetFileName(d));
                return list.ToArray();
            }
        }

        public static Character Character(string pet)
        {
            using (var r = new StreamReader(Path.Combine(Root, "assets", pet, "character.txt"), Encoding.UTF8)) return Aly.Core.Character.Parse(r);
        }

        public static LineBook Lines(string pet)
        {
            using (var r = new StreamReader(Path.Combine(Root, "assets", pet, "lines.zh.txt"), Encoding.UTF8)) return LineBook.Parse(r);
        }
    }

    public static class CharacterTests
    {
        static Character Parse(string text) { return Character.Parse(new StringReader(text)); }

        [Test]
        public static void ParsesNamesFoodsAndFeatures()
        {
            Character c = Parse(
                "key = mochi\nid = MochiPet\nname = 麻薯\npronoun = 他\nhotkey = Ctrl+Alt+K\nselffood = cola\nduty = 2\nfeatures = wardrobe calm\n" +
                "[food cola]\nlabel = 可乐\nclip = eat_cola\ncues = 1:cola 6:burp\nself = 6:burp\nselfline = self_cola\namount = 15\n" +
                "[food bbq]\nlabel = 烧烤\nclip = eat_bbq\ncues = 1:bbq\namount = 50\ngassy = yes\n");
            Assert.Equal("MochiPet", c.Id);
            Assert.Equal("麻薯", c.Nick); // defaults to the name
            Assert.Equal("他", c.Pronoun);
            Assert.Equal(2, c.Duty);
            Assert.True(c.Has("wardrobe") && c.Has("calm") && !c.Has("other"));
            Assert.Equal(2, c.Foods.Count);
            Food cola = c.FindFood("cola");
            Assert.Equal(2, cola.Cues.Length);
            Assert.Equal(6, cola.Cues[1].Frame);
            Assert.Equal("burp", cola.Cues[1].Trigger);
            Assert.Equal("self_cola", cola.SelfLine);
            Assert.True(c.FindFood("bbq").Gassy && !cola.Gassy);
            Assert.True(c.FindFood("bbq").SelfCues == null);
        }

        [Test]
        public static void MistakesAreReportedWithTheLine()
        {
            string ok = "key = a\nid = A\nname = A\n";
            AssertThrows(ok + "colour = red\n", "(4)");
            AssertThrows(ok + "[food x]\nlabel = X\nclip = eat_x\ncues = six:hello\n", "(7)");
            AssertThrows(ok + "selffood = pizza\n", "selffood");
            AssertThrows("key = a\nid = 麻薯\nname = A\n", "id");
            AssertThrows(ok + "[food x]\nclip = eat_x\n", "label");
            AssertThrows(ok + "[food x]\nlabel = X\nclip = eat_x\ncues = 6:a 1:b\n", "order");
        }

        static void AssertThrows(string text, string expected)
        {
            try
            {
                Parse(text);
            }
            catch (FormatException ex)
            {
                Assert.True(ex.Message.Contains(expected), "message '" + ex.Message + "' should mention " + expected);
                return;
            }
            throw new Exception("expected a FormatException for:\n" + text);
        }

        [Test]
        public static void TheShippedPetsParseAndStayApart()
        {
            string[] pets = Repo.Pets;
            Assert.True(pets.Length >= 1, "at least the example pet");
            var ids = new HashSet<string>();
            var hotkeys = new HashSet<string>();
            foreach (string pet in pets)
            {
                Character c = Repo.Character(pet);
                Assert.Equal(pet, c.Key);
                Assert.True(ids.Add(c.Id), "two pets share the id " + c.Id);
                Assert.True(hotkeys.Add(c.Hotkey), "two pets share the hotkey " + c.Hotkey);
                Assert.True(c.Foods.Count > 0, pet + " has something to eat");
            }
        }

        [Test]
        public static void FoodsPointAtRealClipsAndFrames()
        {
            foreach (string pet in Repo.Pets)
            {
                Character c = Repo.Character(pet);
                foreach (string file in Directory.GetFiles(Path.Combine(Repo.Root, "assets", pet), "*.atlas"))
                {
                    Atlas atlas;
                    using (var fs = File.OpenRead(file)) atlas = Atlas.Load(fs);
                    foreach (Food f in c.Foods)
                    {
                        AtlasClip clip = atlas.GetClip(f.Clip);
                        Assert.True(clip != null, Path.GetFileName(file) + " lacks " + f.Clip);
                        foreach (Cue[] cues in new[] { f.Cues, f.SelfCues ?? new Cue[0] })
                            foreach (Cue cue in cues)
                                Assert.True(cue.Frame < clip.Count, f.Clip + " has no frame " + cue.Frame);
                    }
                }
            }
        }

        [Test]
        public static void BothPetsWatchAlongAndPeekFacingYou()
        {
            foreach (string pet in Repo.Pets)
                foreach (string file in Directory.GetFiles(Path.Combine(Repo.Root, "assets", pet), "*.atlas"))
                {
                    Atlas atlas;
                    using (var fs = File.OpenRead(file)) atlas = Atlas.Load(fs);
                    string name = Path.GetFileName(file);
                    foreach (string clip in new[] { "watch", "watch_laugh", "watch_wow", "peek" })
                        Assert.True(atlas.HasClip(clip), name + " lacks " + clip);
                    if (!atlas.HasClip("peek_side_r")) continue;
                    // from a side edge he faces you: his own eyes (they blink and follow the cursor), and
                    // the edge runs through his face, so about half of it shows
                    int eyes = atlas.AnchorIndex("eyes.gaze");
                    int f = atlas.GetClip("peek_side_r").Frames[0], ex = 0, ey;
                    Assert.True(eyes >= 0 && atlas.TryGetAnchor(f, eyes, out ex, out ey), name + ": peek_side_r has runtime eyes");
                    var p = new Pet(atlas, new Config(), new Rng(1));
                    int head = ex - atlas.OriginX; // the middle of his face, from his feet
                    Assert.True(p.SidePeekDepth > head - 4 && p.SidePeekDepth < head, name + ": half a face, depth " + p.SidePeekDepth + " vs " + head);
                    Assert.True(p.SideHideDepth > head + 8, name + ": hidden means all of it");
                }
        }

        /// <summary>Every line the engine and the app ask for exists in every pet's lexicon.</summary>
        static readonly string[] Needed =
        {
            "greet_morning", "greet_afternoon", "greet_evening", "greet_night", "unlock", "welcome_back",
            "poke", "poke_annoyed", "angry", "explode", "sulk_flee", "sulk_over", "forgive", "petted",
            "grabbed", "drag_long", "bite_warn", "bite", "dizzy", "wake_grumpy", "wake", "sleepy", "stare",
            "bark", "idle", "hungry", "full", "busy", "yummy", "fart", "fart_blame",
            "toilet_go", "toilet_strain", "toilet_done", "flush",
            "focus_start", "focus_shh", "focus_stop", "note_focus_done", "break_start", "note_break_done", "focus_rest",
            "note_water", "water_ok", "note_sit", "sit_ok", "note_bedtime1", "note_bedtime2", "note_bedtime3",
            "bedtime_ok", "bedtime_later", "snooze", "todo_added", "todo_done",
            "watch_start", "watch", "watch_laugh", "watch_wow", "watch_shh",
        };

        [Test]
        public static void EveryPetHasEveryLineItNeeds()
        {
            foreach (string pet in Repo.Pets)
            {
                LineBook lines = Repo.Lines(pet);
                Character c = Repo.Character(pet);
                var needed = new List<string>(Needed);
                foreach (Food f in c.Foods)
                {
                    foreach (Cue cue in f.Cues) needed.Add(cue.Trigger);
                    if (f.SelfCues != null) foreach (Cue cue in f.SelfCues) needed.Add(cue.Trigger);
                    if (f.SelfLine != null) needed.Add(f.SelfLine);
                }
                if (c.Partner != null) needed.AddRange(new[] { "meet", "visit", "defend", "partner_asleep", "calm_shh" });
                if (c.Has("wardrobe")) needed.AddRange(new[] { "outfit_start", "outfit_done", "potion", "hair_long", "haircut", "hair_short" });
                if (c.Has("calm")) needed.AddRange(new[] { "calm_on", "calm_shh", "distract_1", "distract_2", "distract_3", "distract_back" });
                foreach (string scene in needed)
                {
                    LineBook.Scene s = lines.Find(scene);
                    Assert.True(s != null && s.Lines.Count > 0, pet + " has nothing to say for " + scene);
                }
                // {0} is only filled in where the app passes a value
                foreach (LineBook.Scene s in lines.Scenes)
                {
                    bool takesValue = s.Key == "focus_shh" || s.Key == "note_focus_done" || s.Key.StartsWith("note_bedtime") || s.Key.StartsWith("distract_");
                    foreach (string l in s.Lines)
                        Assert.True(takesValue || !l.Contains("{0}"), pet + "/" + s.Key + ": no value for {0} in " + l);
                }
            }
        }

        [Test]
        public static void WatchingAlongNeverNags()
        {
            string[] nagging = { "学习", "别看", "少看", "不看了", "关掉", "去睡", "不许看", "作业" };
            foreach (string pet in Repo.Pets)
                foreach (LineBook.Scene s in Repo.Lines(pet).Scenes)
                {
                    if (!s.Key.StartsWith("watch")) continue;
                    foreach (string l in s.Lines)
                        foreach (string n in nagging)
                            Assert.True(l.IndexOf(n, System.StringComparison.Ordinal) < 0, pet + "/" + s.Key + " nags: " + l);
                }
        }

        [Test]
        public static void OutfitsFitTheAtlases()
        {
            foreach (string pet in Repo.Pets)
            {
            string dir = Path.Combine(Repo.Root, "assets", pet);
            if (!File.Exists(Path.Combine(dir, "outfits.txt"))) continue;
            Wardrobe w;
            using (var r = new StreamReader(Path.Combine(dir, "outfits.txt"), Encoding.UTF8)) w = Wardrobe.Parse(r);
            Assert.True(w.Outfits.Count > 0);
            foreach (string file in Directory.GetFiles(dir, "*.atlas"))
            {
                Atlas a;
                string look = Path.GetFileNameWithoutExtension(file);
                using (var fs = File.OpenRead(file)) a = Atlas.Load(fs);
                foreach (Outfit o in w.Outfits)
                    foreach (var kv in o.Colors)
                        Assert.True(kv.Key > 0 && kv.Key < a.Palette.Length, o.Id + " recolours index " + kv.Key + " outside the palette");
                // the default outfit is what the atlas is drawn in
                foreach (var kv in w.Default.Colors)
                    Assert.Equal(a.Palette[kv.Key], kv.Value, look + " palette index " + kv.Key);
                foreach (string clip in new[] { "change_clothes", "potion", "haircut" })
                    Assert.True(a.HasClip(clip), look + " lacks " + clip);
            }
            }
        }

        [Test]
        public static void SceneCuesStartingWithAtGoToTheApp()
        {
            Atlas a = TestAtlas.Make(
                "idle", true, new[] { 300, 300 },
                "walk_l", true, new[] { 110 }, "walk_r", true, new[] { 110 },
                "drag", true, new[] { 110 }, "fall", true, new[] { 90 }, "land", false, new[] { 60 },
                "change_clothes", false, new[] { 100, 100, 100, 100, 100, 100 });
            var pet = new Pet(a, new Config(), new Rng(4));
            pet.Body.X = 500;
            pet.Body.Y = 500;
            pet.SetWorld(new Bounds(0, 0, 1000, 500));
            var lines = new Lines();
            lines.Load(new StringReader("[outfit_done]\n帅吗？\n"));
            pet.Lines = lines;
            var seen = new List<string>();
            pet.CueAction = seen.Add;
            int ended = 0;
            Assert.True(pet.PlayScene("change_clothes", new[] { new Cue(2, "@swap"), new Cue(4, "outfit_done") }, delegate { ended++; }));
            for (int i = 0; i < 60; i++) pet.Update(1 / 60.0);
            Assert.Equal(1, seen.Count);
            Assert.Equal("swap", seen[0]);
            Assert.True(pet.Speech != null && pet.Speech.Text == "帅吗？", "plain cues are still lines");
            Assert.Equal(1, ended);
        }

        [Test]
        public static void ACutShortSceneStillEndsOnce()
        {
            Atlas a = TestAtlas.Make(
                "idle", true, new[] { 300, 300 },
                "walk_l", true, new[] { 110 }, "walk_r", true, new[] { 110 },
                "drag", true, new[] { 110 }, "fall", true, new[] { 90 }, "land", false, new[] { 60 },
                "potion", false, new[] { 200, 200, 200, 200 });
            var pet = new Pet(a, new Config(), new Rng(5));
            pet.Body.X = 500;
            pet.Body.Y = 500;
            pet.SetWorld(new Bounds(0, 0, 1000, 500));
            int ended = 0;
            Assert.True(pet.PlayScene("potion", new[] { new Cue(2, "@swap") }, delegate { ended++; }));
            pet.Update(0.1);
            pet.PointerDown(500, 470, pet.Time);
            pet.PointerMove(540, 400, pet.Time + 0.05); // picked up before the swap
            for (int i = 0; i < 60; i++) pet.Update(1 / 60.0);
            Assert.Equal(1, ended);
        }
    }

    public static class LineBookTests
    {
        [Test]
        public static void ParsesScenesAndReplaceMarks()
        {
            LineBook b = LineBook.Parse(new StringReader("# note\n[poke]\n嗯？\n嗯？\n干嘛\n\n[idle]!\n嘿嘿\n"));
            Assert.Equal(2, b.Scenes.Count);
            Assert.Equal(2, b.Find("poke").Lines.Count); // duplicates dropped
            Assert.False(b.Find("poke").Replace);
            Assert.True(b.Find("idle").Replace);
        }

        [Test]
        public static void AddRemoveAndReplaceRoundTrip()
        {
            var b = new LineBook();
            Assert.True(b.Add("petted", "  亲亲\r\n嘻嘻 "));
            Assert.False(b.Add("petted", "亲亲 嘻嘻"), "already there");
            Assert.False(b.Add("petted", "#不行"));
            Assert.False(b.Add("petted", "[不行]"));
            Assert.False(b.Add("petted", "   "));
            Assert.False(b.Add("petted", new string('长', LineBook.MaxLength + 1)));
            b.SetReplace("idle", true);
            var w = new StringWriter();
            b.Write(w, "我的台词\n第二行");
            string text = w.ToString();
            Assert.True(text.StartsWith("# 我的台词"), text);
            LineBook back = LineBook.Parse(new StringReader(text));
            Assert.Equal("亲亲 嘻嘻", back.Find("petted").Lines[0]);
            Assert.True(back.Find("idle").Replace && back.Find("idle").Lines.Count == 0);
            Assert.True(back.Remove("petted", "亲亲 嘻嘻"));
            Assert.True(back.Find("petted") == null, "an emptied scene goes away");
            back.SetReplace("idle", false);
            Assert.Equal(0, back.Scenes.Count);
        }

        [Test]
        public static void WhatTheBookWritesTheEngineReads()
        {
            var b = new LineBook();
            b.Add("poke", "嗯？");
            b.SetReplace("poke", true);
            var w = new StringWriter();
            b.Write(w, null);
            var lines = new Lines();
            lines.Load(new StringReader("[poke]\n别戳啦！\n干嘛\n"));
            lines.Load(new StringReader(w.ToString()));
            Assert.Equal(1, lines.Count("poke"));
        }

        [Test]
        public static void HerNameFillsTheNamePlaceholder()
        {
            Pet pet = new Pet(TestAtlas.ForPet(), new Config(), new Rng(3));
            var lines = new Lines();
            lines.Load(new StringReader("[greet_morning]\n{name}！报道！\n[focus_shh]\n{name}说：还有 {0} 分钟\n"));
            pet.Lines = lines;
            pet.Name = "小柴";
            pet.Say("greet_morning");
            Assert.Equal("小柴！报道！", pet.Speech.Text);
            Assert.Equal("小柴说：还有 12 分钟", pet.Line("focus_shh", "12"));
            pet.Name = null;
            Assert.Equal("{name}！报道！", pet.Line("greet_morning"));
        }
    }
}
