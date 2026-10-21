using System;
using System.Collections.Generic;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    [TestFixture]
    public class InfrastructureTests
    {
        const double StartJd = 2461324.0;


        static World Create(out Ephemeris eph)
        {
            eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return World.CreateDefault(eph, StartJd);
        }

        [Test]
        public void EarthHasSevenRingsHoveringOverItsCitiesAtGeostationaryAltitude()
        {
            var w = Create(out var eph);
            var earth = eph.Catalog["Earth"];
            var rings = w.Infrastructure.RingsAt(earth.Index).ToList();
            Assert.That(rings.Count, Is.EqualTo(7));
            Assert.That(w.Infrastructure.RingsAt(eph.Catalog["Mars"].Index).Count(), Is.EqualTo(2));
            Assert.That(Infrastructure.RingsFor(1e12), Is.EqualTo(7));
            Assert.That(Infrastructure.RingsFor(1.5e6), Is.EqualTo(2));
            Assert.That(Infrastructure.RingsFor(150e3), Is.EqualTo(1));

            var ny = rings.First(r => r.City == "New York");
            Assert.That(ny.RadiusKm, Is.InRange(42163, 42166));
            for (double jd = StartJd; jd < StartJd + 2; jd += 0.137)
            {
                BodyOrientation.Frame(earth, jd, DVec3.Zero, out var north, out var meridian);
                var at = ny.RingPoint.OffsetAt(jd);
                BodyOrientation.LonLat(north, meridian, at, out double lon, out double lat);
                Assert.That(at.Length, Is.InRange(ny.RadiusKm - 1e-6, ny.RadiusKm + 1e-6));
                Assert.That(lat, Is.InRange(40.7128 - 1e-6, 40.7128 + 1e-6));
                Assert.That(lon, Is.InRange(-74.0060 - 1e-6, -74.0060 + 1e-6));

                // It turns with the ground: velocity = ω × r.
                const double h = 1e-4;
                var v = (ny.RingPoint.OffsetAt(jd + h) - ny.RingPoint.OffsetAt(jd - h)) / (2 * h) / Astro.SecondsPerDay;
                var omega = north * (BodyOrientation.RateDegPerDay(earth) * Astro.Deg2Rad / Astro.SecondsPerDay);
                Assert.That((v - DVec3.Cross(omega, at)).Length < 1e-3, Is.True, $"ring velocity off by {(v - DVec3.Cross(omega, at)).Length:G3} km/s");
            }
            // Sydney is south of the equator.
            var sydney = rings.First(r => r.City == "Sydney");
            BodyOrientation.Frame(earth, StartJd, DVec3.Zero, out var n2, out var m2);
            BodyOrientation.LonLat(n2, m2, sydney.RingPoint.OffsetAt(StartJd), out double slon, out double slat);
            Assert.That(slat, Is.InRange(-33.87, -33.86));
            Assert.That(slon, Is.InRange(151.20, 151.22));
        }

        [Test]
        public void MerchantsQueueDescendLandAndClimbOutInOrderAndWithoutJumps()
        {
            var w = Create(out var eph);
            var fleet = w.Fleet;
            var entries = new Dictionary<DockingRing, List<double>>();
            var last = new Dictionary<Ship, (DVec3 pos, ShipPhase phase)>();
            int landings = 0, checkedChanges = 0, departures = 0;
            const double step = 0.001;
            for (double jd = StartJd; jd < StartJd + 8; jd += step)
            {
                eph.Update(jd);
                w.Update(jd);
                foreach (var s in fleet.Ships)
                {
                    if (s.Destroyed || s.IsEscort || s.Battle != null) { last.Remove(s); continue; }
                    var st = fleet.StateOf(s, jd);
                    if (last.TryGetValue(s, out var prev) && prev.phase != s.Phase)
                    {
                        bool ringChange = prev.phase >= ShipPhase.Queued || s.Phase >= ShipPhase.Queued;
                        if (ringChange)
                        {
                            Assert.That(s.Spec.IsMilitary || s.Faction.IsPirate, Is.False, $"{s.Name} went down to a surface");
                            double moved = (st.HelioKm - prev.pos).Length;
                            double allowance = (Math.Max(st.SpeedKmS, 1) + 50) * Astro.SecondsPerDay * step * 3 + 2000;
                            Assert.That(moved < allowance, Is.True, $"{s.Name} jumped {moved:N0} km going from {prev.phase} to {s.Phase}");
                            checkedChanges++;
                        }
                        if (s.Phase == ShipPhase.Descent)
                        {
                            if (!entries.TryGetValue(s.Ring, out var list)) entries[s.Ring] = list = new List<double>();
                            list.Add(s.PhaseStartJd);
                        }
                        if (s.Phase == ShipPhase.Landed) landings++;
                        if (prev.phase == ShipPhase.Ascent) departures++;
                    }
                    if (s.Phase == ShipPhase.Landed)
                        Assert.That((st.HelioKm - eph.Helio(s.Ring.Body)).Length < s.Ring.SurfaceKm + 1, Is.True, $"{s.Name} landed off the ground");
                    last[s] = (st.HelioKm, s.Phase);
                }
            }
            foreach (var kv in entries)
            {
                var times = kv.Value;
                for (int i = 1; i < times.Count; i++)
                    Assert.That(times[i] - times[i - 1] >= kv.Key.EntryIntervalDays - 1e-9, Is.True, $"{kv.Key.Name} cleared two ships {(times[i] - times[i - 1]) * 1440:0} min apart");
                TestContext.Out.WriteLine($"{kv.Key.Name,-18} {times.Count} descents");
            }
            TestContext.Out.WriteLine($"{landings} landings, {departures} climbs out, {checkedChanges} ring phase changes checked");
            Assert.That(landings > 5, Is.True, "hardly anyone landed");
            Assert.That(departures > 2, Is.True, "nobody climbed back out");
            Assert.That(checkedChanges > 20, Is.True);
        }

        [Test]
        public void YardsBuildInOrbitAndLaunchFromTheirSlips()
        {
            var w = Create(out var eph);
            var earthYard = w.Infrastructure.Structures.First(s => s.Kind == StructureKind.Shipyard && s.Settlement.Name == "Earth Orbital");
            Assert.That(eph.Catalog[earthYard.Body].Name == "Earth", Is.True);
            Assert.That((w.Infrastructure.Structures).Any(s => s.Kind == StructureKind.Habitat && s.Settlement.Name == "Earth Orbital"), Is.True);
            Assert.That((w.Infrastructure.Structures).Any(s => s.Kind == StructureKind.Factory && s.Industry == IndustryKind.ElectronicsFab), Is.True);
            Assert.That((w.Infrastructure.Structures).Any(s => s.Name == "ISS" && s.Real), Is.True);
            // Where each new hull is the moment it is launched (it soon leaves for its first port).
            var launched = new List<(Ship ship, int body, double radius)>();
            w.ShipAdded += s =>
            {
                if (!s.IsEscort && !s.Faction.IsPirate) launched.Add((s, s.Orbit.Body, s.Orbit.RadiusKm));
            };
            double maxProgress = 0;
            for (double jd = StartJd; jd < StartJd + 240 && launched.Count < 4; jd += 0.1)
            {
                eph.Update(jd);
                w.Update(jd);
                foreach (var y in w.Infrastructure.Structures.Where(s => s.Kind == StructureKind.Shipyard))
                {
                    Assert.That(y.SlipProgress, Is.InRange(0, 1));
                    maxProgress = Math.Max(maxProgress, y.SlipProgress);
                }
            }
            TestContext.Out.WriteLine($"{launched.Count} launches, slips reached {maxProgress:P0}");
            Assert.That(launched, Is.Not.Empty);
            foreach (var (ship, body, radius) in launched)
            {
                var yards = w.Infrastructure.Structures.Where(s => s.Kind == StructureKind.Shipyard && s.Body == body).ToList();
                Assert.That((yards).Any(y => Math.Abs(y.Orbit.RadiusKm - radius) < 1e-6), Is.True);
            }
        }

        [Test]
        public void ConstellationsSitAtTheirRealAltitudes()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var infra = new Infrastructure(eph.Catalog);
            infra.AddSolSpacecraft();
            var earth = eph.Catalog["Earth"];
            foreach (var c in infra.Constellations)
            {
                var points = new DVec3[c.Count];
                c.Offsets(earth, StartJd, points);
                double min = points.Min(p => p.Length), max = points.Max(p => p.Length);
                TestContext.Out.WriteLine($"{c.Name,-20} {c.Count,5} satellites, {min - earth.RadiusKm:N0}-{max - earth.RadiusKm:N0} km up");
                Expect.All(points, p => Assert.That(double.IsNaN(p.X), Is.False));
                Assert.That(min > earth.RadiusKm + 300, Is.True);
            }
            var gps = infra.Constellations.First(c => c.Name == "GPS");
            var gpsPoints = new DVec3[gps.Count];
            gps.Offsets(earth, StartJd, gpsPoints);
            // Inclination 55° to the equator: the highest any gets above it is sin 55° of the radius.
            double top = gpsPoints.Max(p => DVec3.Dot(p, earth.SpinAxis)) / 26560;
            Assert.That(top, Is.InRange(Math.Sin(45 * Astro.Deg2Rad), Math.Sin(55.01 * Astro.Deg2Rad)));
            var jwst = infra.Structures.First(s => s.Name == "JWST");
            double fromEarth = (jwst.Helio(eph) - eph.Helio(earth.Index)).Length;
            Assert.That(fromEarth, Is.InRange(1.49e6, 1.51e6));
            Assert.That(jwst.Helio(eph).Length > eph.Helio(earth.Index).Length, Is.True, "JWST is on the night side");
        }

        [Test]
        public void ProximaBHasTwoGatesOnItsTerminator()
        {
            var sys = StarSystem.CreateAlphaCentauri(StartJd);
            var b = sys.Catalog["Proxima b"];
            var gates = sys.World.Infrastructure.RingsAt(b.Index).ToList();
            Assert.That(gates.Count, Is.EqualTo(2));
            // No stationary orbit inside the neighbourhood of a tidally locked world: three radii out.
            Expect.All(gates, g => Assert.That(g.RadiusKm, Is.EqualTo(b.RadiusKm * 3).Within(1e-6)));
            int landed = 0;
            for (double jd = StartJd; jd < StartJd + 60; jd += 0.02)
            {
                sys.Update(jd);
                landed = Math.Max(landed, sys.World.Fleet.Ships.Count(s => s.Phase == ShipPhase.Landed && s.Ring?.Body == b.Index));
                // Ships on the ground, or on the way down or up, are out of any fight.
                Assert.That((sys.World.Fleet.Ships).Any(s => s.Planetside && s.Battle != null), Is.False);
                if (Math.Abs(jd - (StartJd + 30)) < 0.01)
                {
                    // The gates stay over the terminator: 90° from the point facing Proxima, give or take
                    // the swing of its slightly eccentric orbit.
                    var toStar = -sys.Ephemeris.Relative(b.Index);
                    var north = BodyOrientation.MapNorth(b);
                    var subStellar = (toStar - north * DVec3.Dot(toStar, north)).Normalized;
                    foreach (var g in gates)
                    {
                        var up = g.Up(jd);
                        var flat = (up - north * DVec3.Dot(up, north)).Normalized;
                        double angle = Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(flat, subStellar)))) * Astro.Rad2Deg;
                        TestContext.Out.WriteLine($"{g.Name}: {angle:0.0}° from the sub-stellar point");
                        Assert.That(angle, Is.InRange(85, 95));
                    }
                }
            }
            Assert.That(landed > 0, Is.True, "nobody landed at New Kentaurus");
        }

        /// <summary>U.SPC-T181: a world built on an ephemeris that has no date yet (the tests' usual way) locked
        /// Titan's Kraken Gate to a NaN face, and every merchant flying to or from it froze on a NaN schedule.</summary>
        [Test]
        public void AFaceLockedRingBuiltBeforeTheEphemerisHasADateIsSomewhereAndShipsLeaveIt()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            var w = World.CreateDefault(eph, StartJd);
            var kraken = w.Infrastructure.Rings.First(r => r.Name == "Kraken Gate");
            var at = kraken.RingPoint.OffsetAt(StartJd);
            Assert.That(double.IsNaN(at.X) || double.IsNaN(at.Y) || double.IsNaN(at.Z), Is.False, "the ring point is NaN");
            Assert.That(at.Length, Is.EqualTo(kraken.RadiusKm).Within(1));
            for (double jd = StartJd; jd < StartJd + 30; jd += 0.25) w.Update(jd);
            var frozen = w.Fleet.Ships.Where(s => !s.Destroyed && double.IsNaN(s.PhaseEndJd)).Select(s => s.Name).ToList();
            Assert.That(frozen, Is.Empty, "ships stuck on a NaN schedule");
        }
    }
}
