using System.IO;
using Aly.Core;
using Aly.Core.Engine;

namespace Aly.Tests
{
    public static class NoteTests
    {
        static Pet NewPet()
        {
            Atlas a = TestAtlas.Make(
                "idle", true, new[] { 300, 300 },
                "walk_l", true, new[] { 110, 110 },
                "walk_r", true, new[] { 110, 110 },
                "drag", true, new[] { 110 },
                "fall", true, new[] { 90 },
                "land", false, new[] { 60 },
                "happy", false, new[] { 80, 100, 100 },
                "stretch", false, new[] { 150, 150, 200, 400 },
                "drink_water", false, new[] { 120, 250, 350, 250 },
                "sit_down", false, new[] { 80, 90 },
                "stand_up", false, new[] { 90, 90 },
                "sit", true, new[] { 400, 120 },
                "sleep_down", false, new[] { 200, 140 },
                "sleep", true, new[] { 800, 800 },
                "wake", false, new[] { 150, 150 },
                "work_start", false, new[] { 90, 120, 150 },
                "work", true, new[] { 240, 180, 240, 180 },
                "work_end", false, new[] { 120, 120 },
                "sign_up", false, new[] { 80, 100, 120 },
                "sign_hold", true, new[] { 400, 150, 400, 150 },
                "sign_down", false, new[] { 100, 80, 80 });
            var pet = new Pet(a, new Config(), new Rng(5));
            pet.Body.X = 500;
            pet.Body.Y = 500;
            pet.SetWorld(new Bounds(0, 0, 1000, 500));
            var lines = new Lines();
            lines.Load(new StringReader("[focus_shh]\n嘘——在专注呢（还剩 {0} 分钟）\n[todo_done]\n完成啦！"));
            pet.Lines = lines;
            return pet;
        }

        static void Step(Pet pet, double seconds)
        {
            for (double t = 0; t < seconds; t += 1 / 60.0) pet.Update(1 / 60.0);
        }

        static NoteRequest Note(string text, object tag, string intro = null)
        {
            var n = new NoteRequest();
            n.Text = text;
            n.Buttons = new[] { "好", "稍后" };
            n.Tag = tag;
            n.Intro = intro;
            return n;
        }

        static void Click(Pet pet)
        {
            pet.PointerDown(500, 470, pet.Time);
            pet.PointerUp(500, 470, pet.Time + 0.05);
        }

        [Test]
        public static void AClickOnHerAnswersYes()
        {
            Pet pet = NewPet();
            Assert.True(pet.ShowNote(Note("该喝水啦", "water")));
            Assert.Equal(SignStyle.Note, pet.Sign.Style);
            Step(pet, 0.5);
            Click(pet);
            SignContent note;
            int answer;
            Assert.True(pet.TakeNoteAnswer(out note, out answer));
            Assert.Equal(0, answer);
            Assert.Equal("water", (string)note.Tag);
            Assert.False(pet.TakeNoteAnswer(out note, out answer), "reported once");
            Assert.True(pet.Signing.Lowering);
            Assert.Equal(0.0, pet.Stats.Annoyance);
        }

        [Test]
        public static void AButtonGivesItsOwnAnswer()
        {
            Pet pet = NewPet();
            pet.ShowNote(Note("交作业", 7));
            Step(pet, 0.5);
            pet.AnswerNote(1);
            SignContent note;
            int answer;
            pet.TakeNoteAnswer(out note, out answer);
            Assert.Equal(1, answer);
            Assert.Equal(7, (int)note.Tag);
        }

        [Test]
        public static void ANoteTakesOverTheClipboardSignButNotTheOtherWayRound()
        {
            Pet pet = NewPet();
            pet.ShowSign("复制的东西");
            Step(pet, 1);
            Assert.True(pet.ShowNote(Note("该喝水啦", "water")));
            Assert.Equal(SignStyle.Note, pet.Sign.Style);
            Assert.Near(pet.Time, pet.Sign.FlippedAt, 1e-9);
            Assert.False(pet.ShowSign("又复制了"));
            Assert.Equal("该喝水啦", pet.Sign.Text);
            Click(pet);
            SignContent note;
            int answer;
            pet.TakeNoteAnswer(out note, out answer);
            Assert.Equal(0, answer, "the click answers the note, it doesn't just lower it");
        }

