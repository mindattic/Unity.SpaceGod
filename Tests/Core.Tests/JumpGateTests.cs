using System;
using System.Linq;
using SolarSystem.Core;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
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

            Assert.That(sol.Gate, Is.Not.Null);
            Assert.That(ac.Gate, Is.Not.Null);
            Assert.That(sol.Gate.Destination, Is.SameAs(ac));
            Assert.That(ac.Gate.Destination, Is.SameAs(sol));
        }

        [Test]
        public void GatesSitAtTheSystemsOwnOuterEdgeFacingTheOtherSystem()
        {
            var g = Create();
            var sol = g.Systems[0];
            var ac = g.Systems[1];

            Assert.That(sol.Gate.PositionKm.Length, Is.EqualTo(JumpGate.RadiusKm).Within(1e-6));
            Assert.That(ac.Gate.PositionKm.Length, Is.EqualTo(JumpGate.RadiusKm).Within(1e-6));

            // Each gate points (in its own system's frame) toward the real direction of the other star.
            double towardAc = DVec3.Dot(sol.Gate.PositionKm.Normalized, Galaxy.Direction(sol, ac));
            double towardSol = DVec3.Dot(ac.Gate.PositionKm.Normalized, Galaxy.Direction(ac, sol));
            TestContext.Out.WriteLine($"Sol gate alignment {towardAc:0.000}, Centauri gate alignment {towardSol:0.000}");
            Assert.That(towardAc, Is.InRange(0.9999, 1.0001));
            Assert.That(towardSol, Is.InRange(0.9999, 1.0001));
        }

        [Test]
        public void OnlyInRangeOfItsOwnGateCanAShipActivateIt()
        {
            var g = Create();
            var sol = g.Systems[0];
            var gate = sol.Gate;

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

            var arrival = sol.Gate.ArrivalOffsetKm;
            double distanceFromAcRoot = arrival.Length;
            Assert.That(distanceFromAcRoot < ac.Gate.PositionKm.Length, Is.True, "arrival should be closer to the destination's root than its gate");
            Assert.That(distanceFromAcRoot, Is.EqualTo(JumpGate.RadiusKm - JumpGate.ArrivalInsetKm).Within(1e-6));

            // Facing back toward the destination's own root (into the system, not out past the gate).
            double facingInward = DVec3.Dot(sol.Gate.ArrivalForward, -ac.Gate.PositionKm.Normalized);
            Assert.That(facingInward, Is.InRange(0.9999, 1.0001));
        }

        [Test]
        public void TransitRetiresTheOriginHullAndAppendsAnArrivedCopyToTheDestinationFleet()
        {
            var g = Create();
            var sol = g.Systems[0];
            var ac = g.Systems[1];
            var ship = sol.World.Fleet.Ships.First(s => !s.Destroyed && !s.IsEscort);
            string name = ship.Name;
            double credits = ship.Credits = 1234;
            int acCountBefore = ac.World.Fleet.Ships.Count;
            int solCountBefore = sol.World.Fleet.Ships.Count;

            bool ok = GateTransit.TryPerform(sol.Gate, sol.Gate.PositionKm, ship, sol.World.Fleet, StartJd, out var arrived);

            Assert.That(ok, Is.True);
            Assert.That(arrived, Is.Not.Null);
            // The origin hull stays in its fleet at its own id (ids are fixed indices -- see Fleet.Receive),
            // just retired in place rather than actually removed.
            Assert.That(sol.World.Fleet.Ships, Does.Contain(ship));
            Assert.That(ship.Destroyed, Is.True);
            Assert.That(ship.GateDeparted, Is.True);
            Assert.That(sol.World.Fleet.Ships.Count, Is.EqualTo(solCountBefore));

            Assert.That(ac.World.Fleet.Ships, Does.Contain(arrived));
            Assert.That(ac.World.Fleet.Ships.Count, Is.EqualTo(acCountBefore + 1));
            Assert.That(arrived.Name, Is.EqualTo(name));
            Assert.That(arrived.Credits, Is.EqualTo(credits));
            Assert.That(arrived.Destroyed, Is.False);
            Assert.That(arrived.Role, Is.EqualTo(ShipRole.Free));
            Assert.That(arrived.Orbit.Body, Is.EqualTo(ac.Catalog.Root.Index));
            Assert.That((sol.Gate.ArrivalOffsetKm - arrived.Orbit.Center).Length < 1e-6, Is.True, "the ship should land exactly at the arrival offset the activated gate computes");
            Assert.That(arrived.Ports, Is.Not.Null);
            Assert.That(arrived.Ports, Is.Not.Empty);
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

            Assert.That(GateTransit.TryPerform(sol.Gate, sol.Gate.PositionKm, ship, sol.World.Fleet, StartJd, out var arrived), Is.True);

            Assert.That(arrived.HasCargo, Is.True, "the goods themselves come along");
            Assert.That(arrived.CargoTonnes, Is.EqualTo(100));
            Assert.That(arrived.CargoDestination, Is.Null, "a market in the other system can't be delivered to from here");
            Assert.That(arrived.CargoBonus, Is.EqualTo(0));
            double before = booked.Inbound[(int)Good.Food];
            ac.World.Economy.RecountInbound(ac.World.Fleet.Ships);
            Assert.That(booked.Inbound[(int)Good.Food], Is.EqualTo(before), "the far system's books reached into this one");
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

            bool ok = GateTransit.TryPerform(sol.Gate, DVec3.Zero, ship, sol.World.Fleet, StartJd, out var arrived);

            Assert.That(ok, Is.False);
            Assert.That(arrived, Is.Null);
            Assert.That(ship.Destroyed, Is.False);
            Assert.That(sol.World.Fleet.Ships.Count, Is.EqualTo(solBefore));
            Assert.That(ac.World.Fleet.Ships.Count, Is.EqualTo(acBefore));
        }
    }
}
