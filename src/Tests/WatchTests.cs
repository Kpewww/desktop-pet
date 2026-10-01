using System.IO;
using Aly.Core;
using Aly.Core.Engine;

namespace Aly.Tests
{
    /// <summary>陪看: sitting at her side of the screen while a video is on, and not getting in the way.</summary>
    public static class WatchTests
    {
        static Pet NewPet(double x)
        {
            Atlas a = TestAtlas.Make(
                "idle", true, new[] { 300, 300 },
                "walk_l", true, new[] { 110, 110 },
                "walk_r", true, new[] { 110, 110 },
                "walk_stop_l", false, new[] { 90, 110 },
                "walk_stop_r", false, new[] { 90, 110 },
                "drag", true, new[] { 110 },
                "fall", true, new[] { 90 },
                "land", false, new[] { 60 },
                "sit_down", false, new[] { 80, 90, 90 },
                "sit", true, new[] { 400, 120 },
                "stand_up", false, new[] { 90, 90, 120 },
                "watch", true, new[] { 2400, 160 },
                "watch_laugh", false, new[] { 90, 110, 110, 200 },
                "watch_wow", false, new[] { 80, 600, 200 },
                "sleep_down", false, new[] { 200, 140 },
                "sleep", true, new[] { 800, 800 },
                "wake", false, new[] { 150, 150 },
                "sign_up", false, new[] { 80, 100 },
                "sign_hold", true, new[] { 400, 150 },
                "sign_down", false, new[] { 100, 80 },
                "eat_cola", false, new[] { 100, 100, 100, 100 });
            var pet = new Pet(a, new Config(), new Rng(31));
            pet.Body.X = x;
            pet.Body.Y = 500;
            pet.SetWorld(new Bounds(0, 0, 1000, 500));
            var lines = new Lines();
            lines.Load(new StringReader(
                "[watch_start]\n一起看！\n[watch]\n好看！\n[watch_laugh]\n哈哈哈哈\n[watch_wow]\n哇！\n" +
                "[watch_shh]\n嘘——正精彩呢\n[poke]\n别戳\n[petted]\n嘿嘿\n[full]\n吃不下了\n"));
            pet.Lines = lines;
            var cola = new Food();
            cola.Id = "cola";
            cola.Label = "可乐";
            cola.Clip = "eat_cola";
            pet.Foods = new[] { cola };
            pet.Stats.Energy = 90;
            return pet;
        }

        static void Step(Pet pet, double seconds)
        {
            for (double t = 0; t < seconds; t += 1 / 30.0) pet.Update(1 / 30.0);
        }

        static bool OnARightSide(Pet p)
        {
            double maxX = 1000 - p.Cfg.HalfWidth - p.Cfg.WalkMargin, minX = p.Cfg.HalfWidth + p.Cfg.WalkMargin;
            return p.Body.X >= maxX - (maxX - minX) * p.Cfg.SideZone - 1e-6;
        }

        [Test]
        public static void FromTheMiddleSheGoesToHerSideAndSitsDownFacingTheScreen()
        {
            Pet pet = NewPet(560);
            pet.Watching = true;
            Step(pet, 30);
            Assert.True(pet.State == pet.Watch && pet.Watch.Seated, "watching: " + pet.StateName);
            Assert.True(OnARightSide(pet), "on the right side: " + pet.Body.X);
            Assert.Equal(-1, pet.Facing);          // towards the middle, where the video is
            Assert.Equal(-2, pet.Eyes.TargetH);
            Assert.Equal(-1, pet.Eyes.TargetV);
        }

        [Test]
        public static void OnHerSideAlreadySheSitsRightThere()
        {
            Pet pet = NewPet(80);
            pet.Watching = true;
            Step(pet, 3);
            Assert.True(pet.State == pet.Watch && pet.Watch.Seated);
            Assert.Near(80, pet.Body.X, 1e-9);
            Assert.Equal(1, pet.Facing);
        }

