using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// U.SPC-T63: capture alive vs dead. A bounty pays in full alive and at <see cref="Outlaws.DeadShare"/> dead; a
    /// kill pays only on proof (a scanned wreck); a flagship boarded and taken puts its captain in a captor's brig, to be
    /// handed over at a lawful port of a poster for the full price or ransomed back to the raiders, and a breached brig
    /// lets them go; a captain whose flagship goes down may get away in its escape pods, which whoever tractors them in has.
    /// </summary>
    [TestFixture]
    public class CaptureAliveTests
    {
        const double StartJd = 2461324.0;

        static World CreateWorld()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            w.War.Battles.Clear();
            return w;
        }

        static int Body(World w, string name) => w.Catalog[name].Index;

        static Ship Hull(ShipClass c, Faction f, int id, string name = null) =>
            new Ship { Id = id, Name = name ?? c + "" + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c) };

        /// <summary>A raider band's flagship at <paramref name="body"/>, on the books, with a Federation bounty for piracy.</summary>
        static Outlaw WantedRaider(World w, int body, string name = "Gutter Knife")
        {
            var ship = w.Fleet.AddShip(name, ShipClass.Destroyer, w.Pirates, ShipRole.Patrol);
            ship.Route = new[] { Body(w, "Deimos") };
            w.Fleet.Station(ship, body, StartJd);
            w.Outlaws.Sync(StartJd);
            var o = w.Outlaws.Of(ship);
            w.Law.Commit(o.Record, CrimeKind.Piracy, w.Sol, w.Belt, body, StartJd, "Earth Orbital");
            return o;
        }

        static Settlement Port(World w, string name) => w.Economy.Settlements.First(s => s.Name == name);

        static Ship Pilot(World w, ShipClass cls = ShipClass.Destroyer)
        {
            var ship = w.Fleet.AddShip("Lawman", cls, w.Independents, ShipRole.Free);
            ship.Orbit = new ParkingOrbit { Body = Body(w, "Earth") };
            ship.Phase = ShipPhase.Parked;
            w.PilotedShip = ship;
            return ship;
        }

        static void End(World w, Battle b)
        {
            w.Law.OnBattleEnded(b, StartJd);
            w.Outlaws.OnBattleEnded(b, StartJd);
        }

        // ---------------------------------------------------------------- alive

        [Test]
        public void HuntersSailWithABrig()
        {
            var w = CreateWorld();
            var h = w.Outlaws.Commission(Port(w, "Earth Orbital"), StartJd);
            Assert.That(Outfitting.BrigCapacity(h.Ship), Is.GreaterThan(0));
            Assert.That(Outfitting.BrigRoom(h.Ship), Is.EqualTo(Outfitting.BrigCapacity(h.Ship)));
        }

        [Test]
        public void AFlagshipBoardedAndTakenPutsItsCaptainInTheBrigAndHandingThemOverPaysInFull()
        {
            var w = CreateWorld();
            int earth = Body(w, "Earth");
            var o = WantedRaider(w, earth);
            var h = w.Outlaws.Commission(Port(w, "Earth Orbital"), StartJd);
            var b = new Battle(0, earth, "Battle of Earth", new DVec3(20000, 0, 0), StartJd, 1);
            b.AddShip(h.Ship, 0, new DVec3(4, 0, 0), -DVec3.UnitX);
            var prize = b.AddShip(o.Ship, 1, new DVec3(-4, 0, 0), DVec3.UnitX);
            prize.Captured = true;
            prize.Combat.Interior.Boarder = w.Independents;
            End(w, b);

            Assert.That(o.Fate, Is.EqualTo(OutlawFate.Held));
            Assert.That(o.HeldBy, Is.SameAs(h.Ship));
            Assert.That(h.Ship.CaptainsHeld, Is.EqualTo(1));
            Assert.That(Outfitting.BrigRoom(h.Ship), Is.EqualTo(Outfitting.BrigCapacity(h.Ship) - 1), "the captain takes a berth");
            Assert.That(o.Record.Wanted, Is.True, "nothing is paid until they are handed over");
            // (Firing on the hunter in Federation space was piracy too: it is on the bill.)
            double total = o.Record.TotalBounty;
            Assert.That(w.Outlaws.HeldAboard(h.Ship), Does.Contain(o));
            // The hunter makes for a port that pays for them.
            int next = w.Outlaws.ChooseNext(h.Ship, Body(w, "Ceres"), StartJd);
            Assert.That(w.Economy.At(next).Any(s => s.Owner == w.Sol && !s.Fallen), Is.True);

            double sol = w.Sol.Treasury, guild = w.Independents.Treasury;
            Assert.That(w.Outlaws.Deliver(o, Port(w, "Ceres Hub"), StartJd), Is.Zero, "a port whose owner posted nothing pays nothing");
            double paid = w.Outlaws.Deliver(o, Port(w, "Earth Orbital"), StartJd);
            Assert.That(paid, Is.EqualTo(total).Within(1), "alive pays the full price");
            Assert.That(sol - w.Sol.Treasury, Is.EqualTo(total).Within(1));
            Assert.That(w.Independents.Treasury - guild, Is.EqualTo(total).Within(1));
            Assert.That(h.Collected, Is.EqualTo(total).Within(1));
            Assert.That(o.Fate, Is.EqualTo(OutlawFate.Captured));
            Assert.That(o.Record.Wanted, Is.False);
            Assert.That(h.Ship.CaptainsHeld, Is.Zero);
        }

        [Test]
        public void AHunterLyingAtAPortThatPaysHandsItsCaptainOverOnItsOwn()
        {
            var w = CreateWorld();
            int earth = Body(w, "Earth");
            var o = WantedRaider(w, earth);
            var h = w.Outlaws.Commission(Port(w, "Earth Orbital"), StartJd);
            Assert.That(w.Outlaws.Hold(o, h.Ship, StartJd, "taken alive"), Is.True);
            w.Outlaws.Update(StartJd);
            w.Outlaws.Update(StartJd + 1.01);
            Assert.That(o.Fate, Is.EqualTo(OutlawFate.Captured));
            Assert.That(h.Takes, Is.EqualTo(1));
        }

        [Test]
        public void ARansomFreesTheCaptainStillWanted()
        {
            var w = CreateWorld();
            int earth = Body(w, "Earth");
            var o = WantedRaider(w, earth);
            var ship = Pilot(w);
            Assert.That(w.Outlaws.Hold(o, ship, StartJd, "taken alive"), Is.False, "no brig, no berth");
            Assert.That(Outfitting.Install(ship, ModuleKind.Brig), Is.True);
            Assert.That(w.Outlaws.Hold(o, ship, StartJd, "taken alive"), Is.True);
            var haven = w.Economy.Settlements.First(s => s.Owner == w.Pirates && s.Kind == SettlementKind.Haven && !s.Fallen);
            Assert.That(w.Outlaws.Ransom(o, Port(w, "Earth Orbital"), StartJd), Is.Zero, "only the raiders ransom their own");
            w.Pirates.Treasury = 1e8;
            double purse = ship.Faction.Treasury, ransom = Outlaws.RansomFor(o);
            Assert.That(ransom, Is.LessThan(o.Record.TotalBounty).And.GreaterThan(Outlaws.DeadRate(o.Record)));
            Assert.That(w.Outlaws.Ransom(o, haven, StartJd), Is.EqualTo(ransom));
            Assert.That(ship.Faction.Treasury - purse, Is.EqualTo(ransom).Within(1));
            Assert.That(o.AtLarge && o.Ashore, Is.True);
            Assert.That(o.Record.Wanted, Is.True);
            Assert.That(ship.CaptainsHeld, Is.Zero);
        }

        [Test]
        public void ABreachedBrigLetsTheCaptainGo()
        {
            var w = CreateWorld();
            int earth = Body(w, "Earth");
            var o = WantedRaider(w, earth);
            var h = w.Outlaws.Commission(Port(w, "Earth Orbital"), StartJd);
            w.Outlaws.Hold(o, h.Ship, StartJd, "taken alive");
            var b = new Battle(0, earth, "Battle of Earth", new DVec3(20000, 0, 0), StartJd, 1);
            var hu = b.AddShip(h.Ship, 0, new DVec3(4, 0, 0), -DVec3.UnitX);
            b.AddShip(Hull(ShipClass.Destroyer, w.Pirates, 9002, "Rescuer"), 1, new DVec3(-4, 0, 0), DVec3.UnitX);
            hu.Combat.Hull = hu.Combat.Spec.Hull * 0.5;
            End(w, b);
            Assert.That(o.Fate, Is.EqualTo(OutlawFate.Held), "a battered hull still holds them");
            hu.Combat.Hull = hu.Combat.Spec.Hull * (Outlaws.BrigBreachHull * 0.5);
            End(w, b);
            Assert.That(o.AtLarge && o.Ashore, Is.True);
            Assert.That(h.Ship.CaptainsHeld, Is.Zero);
        }

        [Test]
        public void HuntersHoldFireOnADisabledQuarryToBoardIt()
        {
            var w = CreateWorld();
            int earth = Body(w, "Earth");
            var o = WantedRaider(w, earth);
            var h = w.Outlaws.Commission(Port(w, "Earth Orbital"), StartJd);
            Assert.That(w.Outlaws.WantsAlive(h.Ship, o.Ship), Is.True);
            Assert.That(w.Outlaws.WantsAlive(Hull(ShipClass.Cruiser, w.Sol, 9001), o.Ship), Is.False, "a navy just wants it gone");
            var b = new Battle(0, earth, "Battle of Earth", new DVec3(20000, 0, 0), StartJd, 1)
            {
                Boards = u => w.Outlaws.IsHunter(u.Ship),
                WantsAlive = (u, t) => w.Outlaws.WantsAlive(u.Ship, t.Ship),
            };
            var hunter = b.AddShip(h.Ship, 0, new DVec3(4, 0, 0), -DVec3.UnitX);
            var quarry = b.AddShip(o.Ship, 1, new DVec3(-4, 0, 0), DVec3.UnitX);
            Assert.That(b.PickTarget(hunter), Is.SameAs(quarry));
            quarry.Combat.Hull = quarry.Combat.Spec.Hull * 0.05;
            Assert.That(quarry.Combat.Disabled, Is.True);
            Assert.That(b.PickTarget(hunter), Is.Null, "a disabled quarry is boarded, not finished off");
        }

        // ---------------------------------------------------------------- dead

        [Test]
        public void ThePlayersKillPaysTheDeadRateOnlyOnceTheWreckIsScanned()
        {
            var w = CreateWorld();
            int earth = Body(w, "Earth");
            var o = WantedRaider(w, earth);
            var ship = Pilot(w);
            var b = new Battle(0, earth, "Battle of Earth", new DVec3(20000, 0, 0), StartJd, 1);
            b.AddShip(ship, 0, new DVec3(4, 0, 0), -DVec3.UnitX).PlayerControlled = true;
            var dead = b.AddShip(o.Ship, 1, new DVec3(-4, 0, 0), DVec3.UnitX);
            dead.Dead = true;
            var wreck = w.War.Wrecks.Add(dead, earth, b.Anchor, StartJd);
            End(w, b);
            Assert.That(o.Fate, Is.EqualTo(OutlawFate.Killed));
            Assert.That(w.Outlaws.ClaimOn(o), Is.Not.Null);
            Assert.That(o.Record.Wanted, Is.True, "no proof yet");
            // (Firing on the player's ship in Federation space was piracy too: it is on the bill.)
            double total = o.Record.TotalBounty;

            double purse = w.Independents.Treasury;
            var at = w.WreckHelio(wreck, StartJd) - w.Ephemeris.HelioAt(earth, StartJd);
            w.Outlaws.UpdatePilot(ship, earth, at + new DVec3(Outlaws.ProofScanKm * 3, 0, 0), DVec3.UnitX, StartJd);
            Assert.That(o.Record.Wanted, Is.True, "too far off to scan it");
            w.Outlaws.UpdatePilot(ship, earth, at + new DVec3(Outlaws.ProofScanKm * 0.5, 0, 0), DVec3.UnitX, StartJd);
            Assert.That(o.Record.Wanted, Is.False);
            Assert.That(w.Outlaws.ClaimOn(o), Is.Null);
            Assert.That(w.Independents.Treasury - purse, Is.EqualTo(total * Outlaws.DeadShare).Within(1), "dead pays half");
        }

        [Test]
        public void AKillNobodyProvesBeforeTheWreckBreaksUpPaysNothing()
        {
            var w = CreateWorld();
            int earth = Body(w, "Earth");
            var o = WantedRaider(w, earth);
            var ship = Pilot(w);
            var b = new Battle(0, earth, "Battle of Earth", new DVec3(20000, 0, 0), StartJd, 1);
            b.AddShip(ship, 0, new DVec3(4, 0, 0), -DVec3.UnitX).PlayerControlled = true;
            var dead = b.AddShip(o.Ship, 1, new DVec3(-4, 0, 0), DVec3.UnitX);
            dead.Dead = true;
            var wreck = w.War.Wrecks.Add(dead, earth, b.Anchor, StartJd, lifetimeDays: 2);
            End(w, b);
            w.Outlaws.Update(StartJd);
            w.Outlaws.Update(StartJd + 3.5);
            Assert.That(w.Outlaws.ClaimOn(o), Is.Null);
            Assert.That(o.Record.Wanted, Is.False, "no proof, no bounty");
        }

        // ---------------------------------------------------------------- the pods

        static (Battle b, EscapePod pod) Eject(World w, Outlaw o, int body)
        {
            var b = new Battle(0, body, "Battle of Earth", new DVec3(20000, 0, 0), StartJd, 1);
            b.AddShip(Hull(ShipClass.Cruiser, w.Sol, 9001, "Lance"), 0, new DVec3(4, 0, 0), -DVec3.UnitX);
            var u = b.AddShip(o.Ship, 1, new DVec3(-4, 0, 0), DVec3.UnitX);
            var pod = w.War.Crews.Launch(o.Ship.Name, w.Pirates, 60, 60, AbandonCause.Scuttled, body, b.Anchor, u.Pos, null, StartJd);
            w.Outlaws.OnHullLost(b, u, pod, false, StartJd, 0.0);
            u.Dead = true;
            o.Ship.Destroyed = true;
            End(w, b);
            return (b, pod);
        }

        [Test]
        public void ACaptainWhoEjectsIsStillWantedAndWhoeverTractorsThePodsInHasThem()
        {
            var w = CreateWorld();
            int earth = Body(w, "Earth");
            var o = WantedRaider(w, earth);
            var (_, pod) = Eject(w, o, earth);
            Assert.That(pod.Captain, Is.SameAs(o));
            Assert.That(o.AtLarge, Is.True);
            Assert.That(o.Aboard, Is.False);
            Assert.That(o.Record.Wanted, Is.True, "the bounty stands on a captain adrift");
            Assert.That(w.Outlaws.Postings().Any(p => p.Outlaw == o), Is.True);

            var h = w.Outlaws.Commission(Port(w, "Earth Orbital"), StartJd);
            w.War.Crews.Collect(pod, w.Independents, h.Ship, w.Diplomacy, StartJd + 0.5);
            Assert.That(o.Fate, Is.EqualTo(OutlawFate.Held));
            Assert.That(o.HeldBy, Is.SameAs(h.Ship));
        }

        [Test]
        public void PodsLeftAloneTakeTheCaptainWithThem()
        {
            var w = CreateWorld();
            int earth = Body(w, "Earth");
            var o = WantedRaider(w, earth);
            Eject(w, o, earth);
            w.War.Crews.Expire(StartJd + Crews.LifeSupportDays + 0.1);
            Assert.That(o.Fate, Is.EqualTo(OutlawFate.Killed));
            Assert.That(o.Record.Wanted, Is.False);
        }

        [Test]
        public void ACaptainTheRaidersHaulInTakesTheNextBandThatSails()
        {
            var w = CreateWorld();
            int earth = Body(w, "Earth");
            var o = WantedRaider(w, earth);
            int before = w.Outlaws.All.Count;
            var (_, pod) = Eject(w, o, earth);
            w.War.Crews.Collect(pod, w.Pirates, Hull(ShipClass.Destroyer, w.Pirates, 9003, "Lifeboat"), w.Diplomacy, StartJd + 0.5);
            Assert.That(o.AtLarge && o.Ashore, Is.True);

            var next = w.Fleet.AddShip("Second Chance", ShipClass.Destroyer, w.Pirates, ShipRole.Patrol);
            w.Fleet.Station(next, Body(w, "Mars"), StartJd);
            w.Outlaws.Sync(StartJd + 1);
            Assert.That(w.Outlaws.All.Count, Is.EqualTo(before), "the same captain, not a new one");
            Assert.That(o.Ship, Is.SameAs(next));
            Assert.That(o.Aboard, Is.True);
            Assert.That(o.Record.Captain, Is.SameAs(next));
            Assert.That(w.Law.FindRecord(next), Is.SameAs(o.Record));
            Assert.That(w.Outlaws.Of(next), Is.SameAs(o));
            Assert.That(o.Record.Wanted, Is.True);
        }

        [Test]
        public void AFreedCaptainLeavesABandAlreadyWantedForItsOwnDeedsToItsOwnCaptain()
        {
            var w = CreateWorld();
            int earth = Body(w, "Earth");
            var o = WantedRaider(w, earth);
            var (_, pod) = Eject(w, o, earth);
            w.War.Crews.Collect(pod, w.Pirates, Hull(ShipClass.Destroyer, w.Pirates, 9003, "Lifeboat"), w.Diplomacy, StartJd + 0.5);
            Assert.That(o.AtLarge && o.Ashore, Is.True);

            // A band sails (through a gate, say) and is wanted for an attack before the day's roll-call reaches it.
            var next = w.Fleet.AddShip("Fresh Blood", ShipClass.Destroyer, w.Pirates, ShipRole.Patrol);
            w.Fleet.Station(next, Body(w, "Mars"), StartJd);
            var record = w.Law.RecordOf(next);
            w.Law.Commit(record, CrimeKind.Piracy, w.Sol, w.Sol, Body(w, "Mars"), StartJd + 0.6, "Ares Prime");
            double bounty = record.TotalBounty;
            w.Outlaws.Sync(StartJd + 1);

            Assert.That(w.Law.FindRecord(next), Is.SameAs(record), "its own captain's books stand");
            Assert.That(record.Crimes.Count, Is.EqualTo(1));
            Assert.That(record.TotalBounty, Is.EqualTo(bounty));
            Assert.That(w.Outlaws.Of(next).Record, Is.SameAs(record));
            Assert.That(o.Ashore, Is.True, "the freed captain waits for a band of their own");
            Assert.That(o.Record.Wanted, Is.True);
        }

        [Test]
        public void ACaptainInTheBrigOfAHullThatGoesDownBreaksOutIntoItsPods()
        {
            var w = CreateWorld();
            int earth = Body(w, "Earth");
            var o = WantedRaider(w, earth);
            var h = w.Outlaws.Commission(Port(w, "Earth Orbital"), StartJd);
            w.Outlaws.Hold(o, h.Ship, StartJd, "taken alive");
            var b = new Battle(0, earth, "Battle of Earth", new DVec3(20000, 0, 0), StartJd, 1);
            var hu = b.AddShip(h.Ship, 0, new DVec3(4, 0, 0), -DVec3.UnitX);
            var pod = w.War.Crews.Launch(h.Ship.Name, w.Independents, 100, 100, AbandonCause.Scuttled, earth, b.Anchor, hu.Pos, null, StartJd);
            w.Outlaws.OnHullLost(b, hu, pod, false, StartJd, 0.99);
            Assert.That(pod.Captain, Is.SameAs(o));
            Assert.That(o.AtLarge, Is.True);
            Assert.That(h.Ship.CaptainsHeld, Is.Zero);
        }
    }
}
