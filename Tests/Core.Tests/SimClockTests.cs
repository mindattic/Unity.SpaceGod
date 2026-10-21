using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    [TestFixture]
    public class SimClockTests
    {
        [Test]
        public void AdvanceAppliesWarpAndPause()
        {
            var clock = new SimClock(Astro.J2000);
            clock.SetWarp(6); // 1 day/s
            Assert.That(clock.Advance(1.0), Is.EqualTo(1.0).Within(1e-9));
            clock.Paused = true;
            Assert.That(clock.Advance(1.0), Is.EqualTo(0.0));
            Assert.That(clock.Jd, Is.EqualTo(Astro.J2000 + 1).Within(1e-9));
        }

        [Test]
        public void WarpIsClamped()
        {
            var clock = new SimClock(Astro.J2000);
            for (int i = 0; i < 50; i++) clock.Faster();
            Assert.That(clock.WarpIndex, Is.EqualTo(SimClock.Levels.Length - 1));
            for (int i = 0; i < 50; i++) clock.Slower();
            Assert.That(clock.WarpIndex, Is.EqualTo(0));
        }

        [Test]
        public void PauseEasesToAStopAndResumesTheSameWay()
        {
            var clock = new SimClock(Astro.J2000);
            clock.SetWarp(6);
            clock.ToggleEasedPause();
            Assert.That(clock.Pausing && clock.PauseHeld, Is.True);
            double frame = 1.0 / 144, t = 0, previous = 1;
            while (!clock.Paused && t < 10)
            {
                clock.Advance(frame);
                t += frame;
                Assert.That(clock.Speed <= previous, Is.True);
                previous = clock.Speed;
            }
            // 0.97 per step at 120 steps/s from 1 to under 1%: ln(0.01)/ln(0.97)/120 = 1.26 s.
            Assert.That(clock.Paused, Is.True);
            Assert.That(t, Is.InRange(1.15, 1.4));
            Assert.That(clock.Speed, Is.EqualTo(0));
            double jd = clock.Jd;
            clock.Advance(1);
            Assert.That(clock.Jd, Is.EqualTo(jd));

            clock.ToggleEasedPause();
            Assert.That(clock.Resuming && !clock.Paused, Is.True);
            t = 0;
            while (clock.Resuming && t < 10)
            {
                clock.Advance(frame);
                t += frame;
            }
            Assert.That(clock.Speed, Is.EqualTo(1));
            Assert.That(t, Is.InRange(1.15, 1.4));

            // Reversing mid-ease picks up from where it had got to, not from a standstill.
            clock.ToggleEasedPause();
            for (int i = 0; i < 100; i++) clock.Advance(frame);
            double partway = clock.Speed;
            clock.ToggleEasedPause();
            Assert.That(clock.Speed, Is.EqualTo(partway));
            Assert.That(clock.Resuming, Is.True);
        }
    }
}
