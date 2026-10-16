using SolarSystem.Core;
using Xunit;

namespace SolarSystem.Core.Tests
{
    /// <summary>
    /// Covers U.SPC-T6's manual buy/sell: <see cref="Economy.BuyIntoHold"/> and
    /// <see cref="Economy.SellFromHold"/>, the Core-side operations behind the player's market UI.
    /// </summary>
    public class MarketTradeTests
    {
        // The player's faction and the port's owner are deliberately different factions (not just
        // different Faction instances with the same name) -- otherwise a port tax paid to the owner
        // and the same amount drawn from the buyer would land back in the one treasury and net to
        // zero tax, masking a real bug in the amount actually charged.
        static (Economy economy, Faction faction, Settlement settlement) CreateMarket(double stock = 10000, double demand = 10, double treasury = 1_000_000)
        {
            var economy = new Economy(new Diplomacy(), startJd: 0);
            var owner = new Faction("PortOwner", "OWN", default);
            var faction = new Faction("Testers", "TST", default);
            var settlement = economy.Add("Port", body: 1, SettlementKind.Station, owner, population: 0);
            settlement.Demand[(int)Good.Electronics] = demand;
            settlement.Stock[(int)Good.Electronics] = stock;
            settlement.RefreshPrices();
            faction.Treasury = treasury;
            return (economy, faction, settlement);
        }

        static Ship CreateShip(Faction faction, ShipClass cls = ShipClass.Freighter) => new Ship { Spec = ShipClassSpec.Of(cls), Faction = faction };

        [Fact]
        public void BuyingACrateMovesCreditsFromTheShipsFactionToTheSettlementsOwnerAtThePurchaseCostFormula()
        {
            var (economy, faction, settlement) = CreateMarket();
            var ship = CreateShip(faction);
            double unitTonnes = CargoFootprint.Of(Good.Electronics).Tonnes;
            double expectedBase = settlement.PurchaseCost(Good.Electronics, unitTonnes);
            double expectedTotal = expectedBase * (1 + Economy.PortTax);
            double ownerBefore = faction.Treasury;

            var outcome = economy.BuyIntoHold(ship, settlement, Good.Electronics, 1, out int bought);

            Assert.Equal(Economy.TradeOutcome.Ok, outcome);
            Assert.Equal(1, bought);
            Assert.Equal(ownerBefore - expectedTotal, faction.Treasury, 6);
            Assert.Equal(expectedBase * Economy.PortTax, settlement.Owner.Treasury, 6);
        }

        [Fact]
        public void BuyingACratePlacesItInThePersonalHoldNotTheAiSingleSlotFields()
        {
            var (economy, faction, settlement) = CreateMarket();
            var ship = CreateShip(faction);

            economy.BuyIntoHold(ship, settlement, Good.Electronics, 1, out int bought);

            Assert.Equal(1, bought);
            Assert.Single(ship.PersonalHold.Items);
            Assert.Equal(Good.Electronics, ship.PersonalHold.Items[0].Good);
            Assert.False(ship.HasCargo);
        }

        [Fact]
        public void BuyingACrateDrainsTheSettlementsStockByTheCratesTonnage()
        {
            var (economy, faction, settlement) = CreateMarket(stock: 500);
            var ship = CreateShip(faction);
            double unitTonnes = CargoFootprint.Of(Good.Electronics).Tonnes;

            economy.BuyIntoHold(ship, settlement, Good.Electronics, 1, out _);

            Assert.Equal(500 - unitTonnes, settlement.Stock[(int)Good.Electronics], 6);
        }

        [Fact]
        public void BuyingWithoutEnoughStockOnHandIsRejectedAndNothingChanges()
        {
            var (economy, faction, settlement) = CreateMarket(stock: 1, demand: 0);
            var ship = CreateShip(faction);
            double treasuryBefore = faction.Treasury, stockBefore = settlement.Stock[(int)Good.Electronics];

            var outcome = economy.BuyIntoHold(ship, settlement, Good.Electronics, 1, out int bought);

            Assert.Equal(Economy.TradeOutcome.NoStock, outcome);
            Assert.Equal(0, bought);
            Assert.Empty(ship.PersonalHold.Items);
            Assert.Equal(treasuryBefore, faction.Treasury);
            Assert.Equal(stockBefore, settlement.Stock[(int)Good.Electronics]);
        }

