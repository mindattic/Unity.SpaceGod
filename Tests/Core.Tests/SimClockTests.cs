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
    }
}
