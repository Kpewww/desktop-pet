using System.IO;
using Aly.Core;
using Aly.Core.Engine;

namespace Aly.Tests
{
    public static class BehaviourTests
    {
        static Atlas FullAtlas()
        {
            return TestAtlas.Make(
                "idle", true, new[] { 300, 300 },
                "walk_l", true, new[] { 110, 110 },
                "walk_r", true, new[] { 110, 110 },
                "drag", true, new[] { 110, 110 },
                "grabbed", false, new[] { 60, 120 },
                "struggle_hard", true, new[] { 80, 80 },
                "bite", false, new[] { 80, 80 },
                "fall", true, new[] { 90 },
                "land", false, new[] { 60, 80 },
                "poked", false, new[] { 60, 140, 120, 240 },
                "annoyed", true, new[] { 300, 300 },
                "angry", true, new[] { 120, 120 },
                "explode", false, new[] { 90, 90, 90 },
                "sulk", true, new[] { 300, 400 },
                "petted", true, new[] { 160, 120 },
                "happy", false, new[] { 100, 100 },
                "bark", false, new[] { 70, 110 },
                "dizzy", true, new[] { 140, 140 },
                "sit_down", false, new[] { 80, 90 },
                "sit", true, new[] { 400, 120 },
                "stand_up", false, new[] { 90, 90 },
                "sleep_down", false, new[] { 200, 140 },
                "sleep", true, new[] { 500, 500 },
                "wake", false, new[] { 150, 150 },
                "sassy_glance", false, new[] { 150, 600 });
        }

        static Pet NewPet(ulong seed)
        {
            var pet = new Pet(FullAtlas(), new Config(), new Rng(seed));
            pet.Body.X = 500;
            pet.Body.Y = 500;
            pet.SetWorld(new Bounds(0, 0, 1000, 500));
            pet.DragBounds = new Bounds(0, 0, 1000, 540);
            var lines = new Lines();
            lines.Load(new StringReader("[explode]\n气死我啦！\n[forgive]\n哼…这次原谅你\n[bite_warn]\n再不放我下来我咬你了！\n[welcome_back]\n欢迎回来！\n[poke]\n嗯？"));
            pet.Lines = lines;
            return pet;
        }

        static void Step(Pet pet, double seconds)
        {
            for (double t = 0; t < seconds; t += 1 / 60.0) pet.Update(1 / 60.0);
        }

        static void Click(Pet pet)
        {
            double t = pet.Time;
            pet.PointerDown(500, 470, t);
            pet.PointerUp(500, 470, t + 0.05);
        }

        [Test]
        public static void RapidPokesEscalateToAnExplosionThenSulk()
        {
            Pet pet = NewPet(1);
            Click(pet);
            Assert.Equal("react", pet.StateName);
            Step(pet, 0.7); // longer than a double click, so it stays a poke
            Click(pet);
            Assert.Equal("annoyed", pet.StateName);
            Step(pet, 0.7);
            Click(pet);
            Assert.Equal("angry", pet.StateName);
            Step(pet, 0.7);
            Click(pet);
            Assert.Equal("angry", pet.StateName);
            Step(pet, 0.7);
            Click(pet);
            Assert.Equal("react", pet.StateName, "explode plays first");
            Assert.Equal("explode", pet.Anim.ClipName);
            Assert.True(pet.Speech != null && pet.Speech.Text == "气死我啦！", "she must shout the line");
            Assert.Equal(SpeechStyle.Shout, pet.Speech.Style);
            Step(pet, 0.5);
            Assert.Equal("sulk", pet.StateName);
        }

        [Test]
        public static void SlowPokesDoNotStackUp()
        {
            Pet pet = NewPet(2);
            for (int i = 0; i < 6; i++)
            {
                Click(pet);
                Step(pet, 8);
            }
            Assert.True(pet.Stats.Annoyance < pet.Cfg.AnnoyedAt, "annoyance should decay between slow pokes");
        }

        [Test]
        public static void PettingTheHeadForgivesHer()
        {
            Pet pet = NewPet(3);
            pet.Stats.Annoyance = 80;
            pet.Angry.Duration = 10;
            pet.ChangeState(pet.Angry);
            double x = 500;
            for (int i = 0; i < 12; i++)
            {
                x += (i % 2 == 0) ? 6 : -6;
                pet.PointerHover(x, 450, pet.Time);
                Step(pet, 0.1);
            }
            Assert.Equal("petted", pet.StateName);
            Assert.True(pet.Stats.Annoyance < 1);
            Assert.Equal("哼…这次原谅你", pet.Speech == null ? null : pet.Speech.Text);
            Step(pet, 2.0); // strokes stopped
            Assert.True(pet.StateName == "react" || pet.StateName == "idle");
        }

