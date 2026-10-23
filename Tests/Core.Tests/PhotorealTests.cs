using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The pipeline-neutral half of the HDRP photoreal path (U.SPC-T430): which planet's atmosphere the physically based
    /// sky draws, how its air is described from the planet's own atmosphere colour and strength, the fog at the camera,
    /// and the photoreal choices' defaults.
    /// </summary>
    [TestFixture]
    public class PhotorealTests
    {
        static readonly Rgb EarthAir = new Rgb(0.4f, 0.65f, 1f);
        const float EarthStrength = 0.65f;

        [Test]
        public void EarthsAirComesOutAsHdrpsEarth()
        {
            var shell = PlanetAtmospheres.Describe(3, 6.371, 6.371 * 3, EarthAir.R, EarthAir.G, EarthAir.B, EarthStrength);
            // HDRP's Earth defaults: zenith opacity 0.045 (red), 0.102 (green), 0.233 (blue).
            Assert.That(shell.AirB, Is.EqualTo(0.233).Within(0.005));
            Assert.That(shell.AirG, Is.EqualTo(0.102).Within(0.01));
            Assert.That(shell.AirR, Is.EqualTo(0.045).Within(0.01));
            Assert.That(shell.Body, Is.EqualTo(3));
            Assert.That(shell.Radius, Is.EqualTo(6.371));
            Assert.That(shell.Thickness, Is.EqualTo(6.371 * PlanetAtmospheres.ThicknessFraction(EarthStrength)).Within(1e-12));
            Assert.That(shell.HazeThickness, Is.LessThan(shell.Thickness));
            Assert.That(shell.CameraInside, Is.False, "two radii up is above the air");
            Assert.That(double.IsPositiveInfinity(shell.FogMeanFreePath), Is.True, "no fog above the air");
        }

        [Test]
        public void TheAirTakesThePlanetsTintAndStrength()
        {
            var mars = PlanetAtmospheres.Describe(0, 3.4, 5, 0.95f, 0.6f, 0.4f, 0.2f);
            Assert.That(mars.AirR, Is.GreaterThan(mars.AirB), "a red sky scatters red most");
            var thin = PlanetAtmospheres.Describe(0, 1, 2, 0.4f, 0.65f, 1f, 0.2f);
            var thick = PlanetAtmospheres.Describe(0, 1, 2, 0.4f, 0.65f, 1f, 0.9f);
            Assert.That(thick.AirB, Is.GreaterThan(thin.AirB));
            Assert.That(thick.Thickness, Is.GreaterThan(thin.Thickness));
            Assert.That(PlanetAtmospheres.Describe(0, 1, 2, 0.4f, 0.65f, 1f, 0f).IsNone, Is.True, "no air, no sky");
            Assert.That(PlanetAtmospheres.Describe(-1, 1, 2, 0.4f, 0.65f, 1f, 0.5f).IsNone, Is.True);
        }

        [Test]
        public void TheFogThickensTowardTheSurface()
        {
            double r = 10;
            double top = r * PlanetAtmospheres.ThicknessFraction(EarthStrength);
            var low = PlanetAtmospheres.Describe(0, r, r + top * 0.05, EarthAir.R, EarthAir.G, EarthAir.B, EarthStrength);
            var high = PlanetAtmospheres.Describe(0, r, r + top * 0.8, EarthAir.R, EarthAir.G, EarthAir.B, EarthStrength);
            Assert.That(low.CameraInside && high.CameraInside, Is.True);
            Assert.That(low.FogMeanFreePath, Is.LessThan(high.FogMeanFreePath));
            Assert.That(low.FogMeanFreePath, Is.GreaterThan(0));
        }

        [Test]
        public void TheSkyFollowsTheNearestAtmosphereWithinReach()
        {
            Assert.That(PlanetAtmospheres.Closeness(20, 10, 0.5f, false), Is.EqualTo(1.0), "one radius up");
            Assert.That(PlanetAtmospheres.Closeness(5, 10, 0.5f, false), Is.EqualTo(0.0), "inside counts as at the surface");
            Assert.That(double.IsPositiveInfinity(PlanetAtmospheres.Closeness(10 * (2 + PlanetAtmospheres.ReachRadii), 10, 0.5f, false)), Is.True, "too far");
            Assert.That(double.IsPositiveInfinity(PlanetAtmospheres.Closeness(20, 10, 0f, false)), Is.True, "an airless moon has no sky");
            Assert.That(double.IsPositiveInfinity(PlanetAtmospheres.Closeness(20, 10, 0.5f, true)), Is.True, "a star is not a sky");
            // A small world close by beats a big one far off, measured in their own sizes.
            Assert.That(PlanetAtmospheres.Closeness(1.5, 1, 0.5f, false), Is.LessThan(PlanetAtmospheres.Closeness(30, 10, 0.5f, false)));
        }

        [Test]
        public void ANewProfileGetsTheSkyVolumetricsAndFixedSunlightButNotRayTracing()
        {
            var o = PhotorealOptions.Default;
            Assert.That(o.PhysicalSky, Is.True);
            Assert.That(o.Volumetrics, Is.True);
            Assert.That(o.Exposure, Is.EqualTo(SunExposure.Fixed));
            Assert.That(o.RayTracing, Is.False, "ray tracing costs too much to be on by default");
            Assert.That(o.SunBrightness, Is.GreaterThan(1));
            Assert.That(o.MeteredEv100(15), Is.EqualTo(15));
            o.ExposureCompensation = 1;
            Assert.That(o.MeteredEv100(15), Is.EqualTo(14), "a stop brighter is one EV lower");
            Assert.That(o, Is.Not.EqualTo(PhotorealOptions.Default));
            var fresh = PhotorealOptions.Default;
            Assert.That(fresh.Equals(PhotorealOptions.Default) && fresh.GetHashCode() == PhotorealOptions.Default.GetHashCode(), Is.True);
        }
    }
}
