using System;
using System.Collections.Generic;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    [TestFixture]
    public class AvoidanceTests
    {
        const double StartJd = 2461324.0;


        /// <summary>Two ships told to hold the very same spot spread apart instead of sitting inside each other.</summary>
        [Test]
        public void ShipsSharingASpotSpreadApart()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var fleet = new Fleet(eph, 7) { Dwell = (s, d) => 30 };
            var f = fleet.AddFaction("Test", "TS", new Rgb(1, 1, 1));
            int earth = eph.Catalog["Earth"].Index;
            var a = fleet.AddShip("A", ShipClass.Freighter, f, ShipRole.Trader);
            var b = fleet.AddShip("B", ShipClass.Freighter, f, ShipRole.Trader);
            a.Ports = b.Ports = new[] { earth, eph.Catalog["Moon"].Index };
            var spot = Infrastructure.EquatorialOrbit(eph.Catalog[earth], 20000, 0, 0, 0, StartJd);
            fleet.Station(a, earth, StartJd, spot);
            fleet.Station(b, earth, StartJd, spot);
            double first = 0, last = 0;
            for (double jd = StartJd; jd < StartJd + 0.15; jd += 0.0005)
            {
                eph.Update(jd);
                fleet.Update(jd);
                double sep = (fleet.StateOf(a, jd).HelioKm - fleet.StateOf(b, jd).HelioKm).Length;
                if (jd == StartJd) first = sep;
                last = sep;
            }
            TestContext.Out.WriteLine($"separation {first:0.000} km -> {last:0.0} km (safe {Fleet.SafeDistanceKm(a, b):0.0} km)");
            Assert.That(last > Fleet.SafeDistanceKm(a, b) * 0.9, Is.True, "they stayed on top of each other");
        }

        /// <summary>At high time warp (long frames) avoidance settles in one shot instead of thousands of sub-steps, and stays finite. (U.SPC-T554)</summary>
        [Test]
        public void LongFramesSettleWithoutOvershoot()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var fleet = new Fleet(eph, 7) { Dwell = (s, d) => 30 };
            var f = fleet.AddFaction("Test", "TS", new Rgb(1, 1, 1));
            int earth = eph.Catalog["Earth"].Index;
            var a = fleet.AddShip("A", ShipClass.Freighter, f, ShipRole.Trader);
            var b = fleet.AddShip("B", ShipClass.Freighter, f, ShipRole.Trader);
            a.Ports = b.Ports = new[] { earth, eph.Catalog["Moon"].Index };
            var spot = Infrastructure.EquatorialOrbit(eph.Catalog[earth], 20000, 0, 0, 0, StartJd);
            fleet.Station(a, earth, StartJd, spot);
            fleet.Station(b, earth, StartJd, spot);
            for (int k = 1; k <= 5; k++)
            {
                double jd = StartJd + k * 10;
                eph.Update(jd);
                fleet.Update(jd);
                var off = fleet.EvasionOf(a);
                Assert.That(double.IsNaN(off.Length) || double.IsInfinity(off.Length), Is.False);
            }
        }

        /// <summary>The ship the player is flying isn't at its scheduled spot: nobody gives way to that ghost.</summary>
        [Test]
        public void NobodySteersRoundTheScheduledSpotOfAHeldShip()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var fleet = new Fleet(eph, 7) { Dwell = (s, d) => 30 };
            var f = fleet.AddFaction("Test", "TS", new Rgb(1, 1, 1));
            int earth = eph.Catalog["Earth"].Index;
            var a = fleet.AddShip("A", ShipClass.Freighter, f, ShipRole.Trader);
            var b = fleet.AddShip("B", ShipClass.Freighter, f, ShipRole.Trader);
            a.Ports = b.Ports = new[] { earth, eph.Catalog["Moon"].Index };
            var spot = Infrastructure.EquatorialOrbit(eph.Catalog[earth], 20000, 0, 0, 0, StartJd);
            fleet.Station(a, earth, StartJd, spot);
            fleet.Station(b, earth, StartJd, spot);
            // Let them start giving way to each other, then the player takes A.
            for (double jd = StartJd; jd < StartJd + 0.02; jd += 0.0005)
            {
                eph.Update(jd);
                fleet.Update(jd);
            }
            Assert.That(fleet.EvasionOf(a).Length, Is.GreaterThan(0));
            fleet.IsHeld = s => s == a;
            for (double jd = StartJd + 0.02; jd < StartJd + 0.07; jd += 0.0005)
            {
                eph.Update(jd);
                fleet.Update(jd);
                Assert.That(fleet.EvadingFrom(b), Is.Not.SameAs(a));
            }
            Assert.That(fleet.EvasionOf(a).Length, Is.EqualTo(0), "the held ship kept a stale sidestep for when it is released");
        }

        /// <summary>A parked ship moves with its world round the Sun, as a ship in flight beside it does.</summary>
        [Test]
        public void AParkedShipsVelocityIncludesItsWorldsMotion()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var fleet = new Fleet(eph, 7) { Dwell = (s, d) => 30 };
            var f = fleet.AddFaction("Test", "TS", new Rgb(1, 1, 1));
            int earth = eph.Catalog["Earth"].Index;
            var a = fleet.AddShip("A", ShipClass.Freighter, f, ShipRole.Trader);
            a.Ports = new[] { earth, eph.Catalog["Moon"].Index };
            fleet.Station(a, earth, StartJd, Infrastructure.EquatorialOrbit(eph.Catalog[earth], 20000, 0, 0, 0, StartJd));
            fleet.Update(StartJd);
            Assert.That(a.Phase, Is.EqualTo(ShipPhase.Parked));
            var earthVel = (eph.HelioAt(earth, StartJd + 1e-4) - eph.HelioAt(earth, StartJd - 1e-4)) / 2e-4;
            var rel = fleet.HelioVelocityOf(a, StartJd) - earthVel;
            // About 4.5 km/s of orbit round Earth, not Earth's 30 km/s round the Sun missing.
            Assert.That(rel.Length / Astro.SecondsPerDay, Is.LessThan(10));
        }

        /// <summary>Head-on: two ships burning straight at each other see it coming and pass wide.</summary>
        [Test]
        public void HeadOnShipsTurnAside()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var fleet = new Fleet(eph, 9) { Dwell = (s, d) => 30 };
            var f = fleet.AddFaction("Test", "TS", new Rgb(1, 1, 1));
            int earth = eph.Catalog["Earth"].Index;
            var a = fleet.AddShip("A", ShipClass.Courier, f, ShipRole.Trader);
            var b = fleet.AddShip("B", ShipClass.Courier, f, ShipRole.Trader);
            a.Ports = b.Ports = new[] { earth, eph.Catalog["Moon"].Index };
            // The same circular orbit flown both ways: they meet head on twice a revolution.
            var prograde = Infrastructure.EquatorialOrbit(eph.Catalog[earth], 40000, 0, 0, 0, StartJd);
            var retrograde = Infrastructure.EquatorialOrbit(eph.Catalog[earth], 40000, 180, 0, 180, StartJd);
            fleet.Station(a, earth, StartJd, prograde);
            fleet.Station(b, earth, StartJd, retrograde);
            double closest = double.MaxValue, closestWithout = double.MaxValue;
            var without = new Fleet(eph, 9) { Dwell = (s, d) => 30, AvoidCollisions = false };
            var a2 = without.AddShip("A", ShipClass.Courier, f, ShipRole.Trader);
            var b2 = without.AddShip("B", ShipClass.Courier, f, ShipRole.Trader);
            a2.Ports = b2.Ports = a.Ports;
            without.Station(a2, earth, StartJd, prograde);
            without.Station(b2, earth, StartJd, retrograde);
            bool evaded = false;
            // They meet a quarter of a revolution in (about 5.5 hours), closing at 6 km/s: sample every second.
            for (double jd = StartJd; jd < StartJd + 0.3; jd += 0.00001)
            {
                eph.Update(jd);
                fleet.Update(jd);
                without.Update(jd);
                closest = Math.Min(closest, (fleet.StateOf(a, jd).HelioKm - fleet.StateOf(b, jd).HelioKm).Length);
                closestWithout = Math.Min(closestWithout, (without.StateOf(a2, jd).HelioKm - without.StateOf(b2, jd).HelioKm).Length);
                evaded |= fleet.EvadingFrom(a) == b;
            }
            double safe = Fleet.SafeDistanceKm(a, b);
            TestContext.Out.WriteLine($"closest pass {closest:0.00} km with avoidance, {closestWithout:0.00} km without (safe {safe:0.00} km)");
            Assert.That(evaded, Is.True, "they never saw each other coming");
            Assert.That(closestWithout < safe * 0.5, Is.True, "the test orbits do not actually meet");
            Assert.That(closest > safe * 0.8, Is.True, $"they passed {closest:0.00} km apart");
        }

        /// <summary>A month of the whole Sol system: nobody flies through anybody.</summary>
        [Test]
        public void NoTwoShipsCollideInAMonth()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            var fleet = w.Fleet;
            int checks = 0, evasions = 0;
            for (double jd = StartJd; jd < StartJd + 30; jd += 0.002)
            {
                eph.Update(jd);
                w.Update(jd);
                var flying = fleet.Ships.Where(s => !s.IsEscort && !s.Destroyed && !s.Derelict && s.Battle == null &&
                    (s.Phase == ShipPhase.Parked || s.Phase == ShipPhase.Transit || s.Phase == ShipPhase.Queued)).ToList();
                var at = flying.Select(s => fleet.StateOf(s, jd).HelioKm).ToList();
                for (int i = 0; i < flying.Count; i++)
                {
                    if (fleet.EvadingFrom(flying[i]) != null) evasions++;
                    for (int j = i + 1; j < flying.Count; j++)
                    {
                        double d = (at[i] - at[j]).Length;
                        // A third of the safe distance is a collision course nobody turned away from.
                        Assert.That(d > Fleet.SafeDistanceKm(flying[i], flying[j]) / 3, Is.True, $"{flying[i].Name} and {flying[j].Name} pass {d:0.00} km apart at JD {jd:0.000}");
                        checks++;
                    }
                }
            }
            TestContext.Out.WriteLine($"{checks:N0} pair checks, {evasions:N0} ship-steps spent giving way");
        }

        /// <summary>Free ships go where they please: out to open space near a world, and on to others.</summary>
        [Test]
        public void FreeShipsRoamOnTheirOwn()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            var free = w.Fleet.Ships.Where(s => s.Role == ShipRole.Free).ToList();
            Assert.That(free.Count >= 8, Is.True);
            int hops = 0, voyages = 0;
            var visited = new HashSet<int>();
            var seen = new HashSet<(int, double)>();
            for (double jd = StartJd; jd < StartJd + 60; jd += 0.02)
            {
                eph.Update(jd);
                w.Update(jd);
                foreach (var s in free)
                {
                    if (s.Destroyed || s.Phase != ShipPhase.Transit || !seen.Add((s.Id, s.PhaseStartJd))) continue;
                    if (s.Wander) hops++;
                    else voyages++;
                    visited.Add(s.Orbit.Body);
                    var st = w.Fleet.StateOf(s, jd);
                    Assert.That(double.IsNaN(st.HelioKm.X), Is.False, s.Name);
                }
            }
            TestContext.Out.WriteLine($"{hops} hops out to open space, {voyages} trips between worlds, {visited.Count} worlds visited");
            Assert.That(hops > 20, Is.True, "free ships never wandered");
            Assert.That(voyages > 10, Is.True, "free ships never left home");
            Assert.That(visited.Count > 6, Is.True);
        }
            /// <summary>A merchant changing course mid-flight carries on from exactly where it is, moving as it was.</summary>
        [Test]
        public void ADivertedShipTurnsFromWhereItIs()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            int diverted = 0;
            for (double jd = StartJd; jd < StartJd + 20 && diverted < 5; jd += 0.05)
            {
                eph.Update(jd);
                w.Update(jd);
                foreach (var s in w.Fleet.Ships)
                {
                    if (!w.IsTrader(s) || s.Phase != ShipPhase.Transit || s.Journey != null || s.PhaseEndJd - jd < 0.2) continue;
                    int other = s.Ports.FirstOrDefault(p => p != s.Orbit.Body && w.Fleet.Lanes.SystemOf(p) == w.Fleet.Lanes.SystemOf(s.Orbit.Body));
                    if (other == 0 && s.Ports.All(p => p != 0)) other = s.OriginBody;
                    if (other == s.Orbit.Body) continue;
                    const double h = 1e-4;
                    var before = w.Fleet.StateOf(s, jd).HelioKm;
                    var vBefore = (w.Fleet.StateOf(s, jd).HelioKm - w.Fleet.StateOf(s, jd - h).HelioKm) / h;
                    if (!w.Fleet.Divert(s, other, jd)) continue;
                    var after = w.Fleet.StateOf(s, jd).HelioKm;
                    var vAfter = (w.Fleet.StateOf(s, jd + h).HelioKm - after) / h;
                    Assert.That((after - before).Length < 1, Is.True, $"{s.Name} jumped {(after - before).Length:N0} km changing course");
                    Assert.That((vAfter - vBefore).Length < 0.02 * Math.Max(vBefore.Length, 1000), Is.True, $"{s.Name}'s velocity jumped changing course");
                    diverted++;
                    break;
                }
            }
            TestContext.Out.WriteLine($"{diverted} course changes checked");
            Assert.That(diverted > 0, Is.True);
        }

        /// <summary>When fighting breaks out at a port, merchants on their way there turn aside.</summary>
        [Test]
        public void MerchantsTurnAsideFromFreshBattles()
        {
            // Whether a fight breaks out at a port while merchants are still on their way there is down to the dice:
            // across a few seeds, some do and the merchants turn aside.
            var turns = new System.Collections.Generic.List<WorldEvent>();
            foreach (int seed in new[] { 1977, 1978, 1979, 1980 })
            {
                var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
                eph.Update(StartJd);
                var w = World.CreateDefault(eph, StartJd, seed);
                for (double jd = StartJd; jd < StartJd + 120; jd += 0.05)
                {
                    eph.Update(jd);
                    w.Update(jd);
                }
                turns = w.Events.Where(e => e.Text.Contains("turns aside")).ToList();
                TestContext.Out.WriteLine($"seed {seed}: {turns.Count} diversions in 120 days");
                if (turns.Count > 0) break;
            }
            foreach (var e in turns.Take(5)) TestContext.Out.WriteLine($"{Astro.ToDateTime(e.Jd):yyyy-MM-dd}  {e.Text}");
            Assert.That(turns, Is.Not.Empty);
        }
    }
}