        [Fact]
        public void BuyingWithoutEnoughCreditsIsRejectedAndNothingChanges()
        {
            var (economy, faction, settlement) = CreateMarket(treasury: 0);
            var ship = CreateShip(faction);
            double stockBefore = settlement.Stock[(int)Good.Electronics];

            var outcome = economy.BuyIntoHold(ship, settlement, Good.Electronics, 1, out int bought);

            Assert.Equal(Economy.TradeOutcome.CantAfford, outcome);
            Assert.Equal(0, bought);
            Assert.Empty(ship.PersonalHold.Items);
            Assert.Equal(stockBefore, settlement.Stock[(int)Good.Electronics]);
        }

        [Fact]
        public void BuyingWhenTheHoldHasNoRoomLeftIsRejectedAndNothingChanges()
        {
            var (economy, faction, settlement) = CreateMarket();
            var ship = CreateShip(faction, ShipClass.Courier); // 3x3 hold
            for (int i = 0; i < ship.PersonalHold.Width * ship.PersonalHold.Height; i++)
                Assert.NotNull(ship.PersonalHold.AddFirstFit(Good.Electronics));
            double treasuryBefore = faction.Treasury, stockBefore = settlement.Stock[(int)Good.Electronics];

            var outcome = economy.BuyIntoHold(ship, settlement, Good.Electronics, 1, out int bought);

            Assert.Equal(Economy.TradeOutcome.NoRoom, outcome);
            Assert.Equal(0, bought);
            Assert.Equal(treasuryBefore, faction.Treasury);
            Assert.Equal(stockBefore, settlement.Stock[(int)Good.Electronics]);
        }

        [Fact]
        public void BuyingMoreCratesThanFitStopsAtTheHoldsLimitButKeepsWhatAlreadyWentThrough()
        {
            var (economy, faction, settlement) = CreateMarket();
            var ship = CreateShip(faction, ShipClass.Courier); // 3x3 = 9 cells
            int cells = ship.PersonalHold.Width * ship.PersonalHold.Height;
            for (int i = 0; i < cells - 2; i++) Assert.NotNull(ship.PersonalHold.AddFirstFit(Good.Electronics));

            var outcome = economy.BuyIntoHold(ship, settlement, Good.Electronics, 5, out int bought);

            Assert.Equal(Economy.TradeOutcome.Ok, outcome);
            Assert.Equal(2, bought);
            Assert.Equal(cells, ship.PersonalHold.Items.Count);
        }

        [Fact]
        public void BuyingWhereDiplomacyForbidsDockingIsRejectedAndNothingChanges()
        {
            var diplomacy = new Diplomacy();
            var economy = new Economy(diplomacy, startJd: 0);
            var owner = new Faction("Owner", "OWN", default);
            var raider = new Faction("Raider", "RAI", default);
            diplomacy.Set(owner, raider, Relation.War);
            var settlement = economy.Add("Port", body: 1, SettlementKind.Station, owner, population: 0);
            settlement.Demand[(int)Good.Electronics] = 10;
            settlement.Stock[(int)Good.Electronics] = 10000;
            settlement.RefreshPrices();
            raider.Treasury = 1_000_000;
            var ship = CreateShip(raider);

            var outcome = economy.BuyIntoHold(ship, settlement, Good.Electronics, 1, out int bought);

            Assert.Equal(Economy.TradeOutcome.CannotDock, outcome);
            Assert.Equal(0, bought);
            Assert.Empty(ship.PersonalHold.Items);
        }

        [Fact]
        public void BuyingZeroCratesIsANoOp()
        {
            var (economy, faction, settlement) = CreateMarket();
            var ship = CreateShip(faction);
            double treasuryBefore = faction.Treasury;

            var outcome = economy.BuyIntoHold(ship, settlement, Good.Electronics, 0, out int bought);

            Assert.Equal(Economy.TradeOutcome.Ok, outcome);
            Assert.Equal(0, bought);
            Assert.Equal(treasuryBefore, faction.Treasury);
        }

