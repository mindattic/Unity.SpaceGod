using System;
using System.Linq;
using SolarSystem.Core;
using Xunit;

namespace SolarSystem.Core.Tests
{
    /// <summary>
    /// Covers USS-T33: the AI trader logistics in <see cref="Economy"/> -- <see cref="Economy.PlanTrade"/>,
    /// <see cref="Economy.OnArrive"/>, <see cref="Economy.Release"/>, <see cref="Economy.RecountInbound"/>,
    /// <see cref="Economy.Carries"/>, <see cref="Economy.CapacityOf"/> and <see cref="Economy.CargoText"/>.
    /// Until now these were only exercised indirectly, over years of simulated time, by
    /// <see cref="EconomyTests.TwoYearsOfTradeKeepEveryoneFedAndTradersInProfit"/> and
    /// <see cref="EconomyTests.TradersOnlyDockWhereTheyAreWelcome"/>.
    /// </summary>
    public class TradeLogisticsTests
    {
        static Faction MakeFaction(string name, double treasury = 1_000_000_000, bool pirate = false) =>
            new Faction(name, name.Substring(0, Math.Min(3, name.Length)).ToUpperInvariant(), default)
            {
                Treasury = treasury,
                IsPirate = pirate,
            };

        static Settlement MakePort(Economy economy, string name, int body, Faction owner) =>
            economy.Add(name, body, SettlementKind.Station, owner, population: 0);

        /// <summary>Sets a good's market state directly (bypassing production) and refreshes its price.</summary>
        static void SetGood(Settlement s, Good g, double stock = 0, double demand = 0, double inbound = 0)
        {
            s.Stock[(int)g] = stock;
            s.Demand[(int)g] = demand;
            s.Inbound[(int)g] = inbound;
            s.RefreshPrices();
        }

        static Ship MakeShip(Faction faction, ShipClass cls) =>
            new Ship { Spec = ShipClassSpec.Of(cls), Faction = faction, CapacityTonnes = Economy.CapacityOf(cls) };

        static Ship LoadedShip(Faction faction, ShipClass cls, Good good, double tonnes, double cost, Settlement destination = null, double bonus = 0) =>
            new Ship
            {
                Spec = ShipClassSpec.Of(cls),
                Faction = faction,
                CapacityTonnes = Economy.CapacityOf(cls),
                HasCargo = true,
                CargoGood = good,
                CargoTonnes = tonnes,
                CargoCost = cost,
                CargoDestination = destination,
                CargoBonus = bonus,
            };

        // ---------------------------------------------------------------- Carries

        [Theory]
        [InlineData(Good.NickelIron, true)]
        [InlineData(Good.Copper, true)]
        [InlineData(Good.Platinum, true)]
        [InlineData(Good.Water, true)]
        [InlineData(Good.Nitrogen, true)]
        [InlineData(Good.Hydrocarbons, true)]
        [InlineData(Good.HeliumThree, false)] // volatile, but reserved for couriers, not miners
        [InlineData(Good.Food, false)]
        [InlineData(Good.Electronics, false)]
        [InlineData(Good.Munitions, false)]
        public void CarriesForAMinerIsMineralsPlusWaterNitrogenAndHydrocarbonsOnly(Good good, bool expected) =>
            Assert.Equal(expected, Economy.Carries(ShipClass.Miner, good));

        [Theory]
        [InlineData(Good.MedicalSupplies, true)]
        [InlineData(Good.Electronics, true)]
        [InlineData(Good.Platinum, true)]
        [InlineData(Good.RareEarths, true)]
        [InlineData(Good.HeliumThree, true)]
        [InlineData(Good.NickelIron, false)]
        [InlineData(Good.Food, false)]
        [InlineData(Good.Water, false)]
        [InlineData(Good.Munitions, false)]
        [InlineData(Good.Machinery, false)]
        public void CarriesForACourierIsOnlyTheNamedHighValueGoods(Good good, bool expected) =>
            Assert.Equal(expected, Economy.Carries(ShipClass.Courier, good));

        [Theory]
        [InlineData(Good.Food)]
        [InlineData(Good.NickelIron)]
        [InlineData(Good.Electronics)]
        [InlineData(Good.Munitions)]
        public void CarriesForAFreighterIsTrueForEveryGood(Good good) =>
            Assert.True(Economy.Carries(ShipClass.Freighter, good));

