using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>Interactive boarding (U.SPC-T468): a player's standing orders to one side of a fight aboard, over the resolver.</summary>
    [TestFixture]
    public class BoardingCommandTests
    {
        const double StartJd = 2461324.0;

        static readonly Faction Blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true, MarineQuality = 1.2 };
        static readonly Faction Red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsPirate = true, MarineQuality = 1.1 };

        static Interior Inside(ShipClass cls) => new ShipCombat(CombatSpec.Of(cls), cls).Interior;

        static Ship Hull(ShipClass c, Faction f, int id) =>
            new Ship { Id = id, Name = c + "" + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c) };

        [Test]
        public void OrdersAreRefusedWithNoFightAboardOrAPushWithNowhereToGo()
        {
            var interior = Inside(ShipClass.Destroyer);
            Assert.That(interior.Command(BoardingRole.Boarders, BoardingOrder.Hold), Is.False, "ordered a fight that is not happening");
            interior.Board(Red, 1.1, 20, interior.Of(CompartmentKind.Bow), true, 0, new Random(1));
            Assert.That(interior.Command(BoardingRole.Boarders, BoardingOrder.Push), Is.False);
            Assert.That(interior.Command(BoardingRole.Boarders, BoardingOrder.Push, interior.Compartments.Count), Is.False);
            Assert.That(interior.Commanded, Is.False);
            Assert.That(interior.Command(BoardingRole.Defenders, BoardingOrder.Push, interior.Of(CompartmentKind.Bridge).Index), Is.True);
            Assert.That(BoardingCommand.Label(interior, BoardingRole.Defenders), Does.Contain("Bridge"));
        }

        [Test]
        public void HandedBackToAutoTheFightIsTheResolversExactly()
        {
            Interior Run(bool commandedThenHandedBack)
            {
                var interior = Inside(ShipClass.Cruiser);
                var rng = new Random(9);
                interior.Board(Red, 1.1, 60, interior.Of(CompartmentKind.Bow), true, 0, rng);
                if (commandedThenHandedBack)
                {
                    interior.Command(BoardingRole.Boarders, BoardingOrder.Hold);
                    interior.Command(BoardingRole.Defenders, BoardingOrder.Push, 0);
                    interior.AutoResolve(BoardingRole.Boarders);
                    interior.AutoResolve(BoardingRole.Defenders);
                }
                for (int s = 0; s < 1800 && interior.Boarder != null && !interior.Captured; s++) interior.Step(1, s, rng);
                return interior;
            }
            var plain = Run(false);
            var auto = Run(true);
            Assert.That(auto.Captured, Is.EqualTo(plain.Captured));
            Assert.That(auto.BoardersLost, Is.EqualTo(plain.BoardersLost));
            Assert.That(auto.MarinesLost, Is.EqualTo(plain.MarinesLost));
            Assert.That(auto.CrewLost, Is.EqualTo(plain.CrewLost));
            Assert.That(auto.Log.Select(n => n.Text), Is.EqualTo(plain.Log.Select(n => n.Text)));
        }

        [Test]
        public void BoardersSentForAnObjectiveTakeItThenPressOnAsTheResolverWould()
        {
            var interior = Inside(ShipClass.Destroyer);
            var rng = new Random(2);
            var barracks = interior.Of(CompartmentKind.Barracks);
            interior.Board(Red, 2.0, 200, interior.Of(CompartmentKind.Bow), true, 0, rng);
            Assert.That(interior.Command(BoardingRole.Boarders, BoardingOrder.Push, barracks.Index), Is.True);
            bool barracksTaken = false;
            for (int s = 0; s < 3600 && !interior.Captured && interior.Boarder != null; s++)
            {
                interior.Step(1, s, rng);
                barracksTaken |= barracks.Taken;
            }
            foreach (var note in interior.Log) TestContext.Out.WriteLine($"{note.Seconds,5:0}s  {note.Text}");
            Assert.That(barracksTaken, Is.True, "the boarders never took the barracks they were sent for");
            Assert.That(interior.Log.Any(n => n.Text.Contains("taken as ordered")), Is.True);
            Assert.That(interior.Captured, Is.True, "having taken their objective, the boarders did not go on to take the ship");
        }

        [Test]
        public void LeftToTheResolverBoardersNeverGoIntoTheBarracks()
        {
            var interior = Inside(ShipClass.Destroyer);
            var rng = new Random(2);
            var barracks = interior.Of(CompartmentKind.Barracks);
            interior.Board(Red, 2.0, 200, interior.Of(CompartmentKind.Bow), true, 0, rng);
            for (int s = 0; s < 3600 && !interior.Captured && interior.Boarder != null; s++)
            {
                interior.Step(1, s, rng);
                Assert.That(barracks.Boarders, Is.EqualTo(0), "the resolver alone sent boarders into the barracks");
            }
        }

        [Test]
        public void BoardersToldToDigInHoldWhereTheyLanded()
        {
            var interior = Inside(ShipClass.Destroyer);
            var rng = new Random(3);
            var bow = interior.Of(CompartmentKind.Bow);
            interior.Board(Red, 2.0, 200, bow, true, 0, rng);
            interior.Command(BoardingRole.Boarders, BoardingOrder.Hold);
            for (int s = 0; s < 300; s++)
            {
                interior.Step(1, s, rng);
                foreach (var c in interior.Compartments)
                    if (c != bow) Assert.That(c.Boarders, Is.EqualTo(0), "a party left the bow while ordered to dig in");
            }
            Assert.That(bow.Taken, Is.True);
            Assert.That(interior.Captured, Is.False);
        }

        [Test]
        public void DefendersMusteredOnTheBridgeGatherThereInsteadOfAtTheBreach()
        {
            double OnBridge(bool ordered)
            {
                var interior = Inside(ShipClass.Cruiser);
                var rng = new Random(4);
                interior.Board(Red, 1.1, 20, interior.Of(CompartmentKind.Bow), true, 0, rng);
                if (ordered) interior.Command(BoardingRole.Defenders, BoardingOrder.Push, interior.Of(CompartmentKind.Bridge).Index);
                for (int s = 0; s < 120; s++) interior.Step(1, s, rng);
                return interior.Of(CompartmentKind.Bridge).Marines;
            }
            double auto = OnBridge(false), mustered = OnBridge(true);
            TestContext.Out.WriteLine($"marines on the bridge: auto {auto:0}, mustered {mustered:0}");
            Assert.That(auto, Is.LessThan(1));
            Assert.That(mustered, Is.GreaterThan(100));
        }

        [Test]
        public void DefendersToldToHoldTheirPostsStayInTheBarracks()
        {
            var interior = Inside(ShipClass.Cruiser);
            var rng = new Random(5);
            var barracks = interior.Of(CompartmentKind.Barracks);
            double before = barracks.Marines;
            interior.Board(Red, 1.1, 20, interior.Of(CompartmentKind.Bow), true, 0, rng);
            interior.Command(BoardingRole.Defenders, BoardingOrder.Hold);
            for (int s = 0; s < 120; s++) interior.Step(1, s, rng);
            Assert.That(barracks.Marines, Is.EqualTo(before).Within(1e-9));
        }

        [Test]
        public void ARepulsedBoardingAndAFreshOneStartWithTheResolversOrders()
        {
            var interior = Inside(ShipClass.Cruiser);
            var rng = new Random(6);
            interior.Board(Red, 1.0, 5, interior.Of(CompartmentKind.Bow), true, 0, rng);
            interior.Command(BoardingRole.Defenders, BoardingOrder.Push, 0);
            for (int s = 0; s < 3600 && interior.Boarder != null; s++) interior.Step(1, s, rng);
            Assert.That(interior.Boarder, Is.Null);
            Assert.That(interior.Commanded, Is.False);
            interior.Board(Red, 1.0, 5, interior.Of(CompartmentKind.Bow), true, 0, rng);
            Assert.That(interior.Commanded, Is.False);
        }

        [Test]
        public void APlayerCommandsTheFightAboardTheirOwnHullOrTheirMarinesAndOrdersLapseWhenTheyLeave()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 11) { Launch = FighterAI.Launch, FighterStep = FighterAI.Step };
            var cruiser = b.AddShip(Hull(ShipClass.Cruiser, Blue, 0), 0, new DVec3(5, 0, 0), -DVec3.UnitX);
            var destroyer = b.AddShip(Hull(ShipClass.Destroyer, Red, 1), 1, new DVec3(-3, 0, 0), DVec3.UnitX);
            Assert.That(BoardingCommand.FightFor(b, cruiser), Is.Null);

            var inside = destroyer.Combat.Interior;
            inside.Board(Blue, 1.2, 60, inside.Of(CompartmentKind.Bow), true, 0, new Random(1));
            cruiser.PlayerControlled = true;
            var fight = BoardingCommand.FightFor(b, cruiser);
            Assert.That(fight, Is.Not.Null);
            Assert.That(fight.Value.hull, Is.EqualTo(destroyer));
            Assert.That(fight.Value.role, Is.EqualTo(BoardingRole.Boarders));
            // The destroyer's own helm defends it.
            Assert.That(BoardingCommand.FightFor(b, destroyer).Value.role, Is.EqualTo(BoardingRole.Defenders));

            Assert.That(inside.Command(BoardingRole.Boarders, BoardingOrder.Hold), Is.True);
            Assert.That(inside.Command(BoardingRole.Defenders, BoardingOrder.Hold), Is.True);
            b.Advance(1);
            Assert.That(inside.BoarderOrder, Is.EqualTo(BoardingOrder.Hold), "the player's order lapsed while they were still at the helm");
            Assert.That(inside.DefenderOrder, Is.EqualTo(BoardingOrder.Auto), "defender orders stood with nobody at the destroyer's helm");

            cruiser.PlayerControlled = false;
            b.Advance(1);
            Assert.That(inside.BoarderOrder, Is.EqualTo(BoardingOrder.Auto), "the order stood after the player left");
        }
    }
}
