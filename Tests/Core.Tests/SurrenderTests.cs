using System.Collections.Generic;
using System.Linq;
using UnitySpace.Core;
using NUnit.Framework;

namespace UnitySpace.Core.Tests
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
        public void NoGunFiresOnAHullWaitingOnTerms()
        {
            // A player captor holds the hailing window open; an AI wingman of theirs stands by with its guns.
            var (b, merchant, _) = BatteredMerchant(18, playerPirate: true);
            b.AddShip(Hull(ShipClass.Destroyer, Pirates, 2), 1, new DVec3(-4, 3, 0), DVec3.UnitX);
            for (int i = 0; i < 40 && !merchant.Surrendering; i++) b.Advance(0.1);
            Assert.That(merchant.Surrendering, Is.True);
            // Anything already in flight lands first; then nothing new is fired before terms are settled.
            b.Advance(3);
            double hull = merchant.Combat.Hull;
            for (int i = 0; i < 100 && merchant.SurrenderOutcome == null; i++) b.Advance(0.1);
            Assert.That(merchant.Combat.Hull, Is.EqualTo(hull).Within(1e-9), "a hull that has struck its colours is not shot at");
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
        public void ARoundInFlightSparesAPrizeItsSideHasJustTaken()
        {
            var (b, merchant, pirate) = BatteredMerchant(26);
            double hull = merchant.Combat.Hull;
            b.Rounds.Add(new KineticRound { Pos = pirate.Pos, Vel = DVec3.UnitX, Target = merchant, Damage = 500, ArriveIn = 0.5, Life = 5, Side = pirate.Side, Hit = true });
            merchant.Side = pirate.Side; // taken by the pirates while the round was on its way
            merchant.Captured = true;
            b.Advance(1);
            Assert.That(merchant.Combat.Hull, Is.EqualTo(hull).Within(1e-9));
        }

        [Test]
        public void PodsAreNotFiredAtAHullWaitingOnTerms()
        {
            var (b, merchant, pirate) = BatteredMerchant(25, playerPirate: true);
            // A second raider, AI-flown and carrying pods, standing by while the player decides.
            var podder = b.AddShip(Hull(ShipClass.Destroyer, Pirates, 3), 1, new DVec3(-4, 2, 0), DVec3.UnitX);
            Assert.That(podder.Combat.Subsystems.Any(s => s.Kind == SubsystemKind.BoardingPods), Is.True, "the test needs a pod launcher");
            b.Surrender(merchant); // before any pod is away
            int pods = podder.Combat.Subsystems.Where(s => s.Kind == SubsystemKind.BoardingPods).Sum(s => s.Ammo);
            for (int i = 0; i < 12; i++) b.Advance(1);
            Assert.That(merchant.SurrenderOutcome, Is.Null, "still inside the hailing window");
            Assert.That(podder.Combat.Subsystems.Where(s => s.Kind == SubsystemKind.BoardingPods).Sum(s => s.Ammo), Is.EqualTo(pods));
        }

        [Test]
        public void AnUnarmedPlayerHullWithMarinesDoesNotHoldADeadFightOpen()
        {
            var b = NewBattle(27);
            var player = b.AddShip(Hull(ShipClass.Destroyer, Navy, 0), 0, new DVec3(-3, 0, 0), DVec3.UnitX);
            player.PlayerControlled = true;
            foreach (var s in player.Combat.Subsystems) if (s.Def.Weapon != null) s.Hp = 0;
            var hulk = b.AddShip(Hull(ShipClass.Freighter, Traders, 1), 1, new DVec3(3, 0, 0), -DVec3.UnitX);
            hulk.Combat.Hull = hulk.Combat.Spec.Hull * 0.1; // disabled: can't run, fight or strike
            Assert.That(player.Combat.MarinesAvailable >= 15, Is.True);
            for (int i = 0; i < 600 && !b.Over; i++) b.Advance(1);
            Assert.That(b.Over, Is.True, "a gunless player hull that can't board kept the battle open");
        }

        [Test]
        public void TheAiDoesNotDecideToRunForAPlayerFlownHull()
        {
            var b = NewBattle(24);
            var freighter = Hull(ShipClass.Freighter, Traders, 0);
            var player = b.AddShip(freighter, 0, new DVec3(6, 0, 0), -DVec3.UnitX);
            player.PlayerControlled = true;
            b.AddShip(Hull(ShipClass.Destroyer, Pirates, 1), 1, new DVec3(-6, 0, 0), DVec3.UnitX);
            b.Advance(2);
            Assert.That(player.Fleeing, Is.False, "a merchant hull the player flies was set running by the AI");
        }

        [Test]
        public void APlayerAloneCannotOrderBoardersAwayItHasNoWayToSend()
        {
            var (b, merchant, pirate) = BatteredMerchant(21, playerPirate: true);
            b.Advance(2);
            Assert.That(merchant.Surrendering, Is.True);
            Assert.That(pirate.Combat.MarinesAvailable >= 15, Is.True);
            Assert.That(b.OfferSurrenderTerms(merchant, SurrenderTerms.TakeShip), Is.False,
                "only the AI flies a boarding approach; taking the ship would leave it waiting for ever");
            b.Advance(Battle.SurrenderDecisionSeconds + 3);
            Assert.That(merchant.SurrenderOutcome, Is.Not.EqualTo(SurrenderTerms.TakeShip));
        }

        [Test]
        public void AHullTakenWhileStillHailingIsNotThenLetGo()
        {
            var (b, merchant, pirate) = BatteredMerchant(23, playerPirate: true);
            int decided = 0;
            b.SurrenderDecided += (_, u, captor, terms) => decided++;
            // Boarders already aboard in strength when it strikes its colours: they take it inside the hailing window.
            var interior = merchant.Combat.Interior;
            interior.Board(Pirates, 1.2, 40, interior.Of(CompartmentKind.Midships) ?? interior.Nearest(0, 0), false, 0, new System.Random(1));
            foreach (var c in interior.Compartments)
            {
                c.Crew = c.Marines = 0;
                c.Boarders = 5;
            }
            b.Surrender(merchant);
            for (int i = 0; i < 14 && !merchant.Captured; i++) b.Advance(1);
            Assert.That(merchant.Captured, Is.True, "the boarders should have taken it inside the window");
            b.Advance(Battle.SurrenderDecisionSeconds + 5);
            Assert.That(decided, Is.EqualTo(0), "terms were settled on a ship already taken");
            Assert.That(merchant.Fled, Is.False, "the prize was released as if it had been let go");
        }

        [Test]
        public void ABoarderCrippledOnItsApproachGivesUpThePrize()
        {
            var b = NewBattle(22);
            var raider = b.AddShip(Hull(ShipClass.Destroyer, Pirates, 0), 0, new DVec3(-3, 0, 0), DVec3.UnitX);
            var wreck = b.AddShip(Hull(ShipClass.Freighter, Traders, 1), 1, new DVec3(3, 0, 0), -DVec3.UnitX);
            wreck.Combat.Hull = wreck.Combat.Spec.Hull * 0.1;
            b.Advance(2);
            Assert.That(raider.BoardTarget, Is.SameAs(wreck));
            raider.Combat.Hull = raider.Combat.Spec.Hull * 0.1;
            Assert.That(raider.Combat.Disabled, Is.True);
            b.Advance(1);
            Assert.That(raider.BoardTarget, Is.Null, "a crippled boarder that can't close would hold the battle open");
        }

        [Test]
        public void AnArmedWarshipThatStrikesItsColoursCanStillBeBoarded()
        {
            // Its own guns are powered down: a hull waiting on terms is no threat to the boarders it asked for.
            var b = NewBattle(20);
            var navy = b.AddShip(Hull(ShipClass.Destroyer, Navy, 0), 0, new DVec3(-4, 0, 0), DVec3.UnitX);
            var raider = b.AddShip(Hull(ShipClass.Destroyer, Pirates, 1), 1, new DVec3(4, 0, 0), -DVec3.UnitX);
            Assert.That(raider.Combat.Armed, Is.True);
            navy.PlayerControlled = true;
            b.Surrender(raider);
            b.Advance(1);
            navy.PlayerControlled = false;
            Assert.That(b.OfferSurrenderTerms(raider, SurrenderTerms.TakeShip), Is.True);
            for (int i = 0; i < 1200 && !raider.Captured; i++) b.Advance(1);
            Assert.That(raider.Captured, Is.True, "nobody would close on a surrendered warship to board it");
        }

        [Test]
        public void ASurrenderedShipIsTakenOnceAndStaysWithItsCaptor()
        {
            var (b, merchant, pirate) = BatteredMerchant(15, playerPirate: true);
            int captures = 0;
            b.UnitCaptured += (_, u, captor) => captures++;
            b.Advance(2);
            pirate.PlayerControlled = false;
            Assert.That(b.OfferSurrenderTerms(merchant, SurrenderTerms.TakeShip), Is.True);
            for (int i = 0; i < 1200 && !merchant.Captured; i++) b.Advance(1);
            Assert.That(merchant.Captured, Is.True);
            int side = merchant.Side;
            for (int i = 0; i < 600; i++) b.Advance(1);
            Assert.That(captures, Is.EqualTo(1), "the prize must be captured exactly once");
            Assert.That(merchant.Side, Is.EqualTo(side), "the prize must not flip back to its old side");
            Assert.That(merchant.Combat.Interior.UnderBoarding, Is.False, "no fight goes on aboard a ship already taken");
        }

        [Test]
        public void APlayerFlownHullNeverPicksABoardingTargetItCannotFlyTo()
        {
            // The AI would close in and latch; a player-flown hull is flown by the player, so a
            // boarding target would only hold its own guns off the prize and keep the battle open.
            var b = NewBattle(19);
            var player = b.AddShip(Hull(ShipClass.Destroyer, Pirates, 0), 0, new DVec3(-3, 0, 0), DVec3.UnitX);
            player.PlayerControlled = true;
            var wreck = b.AddShip(Hull(ShipClass.Freighter, Traders, 1), 1, new DVec3(3, 0, 0), -DVec3.UnitX);
            wreck.Combat.Hull = wreck.Combat.Spec.Hull * 0.1;
            Assert.That(wreck.Combat.Disabled, Is.True);
            Assert.That(player.Combat.MarinesAvailable >= 15, Is.True);
            b.Advance(5);
            Assert.That(player.BoardTarget, Is.Null);
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