        [Test]
        public static void TheIntroPlaysFirstAndAPickUpPutsTheNoteBack()
        {
            Pet pet = NewPet();
            Assert.True(pet.ShowNote(Note("该喝水啦", "water", "drink_water")));
            Assert.Equal("drink_water", pet.Anim.ClipName);
            Assert.True(pet.Sign == null, "not yet");
            pet.PointerDown(500, 470, pet.Time);
            pet.PointerMove(540, 400, pet.Time + 0.05); // picked up mid-drink
            pet.PointerUp(540, 400, pet.Time + 0.1);
            Step(pet, 3);
            Assert.True(pet.Sign != null && pet.Sign.Style == SignStyle.Note, "raised once she landed");
            SignContent note;
            int answer;
            Assert.False(pet.TakeNoteAnswer(out note, out answer), "nothing to report: it was never answered");
        }

        [Test]
        public static void IgnoredAndDismissedAreTold()
        {
            Pet pet = NewPet();
            pet.ShowNote(Note("该喝水啦", "a"));
            Step(pet, SignState.NoteSeconds + 2);
            SignContent note;
            int answer;
            Assert.True(pet.TakeNoteAnswer(out note, out answer));
            Assert.Equal(NoteAnswer.Ignored, answer);

            pet.ShowNote(Note("交作业", "b"));
            Step(pet, 1);
            pet.PointerDown(500, 470, pet.Time);
            pet.PointerMove(540, 400, pet.Time + 0.05);
            Assert.True(pet.TakeNoteAnswer(out note, out answer));
            Assert.Equal(NoteAnswer.Dismissed, answer);
            Assert.Equal("b", (string)note.Tag);
        }

        [Test]
        public static void SheWorksWhileFocusingAndShushesPokes()
        {
            Pet pet = NewPet();
            pet.Focusing = true;
            pet.FocusMinutesLeft = 12;
            Step(pet, 0.2);
            Assert.True(pet.State == pet.Work);
            Step(pet, 1);
            Assert.Equal("work", pet.Anim.ClipName);
            Click(pet);
            Assert.Equal("嘘——在专注呢（还剩 12 分钟）", pet.Speech == null ? null : pet.Speech.Text);
            Assert.Equal(0.0, pet.Stats.Annoyance, "no annoyance while working");
            pet.Focusing = false;
            Step(pet, 1);
            Assert.True(pet.State != pet.Work);
        }

        [Test]
        public static void ANoteDuringWorkWaitsForHerToStandUp()
        {
            Pet pet = NewPet();
            pet.Focusing = true;
            Step(pet, 1);
            Assert.False(pet.ShowNote(Note("交作业", 1)));
            Step(pet, 0.5);
            Assert.True(pet.Sign != null && pet.Sign.Text == "交作业");
            pet.AnswerNote(0);
            Step(pet, 1);
            Assert.True(pet.State == pet.Work, "back to work afterwards");
        }

        [Test]
        public static void SheReactsOnceTheNoteIsDown()
        {
            Pet pet = NewPet();
            pet.ShowNote(Note("交作业", 1));
            Step(pet, 0.5);
            pet.AnswerNote(0);
            pet.AfterNote("happy", "todo_done");
            Assert.True(pet.State == pet.Signing, "lowers first");
            Step(pet, 0.4);
            Assert.Equal("happy", pet.Anim.ClipName);
            Assert.Equal("完成啦！", pet.Speech == null ? null : pet.Speech.Text);
        }

        [Test]
        public static void ANapEndsForANoteButAwayItWaits()
        {
            Pet pet = NewPet();
            pet.Sleep.Begin(pet, false);
            Step(pet, 2);
            Assert.True(pet.State.Sleeping);
            Assert.False(pet.ShowNote(Note("该睡觉啦", "bed")));
            Step(pet, 2);
            Assert.True(pet.Sign != null, "woke up to show it");

            Pet away = NewPet();
            away.UserIdleSeconds = 1000;
            Step(away, 1);
            Assert.True(away.State == away.Sleep);
            Assert.False(away.ShowNote(Note("交作业", 2)));
            Step(away, 3);
            Assert.True(away.Sign == null, "nobody there to read it");
            away.UserIdleSeconds = 0;
            Step(away, 4);
            Assert.True(away.Sign != null, "shown once you are back");
        }

        [Test]
        public static void HidingHerDismissesTheNote()
        {
            Pet pet = NewPet();
            pet.ShowNote(Note("交作业", 3));
            Step(pet, 0.5);
            pet.DropSigns();
            SignContent note;
            int answer;
            Assert.True(pet.TakeNoteAnswer(out note, out answer));
            Assert.Equal(NoteAnswer.Dismissed, answer);
            Assert.True(pet.State == pet.Idle);
        }
    }
}
