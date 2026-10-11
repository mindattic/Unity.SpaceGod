using System;
using System.Collections.Generic;
using System.Linq;
using SolarSystem.Core;
using Xunit;
using Xunit.Abstractions;

namespace SolarSystem.Core.Tests
{
    public class FighterTests
    {
        readonly ITestOutputHelper output;

        public FighterTests(ITestOutputHelper output) => this.output = output;

        static Battle CarrierAction(int seed)
        {
            var blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
            var red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), 2461324.0, seed)
            {
                Launch = FighterAI.Launch,
                FighterStep = FighterAI.Step,
            };
            Ship Hull(ShipClass c, Faction f, int id) => new Ship { Id = id, Name = c + "" + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c) };
            b.AddShip(Hull(ShipClass.Carrier, blue, 0), 0, new DVec3(14, 0, 0), -DVec3.UnitX);
            b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(-14, 0, 0), DVec3.UnitX);
            b.AddShip(Hull(ShipClass.Destroyer, red, 2), 1, new DVec3(-14, 4, 0), DVec3.UnitX);
            return b;
        }

        [Fact]
        public void CarriersLaunchTheirSquadronsWhichFlyRunsShootAndTorpedo()
        {
            var b = CarrierAction(5);
            var fighters = b.Units.Where(u => u.Kind == UnitKind.Fighter).ToList();
            Assert.Equal(18, fighters.Count);
            Assert.Equal(0, b.Units[0].Combat.Fighters);
            Assert.Equal(6, fighters.Count(f => f.Fighter.Bomber));
            Assert.Equal(3, fighters.Count(f => f.Fighter.Lead == null));

            var phases = new HashSet<FighterPhase>();
            bool guns = false;
            for (int i = 0; i < 900 && !b.Over; i++)
            {
                b.Advance(1);
                foreach (var f in fighters.Where(f => f.Present && f.Fighter.Lead == null)) phases.Add(f.Fighter.Phase);
                guns |= b.Fx.Any(f => f.Kind == FxKind.Guns);
            }
            output.WriteLine("phases seen: " + string.Join(", ", phases));
            Assert.Contains(FighterPhase.AttackRun, phases);
            Assert.Contains(FighterPhase.BreakOff, phases);
            Assert.True(guns, "no fighter ever fired its guns");
            Assert.True(fighters.Where(f => f.Fighter.Bomber).SelectMany(f => f.Combat.Subsystems).Any(s => s.Kind == SubsystemKind.Torpedo && s.Ammo < s.Def.Weapon.Ammo), "no bomber fired a torpedo");
            foreach (var f in fighters)
                Assert.False(double.IsNaN(f.Pos.X) || f.Pos.Length > Battle.EscapeKm * 1.2, "fighter left the battle space");
        }

        [Fact]
        public void WingmenHoldALooseFormationThatBreathes()
        {
            var b = CarrierAction(11);
            var squadron = b.Units.Where(u => u.Kind == UnitKind.Fighter && u.Fighter.Squadron == 0).ToList();
            var lead = squadron.First(u => u.Fighter.Lead == null);
            var gaps = new List<double>();
            for (int i = 0; i < 120; i++)
            {
                b.Advance(1);
                if (i < 20 || !lead.Present) continue;
                foreach (var w in squadron.Where(w => w != lead && w.Present)) gaps.Add((w.Pos - lead.Pos).Length);
            }
            double mean = gaps.Average();
            double spread = Math.Sqrt(gaps.Select(g => (g - mean) * (g - mean)).Average());
            output.WriteLine($"wingman distance from lead: mean {mean * 1000:0} m, spread {spread * 1000:0} m");
            Assert.InRange(mean, 0.08, 1.2);
            Assert.True(spread > 0.01, "formation is welded: no variation at all");
        }

        [Fact]
        public void WhenTheLeadDiesTheSeniorWingmanTakesOver()
        {
            var b = CarrierAction(2);
            var squadron = b.Units.Where(u => u.Kind == UnitKind.Fighter && u.Fighter.Squadron == 1).ToList();
            var lead = squadron.First(u => u.Fighter.Lead == null);
            b.Advance(1);
            lead.Dead = true;
            b.Advance(1);
            var leads = squadron.Where(u => u.Present && u.Fighter.Lead == null).ToList();
            Assert.Single(leads);
            Assert.Equal(1, leads[0].Fighter.Slot);
            Assert.All(squadron.Where(u => u.Present && u != leads[0]), w => Assert.Equal(leads[0], w.Fighter.Lead));
        }
    }
}
