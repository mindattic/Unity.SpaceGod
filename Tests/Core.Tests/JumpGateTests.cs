using System;
using System.Linq;
using SolarSystem.Core;
using Xunit;
using Xunit.Abstractions;

namespace SolarSystem.Core.Tests
{
    public class JumpGateTests
    {
        const double StartJd = 2461324.0;
        readonly ITestOutputHelper output;

        public JumpGateTests(ITestOutputHelper output) => this.output = output;

        static Galaxy Create() => Galaxy.CreateDefault(StartJd);

        [Fact]
        public void EachSystemGetsAGatePairedWithTheOther()
        {
            var g = Create();
            var sol = g.Systems[0];
            var ac = g.Systems[1];

            Assert.NotNull(sol.Gate);
            Assert.NotNull(ac.Gate);
            Assert.Same(ac, sol.Gate.Destination);
            Assert.Same(sol, ac.Gate.Destination);
        }

        [Fact]
        public void GatesSitAtTheSystemsOwnOuterEdgeFacingTheOtherSystem()
        {
            var g = Create();
            var sol = g.Systems[0];
            var ac = g.Systems[1];

            Assert.Equal(JumpGate.RadiusKm, sol.Gate.PositionKm.Length, 6);
            Assert.Equal(JumpGate.RadiusKm, ac.Gate.PositionKm.Length, 6);

            // Each gate points (in its own system's frame) toward the real direction of the other star.
            double towardAc = DVec3.Dot(sol.Gate.PositionKm.Normalized, Galaxy.Direction(sol, ac));
            double towardSol = DVec3.Dot(ac.Gate.PositionKm.Normalized, Galaxy.Direction(ac, sol));
            output.WriteLine($"Sol gate alignment {towardAc:0.000}, Centauri gate alignment {towardSol:0.000}");
            Assert.InRange(towardAc, 0.9999, 1.0001);
            Assert.InRange(towardSol, 0.9999, 1.0001);
        }

        [Fact]
        public void OnlyInRangeOfItsOwnGateCanAShipActivateIt()
        {
            var g = Create();
            var sol = g.Systems[0];
            var gate = sol.Gate;

            Assert.True(gate.CanActivate(gate.PositionKm));
            var justInside = gate.PositionKm + new DVec3(JumpGate.ActivationRangeKm * 0.5, 0, 0);
            var farAway = gate.PositionKm + new DVec3(JumpGate.ActivationRangeKm * 50, 0, 0);
            Assert.True(gate.CanActivate(justInside));
            Assert.False(gate.CanActivate(farAway));
            Assert.False(gate.CanActivate(DVec3.Zero));
        }

        [Fact]
        public void ArrivalLandsJustInsideTheFarGateFacingItsSystemsCentre()
        {
            var g = Create();
            var sol = g.Systems[0];
            var ac = g.Systems[1];

            var arrival = sol.Gate.ArrivalOffsetKm;
            double distanceFromAcRoot = arrival.Length;
            Assert.True(distanceFromAcRoot < ac.Gate.PositionKm.Length, "arrival should be closer to the destination's root than its gate");
            Assert.Equal(JumpGate.RadiusKm - JumpGate.ArrivalInsetKm, distanceFromAcRoot, 6);

            // Facing back toward the destination's own root (into the system, not out past the gate).
            double facingInward = DVec3.Dot(sol.Gate.ArrivalForward, -ac.Gate.PositionKm.Normalized);
            Assert.InRange(facingInward, 0.9999, 1.0001);
        }

        [Fact]
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

            Assert.True(ok);
            Assert.NotNull(arrived);
            // The origin hull stays in its fleet at its own id (ids are fixed indices -- see Fleet.Receive),
            // just retired in place rather than actually removed.
            Assert.Contains(ship, sol.World.Fleet.Ships);
            Assert.True(ship.Destroyed);
            Assert.True(ship.GateDeparted);
            Assert.Equal(solCountBefore, sol.World.Fleet.Ships.Count);

            Assert.Contains(arrived, ac.World.Fleet.Ships);
            Assert.Equal(acCountBefore + 1, ac.World.Fleet.Ships.Count);
            Assert.Equal(name, arrived.Name);
            Assert.Equal(credits, arrived.Credits);
            Assert.False(arrived.Destroyed);
            Assert.Equal(ShipRole.Free, arrived.Role);
            Assert.Equal(ac.Catalog.Root.Index, arrived.Orbit.Body);
            Assert.True((sol.Gate.ArrivalOffsetKm - arrived.Orbit.Center).Length < 1e-6, "the ship should land exactly at the arrival offset the activated gate computes");
            Assert.NotNull(arrived.Ports);
            Assert.NotEmpty(arrived.Ports);
        }

        [Fact]
        public void TransitFailsWhenTheShipIsOutOfRange()
        {
            var g = Create();
            var sol = g.Systems[0];
            var ac = g.Systems[1];
            var ship = sol.World.Fleet.Ships.First(s => !s.Destroyed && !s.IsEscort);
            int solBefore = sol.World.Fleet.Ships.Count;
            int acBefore = ac.World.Fleet.Ships.Count;

            bool ok = GateTransit.TryPerform(sol.Gate, DVec3.Zero, ship, sol.World.Fleet, StartJd, out var arrived);

            Assert.False(ok);
            Assert.Null(arrived);
            Assert.False(ship.Destroyed);
            Assert.Equal(solBefore, sol.World.Fleet.Ships.Count);
            Assert.Equal(acBefore, ac.World.Fleet.Ships.Count);
        }
    }
}
