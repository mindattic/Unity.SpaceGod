using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// U.SPC-T62: named outlaws and the bounty board. Every raider captain is on the books with a name, a flagship
    /// and a home haven, and a record built from what their band actually did (ships robbed, kills, ransoms); each
    /// deed raises the bounty the crime posts; each lawful port's board lists who its owner wants, with where they
    /// were last seen; bounty hunters take contracts they can match, go after the target (and the player, when
    /// wanted) and collect; a captain who survives long enough at large becomes a feared name.
    /// </summary>
    [TestFixture]
    public class OutlawTests
    {
        const double StartJd = 2461324.0;

        static World CreateWorld()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            // Only the fights a test sets up.
            w.War.Battles.Clear();
            return w;
        }

        static int Body(World w, string name) => w.Catalog[name].Index;

        static Ship Hull(ShipClass c, Faction f, int id, string name = null) =>
            new Ship { Id = id, Name = name ?? c + "" + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c) };

        /// <summary>A new raider band's flagship (with <paramref name="escorts"/> destroyers), parked at <paramref name="body"/> and on the books.</summary>
        static Outlaw Raider(World w, int body, string name = "Gutter Knife", ShipClass cls = ShipClass.Destroyer, int escorts = 0)
        {
            var ship = w.Fleet.AddShip(name, cls, w.Pirates, ShipRole.Patrol);
            ship.Route = new[] { Body(w, "Deimos") };
            for (int i = 0; i < escorts; i++)
                w.Fleet.AddEscort(ship, w.Fleet.AddShip(name + " escort " + i, ShipClass.Destroyer, w.Pirates, ShipRole.Escort), Fleet.SlotFor(i));
            w.Fleet.Station(ship, body, StartJd);
            w.Outlaws.Sync(StartJd);
            return w.Outlaws.Of(ship);
        }

        static Settlement Port(World w, string name) => w.Economy.Settlements.First(s => s.Name == name);

        static Ship Pilot(World w)
        {
            var ship = w.Fleet.AddShip("Lawbreaker", ShipClass.Courier, w.Independents, ShipRole.Free);
            ship.Orbit = new ParkingOrbit { Body = Body(w, "Earth") };
            ship.Phase = ShipPhase.Parked;
            w.PilotedShip = ship;
            return ship;
        }

        // ---------------------------------------------------------------- on the books

        [Test]
        public void EveryRaiderCaptainIsOnTheBooksWithANameAFlagshipAndAHomeHaven()
        {
            var w = CreateWorld();
            var leaders = w.Fleet.Ships.Where(s => s.Faction.IsPirate && !s.IsEscort && !s.Destroyed).ToList();
            Assert.That(leaders, Is.Not.Empty);
            foreach (var s in leaders)
            {
                var o = w.Outlaws.Of(s);
                Assert.That(o, Is.Not.Null, s.Name);
                Assert.That(o.Name, Is.EqualTo(PirateCaptains.NameFor(s)), "the same name the bounty adventures use");
                Assert.That(o.Ship, Is.SameAs(s));
                Assert.That(o.Haven, Is.Not.Null);
                Assert.That(o.Haven.Kind, Is.EqualTo(SettlementKind.Haven));
                Assert.That(o.Record, Is.SameAs(w.Law.FindRecord(s)), "one record: the law's");
                Assert.That(o.AtLarge, Is.True);
            }
            // An escort's hull answers to its band's captain.
            var escort = w.Fleet.Ships.First(s => s.Faction.IsPirate && s.IsEscort);
            Assert.That(w.Outlaws.Of(escort), Is.SameAs(w.Outlaws.Of(escort.Leader)));
        }

        [Test]
        public void WhatABandDoesInAFightGoesOnItsCaptainsRecordAndEachDeedRaisesTheBounty()
        {
            var w = CreateWorld();
            int earth = Body(w, "Earth");
            var o = Raider(w, earth);
            var b = new Battle(0, earth, "Battle of Earth", new DVec3(20000, 0, 0), StartJd, 1);
            b.AddShip(o.Ship, 0, new DVec3(-4, 0, 0), DVec3.UnitX);
            b.AddShip(Hull(ShipClass.Freighter, w.Belt, 9001, "Ledger"), 1, new DVec3(4, 0, 0), -DVec3.UnitX).Dead = true;
            b.AddShip(Hull(ShipClass.Freighter, w.Belt, 9002, "Tally"), 1, new DVec3(5, 0, 0), -DVec3.UnitX).Captured = true;
            b.AddShip(Hull(ShipClass.Freighter, w.Sol, 9003, "Abacus"), 1, new DVec3(6, 0, 0), -DVec3.UnitX).SurrenderOutcome = SurrenderTerms.RansomCargo;
            w.Law.OnBattleEnded(b, StartJd);
            w.Outlaws.OnBattleEnded(b, StartJd);

            Assert.That(o.Kills, Is.EqualTo(1));
            Assert.That(o.ShipsRobbed, Is.EqualTo(2), "one boarded, one ransomed");
            Assert.That(o.Ransoms, Is.EqualTo(1));
            Assert.That(o.CrimeCount, Is.EqualTo(1));
            Assert.That(o.Deeds(), Does.Contain("2 ships robbed").And.Contain("1 destroyed").And.Contain("1 cargo ransomed"));
            double piracy = Law.Severity(CrimeKind.Piracy).bounty;
            double deeds = Outlaws.KillBounty + 2 * Outlaws.RobbedBounty + Outlaws.RansomBounty;
            Assert.That(o.Record.BountyBy(w.Sol), Is.EqualTo(piracy + deeds), "Earth is Federation space: the Federation posts it all");
            Assert.That(o.Record.Crimes.Last().Bounty, Is.EqualTo(piracy + deeds));
            Assert.That(w.Events.Any(e => e.Kind == WorldEventKind.Law && e.Text.Contains("raises the price on " + o.Name)), Is.True);

            // The next crime adds again.
            w.Law.Commit(o.Record, CrimeKind.Assault, w.Sol, w.Sol, earth, StartJd + 1, "Earth Orbital");
            Assert.That(o.Record.BountyBy(w.Sol), Is.EqualTo(piracy + deeds + Law.Severity(CrimeKind.Assault).bounty));
            Assert.That(o.CrimeCount, Is.EqualTo(2));
        }

        [Test]
        public void ACaptainKilledOrTakenIsFinishedButStaysInTheRoguesGallery()
        {
            var w = CreateWorld();
            int earth = Body(w, "Earth");
            var o = Raider(w, earth);
            var b = new Battle(0, earth, "Battle of Earth", new DVec3(20000, 0, 0), StartJd, 1);
            b.AddShip(Hull(ShipClass.Cruiser, w.Sol, 9001, "Lance"), 0, new DVec3(4, 0, 0), -DVec3.UnitX);
            b.AddShip(o.Ship, 1, new DVec3(-4, 0, 0), DVec3.UnitX).Dead = true;
            w.Law.OnBattleEnded(b, StartJd);
            w.Outlaws.OnBattleEnded(b, StartJd);
            Assert.That(o.Fate, Is.EqualTo(OutlawFate.Killed));
            Assert.That(o.FateBy, Is.EqualTo(w.Sol.Name));
            Assert.That(w.Outlaws.All, Does.Contain(o));
            Assert.That(w.Outlaws.Postings().Any(p => p.Outlaw == o), Is.False);
        }

        // ---------------------------------------------------------------- the boards

        [Test]
        public void APortsBoardListsWhoItsOwnerWantsWithWhereTheyWereLastSeen()
        {
            var w = CreateWorld();
            int earth = Body(w, "Earth"), mars = Body(w, "Mars");
            var o = Raider(w, mars);
            w.Law.Commit(o.Record, CrimeKind.Piracy, w.Sol, w.Belt, mars, StartJd, "Ares Prime", "Battle of Mars");
            var p = Pilot(w);
            w.Law.Commit(w.Law.Player, CrimeKind.Assault, w.Sol, w.Sol, earth, StartJd + 0.5, "Earth Orbital");

            var board = w.Outlaws.BoardAt(Port(w, "Earth Orbital"));
            Assert.That(board.Count, Is.EqualTo(2));
            Assert.That(board[0].Outlaw, Is.SameAs(o), "the biggest bounty first");
            Assert.That(board[0].Bounty, Is.EqualTo(Law.Severity(CrimeKind.Piracy).bounty));
            Assert.That(board[0].LastSeen.Body, Is.EqualTo(mars));
            Assert.That(board[0].LastSeen.By, Is.EqualTo("Ares Prime"));
            Assert.That(board[1].IsPlayer, Is.True);
            Assert.That(board[1].LastSeen.Body, Is.EqualTo(earth));
            string line = Outlaws.Describe(board[0], StartJd + 2);
            Assert.That(line, Does.Contain(o.Name).And.Contain("150,000").And.Contain("Mars").And.Contain("2 days ago"));

            // The Reach has posted nothing; a haven keeps no board.
            Assert.That(w.Outlaws.BoardAt(Port(w, "Kraken Port")), Is.Empty);
            Assert.That(w.Outlaws.BoardAt(Port(w, "Blackwater")), Is.Empty);
            Assert.That(p, Is.Not.Null);
        }

        [Test]
        public void ASightingOverAHavenNobodyHasFoundNamesNoRock()
        {
            var w = CreateWorld();
            int psyche = Body(w, "Psyche");
            var o = Raider(w, psyche);
            w.Law.Commit(o.Record, CrimeKind.Piracy, null, w.Belt, psyche, StartJd, "the Ledger");
            var seen = w.Outlaws.SeenLast(o.Record);
            Assert.That(seen.Body, Is.EqualTo(-1));
            Assert.That(seen.Where, Does.Contain("Belt").And.Not.Contain("Psyche"));
        }

        [Test]
        public void ALawfulShipOrStationInSensorRangeSightsAnOutlawAtLargeAndNobodySeesTheDeepDark()
        {
            var w = CreateWorld();
            int mars = Body(w, "Mars");
            var o = Raider(w, mars);
            w.Outlaws.Update(StartJd);
            w.Outlaws.Update(StartJd + 1);
            var seen = w.Outlaws.SeenLast(o.Record);
            Assert.That(seen, Is.Not.Null);
            Assert.That(seen.Body, Is.EqualTo(mars));
            Assert.That(seen.Where, Is.EqualTo("at Mars"));
            Assert.That(seen.By, Is.Not.Null);
        }

        // ---------------------------------------------------------------- feared names

        [Test]
        public void ACaptainWhoSurvivesLongEnoughBecomesAFearedNameAndCrewsFacingThemBreakSooner()
        {
            var w = CreateWorld();
            int mars = Body(w, "Mars");
            w.Outlaws.Update(StartJd);
            var o = Raider(w, mars);
            for (int i = 0; i < Outlaws.FearedCrimes; i++)
                w.Law.Commit(o.Record, CrimeKind.Assault, w.Sol, w.Sol, mars, StartJd + i * 0.1, "Ares Prime");
            double before = o.Record.BountyBy(w.Sol);
            for (double jd = StartJd + 1; jd <= StartJd + Outlaws.FearedDays - 2; jd += 1) w.Outlaws.Update(jd);
            Assert.That(o.Feared, Is.False, "not yet");
            for (double jd = StartJd + Outlaws.FearedDays - 1; jd <= StartJd + Outlaws.FearedDays + 2; jd += 1) w.Outlaws.Update(jd);
            Assert.That(o.Feared, Is.True);
            Assert.That(o.Title, Is.EqualTo("the feared " + o.Name));
            Assert.That(o.Record.BountyBy(w.Sol), Is.EqualTo(before * Outlaws.FearedBountyFactor).Within(1e-6));
            Assert.That(w.Events.Any(e => e.Text.Contains(o.Name) && e.Text.Contains("feared")), Is.True);

            // Every crime now costs half again.
            double now = o.Record.BountyBy(w.Sol);
            var c = w.Law.Commit(o.Record, CrimeKind.Piracy, w.Sol, w.Belt, mars, StartJd + Outlaws.FearedDays + 3, "Ares Prime");
            Assert.That(o.Record.BountyBy(w.Sol) - now, Is.EqualTo(Law.Severity(CrimeKind.Piracy).bounty * Outlaws.FearedBountyFactor).Within(1e-6));
            Assert.That(c.Bounty, Is.EqualTo(Law.Severity(CrimeKind.Piracy).bounty * Outlaws.FearedBountyFactor).Within(1e-6));

            // A crew facing the name keeps less of its nerve.
            var b = new Battle(0, mars, "Battle of Mars", new DVec3(20000, 0, 0), StartJd, 1);
            b.AddShip(Hull(ShipClass.Freighter, w.Belt, 9001), 0, new DVec3(4, 0, 0), -DVec3.UnitX);
            b.AddShip(o.Ship, 1, new DVec3(-4, 0, 0), DVec3.UnitX);
            w.Outlaws.OnBattleStarted(b);
            Assert.That(b.Dread[0], Is.EqualTo(Outlaws.FearedResolve));
            Assert.That(b.Dread[1], Is.EqualTo(1));
        }

        // ---------------------------------------------------------------- bounty hunters

        [Test]
        public void AHunterTakesAContractItCanMatchAndGoesWhereTheTargetWasLastSeen()
        {
            var w = CreateWorld();
            int earth = Body(w, "Earth"), mars = Body(w, "Mars");
            var o = Raider(w, mars);
            w.Law.Commit(o.Record, CrimeKind.Piracy, w.Sol, w.Belt, mars, StartJd, "Ares Prime", "Battle of Mars");
            var h = w.Outlaws.Commission(Port(w, "Earth Orbital"), StartJd);
            Assert.That(w.Outlaws.IsHunter(h.Ship), Is.True);
            Assert.That(h.Ship.Faction, Is.EqualTo(w.Independents));
            Assert.That(w.Fleet.Ships.Any(s => s.Leader == h.Ship), Is.True, "a cruiser with an escort");

            Assert.That(w.War.ChooseNext(h.Ship, earth, StartJd), Is.EqualTo(mars));
            Assert.That(h.Contract.Target, Is.SameAs(o.Record));
            Assert.That(w.Outlaws.BoardAt(Port(w, "Earth Orbital"))[0].HuntedBy, Is.SameAs(h));
            Assert.That(Outlaws.Describe(w.Outlaws.BoardAt(Port(w, "Earth Orbital"))[0], StartJd), Does.Contain("hunted by the " + h.Name));
            Assert.That(w.Events.Any(e => e.Text.Contains(h.Name + " takes the contract on " + o.Name)), Is.True);

            // The contract lapses once the target is no longer wanted.
            o.Record.Bounty.Clear();
            w.War.ChooseNext(h.Ship, mars, StartJd + 1);
            Assert.That(h.Contract, Is.Null);
        }

        [Test]
        public void TheFreeTradersPayTheYardThatFitsOutANewHunter()
        {
            var w = CreateWorld();
            int mars = Body(w, "Mars");
            var o = Raider(w, mars);
            w.Law.Commit(o.Record, CrimeKind.Piracy, w.Sol, w.Belt, mars, StartJd, "Ares Prime", "Battle of Mars");
            w.Independents.Treasury = 5e9;
            var yard = Port(w, "Earth Orbital");
            double cost = Economy.HullPrice(ShipClass.Cruiser) + Economy.HullPrice(ShipClass.Destroyer);
            int hunters = w.Outlaws.Hunters.Count;
            for (int day = 1; day <= 2000 && w.Outlaws.Hunters.Count == hunters; day++)
            {
                double guild = w.Independents.Treasury, owner = yard.Owner.Treasury;
                w.Outlaws.Update(StartJd + day);
                if (w.Outlaws.Hunters.Count == hunters) continue;
                Assert.That(w.Events.Any(e => e.Text.StartsWith(yard.Name + " fits out the bounty hunter")), Is.True);
                Assert.That(guild - w.Independents.Treasury, Is.EqualTo(cost).Within(1));
                Assert.That(yard.Owner.Treasury - owner, Is.EqualTo(cost).Within(1), "the hull price goes to the yard, not to nobody");
            }
            Assert.That(w.Outlaws.Hunters.Count, Is.GreaterThan(hunters), "a hunter was commissioned");
        }

        [Test]
        public void AHunterLeavesAFearedBandItCannotMatchAlone()
        {
            var w = CreateWorld();
            int earth = Body(w, "Earth"), mars = Body(w, "Mars");
            var o = Raider(w, mars, "Black Ledger", ShipClass.Cruiser, escorts: 3);
            o.Feared = true;
            w.Law.Commit(o.Record, CrimeKind.Piracy, w.Sol, w.Belt, mars, StartJd, "Ares Prime");
            var h = w.Outlaws.Commission(Port(w, "Earth Orbital"), StartJd);
            Assert.That(w.War.GroupStrength(h.Ship), Is.LessThan(w.War.GroupStrength(o.Ship)));
            Assert.That(w.Outlaws.TakeContract(h, earth, StartJd), Is.Null);
            // A lone raider it can take (not at Mars, where the band would fight beside it).
            var lone = Raider(w, earth, "Cutpurse");
            w.Law.Commit(lone.Record, CrimeKind.Piracy, w.Sol, w.Belt, earth, StartJd, "Earth Orbital");
            Assert.That(w.Outlaws.TakeContract(h, earth, StartJd).Target, Is.SameAs(lone.Record));
        }

        [Test]
        public void AHunterThatKillsItsTargetCollectsEveryBountyOnItAtTheDeadRate()
        {
            var w = CreateWorld();
            int mars = Body(w, "Mars");
            var o = Raider(w, mars);
            w.Law.Commit(o.Record, CrimeKind.Piracy, w.Sol, w.Belt, mars, StartJd, "Ares Prime");
            w.Law.Commit(o.Record, CrimeKind.Assault, null, w.Belt, mars, StartJd, "the Ledger");
            double total = o.Record.TotalBounty;
            double sol = w.Sol.Treasury, belt = w.Belt.Treasury, guild = w.Independents.Treasury;
            var h = w.Outlaws.Commission(Port(w, "Earth Orbital"), StartJd);
            var b = new Battle(0, mars, "Battle of Mars", new DVec3(20000, 0, 0), StartJd, 1);
            b.AddShip(h.Ship, 0, new DVec3(4, 0, 0), -DVec3.UnitX);
            b.AddShip(o.Ship, 1, new DVec3(-4, 0, 0), DVec3.UnitX).Dead = true;
            w.Law.OnBattleEnded(b, StartJd);
            w.Outlaws.OnBattleEnded(b, StartJd);
            Assert.That(o.Record.Wanted, Is.False);
            // Firing on the hunter in Federation space was piracy too, seen by the hunter: that bounty is collected with
            // the rest -- all of it at the dead rate (U.SPC-T63), the hunters' scanners on the wreck the proof.
            double piracy = Law.Severity(CrimeKind.Piracy).bounty, dead = Outlaws.DeadShare;
            Assert.That(h.Collected, Is.EqualTo((total + piracy) * dead).Within(1));
            Assert.That(h.Takes, Is.EqualTo(1));
            Assert.That(w.Independents.Treasury - guild, Is.EqualTo(h.Collected).Within(1));
            Assert.That(sol - w.Sol.Treasury, Is.EqualTo(2 * piracy * dead).Within(1), "each poster pays its own");
            Assert.That(belt - w.Belt.Treasury, Is.EqualTo(Law.Severity(CrimeKind.Assault).bounty * dead).Within(1));
            Assert.That(o.Fate, Is.EqualTo(OutlawFate.Killed));
            Assert.That(o.FateBy, Does.Contain(h.Name));
            Assert.That(w.Law.FindRecord(h.Ship), Is.Null, "hunting outlaws is no crime");
        }

        [Test]
        public void HuntersComeForTheWantedPlayerAndCollectOnThem()
        {
            var w = CreateWorld();
            int earth = Body(w, "Earth");
            var ship = Pilot(w);
            w.Law.Commit(w.Law.Player, CrimeKind.Piracy, w.Sol, w.Belt, earth, StartJd, "Earth Orbital");
            var h = w.Outlaws.Commission(Port(w, "Earth Orbital"), StartJd);
            Assert.That(w.War.ChooseNext(h.Ship, earth, StartJd), Is.EqualTo(earth));
            Assert.That(h.Contract.Target, Is.SameAs(w.Law.Player));
            Assert.That(w.Law.TakeNotices().Any(n => n.Contains(h.Name) && n.Contains("contract on you")), Is.True);

            // Far off, it does nothing; close by, it moves on the ship.
            var hunterAt = w.Fleet.StateOf(h.Ship, StartJd).HelioKm - w.Ephemeris.HelioAt(earth, StartJd);
            Assert.That(w.Outlaws.UpdatePilot(ship, earth, hunterAt + new DVec3(0, 0, 2 * Outlaws.EngageKm), DVec3.UnitX, StartJd), Is.Null);
            var b = w.Outlaws.UpdatePilot(ship, earth, hunterAt + new DVec3(30000, 0, 0), DVec3.UnitX, StartJd);
            Assert.That(b, Is.Not.Null);
            Assert.That(b.Name, Does.StartWith("Bounty hunt"));
            Assert.That(b.EnforcingSide, Is.EqualTo(1));
            Assert.That(b.UnitOf(h.Ship).Side, Is.EqualTo(1));
            Assert.That(b.UnitOf(ship).PlayerControlled, Is.True);

            b.UnitOf(ship).Dead = true;
            double bounty = w.Law.Player.TotalBounty;
            w.Law.OnBattleEnded(b, StartJd);
            Assert.That(w.Law.Player.Wanted, Is.False);
            Assert.That(h.Collected, Is.EqualTo(bounty * Outlaws.DeadShare).Within(1), "dead pays the dead rate");
            Assert.That(w.Law.FindRecord(h.Ship), Is.Null, "the hunter enforces the law");
        }

        [Test]
        public void HuntersLeaveAPlayerWhoIsNotWantedAlone()
        {
            var w = CreateWorld();
            int earth = Body(w, "Earth");
            var ship = Pilot(w);
            var h = w.Outlaws.Commission(Port(w, "Earth Orbital"), StartJd);
            w.War.ChooseNext(h.Ship, earth, StartJd);
            Assert.That(h.Contract?.Target, Is.Not.SameAs(w.Law.Player));
            var hunterAt = w.Fleet.StateOf(h.Ship, StartJd).HelioKm - w.Ephemeris.HelioAt(earth, StartJd);
            Assert.That(w.Outlaws.UpdatePilot(ship, earth, hunterAt + new DVec3(1000, 0, 0), DVec3.UnitX, StartJd), Is.Null);
        }

        // ---------------------------------------------------------------- the live system

        [Test]
        public void InTheLiveSystemRaidersBuildRecordsTheBoardsFillAndHuntersGoToWork()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            for (double jd = StartJd; jd <= StartJd + 200; jd += 0.1)
            {
                eph.Update(jd);
                w.Update(jd);
            }
            var outlaws = w.Outlaws;
            foreach (var o in outlaws.All.Take(12))
                TestContext.Out.WriteLine($"{o.Title,-40} {o.Fate,-9} {o.Deeds()}  bounty {o.Record.TotalBounty:N0}");
            foreach (var h in outlaws.Hunters)
                TestContext.Out.WriteLine($"hunter {h.Name,-20} alive {h.Alive} takes {h.Takes} collected {h.Collected:N0} contract {h.Contract?.Target.Name}");
            foreach (var e in w.Events.Where(e => e.Kind == WorldEventKind.Law).TakeLast(15))
                TestContext.Out.WriteLine($"{Astro.ToDateTime(e.Jd):yyyy-MM-dd}  {e.Text}");

            Assert.That(outlaws.All.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(outlaws.All.Any(o => o.ShipsRobbed + o.Kills > 0), Is.True, "half a year of raiding goes on somebody's record");
            Assert.That(outlaws.Hunters, Is.Not.Empty, "money on the boards brings out the hunters");
            Assert.That(w.Events.Any(e => e.Text.Contains("takes the contract on")), Is.True);
            foreach (var o in outlaws.All)
            {
                // A captain's record is the law's, and a sighting never gives away a haven nobody has found.
                Assert.That(o.Record.Captain, Is.SameAs(o.Ship));
                var seen = outlaws.SeenLast(o.Record);
                if (seen == null || seen.Body < 0) continue;
                Assert.That(w.Economy.At(seen.Body).All(s => s.Discovered), Is.True, $"{o.Name} placed at a hidden haven");
            }
            // (A hunter taken and pressed into a raider band answers for what it does under the black flag.)
            foreach (var h in outlaws.Hunters.Where(h => h.Ship.Faction == w.Independents))
                Assert.That(w.Law.FindRecord(h.Ship)?.Crimes.Count ?? 0, Is.EqualTo(0), $"the hunter {h.Name} broke the law: " + string.Join("; ", (w.Law.FindRecord(h.Ship)?.Crimes ?? new System.Collections.Generic.List<Crime>()).Select(c => c.Kind + " " + c.Detail + " vs " + c.Victim?.Name)));
        }

        // ---------------------------------------------------------------- the roster over a long game (U.SPC-T321)

        [Test]
        public void ARetiredHunterRowIsDroppedOnceItsGroupHasAnHeir()
        {
            var w = CreateWorld();
            var h = w.Outlaws.Commission(Port(w, "Earth Orbital"), StartJd);
            var heir = w.Fleet.Ships.First(s => s.Leader == h.Ship);
            int before = w.Outlaws.Hunters.Count;
            heir.Leader = null; // as War's own succession leaves the new leader (see War.Succession)
            w.Fleet.Station(heir, h.Ship.Orbit.Body, StartJd);
            w.Outlaws.OnSuccession(h.Ship, heir);
            Assert.That(h.Retired, Is.True);
            Assert.That(w.Outlaws.Hunters.Count, Is.EqualTo(before + 1), "the heir's own row joins the roster alongside the retired one");
            w.Outlaws.Update(StartJd);
            w.Outlaws.Update(StartJd + 1);
            Assert.That(w.Outlaws.Hunters.Contains(h), Is.False, "a retired hunter row was never dropped from the roster");
            Assert.That(w.Outlaws.Hunters.Count, Is.EqualTo(before), "back down to one row per hunter group actually in service");
            Assert.That(w.Outlaws.HunterOf(heir), Is.Not.Null, "the heir still answers for the group");
        }

        [Test]
        public void AHunterGoneThroughAGateKeepsItsRowToTakeUpWhenItComesHome()
        {
            var w = CreateWorld();
            var h = w.Outlaws.Commission(Port(w, "Earth Orbital"), StartJd);
            h.Ship.Destroyed = true; // off on a hunt beyond the gates: InterstellarTraffic.Retire marks the departing hull both ways
            h.Ship.GateDeparted = true;
            w.Outlaws.Update(StartJd);
            w.Outlaws.Update(StartJd + 1);
            Assert.That(w.Outlaws.Hunters.Contains(h), Is.True, "a hunter away through a gate lost its row before it came home");
            var back = w.Fleet.AddShip("Homecoming", ShipClass.Cruiser, h.Ship.Faction, ShipRole.Patrol);
            w.Outlaws.OnSuccession(h.Ship, back);
            Assert.That(w.Outlaws.HunterOf(back), Is.Not.Null, "the hunter home again did not take up its old place");
        }

        [Test]
        public void ARaiderCaptainWhoseBandWentThroughAGateIsAwayNotVanished()
        {
            var w = CreateWorld();
            var o = Raider(w, Body(w, "Earth"), "Gate Runner");
            var record = o.Record;
            var gone = o.Ship;
            gone.Destroyed = true; // off on a raid beyond the gates: InterstellarTraffic.Retire marks the departing hull both ways
            gone.GateDeparted = true;
            w.Outlaws.Update(StartJd);
            w.Outlaws.Update(StartJd + 1);
            Assert.That(o.Fate, Is.EqualTo(OutlawFate.AtLarge), "a captain whose band left through a gate was struck off as vanished");
            // The band comes home as a fresh hull: the captain takes it up again, record and all.
            var back = w.Fleet.AddShip("Homecoming", ShipClass.Destroyer, w.Pirates, ShipRole.Patrol);
            back.Route = new[] { Body(w, "Deimos") };
            w.Fleet.Station(back, Body(w, "Earth"), StartJd + 2);
            w.Outlaws.Sync(StartJd + 2);
            Assert.That(w.Outlaws.Of(back), Is.SameAs(o), "the captain did not take the band home");
            Assert.That(o.Record, Is.SameAs(record));
            Assert.That(w.Law.FindRecord(back), Is.SameAs(record), "the law's file did not follow the captain home");
        }

        [Test]
        public void ADestroyedHunterWithNoHeirIsDroppedFromTheRoster()
        {
            var w = CreateWorld();
            var h = w.Outlaws.Commission(Port(w, "Earth Orbital"), StartJd);
            foreach (var s in w.Fleet.Ships.Where(s => s.Leader == h.Ship).ToList()) s.Destroyed = true;
            h.Ship.Destroyed = true;
            w.Outlaws.Update(StartJd);
            w.Outlaws.Update(StartJd + 1);
            Assert.That(w.Outlaws.Hunters.Contains(h), Is.False, "a lost hunter's row was kept for ever");
        }
    }
}
