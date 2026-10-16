using System;
using SolarSystem.Core;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    /// <summary>Unit tests for the pricing and market-sizing math on <see cref="Settlement"/>, isolated
    /// from the rest of the simulation by setting Demand/Supply/Stock/Inbound directly.</summary>
    [TestFixture]
    public class SettlementPricingTests
    {
        const Good G = Good.Food;
        static readonly double BasePrice = GoodInfo.Of(G).BasePrice;

        static Settlement Create() => new Settlement();

        [Test]
        public void PriceAt_WhenTargetIsZero_ReturnsQuarterOfBasePrice()
        {
            var s = Create(); // Demand and Supply default to 0, so Flow and Target are both 0.
            Assert.That(s.PriceAt(G, 0), Is.EqualTo(BasePrice * 0.25));
            Assert.That(s.PriceAt(G, 500), Is.EqualTo(BasePrice * 0.25));
        }

        [Test]
        public void PriceAt_RisesAsStockFalls()
        {
            var s = Create();
            s.Demand[(int)G] = 10; // Flow = 10, Target = 10 * CoverDays
            double target = s.Target(G);

            double atTarget = s.PriceAt(G, target);
            double atHalf = s.PriceAt(G, target / 2);
            double atTenth = s.PriceAt(G, target / 10);

            Assert.That(atHalf > atTarget, Is.True, "price should rise as stock drops below target");
            Assert.That(atTenth > atHalf, Is.True, "price should keep rising as stock keeps dropping");

            double expectedAtTarget = BasePrice * Math.Pow(target / (target + 0.12 * target), 0.75);
            Assert.That(atTarget, Is.EqualTo(expectedAtTarget).Within(1e-9));
        }

        [Test]
        public void PriceAt_AppliesDiscount_WhenNoLocalDemand()
        {
            var withDemand = Create();
            withDemand.Demand[(int)G] = 10;
            var withoutDemand = Create();
            withoutDemand.Supply[(int)G] = 10; // same Flow, so same Target, but no local Demand

            double stock = withDemand.Target(G); // same Target for both, unclamped region
            double priceWithDemand = withDemand.PriceAt(G, stock);
            double priceWithoutDemand = withoutDemand.PriceAt(G, stock);

            Assert.That(priceWithoutDemand, Is.EqualTo(priceWithDemand * 0.6).Within(1e-9));
        }

        [Test]
        public void PriceAt_ClampsToLowerBound_WhenStockIsHugelyInExcess()
        {
            var s = Create();
            s.Demand[(int)G] = 10;
            double target = s.Target(G);

            double price = s.PriceAt(G, target * 50); // ratio tiny enough to breach the floor
            Assert.That(price, Is.EqualTo(BasePrice * 0.2).Within(1e-9));
        }

        [Test]
        public void PriceAt_StaysWithinClampBounds_AcrossStockLevels()
        {
            var s = Create();
            s.Demand[(int)G] = 10;
            double target = s.Target(G);

            foreach (double stock in new[] { 0, target / 100, target / 2, target, target * 5, target * 1000 })
            {
                double price = s.PriceAt(G, stock);
                Assert.That(price, Is.InRange(BasePrice * 0.2, BasePrice * 6));
            }
        }

        [Test]
        public void TargetAndCapacity_DeriveFromTheLargerOfDemandAndSupply()
        {
            var s = Create();
            s.Demand[(int)G] = 5;
            s.Supply[(int)G] = 12;

            Assert.That(s.Flow(G), Is.EqualTo(12));
            Assert.That(s.Target(G), Is.EqualTo(12 * Settlement.CoverDays));
            Assert.That(s.Capacity(G), Is.EqualTo(12 * Settlement.StorageDays));
        }

        [Test]
        public void Capacity_HasAFloorOfOne_WhenNothingFlows()
        {
            var s = Create(); // Demand and Supply both default to 0
            Assert.That(s.Flow(G), Is.EqualTo(0));
            Assert.That(s.Target(G), Is.EqualTo(0));
            Assert.That(s.Capacity(G), Is.EqualTo(1));
        }

        [Test]
        public void Available_ReservesThirtyFivePercentOfTarget_WhenDemandExists()
        {
            var s = Create();
            s.Demand[(int)G] = 10; // Target = 200, reserve = 70
            double reserve = s.Target(G) * 0.35;

            s.Stock[(int)G] = 150;
            Assert.That(s.Available(G), Is.EqualTo(150 - reserve));

            s.Stock[(int)G] = reserve - 20; // below the reserve
            Assert.That(s.Available(G), Is.EqualTo(0));
        }

        [Test]
        public void Available_KeepsNoReserve_WhenThereIsNoLocalDemand()
        {
            var s = Create();
            s.Supply[(int)G] = 10; // Flow > 0, but Demand is 0
            s.Stock[(int)G] = 150;

            Assert.That(s.Available(G), Is.EqualTo(150));
        }

        [Test]
        public void Absorb_IsZero_WithoutLocalDemand()
        {
            var s = Create();
            s.Supply[(int)G] = 10;
            s.Stock[(int)G] = 1;
            s.Inbound[(int)G] = 1;

            Assert.That(s.Absorb(G), Is.EqualTo(0));
        }

        [Test]
        public void Absorb_SubtractsStockAndInbound_FromOneSixtyPercentOfTarget()
        {
            var s = Create();
            s.Demand[(int)G] = 10; // Target = 200, 1.6x = 320
            s.Stock[(int)G] = 100;
            s.Inbound[(int)G] = 50;

            Assert.That(s.Absorb(G), Is.EqualTo(320 - 100 - 50));
        }

        [Test]
        public void Absorb_ClampsToZero_WhenStockAndInboundAlreadyCoverIt()
        {
            var s = Create();
            s.Demand[(int)G] = 10; // Target = 200, 1.6x = 320
            s.Stock[(int)G] = 300;
            s.Inbound[(int)G] = 100; // sum exceeds 320

            Assert.That(s.Absorb(G), Is.EqualTo(0));
        }

        [Test]
        public void SaleValue_MatchesPriceTimesSpread_ForTinyQuantities()
        {
            var s = Create();
            s.Demand[(int)G] = 10; // Target = 200, well clear of either clamp at stock 1000
            s.Stock[(int)G] = 1000;

            double tonnes = 0.001;
            double expected = s.PriceAt(G, 1000) * tonnes * 0.97;
            double actual = s.SaleValue(G, tonnes);

            Assert.That(Math.Abs(actual - expected) < Math.Abs(expected) * 1e-3, Is.True, $"expected ~{expected}, got {actual}");
        }

        [Test]
        public void PurchaseCost_MatchesPriceTimesSpread_ForTinyQuantities()
        {
            var s = Create();
            s.Demand[(int)G] = 10;
            s.Stock[(int)G] = 1000;

            double tonnes = 0.001;
            double expected = s.PriceAt(G, 1000) * tonnes * 1.03;
            double actual = s.PurchaseCost(G, tonnes);

            Assert.That(Math.Abs(actual - expected) < Math.Abs(expected) * 1e-3, Is.True, $"expected ~{expected}, got {actual}");
        }

        [Test]
        public void SaleValue_WalksPriceDownAsStockRises()
        {
            var s = Create();
            s.Demand[(int)G] = 10; // Target = 200
            double stock = 200, tonnes = 20; // sell 10% of the target stock
            s.Stock[(int)G] = stock;

            double priceBefore = s.PriceAt(G, stock);
            double priceAfter = s.PriceAt(G, stock + tonnes);
            double avgPricePaid = s.SaleValue(G, tonnes) / tonnes / 0.97;

            Assert.That(priceAfter < priceBefore, Is.True, "selling should be raising stock and lowering price");
            Assert.That(avgPricePaid, Is.InRange(priceAfter, priceBefore));
        }

        [Test]
        public void PurchaseCost_WalksPriceUpAsStockFalls()
        {
            var s = Create();
            s.Demand[(int)G] = 10; // Target = 200
            double stock = 200, tonnes = 20; // buy 10% of the target stock
            s.Stock[(int)G] = stock;

            double priceBefore = s.PriceAt(G, stock);
            double priceAfter = s.PriceAt(G, stock - tonnes);
            double avgPricePaid = s.PurchaseCost(G, tonnes) / tonnes / 1.03;

            Assert.That(priceAfter > priceBefore, Is.True, "buying should be lowering stock and raising price");
            Assert.That(avgPricePaid, Is.InRange(priceBefore, priceAfter));
        }

        [Test]
        public void SaleValueAndPurchaseCost_AreZero_ForZeroTonnes()
        {
            var s = Create();
            s.Demand[(int)G] = 10;
            s.Stock[(int)G] = 200;

            Assert.That(s.SaleValue(G, 0), Is.EqualTo(0));
            Assert.That(s.PurchaseCost(G, 0), Is.EqualTo(0));
        }
    }
}
