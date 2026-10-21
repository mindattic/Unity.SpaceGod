using System;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    [TestFixture]
    public class GateJumpTests
    {
        const double StartJd = 2461324.0;

        static Galaxy Create() => Galaxy.CreateDefault(StartJd);

        [Test]
        public void NeighboursAreCrossedByTheGateStraightThereAndOutOfItsPartner()
        {
            var g = Create();
            var sol = g.Find("Sol System");
            var ac = g.Find("Alpha Centauri");
            Assert.That(GateJump.TryChoose(g.Network, sol, ac, out var departure, out var arrival), Is.True);
            Assert.That(departure, Is.SameAs(sol.GateTo(ac)));
            Assert.That(arrival, Is.SameAs(ac.GateTo(sol)));
            Assert.That(arrival.Home, Is.SameAs(ac));
        }

        [Test]
        public void AFarSystemIsReachedByTheFirstGateOnTheRouteAndEnteredByTheLast()
        {
            var g = Create();
            var sol = g.Find("Sol System");
            var far = g.Systems.First(s => s != sol && sol.GateTo(s) == null);
            var route = g.Network.Route(sol, far);
            Assert.That(route.Count, Is.GreaterThan(1));
            Assert.That(GateJump.TryChoose(g.Network, sol, far, out var departure, out var arrival), Is.True);
            Assert.That(departure, Is.SameAs(route[0]));
            Assert.That(departure.Home, Is.SameAs(sol));
            Assert.That(arrival, Is.SameAs(route[route.Count - 1].Partner));
            Assert.That(arrival.Home, Is.SameAs(far));
        }

        [Test]
        public void ASystemWithNoGatesKeepsTheOldWayAcross()
        {
            var g = Create();
            var lone = StarSystem.CreateSol(StartJd);
            Assert.That(lone.Gates, Is.Empty);
            Assert.That(GateJump.TryChoose(g.Network, lone, g.Find("Alpha Centauri"), out var departure, out var arrival), Is.False);
            Assert.That(departure, Is.Null);
            Assert.That(arrival, Is.Null);
            var sol = g.Find("Sol System");
            Assert.That(GateJump.TryChoose(g.Network, sol, sol, out _, out _), Is.False);
        }

        [Test]
        public void TheTripIsNoLongerThanTheOldWayAcross()
        {
            Assert.That(GateJump.TotalSeconds, Is.LessThanOrEqualTo(9.4));
            Assert.That(GateJump.Overall(GateJump.Phase.Approach, 0), Is.EqualTo(0));
            Assert.That(GateJump.Overall(GateJump.Phase.Emerge, 1), Is.EqualTo(1).Within(1e-12));
            double last = -1;
            foreach (GateJump.Phase p in Enum.GetValues(typeof(GateJump.Phase)))
                for (double t = 0; t <= 1; t += 0.05)
                {
                    double o = GateJump.Overall(p, t);
                    Assert.That(o, Is.GreaterThanOrEqualTo(last - 1e-12));
                    last = o;
                }
        }

        [Test]
        public void ThePlungeGathersSpeedAllTheWayToTheRing()
        {
            Assert.That(GateJump.PlungeRemaining(0), Is.EqualTo(1));
            Assert.That(GateJump.PlungeRemaining(1), Is.EqualTo(0));
            double prevStep = 0;
            for (int i = 1; i <= 20; i++)
            {
                double step = GateJump.PlungeRemaining((i - 1) / 20.0) - GateJump.PlungeRemaining(i / 20.0);
                Assert.That(step, Is.GreaterThan(prevStep));
                prevStep = step;
            }
        }

        [Test]
        public void TheEffectsCarryOverEachChangeOfPhaseWithoutAJump()
        {
            var phases = (GateJump.Phase[])Enum.GetValues(typeof(GateJump.Phase));
            for (int i = 0; i + 1 < phases.Length; i++)
            {
                var a = phases[i];
                var b = phases[i + 1];
                Assert.That(GateJump.Warp(a, 1), Is.EqualTo(GateJump.Warp(b, 0)).Within(1e-9), "warp " + a);
                Assert.That(GateJump.Flash(a, 1), Is.EqualTo(GateJump.Flash(b, 0)).Within(1e-9), "flash " + a);
                Assert.That(GateJump.FovKick(a, 1), Is.EqualTo(GateJump.FovKick(b, 0)).Within(1e-9), "fov " + a);
            }
            // The flash is full exactly at each ring, where the view cuts into and out of the gate.
            Assert.That(GateJump.Flash(GateJump.Phase.Plunge, 1), Is.EqualTo(1));
            Assert.That(GateJump.Flash(GateJump.Phase.Emerge, 0), Is.EqualTo(1));
            // Calm at both ends of the trip.
            Assert.That(GateJump.Warp(GateJump.Phase.Approach, 0), Is.EqualTo(0));
            Assert.That(GateJump.Warp(GateJump.Phase.Emerge, 1), Is.EqualTo(0));
            Assert.That(GateJump.Flash(GateJump.Phase.Emerge, 1), Is.EqualTo(0));
            Assert.That(GateJump.FovKick(GateJump.Phase.Emerge, 1), Is.EqualTo(0));
            // The destination is built while the tunnel hides both systems, not in the flash at either ring.
            Assert.That(GateJump.Flash(GateJump.Phase.Tunnel, GateJump.BuildAt), Is.EqualTo(0));
        }
    }
}
