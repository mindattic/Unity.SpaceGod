using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Covers U.SPC-T96: the Lagrange points of every body pair, the infrastructure placed at them
    /// (Earth-Moon L1/L2, Sun-Earth L1/L2, Jupiter's L4/L5), and the real Jupiter Trojans Hektor,
    /// Patroclus and Eurybates as mining grounds and a pirate haven. Truth values are JPL Horizons
    /// vectors (00:00 TDB, km, ecliptic J2000): SEMB-L1..L5 (31, 32, 34, 35) heliocentric, EM-L1..L5
    /// (3011, 3012, 3014, 3015) geocentric, and the Trojans (624, 617, 3548) heliocentric.
    /// </summary>
    [TestFixture]
    public class LagrangeTests
    {
        static readonly BodyCatalog Catalog = BodyCatalog.CreateSolSystem();
        const double StartJd = 2461324.0;

        static double AngleDeg(DVec3 a, DVec3 b) =>
            Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(a.Normalized, b.Normalized)))) * Astro.Rad2Deg;

        [Test]
        public void CollinearPointsMatchTheRestrictedThreeBodyProblem()
        {
            // Earth-Moon (mu = 0.01215): Horizons puts EM-L1 at 84.907% of the Earth-Moon distance from
            // Earth and EM-L2 at 116.783%.
            double mu = Lagrange.MassRatio(398600.435, 4902.800);
            Assert.That(1 - Lagrange.CollinearDistance(mu, LagrangePoint.L1), Is.EqualTo(0.84907).Within(2e-5));
            Assert.That(1 + Lagrange.CollinearDistance(mu, LagrangePoint.L2), Is.EqualTo(1.16783).Within(2e-5));
            // L3 lies a whisker inside the far side: 1 - 7mu/12 to first order.
            Assert.That(Lagrange.CollinearDistance(mu, LagrangePoint.L3), Is.EqualTo(1 - 7 * mu / 12).Within(1e-4));
            // For a tiny mass ratio L1 and L2 close in on the Hill radius (mu/3)^(1/3).
            double small = 3e-6;
            double hill = Math.Pow(small / 3, 1.0 / 3);
            Assert.That(Lagrange.CollinearDistance(small, LagrangePoint.L1) / hill, Is.EqualTo(1 - hill / 3).Within(1e-4));
            Assert.That(Lagrange.CollinearDistance(small, LagrangePoint.L2) / hill, Is.EqualTo(1 + hill / 3).Within(1e-4));
        }

        [TestCase(0.01215)]
        [TestCase(9.54e-4)]
        [TestCase(3.04e-6)]
        [TestCase(0.3)]
        public void EveryPointIsAnEquilibriumInTheRotatingFrame(double mu)
        {
            // Unit separation and angular speed, barycentre at the origin: gravity from both bodies plus
            // the centrifugal pull vanishes at all five points.
            var primary = new DVec3(-mu, 0, 0);
            var secondary = new DVec3(1 - mu, 0, 0);
            foreach (LagrangePoint p in new[] { LagrangePoint.L1, LagrangePoint.L2, LagrangePoint.L3, LagrangePoint.L4, LagrangePoint.L5 })
            {
                var r = primary + Lagrange.Offset(secondary - primary, DVec3.UnitZ, mu, p);
                var d1 = r - primary;
                var d2 = r - secondary;
                var accel = d1 * (-(1 - mu) / Math.Pow(d1.Length, 3)) + d2 * (-mu / Math.Pow(d2.Length, 3)) + new DVec3(r.X, r.Y, 0);
                Assert.That(accel.Length < 1e-9, Is.True, $"{p} at mu {mu}: residual {accel.Length:E2}");
            }
            // L4 leads the smaller body (ahead in the sense of its motion, +y here) and L5 trails.
            Assert.That(Lagrange.Offset(secondary - primary, DVec3.UnitZ, mu, LagrangePoint.L4).Y > 0, Is.True);
            Assert.That(Lagrange.Offset(secondary - primary, DVec3.UnitZ, mu, LagrangePoint.L5).Y < 0, Is.True);
        }

        // Horizons SEMB-L1, L2, L4, L5, heliocentric.
        [TestCase(LagrangePoint.L1, 2461330.5, 1.356177496421320E+08, 5.831235959543677E+07, -4.379096688456833E+03)]
        [TestCase(LagrangePoint.L1, 2461512.5, -1.331432740551894E+08, -6.606616634765628E+07, 4.913608893822879E+03)]
        [TestCase(LagrangePoint.L2, 2461330.5, 1.383697543931055E+08, 5.949565522650705E+07, -4.467958912782371E+03)]
        [TestCase(LagrangePoint.L2, 2461512.5, -1.358450658466543E+08, -6.740680504832143E+07, 5.013317634422332E+03)]
        [TestCase(LagrangePoint.L4, 2461330.5, 1.748392105552160E+07, 1.480870925009285E+08, -9.222512632198632E+03)]
        [TestCase(LagrangePoint.L4, 2461512.5, -9.451274984823523E+06, -1.498385711653183E+08, 9.517331394709647E+03)]
        [TestCase(LagrangePoint.L5, 2461330.5, 1.195052237971305E+08, -8.918506605971140E+07, 4.799133596900851E+03)]
        [TestCase(LagrangePoint.L5, 2461512.5, -1.250383718632641E+08, 8.310432984705012E+07, -4.554035054214299E+03)]
        public void SunEarthPointsMatchJplHorizons(LagrangePoint point, double jd, double x, double y, double z)
        {
            var eph = new Ephemeris(Catalog);
            eph.Update(jd);
            int earth = Catalog["Earth"].Index;
            var model = Lagrange.HelioAt(eph, earth, point, jd);
            var truth = new DVec3(x, y, z);
            Assert.That(AngleDeg(model, truth) < 0.02, Is.True, $"{point} is {AngleDeg(model, truth):0.0000} degrees off");
            Assert.That(model.Length / truth.Length, Is.InRange(0.9998, 1.0002));
            if (point == LagrangePoint.L1 || point == LagrangePoint.L2)
            {
                // About 1.5 million km from Earth, and within 1% of where Horizons has it.
                double fromEarth = (model - eph.Helio(earth)).Length;
                Assert.That(fromEarth, Is.InRange(1.46e6, 1.54e6));
                Assert.That((model - truth).Length < 0.01 * fromEarth, Is.True, $"{point} misses Horizons by {(model - truth).Length:N0} km");
            }
        }

        // Horizons EM-L1, L2, L4, L5, geocentric.
        [TestCase(LagrangePoint.L1, 2461330.5, 3.026887529698227E+04, -3.413667671771903E+05, -2.430971892891391E+04)]
        [TestCase(LagrangePoint.L1, 2461512.5, -2.891586604733508E+05, 1.273871357989081E+05, -8.850660391989892E+03)]
        [TestCase(LagrangePoint.L2, 2461330.5, 4.163279915654918E+04, -4.695270172896897E+05, -3.343638289998859E+04)]
        [TestCase(LagrangePoint.L2, 2461512.5, -3.977182786204732E+05, 1.752124352165129E+05, -1.217348762294191E+04)]
        [TestCase(LagrangePoint.L4, 2461330.5, 3.664940353597807E+05, -1.714528062725354E+05, 4.560818804756942E+03)]
        [TestCase(LagrangePoint.L4, 2461512.5, -2.991156617498362E+05, -2.193035322538691E+05, -3.218493024212051E+04)]
        [TestCase(LagrangePoint.L5, 2461330.5, -3.308444152759741E+05, -2.305970739288016E+05, -3.319195952887017E+04)]
        [TestCase(LagrangePoint.L5, 2461512.5, -4.144532679724413E+04, 3.693356669021968E+05, 2.176093146745270E+04)]
        public void EarthMoonPointsMatchJplHorizons(LagrangePoint point, double jd, double x, double y, double z)
        {
            var eph = new Ephemeris(Catalog);
            eph.Update(jd);
            int moon = Catalog["Moon"].Index;
            var model = Lagrange.OffsetAt(eph, moon, point, jd);
            var truth = new DVec3(x, y, z);
            Assert.That(AngleDeg(model, truth) < 0.1, Is.True, $"{point} is {AngleDeg(model, truth):0.000} degrees off");
            Assert.That(model.Length / truth.Length, Is.InRange(0.995, 1.005));
        }

        // Horizons heliocentric vectors on 2027-01-01 and 2027-12-31 against the SBDB osculating orbits.
        [TestCase("Hektor", 2461406.5, -6.786713416139722E+08, -3.338125746970830E+08, -1.702889547239712E+08)]
        [TestCase("Hektor", 2461770.5, -4.037592945215499E+08, -6.286637439214786E+08, -2.359581998603631E+08)]
        [TestCase("Patroclus", 2461406.5, 9.378462610090424E+07, 7.377185418768520E+08, 1.872421239570651E+08)]
        [TestCase("Patroclus", 2461770.5, -2.901153760895534E+08, 7.122381465742909E+08, 2.886094603077391E+08)]
        [TestCase("Eurybates", 2461406.5, -6.348473220797893E+08, -5.483803536590164E+08, 5.671328592504978E+06)]
        [TestCase("Eurybates", 2461770.5, -3.439982825026492E+08, -7.766959183220327E+08, -4.607048233532608E+07)]
        public void TrojansMatchJplHorizons(string name, double jd, double x, double y, double z)
        {
            var eph = new Ephemeris(Catalog);
            eph.Update(jd);
            var model = eph.Helio(Catalog[name].Index);
            var truth = new DVec3(x, y, z);
            Assert.That(AngleDeg(model, truth) < 0.2, Is.True, $"{name} is {AngleDeg(model, truth):0.000} degrees off at JD {jd}");
            Assert.That(model.Length / truth.Length, Is.InRange(0.998, 1.002));
        }

        [Test]
        public void TrojansKeepToJupitersSwarms()
        {
            // Hektor and Eurybates in the Greek camp 60 degrees ahead, Patroclus in the Trojan camp behind,
            // librating about their points but never leaving them across 2020-2035.
            var eph = new Ephemeris(Catalog);
            int jupiter = Catalog["Jupiter"].Index;
            foreach (var name in new[] { "Hektor", "Patroclus", "Eurybates" })
            {
                var b = Catalog[name];
                Assert.That(b.Kind, Is.EqualTo(BodyKind.Asteroid));
                Assert.That(b.CoOrbitalWith, Is.EqualTo("Jupiter"));
                double sign = b.TrojanPoint == LagrangePoint.L4 ? 1 : -1;
                for (double jd = 2458849.5; jd < 2464329.5; jd += 91)
                {
                    eph.Update(jd);
                    double lead = Lagrange.LeadDeg(eph, jupiter, b.Index, jd) * sign;
                    Assert.That(lead, Is.InRange(25, 100), $"{name} at JD {jd}");
                    // Sharing Jupiter's orbit: the same distance from the Sun, give or take its eccentricity.
                    Assert.That(eph.Helio(b.Index).Length / eph.Helio(jupiter).Length, Is.InRange(0.8, 1.2), name);
                }
            }
            Assert.That(Catalog["Hektor"].TrojanPoint, Is.EqualTo(LagrangePoint.L4));
            Assert.That(Catalog["Eurybates"].TrojanPoint, Is.EqualTo(LagrangePoint.L4));
            Assert.That(Catalog["Patroclus"].TrojanPoint, Is.EqualTo(LagrangePoint.L5));
            // Their elements are the SBDB's (epoch JD 2461200.5).
            var hektor = Catalog["Hektor"].Elements.At(2461200.5);
            Assert.That(hektor.A / Astro.AuKm, Is.EqualTo(5.276986).Within(1e-6));
            Assert.That(hektor.E, Is.EqualTo(0.024345).Within(1e-6));
        }

        [Test]
        public void EveryBodyPairHasPointsAndBinaryStarsDoNot()
        {
            var pairs = Lagrange.Pairs(Catalog).ToList();
            Assert.That(pairs.Any(p => Catalog[p.secondary].Name == "Moon" && Catalog[p.primary].Name == "Earth"), Is.True);
            Assert.That(pairs.Any(p => Catalog[p.secondary].Name == "Jupiter" && p.primary == 0), Is.True);
            Assert.That(pairs.Any(p => Catalog[p.secondary].Name == "Titan"), Is.True);
            Assert.That(pairs.Any(p => p.secondary == 0), Is.False, "the Sun orbits nothing");
            var centauri = BodyCatalog.CreateAlphaCentauri();
            Assert.That(Lagrange.HasPoints(centauri, centauri["Alpha Centauri A"].Index), Is.False, "A orbits the barycentre");
            Assert.That(Lagrange.HasPoints(centauri, centauri["Proxima b"].Index), Is.True);

            // Every pair's points are finite and where they should be along the line.
            var eph = new Ephemeris(Catalog);
            eph.Update(StartJd);
            foreach (var (primary, secondary) in pairs)
            {
                var rel = eph.Helio(secondary) - eph.Helio(primary);
                foreach (LagrangePoint p in new[] { LagrangePoint.L1, LagrangePoint.L2, LagrangePoint.L3, LagrangePoint.L4, LagrangePoint.L5 })
                {
                    var off = Lagrange.OffsetAt(eph, secondary, p, StartJd);
                    Assert.That(double.IsNaN(off.X) || double.IsNaN(off.Y) || double.IsNaN(off.Z), Is.False, Lagrange.Name(Catalog, secondary, p));
                }
                Assert.That(Lagrange.OffsetAt(eph, secondary, LagrangePoint.L1, StartJd).Length < rel.Length, Is.True);
                Assert.That(Lagrange.OffsetAt(eph, secondary, LagrangePoint.L2, StartJd).Length > rel.Length, Is.True);
                Assert.That(AngleDeg(Lagrange.OffsetAt(eph, secondary, LagrangePoint.L3, StartJd), rel), Is.GreaterThan(179));
                Assert.That(AngleDeg(Lagrange.OffsetAt(eph, secondary, LagrangePoint.L4, StartJd), rel), Is.EqualTo(60).Within(0.5));
            }
            Assert.That(Lagrange.Name(Catalog, Catalog["Moon"].Index, LagrangePoint.L1), Is.EqualTo("Earth-Moon L1"));
        }

        [Test]
        public void InfrastructureSitsAtItsPointsAndStaysCoOrbital()
        {
            var eph = new Ephemeris(Catalog);
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            var infra = w.Infrastructure;
            OrbitalStructure Get(string name) => infra.Structures.First(s => s.Name == name);
            int earth = Catalog["Earth"].Index, moon = Catalog["Moon"].Index, jupiter = Catalog["Jupiter"].Index;

            // Depots, relays and gateways where they belong, owned by the powers that use them.
            Assert.That(Get("Cislunar Depot").Kind, Is.EqualTo(StructureKind.Depot));
            Assert.That(Get("L1 Gateway").Kind, Is.EqualTo(StructureKind.Gateway));
            Assert.That(Get("Queqiao").Real, Is.True);
            Assert.That(Get("Sunward Relay").Owner, Is.EqualTo(w.Sol));
            Assert.That(Get("Greek Camp Depot").Owner, Is.EqualTo(w.Belt));
            Assert.That((Get("Cislunar Depot").Body, Get("Cislunar Depot").Lagrange), Is.EqualTo((moon, LagrangePoint.L1)));
            Assert.That((Get("Farside Depot").Body, Get("Farside Depot").Lagrange), Is.EqualTo((moon, LagrangePoint.L2)));
            Assert.That((Get("SOHO").Body, Get("SOHO").Lagrange), Is.EqualTo((earth, LagrangePoint.L1)));
            Assert.That((Get("Outbound Depot").Body, Get("Outbound Depot").Lagrange), Is.EqualTo((earth, LagrangePoint.L2)));
            Assert.That((Get("Trojan Camp Relay").Body, Get("Trojan Camp Relay").Lagrange), Is.EqualTo((jupiter, LagrangePoint.L5)));

            for (double jd = StartJd; jd < StartJd + 4 * 365; jd += 37.3)
            {
                eph.Update(jd);
                var e = eph.Helio(earth);
                var m = eph.Helio(moon);
                // Earth-Moon L1 between them on their line, L2 beyond the Moon.
                var l1 = Get("Cislunar Depot").HelioAt(eph, jd);
                Assert.That(AngleDeg(l1 - e, m - e) < 1e-4, Is.True);
                Assert.That((l1 - e).Length / (m - e).Length, Is.EqualTo(0.849).Within(0.001));
                Assert.That((Get("Farside Depot").HelioAt(eph, jd) - e).Length / (m - e).Length, Is.EqualTo(1.168).Within(0.001));
                // JWST on the night side, 1.5 million km out; SOHO sunward.
                var jwst = Get("JWST").Helio(eph);
                Assert.That((jwst - e).Length, Is.InRange(1.46e6, 1.54e6));
                Assert.That(jwst.Length > e.Length, Is.True, "JWST is on the night side");
                Assert.That(Get("SOHO").Helio(eph).Length < e.Length, Is.True, "SOHO is sunward");
                // Jupiter's camps ride 60 degrees ahead and behind, at Jupiter's distance from the Sun.
                var j = eph.Helio(jupiter);
                var l4 = Get("Greek Camp Depot").Helio(eph);
                var l5 = Get("Trojan Camp Relay").Helio(eph);
                // (Jupiter and its moons together: the point is at their centre of mass's distance, within a few hundred km of Jupiter's.)
                Assert.That(l4.Length / j.Length, Is.EqualTo(1).Within(1e-6));
                Assert.That(AngleDeg(l4, j), Is.EqualTo(60).Within(1e-3));
                Assert.That(AngleDeg(l5, j), Is.EqualTo(60).Within(1e-3));
                Assert.That((l4 - j).Length / j.Length, Is.EqualTo(1).Within(1e-6), "each camp, Jupiter and the Sun form an equilateral triangle");
                Assert.That(DVec3.Dot(DVec3.Cross(j, l4), DVec3.Cross(j, eph.HelioAt(jupiter, jd + 1) - j)) > 0, Is.True, "L4 leads");
                // Look-ahead positions agree with the frame.
                Assert.That((Get("Greek Camp Depot").HelioAt(eph, jd) - l4).Length < 1e-3, Is.True);
            }
        }

        [Test]
        public void TheTrojansAreMiningGroundsAndHideAHaven()
        {
            var eph = new Ephemeris(Catalog);
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            int hektor = Catalog["Hektor"].Index, eurybates = Catalog["Eurybates"].Index, patroclus = Catalog["Patroclus"].Index;

            var diggings = w.Economy.At(hektor).Single();
            Assert.That(diggings.Kind, Is.EqualTo(SettlementKind.MiningOutpost));
            Assert.That(diggings.Owner, Is.EqualTo(w.Belt));
            Assert.That(diggings.Facilities.Any(f => f.Recipe.Kind == IndustryKind.Mine && f.Recipe.Outputs.Any(o => o.good == Good.Hydrocarbons)), Is.True);
            Assert.That(w.Economy.At(eurybates).Single().Kind, Is.EqualTo(SettlementKind.MiningOutpost));

            var hold = w.Economy.At(patroclus).Single();
            Assert.That(hold.Name, Is.EqualTo("Menoetius Hold"));
            Assert.That(hold.Kind, Is.EqualTo(SettlementKind.Haven));
            Assert.That(hold.Owner, Is.EqualTo(w.Pirates));
            Assert.That(hold.Hidden && !hold.Discovered, Is.True, "the Trojan haven is off the charts");

            // Lanes run out to the Greek camp's mines, none to the haven.
            var lanes = w.Fleet.Lanes;
            Assert.That(lanes.Route(lanes.SystemOf(Catalog["Earth"].Index), hektor, StartJd), Is.Not.Null);
            Assert.That(lanes.Route(lanes.SystemOf(Catalog["Ganymede"].Index), eurybates, StartJd), Is.Not.Null);
            Assert.That(lanes.Lanes.Any(l => l.A == patroclus || l.B == patroclus), Is.False);
        }
    }
}