        [Test]
        public static void HeldTooLongSheBitesFree()
        {
            Pet pet = NewPet(4);
            pet.PointerDown(500, 470, pet.Time);
            pet.PointerMove(520, 400, pet.Time + 0.05);
            Assert.Equal("drag", pet.StateName);
            Step(pet, 9);
            Assert.Equal("再不放我下来我咬你了！", pet.Speech == null ? null : pet.Speech.Text);
            Step(pet, 1.5);
            Assert.True(pet.ReleasePointer, "app must be told to release the mouse");
            Assert.False(pet.IsDragging);
            Step(pet, 3);
            Assert.True(pet.StateName != "drag");
            // the rest of that press is ignored
            pet.PointerMove(900, 100, pet.Time);
            pet.PointerUp(900, 100, pet.Time);
            Assert.True(pet.StateName != "drag");
        }

        [Test]
        public static void HardLandingMakesHerDizzy()
        {
            Pet pet = NewPet(5);
            pet.Body.Y = 100;
            pet.Body.Vy = 900;
            pet.ChangeState(pet.Fall);
            Step(pet, 1.5);
            Assert.Equal("dizzy", pet.StateName);
            Step(pet, 3);
            Assert.True(pet.StateName != "dizzy");
        }

        [Test]
        public static void SheNapsWhileYouAreAwayAndWelcomesYouBack()
        {
            Pet pet = NewPet(6);
            pet.UserIdleSeconds = 400;
            Step(pet, 1);
            Assert.Equal("sleep", pet.StateName);
            Step(pet, 3);
            Assert.True(pet.State.Sleeping);
            pet.UserIdleSeconds = 0.5;
            Step(pet, 3);
            Assert.True(pet.StateName != "sleep", "she should be up again");
            Assert.True(pet.Speech == null || pet.Speech.Text == "欢迎回来！");
        }

        [Test]
        public static void PokingASleeperWakesHerGrumpy()
        {
            Pet pet = NewPet(7);
            pet.Sleep.Begin(pet, false);
            Step(pet, 2);
            Assert.True(pet.State.Sleeping);
            Click(pet);
            Step(pet, 1.5);
            Assert.True(pet.StateName == "annoyed" || pet.StateName == "sleep");
            Assert.True(pet.Stats.Annoyance > 0);
        }

        [Test]
        public static void EyesFollowANearCursorThenLoseInterest()
        {
            Pet pet = NewPet(8);
            pet.SetCursor(560, 440); // right of her head
            Step(pet, 0.6);
            Assert.True(pet.Gaze.Engaged);
            Assert.True(pet.Eyes.TargetH > 0, "should look right");
            Step(pet, 6); // the cursor never moves: interest drains
            Assert.False(pet.Gaze.Engaged);
            Assert.True(pet.Gaze.CoolingDown(pet.Time));
            // cursor leaves and comes back: she notices again
            pet.SetCursor(900, 100);
            Step(pet, 0.3);
            pet.SetCursor(440, 440);
            Step(pet, 0.3);
            if (pet.State.Calm) Assert.True(pet.Gaze.Engaged);
        }

        [Test]
        public static void LinesNeverRepeatBackToBack()
        {
            var lines = new Lines();
            lines.Load(new StringReader("[x]\na\nb\nc"));
            var rng = new Rng(9);
            string prev = null;
            for (int i = 0; i < 200; i++)
            {
                string s = lines.Pick("x", rng);
                Assert.True(s != prev, "repeated " + s);
                prev = s;
            }
        }

        [Test]
        public static void UserLinesAddOrReplace()
        {
            var lines = new Lines();
            lines.Load(new StringReader("[a]\n1\n[b]\n2"));
            lines.Load(new StringReader("[a]\n3\n[b]!\n4"));
            Assert.Equal(2, lines.Count("a"));
            Assert.Equal(1, lines.Count("b"));
            Assert.Equal("4", lines.Pick("b", new Rng(1)));
        }

        [Test]
        public static void EnergyDrainsAwakeAndRestoresAsleep()
        {
            var c = new Config();
            var s = new Stats();
            s.Energy = 50;
            s.Tick(0, 3600, false, c);
            Assert.True(s.Energy < 50 - 20);
            s.Tick(0, 600, true, c);
            Assert.True(s.Energy > 50);
            s.Offline(8 * 3600, c);
            Assert.Equal(100.0, s.Energy);
        }
    }
}
