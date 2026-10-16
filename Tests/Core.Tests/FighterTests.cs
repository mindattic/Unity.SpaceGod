using System;
using System.Collections.Generic;
using System.Linq;
using SolarSystem.Core;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    [TestFixture]
    public class FighterTests
    {


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

        [Test]
        public void CarriersLaunchTheirSquadronsWhichFlyRunsShootAndTorpedo()
        {
            var b = CarrierAction(5);
            var fighters = b.Units.Where(u => u.Kind == UnitKind.Fighter).ToList();
            Assert.That(fighters.Count, Is.EqualTo(18));
            Assert.That(b.Units[0].Combat.Fighters, Is.EqualTo(0));
            Assert.That(fighters.Count(f => f.Fighter.Bomber), Is.EqualTo(6));
            Assert.That(fighters.Count(f => f.Fighter.Lead == null), Is.EqualTo(3));

            var phases = new HashSet<FighterPhase>();
            bool guns = false;
            for (int i = 0; i < 900 && !b.Over; i++)
            {
                b.Advance(1);
                foreach (var f in fighters.Where(f => f.Present && f.Fighter.Lead == null)) phases.Add(f.Fighter.Phase);
                guns |= b.Fx.Any(f => f.Kind == FxKind.Guns);
            }
            TestContext.Out.WriteLine("phases seen: " + string.Join(", ", phases));
            Assert.That(phases, Does.Contain(FighterPhase.AttackRun));
            Assert.That(phases, Does.Contain(FighterPhase.BreakOff));
            Assert.That(guns, Is.True, "no fighter ever fired its guns");
            Assert.That(fighters.Where(f => f.Fighter.Bomber).SelectMany(f => f.Combat.Subsystems).Any(s => s.Kind == SubsystemKind.Torpedo && s.Ammo < s.Def.Weapon.Ammo), Is.True, "no bomber fired a torpedo");
            foreach (var f in fighters)
                Assert.That(double.IsNaN(f.Pos.X) || f.Pos.Length > Battle.EscapeKm * 1.2, Is.False, "fighter left the battle space");
        }

        [Test]
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
            TestContext.Out.WriteLine($"wingman distance from lead: mean {mean * 1000:0} m, spread {spread * 1000:0} m");
            Assert.That(mean, Is.InRange(0.08, 1.2));
            Assert.That(spread > 0.01, Is.True, "formation is welded: no variation at all");
        }

        [Test]
        public void WhenTheLeadDiesTheSeniorWingmanTakesOver()
        {
            var b = CarrierAction(2);
            var squadron = b.Units.Where(u => u.Kind == UnitKind.Fighter && u.Fighter.Squadron == 1).ToList();
            var lead = squadron.First(u => u.Fighter.Lead == null);
            b.Advance(1);
            lead.Dead = true;
            b.Advance(1);
            var leads = squadron.Where(u => u.Present && u.Fighter.Lead == null).ToList();
            Assert.That(leads, Has.Exactly(1).Items);
            Assert.That(leads[0].Fighter.Slot, Is.EqualTo(1));
            Expect.All(squadron.Where(u => u.Present && u != leads[0]), w => Assert.That(w.Fighter.Lead, Is.EqualTo(leads[0])));
        }
    }
}