        [Theory]
        [InlineData(ShipClass.Carrier)]
        [InlineData(ShipClass.Battleship)]
        [InlineData(ShipClass.Cruiser)]
        [InlineData(ShipClass.Destroyer)]
        [InlineData(ShipClass.Breacher)]
        [InlineData(ShipClass.Fighter)]
        public void CarriesIsFalseForEveryGoodOnWarshipsAndCombatCraft(ShipClass cls) =>
            Assert.All(Enum.GetValues(typeof(Good)).Cast<Good>(), g => Assert.False(Economy.Carries(cls, g)));

        // ---------------------------------------------------------------- CapacityOf

        [Theory]
        [InlineData(ShipClass.Freighter, 20000)]
        [InlineData(ShipClass.Miner, 30000)]
        [InlineData(ShipClass.Courier, 400)]
        [InlineData(ShipClass.Carrier, 0)]
        [InlineData(ShipClass.Battleship, 0)]
        [InlineData(ShipClass.Fighter, 0)]
        public void CapacityOfReturnsEachHullsHoldSize(ShipClass cls, double expected) =>
            Assert.Equal(expected, Economy.CapacityOf(cls));

        // ---------------------------------------------------------------- CargoText

        [Fact]
        public void CargoTextIsNullWhenTheShipIsNotCarryingAnything()
        {
            var ship = new Ship { HasCargo = false };
            Assert.Null(Economy.CargoText(ship));
        }

        [Fact]
        public void CargoTextNamesTheGoodTonnageAndDestinationWhenBooked()
        {
            var destination = new Settlement { Name = "Ceres Station" };
            var ship = new Ship { HasCargo = true, CargoGood = Good.Electronics, CargoTonnes = 2500, CargoDestination = destination };

            Assert.Equal("2,500 t Electronics for Ceres Station", Economy.CargoText(ship));
        }

        [Fact]
        public void CargoTextOmitsTheDestinationWhenNoneIsBooked()
        {
            var ship = new Ship { HasCargo = true, CargoGood = Good.Electronics, CargoTonnes = 2500, CargoDestination = null };

            Assert.Equal("2,500 t Electronics", Economy.CargoText(ship));
        }

        // ---------------------------------------------------------------- Release

        [Fact]
        public void ReleaseReducesTheBookedDestinationsInboundByTheCargoTonnes()
        {
            var economy = new Economy(new Diplomacy(), startJd: 0);
            var destination = MakePort(economy, "Dest", 1, MakeFaction("Owner"));
            destination.Inbound[(int)Good.Food] = 800;
            var ship = new Ship { HasCargo = true, CargoGood = Good.Food, CargoTonnes = 500, CargoDestination = destination };

            economy.Release(ship);

            Assert.Equal(300, destination.Inbound[(int)Good.Food], 6);
        }

        [Fact]
        public void ReleaseClampsToZeroRatherThanGoingNegative()
        {
            var economy = new Economy(new Diplomacy(), startJd: 0);
            var destination = MakePort(economy, "Dest", 1, MakeFaction("Owner"));
            destination.Inbound[(int)Good.Food] = 200;
            var ship = new Ship { HasCargo = true, CargoGood = Good.Food, CargoTonnes = 500, CargoDestination = destination };

            economy.Release(ship);

            Assert.Equal(0, destination.Inbound[(int)Good.Food]);
        }

        [Fact]
        public void ReleaseIsANoOpWhenTheShipIsNotActuallyCarryingCargo()
        {
            var economy = new Economy(new Diplomacy(), startJd: 0);
            var destination = MakePort(economy, "Dest", 1, MakeFaction("Owner"));
            destination.Inbound[(int)Good.Food] = 800;
            var ship = new Ship { HasCargo = false, CargoGood = Good.Food, CargoTonnes = 500, CargoDestination = destination };

            economy.Release(ship);

            Assert.Equal(800, destination.Inbound[(int)Good.Food]);
        }

        [Fact]
        public void ReleaseIsANoOpWhenNothingIsBooked()
        {
            var economy = new Economy(new Diplomacy(), startJd: 0);
            var ship = new Ship { HasCargo = true, CargoGood = Good.Food, CargoTonnes = 500, CargoDestination = null };

            var ex = Record.Exception(() => economy.Release(ship));

            Assert.Null(ex);
        }

        // ---------------------------------------------------------------- RecountInbound

