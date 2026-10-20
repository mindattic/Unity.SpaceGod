using System.Linq;
using System.Reflection;
using UnitySpace.Core;
using NUnit.Framework;

namespace UnitySpace.Core.Tests
{
    /// <summary>
    /// Covers U.SPC-T43: Sol's hidden pirate havens on real belt asteroids (Psyche, Lutetia, Ida) --
    /// hidden until a ship's scanners come within <see cref="War.ScanRangeKm"/>, a navy follows beaten
    /// raiders home, or prisoners talk; then on the map. A haven whose garrison platforms are broken can
    /// be boarded and taken or destroyed (by the player through <see cref="War.AssaultHaven"/>, or by a
    /// navy's assaulting group), which takes its raiders out of <c>DecidePirate</c> until it is rebuilt.
    /// </summary>
    [TestFixture]
    public class HavenTests
    {
        const double StartJd = 2461324.0;

        static World CreateWorld()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return World.CreateDefault(eph, StartJd);
        }

        static Settlement Haven(World w, string name) => w.Economy.Settlements.Single(s => s.Name == name);

        static Faction MakeFaction(World w, string name)
        {
            var f = w.Fleet.AddFaction(name, name.Substring(0, 3).ToUpperInvariant(), default);
            f.IsMilitary = true;
            return f;
        }

        static Ship ParkWarship(World w, Faction faction, ShipClass cls, int body)
        {
            var ship = w.Fleet.AddShip(cls.ToString(), cls, faction, ShipRole.Patrol);
            ship.Orbit = new ParkingOrbit { Body = body };
            ship.Phase = ShipPhase.Parked;
            ship.PhaseStartJd = StartJd;
            return ship;
        }

        /// <summary>Takes every raider group based at (or parked over) a haven out of the picture.</summary>
        static void ClearRaiders(World w, Settlement haven)
        {
            foreach (var s in w.Fleet.Ships.Where(s => s.Faction.IsPirate && (w.War.HomeHaven(s) == haven || s.Controller.Orbit?.Body == haven.Body)))
                s.Destroyed = true;
        }

        /// <summary>Breaks a haven for the assault tests: no garrison, no raiders, no fight going on over it.</summary>
        static void Break(World w, Settlement haven)
        {
            ClearRaiders(w, haven);
            haven.Platforms.Clear();
            w.War.Battles.RemoveAll(b => b.Body == haven.Body);
        }

        static Ship RaiderOf(World w, Settlement haven) =>
            w.Fleet.Ships.First(s => !s.Destroyed && !s.IsEscort && s.Faction.IsPirate && w.War.HomeHaven(s) == haven);

        /// <summary>A second pirate clan with its own standing haven in the same system, for U.SPC-T306: with two
        /// clans sharing a system, recruiting, Steal and TakeCrewCaptive must each use the haven of the clan the
        /// code is actually acting for, never just the first haven the system happens to list.</summary>
        static (Faction clan, Settlement haven) MakeRivalClan(World w)
        {
            var rival = w.Fleet.AddFaction("Rival Clan", "RIV", default);
            rival.IsPirate = true;
            var haven = w.Economy.Add("Rival Den", w.Catalog["Mercury"].Index, SettlementKind.Haven, rival, 5000);
            return (rival, haven);
        }

        static object CallPrivate(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);

        [Test]
        public void SolHasThreeHiddenHavensOnRealBeltAsteroidsBesidesBlackwater()
        {
            var w = CreateWorld();
            foreach (var (name, body) in new[] { ("Hollow Iron", "Psyche"), ("Lantern Rock", "Lutetia"), ("Dactyl's Rest", "Ida") })
            {
                var h = Haven(w, name);
                Assert.That(h.Kind, Is.EqualTo(SettlementKind.Haven));
                Assert.That(h.Owner, Is.EqualTo(w.Pirates));
                Assert.That(w.Catalog[h.Body].Name, Is.EqualTo(body));
                Assert.That(w.Catalog[h.Body].Kind, Is.EqualTo(BodyKind.Asteroid));
                Assert.That(h.Hidden && !h.Discovered, Is.True, $"{name} should start off the map");
                Assert.That(h.Platforms.Count, Is.EqualTo(2), $"{name}'s garrison");
                // Nobody wanders in by chance: free ships never list a hidden haven's rock.
                foreach (var s in w.Fleet.Ships.Where(s => s.Role == ShipRole.Free))
                    Assert.That(s.Ports, Does.Not.Contain(h.Body));
            }
            var blackwater = Haven(w, "Blackwater");
            Assert.That(blackwater.Hidden, Is.False);
            Assert.That(blackwater.Discovered, Is.True);
            // A raider group sails from the biggest belt haven from the start.
            Assert.That(w.Fleet.Ships.Any(s => !s.Destroyed && !s.IsEscort && w.War.HomeHaven(s) == Haven(w, "Hollow Iron")), Is.True);
        }

