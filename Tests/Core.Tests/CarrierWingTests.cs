using System.Collections.Generic;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// A carrier's wing as real craft between battles (U.SPC-T526): every fighter, interceptor, bomber and recon craft is a
    /// hull docked in its carrier's hangar that launches as itself, keeps its own damage and pilot, and is lost or taken
    /// back aboard as itself.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class CarrierWingTests
    {
        const double StartJd = 2461324.0;

        static Ship Hull(ShipClass c, Faction f, int id) =>
            new Ship { Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c) };

        static (Battle b, Faction blue, Faction red) NewBattle(int seed = 5)
        {
            var blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
            var red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed) { Launch = FighterAI.Launch, FighterStep = FighterAI.Step };
            return (b, blue, red);
        }

        [Test]
        public void ACarriersHangarHoldsRealCraftOfTheirOwnClasses()
        {
            var combat = new ShipCombat(CombatSpec.Of(ShipClass.Carrier), ShipClass.Carrier);
            var spec = combat.Spec;
            Assert.That(combat.Hangar.Count, Is.EqualTo(spec.HangarCraft));
            Assert.That(combat.Fighters, Is.EqualTo(spec.HangarCraft));
            Assert.That(combat.Hangar.Distinct().Count(), Is.EqualTo(spec.HangarCraft), "each a hull of its own");
            for (int i = 0; i < combat.Hangar.Count; i++)
            {
                var craft = combat.Hangar[i];
                Assert.That(craft.Craft, Is.Not.Null);
                Assert.That(craft.Craft.Berth, Is.EqualTo(i), "in berth order");
                Assert.That(craft.Craft.Role, Is.EqualTo(CarrierWings.Berth(spec, i, out _, out _)));
                Assert.That(craft.Spec.Class, Is.EqualTo(FighterCraft.Of(craft.Craft.Role).Class));
                Assert.That(craft.Combat.Spec, Is.SameAs(FighterCraft.Of(craft.Craft.Role).Spec), "on its own class's hull");
                Assert.That(craft.Combat.Hangar, Is.Empty, "a craft carries no craft");
            }
            Assert.That(CarrierWings.Aboard(combat, FighterRole.Recon), Is.EqualTo(spec.ReconCraft));
        }

        [Test]
        public void EachCraftLaunchesAsItselfWithItsOwnHullAndPilot()
        {
            var (b, blue, red) = NewBattle();
            var hull = Hull(ShipClass.Carrier, blue, 0);
            var docked = hull.Combat.Hangar.ToList();
            var carrier = b.AddShip(hull, 0, new DVec3(14, 0, 0), -DVec3.UnitX);
            b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(-14, 0, 0), DVec3.UnitX);
            Assert.That(carrier.Combat.Hangar, Is.Empty, "all aloft");
            var aloft = b.Units.Where(u => u.Kind == UnitKind.Fighter).ToList();
            Assert.That(aloft.Count, Is.EqualTo(docked.Count));
            Assert.That(aloft.Select(u => u.CraftHull), Is.EquivalentTo(docked), "every docked craft, and no other");
            foreach (var u in aloft)
            {
                Assert.That(u.Combat, Is.SameAs(u.CraftHull.Combat), "it fights on its own hull");
                Assert.That(u.Fighter.Role, Is.EqualTo(u.CraftHull.Craft.Role));
                var pilot = u.CraftHull.Craft.Pilot;
                Assert.That(pilot, Is.Not.Null);
                Assert.That(pilot.Sorties, Is.EqualTo(1));
                Assert.That(u.Fighter.Skill, Is.EqualTo(pilot.Skill));
                Assert.That(u.Fighter.Discipline, Is.EqualTo(pilot.Discipline));
                Assert.That(u.CraftHull.Faction, Is.SameAs(blue));
                Assert.That(u.Name, Is.EqualTo(u.CraftHull.Name));
            }
        }

        [Test]
        public void CraftComeBackAboardAsThemselvesWithTheirDamageAndPilotsAndTheLostStayLost()
        {
            var (b, blue, red) = NewBattle();
            var hull = Hull(ShipClass.Carrier, blue, 0);
            var carrier = b.AddShip(hull, 0, new DVec3(14, 0, 0), -DVec3.UnitX);
            var enemy = b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(-14, 0, 0), DVec3.UnitX);
            var aloft = b.Units.Where(u => u.Kind == UnitKind.Fighter).ToList();
            var hurt = aloft[0];
            var lost = aloft[1];
            hurt.Combat.Hull = hurt.Combat.Spec.Hull * 0.6;
            lost.Dead = true;
            var pilots = aloft.ToDictionary(u => u.CraftHull, u => u.CraftHull.Craft.Pilot);
            b.Advance(5);
            enemy.Surrendering = true;
            for (int i = 0; i < 600 && b.Units.Any(u => u.Kind == UnitKind.Fighter && u.Present); i++) b.Advance(1);

            var back = carrier.Combat.Hangar;
            Assert.That(back.Count, Is.EqualTo(aloft.Count - 1), "all but the one shot down");
            Assert.That(back, Does.Not.Contain(lost.CraftHull));
            Assert.That(back, Is.EquivalentTo(aloft.Where(u => u != lost).Select(u => u.CraftHull)), "the same hulls, not new ones");
            foreach (var craft in back) Assert.That(craft.Craft.Pilot, Is.SameAs(pilots[craft]), "each with its own pilot");
            Assert.That(hurt.CraftHull.Combat.Hull, Is.LessThan(hurt.CraftHull.Combat.Spec.Hull), "its damage came home with it");
            Assert.That(back.Select(c => c.Craft.Berth).Distinct().Count(), Is.EqualTo(back.Count), "one craft to a berth");

            // A yard builds a craft only for the berth left empty, of the class laid out for it, and mends the hurt one.
            int lostBerth = lost.CraftHull.Craft.Berth;
            carrier.Combat.RestoreFighters(5);
            Assert.That(carrier.Combat.Hangar.Count, Is.EqualTo(carrier.Combat.Spec.HangarCraft));
            var fresh = carrier.Combat.Hangar.Single(c => !pilots.ContainsKey(c));
            Assert.That(fresh.Craft.Berth, Is.EqualTo(lostBerth));
            Assert.That(fresh.Craft.Role, Is.EqualTo(lost.CraftHull.Craft.Role));
            Assert.That(CarrierWings.Damaged(carrier.Combat), Is.GreaterThanOrEqualTo(1));
            CarrierWings.MendAboard(carrier.Combat);
            Assert.That(CarrierWings.Damaged(carrier.Combat), Is.EqualTo(0));
        }

        [Test]
        public void TheSameCraftAndPilotsFlyTheNextFight()
        {
            var (b, blue, red) = NewBattle();
            var hull = Hull(ShipClass.Carrier, blue, 0);
            var carrier = b.AddShip(hull, 0, new DVec3(14, 0, 0), -DVec3.UnitX);
            var enemy = b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(-14, 0, 0), DVec3.UnitX);
            enemy.Surrendering = true;
            for (int i = 0; i < 600 && b.Units.Any(u => u.Kind == UnitKind.Fighter && u.Present); i++) b.Advance(1);
            var first = hull.Combat.Hangar.ToList();
            Assert.That(first.Count, Is.EqualTo(hull.Combat.Spec.HangarCraft));
            hull.Battle = null;

            var (b2, _, _) = NewBattle(9);
            b2.AddShip(hull, 0, new DVec3(14, 0, 0), -DVec3.UnitX);
            b2.AddShip(Hull(ShipClass.Battleship, red, 2), 1, new DVec3(-14, 0, 0), DVec3.UnitX);
            var again = b2.Units.Where(u => u.Kind == UnitKind.Fighter).ToList();
            Assert.That(again.Select(u => u.CraftHull), Is.EquivalentTo(first));
            Assert.That(again.All(u => u.CraftHull.Craft.Pilot.Sorties == 2), Is.True, "the same pilots, on their second sortie");
        }

        [Test]
        public void ACrippledCraftStaysAboardAndARefitKeepsTheWing()
        {
            var combat = new ShipCombat(CombatSpec.Of(ShipClass.Carrier), ShipClass.Carrier);
            var crippled = combat.Hangar[0];
            crippled.Combat.Hull = crippled.Combat.Spec.Hull * 0.05;
            Assert.That(CarrierWings.Flyable(crippled), Is.False);

            var (b, blue, red) = NewBattle();
            var hull = new Ship { Id = 0, Name = "BLU Carrier", Spec = ShipClassSpec.Of(ShipClass.Carrier), Faction = blue, Combat = combat };
            var carrier = b.AddShip(hull, 0, new DVec3(14, 0, 0), -DVec3.UnitX);
            Assert.That(carrier.Combat.Hangar, Is.EqualTo(new List<Ship> { crippled }), "only the crippled craft stays in its berth");

            var refitted = combat.Refitted(combat.Spec, ShipClass.Carrier, -1);
            Assert.That(refitted.Hangar, Is.SameAs(combat.Hangar), "the refitted hull keeps its own craft");
        }
    }
}
