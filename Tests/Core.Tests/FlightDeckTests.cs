using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Flight decks you fly through in person (U.SPC-T518, <see cref="FlightDecks"/>): a through-hull bay along each of a
    /// carrier's hangars; craft launch out of the bow mouth, fly the approach to the gate astern, are guided in along the
    /// glide path through the stern mouth, turned round on the deck and launched again; the player's craft lined up on the
    /// glide path is taken in the same way.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class FlightDeckTests
    {
        const double StartJd = 2461324.0;

        static Ship Hull(ShipClass c, Faction f, int id) =>
            new Ship { Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c) };

        static (Battle b, BattleUnit carrier, BattleUnit enemy) NewBattle()
        {
            var blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
            var red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 5) { Launch = FighterAI.Launch, FighterStep = FighterAI.Step };
            var carrier = b.AddShip(Hull(ShipClass.Carrier, blue, 0), 0, new DVec3(14, 0, 0), -DVec3.UnitX);
            var enemy = b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(-14, 0, 0), DVec3.UnitX);
            return (b, carrier, enemy);
        }

        [Test]
        public void ACarrierHasAFlightDeckThroughEachHangarOpenAtBothEnds()
        {
            var (_, carrier, _) = NewBattle();
            var decks = FlightDecks.Of(carrier);
            Assert.That(decks.Count, Is.EqualTo(carrier.Combat.Subsystems.Count(s => s.Kind == SubsystemKind.Hangar)));
            foreach (var d in decks)
            {
                Assert.That(d.Open, Is.True);
                Assert.That(d.SternAlong, Is.LessThan(0), "the stern mouth lies astern of the deck's middle");
                Assert.That(d.BowAlong, Is.GreaterThan(0), "the bow mouth ahead of it");
                var stern = carrier.ToLocal(d.Stern);
                var bow = carrier.ToLocal(d.Bow);
                Assert.That(stern.Z, Is.GreaterThanOrEqualTo(-0.5 - 1e-9).And.LessThan(bow.Z), "both mouths are in the hull, stern aft of bow");
                Assert.That(bow.Z, Is.LessThanOrEqualTo(0.5 + 1e-9));
                Assert.That(carrier.ToLocal(d.Gate).Z, Is.LessThan(stern.Z), "the approach gate is astern of the stern mouth");
            }
            Assert.That(FlightDecks.Of(AFighter()).Count, Is.EqualTo(0), "a fighter has no deck");
        }

        static BattleUnit AFighter()
        {
            var (b, _, _) = NewBattle();
            return b.Units.First(u => u.Kind == UnitKind.Fighter);
        }

        [Test]
        public void TheSquadronsLaunchOutOfTheBowMouthsInColumn()
        {
            var (b, carrier, _) = NewBattle();
            var craft = b.Units.Where(u => u.Kind == UnitKind.Fighter).ToList();
            Assert.That(craft, Is.Not.Empty);
            Assert.That(craft.All(u => u.Fighter.Deck != null && u.Fighter.Deck.Stage == DeckStage.Launching), Is.True, "every craft goes out through a deck");
            for (int i = 0; i < 20; i++) b.Advance(1);
            Assert.That(craft.All(u => u.Fighter.Deck == null), Is.True, "all clear of the bows and flying");
        }

        [Test]
        public void AHurtCraftFliesTheApproachLandsIsTurnedRoundAndLaunchesAgain()
        {
            var (b, carrier, _) = NewBattle();
            for (int i = 0; i < 10; i++) b.Advance(1);
            var craft = b.Units.First(u => u.Kind == UnitKind.Fighter && u.Present && u.Fighter.Deck == null);
            craft.Combat.Hull = craft.Combat.Spec.Hull * 0.2;
            bool sawFinal = false, sawDeck = false, sawLaunch = false;
            for (int i = 0; i < 900 && !b.Over && !craft.Dead; i++)
            {
                b.Advance(1);
                var stage = craft.Fighter.Deck?.Stage;
                sawFinal |= stage == DeckStage.Final;
                if (stage == DeckStage.OnDeck)
                {
                    sawDeck = true;
                    Assert.That(craft.Docked, Is.True, "down on the deck it is inside the hull");
                    var local = carrier.ToLocal(craft.Pos);
                    Assert.That(System.Math.Abs(local.Z), Is.LessThanOrEqualTo(0.5), "on a spot inside the hull");
                }
                if (stage == DeckStage.Launching && sawDeck) sawLaunch = true;
                if (sawLaunch && craft.Fighter.Deck == null) break;
            }
            if (craft.Dead) Assert.Inconclusive("shot down on the way home");
            Assert.That(sawFinal, Is.True, "it was guided in on the glide path");
            Assert.That(sawDeck, Is.True, "it landed");
            Assert.That(sawLaunch, Is.True, "and launched again");
            Assert.That(craft.Combat.HullFraction, Is.EqualTo(1).Within(1e-9), "repaired on the deck");
            Assert.That(craft.Docked, Is.False);
        }

        [Test]
        public void ThePlayersCraftLinedUpOnTheGlidePathIsTakenIn()
        {
            var (b, carrier, _) = NewBattle();
            for (int i = 0; i < 10; i++) b.Advance(1);
            var craft = b.Units.First(u => u.Kind == UnitKind.Fighter && u.Present && u.Fighter.Deck == null);
            var seat = new FighterSeat(b, craft);
            seat.TakeCharge();
            var deck = FlightDecks.Of(carrier).First(d => d.Open);
            craft.Pos = deck.At(deck.SternAlong - FlightDecks.ApproachKm * 0.5, DVec3.Zero);
            craft.Vel = carrier.Vel + carrier.Forward * 0.1;
            craft.Forward = carrier.Forward;
            Assert.That(FlightDecks.Capturing(b, craft), Is.EqualTo(deck));
            Assert.That(FlightDecks.Guidance(b, craft), Does.StartWith("DECK"));
            FighterAI.FlyManned(b, craft, b.Flown[craft], 1);
            Assert.That(craft.Fighter.Deck?.Stage, Is.EqualTo(DeckStage.Final), "the deck has it");
            for (int i = 0; i < 60 && craft.Fighter.Deck?.Stage == DeckStage.Final; i++) b.Advance(1);
            Assert.That(craft.Fighter.Deck?.Stage, Is.EqualTo(DeckStage.OnDeck));
            Assert.That(seat.CanEmbody, Is.True, "still the player's while it is turned round");
            Assert.That(FlightDecks.Guidance(b, craft), Does.StartWith("ON DECK"));
        }

        [Test]
        public void TheTurnaroundRefuelsRearmsAndRepairs()
        {
            var (b, _, _) = NewBattle();
            var craft = b.Units.First(u => u.Kind == UnitKind.Fighter && u.Fighter.Bomber);
            var c = craft.Combat;
            c.Hull = 1;
            c.BoostFraction = 0;
            foreach (var s in c.Subsystems)
                if (s.Def.Weapon != null) s.Ammo = 0;
            Assert.That(FlightDecks.OutOfOrdnance(c), Is.True);
            FlightDecks.Service(craft, 1);
            Assert.That(c.HullFraction, Is.EqualTo(1).Within(1e-9));
            Assert.That(c.BoostFraction, Is.EqualTo(1));
            Assert.That(FlightDecks.OutOfOrdnance(c), Is.False);
        }
    }
}
