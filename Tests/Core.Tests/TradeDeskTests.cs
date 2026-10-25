using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// U.SPC-T426: the trading game. Beside buying and selling at the helm, the contracts boards and the house's own fleet
    /// and property, the player's trade desk keeps price intelligence that ages, ranks the runs it adds up to, sells
    /// broker's price sheets, charters the house's hulls out to the trading companies, insures hulls and warehouses
    /// against what the risk at their ports says, opens every lawful port's back door to contraband and blockade runs,
    /// and ranks the trader by what the house is worth.
    /// </summary>
    [TestFixture]
    public class TradeDeskTests
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

        static Settlement Port(World w, string name) => w.Economy.Settlements.First(s => s.Name == name);

        static Holdings Funded(World w, double credits = 5e8)
        {
            var h = w.Holdings;
            Assert.That(h.Deposit(w.Sol, credits, StartJd), Is.True);
            return h;
        }

        /// <summary>A crewed freighter of the house's at Earth Orbital.</summary>
        static OwnedShip Freighter(World w, Holdings h)
        {
            var owned = h.Buy(Port(w, "Earth Orbital"), ShipClass.Freighter, "Plenty", StartJd);
            Assert.That(owned, Is.Not.Null);
            h.Hire(owned, StartJd);
            return owned;
        }

        // ---------------------------------------------------------------- wiring

        [Test]
        public void EveryWorldHasATradeDeskAndItsTuningIsInTheSettings()
        {
            var w = CreateWorld();
            Assert.That(w.Trade, Is.Not.Null);
            Assert.That(w.Trade.Holdings, Is.SameAs(w.Holdings));
            Assert.That(TradeRules.CharterHire.Group, Is.EqualTo(SettingGroups.Economy));
            Assert.That(TradeRules.CustomsChance.Value, Is.InRange(0.0, 1.0));
        }

        // ---------------------------------------------------------------- risk

        [Test]
        public void APortsRiskWeighsRaidersBlockadesEmbargoesAndCollapse()
        {
            var w = CreateWorld();
            var desk = w.Trade;
            var earth = Port(w, "Earth Orbital");
            Assert.That(desk.Assess(earth).Closed, Is.False);

            Assert.That(w.Diplomacy.DeclareBlockade(earth, w.Reach), Is.True);
            var blockaded = desk.Assess(earth);
            Assert.That(blockaded.Closed, Is.True);
            Assert.That(blockaded.BlockadedBy, Is.SameAs(w.Reach));
            Assert.That(blockaded.Total, Is.GreaterThanOrEqualTo(0.5));
            Assert.That(blockaded.Summary, Does.Contain("blockaded"));
            w.Diplomacy.LiftBlockade(earth);

            w.Diplomacy.DeclareEmbargo(earth.Owner, desk.Flag, StartJd, StartJd + 30, "test");
            Assert.That(desk.Assess(earth).Embargoed, Is.True);

            var ceres = Port(w, "Ceres Hub");
            double calm = TradeDesk.CollapseOf(ceres);
            ceres.Unrest = 1;
            ceres.Population = ceres.InitialPopulation * 0.3;
            Assert.That(TradeDesk.CollapseOf(ceres), Is.GreaterThan(calm));
            ceres.Fallen = true;
            Assert.That(TradeDesk.CollapseOf(ceres), Is.EqualTo(1));
        }

        // ---------------------------------------------------------------- intelligence

        [Test]
        public void APriceSheetIsASnapshotThatAgesAndIsTrustedLessAsItDoes()
        {
            var w = CreateWorld();
            var intel = w.Trade.Intel;
            var earth = Port(w, "Earth Orbital");
            var sheet = intel.Observe(earth, StartJd, "seen in port");
            Assert.That(sheet, Is.Not.Null);
            double seen = sheet.Price[(int)Good.Food];
            earth.Price[(int)Good.Food] = seen * 3;
            Assert.That(intel.Of(earth).Price[(int)Good.Food], Is.EqualTo(seen), "what was seen, not what is true now");

            Assert.That(intel.Confidence(sheet, StartJd), Is.EqualTo(1).Within(1e-9));
            Assert.That(intel.Confidence(sheet, StartJd + TradeRules.IntelHalfLife.Value), Is.EqualTo(0.5).Within(1e-9));

            // An older report never overwrites a newer one.
            intel.Observe(earth, StartJd - 5, "old news");
            Assert.That(intel.Of(earth).Source, Is.EqualTo("seen in port"));
        }

        [Test]
        public void TheHousesCaptainsInPortReportTheirMarketsEveryDay()
        {
            var w = CreateWorld();
            var h = Funded(w);
            var owned = Freighter(w, h);
            var earth = Port(w, "Earth Orbital");
            Assert.That(w.Trade.Intel.Of(earth), Is.Null);
            w.Trade.Update(StartJd);
            w.Trade.Update(StartJd + 1.01);
            var sheet = w.Trade.Intel.Of(earth);
            Assert.That(sheet, Is.Not.Null);
            Assert.That(sheet.Source, Does.Contain(owned.Captain.Name));
        }

        [Test]
        public void TheDeskRanksRunsFromWhatItsPricesSay()
        {
            var w = CreateWorld();
            var intel = w.Trade.Intel;
            var earth = Port(w, "Earth Orbital");
            var ceres = Port(w, "Ceres Hub");
            var a = intel.Observe(earth, StartJd, "test");
            var b = intel.Observe(ceres, StartJd, "test");
            int g = (int)Good.Electronics;
            for (int i = 0; i < GoodInfo.Count; i++)
            {
                a.Available[i] = b.Available[i] = 0;
                b.Buys[i] = a.Buys[i] = false;
            }
            a.Price[g] = 100;
            a.Available[g] = 5000;
            b.Price[g] = 400;
            b.Buys[g] = true;
            b.Wants[g] = 800;

            var leads = intel.Leads(2000, StartJd);
            Assert.That(leads, Has.Count.EqualTo(1));
            var lead = leads[0];
            Assert.That(lead.From, Is.SameAs(earth));
            Assert.That(lead.To, Is.SameAs(ceres));
            Assert.That(lead.Good, Is.EqualTo(Good.Electronics));
            Assert.That(lead.Tonnes, Is.EqualTo(800), "no more than the far end wants");
            Assert.That(lead.Margin, Is.GreaterThan(0).And.LessThan(300), "less tax and the price walking against the trade");

            // The same run on stale prices scores lower.
            var later = intel.Leads(2000, StartJd + 3 * TradeRules.IntelHalfLife.Value);
            Assert.That(later[0].Score, Is.LessThan(lead.Score));

            // A run to a blockaded port is the back door's.
            w.Diplomacy.DeclareBlockade(ceres, w.Reach);
            var run = intel.Leads(2000, StartJd);
            Assert.That(run[0].BackDoor, Is.True);
            Assert.That(run[0].Score, Is.LessThan(lead.Score));
        }

        [Test]
        public void RunsAreSizedToTheFreeCratesOfTheHoldThePlayerFlies()
        {
            var w = CreateWorld();
            var intel = w.Trade.Intel;
            var a = intel.Observe(Port(w, "Earth Orbital"), StartJd, "test");
            var b = intel.Observe(Port(w, "Ceres Hub"), StartJd, "test");
            int g = (int)Good.Food;
            for (int i = 0; i < GoodInfo.Count; i++) { a.Available[i] = b.Available[i] = 0; a.Buys[i] = b.Buys[i] = false; }
            a.Price[g] = 100; a.Available[g] = 50000;
            b.Price[g] = 400; b.Buys[g] = true; b.Wants[g] = 50000;

            var hold = new CargoHold(6, 8);
            var lead = intel.Leads(20000, StartJd, 12, null, hold)[0];
            Assert.That(lead.Tonnes, Is.EqualTo(12 * 20), "12 food crates of 2x2 fit a 6x8 hold, not the freighter's 20,000 t");

            hold.AddFirstFit(Good.Food);
            lead = intel.Leads(20000, StartJd, 12, null, hold)[0];
            Assert.That(lead.Tonnes, Is.EqualTo(11 * 20), "a crate already aboard takes its cells");
            Assert.That(hold.Items, Has.Count.EqualTo(1), "sizing leaves the hold untouched");
        }

        [Test]
        public void ABrokerSellsAPriceSheetOfEveryKnownMarket()
        {
            var w = CreateWorld();
            var h = Funded(w);
            var intel = w.Trade.Intel;
            var earth = Port(w, "Earth Orbital");
            Assert.That(intel.HasBroker(earth), Is.True);
            double before = h.Balance, ownerBefore = earth.Owner.Treasury, fee = intel.ReportFee;
            int n = intel.BuyReport(earth, h.House, StartJd);
            Assert.That(n, Is.EqualTo(w.Economy.Settlements.Count(s => s.Discovered)));
            Assert.That(intel.Count, Is.EqualTo(n));
            Assert.That(h.Balance, Is.EqualTo(before - fee).Within(1e-6));
            Assert.That(earth.Owner.Treasury, Is.EqualTo(ownerBefore + fee).Within(1e-6));

            var haven = w.Economy.Settlements.First(s => s.Kind == SettlementKind.Haven);
            Assert.That(intel.HasBroker(haven), Is.False);
            Assert.That(intel.BuyReport(haven, h.House, StartJd), Is.EqualTo(-1));
        }

        // ---------------------------------------------------------------- charters

        static CharterOffer Offer(World w, Company c, ShipClass cls, double days = 40)
        {
            var offer = new CharterOffer
            {
                Id = 99, Charterer = c, Class = cls, RatePerDay = w.Trade.Charters.RateFor(c, cls), Days = days,
                PostedJd = StartJd, ExpiresJd = StartJd + 15, Port = c.HomePort,
            };
            w.Trade.Charters.Offers.Add(offer);
            return offer;
        }

        [Test]
        public void CompaniesPostChartersAtAHireThatCoversAHullsRunningCost()
        {
            var w = CreateWorld();
            var charters = w.Trade.Charters;
            for (int i = 0; i < 20 && charters.Offers.Count == 0; i++) charters.Post(StartJd + i);
            Assert.That(charters.Offers, Is.Not.Empty);
            foreach (var o in charters.Offers)
            {
                Assert.That(Charters.Classes, Does.Contain(o.Class));
                Assert.That(o.RatePerDay, Is.GreaterThanOrEqualTo(Economy.RunningCostPerDay(o.Class)));
                Assert.That(o.Days, Is.InRange(Charters.MinDays, Charters.MaxDays));
                Assert.That(o.Charterer.Trading, Is.True);
            }
        }

        [Test]
        public void ACharteredHullTradesForItsChartererWhoPaysTheHouseItsHireThenItGoesBackToItsOrders()
        {
            var w = CreateWorld();
            var h = Funded(w);
            var owned = Freighter(w, h);
            var route = StandingOrder.TradeRoute(owned.Ship.Orbit.Body, Port(w, "Ares Prime").Body);
            Assert.That(h.Order(owned, route), Is.True);
            var company = w.Companies.Live.First();
            company.Cash = 1e9;
            var offer = Offer(w, company, ShipClass.Freighter, 3);
            var charters = w.Trade.Charters;

            Assert.That(charters.CanTake(offer, owned, StartJd), Is.EqualTo(Charters.TakeOutcome.Ok));
            Assert.That(charters.Candidate(offer, StartJd), Is.SameAs(owned));
            var c = charters.Take(offer, owned, StartJd);
            Assert.That(c, Is.Not.Null);
            Assert.That(owned.OnCharter, Is.True);
            Assert.That(owned.Orders.Kind, Is.EqualTo(StandingOrderKind.FreeTrade));
            Assert.That(charters.Offers, Does.Not.Contain(offer));
            Assert.That(h.Order(owned, StandingOrder.Hold()), Is.False, "its orders are the charterer's while it runs");

            double before = h.Balance, cashBefore = company.Cash;
            charters.Daily(StartJd + 1);
            Assert.That(h.Balance, Is.EqualTo(before + c.RatePerDay).Within(1e-6));
            Assert.That(company.Cash, Is.EqualTo(cashBefore - c.RatePerDay).Within(1e-6));
            charters.Daily(StartJd + 2);
            charters.Daily(StartJd + 3);
            Assert.That(c.Active, Is.False);
            Assert.That(c.Served, Is.True);
            Assert.That(c.HirePaid, Is.EqualTo(3 * c.RatePerDay).Within(1e-6));
            Assert.That(owned.OnCharter, Is.False);
            Assert.That(owned.Orders, Is.SameAs(route), "back to its old orders");
            Assert.That(w.Trade.ChartersServed, Is.EqualTo(1));
        }

        [Test]
        public void ACharterTakenBetweenTicksPaysHireForItsTermAndNoMore()
        {
            var w = CreateWorld();
            var h = Funded(w);
            var owned = Freighter(w, h);
            var company = w.Companies.Live.First();
            company.Cash = 1e9;
            var charters = w.Trade.Charters;
            var c = charters.Take(Offer(w, company, ShipClass.Freighter, 3), owned, StartJd + 0.4);
            Assert.That(c, Is.Not.Null);
            for (int d = 1; d <= 4 && c.Active; d++) charters.Daily(StartJd + d);
            Assert.That(c.Active, Is.False);
            Assert.That(c.Served, Is.True);
            Assert.That(c.HirePaid, Is.EqualTo(3 * c.RatePerDay).Within(1e-6));
        }

        [Test]
        public void AHullOnCharterHandsItsTradeResultToTheCharterer()
        {
            var w = CreateWorld();
            var h = Funded(w);
            var owned = Freighter(w, h);
            var company = w.Companies.Live.First();
            company.Cash = 1e9;
            var c = w.Trade.Charters.Take(Offer(w, company, ShipClass.Freighter), owned, StartJd);
            Assert.That(c, Is.Not.Null);
            h.Update(StartJd + 1);
            double before = h.Balance, cashBefore = company.Cash;
            // A run sold at a profit: the ship's running credits rise, and its faction (the house) is paid.
            owned.Ship.Credits += 50000;
            h.House.Treasury += 50000;
            h.Update(StartJd + 2.01);
            // (Less the day's wages, which the house still pays.)
            Assert.That(h.Balance, Is.LessThanOrEqualTo(before + 1), "the takings are the charterer's");
            Assert.That(company.Cash, Is.EqualTo(cashBefore + 50000).Within(1));
            Assert.That(c.TradeResult, Is.EqualTo(50000).Within(1));
        }

        [Test]
        public void TheWrongClassACaptainlessHullOrAHullAlreadyOutCannotBeChartered()
        {
            var w = CreateWorld();
            var h = Funded(w);
            var owned = Freighter(w, h);
            var company = w.Companies.Live.First();
            company.Cash = 1e9;
            var charters = w.Trade.Charters;
            Assert.That(charters.CanTake(Offer(w, company, ShipClass.Courier), owned, StartJd), Is.EqualTo(Charters.TakeOutcome.WrongClass));
            var bare = h.Buy(Port(w, "Earth Orbital"), ShipClass.Freighter, "Bare", StartJd);
            var offer = Offer(w, company, ShipClass.Freighter);
            Assert.That(charters.CanTake(offer, bare, StartJd), Is.EqualTo(Charters.TakeOutcome.NoCaptain));
            Assert.That(charters.Take(offer, owned, StartJd), Is.Not.Null);
            Assert.That(charters.CanTake(Offer(w, company, ShipClass.Freighter), owned, StartJd), Is.EqualTo(Charters.TakeOutcome.Chartered));
            Assert.That(charters.CanTake(offer, bare, StartJd), Is.EqualTo(Charters.TakeOutcome.Gone), "taken");
        }

        [Test]
        public void RecallingAHullEarlyPaysTheChartererAPenalty()
        {
            var w = CreateWorld();
            var h = Funded(w);
            var owned = Freighter(w, h);
            var company = w.Companies.Live.First();
            company.Cash = 1e9;
            var c = w.Trade.Charters.Take(Offer(w, company, ShipClass.Freighter), owned, StartJd);
            double before = h.Balance, cashBefore = company.Cash;
            Assert.That(w.Trade.Charters.Recall(c, StartJd + 1), Is.True);
            double penalty = c.RatePerDay * Charters.RecallPenaltyDays;
            Assert.That(h.Balance, Is.EqualTo(before - penalty).Within(1e-6));
            Assert.That(company.Cash, Is.EqualTo(cashBefore + penalty).Within(1e-6));
            Assert.That(owned.OnCharter, Is.False);
            Assert.That(c.EndedHow, Is.EqualTo("recalled"));
            Assert.That(h.Order(owned, StandingOrder.Hold()), Is.True);
        }

        [Test]
        public void AChartererThatCannotPayEndsTheCharter()
        {
            var w = CreateWorld();
            var h = Funded(w);
            var owned = Freighter(w, h);
            var company = w.Companies.Live.First();
            company.Cash = 1e9;
            var c = w.Trade.Charters.Take(Offer(w, company, ShipClass.Freighter), owned, StartJd);
            company.Cash = 0;
            w.Trade.Charters.Daily(StartJd + 1);
            Assert.That(c.Active, Is.False);
            Assert.That(c.Served, Is.False);
            Assert.That(c.EndedHow, Does.Contain("defaulted"));
        }

        // ---------------------------------------------------------------- insurance

        [Test]
        public void AnInsuredHullCostsAPremiumADayAndIsPaidOutWhenItIsLost()
        {
            var w = CreateWorld();
            var h = Funded(w);
            var owned = Freighter(w, h);
            var ins = w.Trade.Insurance;
            var (cover, premium, _) = ins.Quote(owned);
            Assert.That(cover, Is.EqualTo(TradeInsurance.ValueOf(owned) * TradeRules.InsuranceCover.Value).Within(1e-6));
            Assert.That(premium, Is.GreaterThan(0));
            var policy = ins.Insure(owned, StartJd);
            Assert.That(policy, Is.Not.Null);
            Assert.That(ins.Insure(owned, StartJd), Is.Null, "covered once");

            double before = h.Balance;
            ins.Daily(StartJd + 1);
            Assert.That(h.Balance, Is.LessThan(before));
            Assert.That(policy.PremiumsPaid, Is.GreaterThan(0));

            h.Update(StartJd + 1);
            double value = Economy.HullPrice(ShipClass.Freighter);
            owned.Ship.Destroyed = true;
            double beforeLoss = h.Balance;
            h.Update(StartJd + 2.01);
            Assert.That(owned.Lost, Is.True);
            Assert.That(policy.Active, Is.False);
            Assert.That(policy.EndedHow, Is.EqualTo("claim paid"));
            Assert.That(policy.Paid, Is.EqualTo(value * TradeRules.InsuranceCover.Value).Within(value * 0.01));
            Assert.That(h.Balance, Is.GreaterThan(beforeLoss + 0.5 * policy.Paid));
        }

        [Test]
        public void PremiumsClimbWithTheRiskAndPastAPointNobodyWillWriteAPolicy()
        {
            var w = CreateWorld();
            var h = Funded(w);
            var ins = w.Trade.Insurance;
            Assert.That(ins.Premium(1e6, 0.8), Is.GreaterThan(ins.Premium(1e6, 0)));
            var earth = Port(w, "Earth Orbital");
            var warehouse = h.BuyWarehouse(earth, StartJd);
            Assert.That(warehouse, Is.Not.Null);
            double safe = ins.Quote(warehouse).premium;
            earth.Fallen = false;
            earth.Unrest = 1;
            w.Diplomacy.DeclareBlockade(earth, w.Reach);
            var (_, risky, risk) = ins.Quote(warehouse);
            Assert.That(risky, Is.GreaterThan(safe));
            earth.Population = 0.01 * earth.InitialPopulation;
            if (ins.Exposure(warehouse) >= TradeInsurance.Uninsurable) Assert.That(ins.Insure(warehouse, StartJd), Is.Null);
            else Assert.That(ins.Insure(warehouse, StartJd), Is.Not.Null);
        }

        [Test]
        public void AnInsuredWarehouseIsPaidOutWhenItIsSeized()
        {
            var w = CreateWorld();
            var h = Funded(w);
            var ceres = Port(w, "Ceres Hub");
            var warehouse = h.BuyWarehouse(ceres, StartJd);
            var policy = w.Trade.Insurance.Insure(warehouse, StartJd);
            Assert.That(policy, Is.Not.Null);
            double before = h.Balance;
            // Its port falls to raiders.
            ceres.Owner = w.Pirates;
            h.Update(StartJd + 1.01);
            Assert.That(warehouse.Lost, Is.True);
            Assert.That(policy.EndedHow, Is.EqualTo("claim paid"));
            Assert.That(h.Balance, Is.GreaterThan(before));
        }

        // ---------------------------------------------------------------- smuggling

        [Test]
        public void ABlockadedPortsBackDoorPaysAPremiumAndTheGoodsReliefTheTown()
        {
            var w = CreateWorld();
            var ship = w.Fleet.Ships.First(s => s.Role == ShipRole.Trader && !s.IsEscort && s.Faction == w.Independents);
            var ceres = Port(w, "Ceres Hub");
            Assert.That(ship.PersonalHold.AddFirstFit(Good.Food), Is.Not.Null);
            var sm = w.Trade.Smuggling;
            Assert.That(sm.ReasonFor(ship, ceres, Good.Food), Is.EqualTo(BackDoorReason.None), "the open market takes it");
            Assert.That(sm.SellBackDoor(ship, ceres, Good.Food, 1, StartJd, out _, out _, 0.99), Is.EqualTo(Smuggling.Outcome.NothingToSmuggle));

            w.Diplomacy.DeclareBlockade(ceres, w.Reach);
            Assert.That(sm.ReasonFor(ship, ceres, Good.Food), Is.EqualTo(BackDoorReason.Blockade));
            double price = Smuggling.PriceFor(ceres, Good.Food, BackDoorReason.Blockade);
            Assert.That(price, Is.GreaterThan(ceres.Price[(int)Good.Food]));
            double stock = ceres.Stock[(int)Good.Food], purse = ship.Faction.Treasury;
            var outcome = sm.SellBackDoor(ship, ceres, Good.Food, 1, StartJd, out int sold, out double credits, 0.99);
            Assert.That(outcome, Is.EqualTo(Smuggling.Outcome.Sold));
            Assert.That(sold, Is.EqualTo(1));
            Assert.That(credits, Is.GreaterThan(0));
            Assert.That(ship.Faction.Treasury, Is.EqualTo(purse + credits).Within(1e-6));
            Assert.That(ceres.Stock[(int)Good.Food], Is.GreaterThan(stock));
            Assert.That(ship.PersonalHold.Items, Is.Empty);
            Assert.That(w.Trade.Smuggled, Is.EqualTo(1));
        }

        [Test]
        public void CaughtAtTheBackDoorTheCratesAreSeizedAndSmugglingIsCharged()
        {
            var w = CreateWorld();
            var ship = w.Fleet.Ships.First(s => s.Role == ShipRole.Trader && !s.IsEscort && s.Faction == w.Independents);
            var earth = Port(w, "Earth Orbital");
            earth.Owner.Contraband.Add(Good.Munitions);
            Assert.That(ship.PersonalHold.AddFirstFit(Good.Munitions), Is.Not.Null);
            var sm = w.Trade.Smuggling;
            Assert.That(sm.ReasonFor(ship, earth, Good.Munitions), Is.EqualTo(BackDoorReason.Contraband));
            Assert.That(sm.CatchChance(earth, BackDoorReason.Contraband), Is.GreaterThan(0));
            int crimes = w.Law.Player.Crimes.Count;
            double purse = ship.Faction.Treasury;
            var outcome = sm.SellBackDoor(ship, earth, Good.Munitions, 1, StartJd, out int sold, out _, 0);
            Assert.That(outcome, Is.EqualTo(Smuggling.Outcome.Caught));
            Assert.That(sold, Is.EqualTo(0));
            Assert.That(ship.PersonalHold.Items, Is.Empty, "seized");
            Assert.That(ship.Faction.Treasury, Is.EqualTo(purse));
            Assert.That(w.Law.Player.Crimes.Count, Is.GreaterThan(crimes));
            Assert.That(w.Law.Player.Crimes.Last().Kind, Is.EqualTo(CrimeKind.Smuggling));
            Assert.That(w.Trade.TimesCaught, Is.EqualTo(1));
        }

        [Test]
        public void AHavenOrLawlessStationHasNoBackDoor()
        {
            var w = CreateWorld();
            var haven = w.Economy.Settlements.First(s => s.Kind == SettlementKind.Haven);
            Assert.That(Smuggling.HasBackDoor(haven), Is.False);
            Assert.That(Smuggling.HasBackDoor(Port(w, "Earth Orbital")), Is.True);
        }

        // ---------------------------------------------------------------- the career

        [Test]
        public void TheTradersRankRisesWithWhatTheHouseIsWorth()
        {
            var w = CreateWorld();
            var desk = w.Trade;
            Assert.That(TradeDesk.RankFor(0), Is.EqualTo(TraderRank.Peddler));
            Assert.That(TradeDesk.RankFor(TradeDesk.RankWorth[(int)TraderRank.Merchant]), Is.EqualTo(TraderRank.Merchant));
            Funded(w, 2e8);
            Assert.That(desk.NetWorth(), Is.GreaterThanOrEqualTo(2e8));
            desk.Update(StartJd);
            desk.Update(StartJd + 1.01);
            Assert.That(desk.Rank, Is.EqualTo(TradeDesk.RankFor(desk.NetWorth())));
            Assert.That(desk.Rank, Is.GreaterThan(TraderRank.Hauler));
            Assert.That(desk.TakeNotices(), Has.Some.Contains("counted a"));
            Assert.That(desk.BrokerDiscount, Is.GreaterThan(0));
        }

        [Test]
        public void TheDeskReportsMarketShocksAndRisksToWhatTheHouseOwns()
        {
            var w = CreateWorld();
            var h = Funded(w);
            var ceres = Port(w, "Ceres Hub");
            h.BuyWarehouse(ceres, StartJd);
            w.Diplomacy.DeclareBlockade(ceres, w.Reach);
            var tips = w.Trade.Tips(StartJd);
            Assert.That(tips.Any(t => t.Market == ceres && t.Text.Contains("uninsured")), Is.True);
        }

        [Test]
        public void AFreeTraderPicksItsOwnRuns()
        {
            var w = CreateWorld();
            var h = Funded(w);
            var owned = Freighter(w, h);
            Assert.That(h.Order(owned, StandingOrder.FreeTrade()), Is.True);
            Assert.That(owned.Orders.Describe(w.Catalog), Is.EqualTo("free trading"));
            int here = owned.Ship.Orbit.Body;
            int next = h.ChooseNext(owned.Ship, here, StartJd);
            // With nothing worth carrying it answers -1 (the fleet moves it on like any merchant), never "stay here".
            Assert.That(next, Is.Not.EqualTo(here));
            if (next < 0) Assert.That(owned.Ship.Cargo, Does.Contain("looking for a cargo"));
            else Assert.That(owned.Ship.HasCargo, Is.True);
        }
    }
}
