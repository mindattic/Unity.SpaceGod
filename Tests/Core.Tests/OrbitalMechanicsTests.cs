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

        // JPL Horizons, 2026-10-11 00:00 TDB (JD 2461324.5): moon relative to its parent's centre,
        // km, ecliptic J2000. Phobos and Miranda get a looser bound (see BodyCatalog notes).
        [Theory]
        [InlineData("Phobos", 6805.860, 5566.134, -2893.088, 2.5)]
        [InlineData("Deimos", -10864.930, 19858.946, 6120.097, 0.5)]
        [InlineData("Io", 312960.209, -281217.598, -5691.267, 0.5)]
        [InlineData("Europa", -646550.487, 158099.391, -8278.278, 0.5)]
        [InlineData("Ganymede", -1072964.415, -17907.116, -16372.860, 0.5)]
        [InlineData("Callisto", 510004.941, 1804866.535, 63394.103, 0.5)]
        [InlineData("Enceladus", 222712.926, 60762.699, -53386.313, 0.5)]
        [InlineData("Titan", -485791.432, -982253.753, 554801.302, 0.5)]
        [InlineData("Miranda", 74985.184, -37264.615, -99478.646, 2.0)]
        [InlineData("Titania", -351233.081, 109719.265, 234883.094, 0.5)]
        [InlineData("Oberon", 348855.606, -9719.491, 466435.960, 0.5)]
        [InlineData("Triton", -76691.585, 222948.357, 265017.955, 0.5)]
        [InlineData("Charon", -13128.476, -14545.454, -435.856, 0.5)]
        public void MoonsMatchJplHorizons(string name, double x, double y, double z, double toleranceDeg)
        {
            var eph = new Ephemeris(Catalog);
            eph.Update(2461324.5);
            var rel = eph.Relative(Catalog[name].Index);
            var truth = new DVec3(x, y, z);
            double deg = Math.Acos(Math.Min(1, DVec3.Dot(rel.Normalized, truth.Normalized))) * Astro.Rad2Deg;
            Assert.True(deg < toleranceDeg, $"{name} is {deg:0.00} degrees off");
            Assert.InRange(rel.Length / truth.Length, 0.97, 1.03);
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
