using System;
using System.Collections.Generic;
using System.IO;
using Aly.Core;
using Aly.Core.Engine;

namespace Aly.Tests
{
    public static class ClipTextTests
    {
        static string Repeat(string s, int n)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++) sb.Append(s);
            return sb.ToString();
        }

        [Test]
        public static void TwentyCharactersAreShownWhole()
        {
            string t = Repeat("字", 20);
            Assert.Equal(t, ClipText.ForSign(t));
        }

        [Test]
        public static void TwentyOneCharactersAreCutAfterTwenty()
        {
            Assert.Equal(Repeat("字", 20) + "...", ClipText.ForSign(Repeat("字", 21)));
            Assert.Equal("abcdefghijklmnopqrst...", ClipText.ForSign("abcdefghijklmnopqrstuvwxyz"));
        }

        [Test]
        public static void EmojiSequencesCountAsOneCharacter()
        {
            string family = "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466"; // man, woman, girl, boy
            string thumb = "\U0001F44D\U0001F3FD";                                         // thumbs up, medium skin
            string flag = "\U0001F1E8\U0001F1F3";                                           // regional C + N
            string keycap = "1\uFE0F\u20E3";
            string heart = "\u2764\uFE0F";
            foreach (string e in new[] { family, thumb, flag, keycap, heart })
            {
                Assert.Equal(1, ClipText.Length(e), "one perceived character: " + e);
                Assert.Equal(Repeat(e, 20), ClipText.ForSign(Repeat(e, 20)));
                Assert.Equal(Repeat(e, 20) + "...", ClipText.ForSign(Repeat(e, 21)));
            }
            Assert.Equal(2, ClipText.Length(flag + flag), "two flags");
            Assert.Equal(3, ClipText.Length("a" + family + "b"));
        }

        [Test]
        public static void SurrogatePairsAndCombiningMarksAreNeverSplit()
        {
            Assert.Equal(1, ClipText.Length("\U00020000"));   // CJK extension B
            Assert.Equal(1, ClipText.Length("e\u0301"));      // e + combining acute
            string s = Repeat("e\u0301", 21);
            Assert.Equal(Repeat("e\u0301", 20) + "...", ClipText.ForSign(s));
        }

        [Test]
        public static void NewlinesTabsAndSpaceRunsCollapse()
        {
            Assert.Equal("a b c", ClipText.ForSign("  a\r\n\tb    c \n"));
            Assert.Equal("第一行 第二行", ClipText.ForSign("第一行\r\n\r\n第二行"));
        }

        [Test]
        public static void AllWhitespaceIsEmpty()
        {
            Assert.Equal("", ClipText.ForSign(" \r\n\t  "));
            Assert.Equal("", ClipText.ForSign(null));
            var item = new ClipItem();
            item.Kind = ClipKind.Text;
            item.Text = "   \n";
            Assert.Equal("（空白）", item.SignText);
        }

        [Test]
        public static void ImagesAndFilesHaveTheirOwnSignText()
        {
            var img = new ClipItem();
            img.Kind = ClipKind.Image;
            img.ImageWidth = 1920;
            img.ImageHeight = 1080;
            Assert.Equal("图片 1920×1080", img.SignText);

            var one = new ClipItem();
            one.Kind = ClipKind.Files;
            one.Files = new[] { @"C:\Users\aly\Desktop\旅行照片.zip" };
            Assert.Equal("旅行照片.zip", one.SignText);

            var many = new ClipItem();
            many.Kind = ClipKind.Files;
            many.Files = new[] { @"D:\a\一个名字特别特别特别长的文件夹用来测试截断规则", @"D:\a\b.txt", @"D:\a\c.txt" };
            Assert.Equal("一个名字特别特别特别长的文件夹用来测试截... 等 3 个文件", many.SignText);

            var secret = new ClipItem();
            secret.Kind = ClipKind.Text;
            secret.Text = "Summer2026!";
            secret.Secret = true;
            Assert.Equal("（已隐藏）", secret.SignText);
        }
    }

    public static class PrivacyTests
    {
        [Test]
        public static void PasswordsAndKeysLookSecret()
        {
            string[] secrets =
            {
                "Passw0rd!", "Summer2026", "hunter2Hunter", "xK9mP2qL7vN4", "correct#Horse7",
                "sk-proj-abcdefghijklmnopqrstuvwx", "ghp_abcdefghijklmnopqrstuvwxyz0123456789",
                "AKIAIOSFODNN7EXAMPLE", "AIzaSyA-1234567890abcdefghijklmnopqrstu",
                "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0In0.abcDEF123_-x",
                "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b",
            };
            foreach (string s in secrets) Assert.True(Privacy.LooksSecret(s), "should hide: " + s);
        }

        [Test]
        public static void EverydayTextDoesNotLookSecret()
        {
            string[] normal =
            {
                "hello world", "今天吃日式烧烤", "Passwort ist geheim", "password", "13812345678",
                "https://example.com/Path?a=1&B=2", "me@example.com", @"C:\Users\Me\Desktop",
                "IMG_2024.JPG", "README.md", "report-v2.docx", "v1.2.3-beta", "2026-09-24T10:00:00",
                "getElementById", "user_name_long", "short1A", "", null, "小柴2026!",
            };
            foreach (string s in normal) Assert.False(Privacy.LooksSecret(s), "should show: " + s);
        }
    }

    public static class ClipHistoryTests
    {
        static ClipItem Text(string t)
        {
            var i = new ClipItem();
            i.Kind = ClipKind.Text;
            i.Text = t;
            i.Signature = "T:" + t;
            i.Time = DateTime.Now;
            return i;
        }

        [Test]
        public static void RepeatedCopiesAreRecordedOnce()
        {
            var h = new ClipHistory();
            ClipItem a = h.Add(Text("a"));
            Assert.True(ReferenceEquals(a, h.Add(Text("a"))), "same copy again refreshes the newest");
            Assert.Equal(1, h.Count);
            h.Add(Text("b"));
            ClipItem again = h.Add(Text("a"));
            Assert.True(ReferenceEquals(a, again), "an older copy moves back to the top");
            Assert.Equal(2, h.Count);
            Assert.Equal("a", h.Latest.Text);
        }

        [Test]
        public static void CapacityDropsTheOldestButKeepsPinned()
        {
            var h = new ClipHistory();
            var removed = new List<string>();
            h.Removed = i => removed.Add(i.Text);
            ClipItem first = h.Add(Text("first"));
            h.SetPinned(first.Id, true);
            for (int i = 0; i < 60; i++) h.Add(Text("t" + i));
            Assert.Equal(51, h.Count, "50 unpinned plus the pinned one");
            Assert.True(h.Find(first.Id) != null, "pinned survives");
            Assert.Equal(10, removed.Count);
            Assert.Equal("t0", removed[0]);
        }

        [Test]
        public static void ClearKeepsPinnedAndPauseStopsRecording()
        {
            var h = new ClipHistory();
            ClipItem keep = h.Add(Text("keep"));
            h.Add(Text("drop"));
            h.SetPinned(keep.Id, true);
            h.Clear();
            Assert.Equal(1, h.Count);
            h.Paused = true;
            Assert.True(h.Add(Text("new")) == null);
            Assert.Equal(1, h.Count);
        }
    }

    public static class DibTests
    {
        /// <summary>A DIB in memory: 40-byte header (+ masks), then rows. Pixels are 0xRRGGBB, listed top row first.</summary>
        static byte[] Make(int w, int h, int bpp, bool topDown, bool bitfields, uint[] rgbTopFirst)
        {
            int stride = ((w * bpp + 31) / 32) * 4;
            int offset = 40 + (bitfields ? 12 : 0);
            var b = new byte[offset + stride * h];
            BitConverter.GetBytes(40).CopyTo(b, 0);
            BitConverter.GetBytes(w).CopyTo(b, 4);
            BitConverter.GetBytes(topDown ? -h : h).CopyTo(b, 8);
            BitConverter.GetBytes((short)1).CopyTo(b, 12);
            BitConverter.GetBytes((short)bpp).CopyTo(b, 14);
            BitConverter.GetBytes(bitfields ? 3 : 0).CopyTo(b, 16);
            if (bitfields)
            {
                BitConverter.GetBytes(0x00FF0000).CopyTo(b, 40);
                BitConverter.GetBytes(0x0000FF00).CopyTo(b, 44);
                BitConverter.GetBytes(0x000000FF).CopyTo(b, 48);
            }
            for (int y = 0; y < h; y++)
            {
                int row = topDown ? y : h - 1 - y;
                for (int x = 0; x < w; x++)
                {
                    uint c = rgbTopFirst[y * w + x];
                    int o = offset + row * stride + x * (bpp / 8);
                    b[o] = (byte)c;
                    b[o + 1] = (byte)(c >> 8);
                    b[o + 2] = (byte)(c >> 16);
                }
            }
            return b;
        }

        static DibInfo Read(byte[] data, int maxW = 160, int maxH = 120)
        {
            var pin = System.Runtime.InteropServices.GCHandle.Alloc(data, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { return Dib.Read(pin.AddrOfPinnedObject(), data.Length, maxW, maxH); }
            finally { pin.Free(); }
        }

        const uint Red = 0xFF0000, Green = 0x00FF00, Blue = 0x0000FF, White = 0xFFFFFF;

        [Test]
        public static void BottomUpAndTopDownRowsComeOutTheRightWayUp()
        {
            uint[] px = { Red, Red, Green, Green, Blue, Blue, White, White };
            foreach (bool topDown in new[] { false, true })
            {
                foreach (int bpp in new[] { 24, 32 })
                {
                    DibInfo d = Read(Make(4, 2, bpp, topDown, bpp == 32 && topDown, px));
                    string what = (topDown ? "top-down " : "bottom-up ") + bpp;
                    Assert.Equal(4, d.Width, what);
                    Assert.Equal(2, d.Height, what);
                    Assert.Equal(4, d.ThumbWidth, what);
                    Assert.Equal(0xFF000000u | Red, d.Thumb[0], what);
                    Assert.Equal(0xFF000000u | Green, d.Thumb[3], what);
                    Assert.Equal(0xFF000000u | Blue, d.Thumb[4], what);
                    Assert.Equal(0xFF000000u | White, d.Thumb[7], what);
                }
            }
        }

        [Test]
        public static void BigPicturesShrinkToAThumbnail()
        {
            int w = 320, h = 240;
            var px = new uint[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) px[y * w + x] = x < w / 2 ? Red : Blue;
            DibInfo d = Read(Make(w, h, 24, false, false, px));
            Assert.Equal(160, d.ThumbWidth);
            Assert.Equal(120, d.ThumbHeight);
            Assert.Equal(0xFF000000u | Red, d.Thumb[0]);
            Assert.Equal(0xFF000000u | Blue, d.Thumb[159]);
        }

        [Test]
        public static void OddFormatsAndShortDataStillGiveTheSize()
        {
            byte[] eight = Make(4, 2, 24, false, false, new uint[8]);
            BitConverter.GetBytes((short)8).CopyTo(eight, 14); // claims 8-bit with a palette it doesn't have
            DibInfo d = Read(eight);
            Assert.Equal(4, d.Width);
            Assert.True(d.Thumb == null);

            byte[] full = Make(64, 64, 32, false, false, new uint[64 * 64]);
            var cut = new byte[200];
            Array.Copy(full, cut, cut.Length);
            DibInfo c = Read(cut);
            Assert.Equal(64, c.Width);
            Assert.True(c.Thumb == null, "no reading past the end");
            Assert.True(Read(new byte[10]) == null);
        }

        [Test]
        public static void TheSameImageGetsTheSameSignature()
        {
            uint[] px = { Red, Green, Blue, White };
            ClipItem a = ClipItem.FromImage(Read(Make(2, 2, 24, false, false, px)));
            ClipItem b = ClipItem.FromImage(Read(Make(2, 2, 24, false, false, px)));
            ClipItem c = ClipItem.FromImage(Read(Make(2, 2, 24, false, false, new[] { Red, Red, Blue, White })));
            Assert.Equal(a.Signature, b.Signature);
            Assert.True(a.Signature != c.Signature);
        }
    }

    public static class SignTests
    {
        static Pet NewPet()
        {
            Atlas a = TestAtlas.Make(
                "idle", true, new[] { 300, 300 },
                "walk_l", true, new[] { 110, 110 },
                "walk_r", true, new[] { 110, 110 },
                "walk_stop_r", false, new[] { 90, 110, 140 },
                "drag", true, new[] { 110 },
                "fall", true, new[] { 90 },
                "land", false, new[] { 60 },
                "eat_smoothie", false, new[] { 90, 120, 250, 150 },
                "sit_down", false, new[] { 80, 90 },
                "sleep_down", false, new[] { 200, 140 },
                "sleep", true, new[] { 800, 800 },
                "wake", false, new[] { 150, 150 },
                "stand_up", false, new[] { 90, 90 },
                "sign_up", false, new[] { 80, 100, 120 },
                "sign_hold", true, new[] { 400, 150, 400, 150 },
                "sign_down", false, new[] { 100, 80, 80 });
            var pet = new Pet(a, new Config(), new Rng(9));
            pet.Body.X = 500;
            pet.Body.Y = 500;
            pet.SetWorld(new Bounds(0, 0, 1000, 500));
            TestFoods.Give(pet);
            return pet;
        }

        static void Step(Pet pet, double seconds)
        {
            for (double t = 0; t < seconds; t += 1 / 60.0) pet.Update(1 / 60.0);
        }

        [Test]
        public static void SheRaisesHoldsAndLowersTheSign()
        {
            Pet pet = NewPet();
            Assert.True(pet.ShowSign("你好"));
            Assert.Equal("sign_up", pet.Anim.ClipName);
            Assert.Equal("你好", pet.Sign.Text);
            Step(pet, 0.5);
            Assert.True(pet.Signing.Holding);
            Assert.Equal("sign_hold", pet.Anim.ClipName);
            Step(pet, pet.SignSeconds);
            Assert.True(pet.State != pet.Signing || pet.Signing.Lowering);
            Step(pet, 0.5);
            Assert.True(pet.State == pet.Idle);
            Assert.True(pet.Sign == null, "the board is gone");
        }

        [Test]
        public static void NewContentFlipsTheBoardAndRestartsTheTimer()
        {
            Pet pet = NewPet();
            pet.ShowSign("第一条");
            Step(pet, 5);
            int serial = pet.Sign.Serial;
            Assert.True(pet.ShowSign("第二条"));
            Assert.Equal("第二条", pet.Sign.Text);
            Assert.True(pet.Sign.Serial != serial);
            Assert.Near(pet.Time, pet.Sign.FlippedAt, 1e-9);
            Step(pet, pet.SignSeconds - 1);
            Assert.True(pet.Signing.Holding, "held for a full stretch after the flip");
        }

        [Test]
        public static void AClickPutsItDownWithoutAnnoyingHer()
        {
            Pet pet = NewPet();
            pet.ShowSign("看这里");
            Step(pet, 1);
            pet.PointerDown(500, 470, pet.Time);
            pet.PointerUp(500, 470, pet.Time + 0.05);
            Assert.True(pet.Signing.Lowering);
            Assert.Equal(0.0, pet.Stats.Annoyance);
        }

        [Test]
        public static void UntilClickedMeansItStaysUp()
        {
            Pet pet = NewPet();
            pet.SignSeconds = 0;
            pet.ShowSign("一直举着");
            Step(pet, 60);
            Assert.True(pet.Signing.Holding);
        }

        [Test]
        public static void WhileBusyTheNewestTextWaitsForHer()
        {
            Pet pet = NewPet();
            pet.Stats.Fullness = 40;
            Assert.True(pet.Feed("smoothie"));
            Assert.False(pet.ShowSign("旧的"));
            Assert.False(pet.ShowSign("新的"));
            Assert.True(pet.Sign == null, "not while eating");
            Step(pet, 1.5);
            Assert.True(pet.State == pet.Signing, "raised once the meal is over");
            Assert.Equal("新的", pet.Sign.Text);
        }

        [Test]
        public static void AStaleWaitingSignIsDropped()
        {
            Pet pet = NewPet();
            pet.Stats.Fullness = 40;
            pet.Feed("smoothie");
            pet.ShowSign("很久以前");
            pet.Time += pet.Cfg.SignPendingSeconds + 1;
            Step(pet, 1.5);
            Assert.True(pet.State != pet.Signing);
            Assert.False(pet.HasPendingSign);
        }

        [Test]
        public static void WalkingSheStopsFirst()
        {
            Pet pet = NewPet();
            pet.Walk.Target = 700;
            pet.ChangeState(pet.Walk);
            Step(pet, 1);
            Assert.False(pet.ShowSign("停一下"));
            Step(pet, 0.2);
            Assert.Equal("walk_stop_r", pet.Anim.ClipName);
            Step(pet, 0.5);
            Assert.True(pet.State == pet.Signing);
        }

        [Test]
        public static void AStickySignStaysUpButSheStillTakesBreaks()
        {
            Pet pet = NewPet();
            pet.SetStickySign("常驻的内容");
            Step(pet, 0.5);
            Assert.True(pet.Signing.Holding);
            Assert.True(pet.Sign.Sticky);
            Step(pet, pet.SignSeconds + 20);
            Assert.True(pet.Signing.Holding, "no 8-second timeout");
            double breakAt = -1;
            for (double t = 0; t < pet.Cfg.StickyHoldMax && breakAt < 0; t += 0.5)
            {
                Step(pet, 0.5);
                if (pet.Sign == null) breakAt = pet.Time;
            }
            Assert.True(breakAt > 0, "a break within a minute or two");
            Step(pet, pet.Cfg.StickyBreakMax + 15);
            Assert.True(pet.Sign != null && pet.Sign.Sticky, "back up after the break");
            Assert.Equal("常驻的内容", pet.Sign.Text);
        }

        [Test]
        public static void AClickPutsTheStickySignAwayUntilTheNextCopy()
        {
            Pet pet = NewPet();
            pet.SetStickySign("常驻");
            Step(pet, 1);
            pet.PointerDown(500, 470, pet.Time);
            pet.PointerUp(500, 470, pet.Time + 0.05);
            Step(pet, 200);
            Assert.True(pet.State != pet.Signing, "stays put away");
            pet.SetStickySign("新复制的");
            Step(pet, 3);
            Assert.True(pet.State == pet.Signing);
            Assert.Equal("新复制的", pet.Sign.Text);
        }

        [Test]
        public static void AStickySignWaitsOutFocusAndAbsence()
        {
            Pet pet = NewPet();
            pet.Focusing = true;
            pet.SetStickySign("常驻");
            Step(pet, 2);
            Assert.True(pet.Sign == null, "not during a pomodoro");
            pet.Focusing = false;
            Step(pet, 2);
            Assert.True(pet.Sign != null && pet.Sign.Sticky, "up once focus is over");

            pet.UserIdleSeconds = 1000;
            Step(pet, 2);
            Assert.True(pet.Sign == null, "down while you are away");
            pet.UserIdleSeconds = 0;
            Step(pet, 3);
            Assert.True(pet.Sign != null && pet.Sign.Sticky, "and back up when you return");
        }

        [Test]
        public static void BeingPickedUpDropsTheSign()
        {
            Pet pet = NewPet();
            pet.ShowSign("抓我试试");
            Step(pet, 1);
            pet.PointerDown(500, 470, pet.Time);
            pet.PointerMove(540, 400, pet.Time + 0.05);
            Assert.True(pet.State == pet.Drag);
            Assert.True(pet.Sign == null);
        }
    }
}