        [Test]
        public void ScannersFindAHiddenHavenOnlyWithinRange()
        {
            var w = CreateWorld();
            var h = Haven(w, "Lantern Rock");
            var at = w.Ephemeris.HelioAt(h.Body, StartJd);

            Assert.That(w.War.ScanFrom(w.Sol, at + new DVec3(War.ScanRangeKm * 1.5, 0, 0), StartJd), Is.Empty);
            Assert.That(h.Discovered, Is.False);

            var found = w.War.ScanFrom(w.Sol, at + new DVec3(War.ScanRangeKm * 0.5, 0, 0), StartJd, "SFS Lance");
            Assert.That(found, Is.EqualTo(new[] { h }));
            Assert.That(h.Discovered && h.KnownBy(w.Sol), Is.True);
            Assert.That(h.KnownBy(w.Belt), Is.False, "only the finder's navy knows where to strike");
            Assert.That(w.Events.Any(e => e.Text.Contains("Lantern Rock") && e.Text.Contains("scanners")), Is.True);
            Assert.That(w.War.ScanFrom(w.Sol, at, StartJd), Is.Empty, "no news the second time");
            Assert.That(w.War.ScanFrom(w.Pirates, at, StartJd), Is.Empty, "pirates don't 'discover' their own bases");
        }

        [Test]
        public void AWarshipParkedNearAHiddenHavenFindsItOnItsDailySweep()
        {
            var w = CreateWorld();
            var h = Haven(w, "Dactyl's Rest");
            ParkWarship(w, w.Belt, ShipClass.Cruiser, h.Body);
            w.War.Update(StartJd + 1.01);
            Assert.That(h.KnownBy(w.Belt), Is.True);
        }

        [Test]
        public void FollowingBeatenRaidersHomeRevealsTheirHaven()
        {
            var w = CreateWorld();
            var h = Haven(w, "Hollow Iron");
            var raider = RaiderOf(w, h);
            Assert.That(w.War.TrailHome(raider, w.Reach, StartJd), Is.True);
            Assert.That(h.KnownBy(w.Reach) && h.Discovered, Is.True);
            Assert.That(w.War.TrailHome(raider, w.Reach, StartJd), Is.False, "already known");
            Assert.That(w.Events.Any(e => e.Text.Contains("follow the beaten")), Is.True);
        }

        [Test]
        public void AHavenCannotBeBoardedWhileItsGarrisonOrRaidersStillHoldIt()
        {
            var w = CreateWorld();
            var h = Haven(w, "Hollow Iron");
            var ship = ParkWarship(w, w.Sol, ShipClass.Cruiser, h.Body);
            var raider = RaiderOf(w, h);
            Assert.That(w.War.CanAssault(h, ship), Is.False, "two platforms still up");
            Break(w, h);
            Assert.That(w.War.CanAssault(h, ship), Is.True);
            raider.Destroyed = false;
            raider.Orbit = new ParkingOrbit { Body = h.Body };
            raider.Phase = ShipPhase.Parked;
            Assert.That(w.War.CanAssault(h, ship), Is.False, "its raider group is parked over it");
            raider.Destroyed = true;
            Assert.That(w.War.AssaultHaven(h, ship, capture: true, StartJd), Is.True);
            Assert.That(w.War.CanAssault(h, ship), Is.False, "it has already fallen");
        }