        [Fact]
        public void RecountInboundClearsStaleBookingsThatNoShipStillCarries()
        {
            var economy = new Economy(new Diplomacy(), startJd: 0);
            var destination = MakePort(economy, "Dest", 1, MakeFaction("Owner"));
            destination.Inbound[(int)Good.Food] = 999;

            economy.RecountInbound(Array.Empty<Ship>());

            Assert.Equal(0, destination.Inbound[(int)Good.Food]);
        }

        [Fact]
        public void RecountInboundSumsTonnesFromEveryShipBoundForTheSameGoodAndDestination()
        {
            var economy = new Economy(new Diplomacy(), startJd: 0);
            var faction = MakeFaction("Trader");
            var destination = MakePort(economy, "Dest", 1, MakeFaction("Owner"));
            var shipA = new Ship { Faction = faction, HasCargo = true, CargoGood = Good.Food, CargoTonnes = 300, CargoDestination = destination };
            var shipB = new Ship { Faction = faction, HasCargo = true, CargoGood = Good.Food, CargoTonnes = 450, CargoDestination = destination };

            economy.RecountInbound(new[] { shipA, shipB });

            Assert.Equal(750, destination.Inbound[(int)Good.Food], 6);
        }

        [Fact]
        public void RecountInboundIgnoresDestroyedShipsAndShipsWithoutABookedDestination()
        {
            var economy = new Economy(new Diplomacy(), startJd: 0);
            var faction = MakeFaction("Trader");
            var destination = MakePort(economy, "Dest", 1, MakeFaction("Owner"));
            var destroyed = new Ship { Faction = faction, HasCargo = true, CargoGood = Good.Food, CargoTonnes = 500, CargoDestination = destination, Destroyed = true };
            var noCargo = new Ship { Faction = faction, HasCargo = false, CargoGood = Good.Food, CargoTonnes = 500, CargoDestination = destination };
            var noDestination = new Ship { Faction = faction, HasCargo = true, CargoGood = Good.Food, CargoTonnes = 500, CargoDestination = null };

            economy.RecountInbound(new[] { destroyed, noCargo, noDestination });

            Assert.Equal(0, destination.Inbound[(int)Good.Food]);
        }

        // ---------------------------------------------------------------- PlanTrade

        [Fact]
        public void PlanTradeOnlyLoadsGoodsTheHullIsFittedToCarry()
        {
            var economy = new Economy(new Diplomacy(), startJd: 1000);
            var owner = MakeFaction("Owner");
            var origin = MakePort(economy, "Origin", 1, owner);
            var destination = MakePort(economy, "Destination", 2, owner);
            // Electronics is worth far more than nickel-iron ore, so an unfiltered trader would always
            // prefer it; a Miner can only carry the ore (Economy.Carries), proving the filter is applied.
            SetGood(origin, Good.NickelIron, stock: 1_000_000, demand: 0);
            SetGood(origin, Good.Electronics, stock: 1_000_000, demand: 0);
            SetGood(destination, Good.NickelIron, demand: 5000);
            SetGood(destination, Good.Electronics, demand: 5000);
            var miner = MakeShip(MakeFaction("Trader"), ShipClass.Miner);

            int to = economy.PlanTrade(miner, here: 1, jd: 1000);

            Assert.Equal(2, to);
            Assert.True(miner.HasCargo);
            Assert.Equal(Good.NickelIron, miner.CargoGood);
        }

        [Fact]
        public void PlanTradeSkipsOriginPortsDiplomacyForbidsEvenWhenNothingElseIsProfitable()
        {
            var diplomacy = new Diplomacy();
            var economy = new Economy(diplomacy, startJd: 1000);
            var haven = MakePort(economy, "Haven", 1, MakeFaction("Raiders", pirate: true));
            var buyer = MakePort(economy, "Buyer", 2, MakeFaction("Market"));
            SetGood(haven, Good.NickelIron, stock: 1_000_000, demand: 0);
            SetGood(buyer, Good.NickelIron, demand: 5000);
            var freighter = MakeShip(MakeFaction("LawfulTrader"), ShipClass.Freighter);

            int to = economy.PlanTrade(freighter, here: 1, jd: 1000);

            Assert.Equal(-1, to);
            Assert.False(freighter.HasCargo);
        }

