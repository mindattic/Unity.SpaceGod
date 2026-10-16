using System.Linq;
using SolarSystem.Core;
using Xunit;

namespace SolarSystem.Core.Tests
{
    /// <summary>
    /// Covers U.SPC-T35: <see cref="War"/>'s strategic rules -- <see cref="War.SiegeNeeded"/>, the
    /// uncontested-siege capture flow in Sieges (and the garrisons/platforms/battles/haven status
    /// that block or reset it), the trader-booking revocation a capture triggers, <see cref="War.AtWar"/>
    /// ignoring pirates, <see cref="War.DefenceAt"/>, <see cref="War.GroupStrength"/> and
    /// <see cref="War.ChooseWarship"/> -- none of which is exercised directly elsewhere (only reached
    /// incidentally via CombatTests' two-year simulation).
    /// </summary>
    public class WarTests
    {
        const double StartJd = 2461324.0;

        static World CreateWorld()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return World.CreateDefault(eph, StartJd);
        }

        static Faction MakeFaction(World w, string name, bool military = true)
        {
            var f = w.Fleet.AddFaction(name, name.Substring(0, System.Math.Min(3, name.Length)).ToUpperInvariant(), default);
            f.IsMilitary = military;
            return f;
        }

        /// <summary>Parks a warship at a body, bypassing the fleet scheduler: these tests drive <see cref="War"/>
        /// directly (via <see cref="War.Update"/> or its pure query methods), never <see cref="Fleet.Update"/>.</summary>
        static Ship ParkWarship(World w, Faction faction, ShipClass cls, int body)
        {
            var ship = w.Fleet.AddShip(cls.ToString(), cls, faction, ShipRole.Patrol);
            ship.Orbit = new ParkingOrbit { Body = body };
            ship.Phase = ShipPhase.Parked;
            ship.PhaseStartJd = StartJd;
            return ship;
        }

        /// <summary>Zeroes every weapon hardpoint so the ship no longer reads as <see cref="ShipCombat.Armed"/>,
        /// keeping War's own "groups met without a fight, fight now" daily sweep from starting a real battle
        /// that these siege tests aren't set up to referee.</summary>
        static void Disarm(Ship ship)
        {
            foreach (var sub in ship.Combat.Subsystems)
                if (sub.Def.Weapon != null) sub.Hp = 0;
        }

        // ---------------------------------------------------------------- SiegeNeeded

        [Fact]
        public void SiegeNeededIsFourDaysPlusOnePerTwoHundredThousandPeopleCappedAtTwenty()
        {
            Assert.Equal(4, War.SiegeNeeded(new Settlement { Population = 0 }), 9);
            Assert.Equal(9, War.SiegeNeeded(new Settlement { Population = 1_000_000 }), 9);
            Assert.Equal(20, War.SiegeNeeded(new Settlement { Population = 3_200_000 }), 9); // exactly at the cap
            Assert.Equal(20, War.SiegeNeeded(new Settlement { Population = 1_000_000_000 }), 9); // would be 5004 uncapped
        }

        // ---------------------------------------------------------------- the siege-to-capture flow

        [Fact]
        public void UndefendedPortFallsExactlyAfterSiegeNeededDaysViaCapture()
        {
            var w = CreateWorld();
            double jd = StartJd;
            var owner = MakeFaction(w, "Homeland");
            var besieger = MakeFaction(w, "Raider");
            w.Diplomacy.Set(owner, besieger, Relation.War);
            int body = w.Catalog["Pluto"].Index;
            var port = w.Economy.Add("Lonely Port", body, SettlementKind.Colony, owner, population: 0);
            port.MaxPlatforms = 0;
            var leader = ParkWarship(w, besieger, ShipClass.Destroyer, body);
            w.War.Orders[leader] = new GroupOrder { Kind = OrderKind.Siege, Body = body };

            double needed = War.SiegeNeeded(port); // 4 days at zero population
            for (int day = 1; day < needed; day++)
            {
                jd += 1;
                w.War.Update(jd);
                Assert.Equal(owner, port.Owner);
                Assert.Equal((double)day, w.War.SiegeDays(port), 9);
            }

            jd += 1;
            w.War.Update(jd);
            Assert.Equal(besieger, port.Owner);
            Assert.Equal(0, port.DaysSinceCapture, 9);
            Assert.Equal(0, w.War.SiegeDays(port), 9);
        }

