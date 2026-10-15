using System;
using SolarSystem.Core;
using Xunit;

namespace SolarSystem.Core.Tests
{
    public class AstroTests
    {
        [Theory]
        [InlineData(-0.5)]
        [InlineData(0.5)]
        [InlineData(3.0)]
        [InlineData(-3.0)]
        public void WrapRadiansIsIdentityWithinRange(double a)
        {
            Assert.Equal(a, Astro.WrapRadians(a), 12);
        }

        [Fact]
        public void WrapRadiansWrapsMultipleTurns()
        {
            Assert.Equal(1.0, Astro.WrapRadians(1.0 + 4 * Math.PI), 9);
            Assert.Equal(-1.0, Astro.WrapRadians(-1.0 - 4 * Math.PI), 9);
            Assert.Equal(Math.PI, Astro.WrapRadians(5 * Math.PI), 9);
            Assert.Equal(-Math.PI, Astro.WrapRadians(-5 * Math.PI), 9);
        }

        [Theory]
        [InlineData(-30, 330)]
        [InlineData(-765, 315)]
        [InlineData(360, 0)]
        [InlineData(720.5, 0.5)]
        [InlineData(-0.5, 359.5)]
        [InlineData(0, 0)]
        public void WrapDegrees360WrapsIntoZeroTo360(double input, double expected)
        {
            Assert.Equal(expected, Astro.WrapDegrees360(input), 9);
        }

        [Fact]
        public void DmsCombinesDegreesMinutesAndSeconds()
        {
            Assert.Equal(45.5, Astro.Dms(45, 30, 0), 9);
            Assert.Equal(-45.5, Astro.Dms(45, 30, 0, south: true), 9);
        }

        [Fact]
        public void DmsSouthFlagControlsSignRegardlessOfDegreesSign()
        {
            // Only the `south` flag decides sign; a negative degrees argument is just a magnitude.
            Assert.Equal(45.5, Astro.Dms(-45, 30, 0));
            Assert.Equal(-45.5, Astro.Dms(-45, 30, 0, south: true));
        }

        [Theory]
        [InlineData(0, 0, 0, 0)]
        [InlineData(12, 0, 0, 180)]
        [InlineData(24, 0, 0, 360)]
        [InlineData(6, 45, 9, 101.2875)]
        public void HmsConvertsHoursMinutesSecondsToDegrees(double h, double m, double s, double expectedDeg)
        {
            Assert.Equal(expectedDeg, Astro.Hms(h, m, s), 9);
        }

        [Fact]
        public void SmoothStepClampsBelowLowerEdge()
        {
            Assert.Equal(0.0, Astro.SmoothStep(0, 10, -5));
            Assert.Equal(0.0, Astro.SmoothStep(0, 10, 0));
        }

        [Fact]
        public void SmoothStepClampsAboveUpperEdge()
        {
            Assert.Equal(1.0, Astro.SmoothStep(0, 10, 15));
            Assert.Equal(1.0, Astro.SmoothStep(0, 10, 10));
        }

        [Fact]
        public void SmoothStepIsOneHalfAtTheMidpoint()
        {
            Assert.Equal(0.5, Astro.SmoothStep(0, 10, 5), 12);
            Assert.Equal(0.5, Astro.SmoothStep(-4, 4, 0), 12);
        }

        [Theory]
        [InlineData(0.3, 0.6, 0.74)]
        [InlineData(-0.5, 0.2, 0.1)]
        [InlineData(1, 0, 0)]
        [InlineData(0, 0, 1)]
        public void EquatorialEclipticConversionsAreInverses(double x, double y, double z)
        {
            var v = new DVec3(x, y, z);

            var toEcliptic = Astro.EquatorialToEcliptic(v);
            var backFromEcliptic = Astro.EclipticToEquatorial(toEcliptic);
            Assert.True((backFromEcliptic - v).Length < 1e-12);

            var toEquatorial = Astro.EclipticToEquatorial(v);
            var backFromEquatorial = Astro.EquatorialToEcliptic(toEquatorial);
            Assert.True((backFromEquatorial - v).Length < 1e-12);
        }

        [Fact]
        public void EclipticRotationPreservesLengthAndTheXAxis()
        {
            var v = new DVec3(2, 3, -4);
            var e = Astro.EquatorialToEcliptic(v);
            Assert.Equal(v.X, e.X, 12);
            Assert.Equal(v.Length, e.Length, 9);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(90, 45)]
        [InlineData(180, -45)]
        [InlineData(270, 0)]
        [InlineData(359.999, 10)]
        public void DirectionFromRaDecAndRaDecOfRoundTripAwayFromThePoles(double raDeg, double decDeg)
        {
            var dir = Astro.DirectionFromRaDec(raDeg, decDeg);
            Astro.RaDecOf(dir, out double ra2, out double dec2);

            Assert.Equal(raDeg, ra2, 6);
            Assert.Equal(decDeg, dec2, 9);
        }

        [Fact]
        public void DirectionFromRaDecWrapsOutOfRangeRightAscension()
        {
            var below = Astro.DirectionFromRaDec(-10, 20);
            Astro.RaDecOf(below, out double raBelow, out double decBelow);
            Assert.Equal(350.0, raBelow, 6);
            Assert.Equal(20.0, decBelow, 9);

            var above = Astro.DirectionFromRaDec(400, -20);
            Astro.RaDecOf(above, out double raAbove, out double decAbove);
            Assert.Equal(40.0, raAbove, 6);
            Assert.Equal(-20.0, decAbove, 9);
        }

        [Fact]
        public void DirectionFromRaDecRoundTripsDeclinationAtTheNorthPole()
        {
            var dir = Astro.DirectionFromRaDec(123.0, 90.0);
            Astro.RaDecOf(dir, out _, out double dec);
            Assert.Equal(90.0, dec, 6);
        }

        [Fact]
        public void DirectionFromRaDecRoundTripsDeclinationAtTheSouthPole()
        {
            var dir = Astro.DirectionFromRaDec(50.0, -90.0);
            Astro.RaDecOf(dir, out _, out double dec);
            Assert.Equal(-90.0, dec, 6);
        }
    }
}
