using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Carriers clamp escorts to the hull, and a lost carrier strands its wings (U.SPC-T473; docs/SHIP_ROSTER.md,
    /// "Carrier"): besides the fighters, bombers and small craft in its hangars, a carrier takes a few escorts with no
    /// drive of their own clamped to the outside of its hull through its own jump, so a jump strike group is a carrier, the
    /// capitals with drives of their own and the escorts clamped to it; and when a carrier is destroyed or leaves the field,
    /// its wings still out must reach another friendly carrier with room or a friendly station, or be lost.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class CarrierStrikeGroupTests
    {
        const double StartJd = 2461324.0;

        static Faction Navy(string name = "Test Navy") => new Faction(name, "TNS", default) { IsMilitary = true };

        static Ship Hull(string name, ShipClass cls, Faction faction, int id = 0) => new Ship
        {
            Id = id, Name = name, Spec = ShipClassSpec.Of(cls), Faction = faction, Role = ShipRole.Free, Combat = new ShipCombat(CombatSpec.Of(cls), cls),
        };

        // ---------------------------------------------------------------- clamps

        [Test]
        public void ACarrierClampsAFewDestroyersToItsHullBesideTheCraftInItsHangars()
        {
            Assert.That(FtlCarriage.ClampsOf(ShipClass.Carrier), Is.GreaterThan(0));
            Assert.That(FtlCarriage.ClampSizeOf(ShipClass.Destroyer), Is.GreaterThan(0));
            Assert.That(FtlCarriage.RidesClamped(ShipClass.Destroyer), Is.True);
            Assert.That(FtlCarriage.RidesClamped(ShipClass.Fighter), Is.False, "a fighter rides in a hangar");
            foreach (var c in new[] { ShipClass.Battleship, ShipClass.CommandCruiser, ShipClass.Explorer, ShipClass.Cruiser, ShipClass.Destroyer })
                Assert.That(FtlCarriage.ClampsOf(c), Is.EqualTo(0), c + " has no clamps: only a carrier does");
            foreach (var c in new[] { ShipClass.Cruiser, ShipClass.Freighter, ShipClass.Battleship })
                Assert.That(FtlCarriage.ClampSizeOf(c), Is.EqualTo(0), c + " is never clamped to a hull");

            var navy = Navy();
            var carrier = Hull("TNS Bastion", ShipClass.Carrier, navy);
            var fighters = Enumerable.Range(0, 6).Select(i => Hull("TNS Gnat " + i, ShipClass.Fighter, navy)).ToList();
            var destroyers = Enumerable.Range(0, 3).Select(i => Hull("TNS Swift " + i, ShipClass.Destroyer, navy)).ToList();
            var cruiser = Hull("TNS Lance", ShipClass.Cruiser, navy);
            var freighter = Hull("Bulk", ShipClass.Freighter, navy);
            Assert.That(FtlCarriage.Carriable(destroyers[0]), Is.True, "a destroyer has no drive, and fits a clamp");
            Assert.That(FtlCarriage.Carriable(cruiser), Is.False, "a cruiser goes by the gates");

            var aboard = FtlCarriage.Stow(new[] { carrier }, fighters.Concat(destroyers).Concat(new[] { cruiser, freighter }));
            int fit = FtlCarriage.ClampsOf(ShipClass.Carrier) / FtlCarriage.ClampSizeOf(ShipClass.Destroyer);
            Assert.That(fit, Is.InRange(1, 3), "a few escorts, not a fleet");
            Assert.That(aboard.Keys.Count(s => s.Spec.Class == ShipClass.Fighter), Is.EqualTo(6), "the hangars are untouched by the clamps");
            Assert.That(aboard.Keys.Where(s => s.Spec.Class == ShipClass.Destroyer), Is.EqualTo(destroyers.Take(fit)));
            Assert.That(aboard.Values.All(v => v == carrier), Is.True);
            Assert.That(aboard.ContainsKey(cruiser), Is.False);
            Assert.That(aboard.ContainsKey(freighter), Is.False);
            Assert.That(FtlCarriage.CountClamped(aboard.Keys), Is.EqualTo(fit));

            // A battleship with a drive has berths, but no clamps: its destroyers go by the gates.
            var battleship = Hull("TNS Ardent", ShipClass.Battleship, navy);
            aboard = FtlCarriage.Stow(new[] { battleship }, destroyers);
            Assert.That(aboard, Is.Empty);

            Assert.That(FtlRoster.NoDriveReason(destroyers[0]), Does.Contain("clamped to a carrier's hull"));
            Assert.That(FtlCarriage.Describe(1, 6, 2), Is.EqualTo(", 1 escort alongside, 6 craft docked aboard and 2 escorts clamped to the hull"));
            Assert.That(FtlCarriage.Describe(0, 0, 0), Is.Empty);
        }

        [Test]
        public void AFrigateOrCorvetteSlotsInOnTheClampsAsData()
        {
            var navy = Navy();
            var carrier = Hull("TNS Bastion", ShipClass.Carrier, navy);
            var pickets = Enumerable.Range(0, 6).Select(i => Hull("TNS Watch " + i, ShipClass.GatePicket, navy)).ToList();
            Assert.That(FtlCarriage.Stow(new[] { carrier }, pickets), Is.Empty);
            try
            {
                // As a frigate does: one clamp each (a corvette never leaves its home system, U.SPC-T476).
                FtlCarriage.RegisterClampable(ShipClass.GatePicket, 1);
                var aboard = FtlCarriage.Stow(new[] { carrier }, pickets);
                Assert.That(aboard.Count, Is.EqualTo(FtlCarriage.ClampsOf(ShipClass.Carrier)));
                Assert.That(FtlCarriage.CountClamped(aboard.Keys), Is.EqualTo(aboard.Count));
            }
            finally
            {
                FtlCarriage.RegisterClampable(ShipClass.GatePicket, 0);
            }
            Assert.That(FtlCarriage.Carriable(pickets[0]), Is.False);
        }

        [Test]
        public void AnEscortClampedToTheHullIsLostWithItsCarrierInOpenSpace()
        {
            var g = new Galaxy();
            var a = StarSystem.CreateVoidBase("Void Base West", new DVec3(0, 0, 0), StartJd, g.Polities);
            var b = StarSystem.CreateVoidBase("Void Base East", new DVec3(4, 0, 0), StartJd, g.Polities);
            g.Systems.Add(a);
            g.Systems.Add(b);
            var navy = Navy();
            var carrier = Hull("TNS Bastion", ShipClass.Carrier, navy);
            var battleship = Hull("TNS Sabre", ShipClass.Battleship, navy);
            var screen = Hull("TNS Swift", ShipClass.Destroyer, navy);
            foreach (var s in new[] { battleship, screen }) s.Leader = carrier;
            var aboard = FtlCarriage.Stow(new[] { carrier, battleship }, new[] { screen });
            Assert.That(aboard[screen], Is.SameAs(carrier), "the destroyer clamps to the carrier, not the battleship");
            var c = g.DeepSpace.Launch(new[] { carrier, battleship, screen }, a, DeepSpace.DeparturePointKm(a, b.Origin), b, CruiseDrives.Capital, StartJd);
            foreach (var kv in aboard) c.Aboard[kv.Key] = kv.Value;

            g.DeepSpace.Destroy(c, carrier, StartJd + 1, "in a test");
            Assert.That(screen.Destroyed, Is.True, "an escort clamped to the hull goes down with its carrier");
            Assert.That(battleship.Destroyed, Is.False);
            Assert.That(c.Leader, Is.SameAs(battleship));
        }

        // ---------------------------------------------------------------- stranded wings

        static Battle NewBattle(int seed = 7) => new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed)
        {
            Launch = FighterAI.Launch,
            FighterStep = FighterAI.Step,
        };

        [Test]
        public void ALostCarriersWingsLandOnAnotherFriendlyCarrierAsFarAsItHasRoom()
        {
            var blue = Navy("Blue");
            var red = Navy("Red");
            var b = NewBattle();
            var lost = b.AddShip(Hull("Blue Hive", ShipClass.Carrier, blue, 1), 0, new DVec3(14, 0, 0), -DVec3.UnitX);
            var spareHull = Hull("Blue Nest", ShipClass.Carrier, blue, 2);
            // A carrier that went in a squadron short: room in its hangars for the strays.
            spareHull.Combat.Fighters = 6;
            var spare = b.AddShip(spareHull, 0, new DVec3(14, 6, 0), -DVec3.UnitX);
            var enemy = b.AddShip(Hull("Red Bulwark", ShipClass.Battleship, red, 3), 1, new DVec3(-40, 0, 0), DVec3.UnitX);
            var wing = b.Units.Where(u => u.Kind == UnitKind.Fighter && u.Fighter.Carrier == lost).ToList();
            Assert.That(wing.Count, Is.EqualTo(18));
            int room = spareHull.Combat.Spec.Squadrons * 6 - 6;

            // The carrier is lost with its wing out.
            lost.Dead = true;
            for (int i = 0; i < 10; i++) b.Advance(1);
            var rehomed = wing.Where(u => u.Present && u.Fighter.Home == spare).ToList();
            var stranded = wing.Where(u => u.Present && u.Fighter.Stranded).ToList();
            Assert.That(rehomed.Count, Is.EqualTo(room), "the other carrier takes in as many as its hangars have room for");
            Assert.That(stranded.Count, Is.EqualTo(wing.Count(u => u.Present) - room), "the rest have nowhere to land");
            Assert.That(wing.All(u => u.Fighter.Carrier == lost), Is.True, "they fly on in their own squadrons");

            // The shooting stops: the strays land on the other carrier, which never holds more than its hangars do.
            enemy.Fled = true;
            for (int i = 0; i < 4000 && !b.Over; i++) b.Advance(1);
            Assert.That(b.Over, Is.True);
            Assert.That(spare.Combat.Fighters, Is.LessThanOrEqualTo(spareHull.Combat.Spec.Squadrons * 6));
            Assert.That(rehomed.Count(u => u.Docked), Is.GreaterThan(0), "no stray landed on the other carrier");
            Assert.That(stranded.Any(u => u.Docked), Is.False, "a craft with nowhere to land landed anyway");
        }

        [Test]
        public void AWingWhoseCarrierRanPutsInAtAFriendlyStationOnTheField()
        {
            var blue = Navy("Blue");
            var red = Navy("Red");
            var b = NewBattle(5);
            var carrier = b.AddShip(Hull("Blue Hive", ShipClass.Carrier, blue, 1), 0, new DVec3(14, 0, 0), -DVec3.UnitX);
            var port = new Settlement { Name = "Vesta Highport", Owner = blue };
            var station = b.AddPlatform(port, new ShipCombat(CombatSpec.Platform), 0, new DVec3(20, -5, 0));
            var enemy = b.AddShip(Hull("Red Bulwark", ShipClass.Battleship, red, 3), 1, new DVec3(-40, 0, 0), DVec3.UnitX);
            var wing = b.Units.Where(u => u.Kind == UnitKind.Fighter).ToList();

            // The carrier jumps away (leaves the field) with its wing still out.
            carrier.Fled = true;
            for (int i = 0; i < 10; i++) b.Advance(1);
            Assert.That(wing.Where(u => u.Present).All(u => u.Fighter.Home == station), Is.True, "a station takes in any number");
            Assert.That(wing.Any(u => u.Fighter.Stranded), Is.False);

            enemy.Fled = true;
            station.Combat.Hull = station.Combat.Spec.Hull;
            for (int i = 0; i < 4000 && !b.Over; i++) b.Advance(1);
            Assert.That(b.Over, Is.True);
            Assert.That(wing.Any(u => u.Docked), Is.True, "nobody landed on the station");
            Assert.That(carrier.Combat.Fighters, Is.EqualTo(0), "nothing landed on the carrier that left");
        }

        // ---------------------------------------------------------------- after the fight

        static World CreateWorld()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return World.CreateDefault(eph, StartJd);
        }

        /// <summary>A fight at <paramref name="body"/> in <paramref name="w"/> between a carrier of a fresh navy and a
        /// battleship of another, with the carrier just lost and the battleship gone, so it ends at the next step.</summary>
        static (Battle b, Faction blue, System.Collections.Generic.List<BattleUnit> wing) LostCarrier(World w, int body)
        {
            var blue = w.Fleet.AddFaction("Strandmark Navy", "SMN", default);
            blue.IsMilitary = true;
            var red = w.Fleet.AddFaction("Redmark Navy", "RMN", default);
            red.IsMilitary = true;
            var carrierShip = w.Fleet.SpawnAt("SMN Hive", ShipClass.Carrier, blue, body, new DVec3(2e4, 0, 0), DVec3.UnitX, StartJd);
            var enemyShip = w.Fleet.SpawnAt("RMN Bulwark", ShipClass.Battleship, red, body, new DVec3(2e4, 50, 0), DVec3.UnitX, StartJd);
            var b = new Battle(9101, body, "Battle of the Strays", new DVec3(2e4, 0, 0), StartJd, 3)
            {
                Launch = FighterAI.Launch,
                FighterStep = FighterAI.Step,
            };
            var carrier = b.AddShip(carrierShip, 0, new DVec3(14, 0, 0), -DVec3.UnitX);
            var enemy = b.AddShip(enemyShip, 1, new DVec3(-14, 0, 0), DVec3.UnitX);
            var wing = b.Units.Where(u => u.Kind == UnitKind.Fighter).ToList();
            carrier.Dead = true;
            carrierShip.Destroyed = true;
            enemy.Fled = true;
            w.War.Battles.Add(b);
            return (b, blue, wing);
        }

        [Test]
        public void AStrandedWingWithNowhereToLandIsLostAndItsPilotsEject()
        {
            var w = CreateWorld();
            // A world nobody has a port at.
            int body = Enumerable.Range(1, w.Catalog.Count - 1).First(i => !w.Economy.At(i).Any());
            var (b, _, wing) = LostCarrier(w, body);
            for (int i = 0; i < 3 && w.War.Battles.Contains(b); i++) w.War.Update(StartJd + 0.001 * (i + 1));
            Assert.That(w.War.Battles.Contains(b), Is.False, "the fight never ended");
            Assert.That(wing.All(u => u.Dead), Is.True, "a craft with nowhere to land got home anyway");
            Assert.That(w.Events.Any(e => e.Text.Contains("Battle of the Strays") && e.Text.Contains("lost with nowhere to land")), Is.True);
        }

        [Test]
        public void AStrandedWingPutsInAtAFriendlyPortAtTheWorldTheFightWasOver()
        {
            var w = CreateWorld();
            int body = w.Catalog["Pluto"].Index;
            var (b, blue, wing) = LostCarrier(w, body);
            w.Economy.Add("Strandmark Station", body, SettlementKind.Station, blue, population: 0);
            for (int i = 0; i < 3 && w.War.Battles.Contains(b); i++) w.War.Update(StartJd + 0.001 * (i + 1));
            Assert.That(w.War.Battles.Contains(b), Is.False, "the fight never ended");
            Assert.That(wing.Any(u => u.Dead), Is.False, "a craft with a friendly port to put in at was lost");
            Assert.That(w.Events.Any(e => e.Text.Contains("put in at Strandmark Station")), Is.True);
        }
    }
}