        [Fact]
        public void CaptureRevokesATradersBookingOnceItCanNoLongerDockThere()
        {
            var w = CreateWorld();
            double jd = StartJd;
            var owner = MakeFaction(w, "Homeland2");
            var besieger = MakeFaction(w, "Raider2");
            var traderFaction = MakeFaction(w, "Neutrals", military: false);
            w.Diplomacy.Set(owner, besieger, Relation.War);
            w.Diplomacy.Set(traderFaction, besieger, Relation.War); // at war with the besieger, at peace with the owner
            int body = w.Catalog["Pluto"].Index;
            var port = w.Economy.Add("Lonely Port 2", body, SettlementKind.Colony, owner, population: 0);
            port.MaxPlatforms = 0;
            var leader = ParkWarship(w, besieger, ShipClass.Destroyer, body);
            w.War.Orders[leader] = new GroupOrder { Kind = OrderKind.Siege, Body = body };

            var trader = w.Fleet.AddShip("Booked Freighter", ShipClass.Freighter, traderFaction, ShipRole.Trader);
            trader.CargoDestination = port;

            double needed = War.SiegeNeeded(port);
            for (int day = 1; day < needed; day++)
            {
                jd += 1;
                w.War.Update(jd);
                Assert.Same(port, trader.CargoDestination); // still welcome while the port holds out
            }

            jd += 1;
            w.War.Update(jd);
            Assert.Equal(besieger, port.Owner);
            Assert.Null(trader.CargoDestination); // the new owner is at war with this trade house
        }

        // ---------------------------------------------------------------- what blocks or resets a siege

        [Fact]
        public void PlatformsAtThePortBlockTheSiegeEvenUnderSustainedBlockade()
        {
            var w = CreateWorld();
            double jd = StartJd;
            var owner = MakeFaction(w, "Owner3");
            var besieger = MakeFaction(w, "Besieger3");
            w.Diplomacy.Set(owner, besieger, Relation.War);
            int body = w.Catalog["Pluto"].Index;
            var port = w.Economy.Add("Fortified Port", body, SettlementKind.Colony, owner, population: 0);
            port.MaxPlatforms = 1;
            port.Platforms.Add(new ShipCombat(CombatSpec.Platform));
            var leader = ParkWarship(w, besieger, ShipClass.Destroyer, body);
            Disarm(leader); // keep War's daily sweep from starting a real platform-vs-ship battle here
            w.War.Orders[leader] = new GroupOrder { Kind = OrderKind.Siege, Body = body };

            for (int day = 1; day <= 10; day++)
            {
                jd += 1;
                w.War.Update(jd);
                Assert.Equal(owner, port.Owner);
                Assert.Equal(0, w.War.SiegeDays(port), 9);
            }
        }

        [Fact]
        public void AnActiveBattleAtThePortBlocksTheSiegeWhileItLasts()
        {
            var w = CreateWorld();
            double jd = StartJd;
            var owner = MakeFaction(w, "Owner4");
            var besieger = MakeFaction(w, "Besieger4");
            w.Diplomacy.Set(owner, besieger, Relation.War);
            int body = w.Catalog["Pluto"].Index;
            var port = w.Economy.Add("Contested Port", body, SettlementKind.Colony, owner, population: 0);
            port.MaxPlatforms = 0;
            var leader = ParkWarship(w, besieger, ShipClass.Destroyer, body);
            w.War.Orders[leader] = new GroupOrder { Kind = OrderKind.Siege, Body = body };
            // An empty battle: enough to make BattleAt(body) non-null for one tick (it resolves itself
            // the moment it is actually advanced, having nobody left to fight).
            w.War.Battles.Add(new Battle(9001, body, "Ongoing fight", DVec3.Zero, jd, 1));

            jd += 1;
            w.War.Update(jd);
            Assert.Equal(owner, port.Owner);
            Assert.Equal(0, w.War.SiegeDays(port), 9); // the active battle holds the siege clock at zero

            jd += 1;
            w.War.Update(jd); // the empty battle resolves itself this tick, clearing the way
            Assert.Equal(1, w.War.SiegeDays(port), 9);
        }

        [Fact]
        public void TheNinetyDayCaptureGraceGarrisonBlocksAnotherSiegeUntilItExpires()
        {
            var w = CreateWorld();
            double jd = StartJd;
            var owner = MakeFaction(w, "Owner5");
            var besieger = MakeFaction(w, "Besieger5");
            w.Diplomacy.Set(owner, besieger, Relation.War);
            int body = w.Catalog["Pluto"].Index;
            var port = w.Economy.Add("Freshly Taken Port", body, SettlementKind.Colony, owner, population: 0);
            port.MaxPlatforms = 0;
            port.DaysSinceCapture = 0; // just captured, inside the 90-day grace window
            var leader = ParkWarship(w, besieger, ShipClass.Destroyer, body);
            w.War.Orders[leader] = new GroupOrder { Kind = OrderKind.Siege, Body = body };

            for (int day = 1; day <= 10; day++)
            {
                jd += 1;
                w.War.Update(jd);
                Assert.Equal(owner, port.Owner);
                Assert.Equal(0, w.War.SiegeDays(port), 9);
            }

            port.DaysSinceCapture = War.CaptureGraceDays; // the grace period has just elapsed
            jd += 1;
            w.War.Update(jd);
            Assert.Equal(1, w.War.SiegeDays(port), 9);
        }