        [Test]
        public void BoardingAHavenSeizesItsGoodsTakesItOutAndItsRaidersLieLowUntilItIsRebuilt()
        {
            var w = CreateWorld();
            var h = Haven(w, "Hollow Iron");
            var raider = RaiderOf(w, h);
            // The raiders are out hunting when the Federation comes in.
            raider.Orbit = new ParkingOrbit { Body = w.Catalog["Ceres"].Index };
            raider.Phase = ShipPhase.Parked;
            h.Platforms.Clear();
            w.War.Battles.RemoveAll(b => b.Body == h.Body);
            h.Stock[(int)Good.Platinum] = 10;
            var ship = ParkWarship(w, w.Sol, ShipClass.Cruiser, h.Body);
            double before = w.Sol.Treasury;

            Assert.That(w.War.AssaultHaven(h, ship, capture: true, StartJd), Is.True);
            Assert.That(h.Fallen, Is.True);
            Assert.That(h.Population, Is.EqualTo(0));
            Assert.That(h.Stock.Sum(), Is.EqualTo(0));
            Assert.That(w.Sol.Treasury, Is.GreaterThan(before), "the stolen goods are seized");
            // And it stays an empty shell while it lies fallen: the markets don't repopulate or restock it.
            w.Economy.AdvanceTo(StartJd + 10);
            Assert.That(h.Population, Is.EqualTo(0), "the economy brought a fallen haven's people back");
            Assert.That(w.Events.Any(e => e.Kind == WorldEventKind.Capture && e.Text.Contains("board and take Hollow Iron")), Is.True);

            // Its raiders no longer raid or lurk: they hold where they are.
            int ceres = w.Catalog["Ceres"].Index;
            for (int i = 0; i < 20; i++)
            {
                Assert.That(w.War.ChooseNext(raider, ceres, StartJd + i * 0.1), Is.EqualTo(ceres));
                Assert.That(w.War.OrderOf(raider).Kind, Is.EqualTo(OrderKind.Patrol));
            }

            // The pirates rebuild it, garrison and all, and the band goes back to work.
            w.War.Update(StartJd + War.HavenRebuildDays + 1.5);
            Assert.That(h.Fallen, Is.False);
            Assert.That(h.Population, Is.EqualTo(h.InitialPopulation));
            Assert.That(h.Platforms.Count, Is.EqualTo(h.MaxPlatforms));
        }

        [Test]
        public void ThePlayerCanDestroyABrokenHavenInsteadOfTakingIt()
        {
            var w = CreateWorld();
            var h = Haven(w, "Blackwater");
            Break(w, h);
            var ship = ParkWarship(w, w.Independents, ShipClass.Destroyer, h.Body);
            w.PilotedShip = ship;
            double before = w.Independents.Treasury;
            Assert.That(w.War.AssaultHaven(h, ship, capture: false, StartJd), Is.True);
            Assert.That(h.Fallen, Is.True);
            Assert.That(w.Independents.Treasury, Is.EqualTo(before), "nothing is seized from a haven blown up");
            Assert.That(w.Events.Any(e => e.Text.Contains("destroy Blackwater")), Is.True);
        }

        [Test]
        public void ANoMarinesShipCannotBoardAHaven()
        {
            var w = CreateWorld();
            var h = Haven(w, "Lantern Rock");
            Break(w, h);
            var fighter = ParkWarship(w, w.Sol, ShipClass.Fighter, h.Body);
            Assert.That(w.War.HasMarines(fighter), Is.False);
            Assert.That(w.War.AssaultHaven(h, fighter, capture: true, StartJd), Is.False);
            Assert.That(h.Fallen, Is.False);
        }

        [Test]
        public void ANavyThatFoundAHavenSendsAGroupToAssaultIt()
        {
            var w = CreateWorld();
            var f = MakeFaction(w, "Vigilance");
            var h = Haven(w, "Dactyl's Rest");
            ClearRaiders(w, h);
            int ceres = w.Catalog["Ceres"].Index;
            ParkWarship(w, f, ShipClass.Destroyer, ceres); // the oldest group stays home
            var strike = ParkWarship(w, f, ShipClass.Battleship, ceres);
            // Half again the haven's two platforms takes more than one battleship.
            for (int i = 0; i < 2; i++)
                w.Fleet.AddEscort(strike, w.Fleet.AddShip("Escort " + i, ShipClass.Battleship, f, ShipRole.Escort), Fleet.SlotFor(i));

            w.War.ChooseNext(strike, ceres, StartJd);
            Assert.That(w.War.OrderOf(strike).Kind, Is.Not.EqualTo(OrderKind.Assault), "it does not know the haven is there");

            w.War.Discover(h, f, StartJd, "test");
            Assert.That(w.War.ChooseNext(strike, ceres, StartJd), Is.EqualTo(h.Body));
            Assert.That(w.War.OrderOf(strike).Kind, Is.EqualTo(OrderKind.Assault));
        }

