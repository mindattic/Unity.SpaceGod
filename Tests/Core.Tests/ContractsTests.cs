using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// U.SPC-T101: the contracts boards. Stations post jobs from what the sim actually needs -- freight to markets
    /// short of a good, passengers and colonists where people are leaving, escorts, patrols over troubled ports,
    /// surveys of unvisited bodies, bounties -- priced by distance, danger and urgency; offers lapse, and lapse early
    /// when the need goes; the player and NPC captains take and complete them; failing or abandoning one costs standing.
    /// </summary>
    [TestFixture]
    public class ContractsTests
    {
        const double StartJd = 2461324.0;

        static World CreateWorld()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            w.War.Battles.Clear();
            // Only the contracts a test posts.
            w.Contracts.All.Clear();
            return w;
        }

        static int Body(World w, string name) => w.Catalog[name].Index;

        static Settlement Port(World w, string name) => w.Economy.Settlements.First(s => s.Name == name);

        /// <summary>Empties a market of a good (nothing on hand, nothing on the way).</summary>
        static void Starve(Settlement s, Good g)
        {
            s.Stock[(int)g] = 0;
            s.Inbound[(int)g] = 0;
        }

        static Contract FreightTo(World w, Settlement to, Good g) =>
            w.Contracts.All.FirstOrDefault(c => c.Kind == ContractKind.Freight && c.IsOpen && c.Destination == to && c.Good == g);

        /// <summary>The player at the helm of a fresh freighter, close in at <paramref name="body"/>.</summary>
        static Ship Pilot(World w, int body, double jd)
        {
            var ship = w.Fleet.AddShip("Odd Job", ShipClass.Freighter, w.Independents, ShipRole.Free);
            ship.Orbit = new ParkingOrbit { Body = body };
            ship.Phase = ShipPhase.Parked;
            w.PilotedShip = ship;
            w.Contracts.UpdatePilot(ship, body, DVec3.Zero, jd);
            return ship;
        }

        /// <summary>An NPC merchant parked at <paramref name="body"/>.</summary>
        static Ship Merchant(World w, int body, ShipClass cls = ShipClass.Freighter, ShipRole role = ShipRole.Trader)
        {
            var ship = w.Fleet.AddShip("Contractor " + w.Fleet.Ships.Count, cls, w.Independents, role);
            ship.CapacityTonnes = Economy.CapacityOf(cls);
            ship.Ports = w.PortBodies();
            // Parked, but not yet planning a run of its own off the market (stationing would, and whether it looked
            // at the board first is a roll of the fleet's dice): these tests are of what it does with the board.
            ship.Orbit = new ParkingOrbit { Body = body, EpochJd = StartJd };
            ship.Phase = ShipPhase.Parked;
            ship.PhaseStartJd = StartJd;
            ship.PhaseEndJd = StartJd + 1;
            return ship;
        }

        /// <summary>Leaves <paramref name="c"/> the only offer on any board (a ship put into service looks at the boards at once).</summary>
        static void Only(World w, Contract c) => w.Contracts.All.RemoveAll(o => o != c && o.IsOpen);

        /// <summary>An NPC ship takes <paramref name="c"/> through its fleet hook (it looks at the board now and then, so ask until it does).</summary>
        static int TakeByHook(World w, Ship ship, Contract c, int here)
        {
            for (int i = 0; i < 50; i++)
            {
                int next = w.Contracts.ChooseNext(ship, here, StartJd);
                if (next >= 0) return next;
            }
            return -1;
        }

        // ---------------------------------------------------------------- from actual shortages

        [Test]
        public void FreightIsPostedForAMarketShortOfAGoodFromAPortThatHasItToSpare()
        {
            var w = CreateWorld();
            var mars = Port(w, "Ares Prime");
            Starve(mars, Good.Food);
            w.Contracts.Post(StartJd);
            var c = FreightTo(w, mars, Good.Food);
            Assert.That(c, Is.Not.Null, "an empty food market posts freight");
            Assert.That(c.Board, Is.Not.SameAs(mars));
            Assert.That(c.Board.Body, Is.Not.EqualTo(mars.Body), "loaded somewhere else");
            Assert.That(c.Board.Available(Good.Food), Is.GreaterThanOrEqualTo(c.Tonnes), "from a port that has it to spare");
            Assert.That(c.Patron, Is.SameAs(mars.Owner), "the short market's owner pays");
            Assert.That(c.CoverAtPost, Is.LessThan(Contracts.ShortCover));
            Assert.That(c.Urgency, Is.EqualTo(1).Within(1e-9), "nothing at all on hand is as urgent as it gets");
            Assert.That(w.Contracts.BoardAt(c.Board), Does.Contain(c));
            Assert.That(w.Contracts.BoardAt(mars).Where(o => o.Kind == ContractKind.Freight && o.Good == Good.Food), Is.Empty, "posted where the goods are, not where they go");
        }

        [Test]
        public void EveryFreightContractComesFromAMarketThatIsActuallyShort()
        {
            var w = CreateWorld();
            // A few real shortages, and every market's cover as it stands.
            Starve(Port(w, "Ares Prime"), Good.Food);
            Starve(Port(w, "Ceres Hub"), Good.MedicalSupplies);
            Starve(Port(w, "Kraken Port"), Good.Water);
            var cover = new Dictionary<(Settlement, Good), double>();
            foreach (var s in w.Economy.Settlements)
                for (int i = 0; i < GoodInfo.Count; i++)
                    cover[(s, (Good)i)] = s.Target((Good)i) > 0 ? (s.Stock[i] + s.Inbound[i]) / s.Target((Good)i) : double.PositiveInfinity;
            int before = w.Contracts.All.Count;
            w.Contracts.Post(StartJd);
            var freight = w.Contracts.All.Skip(before).Where(c => c.Kind == ContractKind.Freight).ToList();
            Assert.That(freight.Count, Is.GreaterThanOrEqualTo(3));
            foreach (var c in freight)
            {
                Assert.That(cover[(c.Destination, c.Good)], Is.LessThan(Contracts.ShortCover), c.Title + ": posted for a market that was short");
                Assert.That(c.Destination.Demand[(int)c.Good], Is.GreaterThan(0), c.Title + ": a good the market uses");
            }
        }

        [Test]
        public void AWellStockedMarketPostsNoFreightAndALotIsNotPostedTwice()
        {
            var w = CreateWorld();
            var mars = Port(w, "Ares Prime");
            mars.Stock[(int)Good.Food] = mars.Target(Good.Food) * 1.5;
            w.Contracts.Post(StartJd);
            Assert.That(FreightTo(w, mars, Good.Food), Is.Null, "plenty on hand: nothing to post");

            Starve(mars, Good.Food);
            w.Contracts.Post(StartJd);
            int lots = w.Contracts.All.Count(c => c.Kind == ContractKind.Freight && c.Destination == mars && c.Good == Good.Food);
            Assert.That(lots, Is.GreaterThan(0));
            double posted = w.Contracts.All.Where(c => c.Kind == ContractKind.Freight && c.Destination == mars && c.Good == Good.Food).Sum(c => c.Tonnes);
            Assert.That(posted, Is.LessThanOrEqualTo(mars.Target(Good.Food) * Contracts.RestoreCover + 1), "no more than brings it back up");
            w.Contracts.Post(StartJd + 1);
            Assert.That(w.Contracts.All.Count(c => c.Kind == ContractKind.Freight && c.Destination == mars && c.Good == Good.Food), Is.EqualTo(lots),
                "the need is on the board already: the next day posts no more for it");
        }

        // ---------------------------------------------------------------- expiry

        [Test]
        public void AnOfferNobodyTakesExpires()
        {
            var w = CreateWorld();
            var mars = Port(w, "Ares Prime");
            Starve(mars, Good.Food);
            w.Contracts.Post(StartJd);
            var c = FreightTo(w, mars, Good.Food);
            w.Contracts.Review(StartJd + 1);
            Assert.That(c.State, Is.EqualTo(ContractState.Open), "still needed, still within its time");
            w.Contracts.Review(c.ExpiresJd + 0.01);
            Assert.That(c.State, Is.EqualTo(ContractState.Expired));
            Assert.That(w.Contracts.BoardAt(c.Board), Does.Not.Contain(c));
        }

        [Test]
        public void AnOfferIsTakenDownWhenTheShortageIsFilled()
        {
            var w = CreateWorld();
            var mars = Port(w, "Ares Prime");
            Starve(mars, Good.Food);
            w.Contracts.Post(StartJd);
            var c = FreightTo(w, mars, Good.Food);
            mars.Stock[(int)Good.Food] = mars.Target(Good.Food);
            w.Contracts.Review(StartJd + 1);
            Assert.That(c.State, Is.EqualTo(ContractState.Expired), "someone else filled it: the need is gone");
        }

        [Test]
        public void ATakenContractPastItsDeadlineFailsAndCostsStanding()
        {
            var w = CreateWorld();
            var mars = Port(w, "Ares Prime");
            Starve(mars, Good.Food);
            w.Contracts.Post(StartJd);
            var c = FreightTo(w, mars, Good.Food);
            var ship = Pilot(w, c.Board.Body, StartJd);
            Assert.That(w.Contracts.Take(c, ship, true, StartJd), Is.EqualTo(Contracts.TakeOutcome.Ok));
            double before = w.Law.Player.StandingWith(c.Patron);
            w.Contracts.Review(c.DueJd + 0.5);
            Assert.That(c.State, Is.EqualTo(ContractState.Failed));
            Assert.That(w.Law.Player.StandingWith(c.Patron), Is.EqualTo(before - Contracts.FailStanding).Within(1e-9));
        }

        // ---------------------------------------------------------------- pay

        [Test]
        public void PayRisesWithDistanceDangerAndUrgency()
        {
            double base0 = Contracts.Price(100000, 50000, Astro.AuKm, 0, 0);
            Assert.That(Contracts.Price(100000, 50000, 3 * Astro.AuKm, 0, 0), Is.GreaterThan(base0), "further");
            Assert.That(Contracts.Price(100000, 50000, Astro.AuKm, 0.8, 0), Is.GreaterThan(base0), "more dangerous");
            Assert.That(Contracts.Price(100000, 50000, Astro.AuKm, 0, 0.8), Is.GreaterThan(base0), "more urgent");

            // And on the boards: of two posted lots of one good, the one for the more urgent need pays more a tonne.
            var w = CreateWorld();
            var mars = Port(w, "Ares Prime");
            Starve(mars, Good.Food);
            w.Contracts.Post(StartJd);
            var urgent = w.Contracts.All.First(c => c.Kind == ContractKind.Freight && c.Destination == mars && c.Good == Good.Food && c.Tonnes == CargoFootprint.Of(Good.Food).Tonnes * Contracts.ParcelCrates);
            var ganymede = Port(w, "Ganymede Union");
            ganymede.Stock[(int)Good.Food] = ganymede.Target(Good.Food) * 0.4;
            ganymede.Inbound[(int)Good.Food] = 0;
            w.Contracts.Post(StartJd);
            var mild = FreightTo(w, ganymede, Good.Food);
            if (mild != null && mild.Tonnes == urgent.Tonnes && mild.DistanceKm <= urgent.DistanceKm)
                Assert.That(urgent.Pay, Is.GreaterThan(mild.Pay));
            Assert.That(urgent.DeadlineDays, Is.GreaterThan(0));
        }

        // ---------------------------------------------------------------- the player

        [Test]
        public void ThePlayerTakesFreightAndIsPaidWhenTheLotIsHandedOver()
        {
            var w = CreateWorld();
            var mars = Port(w, "Ares Prime");
            Starve(mars, Good.Food);
            w.Contracts.Post(StartJd);
            var c = w.Contracts.All.First(o => o.Kind == ContractKind.Freight && o.IsOpen && o.Destination == mars && o.Good == Good.Food && o.Tonnes <= 200);
            var ship = Pilot(w, Body(w, "Mars"), StartJd);
            Assert.That(w.Contracts.Take(c, ship, true, StartJd), Is.EqualTo(Contracts.TakeOutcome.NotHere), "jobs are signed at the board's port");
            w.Contracts.UpdatePilot(ship, c.Board.Body, DVec3.Zero, StartJd);
            Assert.That(w.Contracts.Take(c, ship, true, StartJd), Is.EqualTo(Contracts.TakeOutcome.Ok));
            Assert.That(w.Contracts.PlayerActive(), Does.Contain(c));
            for (int i = 0; i < Contracts.ParcelCrates; i++) Assert.That(ship.PersonalHold.AddFirstFit(Good.Food), Is.Not.Null);

            double purse = ship.Faction.Treasury, patron = c.Patron.Treasury, stock = mars.Stock[(int)Good.Food];
            double standing = w.Law.Player.StandingWith(c.Patron);
            w.Contracts.UpdatePilot(ship, mars.Body, DVec3.Zero, StartJd + 3);
            Assert.That(c.State, Is.EqualTo(ContractState.Completed));
            Assert.That(ship.PersonalHold.Items.Count(it => it.Good == Good.Food), Is.Zero, "the crates went ashore");
            Assert.That(mars.Stock[(int)Good.Food], Is.EqualTo(stock + c.Tonnes).Within(1e-6), "into the short market");
            Assert.That(ship.Faction.Treasury, Is.EqualTo(purse + c.Pay).Within(1e-3));
            Assert.That(c.Patron.Treasury, Is.EqualTo(patron - c.Pay).Within(1e-3));
            Assert.That(w.Law.Player.StandingWith(c.Patron), Is.GreaterThan(standing));
        }

        [Test]
        public void AbandoningContractsCostsStandingUntilThePatronOffersNoMore()
        {
            var w = CreateWorld();
            var mars = Port(w, "Ares Prime");
            Starve(mars, Good.Food);
            w.Contracts.Post(StartJd);
            var first = FreightTo(w, mars, Good.Food);
            var ship = Pilot(w, first.Board.Body, StartJd);
            double before = w.Law.Player.StandingWith(first.Patron);
            Assert.That(w.Contracts.Take(first, ship, true, StartJd), Is.EqualTo(Contracts.TakeOutcome.Ok));
            Assert.That(w.Contracts.Abandon(first, StartJd), Is.True);
            Assert.That(first.State, Is.EqualTo(ContractState.Abandoned));
            Assert.That(w.Law.Player.StandingWith(first.Patron), Is.EqualTo(before - Contracts.AbandonStanding).Within(1e-9));

            w.Law.Player.Standing[first.Patron] = Law.ContractMinStanding - 1;
            var next = w.Contracts.All.First(o => o.IsOpen && o.Patron == first.Patron);
            Assert.That(w.Contracts.Take(next, ship, true, StartJd), Is.EqualTo(Contracts.TakeOutcome.NotOffered));
        }

        // ---------------------------------------------------------------- NPC captains

        [Test]
        public void AMerchantTakesFreightBuysTheLotAndDeliversItToTheShortMarket()
        {
            var w = CreateWorld();
            var mars = Port(w, "Ares Prime");
            Starve(mars, Good.Food);
            w.Contracts.Post(StartJd);
            var c = w.Contracts.All.Where(o => o.Kind == ContractKind.Freight && o.IsOpen && o.Destination == mars && o.Good == Good.Food).OrderByDescending(o => o.Tonnes).First();
            Only(w, c);
            var ship = Merchant(w, c.Board.Body);
            double credits = ship.Credits;
            int next = TakeByHook(w, ship, c, c.Board.Body);
            Assert.That(next, Is.EqualTo(mars.Body), "it sails for the short market");
            Assert.That(c.State, Is.EqualTo(ContractState.Taken));
            Assert.That(c.Taker, Is.SameAs(ship));
            Assert.That(ship.HasCargo && ship.CargoGood == Good.Food && ship.CargoDestination == mars, Is.True, "loaded and booked for Mars");
            Assert.That(ship.CargoTonnes, Is.EqualTo(c.Tonnes));
            Assert.That(w.Contracts.ChooseNext(ship, c.Board.Body, StartJd), Is.EqualTo(mars.Body), "and keeps to it");

            // Put in somewhere else on the way: the market there does not get the lot.
            Assert.That(w.Contracts.OnArrived(ship, Body(w, "Ceres"), StartJd + 2), Is.True);
            Assert.That(ship.HasCargo, Is.True);

            double stock = mars.Stock[(int)Good.Food];
            double cost = ship.CargoCost;
            Assert.That(cost, Is.GreaterThan(0));
            Assert.That(w.Contracts.OnArrived(ship, mars.Body, StartJd + 4), Is.True);
            Assert.That(c.State, Is.EqualTo(ContractState.Completed));
            Assert.That(ship.HasCargo, Is.False);
            Assert.That(mars.Stock[(int)Good.Food], Is.EqualTo(stock + c.Tonnes).Within(1e-6));
            Assert.That(ship.Credits, Is.EqualTo(credits + c.Pay - cost).Within(1e-3), "its earnings are the pay less what the lot cost");
        }

        [Test]
        public void AMerchantWhoseContractCargoWentElsewhereFailsItAndLosesStanding()
        {
            var w = CreateWorld();
            var mars = Port(w, "Ares Prime");
            Starve(mars, Good.Food);
            w.Contracts.Post(StartJd);
            var c = w.Contracts.All.Where(o => o.Kind == ContractKind.Freight && o.IsOpen && o.Destination == mars).OrderByDescending(o => o.Tonnes).First();
            Only(w, c);
            var ship = Merchant(w, c.Board.Body);
            Assert.That(TakeByHook(w, ship, c, c.Board.Body), Is.GreaterThanOrEqualTo(0));
            ship.HasCargo = false;
            ship.CargoDestination = null;
            w.Contracts.Review(StartJd + 1);
            Assert.That(c.State, Is.EqualTo(ContractState.Failed));
            Assert.That(w.Law.FindRecord(ship).StandingWith(c.Patron), Is.EqualTo(-Contracts.FailStanding).Within(1e-9));
        }

        [Test]
        public void PeopleLeavingASettlementPostPassageAndColonistsActuallyMove()
        {
            var w = CreateWorld();
            var from = Port(w, "Ganymede Union");
            var to = Port(w, "Ares Prime");
            foreach (var s in w.Economy.Settlements) s.NetMigration = 0;
            from.NetMigration = -60;
            to.NetMigration = 45;
            w.Contracts.Post(StartJd);
            var passengers = w.Contracts.All.FirstOrDefault(c => c.Kind == ContractKind.Passengers && c.Board == from);
            var colonists = w.Contracts.All.FirstOrDefault(c => c.Kind == ContractKind.Colonists && c.Board == from);
            Assert.That(passengers, Is.Not.Null, "people leaving want passage");
            Assert.That(colonists, Is.Not.Null, "a settlement emptying fast sends a colony party");
            Assert.That(passengers.Destination, Is.SameAs(to), "to where people are going");
            Assert.That(colonists.Destination, Is.SameAs(to));
            Assert.That(colonists.Patron, Is.SameAs(to.Owner), "the settlement they go to pays to bring them in");
            Assert.That(w.Contracts.All.Any(c => (c.Kind == ContractKind.Passengers || c.Kind == ContractKind.Colonists) && c.Board.NetMigration >= 0), Is.False,
                "nobody posts passage away from a place people are not leaving");

            double popFrom = from.Population, popTo = to.Population;
            Only(w, colonists);
            var ship = Merchant(w, from.Body);
            Assert.That(TakeByHook(w, ship, colonists, from.Body), Is.EqualTo(to.Body));
            Assert.That(from.Population, Is.EqualTo(popFrom - colonists.People).Within(1e-6), "they board");
            w.Contracts.OnArrived(ship, to.Body, StartJd + 5);
            Assert.That(colonists.State, Is.EqualTo(ContractState.Completed));
            Assert.That(to.Population, Is.EqualTo(popTo + colonists.People).Within(1e-6), "and land");
        }

        [Test]
        public void AFreeShipSurveysABodyNobodyHasBeenTo()
        {
            var w = CreateWorld();
            w.Contracts.Post(StartJd);
            var survey = w.Contracts.All.First(c => c.Kind == ContractKind.Survey && c.IsOpen);
            Assert.That(w.Contracts.Visited.ContainsKey(survey.Body), Is.False, "only unvisited bodies");
            Assert.That(w.Economy.At(survey.Body), Is.Empty, "and unsettled ones");
            var others = w.Contracts.All.Where(o => o != survey && o.IsOpen).ToList();
            Only(w, survey);
            var yacht = Merchant(w, survey.Board.Body, ShipClass.Courier, ShipRole.Free);
            Assert.That(TakeByHook(w, yacht, survey, survey.Board.Body), Is.EqualTo(survey.Body));
            w.Contracts.OnArrived(yacht, survey.Body, StartJd + 3);
            Assert.That(survey.State, Is.EqualTo(ContractState.Completed));
            Assert.That(w.Contracts.Visited.ContainsKey(survey.Body), Is.True);

            // A survey offer for a body someone else has since been to comes down.
            var other = others.FirstOrDefault(c => c.Kind == ContractKind.Survey);
            if (other != null) w.Contracts.All.Add(other);
            if (other != null)
            {
                w.Contracts.OnArrived(yacht, other.Body, StartJd + 1);
                w.Contracts.Review(StartJd + 2);
                Assert.That(other.State, Is.EqualTo(ContractState.Expired));
            }
        }

        [Test]
        public void FightingAtAPortPostsAPatrolThatAHunterFlies()
        {
            var w = CreateWorld();
            var ceres = Port(w, "Ceres Hub");
            var danger = (Dictionary<int, double>)typeof(War).GetField("danger", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(w.War);
            danger.Clear();
            w.Contracts.Post(StartJd);
            Assert.That(w.Contracts.All.Any(c => c.Kind == ContractKind.Patrol), Is.False, "no trouble, no patrols");
            danger[ceres.Body] = 4;
            w.Contracts.Post(StartJd);
            var patrol = w.Contracts.All.First(c => c.Kind == ContractKind.Patrol && c.Body == ceres.Body);
            Assert.That(patrol.Board, Is.SameAs(ceres));
            Assert.That(patrol.Danger, Is.GreaterThanOrEqualTo(Contracts.PatrolDanger));

            var hunter = w.Outlaws.Commission(Port(w, "Earth Orbital"), StartJd);
            w.Contracts.All.RemoveAll(c => c != patrol && c.IsOpen);
            Assert.That(w.Contracts.ChooseForHunter(hunter.Ship, Body(w, "Earth"), StartJd), Is.EqualTo(ceres.Body));
            Assert.That(patrol.Taker, Is.SameAs(hunter.Ship));
            w.Fleet.Station(hunter.Ship, ceres.Body, StartJd);
            w.Contracts.Review(StartJd + 1);
            Assert.That(patrol.State, Is.EqualTo(ContractState.Completed), "a day on station");
        }

        [Test]
        public void ABountyOnTheLawsBooksIsOnThePostersBoard()
        {
            var w = CreateWorld();
            var raider = w.Fleet.Ships.First(s => s.Faction.IsPirate && !s.IsEscort && !s.Destroyed);
            var rec = w.Law.RecordOf(raider);
            rec.Bounty[w.Sol] = 400000;
            rec.BountyUntil[w.Sol] = StartJd + 100;
            w.Contracts.Post(StartJd);
            var c = w.Contracts.All.First(o => o.Kind == ContractKind.Bounty && o.Target == rec);
            Assert.That(c.Patron, Is.SameAs(w.Sol));
            Assert.That(c.Board.Owner, Is.SameAs(w.Sol));
            Assert.That(c.Pay, Is.EqualTo(400000));
            rec.Bounty.Clear();
            w.Contracts.Review(StartJd + 1);
            Assert.That(c.State, Is.EqualTo(ContractState.Expired), "no longer wanted: the contract comes down");
        }

        /// <summary>U.SPC-T317: a captured hull flips to its captor's <see cref="BattleUnit.Side"/>, so once the
        /// player takes a bounty target alive, the target and the player end the battle on the same <c>Side</c>;
        /// the completion check has to read <see cref="BattleUnit.FoughtOn"/> (the side it fought on, unchanged by
        /// capture) instead, or it never sees the player as the one who took it down.</summary>
        [Test]
        public void ABountyOnACaptainTakenAliveCompletesWhenThePlayerCapturesThem()
        {
            var w = CreateWorld();
            int earth = Body(w, "Earth");
            var raider = w.Fleet.Ships.First(s => s.Faction.IsPirate && !s.IsEscort && !s.Destroyed);
            var rec = w.Law.RecordOf(raider);
            var player = Pilot(w, earth, StartJd);

            var c = new Contract
            {
                Kind = ContractKind.Bounty,
                State = ContractState.Taken,
                ByPlayer = true,
                Target = rec,
                Taker = player,
                Patron = w.Sol,
                Pay = 400000,
            };
            w.Contracts.All.Add(c);

            var b = new Battle(0, earth, "Battle of Earth", new DVec3(20000, 0, 0), StartJd, 1);
            b.AddShip(player, 0, new DVec3(4, 0, 0), -DVec3.UnitX).PlayerControlled = true;
            var prize = b.AddShip(raider, 1, new DVec3(-4, 0, 0), DVec3.UnitX);
            // Boarded and taken alive: the hull flips to the captor's (the player's) side, but it still fought on side 1.
            prize.Captured = true;
            prize.TakenFromSide = 1;
            prize.Side = 0;

            // The same order World wires war.BattleEnded in: the law and the Outlaws books settle first (taking the
            // captain off "at large"), then the contracts board.
            w.Law.OnBattleEnded(b, StartJd + 1);
            w.Outlaws.OnBattleEnded(b, StartJd + 1);
            w.Contracts.OnBattleEnded(b, StartJd + 1);
            Assert.That(c.State, Is.EqualTo(ContractState.Completed), "the player took the bounty target alive");
        }

        [Test]
        public void OverAMonthTheBoardsFillFromRealNeedsAndCaptainsWorkThem()
        {
            var w = CreateWorld();
            double jd = StartJd;
            for (int i = 0; i < 30 * 4; i++)
            {
                jd += 0.25;
                w.Ephemeris.Update(jd);
                w.Update(jd);
            }
            var all = w.Contracts.All;
            Assert.That(all, Is.Not.Empty, "something is always needed somewhere");
            foreach (var c in all.Where(c => c.Kind == ContractKind.Freight))
                Assert.That(c.CoverAtPost, Is.LessThan(Contracts.ShortCover), c.Title);
            Assert.That(all.Any(c => c.TakenJd > 0 && !c.ByPlayer), Is.True, "NPC captains take contracts");
            Assert.That(all.All(c => !c.IsOpen || c.ExpiresJd > jd - 1), Is.True, "no open offer outlives its time");
        }
    }
}