        [Fact]
        public void AParkedFriendlyWarshipDefendsThePortAndResetsAnyAccumulatedSiege()
        {
            var w = CreateWorld();
            double jd = StartJd;
            var owner = MakeFaction(w, "Owner6");
            var besieger = MakeFaction(w, "Besieger6");
            w.Diplomacy.Set(owner, besieger, Relation.War);
            int body = w.Catalog["Pluto"].Index;
            var port = w.Economy.Add("Garrisoned Port", body, SettlementKind.Colony, owner, population: 0);
            port.MaxPlatforms = 0;
            var leader = ParkWarship(w, besieger, ShipClass.Destroyer, body);
            Disarm(leader); // keep War's daily sweep from starting a real battle once the defender shows up
            w.War.Orders[leader] = new GroupOrder { Kind = OrderKind.Siege, Body = body };

            jd += 1; w.War.Update(jd);
            jd += 1; w.War.Update(jd);
            Assert.Equal(2, w.War.SiegeDays(port), 9); // two days of unopposed siege accrue normally

            var defender = ParkWarship(w, owner, ShipClass.Destroyer, body);
            Disarm(defender);
            jd += 1;
            w.War.Update(jd);
            Assert.Equal(owner, port.Owner);
            Assert.Equal(0, w.War.SiegeDays(port), 9);
        }

        [Fact]
        public void HavenSettlementsAreNeverSieged()
        {
            var w = CreateWorld();
            double jd = StartJd;
            var besieger = MakeFaction(w, "Besieger7");
            int body = w.Catalog["Pluto"].Index;
            var haven = w.Economy.Add("Test Haven", body, SettlementKind.Haven, w.Pirates, population: 0);
            haven.MaxPlatforms = 0;
            var leader = ParkWarship(w, besieger, ShipClass.Destroyer, body);
            w.War.Orders[leader] = new GroupOrder { Kind = OrderKind.Siege, Body = body };

            for (int day = 1; day <= 10; day++)
            {
                jd += 1;
                w.War.Update(jd);
            }

            Assert.Equal(w.Pirates, haven.Owner);
            Assert.Equal(0, w.War.SiegeDays(haven), 9);
        }

        // ---------------------------------------------------------------- AtWar

        [Fact]
        public void AtWarIsTrueForALawfulEnemyButIgnoresAWarRelationWithPirates()
        {
            var w = CreateWorld();
            var f = MakeFaction(w, "LawfulFaction");
            w.Diplomacy.Set(f, w.Pirates, Relation.War);
            Assert.False(w.War.AtWar(f));

            var enemy = MakeFaction(w, "LawfulEnemy");
            w.Diplomacy.Set(f, enemy, Relation.War);
            Assert.True(w.War.AtWar(f));
        }

        // ---------------------------------------------------------------- DefenceAt

        [Fact]
        public void DefenceAtSumsOnlyHostilePlatformsAndParkedHostileWarships()
        {
            var w = CreateWorld();
            var attacker = MakeFaction(w, "Attacker1");
            var defender = MakeFaction(w, "Defender1");
            w.Diplomacy.Set(attacker, defender, Relation.War);
            int body = w.Catalog["Pluto"].Index;
            var port = w.Economy.Add("Defended Port", body, SettlementKind.Colony, defender, population: 0);
            port.MaxPlatforms = 2;
            port.Platforms.Add(new ShipCombat(CombatSpec.Platform));
            port.Platforms.Add(new ShipCombat(CombatSpec.Platform));

            var leader = ParkWarship(w, defender, ShipClass.Cruiser, body);
            var escort = w.Fleet.AddShip("Escort", ShipClass.Destroyer, defender, ShipRole.Escort);
            w.Fleet.AddEscort(leader, escort, new DVec3(1, 0, 0));

            // Distractors that must NOT be counted.
            var notParked = ParkWarship(w, defender, ShipClass.Destroyer, body);
            notParked.Phase = ShipPhase.Transit;
            var destroyedHostile = ParkWarship(w, defender, ShipClass.Destroyer, body);
            destroyedHostile.Destroyed = true;
            var trader = w.Fleet.AddShip("Trader", ShipClass.Freighter, defender, ShipRole.Trader);
            trader.Orbit = new ParkingOrbit { Body = body };
            trader.Phase = ShipPhase.Parked;
            var friendlyToAttacker = ParkWarship(w, attacker, ShipClass.Destroyer, body);

            double expected = port.Platforms.Sum(p => p.Strength) + leader.Combat.Strength + escort.Combat.Strength;
            Assert.Equal(expected, w.War.DefenceAt(body, attacker), 6);
        }

