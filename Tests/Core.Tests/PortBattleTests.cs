using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    /// <summary>
    /// U.SPC-T290: a raider band waiting at a port started the same one-sided battle there over and over, hundreds of
    /// times a year, with nobody fighting in it. Every hull that could only run (a merchant's orphaned escort hopping in
    /// and out round the port, a patrol too shot up to fight coming back to its yard) started a fight on each arrival as
    /// its "aggressor", fled, and came straight back. Now a fight is picked only by someone fit to fight it, and a hull
    /// that has just run from one is let go while it gets clear.
    /// </summary>
    [TestFixture]
    public class PortBattleTests
    {
        const double StartJd = 2461324.0;

        static World CreateWorld()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return World.CreateDefault(eph, StartJd);
        }

        static Ship Park(World w, Faction faction, ShipClass cls, ShipRole role, int body)
        {
            var ship = w.Fleet.AddShip(cls + " " + w.Fleet.Ships.Count, cls, faction, role);
            ship.Orbit = new ParkingOrbit { Body = body };
            ship.Phase = ShipPhase.Parked;
            ship.PhaseStartJd = StartJd;
            ship.PhaseEndJd = StartJd + 30;
            ship.DestinationBody = body;
            return ship;
        }

        /// <summary>Raiders waiting at an empty world, and a lone merchant courier that turns up there.</summary>
        static (World w, Ship raider, Ship courier, int here) RaidersAndAMerchant()
        {
            var w = CreateWorld();
            int here = w.Catalog["Pluto"].Index;
            var carriers = w.Fleet.AddFaction("Test Carriers", "TCR", default);
            Assert.That(w.Diplomacy.Hostile(carriers, w.Pirates), Is.True);
            var raider = Park(w, w.Pirates, ShipClass.Destroyer, ShipRole.Patrol, here);
            var courier = Park(w, carriers, ShipClass.Courier, ShipRole.Trader, here);
            return (w, raider, courier, here);
        }

        [Test]
        public void AMerchantArrivingAmongRaidersIsSetUponByThemNotTheOneWhoStartsIt()
        {
            var (w, raider, courier, here) = RaidersAndAMerchant();
            w.War.OnArrived(courier, StartJd);
            var b = w.War.BattleAt(here);
            Assert.That(b, Is.Not.Null, "raiders waiting at a port still fall on a merchant that turns up");
            Assert.That(b.SideLeaders[b.AggressorSide], Is.EqualTo(w.Pirates), "the raiders pick the fight, not the merchant that can only run");
            Assert.That(b.UnitOf(courier).Side, Is.Not.EqualTo(b.AggressorSide));
        }

        [Test]
        public void AMerchantThatHasJustGotAwayIsNotSetUponAgainEachTimeItComesBack()
        {
            var (w, raider, courier, here) = RaidersAndAMerchant();
            var starts = new List<Battle>();
            w.War.BattleStarted += starts.Add;
            w.War.OnArrived(courier, StartJd);
            Assert.That(starts, Has.Count.EqualTo(1));

            // Fight it out; the courier runs (or is taken, which ends the test's point).
            double jd = StartJd;
            while (w.War.BattleAt(here) != null && jd < StartJd + 0.5)
            {
                jd += 10.0 / Astro.SecondsPerDay;
                w.War.Update(jd);
            }
            Assert.That(w.War.BattleAt(here), Is.Null, "the first fight is over");
            Assume.That(courier.Destroyed || courier.Faction.IsPirate, Is.False, "the courier got away");

            // It comes back to the same port, again and again, inside the time it is pulling back.
            for (int k = 1; k <= 8; k++)
            {
                courier.Orbit = new ParkingOrbit { Body = here };
                courier.Phase = ShipPhase.Parked;
                courier.DestinationBody = here;
                courier.PhaseEndJd = jd + 30;
                jd += 0.5;
                w.War.OnArrived(courier, jd);
                w.War.Update(jd);
            }
            Assert.That(starts, Has.Count.EqualTo(1), "a hull that has just run is let go while it gets clear, not set upon at every return");
        }

        [Test]
        public void AHullThatCannotFightStartsNoFightWhereOnlyTheOtherSideWould()
        {
            var w = CreateWorld();
            int here = w.Catalog["Pluto"].Index;
            var navy = w.Fleet.AddFaction("Test Navy", "TNV", default);
            navy.IsMilitary = true;
            var enemy = w.Fleet.AddFaction("Test Enemy", "TEN", default);
            enemy.IsMilitary = true;
            w.Diplomacy.Set(navy, enemy, Relation.War);
            var foe = Park(w, enemy, ShipClass.Destroyer, ShipRole.Patrol, here);
            // A patrol shot to pieces, its guns gone, coming back to the port it repairs at.
            var wreck = Park(w, navy, ShipClass.Fighter, ShipRole.Patrol, here);
            foreach (var sub in wreck.Combat.Subsystems)
                if (sub.Def.Weapon != null) sub.Hp = 0;
            Assert.That(wreck.Combat.Armed, Is.False);

            var b = w.War.TryStartBattle(here, navy, StartJd);
            Assert.That(b, Is.Null, "a side with nothing fit to fight does not pick one");
            // The enemy that is fit to fight may: then it is the aggressor.
            b = w.War.TryStartBattle(here, enemy, StartJd);
            Assert.That(b, Is.Not.Null);
            Assert.That(b.SideLeaders[b.AggressorSide], Is.EqualTo(enemy));
        }

        /// <summary>
        /// The case it was seen in (U.SPC-T90): a frontier system with a raider haven, run for a year. No port sees the
        /// same fight started there over and over.
        /// </summary>
        [Test]
        public void AFrontierSystemWithAHavenSeesNoPortFoughtOverEveryFewDays()
        {
            var spec = NearbySystems.All().First(s => s.Name == "Lalande 21185");
            var society = NearbySocieties.For(spec);
            Assume.That(society.Raiders, Is.Null, "the test gives Lalande a haven of its own");
            society.Raiders = new FactionSpec("Test Raiders", "", new Rgb(0.7f, 0.3f, 0.5f), 6e7, 1.15);
            society.Station("Rookery", spec.Name, 0.5, 100, "A test haven.");
            society.Add("Rookery", "Rookery", SettlementKind.Haven, SocietyRole.Raiders, 8e3, "A test hold.");
            society.AddStations(spec);
            var sys = StarSystem.Create(spec, StartJd, (eph, at, seed) => World.CreateSettled(eph, at, seed, society));
            var w = sys.World;
            w.Coarse = true;
            var starts = new Dictionary<int, int>();
            w.War.BattleStarted += b => starts[b.Body] = starts.TryGetValue(b.Body, out int n) ? n + 1 : 1;
            for (double jd = StartJd + 0.25; jd <= StartJd + 365 + 1e-9; jd += 0.25) sys.Update(jd);

            foreach (var kv in starts.OrderByDescending(kv => kv.Value))
                TestContext.Out.WriteLine($"{kv.Value,4} {w.Catalog[kv.Key].Name}");
            Assert.That(starts.Values.Sum(), Is.GreaterThan(0), "raiders still raid");
            foreach (var kv in starts)
                Assert.That(kv.Value, Is.LessThan(50), $"{w.Catalog[kv.Key].Name} fought over {kv.Value} times in a year");
        }
    }
}
