using System;
using System.Collections.Generic;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    [TestFixture]
    public class HyperlaneTests
    {
        const double StartJd = 2461324.0;


        static World Create(out Ephemeris eph)
        {
            eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return World.CreateDefault(eph, StartJd);
        }

        [Test]
        public void EveryLaneSystemCanReachEveryOther()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var lanes = Hyperlanes.CreateDefault(eph);
            var systems = lanes.Lanes.SelectMany(l => new[] { l.A, l.B }).Distinct().ToList();
            foreach (int a in systems)
                foreach (int b in systems)
                    if (a != b) Assert.That(lanes.Route(a, b, StartJd), Is.Not.Null);
            var earthSaturn = lanes.Route(eph.Catalog["Earth"].Index, eph.Catalog["Saturn"].Index, StartJd);
            TestContext.Out.WriteLine("Earth to Saturn: " + string.Join(", ", earthSaturn.Select(h => h.Lane.Name)));
        }

        [Test]
        public void LanesSwingWideOfTheSun()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            var lanes = Hyperlanes.CreateDefault(eph);
            for (double jd = StartJd; jd < StartJd + 3000; jd += 37)
            {
                eph.Update(jd);
                foreach (var lane in lanes.Lanes)
                {
                    var a = lanes.RampHelio(lane, lane.A, jd);
                    var b = lanes.RampHelio(lane, lane.B, jd);
                    for (int i = 0; i <= 50; i++)
                    {
                        var p = Hyperlanes.Curve(a, b, lane.Bend, Hyperlanes.SunKeepOutKm, i / 50.0, DVec3.UnitZ);
                        // Near Mercury a lane may run inside 0.3 AU, but never near the Sun keep-out ships respect.
                        Assert.That(p.Length > Fleet.SunKeepOutKm * 1.2, Is.True, $"{lane.Name} passes {p.Length / Astro.AuKm:0.00} AU from the Sun");
                    }
                }
            }
        }

        [Test]
        public void InterplanetaryTripsRideLanesAndTakeHoursToDaysNotWeeks()
        {
            var w = Create(out var eph);
            var earth = eph.Catalog["Earth"].Index;
            var mars = eph.Catalog["Mars"].Index;
            var freighter = w.Fleet.Ships.First(s => s.Spec.Class == ShipClass.Freighter);
            double lanes = w.Fleet.EstimateTravelDays(freighter, earth, mars, StartJd);
            double accel = freighter.Spec.AccelG * Astro.GravityKmPerDay2;
            double direct = 2 * Math.Sqrt((eph.HelioAt(mars, StartJd) - eph.HelioAt(earth, StartJd)).Length / accel);
            TestContext.Out.WriteLine($"Earth to Mars for a 0.1 g freighter: {lanes:0.0} days by lane, {direct:0.0} days by brachistochrone");
            Assert.That(lanes < direct * 0.5, Is.True);
            Assert.That(lanes < 3.5, Is.True);

            int laneRides = 0;
            for (double jd = StartJd; jd < StartJd + 60; jd += 0.1)
            {
                eph.Update(jd);
                w.Update(jd);
                laneRides += w.Fleet.Ships.Count(s => !s.Destroyed && s.Phase == ShipPhase.Lane);
            }
            Assert.That(laneRides > 100, Is.True, "nobody rode a lane");
        }

        /// <summary>Every leg change (ramp, lane, ramp, arrival) is seamless in true space.</summary>
        [Test]
        public void PositionIsContinuousAcrossEveryLegChange()
        {
            var w = Create(out var eph);
            var fleet = w.Fleet;
            int checkedBoundaries = 0;
            var last = new Dictionary<Ship, (double jd, DVec3 pos, ShipPhase phase, int journey)>();
            const double step = 0.002;
            for (double jd = StartJd; jd < StartJd + 25; jd += step)
            {
                eph.Update(jd);
                w.Update(jd);
                foreach (var s in fleet.Ships)
                {
                    if (s.Destroyed || s.IsEscort || s.Battle != null) { last.Remove(s); continue; }
                    var pos = fleet.StateOf(s, jd).HelioKm;
                    if (last.TryGetValue(s, out var prev) && (prev.phase != s.Phase || prev.journey != s.JourneyIndex) && s.Phase != ShipPhase.Parked && prev.phase != ShipPhase.Parked)
                    {
                        // Between samples a ship moves at most its speed (relative to its planet) plus the planet's
                        // own orbital speed (under 50 km/s) for one step; a jump would be far more.
                        double speed = (Math.Max(fleet.StateOf(s, jd).SpeedKmS, 1) + 50) * Astro.SecondsPerDay;
                        double moved = (pos - prev.pos).Length;
                        Assert.That(moved < speed * step * 3 + 2000, Is.True, $"{s.Name} jumped {moved:N0} km changing from {prev.phase} to {s.Phase}");
                        checkedBoundaries++;
                    }
                    last[s] = (jd, pos, s.Phase, s.JourneyIndex);
                }
            }
            TestContext.Out.WriteLine($"{checkedBoundaries} leg changes checked");
            Assert.That(checkedBoundaries > 20, Is.True);
        }

        /// <summary>The drawn ship meets the drawn ramps: burn end equals lane start, lane end equals the next burn's start.</summary>
        [Test]
        public void DrawnLanesMeetTheBurnsAtTheRamps()
        {
            var w = Create(out var eph);
            var scale = new ScaleModel { Blend = 1 };
            var scene = new DVec3[eph.Catalog.Count];
            int checkedRamps = 0;
            var seen = new HashSet<(int, double)>();
            for (double jd = StartJd; jd < StartJd + 40 && checkedRamps < 30; jd += 0.05)
            {
                eph.Update(jd);
                w.Update(jd);
                foreach (var s in w.Fleet.Ships)
                {
                    if (s.Destroyed || s.IsEscort || s.Phase != ShipPhase.Lane || !seen.Add((s.Id, s.PhaseStartJd))) continue;
                    // At the lane's start, it begins where the burn to the ramp ended.
                    double t0 = s.PhaseStartJd;
                    eph.Update(t0);
                    scale.Layout(eph, scene);
                    var laneStart = scale.LaneScene(eph, scene, w.Fleet.Lanes, s, 0, out _);
                    var ramp = scale.MapNear(eph, scene, s.LaneLeg.From, eph.Helio(s.LaneLeg.From) + s.DepartureOrbit.OffsetAt(t0));
                    Assert.That((laneStart - ramp).Length < 1e-3 * Math.Max(1, ramp.Length * 1e-6), Is.True, $"{s.Name} lane starts {(laneStart - ramp).Length:G3} units off its ramp");
                    var tail = scale.LaneScene(eph, scene, w.Fleet.Lanes, s, 0.002, out _);
                    Assert.That((tail - laneStart).Length < 0.05 * scale.LocalDistance(eph.Catalog.Sun, s.LaneLengthKm), Is.True, $"{s.Name} jumps leaving the ramp");
                    checkedRamps++;
                    eph.Update(jd);
                }
            }
            Assert.That(checkedRamps > 5, Is.True);
        }
            /// <summary>A raider leaving its lurk, or a ship climbing back in after an interdiction, eases back onto the lane.</summary>
        [Test]
        public void ShipsRejoinTheLaneWithoutJumping()
        {
            var w = Create(out var eph);
            var fleet = w.Fleet;
            int resumed = 0;
            for (double jd = StartJd; jd < StartJd + 365 && resumed < 3; jd += 0.05)
            {
                eph.Update(jd);
                w.Update(jd);
                foreach (var s in fleet.Ships)
                {
                    if (s.Destroyed || s.IsEscort || s.Battle != null || s.Phase != ShipPhase.Lurk) continue;
                    if (jd - s.PhaseStartJd < 0.2) continue;
                    var before = fleet.StateOf(s, jd).HelioKm;
                    fleet.ResumeLane(s, jd);
                    var after = fleet.StateOf(s, jd).HelioKm;
                    Assert.That((after - before).Length < 1, Is.True, $"{s.Name} jumped {(after - before).Length:N0} km rejoining the lane");
                    resumed++;
                }
            }
            TestContext.Out.WriteLine($"{resumed} lurkers sent back into their lanes");
            Assert.That(resumed > 0, Is.True, "nobody lurked");
        }

        /// <summary>An ambushed lane looks dangerous to the very next planner, not just after the route cache expires.</summary>
        [Test]
        public void DangerReroutesTrafficAtOnce()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var lanes = Hyperlanes.CreateDefault(eph);
            int earth = eph.Catalog["Earth"].Index, ceres = eph.Catalog["Ceres"].Index;
            var direct = lanes.Route(earth, ceres, StartJd);
            Assert.That(direct, Has.Exactly(1).Items);
            lanes.SetDanger(direct[0].Lane, 100);
            var around = lanes.Route(earth, ceres, StartJd);
            Assert.That((around).Any(hop => hop.Lane == direct[0].Lane), Is.False);
        }
    }
}
