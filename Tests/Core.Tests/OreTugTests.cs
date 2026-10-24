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
            Assert.That(copy.Mining.Hauls[0].Tug, Is.SameAs(copy.Mining.Operations[0].Tugs[0]).Or.SameAs(copy.Mining.Operations[0].Tugs[1]),
                "the copy's hauls are flown by the copy's hulls");
            Assert.That(copy.Fleet.Ships, Does.Contain(copy.Mining.Hauls[0].Tug));
        }

        // ---------------------------------------------------------------- real hulls (U.SPC-T498)

        static World Sol(out Ephemeris eph)
        {
            eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return World.CreateDefault(eph, StartJd);
        }

        static void Step(World w, Ephemeris eph, double days, Func<bool> until = null, double dt = 1)
        {
            double jd = w.Jd;
            for (double end = jd + days; jd < end;)
            {
                jd += dt;
                eph.Update(jd);
                w.Update(jd);
                if (until != null && until()) return;
            }
        }

        [Test]
        public void EveryOreTugIsARealHullInTheFleetThePlayerCanTakeTheHelmOf()
        {
            var w = Sol(out _);
            var op = w.Mining.Operations.Single();
            Assert.That(op.Tugs, Has.Count.EqualTo(op.OreTugs), "a hull for every tug the works counts");
            foreach (var tug in op.Tugs)
            {
                Assert.That(w.Fleet.Ships, Does.Contain(tug));
                Assert.That(w.Fleet.IsPresent(tug), Is.True);
                Assert.That(tug.Spec.Class, Is.EqualTo(ShipClass.OreTug));
                Assert.That(tug.Role, Is.EqualTo(ShipRole.Tug));
                Assert.That(tug.Faction, Is.SameAs(op.Owner));
                Assert.That(new ShipHelm(w, tug).CanEmbody, Is.True, "its helm can be taken");
                Assert.That(w.BurnsPropellant(tug), Is.False, "its works keeps it fuelled");
            }
            Assert.That(op.Tugs.Distinct().Count(), Is.EqualTo(op.Tugs.Count));
        }

        [Test]
        public void ALoadedTugIsSlowedByItsLoadAndTakesOnlyHeavyLanes()
        {
            var w = Sol(out _);
            var tug = w.Mining.Operations.Single().Tugs[0];
            tug.Tow = null; // as it runs home empty
            Assert.That(tug.AccelG, Is.EqualTo(tug.Spec.AccelG), "running empty it pulls its full rating");
            var lanes = w.Fleet.Lanes;
            int ceres = lanes.SystemOf(w.Catalog["Ceres"].Index), jupiter = lanes.SystemOf(w.Catalog["Callisto"].Index), earth = lanes.SystemOf(w.Catalog["Earth"].Index);
            Assert.That(lanes.RouteFor(tug, earth, jupiter, StartJd), Is.Not.Null, "empty, it goes any way the lanes run");

            tug.Tow = new CapturedRock { Designation = "test block", Class = AsteroidClass.CometIce, DiameterM = 250, Tonnes = 5e6, Remaining = 5e6 };
            Assert.That(tug.AccelG * 9.80665, Is.EqualTo(BeltMining.TugAccel(5e6)).Within(1e-12), "the same push the works plans its hauls by");
            var heavy = lanes.RouteFor(tug, ceres, jupiter, StartJd);
            Assert.That(heavy, Is.Not.Null);
            Assert.That(heavy.All(h => h.Lane.Heavy), Is.True);
            Assert.That(lanes.RouteFor(tug, earth, jupiter, StartJd), Is.Null, "no heavy lane from Earth: it pushes through open space");
            var lane = heavy[0].Lane;
            double km = lanes.LaneLengthKm(lane, StartJd);
            Assert.That(lanes.RideDays(tug, lane, km), Is.GreaterThan(lanes.RideDays(lane, km)), "a heavy lane carries a load at its own slow pace");
        }

        [Test]
        public void ATugFliesItsHaulWithItsLoadLatchedOnAndBringsItIn()
        {
            var w = Sol(out var eph);
            var m = w.Mining;
            var op = m.Operations.Single();
            var depot = op.IceDepot;
            Step(w, eph, 5);
            var haul = m.Hauls.First();
            var tug = haul.Tug;
            Assert.That(tug, Is.Not.Null, "a hull flies the haul");
            Assert.That(op.Tugs, Does.Contain(tug));
            Assert.That(haul.Latched, Is.True, "ice for Callisto is latched on at the field at Ceres");
            Assert.That(tug.Tow, Is.SameAs(haul.Load), "the load is in its claws");
            Assert.That(tug.AccelG, Is.LessThan(tug.Spec.AccelG / 50), "hundreds of thousands of tonnes and more on a tug of a few thousand");
            Assert.That(tug.Cargo, Does.Contain(haul.Load.Designation));
            Assert.That(ShipLod.ExtentFactor(tug), Is.EqualTo(1 + haul.Load.DiameterM / 1000 / tug.LengthKm).Within(1e-12),
                "drawn, and switched from icon to model, as the tug and its load together");

            bool rodeHeavy = false, flew = false;
            double stored = depot.StoredTonnes;
            Step(w, eph, 700, () =>
            {
                if (haul.State != ConvoyState.EnRoute) return true;
                if (tug.Phase == ShipPhase.Transit || tug.Phase == ShipPhase.Lane) flew = true;
                if (tug.Phase == ShipPhase.Lane)
                {
                    Assert.That(tug.LaneLeg.Lane.Heavy, Is.True, "laden, it rides heavy lanes only");
                    Assert.That(tug.Tow, Is.SameAs(haul.Load), "with its load still on");
                    rodeHeavy = true;
                }
                return false;
            });
            Assert.That(flew, Is.True, "the hull flew the haul");
            Assert.That(haul.State, Is.EqualTo(ConvoyState.Delivered), $"still {haul.State} with the tug {tug.Phase} at {w.Catalog[tug.Orbit?.Body ?? 0].Name}");
            Assert.That(rodeHeavy, Is.True, "out along the belt's heavy lane");
            Assert.That(tug.Tow, Is.Null, "it let go at the depot");
            Assert.That(tug.Orbit.Body, Is.EqualTo(depot.Body).Or.Not.EqualTo(op.TugHome), "it got the load in where it was for");
            Assert.That(haul.ArriveJd, Is.GreaterThan(haul.DepartJd + 1), "its arrival is when the hull got in");
            Assert.That(depot.StoredTonnes, Is.GreaterThan(stored), "the ice is melted into the depot");
            Assert.That(w.Events.Any(e => e.Text.Contains(tug.Name) && e.Text.Contains("brings")), Is.True);
            TestContext.Out.WriteLine($"{haul.Load.Tonnes:N0} t in {haul.ArriveJd - haul.DepartJd:0} d");
        }

        [Test]
        public void ATugWhoseLoadIsForItsOwnFieldFliesOutForItAndPushesItIn()
        {
            var w = Sol(out var eph);
            var m = w.Mining;
            int ceres = w.Catalog["Ceres"].Index;
            var op = m.AddOperation("Ceres Rock Works", w.Belt, ceres, null);
            op.CaptureTugs = 0;
            op.OreTugs = 1;
            var tug = w.RaiseTugs(op, w.Jd).Single();
            Assert.That(tug.Orbit.Body, Is.EqualTo(ceres));

            bool outEmpty = false, inLaden = false;
            TugHaul haul = null;
            Step(w, eph, 200, () =>
            {
                haul ??= m.HaulOf(tug);
                if (haul == null) return false;
                if (!haul.Latched && tug.Phase == ShipPhase.Transit)
                {
                    Assert.That(tug.Tow, Is.Null, "out to the field with its claws open");
                    outEmpty = true;
                }
                if (haul.Latched && tug.Phase == ShipPhase.Transit)
                {
                    Assert.That(tug.Tow, Is.SameAs(haul.Load));
                    inLaden = true;
                }
                return haul.State != ConvoyState.EnRoute;
            }, dt: 0.05);
            Assert.That(haul, Is.Not.Null, "the works sent its tug out");
            Assert.That(outEmpty, Is.True, "it flew out to the field for its load");
            Assert.That(inLaden, Is.True, "and pushed it in");
            Assert.That(haul.State, Is.EqualTo(ConvoyState.Delivered));
            Assert.That(op.RocksCaptured, Is.EqualTo(1), "the rock is parked at the refinery for the grinders");
            Assert.That(tug.Tow, Is.Null);
            Assert.That(tug.Orbit.Body, Is.EqualTo(ceres));
        }

        [Test]
        public void RaidersThreatenATugWhereItActuallyIsAndATugTakenInAFightTakesItsHaulWithIt()
        {
            var w = Sol(out var eph);
            var m = w.Mining;
            var op = m.Operations.Single();
            Step(w, eph, 5);
            var haul = m.Hauls.First();
            var tug = haul.Tug;
            double jd = w.Jd;

            Assert.That(m.TugThreat(haul, jd).Pressure, Is.EqualTo(0), "no raiders near the belt works' field");
            var here = ShipHelm.FrameOf(w, tug, jd);
            var raider = w.Fleet.SpawnAt("Cutthroat", ShipClass.Destroyer, w.Pirates, here.body, here.offsetKm + new DVec3(500, 0, 0), DVec3.UnitX, jd);
            var danger = m.TugThreat(haul, jd);
            Assert.That(danger.Pressure, Is.GreaterThan(0), "a raider alongside it");
            Assert.That(danger.Raiders, Is.SameAs(w.Pirates));
            Assert.That(danger.Body, Is.EqualTo(w.Catalog["Ceres"].Index), "reported where the tug actually is");
            raider.Destroyed = true;

            // Boarded and taken in a fight (as the war's prize rules hand a hull over): its haul goes with it.
            double plundered = m.Plundered.Sum();
            tug.Faction = w.Pirates;
            Step(w, eph, 1);
            Assert.That(haul.State, Is.EqualTo(ConvoyState.Stolen));
            Assert.That(haul.TakenBy, Is.SameAs(w.Pirates));
            Assert.That(tug.Tow, Is.Null);
            Assert.That(op.Tugs, Does.Not.Contain(tug));
            Assert.That(op.OreTugs, Is.EqualTo(1));
            Assert.That(op.TugsLost, Is.EqualTo(1));
            Assert.That(m.Plundered.Sum(), Is.GreaterThan(plundered), "its load is the raiders'");
        }

        [Test]
        public void ATugSeizedOnTheWayFliesTheRaidersColoursFromWhereItWas()
        {
            var w = Sol(out var eph);
            var m = w.Mining;
            Step(w, eph, 5);
            var haul = m.Hauls.First();
            var tug = haul.Tug;
            var start = w.Fleet.StateOf(tug, w.Jd).HelioKm;
            haul.TakenBy = w.Pirates;
            m.TugSeized(haul, w.Jd);
            Assert.That(tug.Faction, Is.SameAs(w.Pirates));
            Assert.That(tug.Role, Is.EqualTo(ShipRole.Free), "no longer the works' to send");
            Assert.That(tug.Destroyed, Is.False, "the same hull, still in the fleet");
            Assert.That(w.Fleet.IsPresent(tug), Is.True);
            Assert.That((w.Fleet.StateOf(tug, w.Jd).HelioKm - start).Length, Is.LessThan(1e4), "taken where it was");
            var havens = w.Economy.Settlements.Where(s => s.Owner == w.Pirates && s.Kind == SettlementKind.Haven && !s.Fallen).Select(s => s.Body).ToList();
            Assert.That(tug.Ports.All(havens.Contains), Is.True, "bound for the raiders' holds");
        }
    }
}
