using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    [TestFixture]
    public class JumpGateTests
    {
        const double StartJd = 2461324.0;


        static Galaxy Create() => Galaxy.CreateDefault(StartJd);

        [Test]
        public void EachSystemGetsAGatePairedWithTheOther()
        {
            var g = Create();
            var sol = g.Systems[0];
            var ac = g.Systems[1];

            Assert.That(sol.GateTo(ac), Is.Not.Null);
            Assert.That(ac.GateTo(sol), Is.Not.Null);
            Assert.That(sol.GateTo(ac).Destination, Is.SameAs(ac));
            Assert.That(ac.GateTo(sol).Destination, Is.SameAs(sol));
        }

        [Test]
        public void GatesSitAtTheSystemsOwnOuterEdgeFacingTheOtherSystem()
        {
            var g = Create();
            var sol = g.Systems[0];
            var ac = g.Systems[1];

            Assert.That(sol.GateTo(ac).PositionKm.Length, Is.EqualTo(Galaxy.GateDistanceKm(sol)).Within(1e-3));
            Assert.That(ac.GateTo(sol).PositionKm.Length, Is.EqualTo(Galaxy.GateDistanceKm(ac)).Within(1e-3));
            // Clear of the root's own body: the Sun's gate isn't inside the Sun.
            Assert.That(sol.GateTo(ac).PositionKm.Length - sol.Catalog.Root.RadiusKm, Is.GreaterThanOrEqualTo(JumpGate.RadiusKm - 1e-6));

            // Each gate points (in its own system's frame) toward the real direction of the other star.
            double towardAc = DVec3.Dot(sol.GateTo(ac).PositionKm.Normalized, Galaxy.Direction(sol, ac));
            double towardSol = DVec3.Dot(ac.GateTo(sol).PositionKm.Normalized, Galaxy.Direction(ac, sol));
            TestContext.Out.WriteLine($"Sol gate alignment {towardAc:0.000}, Centauri gate alignment {towardSol:0.000}");
            Assert.That(towardAc, Is.InRange(0.9999, 1.0001));
            Assert.That(towardSol, Is.InRange(0.9999, 1.0001));
        }

        [Test]
        public void OnlyInRangeOfItsOwnGateCanAShipActivateIt()
        {
            var g = Create();
            var sol = g.Systems[0];
            var ac = g.Systems[1];
            var gate = sol.GateTo(ac);

            Assert.That(gate.CanActivate(gate.PositionKm), Is.True);
            var justInside = gate.PositionKm + new DVec3(JumpGate.ActivationRangeKm * 0.5, 0, 0);
            var farAway = gate.PositionKm + new DVec3(JumpGate.ActivationRangeKm * 50, 0, 0);
            Assert.That(gate.CanActivate(justInside), Is.True);
            Assert.That(gate.CanActivate(farAway), Is.False);
            Assert.That(gate.CanActivate(DVec3.Zero), Is.False);
        }

        [Test]
        public void ArrivalLandsJustInsideTheFarGateFacingItsSystemsCentre()
        {
            var g = Create();
            var sol = g.Systems[0];
            var ac = g.Systems[1];

            var arrival = sol.GateTo(ac).ArrivalOffsetKm;
            double distanceFromAcRoot = arrival.Length;
            Assert.That(distanceFromAcRoot < ac.GateTo(sol).PositionKm.Length, Is.True, "arrival should be closer to the destination's root than its gate");
            Assert.That(distanceFromAcRoot, Is.EqualTo(ac.GateTo(sol).PositionKm.Length - JumpGate.ArrivalInsetKm).Within(1e-3));

            // Facing back toward the destination's own root (into the system, not out past the gate).
            double facingInward = DVec3.Dot(sol.GateTo(ac).ArrivalForward, -ac.GateTo(sol).PositionKm.Normalized);
            Assert.That(facingInward, Is.InRange(0.9999, 1.0001));
        }

        [Test]
        public void TransitTakesTheSameHullOutOfTheOriginFleetAndIntoTheDestinationFleet()
        {
            var g = Create();
            var sol = g.Systems[0];
            var ac = g.Systems[1];
            var ship = sol.World.Fleet.Ships.First(s => !s.Destroyed && !s.IsEscort);
            int originId = ship.Id;
            string name = ship.Name;
            double credits = ship.Credits = 1234;
            int acCountBefore = ac.World.Fleet.Ships.Count;
            int solCountBefore = sol.World.Fleet.Ships.Count;

            bool ok = GateTransit.TryPerform(sol.GateTo(ac), sol.GateTo(ac).PositionKm, ship, sol.World, StartJd, out var arrived);

            Assert.That(ok, Is.True);
            // The same object comes out the far side (U.SPC-T453), never a copy.
            Assert.That(arrived, Is.SameAs(ship));
            // Its old slot in sol's fleet keeps a gone marker at the same id (ids are fixed indices -- see
            // Fleet.LetGo), so no other id there moves; the hull itself is no longer in that list.
            Assert.That(sol.World.Fleet.Ships, Does.Not.Contain(ship));
            Assert.That(sol.World.Fleet.Ships[originId].Name, Is.EqualTo(name));
            Assert.That(sol.World.Fleet.Ships[originId].Destroyed, Is.True);
            Assert.That(sol.World.Fleet.Ships[originId].GateDeparted, Is.True);
            Assert.That(sol.World.Fleet.Ships.Count, Is.EqualTo(solCountBefore));

            Assert.That(ac.World.Fleet.Ships, Does.Contain(arrived));
            Assert.That(ac.World.Fleet.Ships.Count, Is.EqualTo(acCountBefore + 1));
            Assert.That(arrived.Name, Is.EqualTo(name));
            Assert.That(arrived.Credits, Is.EqualTo(credits));
            Assert.That(arrived.Destroyed, Is.False);
            Assert.That(arrived.GateDeparted, Is.False);
            Assert.That(arrived.Role, Is.EqualTo(ShipRole.Free));
            Assert.That(arrived.Orbit.Body, Is.EqualTo(ac.Catalog.Root.Index));
            Assert.That((sol.GateTo(ac).ArrivalOffsetKm - arrived.Orbit.Center).Length < 1e-6, Is.True, "the ship should land exactly at the arrival offset the activated gate computes");
            Assert.That(arrived.Ports, Is.Not.Null);
            Assert.That(arrived.Ports, Is.Not.Empty);
        }

        [Test]
        public void ACompanyHullPilotedThroughTheGateStaysInItsCompany()
        {
            var g = Create();
            var sol = g.Systems[0];
            var ac = g.Systems[1];
            var ship = sol.World.Fleet.Ships.First(s => !s.Destroyed && !s.IsEscort && s.Company != null && s.Company.Trading);
            var company = ship.Company;
            bool announced = false;
            ac.World.ShipAdded += s => announced |= s.Name == ship.Name;

            Assert.That(GateTransit.TryPerform(sol.GateTo(ac), sol.GateTo(ac).PositionKm, ship, sol.World, StartJd, out var arrived), Is.True);

            Assert.That(announced, Is.True, "the far system was never told a ship came in");
            Assert.That(arrived.Company, Is.SameAs(company));
            Assert.That(company.Ships, Does.Contain(arrived), "the hull dropped out of its company");
        }

        [Test]
        public void ACargoCarriedThroughTheGateKeepsNoBookingWithTheSystemItLeft()
        {
            var g = Create();
            var sol = g.Systems[0];
            var ac = g.Systems[1];
            var ship = sol.World.Fleet.Ships.First(s => !s.Destroyed && !s.IsEscort);
            var booked = sol.World.Economy.Settlements[0];
            ship.HasCargo = true;
            ship.CargoGood = Good.Food;
            ship.CargoTonnes = 100;
            ship.CargoDestination = booked;
            ship.CargoBonus = 5000;

            Assert.That(GateTransit.TryPerform(sol.GateTo(ac), sol.GateTo(ac).PositionKm, ship, sol.World, StartJd, out var arrived), Is.True);

            Assert.That(arrived.HasCargo, Is.True, "the goods themselves come along");
            Assert.That(arrived.CargoTonnes, Is.EqualTo(100));
            Assert.That(arrived.CargoDestination, Is.Null, "a market in the other system can't be delivered to from here");
            Assert.That(arrived.CargoBonus, Is.EqualTo(0));
            double before = booked.Inbound[(int)Good.Food];
            ac.World.Economy.RecountInbound(ac.World.Fleet.Ships);
            Assert.That(booked.Inbound[(int)Good.Food], Is.EqualTo(before), "the far system's books reached into this one");
        }

        [Test]
        public void AShipReleasedAtABinarysBarycentreFliesOnWithoutNaN()
        {
            var g = Create();
            var ac = g.Systems[1];
            var fleet = ac.World.Fleet;
            int root = ac.Catalog.Root.Index;
            Assert.That(ac.Catalog.Root.RadiusKm, Is.EqualTo(0));
            // Where a gate arrival (or a spawn out in the open) is pinned when the player lets go.
            var ships = Enumerable.Range(0, 12)
                .Select(i => fleet.SpawnAt("Free " + i, ShipClass.Courier, ac.World.Independents, root, new DVec3(4850 + i, 0, 0), DVec3.UnitX, StartJd)).ToList();
            for (double jd = StartJd; jd < StartJd + 3; jd += 0.05)
            {
                ac.Ephemeris.Update(jd);
                fleet.Update(jd);
            }
            foreach (var s in fleet.Ships.Where(s => !s.Destroyed))
            {
                var p = fleet.StateOf(s, StartJd + 3).HelioKm;
                Assert.That(double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsNaN(p.Z), Is.False, s.Name + " is at NaN");
            }
        }

        [Test]
        public void AFreeShipInASystemWithNoWorldsHoldsInsteadOfCrashing()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var fleet = new Fleet(eph, 3);
            var f = fleet.AddFaction("Wanderers", "WN", new Rgb(1, 1, 1));
            var ship = fleet.SpawnAt("Lonely", ShipClass.Courier, f, 0, new DVec3(2e6, 0, 0), DVec3.UnitX, StartJd);
            ship.Ports = new int[0]; // as in Luhman 16 or Sirius: no planet or station to visit
            for (int i = 0; i < 20; i++) Assert.DoesNotThrow(() => fleet.ScheduleNext(ship, StartJd + i));
            Assert.That(ship.DestinationBody, Is.EqualTo(0));
        }

        [Test]
        public void AShipArrivingThroughTheGateDoesNotKnowTheWayToAHiddenHaven()
        {
            var g = Create();
            var sol = g.Systems[0];
            var ac = g.Systems[1];
            var hidden = sol.World.Economy.Settlements.Where(s => s.Hidden).Select(s => s.Body).ToList();
            Assert.That(hidden, Is.Not.Empty);
            var ship = ac.World.Fleet.Ships.First(s => !s.Destroyed && !s.IsEscort);
            Assert.That(GateTransit.TryPerform(ac.GateTo(sol), ac.GateTo(sol).PositionKm, ship, ac.World, StartJd, out var arrived), Is.True);
            Assert.That(arrived.Ports.Intersect(hidden), Is.Empty);
        }

        [Test]
        public void TransitFailsWhenTheShipIsOutOfRange()
        {
            var g = Create();
            var sol = g.Systems[0];
            var ac = g.Systems[1];
            var ship = sol.World.Fleet.Ships.First(s => !s.Destroyed && !s.IsEscort);
            int solBefore = sol.World.Fleet.Ships.Count;
            int acBefore = ac.World.Fleet.Ships.Count;

            bool ok = GateTransit.TryPerform(sol.GateTo(ac), DVec3.Zero, ship, sol.World, StartJd, out var arrived);

            Assert.That(ok, Is.False);
            Assert.That(arrived, Is.Null);
            Assert.That(ship.Destroyed, Is.False);
            Assert.That(sol.World.Fleet.Ships.Count, Is.EqualTo(solBefore));
            Assert.That(ac.World.Fleet.Ships.Count, Is.EqualTo(acBefore));
        }

        [Test]
        public void ScheduleNextOnAShipInFlightRetargetsWithoutCuttingItsTransitShort()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var fleet = Fleet.CreateDefault(eph, StartJd);
            var s = fleet.Ships.First(x => !x.IsEscort && x.Phase == ShipPhase.Transit && x.PhaseEndJd - StartJd > 2 && StartJd - x.PhaseStartJd > 0.5);
            double end = s.PhaseEndJd;
            var before = fleet.StateOf(s, StartJd).HelioKm;
            s.GateKm = new DVec3(40 * Astro.AuKm, 0, 0);
            fleet.ScheduleNext(s, StartJd);
            Assert.That(s.PhaseEndJd, Is.EqualTo(end));
            Assert.That(s.DestinationBody, Is.EqualTo(0));
            Assert.That((fleet.StateOf(s, StartJd).HelioKm - before).Length, Is.LessThan(1.0));
        }
    }
}
