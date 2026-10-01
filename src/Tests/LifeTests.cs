using System.IO;
using Aly.Core;
using Aly.Core.Engine;

namespace Aly.Tests
{
    public static class LifeTests
    {
        static Pet NewPet(ulong seed)
        {
            Atlas a = TestAtlas.Make(
                "idle", true, new[] { 300, 300 },
                "walk_l", true, new[] { 110, 110 },
                "walk_r", true, new[] { 110, 110 },
                "drag", true, new[] { 110 },
                "fall", true, new[] { 90 },
                "land", false, new[] { 60 },
                "hungry", true, new[] { 320, 140 },
                "eat_smoothie", false, new[] { 90, 120, 250, 150, 350, 250, 90, 90, 90, 90, 250, 120, 150 },
                "eat_yakiniku", false, new[] { 90, 150, 150, 150, 150, 180, 150, 150, 150, 180, 150, 150, 320, 120, 150 },
                "fart", false, new[] { 200, 80, 120, 220, 250 },
                "toilet", false, new[] { 100, 120, 100, 120, 300, 100, 100, 100, 300, 150, 400, 300, 300, 200, 120 });
            var pet = new Pet(a, new Config(), new Rng(seed));
            pet.Body.X = 500;
            pet.Body.Y = 500;
            pet.SetWorld(new Bounds(0, 0, 1000, 500));
            TestFoods.Give(pet);
            var lines = new Lines();
            lines.Load(new StringReader("[freeze]\n好冰！\n[yummy]\n好吃！\n[toilet_done]\n呼…舒服了\n[full]\n吃不下了…\n[self_smoothie]\n我自己买冰沙去！"));
            pet.Lines = lines;
            return pet;
        }

        static void Step(Pet pet, double seconds)
        {
            for (double t = 0; t < seconds; t += 1 / 60.0) pet.Update(1 / 60.0);
        }

        [Test]
        public static void FinishingAMealFillsHerUpAndCues()
        {
            Pet pet = NewPet(1);
            pet.Stats.Fullness = 40;
            Assert.True(pet.Feed("smoothie"));
            Assert.Equal("eat_smoothie", pet.Anim.ClipName);
            bool froze = false;
            for (int i = 0; i < 60 * 3; i++)
            {
                pet.Update(1 / 60.0);
                if (pet.Speech != null && pet.Speech.Text == "好冰！") froze = true;
            }
            Assert.True(froze, "brain-freeze line at its cue frame");
            Assert.True(pet.Stats.Fullness > 60, "fullness " + pet.Stats.Fullness);
            Assert.True(pet.Stats.Stomach > 20);
        }

        [Test]
        public static void AnInterruptedMealDoesNotCount()
        {
            Pet pet = NewPet(2);
            pet.Stats.Fullness = 40;
            pet.Feed("yakiniku");
            Step(pet, 0.5);
            pet.PointerDown(500, 470, pet.Time);
            pet.PointerMove(540, 400, pet.Time + 0.05); // picked up mid-meal
            Step(pet, 3);
            Assert.True(pet.Stats.Fullness < 45);
        }

        [Test]
        public static void SheRefusesWhenFull()
        {
            Pet pet = NewPet(3);
            pet.Stats.Fullness = 95;
            Assert.False(pet.Feed("yakiniku"));
            Assert.Equal("吃不下了…", pet.Speech == null ? null : pet.Speech.Text);
        }

        [Test]
        public static void DigestionLeadsToTheToiletAndResets()
        {
            Pet pet = NewPet(4);
            pet.Stats.Fullness = 30;
            pet.Stats.Eat(50);
            var c = pet.Cfg;
            pet.Stats.Tick(0, 45 * 60, false, c); // 45 min later the meal has turned into urge
            Assert.True(pet.Stats.Digestion >= c.ToiletUrge, "urge " + pet.Stats.Digestion);
            Assert.True(Brain.Compute(pet).Toilet > 0);
            pet.Toilet();
            Step(pet, 4);
            Assert.True(pet.Stats.Digestion < 1);
        }

        [Test]
        public static void SillinessOffMeansNoToiletScene()
        {
            Pet pet = NewPet(5);
            pet.Silly = 0;
            pet.Stats.Digestion = 90;
            Assert.Equal(0.0, Brain.Compute(pet).Toilet);
            Assert.Equal(0.0, Brain.Compute(pet).Fart);
            Brain.Decide(pet);
            Assert.True(pet.Stats.Digestion < 1, "she still went, just off screen");
        }

        [Test]
        public static void YakinikuMakesFartsMoreLikely()
        {
            Pet pet = NewPet(6);
            double before = Brain.Compute(pet).Fart;
            pet.Stats.Fullness = 40;
            pet.Feed("yakiniku");
            Step(pet, 3);
            Assert.True(pet.GassyMealAt > pet.Time - 3, "the finished yakiniku counts as a gassy meal");
            double after = Brain.Compute(pet).Fart;
            Assert.Near(before * pet.Cfg.FartGassyBoost, after, 1e-12);
            pet.Time += pet.Cfg.FartGassyWindow + 1;
            Assert.Near(before, Brain.Compute(pet).Fart, 1e-12);
            pet.Quiet = true;
            Assert.Equal(0.0, Brain.Compute(pet).Fart);
        }

        [Test]
        public static void StarvingAndUnfedSheFeedsHerself()
        {
            Pet pet = NewPet(7);
            pet.Stats.Fullness = 5;
            Assert.True(Brain.Compute(pet).SelfFeed > 0);
            Assert.True(pet.Feed("smoothie", true));
            Assert.Equal("我自己买冰沙去！", pet.Speech == null ? null : pet.Speech.Text);
        }

        [Test]
        public static void OfflineHoursMakeHerHungryAndResetTheToilet()
        {
            var c = new Config();
            var s = new Stats();
            s.Fullness = 80;
            s.Digestion = 60;
            s.Stomach = 30;
            s.Offline(10 * 3600, c);
            Assert.True(s.Fullness < 80 - 40);
            Assert.Equal(0.0, s.Digestion);
            Assert.Equal(0.0, s.Stomach);
        }
    }
}
