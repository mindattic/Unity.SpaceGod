using System.Collections.Generic;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The faction command layer's raider doctrine and gate orders (U.SPC-T374): a band sizes a mark up before it springs
    /// (a juicy, lightly escorted mark is worth more risk; a civilian hull draws more heat than it is worth unless it pays
    /// well or carries someone to ransom), it disables and loots rather than destroys (shields and engines first, a cargo
    /// demand, drones to collect, then away), a hull destroyed draws far more heat than a clean robbery, and a navy's
    /// command sends a group to hold its own gate against raiders lying off it.
    /// </summary>
    [TestFixture]
    public class RaidDoctrineTests
    {
        const double StartJd = 2461324.0;

        static readonly Faction Navy = new Faction("Navy", "NAV", new Rgb(0, 0, 1)) { IsMilitary = true, MarineQuality = 1.2 };
        static readonly Faction Pirates = new Faction("Pirates", "PIR", new Rgb(1, 0, 0)) { IsPirate = true, MarineQuality = 1.1 };
        static readonly Faction Traders = new Faction("Traders", "TRD", new Rgb(0, 1, 0));

        static Ship Hull(ShipClass c, Faction f, int id, ShipRole role = ShipRole.Trader) =>
            new Ship { Id = id, Name = c + "" + id, Spec = ShipClassSpec.Of(c), Faction = f, Role = role, Combat = new ShipCombat(CombatSpec.Of(c), c) };

        static Ship Laden(Ship s, Good g, double tonnes)
        {
            s.HasCargo = true;
            s.CargoGood = g;
            s.CargoTonnes = tonnes;
            return s;
        }

        static Battle NewBattle(int seed) => new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed)
        {
            Launch = FighterAI.Launch,
            FighterStep = FighterAI.Step,
        };

        /// <summary>A visible world with nobody's port at it (no platforms to start a fight), and nobody parked there.</summary>
        static int OpenGround(World w)
        {
            var settled = new HashSet<int>(w.Economy.Settlements.Select(s => s.Body));
            foreach (int b in w.FreeShipPorts())
                if (!settled.Contains(b) && w.War.BattleAt(b) == null && !w.Fleet.Ships.Any(s => !s.Destroyed && s.Phase == ShipPhase.Parked && s.Orbit?.Body == b))
                    return b;
            Assert.Fail("no open ground in Sol");
            return -1;
        }

        // ---------------------------------------------------------------- sizing up a mark

        [Test]
        public void AJuicyMarkIsWorthMoreRiskThanAPoorOne()
        {
            Assert.That(RaidDoctrine.Nerve(0), Is.EqualTo(RaidDoctrine.BaseNerve).Within(1e-9), "an empty hold buys no extra nerve");
            Assert.That(RaidDoctrine.Nerve(1e8), Is.GreaterThan(RaidDoctrine.Nerve(5e6)));
            Assert.That(RaidDoctrine.Nerve(1e15), Is.EqualTo(RaidDoctrine.MaxNerve).Within(1e-9), "no haul is worth any odds");

            // An escort as strong as the band itself: too much for a hold of food, worth it for a hold of electronics.
            var food = Laden(Hull(ShipClass.Freighter, Traders, 1), Good.Food, 20000);
            var chips = Laden(Hull(ShipClass.Freighter, Traders, 2), Good.Electronics, 20000);
            double band = 100, escort = 100;
            Assert.That(RaidDoctrine.WorthTaking(RaidDoctrine.PreyValue(food), RaidDoctrine.Heat(food), escort, band), Is.False);
            Assert.That(RaidDoctrine.WorthTaking(RaidDoctrine.PreyValue(chips), RaidDoctrine.Heat(chips), escort, band), Is.True);
            // Unescorted, any merchant will do.
            var empty = Hull(ShipClass.Freighter, Traders, 3);
            Assert.That(RaidDoctrine.WorthTaking(RaidDoctrine.PreyValue(empty), RaidDoctrine.Heat(empty), 0, band), Is.True);
        }

        [Test]
        public void ACivilianHullIsLeftAloneUnlessItPaysWellOrCarriesSomeoneWorthRansoming()
        {
            var w = World.CreateDefault(new Ephemeris(BodyCatalog.CreateSolSystem()), StartJd);
            var yacht = Hull(ShipClass.Courier, Traders, 1, ShipRole.Free);
            Assert.That(RaidDoctrine.IsCivilian(yacht), Is.True);
            Assert.That(RaidDoctrine.Heat(yacht), Is.EqualTo(RaidDoctrine.CivilianHeat));
            Assert.That(RaidDoctrine.WorthTaking(RaidDoctrine.PreyValue(yacht), RaidDoctrine.Heat(yacht), 0, 100), Is.False,
                "an unguarded yacht is no prize worth the navies' wrath");

            // A courier carrying a party on an ordinary fare is a liner now: civilian, and still not worth it.
            var liner = Hull(ShipClass.Courier, Traders, 2, ShipRole.Courier);
            Assert.That(RaidDoctrine.IsCivilian(liner, w.Contracts), Is.False, "a courier with no passengers is a merchant");
            var fare = new Contract { Kind = ContractKind.Passengers, State = ContractState.Taken, Taker = liner, People = 6, Aboard = true, Pay = 50000 };
            w.Contracts.All.Add(fare);
            Assert.That(RaidDoctrine.IsCivilian(liner, w.Contracts), Is.True);
            Assert.That(RaidDoctrine.WorthTaking(RaidDoctrine.PreyValue(liner, w.Contracts), RaidDoctrine.Heat(liner, w.Contracts), 0, 100), Is.False);

            // Someone aboard who paid a fortune for the passage is worth the heat: a VIP to ransom.
            fare.People = 1;
            fare.Pay = RaidDoctrine.VipFare;
            Assert.That(RaidDoctrine.PreyValue(liner, w.Contracts), Is.GreaterThanOrEqualTo(RaidDoctrine.CivilianMinPrize));
            Assert.That(RaidDoctrine.WorthTaking(RaidDoctrine.PreyValue(liner, w.Contracts), RaidDoctrine.Heat(liner, w.Contracts), 0, 100), Is.True);
        }

        [Test]
        public void DestroyingAHullDrawsFarMoreHeatThanRobbingItAndACivilianMoreStill()
        {
            var freighter = Laden(Hull(ShipClass.Freighter, Traders, 1), Good.Food, 20000);
            double robbery = RaidDoctrine.PreyValue(freighter) * PirateClans.PledgeShare;
            Assert.That(RaidDoctrine.DestroyHeat(freighter), Is.GreaterThan(robbery * 3));
            var yacht = Hull(ShipClass.Courier, Traders, 2, ShipRole.Free);
            var courier = Hull(ShipClass.Courier, Traders, 3, ShipRole.Courier);
            Assert.That(RaidDoctrine.DestroyHeat(yacht), Is.EqualTo(RaidDoctrine.DestroyHeat(courier) * RaidDoctrine.CivilianHeat).Within(1));
            Assert.That(RaidDoctrine.DestroyHeat(Hull(ShipClass.Destroyer, Navy, 4)), Is.EqualTo(0), "a warship is fair game: no extra heat");
        }

        // ---------------------------------------------------------------- disable and loot

        [Test]
        public void RaidersStripShieldsAndEnginesAndNeverAimAtAMerchantsReactor()
        {
            var b = NewBattle(3);
            var merchant = b.AddShip(Laden(Hull(ShipClass.Freighter, Traders, 0), Good.Food, 200), 0, new DVec3(4, 0, 0), -DVec3.UnitX);
            var raider = b.AddShip(Hull(ShipClass.Destroyer, Pirates, 1), 1, new DVec3(-4, 0, 0), DVec3.UnitX);
            var navy = b.AddShip(Hull(ShipClass.Destroyer, Navy, 2), 0, new DVec3(4, 4, 0), -DVec3.UnitX);
            int drive = 0, reactor = 0, picks = 4000;
            for (int i = 0; i < picks; i++)
            {
                var sub = b.PickSubsystem(merchant, raider);
                if (sub == null) continue;
                if (sub.Kind == SubsystemKind.Engine || sub.Kind == SubsystemKind.ShieldGenerator) drive++;
                if (sub.Kind == SubsystemKind.Reactor) reactor++;
            }
            Assert.That(reactor, Is.EqualTo(0), "raiders shot at the reactor of a hull they mean to rob");
            Assert.That(drive, Is.GreaterThan(picks / 2), "raiders didn't go for shields and engines first");
            // A warship is fought like any other: its reactor is still a lucky shot.
            int navyReactor = 0;
            for (int i = 0; i < picks; i++) if (b.PickSubsystem(navy, raider)?.Kind == SubsystemKind.Reactor) navyReactor++;
            Assert.That(navyReactor, Is.GreaterThan(0));
        }

        [Test]
        public void ADisabledMerchantIsHeldNotShotAndJettisonsItsCargoForTheDrones()
        {
            var b = NewBattle(5);
            var freighter = Laden(Hull(ShipClass.Freighter, Traders, 0), Good.Electronics, 400);
            var merchant = b.AddShip(freighter, 0, new DVec3(4, 0, 0), -DVec3.UnitX);
            var raider = b.AddShip(Hull(ShipClass.Destroyer, Pirates, 1), 1, new DVec3(-4, 0, 0), DVec3.UnitX);
            // Dead in space: its engines shot away.
            foreach (var s in merchant.Combat.Subsystems)
                if (s.Kind == SubsystemKind.Engine) s.Hp = 0;
            Assert.That(merchant.Combat.Disabled, Is.True);
            Assert.That(b.PickTarget(raider), Is.Not.SameAs(merchant), "raiders took aim at a hull lying dead in space");

            var demands = new List<Faction>();
            var terms = new List<SurrenderTerms>();
            b.CargoDemanded += (_, u, by) => demands.Add(by);
            b.SurrenderDecided += (_, u, by, t) => terms.Add(t);
            double hull = merchant.Combat.Hull;
            for (int i = 0; i < 20 && !merchant.Fled; i++) b.Advance(0.5);
            Assert.That(demands, Is.EqualTo(new[] { Pirates }), "the raiders never demanded the cargo");
            Assert.That(terms, Is.EqualTo(new[] { SurrenderTerms.RansomCargo }));
            Assert.That(merchant.Fled, Is.True, "a hull that handed its cargo over was not let go");
            Assert.That(merchant.Dead, Is.False);
            Assert.That(merchant.Combat.Hull, Is.EqualTo(hull).Within(1e-9), "the raiders fired on a hull they were robbing");
            Assert.That(b.Robbed[1], Is.True);
        }

        [Test]
        public void WithTheCargoInHandRaidersWithdrawRatherThanFightTheEscort()
        {
            var b = NewBattle(7);
            var freighter = Laden(Hull(ShipClass.Freighter, Traders, 0), Good.Electronics, 400);
            var merchant = b.AddShip(freighter, 0, new DVec3(4, 0, 0), -DVec3.UnitX);
            var escort = b.AddShip(Hull(ShipClass.Fighter, Navy, 2), 0, new DVec3(30, 0, 0), -DVec3.UnitX);
            var raider = b.AddShip(Hull(ShipClass.Destroyer, Pirates, 1), 1, new DVec3(-4, 0, 0), DVec3.UnitX);
            foreach (var s in merchant.Combat.Subsystems)
                if (s.Kind == SubsystemKind.Engine) s.Hp = 0;
            for (int i = 0; i < 20 && !merchant.Fled; i++) b.Advance(0.5);
            Assert.That(merchant.Fled, Is.True, "the merchant was never robbed");
            Assert.That(escort.Present, Is.True);
            b.Advance(1);
            Assert.That(raider.Fleeing, Is.True, "the raiders stayed to fight the escort for nothing");
        }

        [Test]
        public void APlayerWhoRefusesTheDemandIsNoLongerSpared()
        {
            var b = NewBattle(9);
            var freighter = Laden(Hull(ShipClass.Freighter, Traders, 0), Good.Food, 200);
            var player = b.AddShip(freighter, 0, new DVec3(6, 0, 0), -DVec3.UnitX);
            player.PlayerControlled = true;
            var raider = b.AddShip(Hull(ShipClass.Destroyer, Pirates, 1), 1, new DVec3(-6, 0, 0), DVec3.UnitX);
            foreach (var s in player.Combat.Subsystems)
                if (s.Kind == SubsystemKind.Engine) s.Hp = 0;
            Assert.That(b.PickTarget(raider), Is.Null, "a dead-in-space hull with cargo aboard is robbed, not shot");
            for (int i = 0; i < 10 && player.CargoDemandedBy == null; i++) b.Advance(0.5);
            Assert.That(player.CargoDemandedBy, Is.SameAs(Pirates), "a disabled player was never asked for the cargo");
            b.AnswerCargoDemand(player, false);
            Assert.That(player.CargoRefused, Is.True);
            Assert.That(b.PickTarget(raider), Is.SameAs(player), "a refusal is answered with the guns");
        }

        [Test]
        public void ARaiderBandDestroyingAMerchantDrawsHeatOnItsClan()
        {
            var w = World.CreateDefault(new Ephemeris(BodyCatalog.CreateSolSystem()), StartJd);
            w.War.Battles.Clear();
            int body = w.Catalog["Ceres"].Index;
            var band = w.Fleet.AddShip("Black Test", ShipClass.Destroyer, w.Pirates, ShipRole.Patrol);
            w.Fleet.Station(band, body, StartJd);
            w.War.Orders[band] = new GroupOrder { Kind = OrderKind.Raid, Body = body };
            var trader = w.Fleet.AddShip("Plain Dealer", ShipClass.Freighter, w.Independents, ShipRole.Trader);
            w.Fleet.Station(trader, body, StartJd);
            Laden(trader, Good.Food, 1000);
            double heat = 0;
            w.War.Heat += (clan, h, jd) => { if (clan == w.Pirates) heat += h; };
            var b = w.War.TryStartBattle(body, w.Pirates, StartJd);
            Assert.That(b, Is.Not.Null, "a band with a laden freighter beside it and no guns about never fell on it");
            var victim = b.UnitOf(trader);
            b.UnitDestroyed(b, victim);
            Assert.That(heat, Is.EqualTo(RaidDoctrine.DestroyHeat(trader)).Within(1).Or.GreaterThan(0));
            Assert.That(w.War.HeatDrawn[w.Pirates], Is.GreaterThan(0));
        }

        // ---------------------------------------------------------------- commanded raids

        [Test]
        public void ABandLetsAnUnprofitableCivilianGoButFallsOnALadenMerchant()
        {
            var w = World.CreateDefault(new Ephemeris(BodyCatalog.CreateSolSystem()), StartJd);
            w.War.Battles.Clear();
            int body = OpenGround(w);
            // Clear the ground of anyone else's hulls, so only the band and its mark are there.
            foreach (var s in w.Fleet.Ships.Where(s => !s.Destroyed && s.Phase == ShipPhase.Parked && s.Orbit?.Body == body).ToList())
                s.Destroyed = true;
            var band = w.Fleet.AddShip("Black Test", ShipClass.Destroyer, w.Pirates, ShipRole.Patrol);
            w.Fleet.Station(band, body, StartJd);
            // Its clan's command has sent it to raid shipping here (combat is commanded, U.SPC-T371).
            w.War.Orders[band] = new GroupOrder { Kind = OrderKind.Raid, Body = body };
            var yacht = w.Fleet.AddShip("Idle Hours", ShipClass.Courier, w.Independents, ShipRole.Free);
            w.Fleet.Station(yacht, body, StartJd);
            Assert.That(w.War.TryStartBattle(body, w.Pirates, StartJd), Is.Null, "raiders fell on a yacht worth less than the heat");
            Assert.That(w.War.BestMark(new List<Ship> { yacht }, 0, w.War.GroupStrength(band)), Is.Null);

            var trader = w.Fleet.AddShip("Plain Dealer", ShipClass.Freighter, w.Independents, ShipRole.Trader);
            w.Fleet.Station(trader, body, StartJd);
            Laden(trader, Good.Machinery, 5000);
            Assert.That(w.War.BestMark(new List<Ship> { yacht, trader }, 0, w.War.GroupStrength(band)), Is.SameAs(trader));
            Assert.That(w.War.TryStartBattle(body, w.Pirates, StartJd), Is.Not.Null, "raiders let a laden, unguarded freighter go");
        }

        [Test]
        public void ANavySendsAGroupToHoldItsGateAgainstRaidersLyingOffIt()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var w = sol.World;
            int root = sol.Catalog.Root.Index;
            var gate = sol.Gates.FirstOrDefault(x => x.Owner != null && x.Owner.IsMilitary && !x.Owner.IsPirate);
            Assert.That(gate, Is.Not.Null, "no navy holds a gate in Sol");
            var navy = gate.Owner;

            var band = w.Fleet.Ships.First(s => s.Faction == w.Pirates && !s.IsEscort && !s.Destroyed && !s.Runner && s.Spec.IsMilitary);
            w.War.StakeOut(band, gate, StartJd + 60);
            Fleet.PinAt(band, root, band.StakeoutKm.Value, -gate.PositionKm.Normalized, StartJd);

            // A strong group of the gate's navy, idle at one of its ports.
            int port = w.Economy.Settlements.First(s => s.Owner == navy && s.Kind != SettlementKind.Haven).Body;
            var leader = w.Fleet.AddShip("Gatekeeper", ShipClass.Battleship, navy, ShipRole.Patrol);
            w.Fleet.Station(leader, port, StartJd);
            // Not the navy's home guard (its oldest group, which never leaves home).
            Assert.That(w.War.IsHomeGuard(leader), Is.False);
            Assert.That(w.War.GroupStrength(leader), Is.GreaterThan(w.War.GroupStrength(band) * 1.25));

            // Nothing more pressing for command to send it to: the navy at peace with the other states, and no other raiders
            // or enemy warships at its ports.
            w.War.Battles.Clear();
            foreach (var f in w.Fleet.Factions.Where(f => f != navy && !f.IsPirate)) w.Diplomacy.Set(navy, f, Relation.Peace);
            var ports = new HashSet<int>(w.Economy.Settlements.Where(s => s.Owner == navy).Select(s => s.Body));
            foreach (var s in w.Fleet.Ships.Where(s => !s.Destroyed && s.Controller != band && s.Faction != navy && (s.Spec.IsMilitary || s.Faction.IsPirate)).ToList())
                if (s.Controller.Orbit != null && ports.Contains(s.Controller.Orbit.Body)) s.Destroyed = true;

            // The navy knows the band is there: a picket of its own off the gate has made it out, and word has had time to
            // reach the group.
            var picket = w.Fleet.AddShip("Lookout", ShipClass.Fighter, navy, ShipRole.Patrol);
            Fleet.PinAt(picket, root, band.StakeoutKm.Value + new DVec3(300, 0, 0), DVec3.UnitX, StartJd);
            w.Sensors.Sweep(StartJd);
            double jd = StartJd + 0.05;
            Assert.That(w.War.ChooseNext(leader, port, jd), Is.EqualTo(root), "command did not send the group out to the gates");
            Assert.That(w.War.OrderOf(leader).Kind, Is.EqualTo(OrderKind.HoldGate));
            Assert.That(leader.StakeoutKm, Is.Not.Null);
            Assert.That((leader.StakeoutKm.Value - gate.ApproachKm).Length, Is.LessThan(500), "the group took station off some other gate");
            Assert.That(w.War.Commanded(leader, root), Is.True, "a group holding a gate is not commanded to fight there");

            // Once the raiders are gone, command looks again and the gate no longer needs holding.
            band.Destroyed = true;
            leader.StakeoutUntilJd = StartJd;
            w.War.ChooseNext(leader, port, jd + 1);
            Assert.That(w.War.OrderOf(leader).Kind, Is.Not.EqualTo(OrderKind.HoldGate));
            Assert.That(leader.StakeoutKm, Is.Null);
        }
    }
}
