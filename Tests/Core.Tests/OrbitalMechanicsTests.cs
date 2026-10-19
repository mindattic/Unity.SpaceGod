using System;
using UnitySpace.Core;
using NUnit.Framework;

namespace UnitySpace.Core.Tests
{
    [TestFixture]
    public class OrbitalMechanicsTests
    {
        static readonly BodyCatalog Catalog = BodyCatalog.CreateSolSystem();

        [TestCase(0.0)]
        [TestCase(0.0167)]
        [TestCase(0.2056)]
        [TestCase(0.5)]
        [TestCase(0.9)]
        [TestCase(0.97)]
        public void KeplerSolverSatisfiesKeplersEquation(double e)
        {
            for (double m = -Math.PI; m <= Math.PI; m += 0.05)
            {
                double ecc = Kepler.SolveEccentricAnomaly(m, e);
                Assert.That(Math.Abs(ecc - e * Math.Sin(ecc) - m) < 1e-10, Is.True, $"e={e} M={m}");
            }
        }

        [Test]
        public void EarthIsAtPerihelionDistanceAtJ2000()
        {
            // Perihelion was 2000-01-03; on Jan 1.5 Earth was 0.98333 AU from the Sun.
            var eph = new Ephemeris(Catalog);
            eph.Update(Astro.J2000);
            double au = eph.Helio(Catalog["Earth"].Index).Length / Astro.AuKm;
            Assert.That(au, Is.InRange(0.982, 0.9845));
        }

        [Test]
        public void EarthHeliocentricLongitudeAtJ2000()
        {
            // The Sun's geocentric ecliptic longitude at J2000 is 280.46°, so Earth's heliocentric one is ~100.46°.
            var eph = new Ephemeris(Catalog);
            eph.Update(Astro.J2000);
            var earth = eph.Helio(Catalog["Earth"].Index);
            double lon = Astro.WrapDegrees360(Math.Atan2(earth.Y, earth.X) * Astro.Rad2Deg);
            Assert.That(lon, Is.InRange(99.9, 101.0));
        }

        [TestCase("Mercury", 87.97)]
        [TestCase("Mars", 686.98)]
        [TestCase("Jupiter", 4332.6)]
        [TestCase("Neptune", 60190)]
        public void PlanetPeriodsMatchObserved(string name, double days)
        {
            var k = Catalog[name].Elements.At(Astro.J2000);
            Assert.That(k.PeriodDays, Is.InRange(days * 0.995, days * 1.005));
        }

        [Test]
        public void CeresPeriodComesFromKeplersThirdLaw()
        {
            var k = Catalog["Ceres"].Elements.At(Astro.J2000);
            Assert.That(k.PeriodDays / 365.25, Is.InRange(4.59, 4.62));
        }

        [Test]
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
            Assert.That(min, Is.InRange(356000, 370000));
            Assert.That(max, Is.InRange(400000, 407500));
        }

        [Test]
        public void EarthIsOffsetFromTheBarycentreByTheMoon()
        {
            var eph = new Ephemeris(Catalog);
            eph.Update(Astro.J2000 + 100);
            var earth = Catalog["Earth"];
            var moon = Catalog["Moon"];
            var emb = Kepler.Position(earth.Elements.At(Astro.J2000 + 100));
            double offset = (eph.Helio(earth.Index) - emb).Length;
            Assert.That(offset, Is.InRange(4300, 5000));
            Assert.That(DVec3.Dot(eph.Helio(earth.Index) - emb, eph.Relative(moon.Index)) < 0, Is.True, "Earth sits on the far side of the barycentre from the Moon");
        }

        [TestCase("Titania", "Uranus", true)]
        [TestCase("Charon", "Pluto", true)]
        [TestCase("Io", "Jupiter", true)]
        [TestCase("Triton", "Neptune", false)]
        public void MoonsOrbitInTheirParentsSpinSense(string moonName, string parentName, bool prograde)
        {
            var eph = new Ephemeris(Catalog);
            eph.Update(Astro.J2000 + 3);
            int moon = Catalog[moonName].Index;
            var h = DVec3.Cross(eph.Relative(moon), eph.RelativeVelocityKmS(moon));
            double sense = DVec3.Dot(h.Normalized, Catalog[parentName].SpinAxis);
            Assert.That(sense > 0, Is.EqualTo(prograde));
        }

