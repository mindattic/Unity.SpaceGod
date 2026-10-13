using System;
using System.Linq;
using SolarSystem.Core;
using Xunit;
using Xunit.Abstractions;

namespace SolarSystem.Core.Tests
{
    public class BoardingTests
    {
        const double StartJd = 2461324.0;
        readonly ITestOutputHelper output;

        public BoardingTests(ITestOutputHelper output) => this.output = output;

        static readonly Faction Blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true, MarineQuality = 1.2 };
        static readonly Faction Red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsPirate = true, MarineQuality = 1.1 };

        static Ship Hull(ShipClass c, Faction f, int id) =>
            new Ship { Id = id, Name = c + "" + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c) };

        static Battle NewBattle(int seed) => new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed)
        {
            Launch = FighterAI.Launch,
            FighterStep = FighterAI.Step,
        };

        [Fact]
        public void BoardersFightThroughToTheBridgeAndEngineeringAndTakeTheShip()
        {
            var freighter = new ShipCombat(CombatSpec.Of(ShipClass.Freighter), ShipClass.Freighter);
            var interior = freighter.Interior;
            var rng = new Random(1);
            interior.Board(Red, 1.1, 30, interior.Of(CompartmentKind.Hold), true, 0, rng);
            double crewBefore = interior.Crew;
            bool taken = false;
            for (int s = 0; s < 3600 && !taken; s++) taken = interior.Step(1, s, rng);
            foreach (var note in interior.Log) output.WriteLine($"{note.Seconds,5:0}s  {note.Text}");
            Assert.True(taken, "thirty marines failed to take a freighter");
            Assert.True(interior.Of(CompartmentKind.Bridge).Taken && interior.Of(CompartmentKind.Engineering).Taken);
            Assert.True(interior.CrewLost > 0 && interior.Crew < crewBefore);
            Assert.Contains(interior.Log, n => n.Text.Contains("sealed") || n.Text.Contains("seal"));
        }

        [Fact]
        public void AFewBoardersAgainstACruisersMarinesAreRepelled()
        {
            var cruiser = new ShipCombat(CombatSpec.Of(ShipClass.Cruiser), ShipClass.Cruiser);
            var interior = cruiser.Interior;
            var rng = new Random(2);
            interior.Board(Red, 1.0, 12, interior.Of(CompartmentKind.Bow), true, 0, rng);
            for (int s = 0; s < 3600 && interior.Boarder != null; s++) interior.Step(1, s, rng);
            Assert.Null(interior.Boarder);
            Assert.False(interior.Captured);
            Assert.True(interior.BoardersLost > 11);
        }

        [Fact]
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
            Assert.True(afterSeal < before * 0.95, "venting killed nobody");
            // After sealing, only the (tiny) fight goes on: no more mass casualties.
            Assert.True(bow.Crew > afterSeal * 0.8 || interior.Boarder == null);
        }

        [Fact]
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
            output.WriteLine($"shield {target.Combat.Shield:0}, embedded {b.Embedded.Count}, boarded {boarded}, captured {target.Captured}, dead {target.Dead}");
            Assert.True(boarded || target.Dead, "the cruiser never put marines aboard");
            Assert.All(b.Embedded, e => Assert.Equal(target, e.Unit));
        }

        [Fact]
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
            foreach (var note in freighter.Combat.Interior.Log) output.WriteLine(note.Text);
            Assert.True(latched, "the breacher never rammed home");
            Assert.True(freighter.Captured, "the freighter was not taken");
            Assert.Contains(b.Embedded, e => e.Breacher && e.Unit == freighter);
            Assert.Equal(0, freighter.Side);
        }

        [Fact]
        public void AWarshipLinesUpAlongsideADisabledShipAndBoardsIt()
        {
            var b = NewBattle(6);
            var raider = b.AddShip(Hull(ShipClass.Destroyer, Red, 0), 0, new DVec3(8, 0, 0), -DVec3.UnitX);
            var hulk = b.AddShip(Hull(ShipClass.Freighter, Blue, 1), 1, new DVec3(-2, 0, 0), DVec3.UnitX);
            foreach (var s in hulk.Combat.Subsystems) if (s.Kind == SubsystemKind.Engine) s.Hp = 0;
            // No pods: this is about lining up alongside and going in through the airlock.
            foreach (var s in raider.Combat.Subsystems) if (s.Kind == SubsystemKind.BoardingPods) s.Hp = 0;
            Assert.True(hulk.Combat.Disabled);
            bool latched = false;
            int steps = 0;
            for (int i = 0; i < 3000 && !b.Over; i++, steps++)
            {
                b.Advance(1);
                latched |= raider.Latched == hulk;
            }
            output.WriteLine($"steps {steps}, over {b.Over}, target {(raider.BoardTarget != null)}, distance {(raider.Pos - hulk.Pos).Length:0.000} km, hulk dead {hulk.Dead}, raider fled {raider.Fled}, marines {raider.Combat.MarinesAvailable:0}");
            Assert.True(latched, "the raider never latched on");
            Assert.True(hulk.Captured);
            Assert.Contains(b.Fx, f => f.Kind == FxKind.Captured);
        }

        [Fact]
        public void FightersLandBackOnTheirCarrierWhenTheShootingStops()
        {
            var b = NewBattle(7);
            var carrier = b.AddShip(Hull(ShipClass.Carrier, Blue, 0), 0, new DVec3(14, 0, 0), -DVec3.UnitX);
            var victim = b.AddShip(Hull(ShipClass.Destroyer, Red, 1), 1, new DVec3(-10, 0, 0), DVec3.UnitX);
            int launched = b.Units.Count(u => u.Kind == UnitKind.Fighter);
            for (int i = 0; i < 4000 && !b.Over; i++) b.Advance(1);
            int landed = b.Units.Count(u => u.Kind == UnitKind.Fighter && u.Docked);
            int lost = b.Units.Count(u => u.Kind == UnitKind.Fighter && u.Dead);
            output.WriteLine($"launched {launched}, landed {landed}, lost {lost}, aboard {carrier.Combat.Fighters}");
            Assert.True(b.Over);
            Assert.True(landed > 0, "no fighter landed");
            Assert.Equal(landed, carrier.Combat.Fighters);
        }

        [Fact]
        public void RaidersAmbushHyperlanesAndTakePrizes()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            int ambushes = 0, siteBattles = 0;
            var seen = new System.Collections.Generic.HashSet<Battle>();
            for (double jd = StartJd; jd <= StartJd + 365; jd += 0.05)
            {
                eph.Update(jd);
                w.Update(jd);
                foreach (var b in w.War.Battles)
                    if (seen.Add(b) && b.Site != null) siteBattles++;
            }
            ambushes = w.Events.Count(e => e.Text.Contains("spring an ambush"));
            int prizes = w.Events.Count(e => e.Kind == WorldEventKind.Capture && e.Text.Contains(" is "));
            foreach (var e in w.Events.Where(e => e.Text.Contains("ambush") || (e.Kind == WorldEventKind.Capture && e.Text.Contains(" is "))).Take(15))
                output.WriteLine($"{Astro.ToDateTime(e.Jd):yyyy-MM-dd}  {e.Text}");
            output.WriteLine($"ambushes {ambushes}, lane battles {siteBattles}, prizes {prizes}, traders alive {w.Fleet.Ships.Count(s => !s.Destroyed && !s.Derelict && w.IsTrader(s))}");
            Assert.True(ambushes > 0, "no lane ambush in a year");
            Assert.True(siteBattles > 0);
            Assert.True(prizes > 0, "nobody took a prize in a year");
        }
    }
}
