using System;
using System.Linq;
using SolarSystem.Core;
using Xunit;
using Xunit.Abstractions;

namespace SolarSystem.Core.Tests
{
    public class CombatTests
    {
        const double StartJd = 2461324.0;
        readonly ITestOutputHelper output;

        public CombatTests(ITestOutputHelper output) => this.output = output;

        static Ship Hull(ShipClass c, Faction f, int id) => new Ship
        {
            Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c)),
        };

        static (Battle battle, Faction blue, Faction red) Skirmish(int seed, (ShipClass, int side)[] ships)
        {
            var blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
            var red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed);
            int id = 0;
            foreach (var (c, side) in ships)
            {
                var ship = Hull(c, side == 0 ? blue : red, id++);
                var pos = new DVec3(side == 0 ? 13 : -13, (id % 3 - 1) * 3.0, 0);
                b.AddShip(ship, side, pos, side == 0 ? -DVec3.UnitX : DVec3.UnitX);
            }
            return (b, blue, red);
        }

        [Fact]
        public void TheStrongerGroupWinsAndHardpointsTakeTheDamage()
        {
            int blueWins = 0;
            for (int seed = 1; seed <= 6; seed++)
            {
                var (b, _, _) = Skirmish(seed, new[]
                {
                    (ShipClass.Battleship, 0), (ShipClass.Cruiser, 0), (ShipClass.Destroyer, 0),
                    (ShipClass.Destroyer, 1), (ShipClass.Destroyer, 1),
                });
                for (int i = 0; i < 20000 && !b.Over; i++) b.Advance(1);
                Assert.True(b.Over, "battle never ended");
                if (b.Winner == 0) blueWins++;
                var subsHit = b.Units.SelectMany(u => u.Combat.Subsystems).Count(s => s.Hp < s.Def.MaxHp);
                Assert.True(subsHit > 0, "no hardpoint was ever damaged");
                Assert.Contains(b.Fx, f => f.Kind == FxKind.Beam);
                Assert.Contains(b.Fx, f => f.Kind == FxKind.MissileLaunch || f.Kind == FxKind.Impact);
                output.WriteLine($"seed {seed}: winner {b.Winner} after {b.Seconds / 60:0} min, dead {b.Units.Count(u => u.Dead)}, fled {b.Units.Count(u => u.Fled)}");
            }
            Assert.True(blueWins >= 5, $"the battleship group won only {blueWins} of 6");
        }

        [Fact]
        public void LosingTheReactorDestroysTheShipAndLosingTheGeneratorDropsShields()
        {
            var (b, _, _) = Skirmish(3, new[] { (ShipClass.Cruiser, 0), (ShipClass.Cruiser, 1) });
            var target = b.Units[1];
            var gen = target.Combat.Subsystems.First(s => s.Kind == SubsystemKind.ShieldGenerator);
            target.Combat.Shield = 0;
            b.ApplyDamage(target, gen, 5000, target.Pos);
            Assert.True(gen.Destroyed);
            Assert.False(target.Combat.HasShieldGenerator);
            Assert.Equal(0, target.Combat.Shield);

            var reactor = target.Combat.Subsystems.First(s => s.Kind == SubsystemKind.Reactor);
            target.Combat.Hull = target.Combat.Spec.Hull;
            b.ApplyDamage(target, reactor, 5000, target.Pos);
            Assert.True(target.Dead);
            Assert.Contains(b.Fx, f => f.Kind == FxKind.UnitDestroyed);
        }

        [Fact]
        public void HardpointsSitOnTheHull()
        {
            var u = new BattleUnit { Pos = new DVec3(1, 2, 3), Forward = new DVec3(0, 1, 0), LengthKm = 2 };
            var nose = new SubsystemDef(SubsystemKind.Engine, 0, 0, 0.5);
            var top = new SubsystemDef(SubsystemKind.Engine, 0, 0.5, 0);
            Assert.True((u.PointOf(nose) - new DVec3(1, 3, 3)).Length < 1e-9);
            // "Up" on a level hull is ecliptic north.
            Assert.True((u.PointOf(top) - new DVec3(1, 2, 4)).Length < 1e-9);
        }

        [Fact]
        public void CoarseStepsAtHighWarpStillFinishTheBattle()
        {
            var (b, _, _) = Skirmish(9, new[] { (ShipClass.Battleship, 0), (ShipClass.Destroyer, 1), (ShipClass.Destroyer, 1) });
            b.Advance(3 * 86400);
            Assert.True(b.Over);
        }

        [Fact]
        public void TwoYearsOfWarHaveBattlesLossesAndAStillWorkingEconomy()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            int maxBattles = 0;
            for (double jd = StartJd; jd <= StartJd + 730; jd += 0.25)
            {
                eph.Update(jd);
                w.Update(jd);
                maxBattles = Math.Max(maxBattles, w.War.Battles.Count);
                foreach (var s in w.Fleet.Ships)
                {
                    if (s.Destroyed) continue;
                    var st = w.Fleet.StateOf(s, jd);
                    Assert.False(double.IsNaN(st.HelioKm.X), s.Name);
                    if (s.Battle != null) Assert.False(s.Battle.Over, $"{s.Name} still held by a finished battle");
                }
            }
            foreach (var e in w.Events.Where(e => e.Kind != WorldEventKind.Launch).TakeLast(40))
                output.WriteLine($"{Astro.ToDateTime(e.Jd):yyyy-MM-dd}  {e.Text}");
            foreach (var f in w.Fleet.Factions)
                output.WriteLine($"{f.Name,-22} losses {w.War.Losses(f),3}  ships {w.Fleet.Ships.Count(s => s.Faction == f && !s.Destroyed),3}  treasury {f.Treasury / 1e9:0.00} B");
            output.WriteLine($"launches {w.Events.Count(e => e.Kind == WorldEventKind.Launch)}, battles {w.Events.Count(e => e.Kind == WorldEventKind.Battle) / 2}, peak concurrent {maxBattles}");
            foreach (var s in w.Economy.Settlements) output.WriteLine($"{s.Name,-20} {s.Owner.Prefix,-4} pop {s.Population / 1e3,7:N0}k platforms {s.Platforms.Count}/{s.MaxPlatforms}");

            Assert.True(w.Events.Count(e => e.Kind == WorldEventKind.Battle) >= 4, "hardly any fighting in two years");
            Assert.True(w.Fleet.Ships.Count(s => s.Destroyed) > 0, "nobody ever died");
            Assert.True(w.Fleet.Ships.Count(s => !s.Destroyed && w.IsTrader(s)) > 20, "the merchant fleet was wiped out");
            Assert.True(w.Fleet.Ships.Count(s => !s.Destroyed && s.Spec.IsMilitary && s.Faction == w.Sol) > 0);
            Assert.True(w.Fleet.Ships.Count(s => !s.Destroyed && s.Spec.IsMilitary && s.Faction == w.Reach) > 0);
            foreach (var s in w.Economy.Settlements)
                Assert.True(s.Population >= s.InitialPopulation * 0.5, $"{s.Name} collapsed to {s.Population:N0}");
        }
    }
}