        [Fact]
        public void PlanTradeSkipsDestinationsDiplomacyForbidsEvenWhenTheyWouldPayBest()
        {
            var diplomacy = new Diplomacy();
            var economy = new Economy(diplomacy, startJd: 1000);
            var trader = MakeFaction("Trader");
            var enemyOwner = MakeFaction("Enemy");
            diplomacy.Set(trader, enemyOwner, Relation.War);
            var origin = MakePort(economy, "Origin", 1, MakeFaction("Dock"));
            var forbiddenDestination = MakePort(economy, "EnemyMarket", 2, enemyOwner);
            var allowedDestination = MakePort(economy, "FriendlyMarket", 3, MakeFaction("Friend"));
            SetGood(origin, Good.NickelIron, stock: 1_000_000, demand: 0);
            // The forbidden market is starved (far scarcer, so it would pay much more) -- it must still be skipped.
            SetGood(forbiddenDestination, Good.NickelIron, stock: 0, demand: 5000);
            SetGood(allowedDestination, Good.NickelIron, stock: 90000, demand: 5000);
            var freighter = MakeShip(trader, ShipClass.Freighter);

            int to = economy.PlanTrade(freighter, here: 1, jd: 1000);

            Assert.Equal(3, to);
            Assert.Equal(allowedDestination, freighter.CargoDestination);
        }

        [Fact]
        public void PlanTradeBooksInboundAtTheChosenDestinationAndLoadsTheShip()
        {
            var economy = new Economy(new Diplomacy(), startJd: 1000);
            var owner = MakeFaction("Owner");
            var origin = MakePort(economy, "Origin", 1, owner);
            var destination = MakePort(economy, "Destination", 2, owner);
            SetGood(origin, Good.NickelIron, stock: 1_000_000, demand: 0);
            SetGood(destination, Good.NickelIron, demand: 5000);
            var freighter = MakeShip(MakeFaction("Trader"), ShipClass.Freighter);
            double inboundBefore = destination.Inbound[(int)Good.NickelIron];

            int to = economy.PlanTrade(freighter, here: 1, jd: 1000);

            Assert.Equal(2, to);
            Assert.True(freighter.HasCargo);
            Assert.Equal(Good.NickelIron, freighter.CargoGood);
            Assert.True(freighter.CargoTonnes > 0);
            Assert.Equal(destination, freighter.CargoDestination);
            Assert.Equal(inboundBefore + freighter.CargoTonnes, destination.Inbound[(int)Good.NickelIron], 6);
            Assert.Equal(Economy.CargoText(freighter), freighter.Cargo);
            Assert.True(freighter.CargoCost > 0);
        }

        [Fact]
        public void PlanTradeRepositionsEmptyToWhereTheTradesAreWhenNothingPaysHere()
        {
            var economy = new Economy(new Diplomacy(), startJd: 1000);
            var owner = MakeFaction("Owner");
            var empty = MakePort(economy, "Empty", 1, owner);       // nothing to sell here
            var supplier = MakePort(economy, "Supplier", 2, owner); // has the goods
            var buyer = MakePort(economy, "Buyer", 3, owner);       // wants them
            SetGood(supplier, Good.NickelIron, stock: 1_000_000, demand: 0);
            SetGood(buyer, Good.NickelIron, demand: 5000);
            var freighter = MakeShip(MakeFaction("Trader"), ShipClass.Freighter);

            int to = economy.PlanTrade(freighter, here: 1, jd: 1000);

            Assert.Equal(2, to); // reposition to the supplier, not straight to the buyer
            Assert.False(freighter.HasCargo);
            Assert.Equal("Empty, repositioning to " + supplier.Name, freighter.Cargo);
        }

        [Fact]
        public void PlanTradeReturnsNegativeOneWhenNothingIsWorthDoingAnywhere()
        {
            var economy = new Economy(new Diplomacy(), startJd: 1000);
            var owner = MakeFaction("Owner");
            MakePort(economy, "Origin", 1, owner); // no stock, no demand anywhere
            MakePort(economy, "Other", 2, owner);
            var freighter = MakeShip(MakeFaction("Trader"), ShipClass.Freighter);

            int to = economy.PlanTrade(freighter, here: 1, jd: 1000);

            Assert.Equal(-1, to);
            Assert.False(freighter.HasCargo);
            Assert.Null(freighter.Cargo);
        }