        // JPL Horizons, 2026-10-11 00:00 TDB (JD 2461324.5): moon relative to its parent's centre,
        // km, ecliptic J2000. Miranda gets a looser bound (see BodyCatalog notes).
        [TestCase("Phobos", 6805.860, 5566.134, -2893.088, 0.2)]
        [TestCase("Deimos", -10864.930, 19858.946, 6120.097, 0.5)]
        [TestCase("Io", 312960.209, -281217.598, -5691.267, 0.5)]
        [TestCase("Europa", -646550.487, 158099.391, -8278.278, 0.5)]
        [TestCase("Ganymede", -1072964.415, -17907.116, -16372.860, 0.5)]
        [TestCase("Callisto", 510004.941, 1804866.535, 63394.103, 0.5)]
        [TestCase("Enceladus", 222712.926, 60762.699, -53386.313, 0.5)]
        [TestCase("Titan", -485791.432, -982253.753, 554801.302, 0.5)]
        [TestCase("Miranda", 74985.184, -37264.615, -99478.646, 2.0)]
        [TestCase("Titania", -351233.081, 109719.265, 234883.094, 0.5)]
        [TestCase("Oberon", 348855.606, -9719.491, 466435.960, 0.5)]
        [TestCase("Triton", -76691.585, 222948.357, 265017.955, 0.5)]
        [TestCase("Charon", -13128.476, -14545.454, -435.856, 0.5)]
        public void MoonsMatchJplHorizons(string name, double x, double y, double z, double toleranceDeg)
        {
            var eph = new Ephemeris(Catalog);
            eph.Update(2461324.5);
            var rel = eph.Relative(Catalog[name].Index);
            var truth = new DVec3(x, y, z);
            double deg = Math.Acos(Math.Min(1, DVec3.Dot(rel.Normalized, truth.Normalized))) * Astro.Rad2Deg;
            Assert.That(deg < toleranceDeg, Is.True, $"{name} is {deg:0.00} degrees off");
            Assert.That(rel.Length / truth.Length, Is.InRange(0.97, 1.03));
        }

