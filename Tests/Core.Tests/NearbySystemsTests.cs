using System;
using System.Collections.Generic;
using System.Linq;
using SolarSystem.Core;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    /// <summary>The catalogue-built star systems keep the catalogue's orbits, stars and discs (Systems/SOURCES.md).</summary>
    [TestFixture]
    public class NearbySystemsTests
    {
        const double StartJd = 2461324.0;

        static IEnumerable<SystemSpec> AllSpecs() => new[] { AlphaCentauri.Spec() }.Concat(NearbySystems.All());

        static IEnumerable<TestCaseData> Systems() =>
            AllSpecs().Select(s => new TestCaseData(s.Name).SetName("{m}(" + s.Name + ")"));

        static SystemSpec SpecNamed(string name) => AllSpecs().First(s => s.Name == name);

        static Ephemeris Build(SystemSpec spec, out StarSystemBuilder.Result built)
        {
            built = StarSystemBuilder.Build(spec);
            var eph = new Ephemeris(new BodyCatalog(built.Bodies));
            eph.Update(Astro.J2000);
            return eph;
        }

        [TestCaseSource(nameof(Systems))]
        public void EveryPlanetKeepsTheCataloguedPeriodAxisAndEccentricity(string system)
        {
            var spec = SpecNamed(system);
            var eph = Build(spec, out _);
            var c = eph.Catalog;
            var samples = new DVec3[1024];
            foreach (var p in spec.Planets)
            {
                var b = c[p.Name];
                Assert.That(b.Kind, Is.EqualTo(BodyKind.Planet), p.Name);
                Assert.That(c[b.ParentIndex].Name, Is.EqualTo(p.Host), p.Name);
                Assert.That(b.Elements.AKm, Is.EqualTo(p.AAu * Astro.AuKm).Within(1e-9).Percent, p.Name);
                Assert.That(b.Elements.E, Is.EqualTo(p.E), p.Name);
                Assert.That(b.Elements.At(Astro.J2000).PeriodDays, Is.EqualTo(p.PeriodDays).Within(1e-9).Percent, p.Name);
                Assert.That(b.Gm, Is.EqualTo(p.MassEarth * StarSystemBuilder.EarthGm).Within(1e-9).Percent, p.Name);
                Assert.That(b.RadiusKm, Is.EqualTo(p.RadiusEarth * StarSystemBuilder.EarthRadiusKm).Within(1e-9).Percent, p.Name);
                Assert.That(b.Status, Is.EqualTo(p.Status), p.Name);
                Assert.That(b.MassIsMinimum, Is.EqualTo(p.MassIsMinimum), p.Name);

                // The ellipse the ephemeris traces: periapsis a(1-e), apoapsis a(1+e), and it closes after one period.
                for (int k = 0; k < samples.Length; k++)
                {
                    double jd = Astro.J2000 + p.PeriodDays * k / samples.Length;
                    samples[k] = eph.HelioAt(b.Index, jd) - eph.HelioAt(b.ParentIndex, jd);
                }
                double min = samples.Min(v => v.Length) / Astro.AuKm, max = samples.Max(v => v.Length) / Astro.AuKm;
                Assert.That(min, Is.EqualTo(p.AAu * (1 - p.E)).Within(0.2).Percent, p.Name + " periapsis");
                Assert.That(max, Is.EqualTo(p.AAu * (1 + p.E)).Within(0.2).Percent, p.Name + " apoapsis");
                var start = eph.HelioAt(b.Index, StartJd) - eph.HelioAt(b.ParentIndex, StartJd);
                var later = eph.HelioAt(b.Index, StartJd + p.PeriodDays) - eph.HelioAt(b.ParentIndex, StartJd + p.PeriodDays);
                Assert.That((later - start).Length, Is.LessThan(1e-6 * start.Length), p.Name + " does not close in one period");
            }
        }

        [TestCaseSource(nameof(Systems))]
        public void EveryStarHasItsCataloguedMassSizeAndLight(string system)
        {
            var spec = SpecNamed(system);
            var c = Build(spec, out _).Catalog;
            foreach (var s in spec.Stars())
            {
                var b = c[s.Name];
                Assert.That(b.IsStar, Is.True, s.Name);
                Assert.That(b.Gm, Is.EqualTo(s.MassSolar * StarSystemBuilder.SunGm).Within(1e-9).Percent, s.Name);
                Assert.That(b.RadiusKm, Is.EqualTo(s.RadiusSolar * StarSystemBuilder.SunRadiusKm).Within(1e-9).Percent, s.Name);
                Assert.That(b.LuminositySolar, Is.EqualTo(s.LuminositySolar), s.Name);
                Assert.That(b.TeffK, Is.EqualTo(s.TeffK), s.Name);
                Assert.That(b.SpectralType, Is.EqualTo(s.Spectral), s.Name);
            }
            // The root is the primary star, or the barycentre of a primary pair.
            Assert.That(c.Root.Name, Is.EqualTo(spec.Primary.IsPair ? spec.Primary.Name : spec.Primary.Star.Name));
        }

        static IEnumerable<TestCaseData> Pairs()
        {
            foreach (var spec in AllSpecs())
            {
                if (spec.Primary.IsPair) yield return new TestCaseData(spec.Name, spec.Primary.Name).SetName("{m}(" + spec.Primary.Name + ")");
                foreach (var c in spec.Companions)
                    if (c.Node.IsPair) yield return new TestCaseData(spec.Name, c.Node.Name).SetName("{m}(" + c.Node.Name + ")");
            }
        }

        [TestCaseSource(nameof(Pairs))]
        public void BinariesCircleTheirBarycentreOnTheMeasuredVisualOrbit(string system, string pair)
        {
            var spec = SpecNamed(system);
            var node = spec.Primary.IsPair && spec.Primary.Name == pair ? spec.Primary : spec.Companions.First(x => x.Node.Name == pair).Node;
            var o = node.Orbit;
            var eph = Build(spec, out _);
            var c = eph.Catalog;
            int bary = c[pair].Index, a = c[node.A.Name].Index, b = c[node.B.Name].Index;
            Assert.That(c[a].ParentIndex, Is.EqualTo(bary));
            Assert.That(c[b].ParentIndex, Is.EqualTo(bary));

            double period = o.PeriodYears * 365.25, min = double.MaxValue, max = 0;
            for (double jd = StartJd; jd < StartJd + period; jd += period / 4000)
            {
                eph.Update(jd);
                var ra = eph.Relative(a);
                var rb = eph.Relative(b);
                var balance = ra * node.A.MassSolar + rb * node.B.MassSolar;
                Assert.That(balance.Length, Is.LessThan(1e-6 * ra.Length), "the barycentre moved");
                double sep = (rb - ra).Length / Astro.AuKm;
                min = Math.Min(min, sep);
                max = Math.Max(max, sep);
            }
            TestContext.Out.WriteLine($"{pair}: {min:0.000} to {max:0.000} AU apart");
            Assert.That(min, Is.EqualTo(o.AAu * (1 - o.E)).Within(0.1).Percent);
            Assert.That(max, Is.EqualTo(o.AAu * (1 + o.E)).Within(0.1).Percent);

            // Periastron falls on the catalogued date (and every period after it).
            double next = Astro.J2000 + (o.PeriastronYear - 2000) * 365.25;
            while (next < StartJd) next += period;
            eph.Update(next);
            Assert.That((eph.Relative(b) - eph.Relative(a)).Length / Astro.AuKm, Is.EqualTo(o.AAu * (1 - o.E)).Within(1e-6).Percent);

            // Seen from Earth the orbit is tilted by the measured inclination.
            var normal = DVec3.Cross(eph.Relative(b) - eph.Relative(a), eph.RelativeVelocityKmS(b) - eph.RelativeVelocityKmS(a)).Normalized;
            double tilt = Math.Acos(Math.Abs(DVec3.Dot(normal, Astro.DirectionFromRaDec(spec.RaDeg, spec.DecDeg)))) * Astro.Rad2Deg;
            Assert.That(tilt, Is.EqualTo(o.IDeg <= 90 ? o.IDeg : 180 - o.IDeg).Within(0.01));
        }

        [Test]
        public void SiriusBSwingsBetweenEightAndThirtyOneAu()
        {
            // Bond et al. 2017: a = 7.4957" at a parallax of 378.9 mas is 19.78 AU, e = 0.5914.
            var spec = NearbySystems.Sirius();
            Assert.That(spec.Primary.Orbit.AAu, Is.EqualTo(19.78).Within(0.01));
            Assert.That(spec.DistanceLy, Is.EqualTo(8.608).Within(0.001));
            Assert.That(spec.Primary.Orbit.AAu * (1 - spec.Primary.Orbit.E), Is.InRange(8.0, 8.2));
            Assert.That(spec.Primary.Orbit.AAu * (1 + spec.Primary.Orbit.E), Is.InRange(31.4, 31.6));
            // Kepler's third law holds for the catalogued masses, period and axis (to their quoted precision).
            double total = spec.Primary.MassSolar;
            Assert.That(Math.Pow(spec.Primary.Orbit.AAu, 3) / Math.Pow(spec.Primary.Orbit.PeriodYears, 2), Is.EqualTo(total).Within(2).Percent);
            var luhman = NearbySystems.Luhman16().Primary;
            Assert.That(Math.Pow(luhman.Orbit.AAu, 3) / Math.Pow(luhman.Orbit.PeriodYears, 2), Is.EqualTo(luhman.MassSolar).Within(2).Percent);
        }

        [Test]
        public void ThePlanetsMatchTheExoplanetArchive()
        {
            // Spot checks against the NASA Exoplanet Archive's composite table (2026-10-16).
            (string system, string planet, double period, double aAu, double e, double mass)[] archive =
            {
                ("Barnard's Star", "Barnard b", 3.1542, 0.0229, 0.03, 0.299),
                ("Barnard's Star", "Barnard e", 6.7392, 0.0381, 0.04, 0.193),
                ("Lalande 21185", "Lalande 21185 c", 2946, 2.94, 0.132, 13.6),
                ("Ross 128", "Ross 128 b", 9.8658, 0.0496, 0.116, 1.40),
                ("Epsilon Eridani", "Epsilon Eridani b", 2680, 3.53, 0.06, 317.83),
                ("Epsilon Indi", "Epsilon Indi Ab", 25450, 15.76, 0.25, 2065.88),
                ("Tau Ceti", "Tau Ceti f", 636.13, 1.334, 0.16, 3.93),
                ("GJ 1061", "GJ 1061 d", 13.066, 0.054, 0.04, 1.64),
                ("Teegarden's Star", "Teegarden's Star b", 4.90634, 0.0259, 0.03, 1.16),
                ("Luyten's Star", "Luyten b", 18.6498, 0.091101, 0.10, 2.89),
                ("Alpha Centauri", "Proxima b", 11.18465, 0.04848, 0.0, 1.055),
            };
            foreach (var x in archive)
            {
                var p = SpecNamed(x.system).Planets.First(q => q.Name == x.planet);
                Assert.That((p.PeriodDays, p.AAu, p.E, p.MassEarth), Is.EqualTo((x.period, x.aAu, x.e, x.mass)), x.planet);
            }
        }

        [Test]
        public void CandidatesAreLabelledAsSuch()
        {
            var candidates = AllSpecs().SelectMany(s => s.Planets).Where(p => p.Status == BodyStatus.Candidate).Select(p => p.Name).ToList();
            Assert.That(candidates, Is.SupersetOf(new[]
            {
                "Wolf 359 b", "Lalande 21185 d", "Luyten d", "Luyten e", "Alpha Centauri Ab",
            }));
            var confirmed = AllSpecs().SelectMany(s => s.Planets).Where(p => p.Status == BodyStatus.Confirmed).Select(p => p.Name).ToList();
            Assert.That(confirmed, Is.SupersetOf(new[]
            {
                "Barnard b", "Barnard c", "Barnard d", "Barnard e", "Lalande 21185 b", "Lalande 21185 c", "Ross 128 b", "Epsilon Eridani b",
                "Epsilon Indi Ab", "GJ 1061 b", "GJ 1061 c", "GJ 1061 d", "Teegarden's Star b", "Teegarden's Star c", "Teegarden's Star d",
                "Luyten b", "Luyten c", "Proxima b", "Proxima d", "Tau Ceti g", "Tau Ceti h", "Tau Ceti f",
            }));
            // Every candidate says so where the player reads it.
            foreach (var p in AllSpecs().SelectMany(s => s.Planets).Where(p => p.Status != BodyStatus.Confirmed))
                Assert.That(p.Summary, Does.StartWith(p.Status == BodyStatus.Candidate ? "CANDIDATE" : "DISPUTED"), p.Name);
        }

        [Test]
        public void HabitableZoneFlagsFollowEachStarsLight()
        {
            HabitableZone Zone(string system, string planet) =>
                new BodyCatalog(StarSystemBuilder.Build(SpecNamed(system)).Bodies)[planet].HabitableZone;

            Assert.That(Zone("Alpha Centauri", "Proxima b"), Is.EqualTo(HabitableZone.Conservative));
            Assert.That(Zone("Alpha Centauri", "Proxima d"), Is.EqualTo(HabitableZone.None));
            Assert.That(Zone("Teegarden's Star", "Teegarden's Star b"), Is.Not.EqualTo(HabitableZone.None));
            Assert.That(Zone("GJ 1061", "GJ 1061 d"), Is.Not.EqualTo(HabitableZone.None));
            foreach (var p in new[] { "Barnard b", "Barnard c", "Barnard d", "Barnard e" })
                Assert.That(Zone("Barnard's Star", p), Is.EqualTo(HabitableZone.None), p);
            Assert.That(Zone("Epsilon Eridani", "Epsilon Eridani b"), Is.EqualTo(HabitableZone.None));
            Assert.That(Zone("Epsilon Indi", "Epsilon Indi Ab"), Is.EqualTo(HabitableZone.None));

            // The Sun's conservative zone (Kopparapu et al. 2013): about 0.95 to 1.68 AU, with Earth inside it.
            HabitableZones.ConservativeEdgesAu(1, 5780, out double inner, out double outer);
            Assert.That(inner, Is.InRange(0.97, 0.99));
            Assert.That(outer, Is.InRange(1.68, 1.70));
            Assert.That(HabitableZones.Classify(1, 5780, 1, 0.0167), Is.EqualTo(HabitableZone.Conservative));
            Assert.That(HabitableZones.Classify(1, 5780, 0.723, 0.0068), Is.EqualTo(HabitableZone.None));
            Assert.That(HabitableZones.Classify(1, 5780, 1.524, 0.093), Is.EqualTo(HabitableZone.Conservative));
        }

        [Test]
        public void EpsilonEridaniHasItsThreeBeltsAndTauCetiItsDisc()
        {
            var eri = StarSystem.Create(NearbySystems.EpsilonEridani(), StartJd);
            Assert.That(eri.Discs.Select(d => (d.InnerKm / Astro.AuKm, d.OuterKm / Astro.AuKm)),
                Is.EqualTo(new[] { (1.5, 2.0), (8.0, 20.0), (65.0, 75.0) }).Within(1e-9));
            Assert.That(eri.Discs.All(d => eri.Catalog[d.HostIndex].Name == "Epsilon Eridani"), Is.True);
            // The main belt is tilted 33.7 degrees from face-on (Booth et al. 2023).
            var los = Astro.DirectionFromRaDec(eri.Spec.RaDeg, eri.Spec.DecDeg);
            double tilt = Math.Acos(Math.Abs(DVec3.Dot(eri.Discs[2].Normal, los))) * Astro.Rad2Deg;
            Assert.That(tilt, Is.EqualTo(33.7).Within(1e-6));
            // The planet orbits between the inner belts, inside the main one.
            double b = eri.Catalog["Epsilon Eridani b"].Elements.AKm;
            Assert.That(b > eri.Discs[0].OuterKm && b < eri.Discs[1].InnerKm, Is.True);
            Assert.That(eri.ExtentKm, Is.EqualTo(75 * Astro.AuKm).Within(1e-6).Percent);

            var tau = StarSystem.Create(NearbySystems.TauCeti(), StartJd);
            Assert.That(tau.Discs, Has.Count.EqualTo(1));
            Assert.That(tau.Discs[0].InnerKm / Astro.AuKm, Is.EqualTo(6.2).Within(1e-9));
            Assert.That(tau.Discs[0].OuterKm / Astro.AuKm, Is.EqualTo(52).Within(1e-9));
        }

        [Test]
        public void EpsilonIndisBrownDwarfsCircleEachOtherFarOutFromA()
        {
            var sys = StarSystem.Create(NearbySystems.EpsilonIndi(), StartJd);
            var c = sys.Catalog;
            Assert.That(c.Root.Name, Is.EqualTo("Epsilon Indi A"));
            var pair = c["Epsilon Indi B"];
            Assert.That(pair.Kind, Is.EqualTo(BodyKind.Barycentre));
            Assert.That(pair.ParentIndex, Is.EqualTo(0));
            sys.Ephemeris.Update(Astro.J2000);
            // About 1,500 AU (projected) from A, across the line of sight.
            var offset = sys.Ephemeris.Helio(pair.Index);
            Assert.That(offset.Length / Astro.AuKm, Is.EqualTo(1500).Within(1e-6).Percent);
            Assert.That(Math.Abs(DVec3.Dot(offset.Normalized, Astro.DirectionFromRaDec(sys.Spec.RaDeg, sys.Spec.DecDeg))), Is.LessThan(1e-9));
            Assert.That(c["Epsilon Indi Ba"].ParentIndex, Is.EqualTo(pair.Index));
            Assert.That(c["Epsilon Indi Bb"].ParentIndex, Is.EqualTo(pair.Index));
        }

        [Test]
        public void TheGalaxyHoldsEveryNearbySystemAtItsRealDistance()
        {
            var g = Galaxy.CreateDefault(StartJd);
            Assert.That(g.Systems.Select(s => s.Name), Is.EqualTo(new[]
            {
                "Sol System", "Alpha Centauri", "Barnard's Star", "Luhman 16", "Wolf 359", "Lalande 21185", "Sirius", "Epsilon Eridani",
                "Ross 128", "Epsilon Indi", "Tau Ceti", "GJ 1061", "Luyten's Star", "Teegarden's Star",
            }));
            var sol = g.Systems[0];
            (string name, double ly)[] distances =
            {
                ("Barnard's Star", 5.963), ("Luhman 16", 6.510), ("Wolf 359", 7.856), ("Lalande 21185", 8.304), ("Sirius", 8.608),
                ("Epsilon Eridani", 10.475), ("Ross 128", 11.007), ("Epsilon Indi", 11.867), ("Tau Ceti", 11.912), ("GJ 1061", 11.984),
                ("Luyten's Star", 12.348), ("Teegarden's Star", 12.497),
            };
            foreach (var (name, ly) in distances)
                Assert.That(Galaxy.DistanceLy(sol, g.Find(name)), Is.EqualTo(ly).Within(0.001), name);

            // A simulated system replaces its plain star in the interstellar view; the rest stay.
            Assert.That(g.NearbyStars.Select(n => n.Name), Has.None.AnyOf("Sirius", "Barnard's Star", "Tau Ceti", "GJ 1061", "Luhman 16"));
            Assert.That(g.NearbyStars.Select(n => n.Name), Does.Contain("Procyon"));
            Assert.That(g.NearbyStars.Count + 10, Is.EqualTo(Galaxy.Neighbours().Count()));

            // Every system is on the gate lattice; the new systems start with no settlements or ships.
            Assert.That(g.Systems.Where(s => s.Gates.Count == 0).Select(s => s.Name), Is.Empty);
            foreach (var s in g.Systems.Skip(2))
            {
                Assert.That(s.World.Economy.Settlements, Is.Empty, s.Name);
                Assert.That(s.World.Fleet.Ships, Is.Empty, s.Name);
                Assert.That(s.OverviewSpanKm, Is.GreaterThan(0), s.Name);
            }
        }

        [Test]
        public void UnsettledSystemsRunQuietlyForAYear()
        {
            foreach (var spec in NearbySystems.All())
            {
                var s = StarSystem.Create(spec, StartJd);
                for (double jd = StartJd; jd <= StartJd + 365; jd += 0.5) s.Update(jd);
                Assert.That(s.Ephemeris.Jd, Is.EqualTo(StartJd + 365), spec.Name);
                Assert.That(s.World.Fleet.Ships, Is.Empty, spec.Name);
                Assert.That(s.World.War.Battles, Is.Empty, spec.Name);
            }
        }
    }
}
