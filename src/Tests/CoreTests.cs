using System;
using System.IO;
using Aly.Core;
using Aly.Core.Engine;

namespace Aly.Tests
{
    public static class AtlasTests
    {
        [Test]
        public static void RoundTripKeepsEverything()
        {
            Atlas a = TestAtlas.Make("idle", true, new[] { 100, 200 }, "land", false, new[] { 50 });
            int eyes = a.InternAnchor("head.eyes");
            a.Frames[0].Anchors = new[] { new AtlasAnchor { NameIndex = eyes, X = 7, Y = -3 } };

            Atlas b = TestAtlas.RoundTrip(a);

            Assert.Equal(16, b.CanvasWidth);
            Assert.Equal(15, b.OriginY);
            Assert.Equal(3, b.Palette.Length);
            Assert.Equal(0xFF112233u, b.Palette[1]);
            Assert.Equal(a.Frames.Count, b.Frames.Count);
            Assert.Equal(1, b.Frames[1].OffsetX);
            Assert.True(b.GetClip("idle").Loop);
            Assert.False(b.GetClip("land").Loop);
            Assert.Equal(200, b.GetClip("idle").Durations[1]);
            int x, y;
            Assert.True(b.TryGetAnchor(0, b.AnchorIndex("head.eyes"), out x, out y));
            Assert.Equal(7, x);
            Assert.Equal(-3, y);
        }

        [Test]
        public static void RejectsGarbage()
        {
            var ms = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
            Assert.Throws<InvalidDataException>(() => Atlas.Load(ms));
        }
    }

    public static class AnimatorTests
    {
        [Test]
        public static void LoopingClipAdvancesByRealTime()
        {
            var anim = new Animator(TestAtlas.Make("idle", true, new[] { 100, 200, 300 }));
            anim.Play("idle");
            anim.Update(99);
            Assert.Equal(0, anim.Index);
            anim.Update(1);
            Assert.Equal(1, anim.Index);
            anim.Update(200 + 300 + 50); // wraps back into frame 0
            Assert.Equal(0, anim.Index);
            Assert.Near(50, 100 - anim.MsUntilNextEvent, 1e-9);
        }

        [Test]
        public static void HugeTimeStepsStayCheapAndCorrect()
        {
            var anim = new Animator(TestAtlas.Make("idle", true, new[] { 100, 100 }));
            anim.Play("idle");
            anim.Update(1e9 + 150);
            Assert.Equal(1, anim.Index);
        }

        [Test]
        public static void OneShotFinishesOnLastFrame()
        {
            var anim = new Animator(TestAtlas.Make("land", false, new[] { 50, 50 }));
            anim.Play("land");
            anim.Update(1000);
            Assert.True(anim.Finished);
            Assert.Equal(1, anim.Index);
            Assert.True(double.IsPositiveInfinity(anim.MsUntilNextEvent));
        }

        [Test]
        public static void ChangedFlagOnlyWhenFrameChanges()
        {
            var anim = new Animator(TestAtlas.Make("idle", true, new[] { 100, 100 }));
            anim.Play("idle");
            anim.Changed = false;
            anim.Update(50);
            Assert.False(anim.Changed);
            anim.Update(60);
            Assert.True(anim.Changed);
        }

        [Test]
        public static void SyncedSwitchKeepsPhase()
        {
            var anim = new Animator(TestAtlas.Make("idle", true, new[] { 100, 100, 100 }, "idle_r34", true, new[] { 100, 100, 100 }));
            anim.Play("idle");
            anim.Update(150);
            anim.PlaySynced("idle_r34");
            Assert.Equal("idle_r34", anim.ClipName);
            Assert.Equal(1, anim.Index);
            Assert.Near(50, anim.MsUntilNextEvent, 1e-9);
        }

        [Test]
        public static void PlayingSameClipDoesNotRestart()
        {
            var anim = new Animator(TestAtlas.Make("idle", true, new[] { 100, 100 }));
            anim.Play("idle");
            anim.Update(150);
            anim.Play("idle");
            Assert.Equal(1, anim.Index);
            anim.Play("idle", true);
            Assert.Equal(0, anim.Index);
        }
    }

    public static class PhysicsTests
    {
        static readonly Bounds World = new Bounds(0, 0, 1000, 500);

        [Test]
        public static void DroppedBodyLandsOnTheFloor()
        {
            var cfg = new Config();
            var b = new Body { X = 500, Y = 100 };
            bool landed = false;
            for (int i = 0; i < 600 && !landed; i++)
            {
                StepResult r = Physics.Fly(b, World, cfg, 1 / 60.0);
                landed = (r.Impacts & Impact.Landed) != 0;
            }
            Assert.True(landed, "never landed");
            Assert.Equal(500.0, b.Y);
            Assert.Equal(0.0, b.Vy);
        }

        [Test]
        public static void FastImpactBouncesFirst()
        {
            var cfg = new Config();
            var b = new Body { X = 500, Y = 495, Vy = 900 };
            StepResult r = Physics.Fly(b, World, cfg, 1 / 60.0);
            Assert.True((r.Impacts & Impact.Bounce) != 0);
            Assert.True(b.Vy < 0, "should move up after the bounce");
            Assert.True(r.FloorSpeed >= 900);
        }