        [Fact]
        public void PlanTradeDiscountsDestinationsByDangerAndCanExcludeAnUnsafeOneEntirely()
        {
            var economy = new Economy(new Diplomacy(), startJd: 1000);
            var owner = MakeFaction("Owner");
            var origin = MakePort(economy, "Origin", 1, owner);
            var dangerous = MakePort(economy, "Dangerous", 2, owner);
            var safe = MakePort(economy, "Safe", 3, owner);
            SetGood(origin, Good.NickelIron, stock: 1_000_000, demand: 0);
            // Identical trades at both destinations: only the danger discount should tell them apart.
            SetGood(dangerous, Good.NickelIron, stock: 90000, demand: 5000);
            SetGood(safe, Good.NickelIron, stock: 90000, demand: 5000);
            economy.Danger = body => body == dangerous.Body ? 0.9 : 0;
            var freighter = MakeShip(MakeFaction("Trader"), ShipClass.Freighter);

            int to = economy.PlanTrade(freighter, here: 1, jd: 1000);

            Assert.Equal(3, to);
            Assert.Equal(safe, freighter.CargoDestination);
        }

        [Fact]
        public void PlanTradePaysAnEssentialShortageContractBonusAtTheDestination()
        {
            var economy = new Economy(new Diplomacy(), startJd: 1000);
            var origin = MakePort(economy, "Origin", 1, MakeFaction("Owner"));
            var destOwner = MakeFaction("DestOwner", treasury: 10_000_000);
            var destination = MakePort(economy, "Destination", 2, destOwner);
            SetGood(origin, Good.Food, stock: 1_000_000, demand: 0);
            SetGood(destination, Good.Food, stock: 0, demand: 1000); // well under 70% of target: a shortage
            var freighter = MakeShip(MakeFaction("Trader"), ShipClass.Freighter);

            int to = economy.PlanTrade(freighter, here: 1, jd: 1000);

            Assert.Equal(2, to);
            double expectedBonus = 600000 + freighter.CargoTonnes * GoodInfo.Of(Good.Food).BasePrice * 1.5;
            Assert.Equal(expectedBonus, freighter.CargoBonus, 2);
        }

        [Fact]
        public void PlanTradeWithCargoAlreadyLoadedReroutesToWhereItSellsBestAndRebooksInbound()
        {
            var economy = new Economy(new Diplomacy(), startJd: 1000);
            var owner = MakeFaction("Owner");
            var oldDestination = MakePort(economy, "OldDestination", 1, owner); // nobody here wants it anymore
            var worse = MakePort(economy, "Worse", 2, owner);
            var better = MakePort(economy, "Better", 3, owner);
            SetGood(worse, Good.NickelIron, stock: 1_000_000, demand: 100);   // glutted: floor price
            SetGood(better, Good.NickelIron, stock: 0, demand: 50000);       // scarce: high price
            var freighter = MakeShip(MakeFaction("Trader"), ShipClass.Freighter);
            freighter.HasCargo = true;
            freighter.CargoGood = Good.NickelIron;
            freighter.CargoTonnes = 200;
            freighter.CargoCost = 10000;
            freighter.CargoBonus = 777; // stale bonus from the previous leg -- must be cleared, not carried forward
            freighter.CargoDestination = oldDestination;
            oldDestination.Inbound[(int)Good.NickelIron] = 200; // booked from before

            int to = economy.PlanTrade(freighter, here: 1, jd: 1000);

            Assert.Equal(3, to); // the scarcer, higher-paying market
            Assert.Equal(better, freighter.CargoDestination);
            Assert.Equal(0, oldDestination.Inbound[(int)Good.NickelIron], 6); // released
            Assert.Equal(200, better.Inbound[(int)Good.NickelIron], 6);      // rebooked
            Assert.Equal(0, freighter.CargoBonus);
        }

        // ---------------------------------------------------------------- OnArrive

        [Fact]
        public void OnArriveSellsAtTheHighestWillingMarketAtTheBody()
        {
            var economy = new Economy(new Diplomacy(), startJd: 1000);
            var owner = MakeFaction("Owner");
            var cheapBuyer = MakePort(economy, "Cheap", 5, owner);
            var richBuyer = MakePort(economy, "Rich", 5, owner);
            SetGood(cheapBuyer, Good.NickelIron, stock: 95000, demand: 5000); // near target: modest price
            SetGood(richBuyer, Good.NickelIron, stock: 0, demand: 50000);     // scarce: high price
            var ship = LoadedShip(MakeFaction("Trader"), ShipClass.Freighter, Good.NickelIron, tonnes: 200, cost: 100);

            economy.OnArrive(ship, body: 5, jd: 1000);

            Assert.False(ship.HasCargo);
            Assert.Equal(200, richBuyer.Stock[(int)Good.NickelIron], 6);
            Assert.Equal(95000, cheapBuyer.Stock[(int)Good.NickelIron], 6);
        }

