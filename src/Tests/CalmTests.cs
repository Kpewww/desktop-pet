using System.IO;
using Aly.Core;
using Aly.Core.Engine;

namespace Aly.Tests
{
    public static class CalmTests
    {
        static Pet NewPet(double x)
        {
            Atlas a = TestAtlas.Make(
                "idle", true, new[] { 300, 300 },
                "walk_l", true, new[] { 110, 110 },
                "walk_r", true, new[] { 110, 110 },
                "drag", true, new[] { 110 },
                "fall", true, new[] { 90 },
                "land", false, new[] { 60 },
                "peek", true, new[] { 3000, 200 },
                "work_start", false, new[] { 80, 90 },
                "work", true, new[] { 240, 180 },
                "work_end", false, new[] { 120, 90 },
                "sign_up", false, new[] { 80, 100, 120 },
                "sign_hold", true, new[] { 400, 150 },
                "sign_down", false, new[] { 100, 80, 80 });
            var pet = new Pet(a, new Config(), new Rng(11));
            pet.Body.X = x;
            pet.Body.Y = 500;
            pet.SetWorld(new Bounds(0, 0, 1000, 500));
            var lines = new Lines();
            lines.Load(new StringReader("[calm_shh]\n嘘——我躲好了\n[focus_shh]\n还有 {0} 分钟\n"));
            pet.Lines = lines;
            return pet;
        }

        static void Step(Pet pet, double seconds)
        {
            for (double t = 0; t < seconds; t += 1 / 60.0) pet.Update(1 / 60.0);
        }

        [Test]
        public static void PeekGoesToTheNearerCornerAndSinksToHisEyes()
        {
            Pet pet = NewPet(955);
            pet.Calm = CalmStyle.Peek;
            Step(pet, 3);
            Assert.True(pet.State == pet.Calming, "state " + pet.StateName);
            Assert.True(pet.Calming.Holding);
            Assert.Near(1000 - pet.Cfg.HalfWidth - pet.Cfg.WalkMargin, pet.Body.X, 0.01);
            Assert.True(pet.PeekDepth > 0);
            Assert.Near(pet.PeekDepth, pet.TuckY, 1e-9);
            Assert.Equal(0.0, pet.TuckX);
            Assert.False(pet.Continuous, "holding still needs no vsync");
            Assert.True(pet.NextWake >= 0.2, "only the eyes' cursor glances and blinks wake him");
        }

        [Test]
        public static void ANoteBringsHimUpAndHeGoesBackAfterwards()
        {
            Pet pet = NewPet(40);
            pet.Calm = CalmStyle.Peek;
            Step(pet, 3);
            Assert.True(pet.Calming.Holding);
            var note = new NoteRequest();
            note.Text = "该喝水啦";
            note.Buttons = new[] { "喝了", "稍后" };
            pet.ShowNote(note);
            Step(pet, 1.5);
            Assert.True(pet.State == pet.Signing, "the note is up: " + pet.StateName);
            Assert.Equal(0.0, pet.TuckY);
            pet.AnswerNote(0);
            Step(pet, 3);
            Assert.True(pet.State == pet.Calming && pet.Calming.Holding, "back down: " + pet.StateName);
        }

        [Test]
        public static void ACopiedTextDoesNotPullHimOut()
        {
            Pet pet = NewPet(40);
            pet.Calm = CalmStyle.Peek;
            Step(pet, 3);
            pet.ShowSign("剪贴板");
            Step(pet, 2);
            Assert.True(pet.State == pet.Calming && pet.Calming.Holding);
        }

        [Test]
        public static void TurningItOffBringsHimBackToNormal()
        {
            Pet pet = NewPet(40);
            pet.Calm = CalmStyle.Peek;
            Step(pet, 3);
            pet.Calm = CalmStyle.Off;
            Step(pet, 1.5);
            Assert.True(pet.State != pet.Calming, pet.StateName);
            Assert.Equal(0.0, pet.TuckY);
            Assert.False(pet.Tucked);
        }

        [Test]
        public static void HiddenSinksAllTheWay()
        {
            Pet pet = NewPet(40);
            pet.Calm = CalmStyle.Hidden;
            Step(pet, 3);
            Assert.True(pet.TuckedAway);
            Assert.Near(pet.HideDepth, pet.TuckY, 1e-9);
            Assert.True(pet.HideDepth > pet.PeekDepth);
        }

        [Test]
        public static void AClickWhilePeekingShushesAndLiftsHimForAMoment()
        {
            Pet pet = NewPet(40);
            pet.Calm = CalmStyle.Peek;
            Step(pet, 3);
            double down = pet.TuckY;
            pet.PointerDown(pet.Body.X, 480, pet.Time);
            pet.PointerUp(pet.Body.X, 480, pet.Time + 0.05);
            Assert.Equal("嘘——我躲好了", pet.Speech == null ? null : pet.Speech.Text);
            Step(pet, 0.5);
            Assert.True(pet.TuckY < down, "lifted a little");
            Step(pet, 3);
            Assert.Near(down, pet.TuckY, 1e-9);
            Assert.Equal(0.0, pet.Stats.Annoyance);
        }

        [Test]
        public static void QuietWalksToACornerAndWorksThere()
        {
            Pet pet = NewPet(980);
            pet.Calm = CalmStyle.Quiet;
            Step(pet, 4);
            Assert.True(pet.State == pet.Work, pet.StateName);
            Assert.True(pet.Work.Working);
            pet.Calm = CalmStyle.Off;
            Step(pet, 1);
            Assert.True(pet.State != pet.Work, "stands up when it ends");
        }