        [Test]
        public static void WallsReflectWithLoss()
        {
            var cfg = new Config();
            var b = new Body { X = 20, Y = 300, Vx = -600 };
            Physics.Fly(b, World, cfg, 0.05);
            Assert.True(b.Vx > 0, "should head back right");
            Assert.True(b.Vx < 600 * cfg.WallRestitution + 1);
            Assert.True(b.X >= cfg.HalfWidth);
        }

        [Test]
        public static void SlidingStopsByFriction()
        {
            var cfg = new Config();
            var b = new Body { X = 500, Y = 500, Vx = 300 };
            for (int i = 0; i < 300; i++) Physics.Slide(b, World, cfg, 1 / 60.0);
            Assert.Equal(0.0, b.Vx);
            Assert.True(b.X > 500 && b.X < 600);
        }

        [Test]
        public static void ReleaseVelocityUsesTrailingWindow()
        {
            var vt = new VelocityTracker();
            vt.Add(0.00, 0, 0);
            vt.Add(0.50, 0, 0);   // long pause, then a quick flick
            vt.Add(0.54, 20, -8);
            vt.Add(0.58, 40, -16);
            double vx, vy;
            vt.Velocity(0.08, out vx, out vy);
            Assert.Near(500, vx, 1e-6);
            Assert.Near(-200, vy, 1e-6);
        }

        [Test]
        public static void HoldingStillThenReleasingDoesNotThrow()
        {
            var vt = new VelocityTracker();
            vt.Add(0.00, 0, 0);
            vt.Add(0.05, 30, 0);
            vt.Add(1.00, 30, 0); // released a second later without moving
            double vx, vy;
            vt.Velocity(0.08, out vx, out vy);
            Assert.Equal(0.0, vx);
            Assert.Equal(0.0, vy);
        }
    }

    public static class PetTests
    {
        static Pet NewPet(ulong seed)
        {
            var pet = new Pet(TestAtlas.ForPet(), new Config(), new Rng(seed));
            pet.Body.X = 500;
            pet.Body.Y = 500;
            pet.SetWorld(new Bounds(0, 0, 1000, 500));
            pet.DragBounds = new Bounds(0, 0, 1000, 540);
            return pet;
        }

        static void Run(Pet pet, double seconds)
        {
            for (double t = 0; t < seconds; t += 1 / 60.0) pet.Update(1 / 60.0);
        }

        [Test]
        public static void SmallPointerMovementIsAClickNotADrag()
        {
            Pet pet = NewPet(1);
            pet.DragThreshold = 2;
            pet.PointerDown(500, 480, 0);
            pet.PointerMove(501, 481, 0.05);
            Assert.False(pet.IsDragging);
            pet.PointerUp(501, 481, 0.1);
            Assert.Equal("fall", pet.StateName); // the placeholder poke is a little hop
        }

        [Test]
        public static void DragFollowsPointerKeepingGrabOffset()
        {
            Pet pet = NewPet(2);
            pet.PointerDown(505, 470, 0);
            pet.PointerMove(600, 300, 0.05);
            Assert.True(pet.IsDragging);
            Assert.Equal("drag", pet.StateName);
            pet.Update(1 / 60.0);
            Assert.Near(595, pet.Body.X, 1e-9);
            Assert.Near(330, pet.Body.Y, 1e-9);
        }

        [Test]
        public static void ThrownPetFallsLandsAndSettles()
        {
            Pet pet = NewPet(3);
            pet.PointerDown(500, 480, 0);
            pet.PointerMove(520, 300, 0.10);
            pet.PointerMove(560, 280, 0.14);
            pet.PointerUp(600, 260, 0.18);
            Assert.Equal("fall", pet.StateName);
            Assert.True(pet.Body.Vx > 0);
            bool settled = false;
            for (int i = 0; i < 600 && !settled; i++)
            {
                pet.Update(1 / 60.0);
                settled = pet.StateName == "idle";
            }
            Assert.True(settled, "never settled back to idle");
            Assert.Equal(500.0, pet.Body.Y);
        }

        [Test]
        public static void WalksToTargetAndStops()
        {
            Pet pet = NewPet(4);
            pet.Walk.Target = 560;
            pet.ChangeState(pet.Walk);
            Assert.Equal("walk_r", pet.Anim.ClipName);
            Run(pet, 5);
            Assert.Near(560, pet.Body.X, 1e-6);
            Assert.True(pet.StateName == "idle" || pet.StateName == "walk");
        }

        [Test]
        public static void WalkSpeedNeverExceedsConfig()
        {
            Pet pet = NewPet(5);
            pet.Walk.Target = 900;
            pet.ChangeState(pet.Walk);
            double prev = pet.Body.X;
            for (int i = 0; i < 300; i++)
            {
                pet.Update(1 / 60.0);
                double v = (pet.Body.X - prev) * 60;
                Assert.True(v <= pet.Cfg.WalkSpeed + 1e-6, "too fast: " + v);
                prev = pet.Body.X;
            }
        }

        [Test]
        public static void IdleEventuallyDecidesSomething()
        {
            Pet pet = NewPet(6);
            bool acted = false;
            for (int i = 0; i < 60 * 60 && !acted; i++)
            {
                pet.Update(1 / 60.0);
                acted = pet.StateName != "idle";
            }
            Assert.True(acted, "stayed idle for a whole minute");
        }

        [Test]
        public static void IdleNeedsNoContinuousTicks()
        {
            Pet pet = NewPet(7);
            Assert.Equal("idle", pet.StateName);
            Assert.False(pet.Continuous);
            Assert.True(pet.NextWake > 0 && pet.NextWake < 10);
        }
    }
}