        [Fact]
        public void SellingACrateMovesCreditsFromTheSettlementsOwnerToTheShipsFactionAtTheSaleValueFormula()
        {
            var (economy, faction, settlement) = CreateMarket();
            var ship = CreateShip(faction);
            var item = ship.PersonalHold.AddFirstFit(Good.Electronics);
            double unitTonnes = item.Tonnes;
            double expectedValue = settlement.SaleValue(Good.Electronics, unitTonnes);
            double expectedTax = expectedValue * Economy.PortTax;
            double treasuryBefore = faction.Treasury, ownerBefore = settlement.Owner.Treasury;

            var outcome = economy.SellFromHold(ship, settlement, Good.Electronics, 1, out int sold);

            Assert.Equal(Economy.TradeOutcome.Ok, outcome);
            Assert.Equal(1, sold);
            Assert.Equal(treasuryBefore + expectedValue - expectedTax, faction.Treasury, 6);
            Assert.Equal(ownerBefore + expectedTax, settlement.Owner.Treasury, 6);
        }

        [Fact]
        public void SellingACrateRemovesItFromThePersonalHoldAndRestocksTheSettlement()
        {
            var (economy, faction, settlement) = CreateMarket();
            var ship = CreateShip(faction);
            var item = ship.PersonalHold.AddFirstFit(Good.Electronics);
            double unitTonnes = item.Tonnes;
            double stockBefore = settlement.Stock[(int)Good.Electronics];

            economy.SellFromHold(ship, settlement, Good.Electronics, 1, out int sold);

            Assert.Equal(1, sold);
            Assert.Empty(ship.PersonalHold.Items);
            Assert.Equal(stockBefore + unitTonnes, settlement.Stock[(int)Good.Electronics], 6);
        }

        [Fact]
        public void SellingWithNoneOfThatGoodInTheHoldIsRejected()
        {
            var (economy, faction, settlement) = CreateMarket();
            var ship = CreateShip(faction);

            var outcome = economy.SellFromHold(ship, settlement, Good.Electronics, 1, out int sold);

            Assert.Equal(Economy.TradeOutcome.NothingToSell, outcome);
            Assert.Equal(0, sold);
        }

        [Fact]
        public void SellingMoreCratesThanAreInTheHoldStopsAtWhatsThereButKeepsWhatAlreadySold()
        {
            var (economy, faction, settlement) = CreateMarket();
            var ship = CreateShip(faction);
            ship.PersonalHold.AddFirstFit(Good.Electronics);
            ship.PersonalHold.AddFirstFit(Good.Electronics);

            var outcome = economy.SellFromHold(ship, settlement, Good.Electronics, 5, out int sold);

            Assert.Equal(Economy.TradeOutcome.Ok, outcome);
            Assert.Equal(2, sold);
            Assert.Empty(ship.PersonalHold.Items);
        }

        [Fact]
        public void SellingWhereDiplomacyForbidsDockingIsRejectedAndTheCrateStays()
        {
            var diplomacy = new Diplomacy();
            var economy = new Economy(diplomacy, startJd: 0);
            var owner = new Faction("Owner", "OWN", default);
            var raider = new Faction("Raider", "RAI", default);
            diplomacy.Set(owner, raider, Relation.War);
            var settlement = economy.Add("Port", body: 1, SettlementKind.Station, owner, population: 0);
            settlement.Demand[(int)Good.Electronics] = 10;
            settlement.Stock[(int)Good.Electronics] = 10000;
            settlement.RefreshPrices();
            var ship = CreateShip(raider);
            ship.PersonalHold.AddFirstFit(Good.Electronics);

            var outcome = economy.SellFromHold(ship, settlement, Good.Electronics, 1, out int sold);

            Assert.Equal(Economy.TradeOutcome.CannotDock, outcome);
            Assert.Equal(0, sold);
            Assert.Single(ship.PersonalHold.Items);
        }
    }
}