        [Test]
        public static void NoHellosWhileKeepingOutOfTheWay()
        {
            Pet pet = NewPet(40);
            pet.Lines.Load(new StringReader("[unlock]\n你回来啦！\n"));
            pet.Calm = CalmStyle.Peek;
            Step(pet, 3);
            pet.Greet("unlock");
            Assert.True(pet.Speech == null);
        }
    }

    public static class DistractionTests
    {
        [Test]
        public static void TheListMatchesTitlesAndProgramNames()
        {
            SiteList l = SiteList.Parse(SiteList.DistractionDefaults + "Steam\r\n电视剧：腾讯视频，芒果TV\r\n");
            Assert.Equal("小红书", l.Match("chrome", "穿搭灵感 - 小红书 - Google Chrome"));
            Assert.Equal("Steam", l.Match("steamwebhelper", "好友"));
            Assert.Equal("电视剧", l.Match("chrome", "芒果tv - 首页") ?? l.Match("x", "芒果TV"));
            Assert.True(l.Match("WINWORD", "论文 - Word") == null);
            Assert.True(l.Match("", "") == null);
        }

        [Test]
        public static void VideosAreForWatchingAlongNotForNagging()
        {
            SiteList nag = SiteList.Parse(SiteList.DistractionDefaults), videos = SiteList.Parse(SiteList.VideoDefaults);
            string[][] watching =
            {
                new[] { "chrome", "【官方】新番推荐 _哔哩哔哩_bilibili - Google Chrome" },
                new[] { "msedge", "Lo-fi beats - YOUTUBE - Microsoft Edge" },
                new[] { "chrome", "某某的直播间 - 哔哩哔哩直播，二次元弹幕直播平台" },
                new[] { "chrome", "一起看猫 - 抖音" },
                new[] { "PotPlayerMini64", "电影.mkv - PotPlayer" },
            };
            foreach (string[] w in watching)
            {
                Assert.True(videos.Match(w[0], w[1]) != null, "a video: " + w[1]);
                Assert.True(nag.Match(w[0], w[1]) == null, "never nagged about: " + w[1]);
            }
            Assert.Equal("B站", videos.Match("chrome", "【官方】新番推荐 _哔哩哔哩_bilibili - Google Chrome"));
            Assert.True(videos.Match("WINWORD", "论文 - Word") == null);
            // the list 1.2.0 first shipped still had them (the app swaps an untouched copy for the new one)
            Assert.True(SiteList.Parse(SiteList.OldDistractionDefaults).Match("chrome", "_哔哩哔哩_bilibili") != null);
        }

        [Test]
        public static void WatchingStartsAfterAFewSecondsAndOutlastsAQuickLookAway()
        {
            var w = new WatchDetector();
            Assert.False(w.Update(true, 0));
            Assert.False(w.Update(true, 3));
            Assert.True(w.Update(true, 5));
            Assert.True(w.Watching);
            Assert.False(w.Update(false, 10)); // over to a chat window for a bit
            Assert.False(w.Update(false, 30));
            Assert.False(w.Update(true, 32));  // …and back
            Assert.False(w.Update(false, 40));
            Assert.True(w.Update(false, 66));  // really done
            Assert.False(w.Watching);
            Assert.False(w.Update(false, 70));
            Assert.False(w.Update(true, 71));  // a flick past a video page is not watching
            Assert.False(w.Update(false, 72));
            Assert.False(w.Update(true, 80));
            Assert.False(w.Watching);
            w.Update(true, 90);
            Assert.True(w.Watching);
            w.Reset();
            Assert.False(w.Watching);
        }

        [Test]
        public static void NudgesAfterTheGraceThenSterner()
        {
            var w = new DistractionWatch();
            Assert.Equal(DistractionWatch.Verdict.None, w.Update("B站", 0));
            Assert.Equal(DistractionWatch.Verdict.None, w.Update("B站", 14));
            Assert.Near(1, w.NextIn(14), 1e-9);
            Assert.Equal(DistractionWatch.Verdict.Nudge, w.Update("B站", 15));
            Assert.Equal(1, w.Level);
            Assert.Equal(DistractionWatch.Verdict.None, w.Update("B站", 100));
            Assert.Equal(DistractionWatch.Verdict.Nudge, w.Update("B站", 135));
            Assert.Equal(2, w.Level);
            Assert.Equal(DistractionWatch.Verdict.Nudge, w.Update("B站", 255));
            Assert.Equal(DistractionWatch.Verdict.Nudge, w.Update("B站", 375));
            Assert.Equal(3, w.Level);
            Assert.Equal(DistractionWatch.Verdict.Back, w.Update(null, 380));
            Assert.Equal(0, w.Level);
            Assert.Equal(DistractionWatch.Verdict.None, w.Update(null, 381));
        }

        [Test]
        public static void AQuickLookIsFineAndSwitchingKeepsTheClock()
        {
            var w = new DistractionWatch();
            w.Update("B站", 0);
            Assert.Equal(DistractionWatch.Verdict.None, w.Update(null, 10)); // gone before the grace ran out: no praise either
            w.Update("B站", 20);
            Assert.Equal(DistractionWatch.Verdict.None, w.Update("抖音", 30));
            Assert.Equal(DistractionWatch.Verdict.Nudge, w.Update("抖音", 35));
            Assert.Equal("抖音", w.Site);
        }

        [Test]
        public static void ForStudyIsLeftAloneUntilTheModeEnds()
        {
            var w = new DistractionWatch();
            w.Update("B站", 0);
            w.Update("B站", 15);
            w.Allow("B站");
            Assert.Equal(DistractionWatch.Verdict.None, w.Update("B站", 200));
            Assert.Equal(DistractionWatch.Verdict.None, w.Update("B站", 500));
            w.Reset();
            w.Update("B站", 600);
            Assert.Equal(DistractionWatch.Verdict.Nudge, w.Update("B站", 615));
        }
    }
}
