using System;
using SolarSystem.Core;
using Xunit;

namespace SolarSystem.Core.Tests
{
    /// <summary>Unit tests for the pricing and market-sizing math on <see cref="Settlement"/>, isolated
    /// from the rest of the simulation by setting Demand/Supply/Stock/Inbound directly.</summary>
    public class SettlementPricingTests
    {
        const Good G = Good.Food;
        static readonly double BasePrice = GoodInfo.Of(G).BasePrice;

        static Settlement Create() => new Settlement();

        [Fact]
        public void PriceAt_WhenTargetIsZero_ReturnsQuarterOfBasePrice()
        {
            var s = Create(); // Demand and Supply default to 0, so Flow and Target are both 0.
            Assert.Equal(BasePrice * 0.25, s.PriceAt(G, 0));
            Assert.Equal(BasePrice * 0.25, s.PriceAt(G, 500));
        }

        [Fact]
        public void PriceAt_RisesAsStockFalls()
        {
            var s = Create();
            s.Demand[(int)G] = 10; // Flow = 10, Target = 10 * CoverDays
            double target = s.Target(G);

            double atTarget = s.PriceAt(G, target);
            double atHalf = s.PriceAt(G, target / 2);
            double atTenth = s.PriceAt(G, target / 10);

            Assert.True(atHalf > atTarget, "price should rise as stock drops below target");
            Assert.True(atTenth > atHalf, "price should keep rising as stock keeps dropping");

            double expectedAtTarget = BasePrice * Math.Pow(target / (target + 0.12 * target), 0.75);
            Assert.Equal(expectedAtTarget, atTarget, 9);
        }

        [Fact]
        public void PriceAt_AppliesDiscount_WhenNoLocalDemand()
        {
            var withDemand = Create();
            withDemand.Demand[(int)G] = 10;
            var withoutDemand = Create();
            withoutDemand.Supply[(int)G] = 10; // same Flow, so same Target, but no local Demand

            double stock = withDemand.Target(G); // same Target for both, unclamped region
            double priceWithDemand = withDemand.PriceAt(G, stock);
            double priceWithoutDemand = withoutDemand.PriceAt(G, stock);

            Assert.Equal(priceWithDemand * 0.6, priceWithoutDemand, 9);
        }

        [Fact]
        public void PriceAt_ClampsToLowerBound_WhenStockIsHugelyInExcess()
        {
            var s = Create();
            s.Demand[(int)G] = 10;
            double target = s.Target(G);

            double price = s.PriceAt(G, target * 50); // ratio tiny enough to breach the floor
            Assert.Equal(BasePrice * 0.2, price, 9);
        }

        [Fact]
        public void PriceAt_StaysWithinClampBounds_AcrossStockLevels()
        {
            var s = Create();
            s.Demand[(int)G] = 10;
            double target = s.Target(G);

            foreach (double stock in new[] { 0, target / 100, target / 2, target, target * 5, target * 1000 })
            {
                double price = s.PriceAt(G, stock);
                Assert.InRange(price, BasePrice * 0.2, BasePrice * 6);
            }
        }

        [Fact]
        public void TargetAndCapacity_DeriveFromTheLargerOfDemandAndSupply()
        {
            var s = Create();
            s.Demand[(int)G] = 5;
            s.Supply[(int)G] = 12;

            Assert.Equal(12, s.Flow(G));
            Assert.Equal(12 * Settlement.CoverDays, s.Target(G));
            Assert.Equal(12 * Settlement.StorageDays, s.Capacity(G));
        }

        [Fact]
        public void Capacity_HasAFloorOfOne_WhenNothingFlows()
        {
            var s = Create(); // Demand and Supply both default to 0
            Assert.Equal(0, s.Flow(G));
            Assert.Equal(0, s.Target(G));
            Assert.Equal(1, s.Capacity(G));
        }

        [Fact]
        public void Available_ReservesThirtyFivePercentOfTarget_WhenDemandExists()
        {
            var s = Create();
            s.Demand[(int)G] = 10; // Target = 200, reserve = 70
            double reserve = s.Target(G) * 0.35;

            s.Stock[(int)G] = 150;
            Assert.Equal(150 - reserve, s.Available(G));

            s.Stock[(int)G] = reserve - 20; // below the reserve
            Assert.Equal(0, s.Available(G));
        }

