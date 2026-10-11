using System;
using SolarSystem.Core;
using Xunit;

namespace SolarSystem.Core.Tests
{
    public class SurfaceAndWeatherTests
    {
        static readonly BodyCatalog Catalog = BodyCatalog.CreateSolSystem();

        static double RaOf(DVec3 ecliptic)
        {
            double eps = Astro.ObliquityDeg * Astro.Deg2Rad;
            double y = ecliptic.Y * Math.Cos(eps) - ecliptic.Z * Math.Sin(eps);
            return Astro.WrapDegrees360(Math.Atan2(y, ecliptic.X) * Astro.Rad2Deg);
        }

        [Theory]
        [InlineData(2451545.0)]
        [InlineData(2460000.5)]
        [InlineData(2461324.75)]
        public void GreenwichFollowsSiderealTime(double jd)
        {
            var earth = Catalog["Earth"];
            BodyOrientation.Frame(earth, jd, DVec3.Zero, out var north, out var meridian);
            double gmst = Astro.WrapDegrees360(280.46061837 + 360.98564736629 * (jd - Astro.J2000));
            double diff = Astro.WrapDegrees360(RaOf(meridian) - gmst + 180) - 180;
            // IAU W is measured in the fixed J2000 frame, GMST from the equinox of date: they part by
            // the 0.3 deg of W0's definition plus precession (0.014 deg/yr).
            Assert.True(Math.Abs(diff) < 0.8, $"Greenwich RA off GMST by {diff:F3} deg");
            Assert.True(DVec3.Dot(north, Astro.PoleFromRaDec(0, 90)) > 0.9999);
        }

        [Fact]
        public void SynchronousMoonsFaceTheirPlanet()
        {
            var eph = new Ephemeris(Catalog);
            double jd = 2461324.75;
            eph.Update(jd);
            foreach (var name in new[] { "Moon", "Io", "Europa", "Titan", "Miranda", "Triton", "Charon" })
            {
                var b = Catalog[name];
                var toParent = -eph.Relative(b.Index).Normalized;
                var normal = DVec3.Cross(eph.Relative(b.Index), eph.RelativeVelocityKmS(b.Index));
                BodyOrientation.Frame(b, jd, toParent, normal, out var north, out var meridian);
                BodyOrientation.LonLat(north, meridian, toParent, out var lon, out var lat);
                Assert.True(Math.Abs(lon) < 1e-6, $"{name} sub-planet longitude {lon}");
                Assert.True(Math.Abs(lat) < 7, $"{name} sub-planet latitude {lat}");
            }
        }

        [Theory]
        [InlineData(2457218.5)] // New Horizons flyby, 2015-07-14
        [InlineData(2461324.75)]
        public void PlutoKeepsItsZeroMeridianOnCharon(double jd)
        {
            // Pluto's IAU prime meridian is defined to face Charon (sub-Charon longitude 0).
            // This holds only if the pole, its sense and W all agree with the orbit of Charon.
            var eph = new Ephemeris(Catalog);
            eph.Update(jd);
            var pluto = Catalog["Pluto"];
            var toCharon = eph.Relative(Catalog["Charon"].Index).Normalized;
            BodyOrientation.Frame(pluto, jd, DVec3.Zero, out var north, out var meridian);
            BodyOrientation.LonLat(north, meridian, toCharon, out var lon, out var lat);
            Assert.True(Math.Abs(lon) < 5, $"sub-Charon longitude {lon:F2}");
            Assert.True(Math.Abs(lat) < 2, $"sub-Charon latitude {lat:F2}");
        }

