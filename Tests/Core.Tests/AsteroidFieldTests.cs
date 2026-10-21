using System;
using System.IO;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    [TestFixture]
    public class AsteroidFieldTests
    {
        static readonly Lazy<AsteroidField> Field = new Lazy<AsteroidField>(() => AsteroidField.Parse(File.ReadAllBytes(DataPath())));

        static string DataPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var p = Path.Combine(dir.FullName, "Assets", "UnitySpace", "Resources", "Data", "asteroids.bytes");
                if (File.Exists(p)) return p;
                dir = dir.Parent;
            }
            throw new FileNotFoundException("asteroids.bytes not found above " + AppContext.BaseDirectory);
        }

        [Test]
        public void LoadsTheWholeCatalogueBrightestFirst()
        {
            var f = Field.Value;
            Assert.That(f.Count > 110000, Is.True, $"count {f.Count}");
            Assert.That(f.EpochJd, Is.EqualTo(2461200.5));
            Assert.That(f.IndexOfNumber(1), Is.EqualTo(-1)); // Ceres is a catalogue body, not part of the field
            for (int i = 1; i < f.Count; i++) Assert.That(f.H[i] >= f.H[i - 1], Is.True);
            Assert.That(f.Group.Count(g => g == 1) > 10000, Is.True, "Jupiter Trojans");
            Assert.That(f.H.Max() < 15.5f, Is.True);
        }

        // JPL Horizons heliocentric ecliptic J2000 vectors (AU), TDB.
        [TestCase(4, 2460676.5, -2.250582048106207, -0.1756708258072572, 0.2792640768981432, 0.02)]
        [TestCase(4, 2461200.5, 2.269028147398455, -0.5440517025183874, -0.2601452414717795, 1e-5)]
        [TestCase(4, 2461771.5, -1.365102708786166, 2.081026133722101, 0.1044633876987572, 0.02)]
        [TestCase(2, 2460676.5, 0.5975712077927406, -2.706753523684491, 1.823787653783709, 0.02)]
        [TestCase(2, 2461200.5, 3.050646479459978, -0.2813127239735989, -0.06888493781935071, 1e-5)]
        [TestCase(2, 2461771.5, -1.907905166527341, 0.9244043548062137, -0.4757384319000333, 0.02)]
        [TestCase(624, 2460676.5, -4.270956709465083, 2.844522827440896, 0.4768431351966363, 0.03)]
        [TestCase(624, 2461200.5, -5.044328953969506, -0.8021940405268528, -0.7400262379720891, 1e-5)]
        [TestCase(624, 2461771.5, -2.692622679814912, -4.206394870926072, -1.577932517733574, 0.03)]
        public void MatchesHorizons(int number, double jd, double x, double y, double z, double toleranceAu)
        {
            var f = Field.Value;
            int i = f.IndexOfNumber(number);
            Assert.That(i >= 0, Is.True);
            var p = f.PositionAu(i, jd);
            double err = (p - new DVec3(x, y, z)).Length;
            Assert.That(err < toleranceAu, Is.True, $"#{number} at {jd}: {err:G4} AU");
        }

        [Test]
        public void BatchAndGpuPathsAgreeWithDoublePrecision()
        {
            var f = Field.Value;
            const int n = 4000;
            double jd = f.EpochJd + 3652.5;
            var xyz = new float[f.Count * 3];
            f.PositionsAu(jd, 0, n, xyz);
            var packed = new float[f.Count * AsteroidField.GpuFloatsPerAsteroid];
            f.PackGpu(packed);
            double worstBatch = 0, worstGpu = 0;
            for (int i = 0; i < n; i++)
            {
                var p = f.PositionAu(i, jd);
                worstBatch = Math.Max(worstBatch, (p - new DVec3(xyz[i * 3], xyz[i * 3 + 1], xyz[i * 3 + 2])).Length);
                AsteroidField.GpuPosition(packed, i, (float)(jd - f.EpochJd), out var gx, out var gy, out var gz);
                worstGpu = Math.Max(worstGpu, (p - new DVec3(gx, gy, gz)).Length);
            }
            Assert.That(worstBatch < 1e-5, Is.True, $"batch {worstBatch:G3}");
            Assert.That(worstGpu < 2e-4, Is.True, $"gpu {worstGpu:G3}");
        }

        [TestCase(2.502, 2.455, 0.25)] // 3:1 resonance with Jupiter
        [TestCase(2.825, 2.78, 0.25)] // 5:2
        [TestCase(2.958, 3.02, 0.5)] // 7:3, a narrower, shallower gap
        public void KirkwoodGapsComeFromTheData(double gapAu, double neighbourAu, double maxRatio)
        {
            var f = Field.Value;
            const double half = 0.006;
            int inGap = 0, beside = 0;
            for (int i = 0; i < f.Count; i++)
            {
                if (f.Group[i] != 0) continue;
                if (Math.Abs(f.A[i] - gapAu) < half) inGap++;
                if (Math.Abs(f.A[i] - neighbourAu) < half) beside++;
            }
            Assert.That(inGap < beside * maxRatio, Is.True, $"gap {inGap} vs neighbour {beside}");
        }

        [Test]
        public void TrojansLeadAndTrailJupiterBySixtyDegrees()
        {
            var f = Field.Value;
            var catalog = BodyCatalog.CreateSolSystem();
            var eph = new Ephemeris(catalog);
            int jupiter = catalog["Jupiter"].Index;
            var j = eph.HelioAt(jupiter, f.EpochJd);
            double jLon = Math.Atan2(j.Y, j.X) * Astro.Rad2Deg;
            int total = 0, near = 0, l4 = 0;
            for (int i = 0; i < f.Count; i++)
            {
                if (f.Group[i] != 1) continue;
                total++;
                var p = f.PositionAu(i, f.EpochJd);
                double d = Astro.WrapDegrees360(Math.Atan2(p.Y, p.X) * Astro.Rad2Deg - jLon + 180) - 180;
                if (Math.Abs(Math.Abs(d) - 60) < 35) near++;
                if (d > 0) l4++;
            }
            Assert.That(near > total * 0.9, Is.True, $"{near}/{total} near L4/L5");
            Assert.That(l4, Is.InRange(total / 4, total * 3 / 4)); // both clouds populated
        }

        [TestCase(0.0)]
        [TestCase(0.37)]
        [TestCase(1.0)]
        public void ShaderSunMappingMatchesScaleModel(double blend)
        {
            var catalog = BodyCatalog.CreateSolSystem();
            var scale = new ScaleModel { Blend = blend };
            AsteroidField.SunMapping(scale, catalog.Sun, out var c0, out var c1);
            foreach (double au in new[] { 1.8, 2.7, 3.3, 5.2 })
            {
                double expect = scale.LocalDistance(catalog.Sun, au * Astro.AuKm);
                double got = AsteroidField.SceneDistanceFromSun(c0, c1, (float)au);
                Assert.That(Math.Abs(got - expect) / expect < 1e-4, Is.True, $"blend {blend}, {au} AU: {got} vs {expect}");
            }
        }
    }
}
