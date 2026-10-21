using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>The player riding the hyperlanes at the helm (U.SPC-T41): in through a ring gate, along the lane, off at the far ramp.</summary>
    [TestFixture]
    public class PilotLaneTests
    {
        const double StartJd = 2461324.0;

        static World Create(out Ephemeris eph)
        {
            eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return World.CreateDefault(eph, StartJd);
        }

        static Lane LaneBetween(World w, Ephemeris eph, string a, string b)
        {
            int ia = eph.Catalog[a].Index, ib = eph.Catalog[b].Index;
            return w.Fleet.Lanes.Lanes.First(l => (l.A == ia && l.B == ib) || (l.A == ib && l.B == ia));
        }

        /// <summary>A courier at the helm, pinned at a spot near Earth as the pilot loop leaves it.</summary>
        static Ship Pilot(World w, Ephemeris eph, DVec3 helioKm)
        {
            var ship = w.Fleet.Ships.First(s => s.Spec.Class == ShipClass.Courier && !s.IsEscort && s.Battle == null);
            int earth = eph.Catalog["Earth"].Index;
            w.PilotedShip = ship;
            Fleet.PinAt(ship, earth, helioKm - eph.HelioAt(earth, StartJd), DVec3.UnitX, StartJd);
            return ship;
        }

        [Test]
        public void AHullInsideARampsRingGateIsOnThatLanesOnRamp()
        {
            var w = Create(out var eph);
            var lanes = w.Fleet.Lanes;
            var lane = LaneBetween(w, eph, "Earth", "Mars");
            int earth = eph.Catalog["Earth"].Index, mars = eph.Catalog["Mars"].Index;
            var ramp = lanes.RampHelio(lane, earth, StartJd);

            var hop = lanes.OnRampAt(ramp + new DVec3(0, Hyperlanes.GateRadiusKm * 0.6, 0), StartJd);
            Assert.That(hop.HasValue, Is.True, "inside the ring");
            Assert.That(hop.Value.Lane, Is.SameAs(lane));
            Assert.That(hop.Value.From, Is.EqualTo(earth));
            Assert.That(hop.Value.To, Is.EqualTo(mars), "the ramp at Earth rides toward Mars");

            Assert.That(lanes.OnRampAt(ramp + new DVec3(0, Hyperlanes.GateRadiusKm * 1.5, 0), StartJd).HasValue, Is.False, "outside the ring");
            Assert.That(lanes.OnRampAt(eph.HelioAt(earth, StartJd), StartJd).HasValue, Is.False, "at Earth itself, far from every ramp");
        }

        [Test]
        public void FlyingInJoinsTheLaneOnTheSamePathAiTrafficRides()
        {
            var w = Create(out var eph);
            var lanes = w.Fleet.Lanes;
            var lane = LaneBetween(w, eph, "Earth", "Mars");
            int earth = eph.Catalog["Earth"].Index;
            var entry = lanes.RampHelio(lane, earth, StartJd) + new DVec3(300, -200, 100);
            var ship = Pilot(w, eph, entry);

            var hop = lanes.OnRampAt(entry, StartJd).Value;
            w.Fleet.JoinLane(ship, hop, entry, StartJd);

            Assert.That(ship.Phase, Is.EqualTo(ShipPhase.Lane));
            Assert.That(ship.LaneLeg.Lane, Is.SameAs(lane));
            Assert.That(ship.PhaseStartJd, Is.EqualTo(StartJd));
            Assert.That(ship.PhaseEndJd - ship.PhaseStartJd, Is.EqualTo(Hyperlanes.RideDays(ship.LaneLengthKm)).Within(1e-9), "a whole lane at the lanes' own pace");
            Assert.That(Fleet.LaneProgress(ship, StartJd), Is.EqualTo(0).Within(1e-12));
            Assert.That(Fleet.LaneProgress(ship, ship.PhaseEndJd), Is.EqualTo(1).Within(1e-12));
            Assert.That((w.Fleet.StateOf(ship, StartJd).HelioKm - entry).Length, Is.LessThan(1), "the ride starts where the hull flew in");

            // Fast in the middle, at rest at both ends.
            double mid = (ship.PhaseStartJd + ship.PhaseEndJd) / 2;
            Assert.That(w.Fleet.StateOf(ship, mid).SpeedKmS, Is.GreaterThan(Hyperlanes.CruiseKmS * 0.9));
            Assert.That(w.Fleet.StateOf(ship, ship.PhaseEndJd - 1e-5).SpeedKmS, Is.LessThan(Hyperlanes.CruiseKmS * 0.01));
        }

        [Test]
        public void AHeldRiderWaitsAtTheFarRampForItsPilotAndComesOffThere()
        {
            var w = Create(out var eph);
            var lanes = w.Fleet.Lanes;
            var lane = LaneBetween(w, eph, "Earth", "Mars");
            int earth = eph.Catalog["Earth"].Index, mars = eph.Catalog["Mars"].Index;
            var entry = lanes.RampHelio(lane, earth, StartJd);
            var ship = Pilot(w, eph, entry);
            w.Fleet.JoinLane(ship, lanes.OnRampAt(entry, StartJd).Value, entry, StartJd);

            double end = ship.PhaseEndJd;
            eph.Update(end + 0.01);
            w.Fleet.Update(end + 0.01);
            Assert.That(ship.Phase, Is.EqualTo(ShipPhase.Lane), "the fleet leaves the helm's ride alone");
            Assert.That(Fleet.RideOver(ship, end + 0.01), Is.True);

            var arrival = w.Fleet.StateOf(ship, end).HelioKm;
            w.Fleet.RampReached(ship, null, DVec3.UnitY, end + 0.01);
            Assert.That(ship.Phase, Is.EqualTo(ShipPhase.Parked));
            Assert.That(ship.Orbit.Body, Is.EqualTo(mars), "off at Mars's ramp, in Mars's frame");
            Assert.That(ship.Journey, Is.Null);
            Assert.That((ship.Orbit.OffsetAt(end) - (arrival - eph.HelioAt(mars, end))).Length, Is.LessThan(1), "pinned right where the ride ended");
            Assert.That(ship.Orbit.OffsetAt(end).Length, Is.EqualTo(lanes.RampRadiusKm(mars)).Within(1), "at the off-ramp");
        }

        [Test]
        public void AtAnOffRampThePilotCanCarryOnAlongAnotherLane()
        {
            var w = Create(out var eph);
            var lanes = w.Fleet.Lanes;
            var lane = LaneBetween(w, eph, "Earth", "Mars");
            int earth = eph.Catalog["Earth"].Index, mars = eph.Catalog["Mars"].Index;
            var entry = lanes.RampHelio(lane, earth, StartJd);
            var ship = Pilot(w, eph, entry);
            w.Fleet.JoinLane(ship, lanes.OnRampAt(entry, StartJd).Value, entry, StartJd);

            var onward = lanes.OnwardHops(mars, lane);
            Assert.That(onward, Is.Not.Empty);
            Assert.That(onward.All(h => h.From == mars && h.Lane != lane), Is.True, "every way on leaves Mars, none straight back to Earth");
            var ceres = onward.First(h => h.To == eph.Catalog["Ceres"].Index);

            double end = ship.PhaseEndJd;
            var arrival = w.Fleet.StateOf(ship, end).HelioKm;
            eph.Update(end);
            w.Fleet.RampReached(ship, ceres, DVec3.UnitY, end);
            Assert.That(ship.Phase, Is.EqualTo(ShipPhase.Lane));
            Assert.That(ship.LaneLeg.Lane, Is.SameAs(ceres.Lane));
            Assert.That(ship.LaneLeg.From, Is.EqualTo(mars));
            Assert.That((w.Fleet.StateOf(ship, end).HelioKm - arrival).Length, Is.LessThan(1), "the next lane picks up right where the last one left off");
            Assert.That(ship.PhaseEndJd, Is.GreaterThan(end));
        }

        [Test]
        public void ARiderLetGoOfMidRideIsCarriedOnByTheFleet()
        {
            var w = Create(out var eph);
            var lanes = w.Fleet.Lanes;
            var lane = LaneBetween(w, eph, "Earth", "Mars");
            int earth = eph.Catalog["Earth"].Index, mars = eph.Catalog["Mars"].Index;
            var entry = lanes.RampHelio(lane, earth, StartJd);
            var ship = Pilot(w, eph, entry);
            w.Fleet.JoinLane(ship, lanes.OnRampAt(entry, StartJd).Value, entry, StartJd);

            w.PilotedShip = null;
            double end = ship.PhaseEndJd;
            eph.Update(end + 1e-3);
            w.Fleet.Update(end + 1e-3);
            Assert.That(ship.Phase, Is.Not.EqualTo(ShipPhase.Lane), "off the lane at the far ramp, as any rider");
            Assert.That(ship.FinalBody, Is.EqualTo(mars), "and on into the Mars system");
        }

        [Test]
        public void ARideOfHoursIsWarpedToUnderAMinuteAtTheHelm()
        {
            double earthMars = 0.29 * Astro.SecondsPerDay;
            int i = SimClock.WarpToCover(earthMars, 45);
            Assert.That(earthMars / SimClock.Levels[i].SimSecondsPerSecond, Is.LessThanOrEqualTo(45));
            Assert.That(earthMars / SimClock.Levels[i - 1].SimSecondsPerSecond, Is.GreaterThan(45), "the slowest warp that does it");
            Assert.That(SimClock.WarpToCover(10, 45, 3), Is.EqualTo(3), "never slower than asked");
            Assert.That(SimClock.WarpToCover(1e12, 45), Is.EqualTo(SimClock.Levels.Length - 1));
        }
    }
}
