using System;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// U.SPC-T330: the asteroid-belt mining pipeline (<see cref="BeltMining"/>): rocks captured, ground to dust,
    /// separated into elements, shipped to warehouses and drawn by megastructure builds; convoys robbed on the way
    /// far more often than warehouses are raided.
    /// </summary>
    [TestFixture]
    public class BeltMiningTests
    {
        const double StartJd = 2461324.0;

        static Faction Builder() => new Faction("Belt Consortium", "BCS", default);

        static (BeltMining mining, MiningOperation op, MaterialWarehouse depot) Pipeline(double pressure = 0, int seed = 7)
        {
            var mining = new BeltMining(seed) { PiratePressure = () => pressure };
            var owner = Builder();
            var depot = mining.AddWarehouse("Valhalla Depot", owner, 2);
            var op = mining.AddOperation("Ceres Belt Works", owner, 1, depot);
            return (mining, op, depot);
        }

        [Test]
        public void TugsCaptureRocksThatAreGroundToDustAndSeparatedIntoElements()
        {
            var (mining, op, _) = Pipeline();
            mining.Update(StartJd);
            mining.Update(StartJd + 60);

            Assert.That(op.RocksCaptured, Is.GreaterThan(0), "no rocks brought in");
            Assert.That(op.TonnesGround, Is.GreaterThan(0), "nothing ground");
            Assert.That(op.TonnesSeparated, Is.GreaterThan(0), "nothing separated");
            // Mass is kept: what came in is ground, waiting, or still in the backlog; what was ground is dust, elements or slag.
            double backlog = op.Backlog.Sum(r => r.Remaining);
            Assert.That(op.TonnesGround + backlog, Is.EqualTo(op.TonnesCaptured).Within(1e-6 * op.TonnesCaptured));
            Assert.That(op.Dust + op.TonnesSeparated + op.Slag, Is.EqualTo(op.TonnesGround).Within(1e-6 * op.TonnesGround));
            // The grinders are the bottleneck, never run past their rate.
            Assert.That(op.TonnesGround, Is.LessThanOrEqualTo(op.GrindTonnesPerDay * 61 + 1e-6));
            // Every rock is mostly slag: only a minority of its mass is recoverable element.
            Assert.That(op.TonnesSeparated, Is.LessThan(op.TonnesGround));
        }

        [Test]
        public void ElementsComeOutInProportionToTheRocksTheyCameFrom()
        {
            var (mining, op, _) = Pipeline();
            op.Destination = null; // keep everything at the refinery to count it
            mining.Update(StartJd);
            mining.Update(StartJd + 400);

            double nickel = op.Separated[(int)Good.NickelIron], water = op.Separated[(int)Good.Water], platinum = op.Separated[(int)Good.Platinum];
            Assert.That(nickel, Is.GreaterThan(0));
            Assert.That(water, Is.GreaterThan(0), "carbonaceous rocks carry water");
            Assert.That(platinum, Is.GreaterThan(0));
            Assert.That(platinum, Is.LessThan(nickel * 1e-3), "platinum-group metals are trace, not bulk");
            Assert.That(op.Separated[(int)Good.Electronics], Is.EqualTo(0), "separation yields elements, not manufactures");
        }

        [Test]
        public void ConvoysCarryTheElementsToTheWarehouse()
        {
            var (mining, op, depot) = Pipeline();
            mining.Update(StartJd);
            mining.Update(StartJd + 120);

            Assert.That(op.ConvoysSent, Is.GreaterThan(0));
            Assert.That(mining.ConvoysDelivered, Is.GreaterThan(0));
            Assert.That(depot.StoredTonnes, Is.GreaterThan(0));
            Assert.That(depot.Stock[(int)Good.NickelIron], Is.GreaterThan(0));
            Assert.That(mining.ConvoysStolen, Is.EqualTo(0), "no raiders about, nothing taken");
            foreach (var c in mining.Convoys.Where(c => c.State == ConvoyState.EnRoute))
                Assert.That(c.Tonnes, Is.LessThanOrEqualTo(op.ConvoyTonnes + 1e-6));
        }

        [Test]
        public void ConvoyTripsTakeTheDistanceBetweenRefineryAndWarehouse()
        {
            var mining = new BeltMining(1) { Position = (body, jd) => new DVec3(body == 1 ? 2.8 * Astro.AuKm : 5.2 * Astro.AuKm, 0, 0) };
            Assert.That(mining.TransitDays(1, 2, StartJd), Is.EqualTo(2.4 / BeltMining.ConvoyAuPerDay).Within(1e-9));
            Assert.That(mining.TransitDays(1, 1, StartJd), Is.EqualTo(BeltMining.MinTransitDays));
        }

        [Test]
        public void MegastructuresAreBuiltFromWhatTheWarehousesHold()
        {
            var (mining, _, depot) = Pipeline();
            var p = mining.Begin(MegastructureSpec.Shipyard, depot.Owner, 3, "Ceres Deep Yard", StartJd);
            MegastructureProject finished = null;
            mining.ProjectCompleted += x => finished = x;
            MegastructureProject yard = null;
            mining.YardBuilt = (x, jd) => yard = x;

            mining.Update(StartJd);
            mining.Update(StartJd + 60);
            Assert.That(p.Progress, Is.GreaterThan(0).And.LessThan(1), "under way after two months");
            Assert.That(p.Used[(int)Good.NickelIron], Is.GreaterThan(0));

            mining.Update(StartJd + 1200);
            Assert.That(p.Done, Is.True, "finished in a little over three years");
            Assert.That(p.Progress, Is.EqualTo(1).Within(1e-9));
            Assert.That(finished, Is.SameAs(p));
            Assert.That(yard, Is.SameAs(p), "a finished shipyard is handed to its world to open");
            Assert.That(p.DoneJd - p.StartJd, Is.GreaterThanOrEqualTo(MegastructureSpec.Shipyard.MinDays - 1), "never faster than its kind allows");
        }

        [Test]
        public void ABuildDrawsOnlyFromItsOwnBuildersWarehouses()
        {
            var (mining, _, depot) = Pipeline();
            var rival = new Faction("Rival", "RV", default);
            var p = mining.Begin(MegastructureSpec.Shipyard, rival, 3, "Rival Yard", StartJd);
            mining.Update(StartJd);
            mining.Update(StartJd + 120);
            Assert.That(depot.StoredTonnes, Is.GreaterThan(0));
            Assert.That(p.Progress, Is.EqualTo(0));
        }

        [Test]
        public void AFinishedDysonRingLaysDownTheNext()
        {
            var mining = new BeltMining(3);
            var owner = Builder();
            var depot = mining.AddWarehouse("Depot", owner, 2);
            for (int g = 0; g < GoodInfo.Count; g++) depot.Stock[g] = 1e9;
            var ring = mining.Begin(MegastructureSpec.DysonRing, owner, 0, "Helios Swarm", StartJd);
            mining.Update(StartJd);
            mining.Update(StartJd + MegastructureSpec.DysonRing.MinDays + 2);

            Assert.That(ring.Done, Is.True);
            var next = mining.Projects.Single(x => x != ring);
            Assert.That(next.Spec, Is.SameAs(MegastructureSpec.DysonRing));
            Assert.That(next.Stage, Is.EqualTo(2));
            Assert.That(next.Done, Is.False);
            Assert.That(next.Name, Does.Contain("Helios Swarm"));
        }

        [Test]
        public void ConvoysOnTheWayAreRobbedFarMoreOftenThanWarehousesAreRaided()
        {
            int stolen = 0, raids = 0;
            for (int seed = 1; seed <= 8; seed++)
            {
                var (mining, _, _) = Pipeline(pressure: 1, seed: seed);
                // Ceres to Callisto: a long haul out to Jupiter.
                mining.Position = (body, jd) => new DVec3(body == 1 ? 2.8 * Astro.AuKm : -5.2 * Astro.AuKm, 0, 0);
                mining.Update(StartJd);
                mining.Update(StartJd + 3 * 365);
                stolen += mining.ConvoysStolen;
                raids += mining.WarehouseRaids;
                Assert.That(mining.ConvoysDelivered, Is.GreaterThan(mining.ConvoysStolen), "most convoys still get through");
            }
            TestContext.Out.WriteLine($"convoys stolen {stolen}, warehouse raids {raids}");
            Assert.That(stolen, Is.GreaterThan(0));
            Assert.That(stolen, Is.GreaterThan(3 * raids), "theft on the way is the common loss, a warehouse raid the rare one");
        }

        [Test]
        public void StolenAndRaidedMaterialGoesToTheRaiders()
        {
            var raiders = new Faction("Black Banner", "", default) { IsPirate = true };
            var (mining, _, depot) = Pipeline(pressure: 1, seed: 5);
            mining.Raiders = () => raiders;
            mining.Position = (body, jd) => new DVec3(body == 1 ? 2.8 * Astro.AuKm : -5.2 * Astro.AuKm, 0, 0);
            MaterialConvoy taken = null;
            mining.ConvoyStolen += c => taken ??= c;
            mining.Update(StartJd);
            mining.Update(StartJd + 3 * 365);

            Assert.That(taken, Is.Not.Null, "three years of full-pressure raiding took nothing");
            Assert.That(taken.State, Is.EqualTo(ConvoyState.Stolen));
            Assert.That(taken.TakenBy, Is.SameAs(raiders));
            double lost = mining.Convoys.Where(c => c.State == ConvoyState.Stolen).Sum(c => c.Tonnes) + depot.TonnesLostToRaids;
            Assert.That(mining.Plundered.Sum(), Is.GreaterThanOrEqualTo(lost - 1e-6));
        }

        [Test]
        public void EscortsAndGuardsCutTheRisk()
        {
            int bare = 0, escorted = 0, unguarded = 0, guarded = 0;
            for (int seed = 1; seed <= 6; seed++)
            {
                var a = Pipeline(pressure: 1, seed: seed);
                var b = Pipeline(pressure: 1, seed: seed);
                b.op.EscortShare = 1;
                b.mining.Guarded = w => true;
                foreach (var m in new[] { a.mining, b.mining })
                {
                    m.Position = (body, jd) => new DVec3(body == 1 ? 2.8 * Astro.AuKm : -5.2 * Astro.AuKm, 0, 0);
                    m.Update(StartJd);
                    m.Update(StartJd + 3 * 365);
                }
                bare += a.mining.ConvoysStolen;
                escorted += b.mining.ConvoysStolen;
                unguarded += a.mining.WarehouseRaids;
                guarded += b.mining.WarehouseRaids;
            }
            TestContext.Out.WriteLine($"stolen bare {bare}, escorted {escorted}; raids unguarded {unguarded}, guarded {guarded}");
            Assert.That(escorted, Is.LessThan(bare));
            Assert.That(guarded, Is.LessThanOrEqualTo(unguarded));
        }

        [Test]
        public void SolsBeltWorksRunInTheDefaultWorldAndSurviveASave()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            double jd = StartJd;
            eph.Update(jd);
            var w = World.CreateDefault(eph, jd);
            var m = w.Mining;
            Assert.That(m, Is.Not.Null);
            Assert.That(m.Operations.Single().Owner, Is.SameAs(w.Belt));
            Assert.That(m.Warehouses.Single().Body, Is.EqualTo(w.Catalog["Callisto"].Index), "Sol's depot is out on a remote moon");
            Assert.That(m.Projects.Select(p => p.Spec), Is.EquivalentTo(new[] { MegastructureSpec.SpaceElevator, MegastructureSpec.DysonRing }));

            for (int d = 1; d <= 150; d++)
            {
                eph.Update(jd + d);
                w.Update(jd + d);
            }
            var op = m.Operations[0];
            Assert.That(op.TonnesSeparated, Is.GreaterThan(0));
            Assert.That(op.ConvoysSent, Is.GreaterThan(0));
            // Ceres to Callisto is a trip of weeks at the least.
            var first = m.Convoys.First();
            Assert.That(first.ArriveJd - first.DepartJd, Is.GreaterThan(10));
            Assert.That(m.ConvoysDelivered + m.ConvoysStolen, Is.GreaterThan(0));

            var copy = ObjectGraph.Clone(w);
            Assert.That(copy.Mining, Is.Not.SameAs(m));
            Assert.That(copy.Mining.Operations[0].TonnesSeparated, Is.EqualTo(op.TonnesSeparated));
            Assert.That(copy.Mining.Projects[0].Spec, Is.SameAs(m.Projects[0].Spec), "megastructure kinds are the shared table entries");
            Assert.That(copy.Mining.PiratePressure.Target, Is.SameAs(copy), "the copy's pipeline reads the copy's raiders");
        }
    }
}
