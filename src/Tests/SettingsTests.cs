using Aly.Core;
using Aly.Core.Engine;

namespace Aly.Tests
{
    public static class SettingsTests
    {
        static Pet NewPet(Atlas a)
        {
            var pet = new Pet(a, new Config(), new Rng(3));
            pet.Body.X = 500;
            pet.Body.Y = 500;
            pet.SetWorld(new Bounds(0, 0, 1000, 500));
            return pet;
        }

        [Test]
        public static void ActivityChangesHowMuchSheWanders()
        {
            Pet pet = NewPet(TestAtlas.ForPet());
            pet.Activity = 1;
            Weights normal = Brain.Compute(pet);
            pet.Activity = 2;
            Weights lively = Brain.Compute(pet);
            pet.Activity = 0;
            Weights calm = Brain.Compute(pet);
            Assert.True(lively.Walk > normal.Walk && normal.Walk > calm.Walk, "walks more when lively");
            Assert.True(calm.Sit > normal.Sit && normal.Sit > lively.Sit, "sits more when calm");
        }

        [Test]
        public static void ActivityChangesHowLongSheStandsAround()
        {
            Pet pet = NewPet(TestAtlas.ForPet());
            Config c = pet.Cfg;
            pet.Activity = 2;
            for (int i = 0; i < 50; i++) Assert.True(pet.IdlePause() <= c.IdleMax * 0.6 + 1e-9);
            pet.Activity = 0;
            for (int i = 0; i < 50; i++) Assert.True(pet.IdlePause() >= c.IdleMin * 1.8 - 1e-9);
        }

        [Test]
        public static void ChattinessScalesOptionalLinesOnly()
        {
            Pet pet = NewPet(TestAtlas.ForPet());
            int[] said = new int[3];
            for (int level = 0; level < 3; level++)
            {
                pet.Chatty = level;
                for (int i = 0; i < 4000; i++) if (pet.ChatChance(0.1)) said[level]++;
            }
            Assert.True(said[0] < said[1] && said[1] < said[2], string.Format("{0} < {1} < {2}", said[0], said[1], said[2]));
            Assert.True(said[0] > 0, "rarely is not never");
        }

        [Test]
        public static void SwappingTheAtlasKeepsTheClipAndFrame()
        {
            Atlas withEars = TestAtlas.ForPet();
            Atlas plain = TestAtlas.ForPet();
            Pet pet = NewPet(withEars);
            pet.Update(0.35); // idle frame 1 (300 ms first frame)
            Assert.Equal(1, pet.Anim.Index);
            pet.UseAtlas(plain);
            Assert.True(ReferenceEquals(plain, pet.Atlas));
            Assert.Equal("idle", pet.Anim.ClipName);
            Assert.Equal(1, pet.Anim.Index);
            Assert.Equal(plain.GetClip("idle").Frames[1], pet.Anim.Frame);
            Assert.True(pet.Anim.Changed);
        }
    }
}
