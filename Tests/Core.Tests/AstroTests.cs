using System;
using SolarSystem.Core;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    [TestFixture]
    public class AstroTests
    {
        [TestCase(-0.5)]
        [TestCase(0.5)]
        [TestCase(3.0)]
        [TestCase(-3.0)]
        public void WrapRadiansIsIdentityWithinRange(double a)
        {
            Assert.That(Astro.WrapRadians(a), Is.EqualTo(a).Within(1e-12));
        }

        [Test]
        public void WrapRadiansWrapsMultipleTurns()
        {
            Assert.That(Astro.WrapRadians(1.0 + 4 * Math.PI), Is.EqualTo(1.0).Within(1e-9));
            Assert.That(Astro.WrapRadians(-1.0 - 4 * Math.PI), Is.EqualTo(-1.0).Within(1e-9));
            Assert.That(Astro.WrapRadians(5 * Math.PI), Is.EqualTo(Math.PI).Within(1e-9));
            Assert.That(Astro.WrapRadians(-5 * Math.PI), Is.EqualTo(-Math.PI).Within(1e-9));
        }

        [TestCase(-30, 330)]
        [TestCase(-765, 315)]
        [TestCase(360, 0)]
        [TestCase(720.5, 0.5)]
        [TestCase(-0.5, 359.5)]
        [TestCase(0, 0)]
        public void WrapDegrees360WrapsIntoZeroTo360(double input, double expected)
        {
            Assert.That(Astro.WrapDegrees360(input), Is.EqualTo(expected).Within(1e-9));
        }

        [Test]
        public void DmsCombinesDegreesMinutesAndSeconds()
        {
            Assert.That(Astro.Dms(45, 30, 0), Is.EqualTo(45.5).Within(1e-9));
            Assert.That(Astro.Dms(45, 30, 0, south: true), Is.EqualTo(-45.5).Within(1e-9));
        }

        [Test]
        public void DmsSouthFlagControlsSignRegardlessOfDegreesSign()
        {
            // Only the `south` flag decides sign; a negative degrees argument is just a magnitude.
            Assert.That(Astro.Dms(-45, 30, 0), Is.EqualTo(45.5));
            Assert.That(Astro.Dms(-45, 30, 0, south: true), Is.EqualTo(-45.5));
        }

        [TestCase(0, 0, 0, 0)]
        [TestCase(12, 0, 0, 180)]
        [TestCase(24, 0, 0, 360)]
        [TestCase(6, 45, 9, 101.2875)]
        public void HmsConvertsHoursMinutesSecondsToDegrees(double h, double m, double s, double expectedDeg)
        {
            Assert.That(Astro.Hms(h, m, s), Is.EqualTo(expectedDeg).Within(1e-9));
        }

        [Test]
        public void SmoothStepClampsBelowLowerEdge()
        {
            Assert.That(Astro.SmoothStep(0, 10, -5), Is.EqualTo(0.0));
            Assert.That(Astro.SmoothStep(0, 10, 0), Is.EqualTo(0.0));
        }

        [Test]
        public void SmoothStepClampsAboveUpperEdge()
        {
            Assert.That(Astro.SmoothStep(0, 10, 15), Is.EqualTo(1.0));
            Assert.That(Astro.SmoothStep(0, 10, 10), Is.EqualTo(1.0));
        }

        [Test]
        public void SmoothStepIsOneHalfAtTheMidpoint()
        {
            Assert.That(Astro.SmoothStep(0, 10, 5), Is.EqualTo(0.5).Within(1e-12));
            Assert.That(Astro.SmoothStep(-4, 4, 0), Is.EqualTo(0.5).Within(1e-12));
        }

        [TestCase(0.3, 0.6, 0.74)]
        [TestCase(-0.5, 0.2, 0.1)]
        [TestCase(1, 0, 0)]
        [TestCase(0, 0, 1)]
        public void EquatorialEclipticConversionsAreInverses(double x, double y, double z)
        {
            var v = new DVec3(x, y, z);

            var toEcliptic = Astro.EquatorialToEcliptic(v);
            var backFromEcliptic = Astro.EclipticToEquatorial(toEcliptic);
            Assert.That((backFromEcliptic - v).Length < 1e-12, Is.True);

            var toEquatorial = Astro.EclipticToEquatorial(v);
            var backFromEquatorial = Astro.EquatorialToEcliptic(toEquatorial);
            Assert.That((backFromEquatorial - v).Length < 1e-12, Is.True);
        }

        [Test]
        public void EclipticRotationPreservesLengthAndTheXAxis()
        {
            var v = new DVec3(2, 3, -4);
            var e = Astro.EquatorialToEcliptic(v);
            Assert.That(e.X, Is.EqualTo(v.X).Within(1e-12));
            Assert.That(e.Length, Is.EqualTo(v.Length).Within(1e-9));
        }

        [TestCase(0, 0)]
        [TestCase(90, 45)]
        [TestCase(180, -45)]
        [TestCase(270, 0)]
        [TestCase(359.999, 10)]
        public void DirectionFromRaDecAndRaDecOfRoundTripAwayFromThePoles(double raDeg, double decDeg)
        {
            var dir = Astro.DirectionFromRaDec(raDeg, decDeg);
            Astro.RaDecOf(dir, out double ra2, out double dec2);

            Assert.That(ra2, Is.EqualTo(raDeg).Within(1e-6));
            Assert.That(dec2, Is.EqualTo(decDeg).Within(1e-9));
        }

        [Test]
        public void DirectionFromRaDecWrapsOutOfRangeRightAscension()
        {
            var below = Astro.DirectionFromRaDec(-10, 20);
            Astro.RaDecOf(below, out double raBelow, out double decBelow);
            Assert.That(raBelow, Is.EqualTo(350.0).Within(1e-6));
            Assert.That(decBelow, Is.EqualTo(20.0).Within(1e-9));

            var above = Astro.DirectionFromRaDec(400, -20);
            Astro.RaDecOf(above, out double raAbove, out double decAbove);
            Assert.That(raAbove, Is.EqualTo(40.0).Within(1e-6));
            Assert.That(decAbove, Is.EqualTo(-20.0).Within(1e-9));
        }

        [Test]
        public void DirectionFromRaDecRoundTripsDeclinationAtTheNorthPole()
        {
            var dir = Astro.DirectionFromRaDec(123.0, 90.0);
            Astro.RaDecOf(dir, out _, out double dec);
            Assert.That(dec, Is.EqualTo(90.0).Within(1e-6));
        }

        [Test]
        public void DirectionFromRaDecRoundTripsDeclinationAtTheSouthPole()
        {
            var dir = Astro.DirectionFromRaDec(50.0, -90.0);
            Astro.RaDecOf(dir, out _, out double dec);
            Assert.That(dec, Is.EqualTo(-90.0).Within(1e-6));
        }
    }
}
