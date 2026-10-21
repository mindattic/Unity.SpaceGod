using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// U.SPC-T372: a jump-gate link is never built longer than <see cref="GateNetwork.MaxLinkLy"/>, and the gaps that
    /// leaves between real stars are bridged by Void Bases -- relays with no star or planet, built in open space
    /// purely to carry a crossing the rest of the way.
    /// </summary>
    [TestFixture]
    public class VoidBaseTests
    {
        const double StartJd = 2461324.0;

        [Test]
        public void RelayPositionsSpaceOutNoFartherThanTheCap()
        {
            var a = new DVec3(0, 0, 0);
            var b = new DVec3(17, 0, 0);
            var relays = GateNetwork.RelayPositions(a, b, 6);
            Assert.That(relays.Count, Is.EqualTo(2), "17 ly at a 6 ly cap needs three hops, so two relays");
            var chain = new System.Collections.Generic.List<DVec3> { a };
            chain.AddRange(relays);
            chain.Add(b);
            for (int i = 1; i < chain.Count; i++)
                Assert.That((chain[i] - chain[i - 1]).Length, Is.LessThanOrEqualTo(6 + 1e-9));

            Assert.That(GateNetwork.RelayPositions(a, new DVec3(5, 0, 0), 6), Is.Empty, "already within one gate's reach");
            Assert.That(GateNetwork.RelayPositions(a, new DVec3(6, 0, 0), 6), Is.Empty, "exactly at the cap needs no relay");
        }

        [Test]
        public void ABuiltLatticeNeverLinksTwoSystemsFartherApartThanTheCap()
        {
            var g = Galaxy.CreateDefault(StartJd);
            foreach (var link in g.Network.Links)
                Assert.That(link.Ly, Is.LessThanOrEqualTo(GateNetwork.MaxLinkLy).Within(1e-6), link.ToString());
        }

        [Test]
        public void EpsilonIndiIsTooFarForADirectGateAndIsBridgedByAVoidBase()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var indi = g.Find("Epsilon Indi");
            Assert.That(indi, Is.Not.Null);
            // Its nearest real neighbour is farther than one gate reaches.
            double nearestReal = g.Systems.Where(s => s != indi && !s.IsVoidBase).Min(s => Galaxy.DistanceLy(indi, s));
            Assert.That(nearestReal, Is.GreaterThan(GateNetwork.MaxLinkLy));

            var voidBases = g.Systems.Where(s => s.IsVoidBase).ToList();
            Assert.That(voidBases, Is.Not.Empty, "nothing was built to bridge the gap");
            Assert.That(indi.Gates.Any(gate => gate.Destination.IsVoidBase), Is.True, "Epsilon Indi's gate should lead to a relay, not straight to a real star");

            var route = g.Network.Route(g.Find("Sol System"), indi);
            Assert.That(route, Is.Not.Null);
            Assert.That(route.Count, Is.GreaterThan(1), "Sol to Epsilon Indi should pass through at least one other hop");
        }

        [Test]
        public void AVoidBaseHasNoRealStarOrPlanetAndIsNeverOpenToSettlement()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var relay = g.Systems.First(s => s.IsVoidBase);
            Assert.That(relay.Spec, Is.Null, "no real catalogue entry: nothing here is a real star or planet");
            foreach (var b in relay.Catalog.Bodies) Assert.That(b.Status, Is.EqualTo(BodyStatus.Fictional), b.Name);
            Assert.That(relay.OpenToSettlement, Is.False);
            Assert.That(relay.Settled, Is.False);
            Assert.That(relay.Holder, Is.Null, "no state ever comes to hold a Void Base");
        }

        [Test]
        public void TheGeneratorIsASingularityCircledByItsOwnTinySuns()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var relay = g.Systems.First(s => s.IsVoidBase);
            var root = relay.Catalog.Root;
            Assert.That(root.Singularity, Is.True, "the generator itself: a black hole in a bottle");
            Assert.That(root.Kind, Is.EqualTo(BodyKind.Star), "so its children lay out and frame like an ordinary star's");
            Assert.That(relay.Catalog.Count, Is.EqualTo(1 + VoidBaseBuilder.TinySuns));
            foreach (var b in relay.Catalog.Bodies)
            {
                if (b == root) continue;
                Assert.That(b.Kind, Is.EqualTo(BodyKind.Star), b.Name + " should be one of the tiny suns circling it");
                Assert.That(b.Singularity, Is.False, b.Name + " is an ordinary little sun, not the generator");
                Assert.That(b.ParentIndex, Is.EqualTo(root.Index));
            }
            // They actually orbit it: never all sitting on top of each other or of the generator.
            relay.Update(StartJd);
            var positions = relay.Catalog.Bodies.Where(b => b != root).Select(b => relay.Ephemeris.Relative(b.Index)).ToList();
            for (int i = 0; i < positions.Count; i++)
            {
                Assert.That(positions[i].Length, Is.GreaterThan(VoidBaseBuilder.RadiusKm), "clear of the generator itself");
                for (int j = i + 1; j < positions.Count; j++)
                    Assert.That((positions[i] - positions[j]).Length, Is.GreaterThan(1e-6), "spread round the ring, not stacked");
            }
        }

        [Test]
        public void AVoidBasesGatesAreUnguardedLikeAnyOtherEmptySystems()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var relay = g.Systems.First(s => s.IsVoidBase);
            foreach (var gate in relay.Gates)
            {
                Assert.That(gate.Owner, Is.SameAs(relay.World.Sol));
                Assert.That(gate.MaxForts, Is.EqualTo(0), "a Survey Service holds it, not a navy");
            }
        }
    }
}
