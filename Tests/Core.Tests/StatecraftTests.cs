using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The diplomacy game (U.SPC-T427, <see cref="Statecraft"/>): states hold opinions of each other, sign pacts (non-aggression,
    /// trade, alliance, tribute, vassalage), put deals to each other and to the player's polity, declare war and make peace;
    /// pacts bind the wars, allies and vassals are called to arms, and tribute moves money and stirs the payer's people.
    /// </summary>
    [TestFixture]
    public class StatecraftTests
    {
        const double StartJd = 2461324.0;

        static SystemSpec Spec(string name) => NearbySystems.All().First(s => s.Name == name);

        /// <summary>Alpha Centauri (the Accord), Barnard's Star (the Federation) and Luhman 16 (the Authority) on a line of gates.</summary>
        static Galaxy Line(out Faction accord, out Faction federation, out Faction authority, out StarSystem ac)
        {
            var g = new Galaxy();
            ac = StarSystem.CreateAlphaCentauri(StartJd, g.Polities);
            var barnard = StarSystem.CreateSettled(Spec("Barnard's Star"), StartJd, g.Polities);
            var luhman = StarSystem.CreateSettled(Spec("Luhman 16"), StartJd, g.Polities);
            g.Systems.Add(ac);
            g.Systems.Add(barnard);
            g.Systems.Add(luhman);
            var net = new GateNetwork();
            net.Link(ac, barnard);
            net.Link(barnard, luhman);
            net.AssignOwners();
            g.Connect(StartJd, net);
            g.Wars.AutoDeclare = false;
            accord = ac.World.Sol;
            federation = barnard.World.Sol;
            authority = luhman.World.Sol;
            return g;
        }

        static double Days(Statecraft s, double fromJd, int days)
        {
            double jd = fromJd;
            for (int i = 0; i < days; i++)
            {
                jd += 1;
                s.Update(jd);
            }
            return jd;
        }

        [Test]
        public void TheStatesArePartiesAndTheirStandingIsReadable()
        {
            var g = Line(out var accord, out var federation, out var authority, out _);
            var s = g.Polities.Statecraft;
            Assert.That(s.Parties(), Does.Contain(accord).And.Contain(federation).And.Contain(authority));
            Assert.That(s.Parties().Any(f => f.IsPirate), Is.False, "raider clans do not treat");
            Assert.That(s.Standing(accord, federation), Is.EqualTo("at peace"));
            Assert.That(s.Opinion(accord, federation), Is.Zero);
        }

        [Test]
        public void AStateWeighsADealByTheSameRulesTheEnvoyReadsAndATradePactPaysBoth()
        {
            var g = Line(out var accord, out var federation, out _, out _);
            var s = g.Polities.Statecraft;
            s.Sway(federation, accord, -60);
            var read = s.Weigh(Deal.TradePact, accord, federation, StartJd);
            Assert.That(read.Accepted, Is.False, "a state that distrusts you will not trade under a pact");
            Assert.That(s.Propose(Deal.TradePact, accord, federation, StartJd, out var answer), Is.Null);
            Assert.That(answer.Accepted, Is.EqualTo(read.Accepted), "the answer is what the envoy read");
            Assert.That(s.PactBetween(accord, federation, PactKind.Trade), Is.Null);

            s.Sway(federation, accord, 120);
            Assert.That(s.Propose(Deal.TradePact, accord, federation, StartJd, out answer), Is.Null);
            Assert.That(answer.Accepted, Is.True);
            Assert.That(s.PactBetween(accord, federation, PactKind.Trade), Is.Not.Null);
            Assert.That(s.Unavailable(Deal.TradePact, federation, accord, StartJd), Is.Not.Null, "one trade pact is enough");

            double a = accord.Treasury, f = federation.Treasury;
            Days(s, StartJd, 10);
            Assert.That(accord.Treasury, Is.GreaterThan(a));
            Assert.That(federation.Treasury, Is.GreaterThan(f));
        }

        [Test]
        public void APactThatKeepsThePeaceStopsAWarUntilItIsBroken()
        {
            var g = Line(out var accord, out var federation, out var authority, out _);
            var s = g.Polities.Statecraft;
            s.Sign(PactKind.NonAggression, accord, authority, StartJd);
            Assert.That(s.BoundToPeace(accord, authority), Is.True);
            Assert.That(g.Wars.Declare(accord, authority, StartJd), Is.Null, "a non-aggression pact keeps the war off");

            double trust = s.Opinion(federation, accord);
            Assert.That(s.DeclareWar(accord, authority, StartJd), Is.Null);
            Assert.That(s.PactBetween(accord, authority, PactKind.NonAggression), Is.Null, "declaring war breaks it");
            Assert.That(g.Polities.Diplomacy.Get(accord, authority), Is.EqualTo(Relation.War));
            Assert.That(g.Wars.Between(accord, authority), Is.Not.Null, "a war with goals across the gates");
            Assert.That(s.Opinion(federation, accord), Is.LessThan(trust), "a broken pact is noticed by everyone");
            Assert.That(s.Opinion(authority, accord), Is.LessThan(-30));
        }

        [Test]
        public void AnAllyIsCalledToArmsAndStandsDownAtThePeace()
        {
            var g = Line(out var accord, out var federation, out var authority, out _);
            var s = g.Polities.Statecraft;
            s.Sign(PactKind.Alliance, federation, authority, StartJd);
            Assert.That(s.DeclareWar(accord, authority, StartJd), Is.Null);
            Assert.That(g.Polities.Diplomacy.Get(federation, accord), Is.EqualTo(Relation.War), "the Federation honours its alliance");
            Assert.That(s.CoBelligerents.Any(c => c.Ally == federation && c.Enemy == accord && c.Friend == authority), Is.True);

            // An ally does not join a war its friend started.
            var g2 = Line(out var accord2, out var federation2, out var authority2, out _);
            var s2 = g2.Polities.Statecraft;
            s2.Sign(PactKind.Alliance, federation2, authority2, StartJd);
            s2.DeclareWar(authority2, accord2, StartJd);
            Assert.That(g2.Polities.Diplomacy.Get(federation2, accord2), Is.EqualTo(Relation.Peace), "an alliance is for defence");

            s.MakePeace(accord, authority, StartJd + 5, "test");
            Days(s, StartJd + 5, 1);
            Assert.That(g.Polities.Diplomacy.Get(federation, accord), Is.EqualTo(Relation.Peace), "its friend at peace, the ally makes peace too");
            Assert.That(s.CoBelligerents, Is.Empty);
        }

        [Test]
        public void TributeMovesMoneyEveryMonthStirsThePayersPeopleAndLapsesWhenItCannotBePaid()
        {
            var g = Line(out var accord, out _, out var authority, out _);
            var s = g.Polities.Statecraft;
            authority.Treasury = 1e9;
            accord.Treasury = 0;
            var port = g.Polities.SettlementsOf(authority).First();
            port.Unrest = 0;
            var p = s.Sign(PactKind.Tribute, authority, accord, StartJd, 3e6);
            Days(s, StartJd, 30);
            Assert.That(accord.Treasury, Is.EqualTo(3e6).Within(1));
            Assert.That(authority.Treasury, Is.EqualTo(1e9 - 3e6).Within(1));
            Assert.That(port.Unrest, Is.GreaterThan(0), "paying another power is resented");
            Assert.That(port.Unrest, Is.LessThanOrEqualTo(Statecraft.TributeUnrestCap));
            Assert.That(s.Standing(authority, accord), Does.Contain("paying them tribute"));

            authority.Treasury = 0;
            Days(s, StartJd + 30, 1);
            Assert.That(s.Pacts, Does.Not.Contain(p), "a payer that cannot pay defaults");
            Assert.That(s.Opinion(accord, authority), Is.LessThan(0));
        }

        [Test]
        public void ADemandIsWeighedOnPowerAndAMightyNavyGetsItsTribute()
        {
            var g = Line(out var accord, out _, out var authority, out var ac);
            var s = g.Polities.Statecraft;
            authority.Treasury = 1e10;
            double amount = Statecraft.SuggestedTribute(authority);
            Assert.That(s.Weigh(Deal.DemandTribute, authority, accord, StartJd, Statecraft.SuggestedTribute(accord)).Accepted, Is.False,
                "the weak demanding of the strong get nothing");
            for (int i = 0; i < 10; i++) ac.World.Fleet.AddShip("Hammer " + i, ShipClass.Battleship, accord, ShipRole.Patrol);
            Assert.That(s.Power(accord), Is.GreaterThan(4 * s.Power(authority)));
            var read = s.Weigh(Deal.DemandTribute, accord, authority, StartJd, amount);
            Assert.That(read.Accepted, Is.True, read.Main);
            Assert.That(s.Propose(Deal.DemandTribute, accord, authority, StartJd, out var answer, amount), Is.Null);
            Assert.That(answer.Accepted, Is.True);
            var p = s.TributeBetween(accord, authority);
            Assert.That(p.A, Is.EqualTo(authority), "the one the demand was put to pays");
            Assert.That(p.Amount, Is.EqualTo(amount));
        }

        [Test]
        public void AProposalToThePlayersPolityWaitsForTheirAnswer()
        {
            var g = Line(out var accord, out var federation, out _, out _);
            var s = g.Polities.Statecraft;
            g.Polities.Command.Player = federation;
            Assert.That(s.Propose(Deal.TradePact, accord, federation, StartJd, out var answer), Is.Null);
            Assert.That(answer, Is.Null, "the player answers for their own polity");
            Assert.That(s.Inbox, Has.Count.EqualTo(1));
            Assert.That(s.Answer(s.Inbox[0], true, StartJd + 1), Is.True);
            Assert.That(s.PactBetween(accord, federation, PactKind.Trade), Is.Not.Null);
            Assert.That(s.Inbox, Is.Empty);

            // Left unanswered, it is taken as declined.
            s.Propose(Deal.NonAggression, accord, federation, StartJd + 1, out _);
            Assert.That(s.Inbox, Has.Count.EqualTo(1));
            Days(s, StartJd + 1, (int)Statecraft.InboxDays + 2);
            Assert.That(s.Inbox, Is.Empty);
            Assert.That(s.PactBetween(accord, federation, PactKind.NonAggression), Is.Null);
        }

        [Test]
        public void AVassalPaysItsLevyFollowsItsOverlordToWarAndMakesNoWarOfItsOwn()
        {
            var g = Line(out var accord, out var federation, out var authority, out _);
            var s = g.Polities.Statecraft;
            s.Sign(PactKind.Vassalage, authority, federation, StartJd);
            Assert.That(s.OverlordOf(authority), Is.EqualTo(federation));
            Assert.That(s.DeclareWar(authority, accord, StartJd), Does.Contain("vassal"));
            Assert.That(g.Wars.Declare(authority, accord, StartJd), Is.Null);

            authority.Treasury = 1e9;
            double before = federation.Treasury;
            Days(s, StartJd, 1);
            Assert.That(federation.Treasury, Is.GreaterThan(before), "the levy");
            Assert.That(authority.Treasury, Is.LessThan(1e9));

            Assert.That(s.DeclareWar(federation, accord, StartJd + 1), Is.Null);
            Assert.That(g.Polities.Diplomacy.Get(authority, accord), Is.EqualTo(Relation.War), "a vassal follows its overlord to war");

            // Throwing off the overlord.
            Assert.That(s.Break(s.PactBetween(authority, federation, PactKind.Vassalage), authority, StartJd + 2), Is.True);
            Assert.That(s.OverlordOf(authority), Is.Null);
            Assert.That(s.Opinion(federation, authority), Is.LessThan(-50));
        }

        [Test]
        public void PeaceIsAgreedWhenTheWarHasGoneOnAndTheGalaxySettlesIt()
        {
            var g = Line(out var accord, out _, out var authority, out _);
            var s = g.Polities.Statecraft;
            Assert.That(s.DeclareWar(accord, authority, StartJd), Is.Null);
            // The Accord's navy far outweighs the Authority's: the Authority would take peace, the Accord fights on.
            Assert.That(s.Weigh(Deal.Peace, accord, authority, StartJd + 1).Accepted, Is.True, "the weaker side sues for peace");
            Assert.That(s.Weigh(Deal.Peace, authority, accord, StartJd + 1).Accepted, Is.False, "a war just begun and being won is not given up");
            Assert.That(s.WarDays(accord, authority, StartJd + 200), Is.EqualTo(200).Within(1e-9));
            s.Sway(accord, authority, 100);
            var read = s.Weigh(Deal.Peace, authority, accord, StartJd + 300);
            Assert.That(read.Accepted, Is.True, read.Main);
            Assert.That(s.Propose(Deal.Peace, authority, accord, StartJd + 300, out _), Is.Null);
            Assert.That(g.Polities.Diplomacy.Get(accord, authority), Is.EqualTo(Relation.Peace));
            g.Wars.Update(StartJd + 302);
            Assert.That(g.Wars.Between(accord, authority), Is.Null, "the war is settled by its treaty");
            Assert.That(g.Polities.Diplomacy.InTruce(accord, authority, StartJd + 310), Is.True);
        }

        [Test]
        public void TheAiPutsDealsOfItsOwnToStatesItLikes()
        {
            var g = Line(out var accord, out var federation, out _, out _);
            var s = g.Polities.Statecraft;
            s.AutoDiplomacy = true;
            s.Sway(accord, federation, 70);
            s.Sway(federation, accord, 70);
            Days(s, StartJd, 120);
            Assert.That(s.PactsOf(accord).Any(p => p.Involves(federation)), Is.True, "two friendly states sign something between them");
        }

        [Test]
        public void TheEnvoyReadsEveryDealAsTheOtherSideWouldAnswerIt()
        {
            var g = Line(out var accord, out var federation, out _, out _);
            var s = g.Polities.Statecraft;
            s.Sway(federation, accord, 80);
            var options = s.Envoy(accord, federation, StartJd);
            Assert.That(options.Select(o => o.Deal), Is.EquivalentTo(Deal.All));
            var peace = options.First(o => o.Deal == Deal.Peace);
            Assert.That(peace.Unavailable, Is.Not.Null, "no peace to make with a state you are not at war with");
            Assert.That(peace.Read, Is.Null);
            var trade = options.First(o => o.Deal == Deal.TradePact);
            Assert.That(trade.Unavailable, Is.Null);
            Assert.That(trade.Read.Accepted, Is.EqualTo(s.Weigh(Deal.TradePact, accord, federation, StartJd).Accepted));
            var demand = options.First(o => o.Deal == Deal.DemandTribute);
            Assert.That(demand.Amount, Is.EqualTo(Statecraft.SuggestedTribute(federation)), "tribute is reckoned on the payer's treasury");
        }

        [Test]
        public void AHailIsAnsweredByHowTheyStandWithYou()
        {
            var g = Line(out var accord, out var federation, out var authority, out _);
            var s = g.Polities.Statecraft;
            g.Polities.Command.Player = accord;
            Assert.That(s.Hail(accord, federation, StartJd), Does.Contain("Federation").Or.Contain(federation.Name));
            s.Sway(federation, accord, -90);
            Assert.That(s.Hail(accord, federation, StartJd), Does.Contain("hostile"));
            s.Sign(PactKind.Alliance, accord, authority, StartJd);
            Assert.That(s.Hail(accord, authority, StartJd), Does.Contain("ally").And.Contain("alliance"));
            s.Propose(Deal.TradePact, authority, accord, StartJd, out _);
            Assert.That(s.Hail(accord, authority, StartJd), Does.Contain("1 proposal"));
            Assert.That(Statecraft.Mood(-80), Is.EqualTo("hostile"));
            Assert.That(Statecraft.Mood(90), Is.EqualTo("warm"));
        }

        [Test]
        public void TheDefaultGalaxyPlaysTheDiplomacyGameAndABareOneDoesNot()
        {
            var g = Line(out _, out _, out _, out _);
            Assert.That(g.Polities.Statecraft.AutoDiplomacy, Is.False);
            Assert.That(g.Polities.Statecraft.Galaxy, Is.SameAs(g));
            Assert.That(PactKind.All, Does.Contain(PactKind.Alliance).And.Contain(PactKind.Vassalage));
            Assert.That(Deal.All.Count, Is.GreaterThanOrEqualTo(8));
        }
    }
}
