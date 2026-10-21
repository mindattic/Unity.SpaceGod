using System;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    [TestFixture]
    public class ScaleModelTests
    {
        static readonly BodyCatalog Catalog = BodyCatalog.CreateSolSystem();

        [Test]
        public void RealisticIsOneLinearScale()
        {
            var scale = new ScaleModel { Blend = 0 };
            foreach (var b in Catalog.Bodies)
            {
                Assert.That(scale.DisplayRadius(b), Is.EqualTo(b.RadiusKm / 1000.0).Within(1e-9));
                Assert.That(scale.LocalDistance(b, 123456.0), Is.EqualTo(123456.0 / 1000.0).Within(1e-6));
            }
        }

        [TestCase(0.0)]
        [TestCase(0.37)]
        [TestCase(1.0)]
        public void SurfaceMapsToDisplayedSurfaceAtEveryBlend(double blend)
        {
            var scale = new ScaleModel { Blend = blend };
            foreach (var b in Catalog.Bodies)
            {
                double surface = scale.LocalDistance(b, b.RadiusKm);
                Assert.That(Math.Abs(surface - scale.DisplayRadius(b)) / scale.DisplayRadius(b) < 1e-9, Is.True, b.Name);
            }
        }

        [TestCase(0.0)]
        [TestCase(0.5)]
        [TestCase(1.0)]
        public void LocalDistanceIsMonotonicAndInvertible(double blend)
        {
            var scale = new ScaleModel { Blend = blend };
            foreach (var b in Catalog.Bodies)
            {
                double previous = 0;
                for (double d = b.RadiusKm * 0.5; d < 1e11; d *= 1.7)
                {
                    double s = scale.LocalDistance(b, d);
                    Assert.That(s > previous, Is.True, $"{b.Name} at {d}");
                    previous = s;
                    double back = scale.InverseLocalDistance(b, s);
                    Assert.That(Math.Abs(back - d) / d < 1e-6, Is.True, $"{b.Name} inverse at {d}");
                }
            }
        }

        [Test]
        public void GameplayMoonsClearTheirParentsAndEachOther()
        {
            var scale = new ScaleModel { Blend = 1 };
            foreach (var moon in Catalog.Bodies)
            {
                if (moon.Kind != BodyKind.Moon) continue;
                var parent = Catalog[moon.ParentIndex];
                var k = moon.Elements.At(Astro.J2000);
                double periapsis = scale.LocalDistance(parent, k.A * (1 - k.E));
                Assert.That(periapsis - scale.DisplayRadius(moon) > scale.DisplayRadius(parent) * 1.2, Is.True, moon.Name);
            }
        }

        [Test]
        public void GameplayPlanetOrbitsDoNotOverlap()
        {
            var scale = new ScaleModel { Blend = 1 };
            string[] order = { "Mercury", "Venus", "Earth", "Mars", "Ceres", "Jupiter", "Saturn", "Uranus", "Neptune" };
            double previousOuter = scale.DisplayRadius(Catalog.Sun);
            foreach (var name in order)
            {
                var b = Catalog[name];
                var k = b.Elements.At(Astro.J2000);
                double inner = scale.LocalDistance(Catalog.Sun, k.A * (1 - k.E)) - scale.DisplayRadius(b);
                double outer = scale.LocalDistance(Catalog.Sun, k.A * (1 + k.E)) + scale.DisplayRadius(b);
                Assert.That(inner > previousOuter, Is.True, $"{name} overlaps the previous orbit");
                previousOuter = outer;
            }
        }

        [Test]
        public void LocalOffsetRoundTripsAndKeepsDirection()
        {
            var scale = new ScaleModel { Blend = 0.6 };
            var earth = Catalog["Earth"];
            var offset = new DVec3(30000, -12000, 8000);
            var scene = scale.LocalOffset(earth, offset);
            Assert.That(Math.Abs(DVec3.Dot(scene.Normalized, offset.SwapYZ.Normalized) - 1) < 1e-12, Is.True);
            var back = scale.InverseLocalOffset(earth, scene);
            Assert.That((back - offset).Length < 1e-3, Is.True);
        }

        [Test]
        public void GameplayCompressesTheOuterSystemDramatically()
        {
            var real = new ScaleModel { Blend = 0 };
            var game = new ScaleModel { Blend = 1 };
            double neptune = 30.07 * Astro.AuKm;
            Assert.That(real.LocalDistance(Catalog.Sun, neptune) / game.LocalDistance(Catalog.Sun, neptune) > 20, Is.True);
            Assert.That(game.DisplayRadius(Catalog["Earth"]) / real.DisplayRadius(Catalog["Earth"]) > 30, Is.True);
        }
    }
}