        [Fact]
        public void OnArrivePrefersTheBookedDestinationWithinA5PercentMargin()
        {
            var economy = new Economy(new Diplomacy(), startJd: 1000);
            var owner = MakeFaction("Owner");
            var booked = MakePort(economy, "Booked", 5, owner);
            var other = MakePort(economy, "Other", 5, owner);
            SetGood(booked, Good.Food, stock: 4000, demand: 1000);
            SetGood(other, Good.Food, stock: 3800, demand: 1000); // pays a little more raw, but within 5%
            const double tonnes = 100;
            double bookedRaw = booked.SaleValue(Good.Food, tonnes);
            double otherRaw = other.SaleValue(Good.Food, tonnes);
            Assert.True(otherRaw > bookedRaw && otherRaw < bookedRaw * 1.05, "setup must keep the raw gap inside the 5% preference band");
            var ship = LoadedShip(MakeFaction("Trader"), ShipClass.Freighter, Good.Food, tonnes, cost: 100, booked);

            economy.OnArrive(ship, body: 5, jd: 1000);

            Assert.Equal(tonnes, booked.Stock[(int)Good.Food] - 4000, 6);
            Assert.Equal(0, other.Stock[(int)Good.Food] - 3800, 6);
        }

        [Fact]
        public void OnArriveLosesTheBookedDestinationWhenAnotherMarketPaysMoreThan5PercentBetter()
        {
            var economy = new Economy(new Diplomacy(), startJd: 1000);
            var owner = MakeFaction("Owner");
            var booked = MakePort(economy, "Booked", 5, owner);
            var other = MakePort(economy, "Other", 5, owner);
            SetGood(booked, Good.Food, stock: 4000, demand: 1000);
            SetGood(other, Good.Food, stock: 500, demand: 1000); // much scarcer: far more than 5% better
            const double tonnes = 100;
            double bookedRaw = booked.SaleValue(Good.Food, tonnes);
            double otherRaw = other.SaleValue(Good.Food, tonnes);
            Assert.True(otherRaw > bookedRaw * 1.05, "setup must put the raw gap outside the 5% preference band");
            var ship = LoadedShip(MakeFaction("Trader"), ShipClass.Freighter, Good.Food, tonnes, cost: 100, booked);

            economy.OnArrive(ship, body: 5, jd: 1000);

            Assert.Equal(tonnes, other.Stock[(int)Good.Food] - 500, 6);
            Assert.Equal(0, booked.Stock[(int)Good.Food] - 4000, 6);
        }

        [Fact]
        public void OnArriveAppliesPortTaxToTheSettlementOwnerOnSale()
        {
            var economy = new Economy(new Diplomacy(), startJd: 1000);
            var portOwner = MakeFaction("PortOwner");
            var traderFaction = MakeFaction("Trader");
            var buyer = MakePort(economy, "Buyer", 5, portOwner);
            SetGood(buyer, Good.NickelIron, stock: 1000, demand: 500);
            const double tonnes = 200;
            double expectedValue = buyer.SaleValue(Good.NickelIron, tonnes);
            double expectedTax = expectedValue * Economy.PortTax;
            double ownerTreasuryBefore = portOwner.Treasury;
            var ship = LoadedShip(traderFaction, ShipClass.Freighter, Good.NickelIron, tonnes, cost: 100);

            economy.OnArrive(ship, body: 5, jd: 1000);

            Assert.Equal(ownerTreasuryBefore + expectedTax, portOwner.Treasury, 4);
        }

