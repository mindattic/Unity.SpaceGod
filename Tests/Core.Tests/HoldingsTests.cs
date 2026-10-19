using System.Linq;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    /// <summary>
    /// U.SPC-T102: the player's fleet and property. The player buys hulls (or takes ships they have into their fleet),
    /// hires captains who fly them under standing orders (a trade route, mining a claim, escorting, a patrol), receives
    /// their earnings and losses into the house account, and owns property: a warehouse, a share in a station, a
    /// mining claim. Owned ships are simulated like any other ship in the world.
    /// </summary>
    [TestFixture]
    public class HoldingsTests
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

        static Settlement Port(World w, string name) => w.Economy.Settlements.First(s => s.Name == name);

        /// <summary>A house with money in the account: deposited from a navy's purse.</summary>
        static Holdings Funded(World w, double credits = 5e8)
        {
            var h = w.Holdings;
            Assert.That(h.Deposit(w.Sol, credits, StartJd), Is.True);
            return h;
        }

        // ---------------------------------------------------------------- ships

        [Test]
        public void BuyingAHullPaysTheYardAndPutsItInSpaceUnderTheHouseFlagLaidUpUntilACaptainIsHired()
        {
            var w = CreateWorld();
            var h = Funded(w);
            var yard = Port(w, "Earth Orbital");
            double ownerBefore = yard.Owner.Treasury, before = h.Balance;
            var owned = h.Buy(yard, ShipClass.Freighter, "Good Fortune", StartJd);

            Assert.That(owned, Is.Not.Null);
            double price = Economy.HullPrice(ShipClass.Freighter);
            Assert.That(h.Balance, Is.EqualTo(before - price).Within(1e-3));
            Assert.That(yard.Owner.Treasury, Is.EqualTo(ownerBefore + price).Within(1e-3));
            var ship = owned.Ship;
            Assert.That(w.Fleet.Ships, Does.Contain(ship), "simulated like any other ship");
            Assert.That(ship.Faction, Is.SameAs(h.House));
            Assert.That(ship.Orbit.Body, Is.EqualTo(yard.Body));
            Assert.That(ship.CapacityTonnes, Is.GreaterThan(0));
            Assert.That(h.Owns(ship), Is.True);
            // No captain: it stays where it is.
            Assert.That(h.ChooseNext(ship, yard.Body, StartJd), Is.EqualTo(yard.Body));
            Assert.That(h.Owns(w.Fleet.Ships[0]), Is.False);
        }

        [Test]
        public void TheHouseCannotBuyWhatItCannotAffordOrFromAYardThatWillNotDeal()
        {
            var w = CreateWorld();
            var h = w.Holdings;
            Assert.That(h.Buy(Port(w, "Earth Orbital"), ShipClass.Freighter, "Pauper", StartJd), Is.Null, "an empty account");
            Funded(w);
            var haven = w.Economy.Settlements.First(s => s.Kind == SettlementKind.Haven);
            Assert.That(h.Buy(haven, ShipClass.Destroyer, "Black Market", StartJd), Is.Null, "pirates sell nothing to the lawful");
        }

        [Test]
        public void ATakenShipJoinsTheFleetWithItsCargoAsClearProfit()
        {
            var w = CreateWorld();
            var h = w.Holdings;
            var ship = w.Fleet.Ships.First(s => s.Faction == w.Independents && s.Role == ShipRole.Trader && !s.IsEscort);
            var owned = h.Enlist(ship, StartJd);
            Assert.That(owned, Is.Not.Null);
            Assert.That(ship.Faction, Is.SameAs(h.House));
            Assert.That(ship.CargoCost, Is.EqualTo(0));
            Assert.That(h.Enlist(ship, StartJd), Is.Null, "already the house's");
            var pirate = w.Fleet.Ships.First(s => s.Faction.IsPirate && !s.IsEscort);
            Assert.That(h.Enlist(pirate, StartJd), Is.Null);
        }

        // ---------------------------------------------------------------- standing orders

        [Test]
        public void ATradeRouteCaptainWorksTheStopsInOrderBuyingAtEachForTheNext()
        {
            var w = CreateWorld();
            var h = Funded(w);
            int earth = Body(w, "Earth"), mars = Body(w, "Mars"), ceres = Body(w, "Ceres");
            var owned = h.Buy(Port(w, "Earth Orbital"), ShipClass.Freighter, "Route Runner", StartJd);
            h.Hire(owned, StartJd, 1);
            Assert.That(h.Order(owned, StandingOrder.TradeRoute(earth, mars, ceres)), Is.True);
            var ship = owned.Ship;
            Assert.That(ship.Role, Is.EqualTo(ShipRole.Trader), "a merchant to the markets and the rings");

            Assert.That(h.ChooseNext(ship, earth, StartJd), Is.EqualTo(mars));
            if (ship.HasCargo) Assert.That(ship.CargoDestination.Body, Is.EqualTo(mars), "bought for the next stop");
            ship.HasCargo = false;
            Assert.That(h.ChooseNext(ship, mars, StartJd), Is.EqualTo(ceres));
            ship.HasCargo = false;
            Assert.That(h.ChooseNext(ship, ceres, StartJd), Is.EqualTo(earth), "and round again");
            // Blown off course (to Venus, say), it makes for the stop it was bound for.
            ship.HasCargo = false;
            Assert.That(h.ChooseNext(ship, Body(w, "Venus"), StartJd), Is.EqualTo(earth));
            // At another stop than the one it was bound for (Mars, not Earth), it carries on from there.
            ship.HasCargo = false;
            Assert.That(h.ChooseNext(ship, mars, StartJd), Is.EqualTo(ceres));
        }

        [Test]
        public void ARouteNeedsTwoStopsAndAHold()
        {
            var w = CreateWorld();
            var h = Funded(w);
            var freighter = h.Buy(Port(w, "Earth Orbital"), ShipClass.Freighter, "Hauler", StartJd);
            var destroyer = h.Buy(Port(w, "Earth Orbital"), ShipClass.Destroyer, "Picket", StartJd);
            Assert.That(h.Order(freighter, StandingOrder.TradeRoute(Body(w, "Earth"))), Is.False);
            Assert.That(h.Order(destroyer, StandingOrder.TradeRoute(Body(w, "Earth"), Body(w, "Mars"))), Is.False, "a warship has no hold");
            Assert.That(h.Order(destroyer, StandingOrder.Escort(destroyer.Ship)), Is.False, "nobody escorts themselves");
        }

        [Test]
        public void APatrolMakesTheRoundOfItsWaypoints()
        {
            var w = CreateWorld();
            var h = Funded(w);
            int earth = Body(w, "Earth"), moon = Body(w, "Moon"), mars = Body(w, "Mars");
            var owned = h.Buy(Port(w, "Earth Orbital"), ShipClass.Destroyer, "Watchful", StartJd);
            h.Hire(owned, StartJd);
            Assert.That(h.Order(owned, StandingOrder.Patrol(moon, mars, earth)), Is.True);
            Assert.That(owned.Ship.Role, Is.EqualTo(ShipRole.Patrol));
            Assert.That(h.ChooseNext(owned.Ship, earth, StartJd), Is.EqualTo(moon));
            Assert.That(h.ChooseNext(owned.Ship, moon, StartJd), Is.EqualTo(mars));
            Assert.That(h.ChooseNext(owned.Ship, mars, StartJd), Is.EqualTo(earth));
            Assert.That(h.ChooseNext(owned.Ship, earth, StartJd), Is.EqualTo(moon));
        }

        [Test]
        public void AMiningCaptainCutsAHoldfulOnTheClaimForNothingAndTakesItToMarket()
        {
            var w = CreateWorld();
            var h = Funded(w);
            int phobos = Body(w, "Phobos");
            var claim = h.StakeClaim(phobos, Good.NickelIron, StartJd);
            Assert.That(claim, Is.Not.Null);
            Assert.That(claim.YieldPerDay, Is.EqualTo(Holdings.BaseYieldPerDay).Within(1e-6));
            Assert.That(Holdings.YieldOf(Good.Platinum), Is.LessThan(Holdings.YieldOf(Good.NickelIron) / 20), "rare ores come slowly");
            Assert.That(h.StakeClaim(phobos, Good.NickelIron, StartJd), Is.Null, "already staked");
            Assert.That(h.StakeClaim(Body(w, "Sun"), Good.NickelIron, StartJd), Is.Null);
            Assert.That(h.StakeClaim(phobos, Good.Electronics, StartJd), Is.Null, "nobody mines electronics");

            var owned = h.Buy(Port(w, "Ares Prime"), ShipClass.Miner, "Pick and Shovel", StartJd);
            var courier = h.Buy(Port(w, "Ares Prime"), ShipClass.Courier, "Dart", StartJd);
            Assert.That(h.Order(courier, StandingOrder.Mining(claim)), Is.False, "a courier cannot carry ore");
            h.Hire(owned, StartJd);
            Assert.That(h.Order(owned, StandingOrder.Mining(claim)), Is.True);
            var ship = owned.Ship;
            // Empty and off the claim: out to it.
            Assert.That(h.ChooseNext(ship, Body(w, "Mars"), StartJd), Is.EqualTo(phobos));
            // On the claim: a holdful of ore that cost nothing, booked for a market, and the time it takes to cut.
            int dest = h.ChooseNext(ship, phobos, StartJd);
            Assert.That(ship.HasCargo, Is.True);
            Assert.That(ship.CargoGood, Is.EqualTo(Good.NickelIron));
            Assert.That(ship.CargoTonnes, Is.EqualTo(ship.CapacityTonnes));
            Assert.That(ship.CargoCost, Is.EqualTo(0));
            Assert.That(dest, Is.Not.EqualTo(phobos));
            Assert.That(ship.CargoDestination, Is.Not.Null);
            Assert.That(ship.CargoDestination.Body, Is.EqualTo(dest));
            Assert.That(owned.TonnesMined, Is.EqualTo(ship.CapacityTonnes));
            Assert.That(h.Dwell(ship, 1), Is.EqualTo(ship.CapacityTonnes / claim.YieldPerDay).Within(1e-9));
            // Sold there, it is all profit, and the claim is credited with it.
            double before = h.Balance;
            w.Economy.OnArrive(ship, dest, StartJd + 3);
            Assert.That(ship.HasCargo, Is.False);
            Assert.That(h.Balance, Is.GreaterThan(before));
            w.Holdings.Update(StartJd + 3);
            Assert.That(owned.Earned, Is.GreaterThan(0));
            Assert.That(claim.Income, Is.EqualTo(owned.Earned).Within(1e-6));
        }

        [Test]
        public void ABadlyHurtMerchantLandedAtARingedYardWorldIsMendedThere()
        {
            var w = CreateWorld();
            var h = Funded(w);
            int earth = Body(w, "Earth"), mars = Body(w, "Mars");
            var owned = h.Buy(Port(w, "Earth Orbital"), ShipClass.Freighter, "Hard Luck", StartJd);
            h.Hire(owned, StartJd, 1);
            Assert.That(h.Order(owned, StandingOrder.TradeRoute(earth, mars)), Is.True);
            var ship = owned.Ship;
            // A merchant comes down through one of Earth's rings and lies landed there.
            var ring = w.Infrastructure.Rings.First(r => r.Body == earth);
            ship.Ring = ring;
            ship.Orbit = ring.RingPoint;
            ship.Phase = ShipPhase.Landed;
            ship.Combat.Hull = ship.Combat.Spec.Hull * 0.3;
            double hurt = ship.Combat.Damage;
            Assert.That(hurt, Is.GreaterThan(Holdings.RepairDamage));
            // Badly hurt at a yard that serves the house, it waits there to be mended ...
            Assert.That(h.ChooseNext(ship, earth, StartJd), Is.EqualTo(earth));
            // ... and is, landed or not.
            w.Holdings.Update(StartJd + 3);
            Assert.That(ship.Combat.Damage, Is.LessThan(hurt));
            Assert.That(owned.Repairs, Is.GreaterThan(0));
        }

        [Test]
        public void AnEscortShadowsItsChargeAndStandsDownWhenItIsLost()
        {
            var w = CreateWorld();
            var h = Funded(w);
            int earth = Body(w, "Earth"), mars = Body(w, "Mars");
            var trader = h.Buy(Port(w, "Earth Orbital"), ShipClass.Freighter, "Precious", StartJd);
            var guard = h.Buy(Port(w, "Earth Orbital"), ShipClass.Destroyer, "Shepherd", StartJd);
            h.Hire(guard, StartJd);
            Assert.That(h.Order(guard, StandingOrder.Escort(trader.Ship)), Is.True);
            // Its charge in port here: it waits with it, checking often.
            Assert.That(h.ChooseNext(guard.Ship, earth, StartJd), Is.EqualTo(earth));
            Assert.That(h.Dwell(guard.Ship, 2), Is.LessThan(0.5));
            // Its charge sets out for Mars: so does it.
            trader.Ship.Phase = ShipPhase.Transit;
            trader.Ship.DestinationBody = mars;
            Assert.That(h.ChooseNext(guard.Ship, earth, StartJd), Is.EqualTo(mars));
            // Lost: the escort stands down, and the loss is on the books at what the hull was worth.
            trader.Ship.Destroyed = true;
            h.Update(StartJd + 1);
            Assert.That(trader.Lost, Is.True);
            Assert.That(trader.LostHow, Is.EqualTo("was destroyed"));
            Assert.That(guard.Orders.Kind, Is.EqualTo(StandingOrderKind.Hold));
            Assert.That(h.TotalLost, Is.EqualTo(Economy.HullPrice(ShipClass.Freighter)).Within(1));
            Assert.That(h.Ledger.Any(e => !e.Cash && e.Text.Contains("Precious")), Is.True);
            Assert.That(h.Owns(trader.Ship), Is.False);
        }

        [Test]
        public void AShipTakenByRaidersIsALoss()
        {
            var w = CreateWorld();
            var h = Funded(w);
            var owned = h.Buy(Port(w, "Earth Orbital"), ShipClass.Freighter, "Unlucky", StartJd);
            owned.Ship.Faction = w.Pirates;
            h.Update(StartJd + 1);
            Assert.That(owned.Lost, Is.True);
            Assert.That(owned.LostHow, Does.Contain(w.Pirates.Name));
        }

        // ---------------------------------------------------------------- earnings

        [Test]
        public void TradeEarningsGoToTheHouseAccountAndOntoTheShipsBooks()
        {
            var w = CreateWorld();
            var h = Funded(w);
            int earth = Body(w, "Earth"), mars = Body(w, "Mars");
            var owned = h.Buy(Port(w, "Earth Orbital"), ShipClass.Freighter, "Earner", StartJd);
            var ship = owned.Ship;
            // A load bought at Earth for Mars: paid from the account.
            ship.HasCargo = true;
            ship.CargoGood = Good.Machinery;
            ship.CargoTonnes = 1000;
            ship.CargoCost = 1e6;
            h.House.Treasury -= 1e6;
            double before = h.Balance;
            var mars0 = Port(w, "Ares Prime");
            double value = mars0.SaleValue(Good.Machinery, 1000);
            w.Economy.OnArrive(ship, mars, StartJd + 0.5);
            double net = value * (1 - Economy.PortTax);
            Assert.That(h.Balance, Is.EqualTo(before + net).Within(1));
            h.Update(StartJd + 1);
            Assert.That(owned.Earned, Is.EqualTo(net - 1e6).Within(1), "the ship's books: what it sold for, less what the load cost");
            Assert.That(h.Ledger.Any(e => e.Text.Contains("takings")), Is.True);
            // The books agree with the account.
            Assert.That(h.BookBalance, Is.EqualTo(h.Balance + h.CargoAfloat).Within(1));
        }

        [Test]
        public void CaptainsAndCrewsArePaidDailyAndAnUnpaidCaptainWalksOff()
        {
            var w = CreateWorld();
            var h = Funded(w, Economy.HullPrice(ShipClass.Freighter) + 10000);
            var owned = h.Buy(Port(w, "Earth Orbital"), ShipClass.Freighter, "Payroll", StartJd);
            var captain = h.Hire(owned, StartJd, 1);
            Assert.That(captain.WagePerDay, Is.EqualTo(Holdings.CaptainWage));
            double day = Holdings.CaptainWage + Economy.RunningCostPerDay(ShipClass.Freighter);
            double before = h.Balance;
            h.Update(StartJd + 1);
            Assert.That(h.Balance, Is.EqualTo(before - day).Within(1e-6));
            Assert.That(owned.Wages, Is.EqualTo(day).Within(1e-6));
            // The account runs dry: the captain works on unpaid for a few days, then quits.
            for (int d = 2; d <= 2 + Holdings.QuitDays + 2; d++) h.Update(StartJd + d);
            Assert.That(owned.Captain, Is.Null);
            Assert.That(h.ChooseNext(owned.Ship, owned.Ship.Orbit.Body, StartJd + 10), Is.EqualTo(owned.Ship.Orbit.Body), "laid up without a captain");
            Assert.That(h.Balance, Is.GreaterThanOrEqualTo(0), "never paid into the red");
        }

        [Test]
        public void ABetterCaptainCostsMoreAndTurnsTheShipRoundFaster()
        {
            var w = CreateWorld();
            var h = Funded(w);
            var a = h.Buy(Port(w, "Earth Orbital"), ShipClass.Freighter, "Green", StartJd);
            var b = h.Buy(Port(w, "Earth Orbital"), ShipClass.Freighter, "Veteran", StartJd);
            h.Hire(a, StartJd, 0.5);
            h.Hire(b, StartJd, 1.5);
            h.Order(a, StandingOrder.TradeRoute(Body(w, "Earth"), Body(w, "Mars")));
            h.Order(b, StandingOrder.TradeRoute(Body(w, "Earth"), Body(w, "Mars")));
            Assert.That(b.Captain.WagePerDay, Is.GreaterThan(a.Captain.WagePerDay));
            Assert.That(h.Dwell(b.Ship, 2), Is.LessThan(h.Dwell(a.Ship, 2)));
        }

        // ---------------------------------------------------------------- property

        [Test]
        public void AStationSharePaysItsShareOfThePortTaxesOnTheTradeThere()
        {
            var w = CreateWorld();
            var h = Funded(w, 3e9);
            var ceres = Port(w, "Ceres Hub");
            double price = Holdings.SharePrice(ceres, 0.2);
            double before = h.Balance, ownerBefore = ceres.Owner.Treasury;
            var share = h.BuyShare(ceres, 0.2, StartJd);
            Assert.That(share, Is.Not.Null);
            Assert.That(h.Balance, Is.EqualTo(before - price).Within(1e-3));
            Assert.That(h.BuyShare(ceres, 0.4, StartJd), Is.Null, "no more than half");
            Assert.That(h.BuyShare(ceres, 0.1, StartJd), Is.SameAs(share), "more of the same share");
            Assert.That(share.Share, Is.EqualTo(0.3).Within(1e-9));

            before = h.Balance;
            ownerBefore = ceres.Owner.Treasury;
            ceres.TradeVolumeCredits += 1e8;
            h.Update(StartJd + 1);
            double dividend = 1e8 * Economy.PortTax * 0.3;
            Assert.That(h.Balance - before, Is.EqualTo(dividend).Within(1));
            Assert.That(share.Income, Is.EqualTo(dividend).Within(1));
            Assert.That(ownerBefore - ceres.Owner.Treasury, Is.EqualTo(dividend).Within(1), "paid out of the owner's taxes");

            // Taken by pirates: the share is gone.
            ceres.Owner = w.Pirates;
            h.Update(StartJd + 2);
            Assert.That(share.Lost, Is.True);
        }

        [Test]
        public void AWarehouseHoldsGoodsBoughtAtItsPortUntilTheyAreSoldAndCostsRent()
        {
            var w = CreateWorld();
            var h = Funded(w);
            var port = Port(w, "Earth Orbital");
            var wh = h.BuyWarehouse(port, StartJd);
            Assert.That(wh, Is.Not.Null);
            Assert.That(h.BuyWarehouse(port, StartJd), Is.Null, "one to a port");
            var good = Enumerable.Range(0, GoodInfo.Count).Select(g => (Good)g).First(g => port.Available(g) > 100 && port.Demand[(int)g] > 0);
            double stock = port.Stock[(int)good];
            double got = h.StoreFromMarket(wh, good, 100, StartJd);
            Assert.That(got, Is.EqualTo(100).Within(1e-6));
            Assert.That(wh.Stock[(int)good], Is.EqualTo(100).Within(1e-6));
            Assert.That(port.Stock[(int)good], Is.EqualTo(stock - 100).Within(1e-6));
            double sold = h.SellFromStore(wh, good, 1000, StartJd);
            Assert.That(sold, Is.GreaterThan(0));
            Assert.That(wh.Stock[(int)good], Is.EqualTo(0).Within(1e-9), "sells only what it holds");
            double before = h.Balance;
            h.Update(StartJd + 1);
            Assert.That(before - h.Balance, Is.EqualTo(Holdings.WarehouseRentPerDay).Within(1e-6));
            Assert.That(h.BookBalance, Is.EqualTo(h.Balance).Within(1));
        }

        // ---------------------------------------------------------------- the live system

        [Test]
        public void InTheLiveSystemOwnedShipsUnderOrdersEarnTheirKeepAndTheBooksBalance()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            var h = w.Holdings;
            h.Deposit(w.Sol, 1e8, StartJd);
            int earth = Body(w, "Earth"), mars = Body(w, "Mars");
            var trader = h.Buy(Port(w, "Earth Orbital"), ShipClass.Freighter, "Steady Earner", StartJd);
            h.Hire(trader, StartJd, 1);
            h.Order(trader, StandingOrder.TradeRoute(earth, mars));
            var claim = h.StakeClaim(Body(w, "Phobos"), Good.NickelIron, StartJd);
            var miner = h.Buy(Port(w, "Ares Prime"), ShipClass.Miner, "Deep Seam", StartJd);
            h.Hire(miner, StartJd, 1);
            h.Order(miner, StandingOrder.Mining(claim));

            for (double jd = StartJd; jd <= StartJd + 150; jd += 0.1)
            {
                eph.Update(jd);
                w.Update(jd);
            }
            foreach (var o in h.Ships)
                TestContext.Out.WriteLine($"{o.Name,-20} {o.Orders.Describe(w.Catalog),-30} trips {o.Ship.TripsCompleted} earned {o.Earned:N0} spent {o.Spent:N0} wages {o.Wages:N0} net {o.Net:N0} lost {o.Lost} {o.LostHow}");
            foreach (var e in h.Ledger.TakeLast(12)) TestContext.Out.WriteLine($"{Astro.ToDateTime(e.Jd):yyyy-MM-dd} {e}");

            Assert.That(trader.Ship.TripsCompleted + miner.Ship.TripsCompleted, Is.GreaterThan(2), "they fly");
            Assert.That(h.Ships.Where(o => !o.Lost).Sum(o => o.Earned), Is.GreaterThan(0), "and bring money in");
            if (!miner.Lost) Assert.That(miner.TonnesMined, Is.GreaterThan(0));
            Assert.That(h.Ships.Sum(o => o.Wages), Is.GreaterThan(0));
            Assert.That(h.BookBalance + h.Unsettled, Is.EqualTo(h.Balance + h.CargoAfloat).Within(1), "every credit is on the books");
        }
    }
}
