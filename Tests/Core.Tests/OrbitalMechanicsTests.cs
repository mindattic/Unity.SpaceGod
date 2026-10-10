using System;
using SolarSystem.Core;
using Xunit;

namespace SolarSystem.Core.Tests
{
    public class OrbitalMechanicsTests
    {
        static readonly BodyCatalog Catalog = BodyCatalog.CreateSolSystem();

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.0167)]
        [InlineData(0.2056)]
        [InlineData(0.5)]
        [InlineData(0.9)]
        [InlineData(0.97)]
        public void KeplerSolverSatisfiesKeplersEquation(double e)
        {
            for (double m = -Math.PI; m <= Math.PI; m += 0.05)
            {
                double ecc = Kepler.SolveEccentricAnomaly(m, e);
                Assert.True(Math.Abs(ecc - e * Math.Sin(ecc) - m) < 1e-10, $"e={e} M={m}");
            }
        }

        [Fact]
        public void EarthIsAtPerihelionDistanceAtJ2000()
        {
            // Perihelion was 2000-01-03; on Jan 1.5 Earth was 0.98333 AU from the Sun.
            var eph = new Ephemeris(Catalog);
            eph.Update(Astro.J2000);
            double au = eph.Helio(Catalog["Earth"].Index).Length / Astro.AuKm;
            Assert.InRange(au, 0.982, 0.9845);
        }

        [Fact]
        public void EarthHeliocentricLongitudeAtJ2000()
        {
            // The Sun's geocentric ecliptic longitude at J2000 is 280.46°, so Earth's heliocentric one is ~100.46°.
            var eph = new Ephemeris(Catalog);
            eph.Update(Astro.J2000);
            var earth = eph.Helio(Catalog["Earth"].Index);
            double lon = Astro.WrapDegrees360(Math.Atan2(earth.Y, earth.X) * Astro.Rad2Deg);
            Assert.InRange(lon, 99.9, 101.0);
        }

        [Theory]
        [InlineData("Mercury", 87.97)]
        [InlineData("Mars", 686.98)]
        [InlineData("Jupiter", 4332.6)]
        [InlineData("Neptune", 60190)]
        public void PlanetPeriodsMatchObserved(string name, double days)
        {
            var k = Catalog[name].Elements.At(Astro.J2000);
            Assert.InRange(k.PeriodDays, days * 0.995, days * 1.005);
        }

        [Fact]
        public void CeresPeriodComesFromKeplersThirdLaw()
        {
            var k = Catalog["Ceres"].Elements.At(Astro.J2000);
            Assert.InRange(k.PeriodDays / 365.25, 4.59, 4.62);
        }

        [Fact]
        public void MoonStaysBetweenPerigeeAndApogee()
        {
            var eph = new Ephemeris(Catalog);
            int moon = Catalog["Moon"].Index;
            double min = double.MaxValue, max = 0;
            for (double jd = Astro.J2000; jd < Astro.J2000 + 60; jd += 0.25)
            {
                eph.Update(jd);
                double d = eph.Relative(moon).Length;
                min = Math.Min(min, d);
                max = Math.Max(max, d);
            }
            Assert.InRange(min, 356000, 370000);
            Assert.InRange(max, 400000, 407500);
        }

        [Fact]
        public void EarthIsOffsetFromTheBarycentreByTheMoon()
        {
            var eph = new Ephemeris(Catalog);
            eph.Update(Astro.J2000 + 100);
            var earth = Catalog["Earth"];
            var moon = Catalog["Moon"];
            var emb = Kepler.Position(earth.Elements.At(Astro.J2000 + 100));
            double offset = (eph.Helio(earth.Index) - emb).Length;
            Assert.InRange(offset, 4300, 5000);
            Assert.True(DVec3.Dot(eph.Helio(earth.Index) - emb, eph.Relative(moon.Index)) < 0, "Earth sits on the far side of the barycentre from the Moon");
        }

        [Theory]
        [InlineData("Titania", "Uranus", true)]
        [InlineData("Charon", "Pluto", true)]
        [InlineData("Io", "Jupiter", true)]
        [InlineData("Triton", "Neptune", false)]
        public void MoonsOrbitInTheirParentsSpinSense(string moonName, string parentName, bool prograde)
        {
            var eph = new Ephemeris(Catalog);
            eph.Update(Astro.J2000 + 3);
            int moon = Catalog[moonName].Index;
            var h = DVec3.Cross(eph.Relative(moon), eph.RelativeVelocityKmS(moon));
            double sense = DVec3.Dot(h.Normalized, Catalog[parentName].SpinAxis);
            Assert.Equal(prograde, sense > 0);
        }

        [Fact]
        public void HelioAtMatchesCachedFrame()
        {
            var eph = new Ephemeris(Catalog);
            double jd = Astro.J2000 + 9000.3;
            eph.Update(jd);
            for (int i = 0; i < Catalog.Count; i++)
                Assert.True((eph.HelioAt(i, jd) - eph.Helio(i)).Length < 1e-3, Catalog[i].Name);
        }

        [Fact]
        public void OrbitalSpeedOfEarthIsAbout30KmPerSecond()
        {
            var eph = new Ephemeris(Catalog);
            eph.Update(Astro.J2000);
            Assert.InRange(eph.OrbitalSpeedKmS(Catalog["Earth"].Index), 29.3, 30.5);
        }

        [Fact]
        public void JulianDateRoundTrips()
        {
            var t = new DateTime(2026, 10, 10, 18, 30, 0, DateTimeKind.Utc);
            double jd = Astro.JulianDate(t);
            Assert.Equal(2461324.270833, jd, 5);
            Assert.True(Math.Abs((Astro.ToDateTime(jd) - t).TotalSeconds) < 0.01);
        }
    }
}
