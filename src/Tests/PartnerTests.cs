using System.IO;
using Aly.Core;
using Aly.Core.Engine;

namespace Aly.Tests
{
    /// <summary>The engine side of two pets on one desktop (the messages themselves are the app's).</summary>
    public static class PartnerTests
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
                "happy", false, new[] { 100, 100, 100 },
                "angry", true, new[] { 120, 120 },
                "drink_water", false, new[] { 100, 100, 100, 100 });
            var pet = new Pet(a, new Config(), new Rng(21));
            pet.Body.X = x;
            pet.Body.Y = 500;
            pet.SetWorld(new Bounds(0, 0, 1000, 500));
            var lines = new Lines();
            lines.Load(new StringReader("[visit]\n贴贴～\n[defend]\n不许欺负他！\n"));
            pet.Lines = lines;
            return pet;
        }

        static void Step(Pet pet, double seconds)
        {
            for (double t = 0; t < seconds; t += 1 / 60.0) pet.Update(1 / 60.0);
        }

        [Test]
        public static void WalkingOverCallsBackOnArrival()
        {
            Pet pet = NewPet(500);
            bool arrived = false;
            pet.WalkTo(540, delegate(Pet p) { arrived = true; p.Meet(560, "visit"); });
            Step(pet, 4);
            Assert.True(arrived);
            Assert.Near(540, pet.Body.X, 0.5);
            Assert.Equal("贴贴～", pet.Speech == null ? null : pet.Speech.Text);
            Assert.Equal(1, pet.Facing);
        }

        [Test]
        public static void PickedUpOnTheWayCallsTheVisitOff()
        {
            Pet pet = NewPet(500);
            bool arrived = false;
            pet.WalkTo(600, delegate { arrived = true; });
            Step(pet, 0.5);
            pet.PointerDown(pet.Body.X, 470, pet.Time);
            pet.PointerMove(pet.Body.X + 40, 400, pet.Time + 0.05);
            Step(pet, 1);
            pet.PointerUp(pet.Body.X, 400, pet.Time);
            Step(pet, 3);
            pet.Walk.Target = 700; // a later, ordinary walk must not fire the old callback
            pet.ChangeState(pet.Walk);
            Step(pet, 8);
            Assert.False(arrived);
        }

        [Test]
        public static void TheBrainVisitsOnlyWithAPartnerAndNotTooOften()
        {
            Pet pet = NewPet(500);
            Assert.Equal(0.0, Brain.Compute(pet).Visit);
            pet.PartnerPresent = true;
            pet.Time = 5000;
            Assert.True(Brain.Compute(pet).Visit > 0);
            int asked = 0;
            pet.VisitRequested = delegate { asked++; };
            pet.RequestVisit();
            Assert.Equal(1, asked);
            Assert.Equal(0.0, Brain.Compute(pet).Visit);
            pet.Time += pet.Cfg.VisitEvery + 1;
            Assert.True(Brain.Compute(pet).Visit > 0);
        }

        [Test]
        public static void SheStandsUpForTheOtherOneAndJoinsInItsReminders()
        {
            Pet pet = NewPet(500);
            pet.Defend();
            Assert.True(pet.State == pet.Angry);
            Assert.Equal("不许欺负他！", pet.Speech.Text);
            Step(pet, 4);
            pet.JoinIn("drink_water");
            Assert.Equal("drink_water", pet.Anim.ClipName);
            // ...but not while keeping out of the way
            Step(pet, 2);
            pet.Calm = CalmStyle.Peek;
            pet.JoinIn("drink_water");
            Assert.True(pet.Anim.ClipName != "drink_water" || !pet.Anim.Finished && pet.State != pet.Reacting);
        }

        [Test]
        public static void HostingAVisitMeansStayingPut()
        {
            Pet pet = NewPet(500);
            pet.HoldUntil = pet.Time + 20;
            Step(pet, 15);
            Assert.True(pet.State == pet.Idle, "no wandering off: " + pet.StateName);
            Assert.Near(500, pet.Body.X, 1e-9);
            Assert.True(pet.NextWake > 0.05, "and no busy waiting");
        }
    }
}