        [Test]
        public static void SheStaysPutForAWholeFilmAndRemarksNowAndThen()
        {
            Pet pet = NewPet(80);
            pet.Watching = true;
            pet.UserIdleSeconds = 3000; // nobody touches anything during a film: that is not "away"
            Step(pet, 2);
            int remarks = 0, lastSerial = pet.SpeechSerial;
            for (int i = 0; i < 90 * 60; i++)
            {
                Step(pet, 1);
                if (pet.Speech != null && pet.SpeechSerial != lastSerial) remarks++;
                lastSerial = pet.SpeechSerial;
                Assert.True(pet.State == pet.Watch, "still watching after " + i + " s: " + pet.StateName);
                Assert.Near(80, pet.Body.X, 1e-9, "no wandering");
            }
            Assert.True(remarks >= 6 && remarks <= 60, "a remark every few minutes: " + remarks);
        }

        [Test]
        public static void SheDoesNotWakeTheAppNeedlessly()
        {
            Pet pet = NewPet(80);
            pet.Watching = true;
            Step(pet, 3);
            Assert.False(pet.Continuous);
            // (blinks still wake her for a moment every few seconds; the watching itself waits long)
            Assert.True(pet.Watch.NextWake(pet) > 10, "long waits between glances and remarks: " + pet.Watch.NextWake(pet));
        }

        [Test]
        public static void WhenTheVideoEndsSheGetsUp()
        {
            Pet pet = NewPet(80);
            pet.Watching = true;
            Step(pet, 3);
            pet.Watching = false;
            Step(pet, 1);
            Assert.True(pet.State != pet.Watch, pet.StateName);
        }

        [Test]
        public static void ANoteGetsHerUpAndSheSitsBackDownAfterwards()
        {
            Pet pet = NewPet(80);
            pet.Watching = true;
            Step(pet, 3);
            var note = new NoteRequest();
            note.Text = "该睡觉啦";
            note.Buttons = new[] { "去睡了", "再玩一会" };
            pet.ShowNote(note);
            Step(pet, 2);
            Assert.True(pet.State == pet.Signing, "the note is up: " + pet.StateName);
            pet.AnswerNote(1);
            Step(pet, 3);
            Assert.True(pet.State == pet.Watch && pet.Watch.Seated, "back to the video: " + pet.StateName);
        }

        [Test]
        public static void APokeIsJustAWhisperAndPettingIsWelcome()
        {
            Pet pet = NewPet(80);
            pet.Watching = true;
            Step(pet, 3);
            pet.PointerDown(80, 470, pet.Time);
            pet.PointerUp(80, 470, pet.Time);
            Assert.True(pet.State == pet.Watch, pet.StateName);
            Assert.Equal("嘘——正精彩呢", pet.Speech.Text);
            Assert.True(pet.Stats.Annoyance < pet.Cfg.AnnoyedAt);
        }

        [Test]
        public static void ASnackGetsHerUpToEatThenBackToWatching()
        {
            Pet pet = NewPet(80);
            pet.Watching = true;
            Step(pet, 3);
            double full = pet.Stats.Fullness;
            Assert.True(pet.Feed("cola"));
            Step(pet, 0.5);
            Assert.True(pet.State == pet.Reacting && pet.Anim.ClipName == "eat_cola", "eating: " + pet.StateName);
            Step(pet, 4);
            Assert.True(pet.Stats.Fullness > full);
            Assert.True(pet.State == pet.Watch && pet.Watch.Seated, "watching again: " + pet.StateName);
        }

        [Test]
        public static void NoWatchingInCalmModeOrWhileFocusing()
        {
            Pet pet = NewPet(80);
            pet.Watching = true;
            pet.Focusing = true;
            Step(pet, 3);
            Assert.True(pet.State != pet.Watch, pet.StateName);
        }

        [Test]
        public static void ACopyDoesNotRaiseTheStickySignWhileWatching()
        {
            Pet pet = NewPet(80);
            pet.Watching = true;
            Step(pet, 3);
            pet.SetStickySign("复制的文字");
            Step(pet, 3);
            Assert.True(pet.State == pet.Watch, pet.StateName);
            pet.Watching = false;
            Step(pet, 3);
            Assert.True(pet.State == pet.Signing, "up once the video is over: " + pet.StateName);
        }
    }
}