        // ---------------------------------------------------------------- GroupStrength

        [Fact]
        public void GroupStrengthSumsTheLeaderPlusOnlyItsLivingEscorts()
        {
            var w = CreateWorld();
            var f = MakeFaction(w, "GroupTest");
            var leader = w.Fleet.AddShip("Flag", ShipClass.Cruiser, f, ShipRole.Patrol);
            var escortAlive = w.Fleet.AddShip("Escort1", ShipClass.Destroyer, f, ShipRole.Escort);
            var escortDead = w.Fleet.AddShip("Escort2", ShipClass.Destroyer, f, ShipRole.Escort);
            w.Fleet.AddEscort(leader, escortAlive, new DVec3(1, 0, 0));
            w.Fleet.AddEscort(leader, escortDead, new DVec3(-1, 0, 0));
            escortDead.Destroyed = true;

            // An escort of some other leader must not bleed into this group's total.
            var otherLeader = w.Fleet.AddShip("OtherFlag", ShipClass.Cruiser, f, ShipRole.Patrol);
            var otherEscort = w.Fleet.AddShip("OtherEscort", ShipClass.Destroyer, f, ShipRole.Escort);
            w.Fleet.AddEscort(otherLeader, otherEscort, new DVec3(0, 1, 0));

            double expected = leader.Combat.Strength + escortAlive.Combat.Strength;
            Assert.Equal(expected, w.War.GroupStrength(leader), 6);
        }

        // ---------------------------------------------------------------- ChooseWarship

        [Fact]
        public void ChooseWarshipReturnsNullForANonMilitaryOwner()
        {
            var w = CreateWorld();
            int body = w.Catalog["Pluto"].Index;
            var yard = w.Economy.Add("Civvie Yard", body, SettlementKind.Station, w.Independents, population: 1000);
            Assert.Null(w.War.ChooseWarship(yard));
        }

        [Fact]
        public void ChooseWarshipReturnsNullAtTheNavyCapInPeacetime()
        {
            var w = CreateWorld();
            var f = MakeFaction(w, "PeaceNavy");
            w.NavyCap = 2;
            int body = w.Catalog["Pluto"].Index;
            var yard = w.Economy.Add("Peace Yard", body, SettlementKind.Station, f, population: 1000);
            w.Fleet.AddShip("A", ShipClass.Destroyer, f, ShipRole.Patrol);
            w.Fleet.AddShip("B", ShipClass.Destroyer, f, ShipRole.Patrol);

            Assert.False(w.War.AtWar(f)); // sanity: the peacetime cap is the one that should apply here
            Assert.Null(w.War.ChooseWarship(yard));
        }

        [Fact]
        public void ChooseWarshipUsesTheHigherWartimeCapNotThePeacetimeOne()
        {
            var w = CreateWorld();
            var f = MakeFaction(w, "WarNavy");
            var rival = MakeFaction(w, "WarNavyRival", military: false);
            w.Diplomacy.Set(f, rival, Relation.War);
            w.NavyCap = 1; // would already block below if this were the cap in effect
            w.NavyCapAtWar = 3;
            int body = w.Catalog["Pluto"].Index;
            var yard = w.Economy.Add("War Yard", body, SettlementKind.Station, f, population: 1000);
            w.Fleet.AddShip("A", ShipClass.Destroyer, f, ShipRole.Patrol);
            w.Fleet.AddShip("B", ShipClass.Destroyer, f, ShipRole.Patrol);

            Assert.True(w.War.AtWar(f));
            Assert.NotNull(w.War.ChooseWarship(yard)); // 2 built, under the wartime cap of 3

            w.Fleet.AddShip("C", ShipClass.Destroyer, f, ShipRole.Patrol);
            Assert.Null(w.War.ChooseWarship(yard)); // 3 built, at the wartime cap
        }

        [Fact]
        public void ChooseWarshipOtherwiseStepsThroughARepeatingSevenHullCycle()
        {
            var w = CreateWorld();
            var f = MakeFaction(w, "CycleFaction");
            w.NavyCap = 1000;
            w.NavyCapAtWar = 1000;
            int body = w.Catalog["Pluto"].Index;
            var yard = w.Economy.Add("Cycle Yard", body, SettlementKind.Station, f, population: 1000);

            var seen = new System.Collections.Generic.List<ShipClass?>();
            for (int i = 0; i < 7; i++)
            {
                seen.Add(w.War.ChooseWarship(yard));
                w.War.LaunchWarship(yard, ShipClass.Destroyer, StartJd);
            }
            var eighth = w.War.ChooseWarship(yard);

            Assert.Equal(seen[0], eighth); // the cycle repeats every 7 hulls laid down
            Assert.True(seen.Distinct().Count() > 1, "ChooseWarship returned the same class every time -- the cycle was never actually exercised");
        }
    }
}
