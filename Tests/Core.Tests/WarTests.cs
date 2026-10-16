using System.Linq;
using SolarSystem.Core;
using NUnit.Framework;

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
    [TestFixture]
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

        [Test]
        public void ANewWarshipFromTheYardIsNotMadeAnEscortOfThePlayersShip()
        {
            var w = CreateWorld();
            var yard = w.Economy.Settlements.First(s => s.HasShipyard && s.Owner == w.Sol);
            var flown = ParkWarship(w, w.Sol, ShipClass.Battleship, yard.Body);
            w.PilotedShip = flown;
            var built = w.War.LaunchWarship(yard, ShipClass.Destroyer, StartJd);
            Assert.That(built.Leader, Is.Not.SameAs(flown));
        }

        [Test]
        public void ABattleEndingLeavesThePlayersShipToThePlayer()
        {
            var w = CreateWorld();
            var player = MakeFaction(w, "Player Navy 2");
            var enemy = MakeFaction(w, "Enemy Navy 2");
            w.Diplomacy.Set(player, enemy, Relation.War);
            int here = w.Catalog["Pluto"].Index;
            var foe = ParkWarship(w, enemy, ShipClass.Destroyer, here);
            var flown = ParkWarship(w, player, ShipClass.Battleship, here);
            flown.PhaseEndJd = StartJd - 1; // its own schedule ran out while it was being flown
            flown.DestinationBody = here;
            w.PilotedShip = flown;
            var b = w.War.TryStartPlayerBattle(flown, here, new DVec3(3000, 0, 0), DVec3.UnitX, StartJd);
            Assert.That(b, Is.Not.Null);
            var foeUnit = b.UnitOf(foe);
            b.ApplyDamage(foeUnit, null, 1e9, foeUnit.Pos);
            double jd = StartJd;
            for (int i = 0; i < 200 && w.War.Battles.Contains(b); i++) w.War.Update(jd += 1.0 / 1440);
            Assert.That(w.War.Battles, Does.Not.Contain(b));
            Assert.That(flown.PhaseEndJd, Is.EqualTo(StartJd - 1), "the AI rescheduled a ship the player is flying");
        }

        [Test]
        public void APortsPlatformsAreNotRefittedInTheMiddleOfAFight()
        {
            var w = CreateWorld();
            var port = w.Economy.Settlements.First(s => s.Platforms.Count > 0 && !s.Fallen);
            var platform = port.Platforms[0];
            platform.Hull = platform.Spec.Hull * 0.5;
            double hull = platform.Hull;
            w.War.Battles.Add(new Battle(98, port.Body, "Battle at the port", new DVec3(1e4, 0, 0), StartJd, 1));
            w.War.Update(StartJd + 1.01);
            Assert.That(platform.Hull, Is.EqualTo(hull), "a platform was repaired mid-battle");
        }

        [Test]
        public void TheDailySweepDoesNotStartFightsOnThePlayersBehalf()
        {
            var w = CreateWorld();
            var player = MakeFaction(w, "Player Navy 7");
            var enemy = MakeFaction(w, "Enemy Navy 7");
            w.Diplomacy.Set(player, enemy, Relation.War);
            int here = w.Catalog["Pluto"].Index;
            ParkWarship(w, enemy, ShipClass.Destroyer, here);
            var flown = ParkWarship(w, player, ShipClass.Battleship, here);
            w.PilotedShip = flown;
            w.War.Update(StartJd + 1.01);
            Assert.That(w.War.BattleAt(here), Is.Null, "a fight was started in the player's name without the player");
        }

        [Test]
        public void APlayerWhoFliesIntoAFightStaysInIt()
        {
            var w = CreateWorld();
            var player = MakeFaction(w, "Player Navy 3");
            var enemy = MakeFaction(w, "Enemy Navy 3");
            w.Diplomacy.Set(player, enemy, Relation.War);
            int here = w.Catalog["Pluto"].Index;
            ParkWarship(w, enemy, ShipClass.Destroyer, here);
            var flown = ParkWarship(w, player, ShipClass.Battleship, here);
            w.PilotedShip = flown;
            var at = new DVec3(5000, 0, 0);
            var b = w.War.TryStartPlayerBattle(flown, here, at, DVec3.UnitX, StartJd);
            Assert.That(b, Is.Not.Null);
            var unit = b.UnitOf(flown);
            b.Advance(1);
            Assert.That(unit.Fled, Is.False, "the player was counted as escaped the moment it joined");
            Assert.That(unit.Pos.Length, Is.LessThan(Battle.EscapeKm));

            // Far out from the fight (still near Pluto): not pulled into it from there.
            var second = ParkWarship(w, player, ShipClass.Destroyer, here);
            w.PilotedShip = second;
            Assert.That(w.War.TryStartPlayerBattle(second, here, b.Anchor + new DVec3(0, 20000, 0), DVec3.UnitX, StartJd), Is.Null);
            // And nowhere near Pluto at all: no fight there either.
            Assert.That(w.War.TryStartPlayerBattle(second, here, new DVec3(5e8, 0, 0), DVec3.UnitX, StartJd), Is.Null);
        }

        [Test]
        public void BoardersStillAboardWhenAFightEndsDoNotStayAboardForGood()
        {
            var w = CreateWorld();
            var player = MakeFaction(w, "Player Navy 6");
            var traders = MakeFaction(w, "Hapless Traders", military: false);
            int here = w.Catalog["Pluto"].Index;
            var merchant = w.Fleet.AddShip("Hapless", ShipClass.Freighter, traders, ShipRole.Trader);
            merchant.Orbit = new ParkingOrbit { Body = here };
            merchant.Phase = ShipPhase.Parked;
            merchant.Ports = new[] { here, w.Catalog["Charon"].Index };
            var flown = ParkWarship(w, player, ShipClass.Destroyer, here);
            ParkWarship(w, player, ShipClass.Destroyer, here); // an AI warship to open the fight
            w.Diplomacy.Set(player, traders, Relation.War);
            w.PilotedShip = flown;
            var b = w.War.TryStartPlayerBattle(flown, here, new DVec3(5000, 0, 0), DVec3.UnitX, StartJd);
            Assert.That(b, Is.Not.Null);
            var mu = b.UnitOf(merchant);
            Assert.That(mu, Is.Not.Null);
            // Boarders aboard when it strikes its colours, and the player lets it go mid-fight aboard.
            var interior = merchant.Combat.Interior;
            interior.Board(player, 1.0, 5, interior.Nearest(0, 0), false, 0, new System.Random(1));
            b.Surrender(mu);
            Assert.That(b.OfferSurrenderTerms(mu, SurrenderTerms.LetGo), Is.True);
            Assert.That(interior.UnderBoarding, Is.True);
            double jd = StartJd;
            for (int i = 0; i < 400 && w.War.Battles.Contains(b); i++) w.War.Update(jd += 1.0 / 1440);
            Assert.That(w.War.Battles, Does.Not.Contain(b));
            Assert.That(interior.Boarder, Is.Null, "the fight aboard stayed 'under way' after the battle");
            Assert.That(flown.Combat.MarinesAvailable, Is.GreaterThan(0));
        }

        [Test]
        public void APlayerShipBoardedAndTakenIsNoLongerThePlayers()
        {
            var w = CreateWorld();
            var player = MakeFaction(w, "Player Navy 5");
            int here = w.Catalog["Pluto"].Index;
            ParkWarship(w, w.Pirates, ShipClass.Destroyer, here);
            var flown = ParkWarship(w, player, ShipClass.Destroyer, here);
            w.Diplomacy.Set(player, w.Pirates, Relation.War);
            w.PilotedShip = flown;
            var b = w.War.TryStartPlayerBattle(flown, here, new DVec3(5000, 0, 0), DVec3.UnitX, StartJd);
            Assert.That(b, Is.Not.Null);
            var interior = flown.Combat.Interior;
            interior.Board(w.Pirates, 1.2, 40, interior.Nearest(0, 0), false, 0, new System.Random(1));
            foreach (var c in interior.Compartments)
            {
                c.Crew = c.Marines = 0;
                c.Boarders = 5;
            }
            double jd = StartJd;
            for (int i = 0; i < 20 && !b.UnitOf(flown).Captured; i++) w.War.Update(jd += 1.0 / 86400);
            Assert.That(b.UnitOf(flown).Captured, Is.True);
            Assert.That(w.PilotedShip, Is.Null, "the player kept flying a ship the pirates had taken");
            Assert.That(b.UnitOf(flown).PlayerControlled, Is.False);
        }

        [Test]
        public void APlayerWhoLeavesAFightIsFreeOfItAtOnce()
        {
            var w = CreateWorld();
            var player = MakeFaction(w, "Player Navy 4");
            var enemy = MakeFaction(w, "Enemy Navy 4");
            w.Diplomacy.Set(player, enemy, Relation.War);
            int here = w.Catalog["Pluto"].Index;
            ParkWarship(w, enemy, ShipClass.Battleship, here);
            ParkWarship(w, player, ShipClass.Battleship, here); // stays on, so the fight goes on without the pilot
            var flown = ParkWarship(w, player, ShipClass.Destroyer, here);
            w.PilotedShip = flown;
            var b = w.War.TryStartPlayerBattle(flown, here, new DVec3(5000, 0, 0), DVec3.UnitX, StartJd);
            Assert.That(b, Is.Not.Null);
            b.UnitOf(flown).Pos = new DVec3(Battle.EscapeKm * 1.5, 0, 0); // flown clear of the fight
            w.War.Update(StartJd + 1.0 / 1440);
            w.War.Update(StartJd + 2.0 / 1440);
            Assert.That(b.Over, Is.False);
            Assert.That(flown.Battle, Is.Null, "the pilot stayed locked to a fight it had left");
            Assert.That(w.War.TryStartPlayerBattle(flown, here, b.Anchor, DVec3.UnitX, StartJd + 2.0 / 1440), Is.Null, "not back into the same fight");

            // Released and flying its own schedule again before the fight it left is over: that fight's end
            // doesn't reach back and reschedule it.
            w.PilotedShip = null;
            flown.DestinationBody = w.Catalog["Charon"].Index;
            flown.PhaseEndJd = StartJd + 30;
            foreach (var u in b.Units) if (u.Ship != null && u.Ship != flown) b.ApplyDamage(u, null, u.Side == 0 ? 0 : 1e9, u.Pos);
            double jd = StartJd + 2.0 / 1440;
            for (int i = 0; i < 400 && w.War.Battles.Contains(b); i++) w.War.Update(jd += 1.0 / 1440);
            Assert.That(w.War.Battles, Does.Not.Contain(b));
            Assert.That(flown.PhaseEndJd, Is.EqualTo(StartJd + 30), "the old fight's end rescheduled a ship that had left it");
        }

        [Test]
        public void AnAmbushOutOnALaneIsNotTheBattleAtTheSystemRoot()
        {
            var w = CreateWorld();
            var ambush = new Battle(99, 0, "Ambush", new DVec3(1e8, 0, 0), StartJd, 1) { Site = new LaneSite() };
            w.War.Battles.Add(ambush);
            Assert.That(w.War.BattleAt(0), Is.Null, "a ship near the root would be put into a fight millions of km away");
        }

        [Test]
        public void RepeatBattlesAreNumberedWithTheRightOrdinal()
        {
            Assert.That(War.Ordinal(2), Is.EqualTo("Second"));
            Assert.That(War.Ordinal(3), Is.EqualTo("Third"));
            Assert.That(War.Ordinal(4), Is.EqualTo("4th"));
            Assert.That(War.Ordinal(11), Is.EqualTo("11th"));
            Assert.That(War.Ordinal(12), Is.EqualTo("12th"));
            Assert.That(War.Ordinal(13), Is.EqualTo("13th"));
            Assert.That(War.Ordinal(21), Is.EqualTo("21st"));
            Assert.That(War.Ordinal(22), Is.EqualTo("22nd"));
            Assert.That(War.Ordinal(23), Is.EqualTo("23rd"));
            Assert.That(War.Ordinal(111), Is.EqualTo("111th"));
        }

        [Test]
        public void ARaiderReplannedBeforeItsAmbushForgetsTheAmbush()
        {
            var w = CreateWorld();
            int body = w.Catalog["Mars"].Index;
            var raider = ParkWarship(w, w.Pirates, ShipClass.Destroyer, body);
            raider.LurkLane = w.Fleet.Lanes.Lanes[0];
            raider.LurkAt = 0.5;
            raider.LurkDays = 3;
            // Badly hurt before it ever got to the lane: off to repair instead.
            raider.Combat.Hull = raider.Combat.Spec.Hull * 0.3;
            w.War.ChooseNext(raider, body, StartJd);
            Assert.That(w.War.Orders[raider].Kind, Is.EqualTo(OrderKind.Repair));
            Assert.That(raider.LurkLane, Is.Null, "a stale ambush would stop the trip to repair on that lane for days");
        }

        // ---------------------------------------------------------------- the player's own ship

        [Test]
        public void APlayerTakingOverAShipInFlightFightsWhereItIsNotWhereItWasBound()
        {
            var w = CreateWorld();
            var player = MakeFaction(w, "Player Navy");
            var enemy = MakeFaction(w, "Enemy Navy");
            w.Diplomacy.Set(player, enemy, Relation.War);
            int here = w.Catalog["Pluto"].Index, boundFor = w.Catalog["Neptune"].Index;
            var foe = ParkWarship(w, enemy, ShipClass.Destroyer, here);
            // Taken over mid-transit: its schedule still points at Neptune, but it is flying at Pluto.
            var flown = ParkWarship(w, player, ShipClass.Destroyer, boundFor);
            w.PilotedShip = flown;
            var b = w.War.TryStartPlayerBattle(flown, here, new DVec3(3000, 0, 0), DVec3.UnitX, StartJd);
            Assert.That(b, Is.Not.Null);
            Assert.That(b.Body, Is.EqualTo(here));
            Assert.That(foe.Battle, Is.SameAs(b));
            Assert.That(flown.Battle, Is.SameAs(b));
        }

        [Test]
        public void TakingAFlagshipsHelmLeavesItsEscortsUnderANewFlagship()
        {
            var w = CreateWorld();
            var flagship = w.Fleet.Ships.First(s => !s.Destroyed && !s.IsEscort && w.Fleet.Ships.Count(e => e.Leader == s) >= 2);
            var escorts = w.Fleet.Ships.Where(e => e.Leader == flagship).ToList();
            w.War.HandOverCommand(flagship);
            Assert.That(w.Fleet.Ships.Any(e => e.Leader == flagship), Is.False, "escorts left holding station on the player's ship");
            var heir = escorts.Single(e => !e.IsEscort);
            Assert.That(escorts.Where(e => e != heir).All(e => e.Leader == heir), Is.True);
            Assert.That(heir.Phase, Is.EqualTo(flagship.Phase));
        }

        [Test]
        public void APlayerFlownWarshipIsNeverMergedIntoAnAiBattleGroup()
        {
            var w = CreateWorld();
            var navy = MakeFaction(w, "Lonely Navy");
            int body = w.Catalog["Pluto"].Index;
            ParkWarship(w, navy, ShipClass.Destroyer, w.Catalog["Neptune"].Index); // the home guard, never merged
            var flagship = ParkWarship(w, navy, ShipClass.Battleship, body);
            var flown = ParkWarship(w, navy, ShipClass.Destroyer, body);
            var leader = ParkWarship(w, navy, ShipClass.Destroyer, body);
            var escort = ParkWarship(w, navy, ShipClass.Destroyer, body);
            w.Fleet.AddEscort(flown, escort, Fleet.SlotFor(0));
            w.PilotedShip = flown;
            w.War.Update(StartJd + 1);
            Assert.That(flown.IsEscort, Is.False, "the player's ship was handed to an AI flagship");
            Assert.That(leader.Leader, Is.SameAs(flagship), "AI groups still merge as before");
            Assert.That(escort.Leader, Is.SameAs(flown), "nothing is merged into the player's ship either");
        }

        // ---------------------------------------------------------------- SiegeNeeded

        [Test]
        public void SiegeNeededIsFourDaysPlusOnePerTwoHundredThousandPeopleCappedAtTwenty()
        {
            Assert.That(War.SiegeNeeded(new Settlement { Population = 0 }), Is.EqualTo(4).Within(1e-9));
            Assert.That(War.SiegeNeeded(new Settlement { Population = 1_000_000 }), Is.EqualTo(9).Within(1e-9));
            Assert.That(War.SiegeNeeded(new Settlement { Population = 3_200_000 }), Is.EqualTo(20).Within(1e-9)); // exactly at the cap
            Assert.That(War.SiegeNeeded(new Settlement { Population = 1_000_000_000 }), Is.EqualTo(20).Within(1e-9)); // would be 5004 uncapped
        }

        // ---------------------------------------------------------------- the siege-to-capture flow

        [Test]
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
                Assert.That(port.Owner, Is.EqualTo(owner));
                Assert.That(w.War.SiegeDays(port), Is.EqualTo((double)day).Within(1e-9));
            }

            jd += 1;
            w.War.Update(jd);
            Assert.That(port.Owner, Is.EqualTo(besieger));
            Assert.That(port.DaysSinceCapture, Is.EqualTo(0).Within(1e-9));
            Assert.That(w.War.SiegeDays(port), Is.EqualTo(0).Within(1e-9));
        }

        [Test]
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
                Assert.That(trader.CargoDestination, Is.SameAs(port)); // still welcome while the port holds out
            }

            jd += 1;
            w.War.Update(jd);
            Assert.That(port.Owner, Is.EqualTo(besieger));
            Assert.That(trader.CargoDestination, Is.Null); // the new owner is at war with this trade house
        }

        // ---------------------------------------------------------------- what blocks or resets a siege

        [Test]
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
                Assert.That(port.Owner, Is.EqualTo(owner));
                Assert.That(w.War.SiegeDays(port), Is.EqualTo(0).Within(1e-9));
            }
        }

        [Test]
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
            Assert.That(port.Owner, Is.EqualTo(owner));
            Assert.That(w.War.SiegeDays(port), Is.EqualTo(0).Within(1e-9)); // the active battle holds the siege clock at zero

            jd += 1;
            w.War.Update(jd); // the empty battle resolves itself this tick, clearing the way
            Assert.That(w.War.SiegeDays(port), Is.EqualTo(1).Within(1e-9));
        }

        [Test]
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
                Assert.That(port.Owner, Is.EqualTo(owner));
                Assert.That(w.War.SiegeDays(port), Is.EqualTo(0).Within(1e-9));
            }

            port.DaysSinceCapture = War.CaptureGraceDays; // the grace period has just elapsed
            jd += 1;
            w.War.Update(jd);
            Assert.That(w.War.SiegeDays(port), Is.EqualTo(1).Within(1e-9));
        }

        [Test]
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
            Assert.That(w.War.SiegeDays(port), Is.EqualTo(2).Within(1e-9)); // two days of unopposed siege accrue normally

            var defender = ParkWarship(w, owner, ShipClass.Destroyer, body);
            Disarm(defender);
            jd += 1;
            w.War.Update(jd);
            Assert.That(port.Owner, Is.EqualTo(owner));
            Assert.That(w.War.SiegeDays(port), Is.EqualTo(0).Within(1e-9));
        }

        [Test]
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

            Assert.That(haven.Owner, Is.EqualTo(w.Pirates));
            Assert.That(w.War.SiegeDays(haven), Is.EqualTo(0).Within(1e-9));
        }

        // ---------------------------------------------------------------- AtWar

        [Test]
        public void AtWarIsTrueForALawfulEnemyButIgnoresAWarRelationWithPirates()
        {
            var w = CreateWorld();
            var f = MakeFaction(w, "LawfulFaction");
            w.Diplomacy.Set(f, w.Pirates, Relation.War);
            Assert.That(w.War.AtWar(f), Is.False);

            var enemy = MakeFaction(w, "LawfulEnemy");
            w.Diplomacy.Set(f, enemy, Relation.War);
            Assert.That(w.War.AtWar(f), Is.True);
        }

        // ---------------------------------------------------------------- DefenceAt

        [Test]
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
            Assert.That(w.War.DefenceAt(body, attacker), Is.EqualTo(expected).Within(1e-6));
        }

        // ---------------------------------------------------------------- GroupStrength

        [Test]
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
            Assert.That(w.War.GroupStrength(leader), Is.EqualTo(expected).Within(1e-6));
        }

        // ---------------------------------------------------------------- ChooseWarship

        [Test]
        public void ChooseWarshipReturnsNullForANonMilitaryOwner()
        {
            var w = CreateWorld();
            int body = w.Catalog["Pluto"].Index;
            var yard = w.Economy.Add("Civvie Yard", body, SettlementKind.Station, w.Independents, population: 1000);
            Assert.That(w.War.ChooseWarship(yard), Is.Null);
        }

        [Test]
        public void ChooseWarshipReturnsNullAtTheNavyCapInPeacetime()
        {
            var w = CreateWorld();
            var f = MakeFaction(w, "PeaceNavy");
            w.NavyCap = 2;
            int body = w.Catalog["Pluto"].Index;
            var yard = w.Economy.Add("Peace Yard", body, SettlementKind.Station, f, population: 1000);
            w.Fleet.AddShip("A", ShipClass.Destroyer, f, ShipRole.Patrol);
            w.Fleet.AddShip("B", ShipClass.Destroyer, f, ShipRole.Patrol);

            Assert.That(w.War.AtWar(f), Is.False); // sanity: the peacetime cap is the one that should apply here
            Assert.That(w.War.ChooseWarship(yard), Is.Null);
        }

        [Test]
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

            Assert.That(w.War.AtWar(f), Is.True);
            Assert.That(w.War.ChooseWarship(yard), Is.Not.Null); // 2 built, under the wartime cap of 3

            w.Fleet.AddShip("C", ShipClass.Destroyer, f, ShipRole.Patrol);
            Assert.That(w.War.ChooseWarship(yard), Is.Null); // 3 built, at the wartime cap
        }

        [Test]
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

            Assert.That(eighth, Is.EqualTo(seen[0])); // the cycle repeats every 7 hulls laid down
            Assert.That(seen.Distinct().Count() > 1, Is.True, "ChooseWarship returned the same class every time -- the cycle was never actually exercised");
        }
    }
}