        [Fact]
        public void OnArriveSettlesBonusCreditsAndTreasuryAndClearsCargoWhenSellingAtTheBookedDestination()
        {
            var economy = new Economy(new Diplomacy(), startJd: 1000);
            var portOwner = MakeFaction("PortOwner", treasury: 10_000_000);
            var traderFaction = MakeFaction("Trader");
            var destination = MakePort(economy, "Dest", 5, portOwner);
            SetGood(destination, Good.Food, stock: 4000, demand: 1000);
            const double tonnes = 500, cargoCost = 20000, bonus = 50000;
            var ship = LoadedShip(traderFaction, ShipClass.Freighter, Good.Food, tonnes, cargoCost, destination, bonus);
            double rawValue = destination.SaleValue(Good.Food, tonnes);
            double bestValue = rawValue * 1.05; // the booked-destination preference
            double tax = bestValue * Economy.PortTax;
            double finalValue = bestValue + bonus;
            double expectedProfit = finalValue - tax - cargoCost;
            double stockBefore = destination.Stock[(int)Good.Food];
            double ownerTreasuryBefore = portOwner.Treasury;
            double traderTreasuryBefore = traderFaction.Treasury;

            economy.OnArrive(ship, body: 5, jd: 1000);

            Assert.Equal(stockBefore + tonnes, destination.Stock[(int)Good.Food], 6);
            Assert.Equal(ownerTreasuryBefore + tax - bonus, portOwner.Treasury, 4);
            Assert.Equal(traderTreasuryBefore + finalValue - tax, traderFaction.Treasury, 4);
            Assert.Equal(expectedProfit, ship.Credits, 4);
            Assert.False(ship.HasCargo);
            Assert.Equal(0, ship.CargoTonnes);
            Assert.Equal(0, ship.CargoCost);
            Assert.Equal(0, ship.CargoBonus);
            Assert.Null(ship.CargoDestination);
            Assert.Null(ship.Cargo);
        }

        [Fact]
        public void OnArriveCarriesTheCargoOnWhenNobodyAtTheBodyWillBuyButReleasesTheOldBooking()
        {
            var economy = new Economy(new Diplomacy(), startJd: 1000);
            var owner = MakeFaction("Owner");
            var bookedDestination = MakePort(economy, "Booked", 9, owner);
            SetGood(bookedDestination, Good.Food, demand: 1000); // wants it, but the ship isn't there
            var here = MakePort(economy, "NoBuyersHere", 5, owner);
            SetGood(here, Good.Food, demand: 0); // nobody here wants Food
            var ship = LoadedShip(MakeFaction("Trader"), ShipClass.Freighter, Good.Food, tonnes: 500, cost: 1000, bookedDestination);
            bookedDestination.Inbound[(int)Good.Food] = 500;

            economy.OnArrive(ship, body: 5, jd: 1000);

            Assert.True(ship.HasCargo);
            Assert.Equal(500, ship.CargoTonnes);
            Assert.Null(ship.CargoDestination);
            Assert.Equal(0, bookedDestination.Inbound[(int)Good.Food], 6);
        }

        [Fact]
        public void OnArriveSkipsAPortDiplomacyForbidsEvenIfItWouldPayBest()
        {
            var diplomacy = new Diplomacy();
            var economy = new Economy(diplomacy, startJd: 1000);
            var traderFaction = MakeFaction("Trader");
            var enemyOwner = MakeFaction("Enemy");
            diplomacy.Set(traderFaction, enemyOwner, Relation.War);
            var forbidden = MakePort(economy, "Forbidden", 5, enemyOwner);
            var allowed = MakePort(economy, "Allowed", 5, MakeFaction("Friend"));
            SetGood(forbidden, Good.NickelIron, stock: 0, demand: 50000);     // would pay far more
            SetGood(allowed, Good.NickelIron, stock: 95000, demand: 5000);   // pays modestly
            var ship = LoadedShip(traderFaction, ShipClass.Freighter, Good.NickelIron, tonnes: 200, cost: 100);

            economy.OnArrive(ship, body: 5, jd: 1000);

            Assert.False(ship.HasCargo);
            Assert.Equal(95200, allowed.Stock[(int)Good.NickelIron], 6);
            Assert.Equal(0, forbidden.Stock[(int)Good.NickelIron], 6);
        }

        [Fact]
        public void OnArriveIsANoOpWhenTheShipHasNoCargo()
        {
            var economy = new Economy(new Diplomacy(), startJd: 1000);
            var port = MakePort(economy, "Port", 5, MakeFaction("Owner"));
            SetGood(port, Good.NickelIron, stock: 1000, demand: 500);
            var ship = MakeShip(MakeFaction("Trader"), ShipClass.Freighter); // HasCargo = false

            economy.OnArrive(ship, body: 5, jd: 1000);

            Assert.Equal(1000, port.Stock[(int)Good.NickelIron]);
            Assert.False(ship.HasCargo);
        }
    }
}
