using System.Collections.Generic;
using System.Linq;
using SolarSystem.Core;
using Xunit;

namespace SolarSystem.Core.Tests
{
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

        [Fact]
        public void ANavyCrewHoldsOnLongerThanPiratesOrMerchants()
        {
            Assert.True(Morale.Resolve(Navy, true) > Morale.Resolve(Pirates, false));
            Assert.True(Morale.Resolve(Navy, true) > Morale.Resolve(Traders, false));
            Assert.Equal(0, Morale.DamageToll(1, 1), 6);
            Assert.Equal(1, Morale.DamageToll(0, 0), 6);
            Assert.True(Morale.DamageToll(0.5, 1) > Morale.DamageToll(1, 0.5), "hull loss should weigh more than a dropped screen");
            Assert.Equal(0, Morale.Outgunned(10, 5), 6);
            Assert.Equal(1, Morale.Outgunned(0, 5), 6);
        }

        [Fact]
        public void AnUnhurtShipNeverStrikesItsColoursHoweverBadTheOdds()
        {
            Assert.False(Morale.StrikesColours(0.1, 1, 0.5, 0.55));
            Assert.True(Morale.StrikesColours(0.5, 1, 0.5, 1.05));
            Assert.False(Morale.StrikesColours(0.5, 0.2, Morale.Resolve(Navy, true), 0.55), "a navy crew a little behind should fight on");
        }

        [Fact]
        public void PiratesRansomCargoOrTakeTheShipAndNaviesTakeOnlyWarships()
        {
            Assert.Equal(SurrenderTerms.RansomCargo, Morale.ChooseTerms(true, false, true, true, 0.1));
            Assert.Equal(SurrenderTerms.TakeShip, Morale.ChooseTerms(true, false, true, true, 0.9));
            Assert.Equal(SurrenderTerms.TakeShip, Morale.ChooseTerms(false, true, false, true, 0.5));
            Assert.Equal(SurrenderTerms.LetGo, Morale.ChooseTerms(false, false, true, true, 0.1));
            // Nobody to board with: settle for the cargo, or let it go.
            Assert.Equal(SurrenderTerms.RansomCargo, Morale.ChooseTerms(true, false, true, false, 0.9));
            Assert.Equal(SurrenderTerms.LetGo, Morale.ChooseTerms(false, true, false, false, 0.5));
        }

        [Fact]
        public void PiratesDemandCargoOnlyWithTheUpperHand()
        {
            Assert.True(Morale.ShouldDemandCargo(0.5, 0));
            Assert.True(Morale.ShouldDemandCargo(0.15, 0.5));
            Assert.False(Morale.ShouldDemandCargo(0.15, 0.1));
            Assert.False(Morale.ShouldDemandCargo(0, 0.9));
        }

        // ---------------------------------------------------------------- in a battle

        [Fact]
        public void ABatteredMerchantStrikesItsColoursAndIsNoLongerShotAt()
        {
            var (b, merchant, pirate) = BatteredMerchant(11);
            var hailed = new List<BattleUnit>();
            b.UnitSurrendered += (_, u) => hailed.Add(u);
            b.Advance(2);
            Assert.True(merchant.Surrendering, "a half-wrecked, outgunned freighter should strike its colours");
            Assert.Equal(new[] { merchant }, hailed);
            Assert.Contains(b.Fx, f => f.Kind == FxKind.Hail && f.Unit == merchant);
            Assert.NotSame(merchant, b.PickTarget(pirate));
            Assert.False(b.Over, "the battle waits on the captor's terms");
        }

        [Fact]
        public void AnAiCaptorSettlesTermsAndAReleasedHullLeaves()
        {
            var (b, merchant, _) = BatteredMerchant(12);
            var decided = new List<(Faction, SurrenderTerms)>();
            b.SurrenderDecided += (_, u, captor, terms) => decided.Add((captor, terms));
            b.Advance(Battle.SurrenderAiDecisionSeconds + 3);
            Assert.Single(decided);
            Assert.Same(Pirates, decided[0].Item1);
            Assert.NotNull(merchant.SurrenderOutcome);
            if (merchant.SurrenderOutcome != SurrenderTerms.TakeShip) Assert.True(merchant.Fled);
            else Assert.True(merchant.SurrenderOpenToBoarding);
        }

        [Fact]
        public void APlayerCaptorChoosesTheTermsWithinTheHailingWindow()
        {
            var (b, merchant, _) = BatteredMerchant(13, playerPirate: true);
            SurrenderTerms? chosen = null;
            b.SurrenderDecided += (_, u, captor, terms) => chosen = terms;
            b.Advance(Battle.SurrenderAiDecisionSeconds + 3);
            Assert.True(merchant.Surrendering);
            Assert.Null(merchant.SurrenderOutcome);
            Assert.Same(merchant, b.AwaitingTermsFrom(1));

            Assert.True(b.OfferSurrenderTerms(merchant, SurrenderTerms.RansomCargo));
            Assert.Equal(SurrenderTerms.RansomCargo, chosen);
            Assert.True(merchant.Fled);
            Assert.False(b.OfferSurrenderTerms(merchant, SurrenderTerms.LetGo), "terms are settled once");
        }

        [Fact]
        public void AnUnansweredHailIsDecidedByTheBattleOnceTheWindowCloses()
        {
            var (b, merchant, _) = BatteredMerchant(14, playerPirate: true);
            b.Advance(Battle.SurrenderDecisionSeconds + 3);
            Assert.NotNull(merchant.SurrenderOutcome);
        }

        [Fact]
        public void TakingASurrenderedShipSendsBoardersAndTheCrewYields()
        {
            var (b, merchant, pirate) = BatteredMerchant(15, playerPirate: true);
            Faction takenBy = null;
            b.UnitCaptured += (_, u, captor) => takenBy = captor;
            b.Advance(2);
            pirate.PlayerControlled = false;
            Assert.True(pirate.Combat.MarinesAvailable >= 15, "the test destroyer needs marines to board with");
            Assert.True(b.OfferSurrenderTerms(merchant, SurrenderTerms.TakeShip));
            for (int i = 0; i < 1200 && !merchant.Captured; i++) b.Advance(1);
            Assert.True(merchant.Captured, "the boarders never took the surrendered freighter");
            Assert.Same(Pirates, takenBy);
        }

        [Fact]
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
            Assert.Equal(new[] { Pirates }, demands);
            Assert.Same(Pirates, player.CargoDemandedBy);
            Assert.True(b.AnswerCargoDemand(player, true));
            Assert.Equal(SurrenderTerms.RansomCargo, paid);
            Assert.True(player.Fled);
        }

        [Fact]
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
            Assert.Equal(1, demands);
            b.Advance(Battle.SurrenderDecisionSeconds + 2);
            Assert.Null(player.CargoDemandedBy);
            Assert.Null(player.SurrenderOutcome);
            Assert.Equal(1, demands);
            Assert.False(b.AnswerCargoDemand(player, true), "a lapsed demand can't be answered");
        }
    }
}
