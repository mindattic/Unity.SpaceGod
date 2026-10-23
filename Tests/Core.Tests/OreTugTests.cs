using System;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// U.SPC-T483: the ore tug, a big engine with claws that latches onto a captured asteroid or a block of comet ice and
    /// pushes it slowly (load mass governs its acceleration) to a refinery or a depot, feeding the belt mining pipeline
    /// (<see cref="BeltMining"/>); big loads need a heavy lane (<see cref="Lane.Heavy"/>), and tugs and their loads are
    /// raided like the pipeline's convoys.
    /// </summary>
    [TestFixture]
    public class OreTugTests
    {
        const double StartJd = 2461324.0;

        static (BeltMining mining, MiningOperation op, MaterialWarehouse depot) TugWorks(double pressure = 0, int seed = 7)
        {
            var mining = new BeltMining(seed) { PiratePressure = () => pressure };
            var owner = new Faction("Belt Consortium", "BCS", default);
            var depot = mining.AddWarehouse("Valhalla Depot", owner, 2);
            var op = mining.AddOperation("Ceres Belt Works", owner, 1, depot);
            op.CaptureTugs = 0; // only the ore tugs bring anything in
            op.OreTugs = 2;
            return (mining, op, depot);
        }

        static void Run(BeltMining mining, double days)
        {
            mining.Update(StartJd);
            mining.Update(StartJd + days);
        }

        /// <summary>Ceres (1) at 2.8 AU, Callisto's depot (2) out at Jupiter on the far side.</summary>
        static DVec3 Apart(int body, double jd) => new DVec3(body == 1 ? 2.8 * Astro.AuKm : -5.2 * Astro.AuKm, 0, 0);

        static HeavyLanePath? CeresJupiter(int from, int to, double jd) => new HeavyLanePath(3.7e8, 4e6, "Ceres–Jupiter");

        [Test]
        public void TheOreTugIsABigEngineWithClawsNotAHold()
        {
            var spec = ShipClassSpec.Of(ShipClass.OreTug);
            Assert.That(spec.Class, Is.EqualTo(ShipClass.OreTug));
            Assert.That(spec.DisplayName, Is.EqualTo("Ore tug"));
            Assert.That(spec.IsMilitary, Is.False);

            var design = CombatSpec.Of(ShipClass.OreTug);
            Assert.That(design.DesignName, Is.EqualTo("Ore tug"));
            Assert.That(design.Crew, Is.GreaterThan(0).And.LessThan(CombatSpec.Of(ShipClass.Miner).Crew), "a skeleton crew");
            Assert.That(design.Layout.Count(s => s.Kind == SubsystemKind.Engine), Is.GreaterThan(CombatSpec.Of(ShipClass.Miner).Layout.Count(s => s.Kind == SubsystemKind.Engine)),
                "more engine than an ore hauler");
            Assert.That(design.Layout.Any(s => s.IsWeapon), Is.False, "no guns");

            var frame = HullFrames.ForClass(ShipClass.OreTug);
            Assert.That(frame, Is.Not.Null);
            Assert.That(frame.Category, Is.EqualTo(HullCategory.Freighter));
            Assert.That(Economy.HullCost(ShipClass.OreTug), Is.GreaterThan(0));
            Assert.That(Economy.HullPrice(ShipClass.OreTug), Is.EqualTo(Economy.HullCost(ShipClass.OreTug) * 1200));

            Assert.That(Economy.CapacityOf(ShipClass.OreTug), Is.EqualTo(0), "its load is in its claws, not a hold");
            foreach (Good g in Enum.GetValues(typeof(Good)))
                Assert.That(Economy.Carries(ShipClass.OreTug, g), Is.False, g.ToString());
            Assert.That(FlightModelRegistry.Default.FamilyOf(ShipClass.OreTug), Is.EqualTo(FlightFamily.Hauler));
            Assert.That(PilotTuning.ControlOf(ShipClass.OreTug), Is.EqualTo(PilotControl.Hauler));
            Assert.That(Interior.For(ShipClass.OreTug, design.Crew, 0).Compartments.Any(c => c.Name == "Claw gantry"), Is.True);
        }

        [Test]
        public void TheLoadsMassGovernsTheTugsAcceleration()
        {
            double g = ShipClassSpec.Of(ShipClass.OreTug).AccelG * 9.80665, tug = BeltMining.TugMassTonnes;
            Assert.That(BeltMining.TugAccel(0), Is.EqualTo(g).Within(1e-12), "unladen it pulls its full rating");
            // Thrust over the tug and its load together: a load of its own mass halves it, one a thousand times heavier all but stops it.
            Assert.That(BeltMining.TugAccel(tug), Is.EqualTo(g / 2).Within(1e-12));
            Assert.That(BeltMining.TugAccel(999 * tug), Is.EqualTo(g / 1000).Within(1e-12));

            // Flip-and-burn over a fixed distance: a hundred times the load (on a much heavier tug-plus-rock) takes about ten times as long.
            double km = BeltMining.TugLocalHaulKm, light = BeltMining.PushDays(km, 1e5), heavy = BeltMining.PushDays(km, 1e7);
            Assert.That(heavy, Is.GreaterThan(light));
            Assert.That(heavy / light, Is.EqualTo(Math.Sqrt((tug + 1e7) / (tug + 1e5))).Within(1e-9));
            Assert.That(BeltMining.PushDays(km, 1e7), Is.GreaterThan(30), "a rock of ten million tonnes comes in over weeks, not hours");
        }

        [Test]
        public void TugsPushTheirRocksInToTheGrinders()
        {
            var (mining, op, _) = TugWorks();
            op.Destination = null;
            Run(mining, 600);

            Assert.That(op.TugHaulsSent, Is.GreaterThan(0));
            Assert.That(op.TugHaulsDelivered, Is.GreaterThan(0), "no tug got its rock in");
            Assert.That(op.RocksCaptured, Is.EqualTo(op.TugHaulsDelivered), "every rock in came by tug");
            Assert.That(op.TonnesGround, Is.GreaterThan(0));
            Assert.That(op.TonnesSeparated, Is.GreaterThan(0), "the tugs' rock feeds the separators");
            // Mass is kept from the claws to the slag heap.
            double backlog = op.Backlog.Sum(r => r.Remaining);
            Assert.That(op.TonnesGround + backlog, Is.EqualTo(op.TonnesCaptured).Within(1e-6 * op.TonnesCaptured));
            Assert.That(op.TugTonnesDelivered, Is.EqualTo(op.TonnesCaptured).Within(1e-6 * op.TonnesCaptured));

            foreach (var h in mining.Hauls)
            {
                Assert.That(h.Load.Tonnes, Is.LessThanOrEqualTo(BeltMining.OpenSpaceMaxTonnes + 1e-6), "no heavy lane, so nothing past open space's limit");
                Assert.That(h.Load.Class, Is.Not.SameAs(AsteroidClass.CometIce), "no ice share set");
                Assert.That(h.ViaHeavyLane, Is.False);
                Assert.That(h.ArriveJd - h.DepartJd, Is.EqualTo(BeltMining.PushDays(BeltMining.TugLocalHaulKm, h.Load.Tonnes)).Within(1e-9).Or.EqualTo(1),
                    "the trip is the push its load allows");
                Assert.That(h.FreeJd, Is.GreaterThan(h.ArriveJd), "then the run home empty");
                Assert.That(h.AccelMs2, Is.EqualTo(BeltMining.TugAccel(h.Load.Tonnes)));
            }
            // Never more tugs out than the operation has.
            for (double jd = StartJd; jd <= StartJd + 600; jd += 10)
                Assert.That(mining.Hauls.Count(h => h.DepartJd <= jd && jd < h.FreeJd), Is.LessThanOrEqualTo(op.OreTugs));
        }

        [Test]
        public void IceBlocksArePushedStraightToTheDepotAndMeltedThere()
        {
            var (mining, op, depot) = TugWorks();
            op.TugIceShare = 1;
            op.IceDepot = depot;
            op.Destination = null; // no convoys: whatever is in the depot came by tug
            Run(mining, 400);

            Assert.That(op.TugHaulsDelivered, Is.GreaterThan(0));
            Assert.That(mining.Hauls.All(h => h.Load.Class == AsteroidClass.CometIce && h.ToDepot == depot), Is.True);
            Assert.That(op.RocksCaptured, Is.EqualTo(0), "ice for the depot never goes through the grinders");
            Assert.That(depot.Stock[(int)Good.Water], Is.GreaterThan(0));
            Assert.That(depot.Stock[(int)Good.Hydrocarbons], Is.GreaterThan(0));
            Assert.That(depot.Stock[(int)Good.NickelIron], Is.EqualTo(0), "ice holds no metal");
            Assert.That(depot.StoredTonnes, Is.EqualTo(op.TugTonnesDelivered * 0.89 * op.SeparationEfficiency).Within(1e-6 * depot.StoredTonnes),
                "its water and volatiles, less what melting loses");
        }

        [Test]
        public void BigLoadsNeedAHeavyLaneAndRideItFarFaster()
        {
            var mining = new BeltMining(1) { Position = Apart };
            double big = 60e6;
            Assert.That(mining.MaxLoadTonnes(1, 2, StartJd), Is.EqualTo(BeltMining.OpenSpaceMaxTonnes));
            Assert.That(mining.HaulDays(1, 2, big, StartJd, out var none), Is.EqualTo(double.PositiveInfinity), "too big for open space");
            Assert.That(none, Is.Null);
            double open = mining.HaulDays(1, 2, 5e6, StartJd, out _);
            Assert.That(open, Is.GreaterThan(365), "pushing a rock eight AU through open space takes years");

            mining.HeavyLane = CeresJupiter;
            Assert.That(mining.MaxLoadTonnes(1, 2, StartJd), Is.EqualTo(Hyperlanes.HeavyLaneMaxTonnes));
            double byLane = mining.HaulDays(1, 2, big, StartJd, out var lane);
            Assert.That(byLane, Is.LessThan(double.PositiveInfinity), "the heavy lane carries it");
            Assert.That(lane?.Name, Is.EqualTo("Ceres–Jupiter"));
            Assert.That(mining.HaulDays(1, 2, 5e6, StartJd, out _), Is.LessThan(open / 3), "and anything far quicker than open space");
            Assert.That(mining.HaulDays(1, 2, 2 * Hyperlanes.HeavyLaneMaxTonnes, StartJd, out _), Is.EqualTo(double.PositiveInfinity), "even a heavy lane has a limit");
            Assert.That(mining.HaulDays(1, 1, 5e6, StartJd, out var local), Is.EqualTo(BeltMining.PushDays(BeltMining.TugLocalHaulKm, 5e6)));
            Assert.That(local, Is.Null, "no lane for a haul within the field");

            // The pipeline takes the heavy lane, and loads up past open space's limit on it.
            var (m, op, depot) = TugWorks();
            m.Position = Apart;
            m.HeavyLane = CeresJupiter;
            op.TugIceShare = 1;
            op.IceDepot = depot;
            op.TugMinLoadM = op.TugMaxLoadM = 600; // 68 million tonnes of ice
            Run(m, 30);
            Assert.That(m.Hauls, Is.Not.Empty);
            foreach (var h in m.Hauls)
            {
                Assert.That(h.ViaHeavyLane, Is.True);
                Assert.That(h.HeavyLane, Is.EqualTo("Ceres–Jupiter"));
                Assert.That(h.Load.Tonnes, Is.GreaterThan(BeltMining.OpenSpaceMaxTonnes));
            }
        }

        [Test]
        public void TugsAndTheirLoadsAreRaidedOnTheWay()
        {
            var raiders = new Faction("Black Banner", "", default) { IsPirate = true };
            int taken = 0, delivered = 0;
            TugHaul seized = null;
            for (int seed = 1; seed <= 6; seed++)
            {
                var (mining, op, _) = TugWorks(pressure: 1, seed: seed);
                op.Destination = null;
                op.OreTugs = 4;
                mining.Raiders = () => raiders;
                mining.TugTaken += h => seized ??= h;
                Run(mining, 3 * 365);
                taken += mining.TugsTaken;
                delivered += mining.TugHaulsDelivered;
                Assert.That(op.TugsLost, Is.EqualTo(mining.TugsTaken));
                Assert.That(op.OreTugs, Is.EqualTo(4 - op.TugsLost), "a seized tug is gone from its works");
                double lost = mining.Hauls.Where(h => h.State == ConvoyState.Stolen).Sum(h => h.Load.Class.Yield.Sum(y => y.fraction) * h.Load.Tonnes);
                Assert.That(mining.Plundered.Sum(), Is.EqualTo(lost).Within(1e-6 * Math.Max(1, lost)), "what the tugs held is the raiders'");
            }
            TestContext.Out.WriteLine($"tugs taken {taken}, hauls delivered {delivered}");
            Assert.That(taken, Is.GreaterThan(0), "three years of full-pressure raiding took no tug");
            Assert.That(seized.State, Is.EqualTo(ConvoyState.Stolen));
            Assert.That(seized.TakenBy, Is.SameAs(raiders));

            // An escort cuts the risk, as for a convoy.
            int bare = 0, escorted = 0;
            for (int seed = 1; seed <= 6; seed++)
            {
                var a = TugWorks(pressure: 1, seed: seed);
                var b = TugWorks(pressure: 1, seed: seed);
                b.op.EscortShare = 1;
                foreach (var w in new[] { a, b })
                {
                    w.op.Destination = null;
                    w.op.OreTugs = 4;
                    Run(w.mining, 3 * 365);
                }
                bare += a.mining.TugsTaken;
                escorted += b.mining.TugsTaken;
            }
            Assert.That(escorted, Is.LessThan(bare));
        }

        [Test]
        public void SolsCeresJupiterLaneIsHeavyAndOnlyHeavyLanesCarryAHeavyRoute()
        {
            var cat = BodyCatalog.CreateSolSystem();
            var eph = new Ephemeris(cat);
            eph.Update(StartJd);
            var lanes = Hyperlanes.CreateDefault(eph);
            var heavy = lanes.Lanes.Where(l => l.Heavy).ToList();
            Assert.That(heavy.Select(l => l.Name), Is.EqualTo(new[] { "Ceres–Jupiter" }));

            int ceres = cat["Ceres"].Index, jupiter = cat["Jupiter"].Index, earth = cat["Earth"].Index;
            var route = lanes.HeavyRoute(ceres, jupiter, StartJd);
            Assert.That(route, Has.Count.EqualTo(1));
            Assert.That(route[0].Lane, Is.SameAs(heavy[0]));
            Assert.That(route[0].To, Is.EqualTo(jupiter));
            Assert.That(lanes.HeavyRoute(earth, jupiter, StartJd), Is.Null, "no heavy lane from Earth");
            Assert.That(lanes.Route(earth, jupiter, StartJd), Is.Not.Null, "ordinary traffic still has its routes");
            Assert.That(Hyperlanes.HeavyRideDays(lanes.LaneLengthKm(heavy[0], StartJd)), Is.GreaterThan(Hyperlanes.RideDays(lanes.LaneLengthKm(heavy[0], StartJd))),
                "a heavy lane is slow beside an ordinary ride");
            Assert.That(lanes.MarkHeavy("Earth", "Pluto"), Is.Null, "no lane there to mark");
        }

        [Test]
        public void SolsTugsPushIceAlongTheHeavyLaneToValhallaAndSurviveASave()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            double jd = StartJd;
            eph.Update(jd);
            var w = World.CreateDefault(eph, jd);
            var m = w.Mining;
            var op = m.Operations.Single();
            Assert.That(op.OreTugs, Is.EqualTo(2));
            Assert.That(op.IceDepot, Is.SameAs(m.Warehouses.Single()));

            for (int d = 1; d <= 5; d++)
            {
                eph.Update(jd + d);
                w.Update(jd + d);
            }
            Assert.That(m.Hauls, Has.Count.EqualTo(2), "both tugs out");
            foreach (var h in m.Hauls)
            {
                Assert.That(h.Load.Class, Is.SameAs(AsteroidClass.CometIce));
                Assert.That(h.ToBody, Is.EqualTo(w.Catalog["Callisto"].Index));
                Assert.That(h.HeavyLane, Is.EqualTo("Ceres–Jupiter"), "out along the belt's heavy lane");
                Assert.That(h.ArriveJd - h.DepartJd, Is.InRange(30, 365), "slow, but not years");
            }

            var copy = ObjectGraph.Clone(w);
            Assert.That(copy.Mining.Hauls, Has.Count.EqualTo(2));
            Assert.That(copy.Mining.Hauls[0].Load.Class, Is.SameAs(AsteroidClass.CometIce), "rock kinds are the shared table entries");
            Assert.That(copy.Mining.Hauls[0].Operation, Is.SameAs(copy.Mining.Operations[0]));
            Assert.That(copy.Mining.HeavyLane.Target, Is.SameAs(copy), "the copy's tugs read the copy's lanes");
        }
    }
}