        [Test]
        public void AnAssaultingGroupHoldingABrokenHavenBoardsIt()
        {
            var w = CreateWorld();
            var h = Haven(w, "Lantern Rock");
            Break(w, h);
            w.War.Discover(h, w.Belt, StartJd, "test");
            var leader = ParkWarship(w, w.Belt, ShipClass.Cruiser, h.Body);
            w.War.Orders[leader] = new GroupOrder { Kind = OrderKind.Assault, Body = h.Body };

            w.War.Update(StartJd + 1.01);
            Assert.That(h.Fallen, Is.True);
            Assert.That(w.Events.Any(e => e.Text.Contains("Belt Consortium board and take Lantern Rock")), Is.True);
        }

        [Test]
        public void NewRaiderBandsSailFromStandingHavensOnly()
        {
            var w = CreateWorld();
            foreach (var h in w.Economy.Settlements.Where(s => s.Kind == SettlementKind.Haven && s.Name != "Dactyl's Rest").ToList())
            {
                Break(w, h);
                Assert.That(w.War.AssaultHaven(h, ParkWarship(w, w.Sol, ShipClass.Cruiser, h.Body), capture: false, StartJd), Is.True, h.Name);
            }
            w.Pirates.Treasury = 1e12;
            for (int day = 1; day <= 120; day++) w.War.Update(StartJd + day + 0.01);
            var homes = w.Fleet.Ships.Where(s => !s.Destroyed && !s.IsEscort && s.Faction.IsPirate).Select(s => w.War.HomeHaven(s)?.Name).ToList();
            Assert.That(homes, Does.Contain("Dactyl's Rest"), "recruiting goes on from the haven still standing");
            Assert.That(homes.Count(n => n == "Dactyl's Rest"), Is.EqualTo(homes.Count), string.Join(", ", homes));
        }

        [Test]
        public void ABattleOverAnUndiscoveredHavenDoesNotNameItsRock()
        {
            var w = CreateWorld();
            var h = Haven(w, "Hollow Iron");
            ParkWarship(w, w.Sol, ShipClass.Cruiser, h.Body);
            var b = w.War.TryStartBattle(h.Body, w.Sol, StartJd);
            Assert.That(b, Is.Not.Null);
            Assert.That(b.Name, Is.EqualTo("Skirmish in the Belt"));
            var news = w.Events.Where(e => e.Text.StartsWith(b.Name)).ToList();
            Assert.That(news, Is.Not.Empty);
            Assert.That(news.All(e => !e.Text.Contains("Psyche") && e.Body == -1), Is.True, "the news must not point at the rock");

            w.War.Discover(h, w.Sol, StartJd, "test");
            w.War.Battles.Clear();
            ParkWarship(w, w.Sol, ShipClass.Cruiser, h.Body);
            var named = w.War.TryStartBattle(h.Body, w.Sol, StartJd);
            Assert.That(named.Name, Is.EqualTo("Battle of Psyche"), "once found, the first battle there is named for it");
            Assert.That(w.Events.Last(e => e.Text.StartsWith(named.Name)).Body, Is.EqualTo(h.Body));
        }

        /// <summary>
        /// U.SPC-T291: a free ship picked a known, hostile haven as somewhere to wander to like any other world, and
        /// its platforms fired on it each time it came back (15-25 short empty battles a year at a visible haven).
        /// Blackwater sits on Deimos, so a free ship off Phobos, its nearest neighbour, would pick it most often.
        /// </summary>
        [Test]
        public void AFreeShipNeverWandersToAKnownHostileHaven()
        {
            var w = CreateWorld();
            var h = Haven(w, "Blackwater");
            Assume.That(h.Hidden, Is.False);
            Assume.That(h.Platforms.Count, Is.GreaterThan(0));
            Assume.That(w.Diplomacy.Hostile(w.Independents, h.Owner), Is.True);
            var ship = w.Fleet.AddShip("Drifter", ShipClass.Courier, w.Independents, ShipRole.Free);
            ship.Ports = w.FreeShipPorts();
            Assert.That(ship.Ports, Does.Contain(h.Body), "a visible haven's rock is still somewhere a free ship can be");
            int phobos = w.Catalog["Phobos"].Index;

            int picks = 0;
            for (int k = 0; k < 300; k++)
            {
                w.Fleet.Station(ship, phobos, StartJd);
                ship.Propellant = ship.PropellantCapacity;
                w.Fleet.ScheduleNext(ship, StartJd);
                if (ship.DestinationBody != phobos) picks++;
                Assert.That(ship.DestinationBody, Is.Not.EqualTo(h.Body), $"pick {k} sends it to {h.Name}");
            }
            Assert.That(picks, Is.GreaterThan(30), "it still goes elsewhere");
        }

