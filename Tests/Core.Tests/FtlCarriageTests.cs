using System;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Which hulls carry FTL (U.SPC-T419): the capital classes and the deep-space explorer carry a drive; carriers and
    /// the other capital ships take fighters and small craft with them docked aboard, so a fleet can strike or explore
    /// off the gate network with its wings; what does not fit, or is too big to dock, stays behind.
    /// </summary>
    [TestFixture]
    public class FtlCarriageTests
    {
        const double StartJd = 2461324.0;

        static Faction Navy() => new Faction("Test Navy", "TNS", default) { IsMilitary = true };

        static Ship Hull(string name, ShipClass cls, Faction faction) => new Ship
        {
            Name = name, Spec = ShipClassSpec.Of(cls), Faction = faction, Role = ShipRole.Free, Combat = new ShipCombat(CombatSpec.Of(cls), cls),
        };

        [Test]
        public void CapitalClassesAndTheExplorerCarryADriveAndOnlyCapitalShipsHaveBerths()
        {
            foreach (var c in new[] { ShipClass.Carrier, ShipClass.Battleship, ShipClass.Cruiser, ShipClass.Destroyer })
            {
                Assert.That(CruiseDrives.Of(c), Is.SameAs(CruiseDrives.Capital), c.ToString());
                Assert.That(FtlCarriage.BerthsOf(c), Is.GreaterThan(0), c.ToString());
            }
            Assert.That(CruiseDrives.Of(ShipClass.Explorer), Is.SameAs(CruiseDrives.Survey));
            Assert.That(FtlCarriage.BerthsOf(ShipClass.Explorer), Is.EqualTo(0), "an explorer goes alone");
            // The carrier has the most room of all.
            foreach (var c in new[] { ShipClass.Battleship, ShipClass.Cruiser, ShipClass.Destroyer })
                Assert.That(FtlCarriage.BerthsOf(ShipClass.Carrier), Is.GreaterThan(FtlCarriage.BerthsOf(c)));
            // Fighters and small craft can be carried; haulers cannot; anything with a drive of its own needs no carrying.
            foreach (var c in new[] { ShipClass.Fighter, ShipClass.Courier, ShipClass.Gunship, ShipClass.Breacher })
                Assert.That(FtlCarriage.Carriable(Hull("x", c, Navy())), Is.True, c.ToString());
            foreach (var c in new[] { ShipClass.Freighter, ShipClass.Miner, ShipClass.Destroyer, ShipClass.Explorer })
                Assert.That(FtlCarriage.Carriable(Hull("x", c, Navy())), Is.False, c.ToString());
            // Every class has its stats, so the new hull is built, flown and priced like any other.
            Assert.That(ShipClassSpec.Of(ShipClass.Explorer).DisplayName, Is.EqualTo("Explorer"));
            Assert.That(CombatSpec.Of(ShipClass.Explorer).DesignName, Is.EqualTo("Explorer"));
        }

        [Test]
        public void CraftAreStowedWhereThereIsRoomAndTheRestStayBehind()
        {
            var navy = Navy();
            var destroyer = Hull("TNS Ardent", ShipClass.Destroyer, navy);
            var explorer = Hull("Far Horizon", ShipClass.Explorer, navy);
            var fighters = Enumerable.Range(0, 4).Select(i => Hull("TNS Gnat " + i, ShipClass.Fighter, navy)).ToList();
            var breacher = Hull("TNS Ram", ShipClass.Breacher, navy);
            var freighter = Hull("Bulk", ShipClass.Freighter, navy);
            int room = FtlCarriage.BerthsOf(ShipClass.Destroyer);

            var aboard = FtlCarriage.Stow(new[] { explorer, destroyer }, fighters.Concat(new[] { breacher, freighter }));
            // The destroyer fills its berths, in order; the explorer has none; the freighter is never carried.
            Assert.That(aboard.Count, Is.EqualTo(room));
            Assert.That(aboard.Keys, Is.EqualTo(fighters.Take(room)));
            Assert.That(aboard.Values.All(v => v == destroyer), Is.True);
            Assert.That(aboard.ContainsKey(freighter), Is.False);
            Assert.That(aboard.ContainsKey(breacher), Is.False, "no room left for a breacher");

            // A carrier takes the whole wing.
            var carrier = Hull("TNS Bastion", ShipClass.Carrier, navy);
            aboard = FtlCarriage.Stow(new[] { carrier }, fighters.Concat(new[] { breacher, freighter }));
            Assert.That(aboard.Count, Is.EqualTo(5));
            Assert.That(aboard.ContainsKey(freighter), Is.False);

            // A berth is data: registered for a new hull, it carries.
            try
            {
                FtlCarriage.RegisterBerths(ShipClass.Explorer, 1);
                aboard = FtlCarriage.Stow(new[] { explorer }, fighters);
                Assert.That(aboard.Count, Is.EqualTo(1));
            }
            finally
            {
                FtlCarriage.RegisterBerths(ShipClass.Explorer, 0);
            }
            Assert.That(FtlCarriage.BerthsOf(ShipClass.Explorer), Is.EqualTo(0));
        }

        [Test]
        public void CraftDockedAboardAreLostWithTheHullCarryingThem()
        {
            var g = new Galaxy();
            var a = StarSystem.CreateVoidBase("Void Base West", new DVec3(0, 0, 0), StartJd, g.Polities);
            var b = StarSystem.CreateVoidBase("Void Base East", new DVec3(4, 0, 0), StartJd, g.Polities);
            g.Systems.Add(a);
            g.Systems.Add(b);
            var navy = Navy();
            var carrier = Hull("TNS Bastion", ShipClass.Carrier, navy);
            var cruiser = Hull("TNS Sabre", ShipClass.Cruiser, navy);
            var gnat = Hull("TNS Gnat", ShipClass.Fighter, navy);
            var midge = Hull("TNS Midge", ShipClass.Fighter, navy);
            foreach (var s in new[] { cruiser, gnat, midge }) s.Leader = carrier;
            var c = g.DeepSpace.Launch(new[] { carrier, cruiser, gnat, midge }, a, DeepSpace.DeparturePointKm(a, b.Origin), b, CruiseDrives.Capital, StartJd);
            c.Aboard[gnat] = carrier;
            c.Aboard[midge] = cruiser;

            g.DeepSpace.Destroy(c, carrier, StartJd + 1, "in a test");
            Assert.That(gnat.Destroyed, Is.True, "a fighter docked aboard goes down with its carrier");
            Assert.That(midge.Destroyed, Is.False);
            Assert.That(c.Ships, Is.EqualTo(new[] { cruiser, midge }));
            Assert.That(c.Leader, Is.SameAs(cruiser), "the next hull with a drive takes command");
            Assert.That(midge.Leader, Is.SameAs(cruiser));
            Assert.That(c.Aboard.Keys, Is.EqualTo(new[] { midge }));
        }

        [Test]
        public void ACapitalShipCrossingOpenSpaceTakesItsFightersDockedAboardAndLeavesWhatCannotDock()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var ac = g.Find("Alpha Centauri");
            Ship Free() => sol.World.Fleet.Ships.FirstOrDefault(s => s.Spec.IsMilitary && InterstellarTraffic.CanCruise(s) && !s.Faction.IsPirate
                && !s.PatrolWing && InterstellarTraffic.Idle(sol.World, s));
            double jd = StartJd;
            for (int i = 0; i < 300 && Free() == null; i++) g.Update(jd += 0.1);
            var ship = Free();
            Assume.That(ship, Is.Not.Null, "no warship came free to send");
            var body = ship.Orbit.Body;
            var fleet = sol.World.Fleet;
            var gnat = fleet.SpawnAt("TNS Gnat", ShipClass.Fighter, ship.Faction, body, ship.Orbit.OffsetAt(jd), DVec3.UnitX, jd);
            var hauler = fleet.SpawnAt("TNS Tender", ShipClass.Freighter, ship.Faction, body, ship.Orbit.OffsetAt(jd), DVec3.UnitX, jd);
            foreach (var e in new[] { gnat, hauler })
            {
                e.Leader = ship;
                e.Role = ShipRole.Escort;
            }
            var v = g.Traffic.SendByCruise(ship, sol, ac, VoyagePurpose.Expedition, jd);
            Assert.That(v, Is.Not.Null);

            Cruise Out() => g.DeepSpace.Cruises.FirstOrDefault(c => c.Leader == ship);
            for (int i = 0; i < 400 && Out() == null; i++) g.Update(jd += 0.1);
            var cruise = Out();
            Assert.That(cruise, Is.Not.Null, "it never engaged its drive");
            if (!gnat.Destroyed && gnat.Battle == null)
            {
                Assert.That(cruise.Ships, Does.Contain(gnat), "its fighter was left behind");
                Assert.That(cruise.Aboard.TryGetValue(gnat, out var carrier) && cruise.Ships.Contains(carrier), Is.True, "the fighter is not docked aboard");
                Assert.That(fleet.Ships.Contains(gnat), Is.False);
            }
            Assert.That(cruise.Ships, Does.Not.Contain(hauler), "a freighter cannot dock and has no drive");
            Assert.That(fleet.Ships.Contains(hauler), Is.True);
        }
    }
}
