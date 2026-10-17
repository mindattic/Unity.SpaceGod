using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    /// <summary>
    /// U.SPC-T64: escape pods and crew survival. Every crewed hull carries pods (one per
    /// <see cref="Crews.PodCapacity"/> of its complement); a hull destroyed or scuttled launches them with
    /// whoever reaches them; they drift and broadcast; their own side or a friend brings the crew home for
    /// a reward, pirates take them as hostages, an enemy takes them prisoner, and nobody coming means they
    /// are lost when the air runs out. Deaths wear down morale and are gone from the faction's manpower,
    /// which crews new hulls and refits.
    /// </summary>
    [TestFixture]
    public class EscapePodTests
    {
        const double Jd = 2461324.0;

        Faction navy, enemy, traders, pirates;
        Diplomacy diplomacy;

        [SetUp]
        public void SetUp()
        {
            navy = new Faction("Navy", "NS", new Rgb(0, 0, 1)) { IsMilitary = true, Manpower = 1000, Treasury = 1e9 };
            enemy = new Faction("Enemy", "EN", new Rgb(1, 0, 1)) { IsMilitary = true, Manpower = 1000, Treasury = 1e9 };
            traders = new Faction("Free Traders", "MV", new Rgb(1, 1, 1)) { Treasury = 1e7 };
            pirates = new Faction("Black Banner", "", new Rgb(1, 0, 0)) { IsPirate = true, Manpower = 100 };
            diplomacy = new Diplomacy();
            diplomacy.Set(navy, enemy, Relation.War);
        }

        static Ship Parked(Faction f, ShipClass cls, int body, int id = 1) => new Ship
        {
            Id = id, Name = "Ship " + id, Spec = ShipClassSpec.Of(cls), Faction = f, Phase = ShipPhase.Parked,
            Orbit = new ParkingOrbit { Body = body },
        };

        EscapePod Launch(Crews crews, Faction owners, double people = 100, int body = 3) =>
            crews.Launch("Test Hull", owners, people, people, AbandonCause.Scuttled, body, new DVec3(5000, 0, 0), new DVec3(1, 2, 0), null, Jd);

        // ---------------------------------------------------------------- pods fitted and launched

        [TestCase(2, 1)]
        [TestCase(20, 1)]
        [TestCase(21, 2)]
        [TestCase(280, 14)]
        [TestCase(3800, 190)]
        public void EveryCrewedHullCarriesOnePodPerTwentyAboard(double complement, int pods)
        {
            Assert.That(Crews.PodsFitted(complement), Is.EqualTo(pods));
        }

        [Test]
        public void PodsLaunchedFollowTheCrewWhoEscapedButNeverMoreThanTheHullCarries()
        {
            Assert.That(Crews.PodsFor(45, 280), Is.EqualTo(3));
            Assert.That(Crews.PodsFor(1, 280), Is.EqualTo(1));
            Assert.That(Crews.PodsFor(280, 280), Is.EqualTo(14));
        }

        [Test]
        public void MostGetOffAScuttledHullFewerASlowDeathFewestAReactorGoingUp()
        {
            for (double roll = 0; roll < 1; roll += 0.1)
            {
                Assert.That(Crews.EscapeShare(AbandonCause.Scuttled, roll), Is.GreaterThan(Crews.EscapeShare(AbandonCause.Destroyed, roll)));
                Assert.That(Crews.EscapeShare(AbandonCause.Destroyed, roll), Is.GreaterThan(Crews.EscapeShare(AbandonCause.ReactorLost, roll)));
            }
        }

        [Test]
        public void AHullGoingDownLaunchesPodsWithWhoeverEscapedAndTheRestAreLost()
        {
            var crews = new Crews(3);
            EscapePod launched = null;
            crews.Launched += p => launched = p;
            var pod = crews.Launch("NS Valiant", navy, 280, 330, AbandonCause.Destroyed, 3, new DVec3(5000, 0, 0), new DVec3(2, 0, 0), null, Jd);

            Assert.That(pod, Is.Not.Null);
            Assert.That(launched, Is.SameAs(pod));
            Assert.That(pod.Drifting, Is.True);
            Assert.That(pod.People, Is.InRange(280 * 0.45 - 1, 280 * 0.8 + 1));
            Assert.That(pod.Pods, Is.EqualTo(Crews.PodsFor(pod.People, 330)));
            Assert.That(navy.CrewLost, Is.EqualTo(280 - pod.People).Within(1e-9), "those who did not reach the pods died with the hull");
            Assert.That(navy.CrewMorale, Is.LessThan(1), "losing a crew wears morale down");
            Assert.That(Expect.Single(crews.Pods), Is.SameAs(pod));
            Assert.That(pod.PosAt(Jd), Is.EqualTo(new DVec3(5002, 0, 0)), "launched from where the hull died");
        }

        [Test]
        public void ThePodsDriftSlowlyAwayFromTheHullAndThenHangThere()
        {
            var pod = Launch(new Crews(1), navy);
            double a = (pod.LocalAt(Jd + 1) - pod.Local).Length, b = (pod.LocalAt(Jd + 30) - pod.Local).Length;
            Assert.That(a, Is.GreaterThan(0));
            Assert.That(b, Is.GreaterThan(a));
            Assert.That(b, Is.LessThanOrEqualTo(EscapePod.MaxDriftKm + 1e-9));
        }

        [Test]
        public void ABattlesEjectedFighterPilotsDriftTogetherInOneCluster()
        {
            var crews = new Crews(2);
            var a = crews.Launch("Navy fighter pilots", navy, 2, 2, AbandonCause.Scuttled, 3, DVec3.Zero, DVec3.Zero, null, Jd, "battle:1:fighters:Navy");
            var b = crews.Launch("Navy fighter pilots", navy, 2, 2, AbandonCause.Scuttled, 3, DVec3.Zero, DVec3.Zero, null, Jd, "battle:1:fighters:Navy");
            Assert.That(b, Is.SameAs(a));
            Assert.That(a.Pods, Is.EqualTo(2));
            Assert.That(a.People, Is.EqualTo(4));
            Assert.That(crews.Pods.Count, Is.EqualTo(1));
        }

        // ---------------------------------------------------------------- who picks them up

        [Test]
        public void AFriendlyShipBringsTheCrewHomeAndIsPaidByTheirOwners()
        {
            var crews = new Crews(1);
            var pod = Launch(crews, navy);
            double people = pod.People, before = navy.Treasury, manpower = navy.Manpower, morale = navy.CrewMorale;

            var state = crews.Collect(pod, traders, null, diplomacy, Jd + 1);

            Assert.That(state, Is.EqualTo(PodState.Rescued));
            Assert.That(crews.Pods, Is.Empty);
            Assert.That(navy.Manpower, Is.EqualTo(manpower + people), "the crew are back in the pool");
            Assert.That(navy.CrewRescued, Is.EqualTo(people));
            Assert.That(navy.CrewMorale, Is.GreaterThan(morale));
            Assert.That(traders.Treasury, Is.EqualTo(1e7 + people * Crews.RescueRewardPerHead));
            Assert.That(navy.Treasury, Is.EqualTo(before - people * Crews.RescueRewardPerHead));
        }

        [Test]
        public void PiratesTakeThePodsCrewsAsHostagesAndRansomThemBack()
        {
            var crews = new Crews(1);
            var pod = Launch(crews, traders);
            double people = pod.People;

            Assert.That(crews.Collect(pod, pirates, null, diplomacy, Jd), Is.EqualTo(PodState.Hostage));
            var held = Expect.Single(crews.Held);
            Assert.That(held.Prisoners, Is.False);
            Assert.That(held.People, Is.EqualTo(people));
            Assert.That(crews.HeldFrom(traders, pirates), Is.EqualTo(people));

            crews.Daily(diplomacy, Jd + 1);
            Assert.That(crews.Held, Is.Not.Empty, "the ransom is not due yet");

            double owed = people * Crews.RansomPerHead;
            traders.Treasury = owed + 5;
            string said = null;
            crews.CaptivesReleased += (c, how, when) => said = how;
            crews.Daily(diplomacy, Jd + 30);
            Assert.That(crews.Held, Is.Empty);
            Assert.That(traders.Treasury, Is.EqualTo(5).Within(1e-6));
            Assert.That(pirates.Treasury, Is.EqualTo(owed).Within(1e-6));
            Assert.That(traders.CrewRescued, Is.EqualTo(people));
            Assert.That(said, Does.Contain("ransom"));
        }

        [Test]
        public void HostagesNobodyCanPayForArePressedIntoThePiratesCrews()
        {
            var crews = new Crews(1);
            var pod = Launch(crews, traders);
            double people = pod.People;
            crews.Collect(pod, pirates, null, diplomacy, Jd);
            traders.Treasury = 0;
            crews.Daily(diplomacy, Jd + 30);
            Assert.That(crews.Held, Is.Empty);
            Assert.That(pirates.Manpower, Is.EqualTo(100 + people));
            Assert.That(traders.CrewLost, Is.EqualTo(100).Within(1e-9), "lost to them for good, with the few who died scuttling");
        }

        [Test]
        public void HostagesAreFreedWhenTheHavenTheyAreHeldAtFalls()
        {
            var crews = new Crews(1);
            var haven = new Settlement { Name = "Blackwater", Kind = SettlementKind.Haven, Owner = pirates };
            var hostages = crews.Hold(traders, pirates, 30, false, haven, Jd, "MV Kestrel");
            crews.Daily(diplomacy, Jd + 1);
            Assert.That(crews.Held, Does.Contain(hostages));
            haven.Fallen = true;
            crews.Daily(diplomacy, Jd + 2);
            Assert.That(crews.Held, Is.Empty);
            Assert.That(traders.CrewRescued, Is.EqualTo(30));
        }

        [Test]
        public void AnEnemyTakesThePodsCrewsPrisonerUntilTheWarEnds()
        {
            var crews = new Crews(1);
            var pod = Launch(crews, navy);
            double people = pod.People, manpower = navy.Manpower;
            Assert.That(crews.Collect(pod, enemy, null, diplomacy, Jd), Is.EqualTo(PodState.Prisoner));
            Assert.That(Expect.Single(crews.Held).Prisoners, Is.True);

            crews.Daily(diplomacy, Jd + 100);
            Assert.That(crews.Held, Is.Not.Empty, "still at war: no exchange");

            diplomacy.Set(navy, enemy, Relation.Ceasefire);
            crews.Daily(diplomacy, Jd + 101);
            Assert.That(crews.Held, Is.Empty);
            Assert.That(navy.Manpower, Is.EqualTo(manpower + people));
        }

        [Test]
        public void RaidersPickedUpByANavyAreLostToTheBannerAndMayGiveTheirHavenAway()
        {
            var crews = new Crews(1);
            var haven = new Settlement { Name = "Hollow Iron", Kind = SettlementKind.Haven, Owner = pirates, Hidden = true };
            var pod = crews.Launch("Gallows Bird", pirates, 50, 50, AbandonCause.Scuttled, 3, DVec3.Zero, DVec3.Zero, null, Jd, haven: haven);
            EscapePod told = null;
            crews.RaidersTaken = (p, by, when) => told = p;
            Assert.That(crews.Collect(pod, navy, null, diplomacy, Jd), Is.EqualTo(PodState.Prisoner));
            Assert.That(told, Is.SameAs(pod));
            Assert.That(told.Haven, Is.SameAs(haven));
            Assert.That(crews.Held, Is.Empty, "raiders are not exchanged");
            Assert.That(pirates.CrewLost, Is.EqualTo(50).Within(1e-9), "those who died scuttling it, and those taken off the pods");
        }

        [Test]
        public void PodsNobodyReachesAreLostWhenTheirAirRunsOut()
        {
            var crews = new Crews(1);
            var pod = Launch(crews, navy);
            double lostBefore = navy.CrewLost;
            crews.Expire(Jd + Crews.LifeSupportDays - 0.5);
            Assert.That(pod.Drifting, Is.True);
            crews.Expire(Jd + Crews.LifeSupportDays + 0.01);
            Assert.That(pod.State, Is.EqualTo(PodState.Lost));
            Assert.That(crews.Pods, Is.Empty);
            Assert.That(navy.CrewLost, Is.EqualTo(lostBefore + pod.People));
        }

        [Test]
        public void ShipsAtThePodsBodyFindThemButShipsElsewhereAndThePlayerNeverDo()
        {
            var crews = new Crews(1);
            var pod = Launch(crews, navy, body: 3);
            var elsewhere = Parked(traders, ShipClass.Freighter, 4, 1);
            var piloted = Parked(traders, ShipClass.Freighter, 3, 2);
            var ships = new List<Ship> { elsewhere, piloted };
            crews.Sweep(ships, diplomacy, piloted, Jd + 1, 30);
            Assert.That(pod.Drifting, Is.True);

            var patrol = Parked(navy, ShipClass.Destroyer, 3, 3);
            ships.Add(patrol);
            crews.Sweep(ships, diplomacy, piloted, Jd + 1, 30);
            Assert.That(pod.State, Is.EqualTo(PodState.Rescued));
            Assert.That(pod.TakenBy, Is.SameAs(navy));
        }

        [Test]
        public void AShipInAFightDoesNotStopForPods()
        {
            var crews = new Crews(1);
            var pod = Launch(crews, navy, body: 3);
            var patrol = Parked(navy, ShipClass.Destroyer, 3);
            patrol.Battle = new Battle(1, 3, "Battle", DVec3.Zero, Jd, 1);
            crews.Sweep(new List<Ship> { patrol }, diplomacy, null, Jd + 1, 30);
            Assert.That(pod.Drifting, Is.True);
        }

        // ---------------------------------------------------------------- manpower and morale

        [Test]
        public void NewHullsNeedTheHandsToCrewThemAndDrawThemFromThePool()
        {
            double destroyer = Crews.Complement(ShipClass.Destroyer);
            navy.Manpower = destroyer - 1;
            Assert.That(Crews.CanCrew(navy, ShipClass.Destroyer), Is.False);
            navy.Manpower = destroyer + 10;
            Assert.That(Crews.CanCrew(navy, ShipClass.Destroyer), Is.True);
            Assert.That(Crews.Draft(navy, destroyer), Is.EqualTo(destroyer));
            Assert.That(navy.Manpower, Is.EqualTo(10));
            Assert.That(Crews.Draft(navy, 50), Is.EqualTo(10), "only what the pool holds");
            Assert.That(navy.Manpower, Is.EqualTo(0));
        }

        [Test]
        public void AFactionWithoutRealReservesIsNeverShortOfHands()
        {
            var f = new Faction("Untracked", "UT", default);
            Assert.That(Crews.CanCrew(f, ShipClass.Carrier), Is.True);
            Crews.Draft(f, 1e6);
            Assert.That(double.IsPositiveInfinity(f.Manpower), Is.True);
        }

        [Test]
        public void RecruitingFromItsSettlementsTopsAFactionUpToItsReserveAndMoraleRecovers()
        {
            var home = new Settlement { Name = "Home", Owner = navy, Population = 1e6, InitialPopulation = 1e6 };
            var settlements = new List<Settlement> { home };
            Crews.MusterReserves(new[] { navy }, settlements);
            double reserve = 1e6 * Crews.ReserveShare;
            Assert.That(navy.Manpower, Is.EqualTo(reserve));

            navy.Manpower = 0;
            navy.CrewMorale = 0.6;
            Crews.Recruit(new[] { navy }, settlements, 10);
            Assert.That(navy.Manpower, Is.EqualTo(1e6 * Crews.RecruitPerPersonPerDay * 10).Within(1e-9));
            Assert.That(navy.CrewMorale, Is.GreaterThan(0.6));
            Crews.Recruit(new[] { navy }, settlements, 10000);
            Assert.That(navy.Manpower, Is.EqualTo(reserve).Within(1e-9), "never past the reserve");
            Assert.That(navy.CrewMorale, Is.EqualTo(1).Within(1e-6));
        }

        [Test]
        public void CrewsWhoHaveLostTooManyShipmatesStrikeTheirColoursSooner()
        {
            double steady = Morale.Resolve(navy, true);
            Crews.RecordDeaths(navy, 6000);
            Assert.That(Morale.Resolve(navy, true), Is.LessThan(steady));
            Assert.That(navy.CrewMorale, Is.GreaterThanOrEqualTo(Crews.MinMorale));
        }
    }

    /// <summary>U.SPC-T64: drifting escape pods become a rescue adventure the player can answer.</summary>
    [TestFixture]
    public class EscapePodAdventureTests
    {
        const double Jd = 2461324.0;

        Faction navy, pirates, hunters;
        Crews crews;
        EventSources src;
        EventDirector director;
        Ship player;
        static readonly DVec3 Near = new DVec3(1e6, 0, 0);

        [SetUp]
        public void SetUp()
        {
            navy = new Faction("Navy", "NS", new Rgb(0, 0, 1)) { IsMilitary = true, Manpower = 0 };
            pirates = new Faction("Black Banner", "", new Rgb(1, 0, 0)) { IsPirate = true };
            hunters = new Faction("Belt Consortium", "BCS", new Rgb(1, 0.5f, 0)) { IsMilitary = true };
            crews = new Crews(5);
            var diplomacy = new Diplomacy();
            src = new EventSources
            {
                Pods = crews.Pods,
                PodHelio = p => Near + p.LocalAt(Jd),
                BodyName = b => "Body" + b,
                PirateHunters = hunters,
                PickUpPod = (p, s) => crews.Collect(p, s.Faction, s, diplomacy, Jd, payRescuer: false),
            };
            director = new EventDirector(seed: 3) { AnomalyRatePerDay = 0 };
            player = new Ship { Id = 999, Name = "Player", Spec = ShipClassSpec.Of(ShipClass.Cruiser), Faction = hunters };
        }

        EscapePod Pods(Faction owners) =>
            crews.Launch("NS Valiant", owners, 100, 100, AbandonCause.Scuttled, 3, DVec3.Zero, DVec3.Zero, null, Jd);

        [Test]
        public void DriftingPodsNearbyBroadcastAndThePlayerWhoComesAlongsidePicksThemUp()
        {
            var pod = Pods(navy);
            director.Update(Jd, src, DVec3.Zero, null);
            var op = Expect.Single(director.Active);
            Assert.That(op.Kind, Is.EqualTo(OpportunityKind.EscapePods));
            Assert.That(op.MarkerLabel, Is.EqualTo("PODS"));
            Assert.That(op.Pod, Is.SameAs(pod));
            Assert.That(op.Hook, Does.Contain("NS Valiant"));
            Assert.That(op.ExpiresJd, Is.EqualTo(Jd + Crews.LifeSupportDays));
            Assert.That(op.Reward.Credits, Is.GreaterThan(0));

            // On the scene but not yet alongside: nothing taken aboard yet.
            director.Update(Jd + 0.01, src, Near + new DVec3(10000, 0, 0), player);
            Assert.That(op.IsActive, Is.True);
            Assert.That(pod.Drifting, Is.True);

            double before = hunters.Treasury;
            director.Update(Jd + 0.02, src, Near, player);
            Assert.That(op.State, Is.EqualTo(OpportunityState.Completed));
            Assert.That(pod.State, Is.EqualTo(PodState.Rescued));
            Assert.That(navy.Manpower, Is.EqualTo(pod.People), "the crew go home to their own navy");
            Assert.That(hunters.Treasury, Is.EqualTo(before + op.Reward.Credits));
            Assert.That(director.ReputationWith(navy), Is.EqualTo(director.PodRescueReputation));
        }

        [Test]
        public void PodsSomeoneElsePicksUpFirstEndTheAdventure()
        {
            var pod = Pods(navy);
            director.Update(Jd, src, DVec3.Zero, null);
            var op = Expect.Single(director.Active);
            crews.Collect(pod, pirates, null, new Diplomacy(), Jd + 0.1);
            director.Update(Jd + 0.2, src, DVec3.Zero, null);
            Assert.That(op.State, Is.EqualTo(OpportunityState.Failed));
            Assert.That(op.Outcome, Does.Contain("Raiders"));
        }

        [Test]
        public void PodsWhoseAirRunsOutFailTheAdventure()
        {
            var pod = Pods(navy);
            director.Update(Jd, src, DVec3.Zero, null);
            var op = Expect.Single(director.Active);
            crews.Expire(Jd + Crews.LifeSupportDays + 0.1);
            director.Update(Jd + Crews.LifeSupportDays + 0.2, src, DVec3.Zero, null);
            Assert.That(op.State, Is.EqualTo(OpportunityState.Failed));
            Assert.That(op.Outcome, Does.Contain("silent"));
        }

        [Test]
        public void APiratePilotHaulsThePodsInAsHostagesAndIsPaidNothingUpFront()
        {
            player.Faction = pirates;
            var pod = Pods(navy);
            director.Update(Jd, src, DVec3.Zero, null);
            var op = Expect.Single(director.Active);
            director.Update(Jd + 0.01, src, Near, player);
            Assert.That(op.State, Is.EqualTo(OpportunityState.Completed));
            Assert.That(pod.State, Is.EqualTo(PodState.Hostage));
            Assert.That(op.Reward.Credits, Is.EqualTo(0));
            Assert.That(crews.HeldFrom(navy, pirates), Is.EqualTo(pod.People));
        }

        [Test]
        public void RaidersPodsPayTheHuntersBountyWhenHauledIn()
        {
            var pod = Pods(pirates);
            director.Update(Jd, src, DVec3.Zero, null);
            var op = Expect.Single(director.Active);
            Assert.That(op.Patron, Is.SameAs(hunters));
            director.Update(Jd + 0.01, src, Near, player);
            Assert.That(op.State, Is.EqualTo(OpportunityState.Completed));
            Assert.That(pod.State, Is.EqualTo(PodState.Prisoner));
            Assert.That(op.Reward.Credits, Is.EqualTo(pod.People * Crews.PrisonerBountyPerHead).Within(1000));
        }
    }

    /// <summary>U.SPC-T64 in the live sim: hulls dying in battle launch pods, and yards need crews.</summary>
    [TestFixture]
    public class EscapePodWorldTests
    {
        const double StartJd = 2461324.0;

        static World CreateWorld(out Ephemeris eph)
        {
            eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return World.CreateDefault(eph, StartJd);
        }

        static Ship ParkWarship(World w, Faction faction, ShipClass cls, int body)
        {
            var ship = w.Fleet.AddShip(cls.ToString(), cls, faction, ShipRole.Patrol);
            ship.Orbit = new ParkingOrbit { Body = body };
            ship.Phase = ShipPhase.Parked;
            ship.PhaseStartJd = StartJd;
            return ship;
        }

        [Test]
        public void AWarshipDestroyedInBattleLaunchesItsPodsWhereItDied()
        {
            var w = CreateWorld(out _);
            var player = w.Fleet.AddFaction("Player Navy", "PN", default);
            player.IsMilitary = true;
            var foes = w.Fleet.AddFaction("Foe Navy", "FN", default);
            foes.IsMilitary = true;
            w.Diplomacy.Set(player, foes, Relation.War);
            int here = w.Catalog["Pluto"].Index;
            var foe = ParkWarship(w, foes, ShipClass.Destroyer, here);
            var flown = ParkWarship(w, player, ShipClass.Battleship, here);
            w.PilotedShip = flown;
            var b = w.War.TryStartPlayerBattle(flown, here, new DVec3(3000, 0, 0), DVec3.UnitX, StartJd);
            Assert.That(b, Is.Not.Null);
            var unit = b.UnitOf(foe);
            double aboard = unit.Combat.Interior.Crew + unit.Combat.Interior.Marines;

            b.ApplyDamage(unit, null, 1e9, unit.Pos);

            var pod = w.War.Crews.Pods.Single(p => p.Name == foe.Name);
            Assert.That(pod.Owners, Is.SameAs(foes));
            Assert.That(pod.Body, Is.EqualTo(here));
            Assert.That(pod.People, Is.GreaterThan(0).And.LessThan(aboard));
            Assert.That(pod.Pods, Is.InRange(1, Crews.PodsFitted(CombatSpec.Of(ShipClass.Destroyer).Crew + CombatSpec.Of(ShipClass.Destroyer).Marines)));
            Assert.That(foes.CrewLost, Is.EqualTo(aboard - pod.People).Within(1e-6));
            var at = w.PodHelio(pod, StartJd);
            var site = w.Ephemeris.HelioAt(here, StartJd) + b.Anchor + unit.Pos;
            Assert.That((at - site).Length, Is.LessThan(1), "launched from where the hull died");
            Assert.That(w.Events.Any(e => e.Text.Contains("escape pod") && e.Text.Contains(foe.Name)), Is.True);
        }

        [Test]
        public void AYardWhoseOwnerHasNoHandsLaysNoHullDown()
        {
            var w = CreateWorld(out _);
            var yard = w.Economy.Settlements.First(s => s.HasShipyard && s.Owner == w.Sol);
            Assert.That(double.IsPositiveInfinity(w.Sol.Manpower), Is.False, "the default system musters real reserves");
            Assert.That(w.Sol.Manpower, Is.GreaterThan(0));
            Assert.That(w.Economy.CanCrew(yard, ShipClass.Destroyer), Is.True);
            w.Sol.Manpower = 10;
            Assert.That(w.Economy.CanCrew(yard, ShipClass.Destroyer), Is.False);
        }

        [Test]
        public void MonthsOfWarLeavePodsRescuedTakenAndLostAndManpowerKeptInBounds()
        {
            var w = CreateWorld(out var eph);
            int launched = 0;
            var fates = new Dictionary<PodState, int>();
            w.War.Crews.Launched += p => launched++;
            w.War.Crews.Resolved += (p, s) => fates[p.State] = (fates.TryGetValue(p.State, out int n) ? n : 0) + 1;
            for (double jd = StartJd; jd <= StartJd + 150; jd += 0.25)
            {
                eph.Update(jd);
                w.Update(jd);
            }
            foreach (var f in w.Fleet.Factions)
                TestContext.Out.WriteLine($"{f.Name,-22} manpower {f.Manpower,8:N0}  lost {f.CrewLost,7:N0}  rescued {f.CrewRescued,7:N0}  morale {f.CrewMorale:0.00}");
            TestContext.Out.WriteLine($"pods launched {launched}: " + string.Join(", ", fates.Select(kv => kv.Key + " " + kv.Value)) + $"; still drifting {w.War.Crews.Pods.Count}, captives {w.War.Crews.Held.Count}");

            Assert.That(w.Fleet.Ships.Count(s => s.Destroyed) > 0, Is.True, "nobody died, so this proves nothing");
            Assert.That(launched, Is.GreaterThan(0), "ships died but nobody ever got to the pods");
            Assert.That(fates.Values.Sum() + w.War.Crews.Pods.Count, Is.EqualTo(launched).Or.LessThan(launched), "every pod is accounted for");
            foreach (var f in w.Fleet.Factions)
            {
                Assert.That(f.Manpower, Is.GreaterThanOrEqualTo(0), f.Name);
                Assert.That(f.CrewMorale, Is.InRange(Crews.MinMorale, Crews.MaxMorale), f.Name);
            }
            foreach (var pod in w.War.Crews.Pods) Assert.That(pod.AirLeftDays(StartJd + 150), Is.GreaterThan(0), "a pod outlived its air");
        }
    }
}