        // ---------------------------------------------------------------- U.SPC-T306: two clans, one system

        [Test]
        public void TakeCrewCaptiveHoldsHostagesAtTheCaptorsOwnHavenNotARivalClansHaven()
        {
            var w = CreateWorld();
            var (rival, rivalHaven) = MakeRivalClan(w);
            var victim = w.Fleet.AddShip("Lawman", ShipClass.Destroyer, w.Sol, ShipRole.Patrol);
            Assert.That(victim.Combat.Interior.Crew + victim.Combat.Interior.Marines, Is.GreaterThan(0.5));

            CallPrivate(w.War, "TakeCrewCaptive", victim, rival, StartJd);

            var held = w.War.Crews.Held.Single(c => c.Owners == w.Sol && c.Captors == rival);
            Assert.That(held.Haven, Is.SameAs(rivalHaven), "hostages taken by a clan must be held at that clan's own haven");
            Assert.That(held.Haven, Is.Not.SameAs(Haven(w, "Blackwater")), "not whichever haven the system happens to list first");
        }

        [Test]
        public void StealRoutesAKeptPrizeToTheCaptorsOwnHavenNotARivalClansHaven()
        {
            var w = CreateWorld();
            var (rival, rivalHaven) = MakeRivalClan(w);
            int earth = w.Catalog["Earth"].Index;
            var prize = w.Fleet.AddShip("Lawman", ShipClass.Destroyer, w.Sol, ShipRole.Patrol);
            prize.Orbit = new ParkingOrbit { Body = earth };
            var b = new Battle(0, earth, "Test Battle", new DVec3(20000, 0, 0), StartJd, 1);
            b.AddShip(prize, 0, new DVec3(4, 0, 0), -DVec3.UnitX);

            bool kept = (bool)CallPrivate(w.War, "Steal", prize, rival, b, StartJd);

            Assert.That(kept, Is.True);
            Assert.That(prize.Faction, Is.SameAs(rival));
            Assert.That(prize.Route, Is.Not.Null);
            Assert.That(prize.Route.Length, Is.EqualTo(1));
            Assert.That(prize.Route[0], Is.EqualTo(rivalHaven.Body), "a prize must sail for its new owner's own haven, not another clan's");
        }

        [Test]
        public void RecruitingRaisesNewBandsAtItsOwnHavenNeverARivalClansHaven()
        {
            var w = CreateWorld();
            var (_, rivalHaven) = MakeRivalClan(w);
            // Every standing haven of the Banner's own already has a band based there, so under the old bug the
            // rival's haven (with none yet) would look like the standing haven "with the fewest" and win out.
            foreach (var name in new[] { "Lantern Rock", "Dactyl's Rest", "Menoetius Hold" })
            {
                var h = Haven(w, name);
                var filler = w.Fleet.AddShip("Filler", ShipClass.Destroyer, w.Pirates, ShipRole.Patrol);
                filler.Route = new[] { h.Body };
                filler.Orbit = new ParkingOrbit { Body = h.Body };
                filler.Phase = ShipPhase.Parked;
            }
            w.PirateGroupCap = 10;
            w.Pirates.Treasury = 1e12;
            w.Pirates.Manpower = double.PositiveInfinity;

            int before = w.Fleet.Ships.Count;
            for (int i = 0; i < 500 && w.Fleet.Ships.Count == before; i++) CallPrivate(w.War, "PirateRecruiting", StartJd + i);
            Assert.That(w.Fleet.Ships.Count, Is.GreaterThan(before), "no new raider band was ever raised");

            var leader = w.Fleet.Ships.Skip(before).First(s => !s.IsEscort);
            Assert.That(leader.Route, Is.Not.Null.And.Length.EqualTo(1));
            Assert.That(leader.Route[0], Is.Not.EqualTo(rivalHaven.Body), "the new band must never sail from a rival clan's haven");
            var home = w.Economy.At(leader.Route[0]).Single(s => s.Kind == SettlementKind.Haven);
            Assert.That(home.Owner, Is.SameAs(w.Pirates));
        }
    }
}
