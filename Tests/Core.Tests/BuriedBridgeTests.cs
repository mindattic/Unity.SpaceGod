using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Warships' bridges buried at the centre of the hull (U.SPC-T492): no viewports and nothing outside, protected by the
    /// hull's full thickness so fire reaches the bridge only once the hull is holed deep, collisions never do, and in the
    /// boarding interior it sits at the centre, opening only off midships, so boarders reach it only by fighting through.
    /// </summary>
    [TestFixture]
    public class BuriedBridgeTests
    {
        const double StartJd = 2461324.0;

        static readonly ShipClass[] Warships =
        {
            ShipClass.Carrier, ShipClass.Battleship, ShipClass.Cruiser, ShipClass.Destroyer,
            ShipClass.CommandCruiser, ShipClass.Frigate,
        };

        static readonly Faction Blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true, MarineQuality = 1.2 };
        static readonly Faction Red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true, MarineQuality = 1.1 };

        static Ship Hull(ShipClass c, Faction f, int id) => new Ship
        {
            Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c),
        };

        static (Battle b, BattleUnit target) Target(ShipClass c)
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 5);
            var target = b.AddShip(Hull(c, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            b.AddShip(Hull(ShipClass.Destroyer, Red, 1), 1, new DVec3(0, 0, 40), DVec3.UnitX);
            target.Combat.ZeroAllShields();
            return (b, target);
        }

        static Subsystem BridgeOf(BattleUnit u) => u.Combat.Subsystems.First(s => s.Kind == SubsystemKind.Bridge);

        [Test]
        public void EveryWarshipsBridgeIsBuriedAtTheCentreOfItsHull()
        {
            foreach (var c in Warships)
            {
                var bridge = CombatSpec.Of(c).Layout.Single(d => d.Kind == SubsystemKind.Bridge);
                Assert.That(bridge.Buried, Is.True, c + "'s bridge is buried");
                Assert.That(Math.Abs(bridge.X) + Math.Abs(bridge.Y) + Math.Abs(bridge.Z), Is.EqualTo(0).Within(1e-9), c + "'s bridge is at the centre");
            }
            // Small craft and merchant hulls keep their cockpits where the pilots can see out.
            Assert.That(CombatSpec.Of(ShipClass.Freighter).Layout.Where(d => d.Kind == SubsystemKind.Bridge).All(d => !d.Buried), Is.True);
            Assert.That(CombatSpec.Of(ShipClass.Fighter).Layout.Where(d => d.Kind == SubsystemKind.Bridge).All(d => !d.Buried), Is.True);
        }

        [Test]
        public void TheBoardingInteriorPutsTheBridgeAtTheCentreOpeningOnlyOffMidships()
        {
            foreach (var c in Warships)
            {
                var interior = new ShipCombat(CombatSpec.Of(c), c).Interior;
                var bridge = interior.Of(CompartmentKind.Bridge);
                Assert.That(bridge.Buried, Is.True, c.ToString());
                Assert.That(bridge.Z, Is.EqualTo(0).Within(1e-9), c.ToString());
                Assert.That(bridge.Y, Is.EqualTo(0).Within(1e-9), c.ToString());
                Assert.That(bridge.Links.Count, Is.EqualTo(1), c + ": one way in");
                Assert.That(interior.Compartments[bridge.Links[0]].Kind, Is.EqualTo(CompartmentKind.Midships), c + ": the way in is midships");
            }
        }

        [Test]
        public void NoPodBreachOrAirlockOpensIntoTheBuriedBridge()
        {
            foreach (var c in Warships)
            {
                var interior = new ShipCombat(CombatSpec.Of(c), c).Interior;
                for (double z = -0.5; z <= 0.5; z += 0.05)
                    foreach (double y in new[] { -0.2, 0, 0.2 })
                        Assert.That(interior.Nearest(z, y).Kind, Is.Not.EqualTo(CompartmentKind.Bridge), $"{c} at {z:0.00},{y:0.00}");
            }
        }

        [Test]
        public void BoardersFightThroughToTheBuriedBridgeAndTakeTheShip()
        {
            var interior = new ShipCombat(CombatSpec.Of(ShipClass.Destroyer), ShipClass.Destroyer).Interior;
            var rng = new Random(2);
            var bridge = interior.Of(CompartmentKind.Bridge);
            var midships = interior.Compartments[bridge.Links[0]];
            Assert.That(interior.Board(Red, 1.1, 1500, interior.Nearest(0.3, 0), true, 0, rng), Is.True);
            bool taken = false, midshipsFirst = true;
            for (int s = 0; s < 3600 && !taken; s++)
            {
                taken = interior.Step(1, s, rng);
                if (bridge.Boarders > 0.01 && !midships.Taken && midships.Boarders <= 0.01) midshipsFirst = false;
            }
            Assert.That(taken, Is.True, "a big boarding party takes the destroyer");
            Assert.That(bridge.Taken, Is.True);
            Assert.That(midshipsFirst, Is.True, "boarders reach the bridge only through midships");
        }

        [Test]
        public void AHitAimedAtTheBuriedBridgeOfAWholeHullStaysInTheHull()
        {
            var (b, target) = Target(ShipClass.Cruiser);
            var bridge = BridgeOf(target);
            double hp = bridge.Hp, hull = target.Combat.Hull;
            b.ApplyDamage(target, bridge, 500, target.Pos);
            Assert.That(bridge.Hp, Is.EqualTo(hp).Within(1e-9), "the bridge is untouched behind the hull's full thickness");
            Assert.That(target.Combat.Hull, Is.LessThan(hull), "the hull took the hit");
        }

        [Test]
        public void DeepPenetrationReachesTheBuriedBridge()
        {
            var (b, target) = Target(ShipClass.Cruiser);
            var bridge = BridgeOf(target);
            target.Combat.Hull = target.Combat.Spec.Hull * 0.1;
            double hp = bridge.Hp;
            b.ApplyDamage(target, bridge, 200, target.Pos);
            Assert.That(bridge.Hp, Is.LessThan(hp), "a hull holed nine tenths through lets fire reach the bridge");
            Assert.That(BuriedBridges.Reach(target.Combat, bridge), Is.GreaterThan(0.5));
        }

        [Test]
        public void GunnersDoNotAimAtABuriedBridgeUntilTheHullIsHoledDeep()
        {
            var (b, target) = Target(ShipClass.Battleship);
            for (int i = 0; i < 400; i++)
                Assert.That(b.PickSubsystem(target)?.Kind, Is.Not.EqualTo(SubsystemKind.Bridge));
            target.Combat.Hull = target.Combat.Spec.Hull * 0.05;
            bool picked = false;
            for (int i = 0; i < 2000 && !picked; i++) picked = b.PickSubsystem(target)?.Kind == SubsystemKind.Bridge;
            Assert.That(picked, Is.True, "once it can be reached, the bridge is a target again");
        }

        [Test]
        public void ACollisionNeverReachesTheBuriedBridge()
        {
            var combat = new ShipCombat(CombatSpec.Of(ShipClass.Destroyer), ShipClass.Destroyer);
            combat.ZeroAllShields();
            var bridge = combat.Subsystems.First(s => s.Kind == SubsystemKind.Bridge);
            double hp = bridge.Hp;
            foreach (var at in new[] { new DVec3(0, 0.05, 0), new DVec3(0.05, 0, 0), new DVec3(0, -0.05, 0.02) })
                Collisions.ApplyDamage(combat, at, 300);
            Assert.That(bridge.Hp, Is.EqualTo(hp).Within(1e-9));
        }
    }
}
