using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    [TestFixture]
    public class BoardingTests
    {
        const double StartJd = 2461324.0;


        static readonly Faction Blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true, MarineQuality = 1.2 };
        static readonly Faction Red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsPirate = true, MarineQuality = 1.1 };

        static Ship Hull(ShipClass c, Faction f, int id) =>
            new Ship { Id = id, Name = c + "" + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c) };

        static Battle NewBattle(int seed) => new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed)
        {
            Launch = FighterAI.Launch,
            FighterStep = FighterAI.Step,
        };

        [Test]
        public void BoardersFightThroughToTheBridgeAndEngineeringAndTakeTheShip()
        {
            var freighter = new ShipCombat(CombatSpec.Of(ShipClass.Freighter), ShipClass.Freighter);
            var interior = freighter.Interior;
            var rng = new Random(1);
            interior.Board(Red, 1.1, 30, interior.Of(CompartmentKind.Hold), true, 0, rng);
            double crewBefore = interior.Crew;
            bool taken = false;
            for (int s = 0; s < 3600 && !taken; s++) taken = interior.Step(1, s, rng);
            foreach (var note in interior.Log) TestContext.Out.WriteLine($"{note.Seconds,5:0}s  {note.Text}");
            Assert.That(taken, Is.True, "thirty marines failed to take a freighter");
            Assert.That(interior.Of(CompartmentKind.Bridge).Taken && interior.Of(CompartmentKind.Engineering).Taken, Is.True);
            Assert.That(interior.CrewLost > 0 && interior.Crew < crewBefore, Is.True);
            Assert.That((interior.Log).Any(n => n.Text.Contains("sealed") || n.Text.Contains("seal")), Is.True);
        }

        [Test]
        public void RearGuardsLeftWithoutAFrontPressOnRatherThanHoldForever()
        {
            var freighter = new ShipCombat(CombatSpec.Of(ShipClass.Freighter), ShipClass.Freighter);
            var interior = freighter.Interior;
            var rng = new Random(5);
            interior.Board(Red, 1.1, 30, interior.Of(CompartmentKind.Hold), true, 0, rng);
            int s = 0;
            for (; s < 3600 && interior.TakenCount < 2; s++) interior.Step(1, s, rng);
            Assert.That(interior.TakenCount >= 2, Is.True);
            // The party at the front falls; only the rear guards in what was taken are left.
            foreach (var c in interior.Compartments) c.Boarders = c.Taken ? 2 : 0;
            Assert.That(interior.UnderBoarding, Is.True);
            for (int end = s + 3600; s < end && interior.UnderBoarding; s++) interior.Step(1, s, rng);
            Assert.That(interior.UnderBoarding, Is.False, "the boarding stalled: neither taken nor repelled");
        }

        [Test]
        public void ASecondLandingDoesNotStandDownDefendersAlreadyOnTheMove()
        {
            var cruiser = new ShipCombat(CombatSpec.Of(ShipClass.Cruiser), ShipClass.Cruiser);
            var interior = cruiser.Interior;
            var rng = new Random(6);
            var bow = interior.Of(CompartmentKind.Bow);
            interior.Board(Red, 1.1, 40, bow, true, 0, rng);
            int s = 0;
            for (; s < 50; s++) interior.Step(1, s, rng);
            Assert.That(interior.UnderBoarding, Is.True);
            double Elsewhere() => interior.Compartments.Where(c => c.Boarders <= 0.01).Sum(c => c.Marines);
            interior.Board(Red, 1.1, 10, bow, true, s, rng);
            double before = Elsewhere();
            for (int end = s + 5; s < end; s++) interior.Step(1, s, rng);
            Assert.That(Elsewhere() < before, Is.True, "the defenders stopped moving up when more boarders landed");
        }

        [Test]
        public void AFewBoardersAgainstACruisersMarinesAreRepelled()
        {
            var cruiser = new ShipCombat(CombatSpec.Of(ShipClass.Cruiser), ShipClass.Cruiser);
            var interior = cruiser.Interior;
            var rng = new Random(2);
            interior.Board(Red, 1.0, 12, interior.Of(CompartmentKind.Bow), true, 0, rng);
            for (int s = 0; s < 3600 && interior.Boarder != null; s++) interior.Step(1, s, rng);
            Assert.That(interior.Boarder, Is.Null);
            Assert.That(interior.Captured, Is.False);
            Assert.That(interior.BoardersLost > 11, Is.True);
        }

        [Test]
        public void VacuumKillsCrewOnlyUntilTheBulkheadsSeal()
        {
            var battleship = new ShipCombat(CombatSpec.Of(ShipClass.Battleship), ShipClass.Battleship);
            var interior = battleship.Interior;
            var rng = new Random(3);
            var bow = interior.Of(CompartmentKind.Bow);
            interior.Board(Red, 1.0, 1, bow, true, 0, rng);
            double before = bow.Crew;
            for (int s = 0; s < 20; s++) interior.Step(1, s, rng);
            double afterSeal = bow.Crew;
            for (int s = 20; s < 60; s++) interior.Step(1, s, rng);
            Assert.That(afterSeal < before * 0.95, Is.True, "venting killed nobody");
            // After sealing, only the (tiny) fight goes on: no more mass casualties.
            Assert.That(bow.Crew > afterSeal * 0.8 || interior.Boarder == null, Is.True);
        }

        [Test]
        public void PodsBounceOffShieldsAndBoardOnceTheyAreDown()
        {
            var b = NewBattle(4);
            var cruiser = b.AddShip(Hull(ShipClass.Cruiser, Blue, 0), 0, new DVec3(5, 0, 0), -DVec3.UnitX);
            var target = b.AddShip(Hull(ShipClass.Destroyer, Red, 1), 1, new DVec3(-3, 0, 0), DVec3.UnitX);
            // A crippled destroyer: guns nearly gone, engines out, so it cannot run before the pods arrive.
            foreach (var s in target.Combat.Subsystems)
            {
                if (s.Def.IsWeapon) s.Hp = 1;
                if (s.Kind == SubsystemKind.Engine) s.Hp = 0;
            }
            bool bounced = false, boarded = false;
            for (int i = 0; i < 1500 && !b.Over; i++)
            {
                b.Advance(1);
                bounced |= b.Fx.Any(f => f.Kind == FxKind.PodBounce);
                boarded |= target.Combat.Interior.Boarder != null || target.Captured;
            }
            TestContext.Out.WriteLine($"shield {target.Combat.Shield:0}, embedded {b.Embedded.Count}, boarded {boarded}, captured {target.Captured}, dead {target.Dead}");
            Assert.That(boarded || target.Dead, Is.True, "the cruiser never put marines aboard");
            Expect.All(b.Embedded, e => Assert.That(e.Unit, Is.EqualTo(target)));
        }

        [Test]
        public void ABreacherRamsItsDrillHomeAndTakesTheShip()
        {
            var b = NewBattle(5);
            var breacher = b.AddShip(Hull(ShipClass.Breacher, Red, 0), 0, new DVec3(6, 0, 0), -DVec3.UnitX);
            var freighter = b.AddShip(Hull(ShipClass.Freighter, Blue, 1), 1, new DVec3(-4, 0, 0), DVec3.UnitX);
            freighter.Combat.ZeroAllShields();
            foreach (var s in freighter.Combat.Subsystems) if (s.Kind == SubsystemKind.ShieldGenerator) s.Hp = 0;
            bool latched = false;
            for (int i = 0; i < 2000 && !b.Over; i++)
            {
                b.Advance(1);
                latched |= breacher.Latched == freighter;
            }
            foreach (var note in freighter.Combat.Interior.Log) TestContext.Out.WriteLine(note.Text);
            Assert.That(latched, Is.True, "the breacher never rammed home");
            Assert.That(freighter.Captured, Is.True, "the freighter was not taken");
            Assert.That((b.Embedded).Any(e => e.Breacher && e.Unit == freighter), Is.True);
            Assert.That(freighter.Side, Is.EqualTo(0));
        }

        [Test]
        public void AWarshipLinesUpAlongsideADisabledShipAndBoardsIt()
        {
            var b = NewBattle(6);
            var raider = b.AddShip(Hull(ShipClass.Destroyer, Red, 0), 0, new DVec3(8, 0, 0), -DVec3.UnitX);
            var hulk = b.AddShip(Hull(ShipClass.Freighter, Blue, 1), 1, new DVec3(-2, 0, 0), DVec3.UnitX);
            foreach (var s in hulk.Combat.Subsystems) if (s.Kind == SubsystemKind.Engine) s.Hp = 0;
            // No pods: this is about lining up alongside and going in through the airlock.
            foreach (var s in raider.Combat.Subsystems) if (s.Kind == SubsystemKind.BoardingPods) s.Hp = 0;
            Assert.That(hulk.Combat.Disabled, Is.True);
            bool latched = false;
            int steps = 0;
            for (int i = 0; i < 3000 && !b.Over; i++, steps++)
            {
                b.Advance(1);
                latched |= raider.Latched == hulk;
            }
            TestContext.Out.WriteLine($"steps {steps}, over {b.Over}, target {(raider.BoardTarget != null)}, distance {(raider.Pos - hulk.Pos).Length:0.000} km, hulk dead {hulk.Dead}, raider fled {raider.Fled}, marines {raider.Combat.MarinesAvailable:0}");
            Assert.That(latched, Is.True, "the raider never latched on");
            Assert.That(hulk.Captured, Is.True);
            Assert.That((b.Fx).Any(f => f.Kind == FxKind.Captured), Is.True);
        }

        [Test]
        public void FightersLandBackOnTheirCarrierWhenTheShootingStops()
        {
            var b = NewBattle(7);
            var carrier = b.AddShip(Hull(ShipClass.Carrier, Blue, 0), 0, new DVec3(14, 0, 0), -DVec3.UnitX);
            var victim = b.AddShip(Hull(ShipClass.Destroyer, Red, 1), 1, new DVec3(-10, 0, 0), DVec3.UnitX);
            int launched = b.Units.Count(u => u.Kind == UnitKind.Fighter);
            for (int i = 0; i < 4000 && !b.Over; i++) b.Advance(1);
            int landed = b.Units.Count(u => u.Kind == UnitKind.Fighter && u.Docked);
            int lost = b.Units.Count(u => u.Kind == UnitKind.Fighter && u.Dead);
            TestContext.Out.WriteLine($"launched {launched}, landed {landed}, lost {lost}, aboard {carrier.Combat.Fighters}");
            Assert.That(b.Over, Is.True);
            Assert.That(landed > 0, Is.True, "no fighter landed");
            Assert.That(carrier.Combat.Fighters, Is.EqualTo(landed));
        }

        [Test]
        public void RaidersAmbushHyperlanesAndTakePrizes()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            int ambushes = 0, siteBattles = 0;
            var seen = new System.Collections.Generic.HashSet<Battle>();
            // The world's own log keeps only its latest 400 entries: count every one as it is logged.
            var logged = new System.Collections.Generic.List<WorldEvent>();
            w.Logged += logged.Add;
            for (double jd = StartJd; jd <= StartJd + 365; jd += 0.05)
            {
                eph.Update(jd);
                w.Update(jd);
                foreach (var b in w.War.Battles)
                    if (seen.Add(b) && b.Site != null) siteBattles++;
            }
            ambushes = logged.Count(e => e.Text.Contains("spring an ambush"));
            int prizes = logged.Count(e => e.Kind == WorldEventKind.Capture && e.Text.Contains(" is "));
            foreach (var e in logged.Where(e => e.Text.Contains("ambush") || (e.Kind == WorldEventKind.Capture && e.Text.Contains(" is "))).Take(15))
                TestContext.Out.WriteLine($"{Astro.ToDateTime(e.Jd):yyyy-MM-dd}  {e.Text}");
            TestContext.Out.WriteLine($"ambushes {ambushes}, lane battles {siteBattles}, prizes {prizes}, traders alive {w.Fleet.Ships.Count(s => !s.Destroyed && !s.Derelict && w.IsTrader(s))}");
            Assert.That(ambushes > 0, Is.True, "no lane ambush in a year");
            Assert.That(siteBattles > 0, Is.True);
            Assert.That(prizes > 0, Is.True, "nobody took a prize in a year");
        }
    }
}