        [Fact]
        public void Available_KeepsNoReserve_WhenThereIsNoLocalDemand()
        {
            var s = Create();
            s.Supply[(int)G] = 10; // Flow > 0, but Demand is 0
            s.Stock[(int)G] = 150;

            Assert.Equal(150, s.Available(G));
        }

        [Fact]
        public void Absorb_IsZero_WithoutLocalDemand()
        {
            var s = Create();
            s.Supply[(int)G] = 10;
            s.Stock[(int)G] = 1;
            s.Inbound[(int)G] = 1;

            Assert.Equal(0, s.Absorb(G));
        }

        [Fact]
        public void Absorb_SubtractsStockAndInbound_FromOneSixtyPercentOfTarget()
        {
            var s = Create();
            s.Demand[(int)G] = 10; // Target = 200, 1.6x = 320
            s.Stock[(int)G] = 100;
            s.Inbound[(int)G] = 50;

            Assert.Equal(320 - 100 - 50, s.Absorb(G));
        }

        [Fact]
        public void Absorb_ClampsToZero_WhenStockAndInboundAlreadyCoverIt()
        {
            var s = Create();
            s.Demand[(int)G] = 10; // Target = 200, 1.6x = 320
            s.Stock[(int)G] = 300;
            s.Inbound[(int)G] = 100; // sum exceeds 320

            Assert.Equal(0, s.Absorb(G));
        }

        [Fact]
        public void SaleValue_MatchesPriceTimesSpread_ForTinyQuantities()
        {
            var s = Create();
            s.Demand[(int)G] = 10; // Target = 200, well clear of either clamp at stock 1000
            s.Stock[(int)G] = 1000;

            double tonnes = 0.001;
            double expected = s.PriceAt(G, 1000) * tonnes * 0.97;
            double actual = s.SaleValue(G, tonnes);

            Assert.True(Math.Abs(actual - expected) < Math.Abs(expected) * 1e-3,
                $"expected ~{expected}, got {actual}");
        }

        [Fact]
        public void PurchaseCost_MatchesPriceTimesSpread_ForTinyQuantities()
        {
            var s = Create();
            s.Demand[(int)G] = 10;
            s.Stock[(int)G] = 1000;

            double tonnes = 0.001;
            double expected = s.PriceAt(G, 1000) * tonnes * 1.03;
            double actual = s.PurchaseCost(G, tonnes);

            Assert.True(Math.Abs(actual - expected) < Math.Abs(expected) * 1e-3,
                $"expected ~{expected}, got {actual}");
        }

        [Fact]
        public void SaleValue_WalksPriceDownAsStockRises()
        {
            var s = Create();
            s.Demand[(int)G] = 10; // Target = 200
            double stock = 200, tonnes = 20; // sell 10% of the target stock
            s.Stock[(int)G] = stock;

            double priceBefore = s.PriceAt(G, stock);
            double priceAfter = s.PriceAt(G, stock + tonnes);
            double avgPricePaid = s.SaleValue(G, tonnes) / tonnes / 0.97;

            Assert.True(priceAfter < priceBefore, "selling should be raising stock and lowering price");
            Assert.InRange(avgPricePaid, priceAfter, priceBefore);
        }

        [Fact]
        public void PurchaseCost_WalksPriceUpAsStockFalls()
        {
            var s = Create();
            s.Demand[(int)G] = 10; // Target = 200
            double stock = 200, tonnes = 20; // buy 10% of the target stock
            s.Stock[(int)G] = stock;

            double priceBefore = s.PriceAt(G, stock);
            double priceAfter = s.PriceAt(G, stock - tonnes);
            double avgPricePaid = s.PurchaseCost(G, tonnes) / tonnes / 1.03;

            Assert.True(priceAfter > priceBefore, "buying should be lowering stock and raising price");
            Assert.InRange(avgPricePaid, priceBefore, priceAfter);
        }

        [Fact]
        public void SaleValueAndPurchaseCost_AreZero_ForZeroTonnes()
        {
            var s = Create();
            s.Demand[(int)G] = 10;
            s.Stock[(int)G] = 200;

            Assert.Equal(0, s.SaleValue(G, 0));
            Assert.Equal(0, s.PurchaseCost(G, 0));
        }
    }
}
