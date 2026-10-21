using System;
using System.Collections.Generic;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
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
        public void FightersRecalledToACarrierAdriftFarOutStillLand()
        {
            var b = CarrierAction(5);
            var carrier = b.Units[0];
            var fighter = b.Units.First(u => u.Kind == UnitKind.Fighter);
            // The carrier has drifted out past where fighters are kept in the fight, and calls them home.
            carrier.Pos = new DVec3(Battle.EscapeKm * 0.9, 0, 0);
            carrier.Vel = DVec3.Zero;
            carrier.Fleeing = true;
            fighter.Pos = carrier.Pos + new DVec3(-3, 0, 0);
            for (int i = 0; i < 2000 && !fighter.Docked; i++) FighterAI.Step(b, fighter, 0.5);
            Assert.That(fighter.Docked, Is.True, "the fighter bounced off the edge of the battle and never got home");
        }

        [Test]
        public void APlayerFlownHullsPointDefenceEngagesFightersOnItsOwn()
        {
            var b = CarrierAction(5);
            var flown = b.Units.First(u => u.Kind == UnitKind.Ship && u.Side == 1 && u.Combat.Subsystems.Any(s => s.Kind == SubsystemKind.PointDefense));
            flown.PlayerControlled = true;
            flown.PlayerFireHeld = false; // the trigger is never pulled
            bool fired = false;
            for (int i = 0; i < 600 && !fired && !b.Over; i++)
            {
                b.Advance(1);
                // Point-defence fire at a fighter (not a missile intercept, which never waited on the trigger)
                // from this hull's own mounts.
                fired = b.Fx.Any(f => f.Kind == FxKind.PointDefense && f.Unit != null && f.Unit.Kind == UnitKind.Fighter
                    && (f.A - flown.Pos).Length < flown.LengthKm);
            }
            Assert.That(fired, Is.True, "point defence waited on the player's trigger to engage fighters");
        }

        [Test]
        public void FightersBreakOffAHullThatStrikesItsColours()
        {
            var blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
            var red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), 2461324.0, 5) { Launch = FighterAI.Launch, FighterStep = FighterAI.Step };
            Ship Hull(ShipClass c, Faction f, int id) => new Ship { Id = id, Name = c + "" + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c) };
            var carrier = b.AddShip(Hull(ShipClass.Carrier, blue, 0), 0, new DVec3(14, 0, 0), -DVec3.UnitX);
            // The only hull on the other side, so it is every squadron's target.
            var victim = b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(-14, 0, 0), DVec3.UnitX);
            for (int i = 0; i < 60; i++) b.Advance(1);
            Assert.That(victim.Present, Is.True);
            // A player captor holds the hailing window open while we watch.
            carrier.PlayerControlled = true;
            b.Surrender(victim);
            for (int i = 0; i < 8; i++)
            {
                b.Advance(1);
                Assert.That(b.Units.Where(f => f.Kind == UnitKind.Fighter && f.Present && f.Target == victim), Is.Empty,
                    "fighters went back to attacking a hull waiting on terms");
            }
        }

        [Test]
        public void BomberWingmenLooseTheirTorpedoesOnTheRunToo()
        {
            var b = CarrierAction(5);
            var wingmen = b.Units.Where(u => u.Kind == UnitKind.Fighter && u.Fighter.Bomber && u.Fighter.Lead != null).ToList();
            Assert.That(wingmen, Is.Not.Empty);
            for (int i = 0; i < 900 && !b.Over; i++) b.Advance(1);
            Assert.That(wingmen.SelectMany(f => f.Combat.Subsystems).Any(s => s.Kind == SubsystemKind.Torpedo && s.Ammo < s.Def.Weapon.Ammo), Is.True,
                "only squadron leads ever fired a torpedo");
        }

        [Test]
        public void BomberWingmenDoNotTorpedoAHullThatHasAlreadyStruckItsColoursOrDied()
        {
            var b = CarrierAction(5);
            var lead = b.Units.First(u => u.Kind == UnitKind.Fighter && u.Fighter.Bomber && u.Fighter.Lead == null);
            var wingman = b.Units.First(u => u.Kind == UnitKind.Fighter && u.Fighter.Bomber && u.Fighter.Lead == lead);
            var victim = b.Units.First(u => u.Kind == UnitKind.Ship && u.Side != lead.Side);

            lead.Fighter.Phase = FighterPhase.AttackRun;
            lead.Target = victim;
            // Lined up for a shot: close, dead ahead.
            wingman.Pos = victim.Pos - DVec3.UnitX * 1.0;
            wingman.Forward = DVec3.UnitX;
            wingman.Vel = DVec3.Zero;
            int ammoBefore = wingman.Combat.Subsystems.First(s => s.Kind == SubsystemKind.Torpedo).Ammo;
            int missilesBefore = b.Missiles.Count;

            // The target struck its colours a moment ago -- resolved later in Battle.Step, after
            // fighters fly, so nothing has cleared anyone's stale Target reference to it yet.
            victim.Surrendering = true;
            FighterAI.Step(b, wingman, 0.1);
            Assert.That(wingman.Combat.Subsystems.First(s => s.Kind == SubsystemKind.Torpedo).Ammo, Is.EqualTo(ammoBefore),
                "a wingman fired a torpedo at a hull that had already struck its colours");
            Assert.That(b.Missiles.Count, Is.EqualTo(missilesBefore));

            // Or it was already destroyed outright.
            victim.Surrendering = false;
            victim.Dead = true;
            FighterAI.Step(b, wingman, 0.1);
            Assert.That(wingman.Combat.Subsystems.First(s => s.Kind == SubsystemKind.Torpedo).Ammo, Is.EqualTo(ammoBefore),
                "a wingman fired a torpedo at a hull that was already dead");
            Assert.That(b.Missiles.Count, Is.EqualTo(missilesBefore));
        }

        [Test]
        public void ACraftTurningBackIntoTheBattleKeepsAUnitHeading()
        {
            var b = CarrierAction(5);
            var wingman = b.Units.First(u => u.Kind == UnitKind.Fighter && u.Fighter.Lead != null);
            // Far out at the edge, nose already pointing back in, with its station close by.
            wingman.Pos = new DVec3(Battle.EscapeKm * 0.82, 0, 0);
            wingman.Forward = -DVec3.UnitX;
            wingman.Fighter.Lead.Pos = wingman.Pos + new DVec3(-0.5, 0.5, 0);
            wingman.Fighter.Lead.Vel = DVec3.Zero;
            FighterAI.Step(b, wingman, 0.1);
            Assert.That(wingman.Forward.Length, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(wingman.Vel.Length <= wingman.Combat.Spec.MaxSpeed * 1.3 + 1e-9, Is.True);
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
