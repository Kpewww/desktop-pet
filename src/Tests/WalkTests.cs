using Aly.Core;
using Aly.Core.Engine;

namespace Aly.Tests
{
    /// <summary>Where she strolls to: the sides of the screen, clear of the other pet.</summary>
    public static class WalkTests
    {
        static Pet NewPet(double x, ulong seed)
        {
            Atlas a = TestAtlas.Make(
                "idle", true, new[] { 300, 300 },
                "walk_l", true, new[] { 110, 110 },
                "walk_r", true, new[] { 110, 110 },
                "drag", true, new[] { 110 },
                "fall", true, new[] { 90 },
                "land", false, new[] { 60 });
            var pet = new Pet(a, new Config(), new Rng(seed));
            pet.Body.X = x;
            pet.Body.Y = 500;
            pet.SetWorld(new Bounds(0, 0, 1000, 500));
            return pet;
        }

        // the walkable stretch of a 1000 u screen and the width of each side
        static double MinX(Pet p) { return p.Cfg.HalfWidth + p.Cfg.WalkMargin; }
        static double MaxX(Pet p) { return 1000 - p.Cfg.HalfWidth - p.Cfg.WalkMargin; }
        static double Zone(Pet p) { return (MaxX(p) - MinX(p)) * p.Cfg.SideZone; }

        [Test]
        public static void SheStaysOnHerSideMostOfTheTime()
        {
            Pet pet = NewPet(100, 5);
            double zone = Zone(pet);
            int stay = 0, cross = 0;
            for (int i = 0; i < 400; i++)
            {
                double t = pet.PickWalkTarget();
                if (t >= MinX(pet) - 1e-9 && t <= MinX(pet) + zone + 1e-9) stay++;
                else if (t >= MaxX(pet) - zone - 1e-9 && t <= MaxX(pet) + 1e-9) cross++;
                else Assert.True(false, "a stroll into the middle: " + t);
            }
            Assert.True(stay > 330, "mostly her own side: " + stay);
            Assert.True(cross > 5, "but now and then the other side: " + cross);
        }

        [Test]
        public static void FromTheMiddleSheHeadsForTheNearerSide()
        {
            for (ulong seed = 1; seed <= 50; seed++)
            {
                Pet pet = NewPet(560, seed);
                double t = pet.PickWalkTarget();
                Assert.True(t >= MaxX(pet) - Zone(pet) - 1e-9 && t <= MaxX(pet) + 1e-9, "seed " + seed + ": " + t);
            }
        }

        [Test]
        public static void StrollsAreNotJustAShuffle()
        {
            Pet pet = NewPet(900, 9);
            for (int i = 0; i < 200; i++)
                Assert.True(System.Math.Abs(pet.PickWalkTarget() - 900) >= pet.Cfg.MinStroll, "a real stroll");
        }

        [Test]
        public static void OnANarrowScreenAllOfItIsHerSide()
        {
            Pet pet = NewPet(100, 3);
            pet.SetWorld(new Bounds(0, 0, 250, 500));
            for (int i = 0; i < 100; i++)
            {
                double t = pet.PickWalkTarget();
                Assert.True(t >= MinX(pet) - 1e-9 && t <= 250 - MinX(pet) + 1e-9, "inside: " + t);
            }
        }

        [Test]
        public static void SheKeepsClearOfTheOtherPet()
        {
            Pet pet = NewPet(80, 17);
            pet.PartnerPresent = true;
            pet.PartnerX = 150;
            double gap = 2 * pet.Cfg.HalfWidth + 8;
            for (int i = 0; i < 300; i++)
                Assert.True(System.Math.Abs(pet.PickWalkTarget() - 150) >= gap, "not on top of it");
            // once it has gone, anywhere will do again
            pet.PartnerPresent = false;
            bool near = false;
            for (int i = 0; i < 300 && !near; i++) near = System.Math.Abs(pet.PickWalkTarget() - 150) < gap;
            Assert.True(near);
        }
    }
}
