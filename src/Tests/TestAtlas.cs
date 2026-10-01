using System.IO;
using Aly.Core;

namespace Aly.Tests
{
    /// <summary>Builds tiny in-memory atlases for engine tests.</summary>
    static class TestAtlas
    {
        public static Atlas Make(params object[] clips)
        {
            var a = new Atlas();
            a.CanvasWidth = 16;
            a.CanvasHeight = 16;
            a.OriginX = 8;
            a.OriginY = 15;
            a.Palette = new uint[] { 0, 0xFF112233, 0xFFFFFFFF };
            int frame = 0;
            for (int i = 0; i < clips.Length; i += 3)
            {
                string name = (string)clips[i];
                bool loop = (bool)clips[i + 1];
                int[] durations = (int[])clips[i + 2];
                var c = new AtlasClip();
                c.Name = name;
                c.Loop = loop;
                c.Durations = durations;
                c.Frames = new int[durations.Length];
                for (int k = 0; k < durations.Length; k++)
                {
                    var f = new AtlasFrame();
                    f.OffsetX = k;
                    f.OffsetY = 0;
                    f.Width = 1;
                    f.Height = 1;
                    f.Pixels = new byte[] { 1 };
                    a.Frames.Add(f);
                    c.Frames[k] = frame++;
                }
                a.AddClip(c);
            }
            return a;
        }

        /// <summary>An atlas containing every clip the pet engine requires.</summary>
        public static Atlas ForPet()
        {
            return Make(
                "idle", true, new[] { 300, 120, 300, 120 },
                "walk_l", true, new[] { 100, 100, 100, 100 },
                "walk_r", true, new[] { 100, 100, 100, 100 },
                "drag", true, new[] { 120, 120 },
                "fall", true, new[] { 100 },
                "land", false, new[] { 60, 80, 100 });
        }

        public static Atlas RoundTrip(Atlas a)
        {
            var ms = new MemoryStream();
            a.Save(ms);
            ms.Position = 0;
            return Atlas.Load(ms);
        }
    }

    /// <summary>Two foods as a character file has them, for engine tests.</summary>
    static class TestFoods
    {
        public const string AlyCharacter =
            "key = example\nid = XiaoChai\nname = 小柴\nselffood = smoothie\n" +
            "[food smoothie]\nlabel = 冰沙\nclip = eat_smoothie\ncues = 1:smoothie 6:freeze\nself = 6:freeze\nselfline = self_smoothie\namount = 25\nmood = 8\n" +
            "[food yakiniku]\nlabel = 日式烧烤\nclip = eat_yakiniku\ncues = 1:yakiniku 12:yummy\namount = 50\nmood = 15\ngassy = yes\n";

        public static void Give(Aly.Core.Engine.Pet pet)
        {
            Character c = Character.Parse(new StringReader(AlyCharacter));
            pet.Foods = c.Foods.ToArray();
            pet.SelfFood = c.SelfFood;
        }
    }
}
