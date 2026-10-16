using System.Collections.Generic;
using System.Linq;
using SolarSystem.Core;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    [TestFixture]
    public class SurrenderTests
    {
        const double StartJd = 2461324.0;

        static readonly Faction Navy = new Faction("Navy", "NAV", new Rgb(0, 0, 1)) { IsMilitary = true, MarineQuality = 1.2 };
        static readonly Faction Pirates = new Faction("Pirates", "PIR", new Rgb(1, 0, 0)) { IsPirate = true, MarineQuality = 1.1 };
        static readonly Faction Traders = new Faction("Traders", "TRD", new Rgb(0, 1, 0));

        static Ship Hull(ShipClass c, Faction f, int id) =>
            new Ship { Id = id, Name = c + "" + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c) };

        static Battle NewBattle(int seed) => new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed)
        {
            Launch = FighterAI.Launch,
            FighterStep = FighterAI.Step,
        };

        /// <summary>A half-wrecked, laden freighter with its screens down, facing a pirate destroyer.</summary>
        static (Battle b, BattleUnit merchant, BattleUnit pirate) BatteredMerchant(int seed, bool playerPirate = false)
        {
            var b = NewBattle(seed);
            var freighter = Hull(ShipClass.Freighter, Traders, 0);
            freighter.HasCargo = true;
            freighter.CargoGood = Good.Food;
            freighter.CargoTonnes = 200;
            var merchant = b.AddShip(freighter, 0, new DVec3(4, 0, 0), -DVec3.UnitX);
            var pirate = b.AddShip(Hull(ShipClass.Destroyer, Pirates, 1), 1, new DVec3(-4, 0, 0), DVec3.UnitX);
            pirate.PlayerControlled = playerPirate;
            merchant.Combat.Hull = merchant.Combat.Spec.Hull * 0.5;
            merchant.Combat.ZeroAllShields();
            return (b, merchant, pirate);
        }

        // ---------------------------------------------------------------- the morale model

        [Test]
        public void ANavyCrewHoldsOnLongerThanPiratesOrMerchants()
        {
            Assert.That(Morale.Resolve(Navy, true) > Morale.Resolve(Pirates, false), Is.True);
            Assert.That(Morale.Resolve(Navy, true) > Morale.Resolve(Traders, false), Is.True);
            Assert.That(Morale.DamageToll(1, 1), Is.EqualTo(0).Within(1e-6));
            Assert.That(Morale.DamageToll(0, 0), Is.EqualTo(1).Within(1e-6));
            Assert.That(Morale.DamageToll(0.5, 1) > Morale.DamageToll(1, 0.5), Is.True, "hull loss should weigh more than a dropped screen");
            Assert.That(Morale.Outgunned(10, 5), Is.EqualTo(0).Within(1e-6));
            Assert.That(Morale.Outgunned(0, 5), Is.EqualTo(1).Within(1e-6));
        }

        [Test]
        public void AnUnhurtShipNeverStrikesItsColoursHoweverBadTheOdds()
        {
            Assert.That(Morale.StrikesColours(0.1, 1, 0.5, 0.55), Is.False);
            Assert.That(Morale.StrikesColours(0.5, 1, 0.5, 1.05), Is.True);
            Assert.That(Morale.StrikesColours(0.5, 0.2, Morale.Resolve(Navy, true), 0.55), Is.False, "a navy crew a little behind should fight on");
        }

        [Test]
        public void PiratesRansomCargoOrTakeTheShipAndNaviesTakeOnlyWarships()
        {
            Assert.That(Morale.ChooseTerms(true, false, true, true, 0.1), Is.EqualTo(SurrenderTerms.RansomCargo));
            Assert.That(Morale.ChooseTerms(true, false, true, true, 0.9), Is.EqualTo(SurrenderTerms.TakeShip));
            Assert.That(Morale.ChooseTerms(false, true, false, true, 0.5), Is.EqualTo(SurrenderTerms.TakeShip));
            Assert.That(Morale.ChooseTerms(false, false, true, true, 0.1), Is.EqualTo(SurrenderTerms.LetGo));
            // Nobody to board with: settle for the cargo, or let it go.
            Assert.That(Morale.ChooseTerms(true, false, true, false, 0.9), Is.EqualTo(SurrenderTerms.RansomCargo));
            Assert.That(Morale.ChooseTerms(false, true, false, false, 0.5), Is.EqualTo(SurrenderTerms.LetGo));
        }

        [Test]
        public void PiratesDemandCargoOnlyWithTheUpperHand()
        {
            Assert.That(Morale.ShouldDemandCargo(0.5, 0), Is.True);
            Assert.That(Morale.ShouldDemandCargo(0.15, 0.5), Is.True);
            Assert.That(Morale.ShouldDemandCargo(0.15, 0.1), Is.False);
            Assert.That(Morale.ShouldDemandCargo(0, 0.9), Is.False);
        }

        // ---------------------------------------------------------------- in a battle

        [Test]
        public void ABatteredMerchantStrikesItsColoursAndIsNoLongerShotAt()
        {
            var (b, merchant, pirate) = BatteredMerchant(11);
            var hailed = new List<BattleUnit>();
            b.UnitSurrendered += (_, u) => hailed.Add(u);
            b.Advance(2);
            Assert.That(merchant.Surrendering, Is.True, "a half-wrecked, outgunned freighter should strike its colours");
            Assert.That(hailed, Is.EqualTo(new[] { merchant }));
            Assert.That((b.Fx).Any(f => f.Kind == FxKind.Hail && f.Unit == merchant), Is.True);
            Assert.That(b.PickTarget(pirate), Is.Not.SameAs(merchant));
            Assert.That(b.Over, Is.False, "the battle waits on the captor's terms");
        }

        [Test]
        public void AnAiCaptorSettlesTermsAndAReleasedHullLeaves()
        {
            var (b, merchant, _) = BatteredMerchant(12);
            var decided = new List<(Faction, SurrenderTerms)>();
            b.SurrenderDecided += (_, u, captor, terms) => decided.Add((captor, terms));
            b.Advance(Battle.SurrenderAiDecisionSeconds + 3);
            Assert.That(decided, Has.Exactly(1).Items);
            Assert.That(decided[0].Item1, Is.SameAs(Pirates));
            Assert.That(merchant.SurrenderOutcome, Is.Not.Null);
            if (merchant.SurrenderOutcome != SurrenderTerms.TakeShip) Assert.That(merchant.Fled, Is.True);
            else Assert.That(merchant.SurrenderOpenToBoarding, Is.True);
        }

        [Test]
        public void APlayerCaptorChoosesTheTermsWithinTheHailingWindow()
        {
            var (b, merchant, _) = BatteredMerchant(13, playerPirate: true);
            SurrenderTerms? chosen = null;
            b.SurrenderDecided += (_, u, captor, terms) => chosen = terms;
            b.Advance(Battle.SurrenderAiDecisionSeconds + 3);
            Assert.That(merchant.Surrendering, Is.True);
            Assert.That(merchant.SurrenderOutcome, Is.Null);
            Assert.That(b.AwaitingTermsFrom(1), Is.SameAs(merchant));

            Assert.That(b.OfferSurrenderTerms(merchant, SurrenderTerms.RansomCargo), Is.True);
            Assert.That(chosen, Is.EqualTo(SurrenderTerms.RansomCargo));
            Assert.That(merchant.Fled, Is.True);
            Assert.That(b.OfferSurrenderTerms(merchant, SurrenderTerms.LetGo), Is.False, "terms are settled once");
        }

        [Test]
        public void AnUnansweredHailIsDecidedByTheBattleOnceTheWindowCloses()
        {
            var (b, merchant, _) = BatteredMerchant(14, playerPirate: true);
            b.Advance(Battle.SurrenderDecisionSeconds + 3);
            Assert.That(merchant.SurrenderOutcome, Is.Not.Null);
        }

        [Test]
        public void TakingASurrenderedShipSendsBoardersAndTheCrewYields()
        {
            var (b, merchant, pirate) = BatteredMerchant(15, playerPirate: true);
            Faction takenBy = null;
            b.UnitCaptured += (_, u, captor) => takenBy = captor;
            b.Advance(2);
            pirate.PlayerControlled = false;
            Assert.That(pirate.Combat.MarinesAvailable >= 15, Is.True, "the test destroyer needs marines to board with");
            Assert.That(b.OfferSurrenderTerms(merchant, SurrenderTerms.TakeShip), Is.True);
            for (int i = 0; i < 1200 && !merchant.Captured; i++) b.Advance(1);
            Assert.That(merchant.Captured, Is.True, "the boarders never took the surrendered freighter");
            Assert.That(takenBy, Is.SameAs(Pirates));
        }

        [Test]
        public void PiratesDemandAPlayersCargoAndComplyingBuysAWayOut()
        {
            var b = NewBattle(16);
            var freighter = Hull(ShipClass.Freighter, Traders, 0);
            freighter.HasCargo = true;
            freighter.CargoGood = Good.Food;
            freighter.CargoTonnes = 200;
            var player = b.AddShip(freighter, 0, new DVec3(6, 0, 0), -DVec3.UnitX);
            player.PlayerControlled = true;
            b.AddShip(Hull(ShipClass.Destroyer, Pirates, 1), 1, new DVec3(-6, 0, 0), DVec3.UnitX);
            b.AddShip(Hull(ShipClass.Destroyer, Pirates, 2), 1, new DVec3(-6, 2, 0), DVec3.UnitX);
            var demands = new List<Faction>();
            SurrenderTerms? paid = null;
            b.CargoDemanded += (_, u, p) => demands.Add(p);
            b.SurrenderDecided += (_, u, captor, terms) => paid = terms;

            b.Advance(2);
            Assert.That(demands, Is.EqualTo(new[] { Pirates }));
            Assert.That(player.CargoDemandedBy, Is.SameAs(Pirates));
            Assert.That(b.AnswerCargoDemand(player, true), Is.True);
            Assert.That(paid, Is.EqualTo(SurrenderTerms.RansomCargo));
            Assert.That(player.Fled, Is.True);
        }

        [Test]
        public void RefusingOrIgnoringACargoDemandMeansAFightAndPiratesAskOnce()
        {
            var b = NewBattle(17);
            var freighter = Hull(ShipClass.Freighter, Traders, 0);
            freighter.HasCargo = true;
            freighter.CargoTonnes = 200;
            var player = b.AddShip(freighter, 0, new DVec3(6, 0, 0), -DVec3.UnitX);
            player.PlayerControlled = true;
            b.AddShip(Hull(ShipClass.Destroyer, Pirates, 1), 1, new DVec3(-6, 0, 0), DVec3.UnitX);
            b.AddShip(Hull(ShipClass.Destroyer, Pirates, 2), 1, new DVec3(-6, 2, 0), DVec3.UnitX);
            int demands = 0;
            b.CargoDemanded += (_, u, p) => demands++;

            b.Advance(2);
            Assert.That(demands, Is.EqualTo(1));
            b.Advance(Battle.SurrenderDecisionSeconds + 2);
            Assert.That(player.CargoDemandedBy, Is.Null);
            Assert.That(player.SurrenderOutcome, Is.Null);
            Assert.That(demands, Is.EqualTo(1));
            Assert.That(b.AnswerCargoDemand(player, true), Is.False, "a lapsed demand can't be answered");
        }
    }
}
