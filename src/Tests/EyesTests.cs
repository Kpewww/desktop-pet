using Aly.Core.Engine;

namespace Aly.Tests
{
    public static class EyesTests
    {
        static void Run(Eyes eyes, ref double now, double seconds)
        {
            for (double t = 0; t < seconds; t += 0.01)
            {
                now += 0.01;
                eyes.Update(now, 0.01);
            }
        }

        [Test]
        public static void BlinksWithinAFewSecondsAndReopens()
        {
            var eyes = new Eyes(new Rng(11), new[] { 40, 70, 40 }, 0);
            double now = 0;
            bool sawBlink = false;
            for (int i = 0; i < 700 && !sawBlink; i++)
            {
                now += 0.01;
                eyes.Update(now, 0.01);
                sawBlink = eyes.Blinking;
            }
            Assert.True(sawBlink, "no blink in 7 s");
            Assert.Equal(EyeSprite.Blink, eyes.Sprite);
            Run(eyes, ref now, 0.2);
            // either reopened, or already inside a quick double blink
            Assert.True(!eyes.Blinking || eyes.BlinkFrame >= 0);
            Run(eyes, ref now, 0.5);
            Assert.False(eyes.Blinking, "stuck mid-blink");
        }

        [Test]
        public static void GazeWalksOneStepAtATime()
        {
            var eyes = new Eyes(new Rng(3), null, 1e9); // no blinking in this test
            double now = 0;
            eyes.LookAt(2, 1, now);
            Run(eyes, ref now, 0.08);
            Assert.Equal(1, eyes.H);
            Assert.Equal(1, eyes.V);
            Run(eyes, ref now, 0.1);
            Assert.Equal(2, eyes.H);
            Assert.Equal(EyeSprite.OpenRight, eyes.Sprite);
            Assert.Equal(1, eyes.OffsetX);
            eyes.LookAt(-2, 0, now);
            Run(eyes, ref now, 0.08);
            Assert.Equal(1, eyes.H); // still travelling, never jumps
            Run(eyes, ref now, 0.5);
            Assert.Equal(-2, eyes.H);
            Assert.Equal(0, eyes.V);
            Assert.Equal(EyeSprite.OpenLeft, eyes.Sprite);
        }

        [Test]
        public static void TargetsAreClamped()
        {
            var eyes = new Eyes(new Rng(3), null, 1e9);
            eyes.LookAt(9, -9, 0);
            Assert.Equal(2, eyes.TargetH);
            Assert.Equal(-1, eyes.TargetV);
        }

        [Test]
        public static void NextWakeCoversPendingSteps()
        {
            var eyes = new Eyes(new Rng(3), null, 1e9);
            eyes.LookAt(1, 0, 0);
            Assert.True(eyes.NextWake(0) <= 0.07);
        }
    }
}