        [Fact]
        public void MapNorthFollowsTheMapConventions()
        {
            // New Horizons maps: north is Pluto's positive (spin) pole, which leaned toward the Sun in 2015.
            Assert.True(DVec3.Dot(BodyOrientation.MapNorth(Catalog["Pluto"]), Catalog["Pluto"].SpinAxis) > 0.999);
            // Uranian satellite maps: north is Uranus's IAU north, opposite its spin.
            Assert.True(DVec3.Dot(BodyOrientation.MapNorth(Catalog["Miranda"]), Catalog["Uranus"].Pole) > 0.99);
            Assert.True(BodyOrientation.RateDegPerDay(Catalog["Miranda"]) < 0);
            Assert.True(BodyOrientation.RateDegPerDay(Catalog["Venus"]) < 0);
            Assert.True(BodyOrientation.RateDegPerDay(Catalog["Pluto"]) > 0);
        }

        [Fact]
        public void GreatRedSpotFollowsTheObservedDrift()
        {
            var grs = typeof(GasGiantWeather.GreatRedSpot);
            Assert.NotNull(grs);
            Assert.Equal(292.0, GasGiantWeather.GreatRedSpot.SystemIIIWestDeg(GasGiantWeather.GreatRedSpot.AnchorJd), 6);

            // Back out System II and compare with the JUPOS track (projectpluto grs_lon.txt).
            static double SystemII(double jd) => Astro.WrapDegrees360(
                GasGiantWeather.GreatRedSpot.SystemIIIWestDeg(jd) - (292.0 - 77.0)
                - GasGiantWeather.GreatRedSpot.SystemIIIMinusIIDegPerDay * (jd - GasGiantWeather.GreatRedSpot.AnchorJd));
            Assert.InRange(SystemII(2460310.5), 47, 53);   // 2024-01-01: 410 = 50
            Assert.InRange(SystemII(2461010.5), 76, 82);   // 2025-12-01: 439 = 79
            Assert.InRange(SystemII(2460584.5), 58, 64);   // 2024-10-01: 421 = 61

            // Map shift is zero when the spot is where the Hubble map shows it.
            double t = GasGiantWeather.GreatRedSpot.AnchorJd +
                       (GasGiantWeather.GreatRedSpot.MapSystemIIIWestDeg - 292.0 + 360) / GasGiantWeather.GreatRedSpot.DriftDegPerDay;
            Assert.True(Math.Abs(GasGiantWeather.GreatRedSpot.MapShiftDeg(t)) < 1e-6);
            Assert.True(Math.Abs(GasGiantWeather.GreatRedSpot.EastLongitudeDeg(t) + 125.9) < 1e-6);
        }

        [Fact]
        public void JupiterWindsCirculateAnticyclonicallyAroundTheSpot()
        {
            var jupiter = Catalog["Jupiter"];
            var p = GasGiantWeather.ProfileFor("Jupiter");
            Assert.InRange(GasGiantWeather.WindMs(p, 0), 80, 120);
            Assert.InRange(GasGiantWeather.WindMs(p, 23.7), 150, 180);
            // North edge of the spot runs west, south edge east: counter-clockwise, an anticyclone in the south.
            Assert.True(GasGiantWeather.WindMs(p, -19.5) < -40);
            Assert.True(GasGiantWeather.WindMs(p, -26.5) > 30);
            // 165 m/s along the 402,000 km circle of latitude at 23.7 N is 12.8 deg/day.
            Assert.InRange(GasGiantWeather.DriftDegPerDay(jupiter, p, 23.7), 12, 13.5);
        }

        [Fact]
        public void RetrogradeRotatorsReverseTheirDrift()
        {
            var uranus = Catalog["Uranus"];
            var p = GasGiantWeather.ProfileFor("Uranus");
            // Prograde 250 m/s at 60 deg follows Uranus's backwards spin: map longitude decreases.
            Assert.True(GasGiantWeather.DriftDegPerDay(uranus, p, 60) < 0);
            var neptune = Catalog["Neptune"];
            Assert.True(GasGiantWeather.DriftDegPerDay(neptune, GasGiantWeather.ProfileFor("Neptune"), 0) < -50);
        }
    }
}
