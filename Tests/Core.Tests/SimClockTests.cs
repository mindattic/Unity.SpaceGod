using SolarSystem.Core;
using Xunit;

namespace SolarSystem.Core.Tests
{
    public class SimClockTests
    {
        [Fact]
        public void AdvanceAppliesWarpAndPause()
        {
            var clock = new SimClock(Astro.J2000);
            clock.SetWarp(6); // 1 day/s
            Assert.Equal(1.0, clock.Advance(1.0), 9);
            clock.Paused = true;
            Assert.Equal(0.0, clock.Advance(1.0));
            Assert.Equal(Astro.J2000 + 1, clock.Jd, 9);
        }

        [Fact]
        public void WarpIsClamped()
        {
            var clock = new SimClock(Astro.J2000);
            for (int i = 0; i < 50; i++) clock.Faster();
            Assert.Equal(SimClock.Levels.Length - 1, clock.WarpIndex);
            for (int i = 0; i < 50; i++) clock.Slower();
            Assert.Equal(0, clock.WarpIndex);
        }

        [Fact]
        public void PauseEasesToAStopAndResumesTheSameWay()
        {
            var clock = new SimClock(Astro.J2000);
            clock.SetWarp(6);
            clock.ToggleEasedPause();
            Assert.True(clock.Pausing && clock.PauseHeld);
            double frame = 1.0 / 144, t = 0, previous = 1;
            while (!clock.Paused && t < 10)
            {
                clock.Advance(frame);
                t += frame;
                Assert.True(clock.Speed <= previous);
                previous = clock.Speed;
            }
            // 0.97 per step at 120 steps/s from 1 to under 1%: ln(0.01)/ln(0.97)/120 = 1.26 s.
            Assert.True(clock.Paused);
            Assert.InRange(t, 1.15, 1.4);
            Assert.Equal(0, clock.Speed);
            double jd = clock.Jd;
            clock.Advance(1);
            Assert.Equal(jd, clock.Jd);

            clock.ToggleEasedPause();
            Assert.True(clock.Resuming && !clock.Paused);
            t = 0;
            while (clock.Resuming && t < 10)
            {
                clock.Advance(frame);
                t += frame;
            }
            Assert.Equal(1, clock.Speed);
            Assert.InRange(t, 1.15, 1.4);

            // Reversing mid-ease picks up from where it had got to, not from a standstill.
            clock.ToggleEasedPause();
            for (int i = 0; i < 100; i++) clock.Advance(frame);
            double partway = clock.Speed;
            clock.ToggleEasedPause();
            Assert.Equal(partway, clock.Speed);
            Assert.True(clock.Resuming);
        }
    }
}