        // JPL Horizons across 2000-2050 (00:00 TDB, km, ecliptic J2000): the Moon (lunar theory),
        // Phobos (with its tidal decay), Charon and Ceres (refitted mean elements) relative to their
        // parents, and Earth (offset from the Earth-Moon barycentre) relative to the Sun.
        [TestCase("Moon", 2451544.5, -317650.241, -241883.176, 36555.823, 0.03, false)]
        [TestCase("Moon", 2456104.5, -371661.576, 65540.317, -33248.061, 0.03, false)]
        [TestCase("Moon", 2460674.5, -19328.367, -387317.199, -33836.462, 0.03, false)]
        [TestCase("Moon", 2465244.5, 400340.940, 38345.190, -34161.464, 0.03, false)]
        [TestCase("Moon", 2469804.5, 332683.345, -148912.113, 32792.178, 0.03, false)]
        [TestCase("Phobos", 2451544.5, 5117.835, 7379.943, -2165.367, 0.15, false)]
        [TestCase("Phobos", 2456104.5, 4773.303, -7372.110, -3032.423, 0.15, false)]
        [TestCase("Phobos", 2460674.5, 6873.392, -5356.867, -3619.920, 0.15, false)]
        [TestCase("Phobos", 2465244.5, 8185.336, -1586.928, -4226.086, 0.15, false)]
        [TestCase("Phobos", 2469804.5, -4011.797, -8445.124, 1655.631, 0.15, false)]
        [TestCase("Charon", 2451544.5, -40.849, -10402.847, -16608.304, 0.02, false)]
        [TestCase("Charon", 2456104.5, 6470.619, -4199.366, -18013.838, 0.02, false)]
        [TestCase("Charon", 2460674.5, -7194.002, 3351.604, 17916.635, 0.02, false)]
        [TestCase("Charon", 2465244.5, 7878.700, -2512.509, -17765.178, 0.02, false)]
        [TestCase("Charon", 2469804.5, 12475.396, 4586.106, -14396.705, 0.02, false)]
        [TestCase("Ceres", 2451544.5, -355673469.309, 119794569.466, 69239521.440, 0.6, true)]
        [TestCase("Ceres", 2456104.5, 274750752.315, 314595523.610, -40768706.267, 0.6, true)]
        [TestCase("Ceres", 2460664.5, 313816028.064, -308005538.158, -67560074.267, 0.6, true)]
        [TestCase("Ceres", 2465224.5, -313671134.688, -239529611.734, 49949543.108, 0.6, true)]
        [TestCase("Ceres", 2469784.5, -56786741.453, 388683211.654, 23233828.581, 0.6, true)]
        [TestCase("Psyche", 2461406.5, -419692865.428, 218107845.359, 1156093.260, 0.5, true)]
        [TestCase("Psyche", 2461770.5, -432902033.049, -241190267.026, 23076037.101, 0.5, true)]
        [TestCase("Lutetia", 2461406.5, 232389684.896, -197008364.646, -13961309.235, 0.5, true)]
        [TestCase("Lutetia", 2461770.5, 88925789.919, 359733115.928, -1631888.375, 0.5, true)]
        [TestCase("Ida", 2461406.5, -427129036.679, 47938329.890, -4247088.140, 0.5, true)]
        [TestCase("Ida", 2461770.5, -198759377.057, -400562066.359, -8682050.608, 0.5, true)]
        [TestCase("Earth", 2451544.5, -25210928.559, 144927919.584, -616.417, 0.02, true)]
        [TestCase("Earth", 2456104.5, 12278188.983, -151573991.820, 4933.524, 0.02, true)]
        [TestCase("Earth", 2460664.5, 4515457.361, 147108059.018, -8574.767, 0.02, true)]
        [TestCase("Earth", 2465224.5, -16566426.690, -151045153.194, 13079.422, 0.02, true)]
        [TestCase("Earth", 2469784.5, 34051721.122, 143364821.819, -16519.417, 0.02, true)]
        public void BodiesStayOnHorizonsAcrossTheCentury(string name, double jd, double x, double y, double z, double toleranceDeg, bool helio)
        {
            var eph = new Ephemeris(Catalog);
            eph.Update(jd);
            int i = Catalog[name].Index;
            var model = helio ? eph.Helio(i) : eph.Relative(i);
            var truth = new DVec3(x, y, z);
            double deg = Math.Acos(Math.Min(1, DVec3.Dot(model.Normalized, truth.Normalized))) * Astro.Rad2Deg;
            Assert.That(deg < toleranceDeg, Is.True, $"{name} is {deg:0.000} degrees off at JD {jd}");
            Assert.That(model.Length / truth.Length, Is.InRange(0.995, 1.005));
            Assert.That((eph.HelioAt(i, jd) - eph.Helio(i)).Length < 1e-3, Is.True, "look-ahead position disagrees with the frame");
        }

        [Test]
        public void HelioAtMatchesCachedFrame()
        {
            var eph = new Ephemeris(Catalog);
            double jd = Astro.J2000 + 9000.3;
            eph.Update(jd);
            for (int i = 0; i < Catalog.Count; i++)
                Assert.That((eph.HelioAt(i, jd) - eph.Helio(i)).Length < 1e-3, Is.True, Catalog[i].Name);
        }

        [Test]
        public void OrbitalSpeedOfEarthIsAbout30KmPerSecond()
        {
            var eph = new Ephemeris(Catalog);
            eph.Update(Astro.J2000);
            Assert.That(eph.OrbitalSpeedKmS(Catalog["Earth"].Index), Is.InRange(29.3, 30.5));
        }

        [Test]
        public void JulianDateRoundTrips()
        {
            var t = new DateTime(2026, 10, 10, 18, 30, 0, DateTimeKind.Utc);
            double jd = Astro.JulianDate(t);
            Assert.That(jd, Is.EqualTo(2461324.270833).Within(1e-5));
            Assert.That(Math.Abs((Astro.ToDateTime(jd) - t).TotalSeconds) < 0.01, Is.True);
        }
    }
}
