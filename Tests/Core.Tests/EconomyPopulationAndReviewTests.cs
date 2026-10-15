using System;
using System.Collections.Generic;
using SolarSystem.Core;
using Xunit;

namespace SolarSystem.Core.Tests
{
    /// <summary>
    /// Covers USS-T32: <see cref="Economy.Consume"/> (feeding people and growing or shrinking
    /// population), <see cref="Economy.Fence"/> (haven smuggling), the 10-day <see cref="Economy.Review"/>
    /// (facility level drift and shipyard builds) and <see cref="Economy.Capture"/>. Settlements are
    /// built directly (no <see cref="Economy.Initialise"/>) so each test starts from known stock and
    /// population, isolated from trading and the production tick where that is not under test.
    /// </summary>
    public class EconomyPopulationAndReviewTests
    {
        /// <summary>Stock large enough that a tick never meaningfully depletes it.</summary>
        const double Ample = 1e9;

        static (Economy economy, Settlement settlement, Faction owner) CreateMarket(
            double startJd = 0, double population = 0, SettlementKind kind = SettlementKind.Colony)
        {
            var economy = new Economy(new Diplomacy(), startJd);
            var owner = new Faction("Owner", "OWN", default);
            var settlement = economy.Add("Test", body: 1, kind, owner, population);
            return (economy, settlement, owner);
        }

        static Facility AddFacility(Settlement s, IndustryKind kind, double level = 1)
        {
            var facility = new Facility(Recipe.Of(kind), level);
            s.Facilities.Add(facility);
            return facility;
        }

        static Facility AddMine(Settlement s, Good ore, double perDay, double level = 1)
        {
            var facility = new Facility(Recipe.Mine(ore, perDay), level);
            s.Facilities.Add(facility);
            return facility;
        }

        static void StockAllNeeds(Settlement s, double amount)
        {
            foreach (var (g, _) in Settlement.PersonNeeds) s.Stock[(int)g] = amount;
        }

        // ---------------------------------------------------------------- Consume: satisfaction

        [Fact]
        public void Consume_PartialStock_SatisfactionIsShareOfNeedMet()
        {
            var (economy, s, _) = CreateMarket(population: 100000);
            double dt = 0.1;
            StockAllNeeds(s, Ample);
            double needFood = Settlement.PersonNeeds[0].perPersonDay * s.Population * dt;
            Assert.Equal(Good.Food, Settlement.PersonNeeds[0].good);
            s.Stock[(int)Good.Food] = needFood * 0.5; // only half of what the colony needs this tick

            economy.AdvanceTo(dt);

            Assert.Equal(0.5, s.Satisfaction[(int)Good.Food], 6);
            Assert.Equal(0, s.Stock[(int)Good.Food], 9); // everything on hand was eaten
            Assert.Equal(1.0, s.Satisfaction[(int)Good.Water], 9); // ample, untouched by the shortage
            Assert.Equal(1.0, s.Satisfaction[(int)Good.Alloys], 9); // not a person need: always 1
        }

        [Fact]
        public void Consume_NoNeedThisTick_SatisfactionStaysAtOne()
        {
            var (economy, s, _) = CreateMarket(population: 0); // nobody to feed
            s.Stock[(int)Good.Food] = 0;

            economy.AdvanceTo(0.1);

            Assert.Equal(1.0, s.Satisfaction[(int)Good.Food], 9);
        }

        // ---------------------------------------------------------------- Consume: population growth

        [Fact]
        public void Consume_FedWateredAndMedicated_GrowsAtTheFullFourPercentAnnualRate()
        {
            var (economy, s, _) = CreateMarket(population: 100000);
            double dt = 0.1;
            StockAllNeeds(s, Ample);
            double initial = s.Population;

            economy.AdvanceTo(dt);

            double expectedRate = 0.00011; // health = 1: 0.00011 * (0.5 + 0.5) - 0
            double expected = initial * (1 + expectedRate * dt);
            Assert.Equal(expected, s.Population, 3);
            Assert.True(s.Population > initial);
            // Roughly 4%/year: 0.00011 * 365 ~= 0.0402.
            Assert.InRange(expectedRate * 365, 0.039, 0.041);
        }

        [Fact]
        public void Consume_FedAndWateredButNoMedicine_SlowlyDeclines()
        {
            var (economy, s, _) = CreateMarket(population: 100000);
            double dt = 0.1;
            StockAllNeeds(s, Ample);
            s.Stock[(int)Good.MedicalSupplies] = 0;
            double initial = s.Population;

            economy.AdvanceTo(dt);

            double expectedRate = 0.00011 * 0.5 - 0.0002; // health = 0
            double expected = initial * (1 + expectedRate * dt);
            Assert.Equal(expected, s.Population, 3);
            Assert.True(s.Population < initial);
            // Roughly a 5%/year decline: 0.000145 * 365 ~= 0.053.
            Assert.InRange(-expectedRate * 365, 0.05, 0.056);
        }

        [Fact]
        public void Consume_Starving_ShrinksMuchFasterThanMissingMedicineAlone()
        {
            var (economy, s, _) = CreateMarket(population: 100000);
            double dt = 0.1;
            StockAllNeeds(s, Ample);
            s.Stock[(int)Good.Food] = 0; // starving: sustenance drops well below 0.98
            double initial = s.Population;

            economy.AdvanceTo(dt);

            Assert.True(s.Satisfaction[(int)Good.Food] < 0.98);
            double expectedRate = (0 - 0.98) * 0.004; // sustenance = 0 (no food at all this tick)
            double expected = initial * (1 + expectedRate * dt);
            Assert.Equal(expected, s.Population, 3);
            double starvingDrop = initial - s.Population;

            // Compare against the no-medicine decline from the same starting population.
            var (economy2, s2, _) = CreateMarket(population: 100000);
            StockAllNeeds(s2, Ample);
            s2.Stock[(int)Good.MedicalSupplies] = 0;
            economy2.AdvanceTo(dt);
            double noMedicineDrop = initial - s2.Population;

            Assert.True(starvingDrop > noMedicineDrop * 10, "hunger should shrink population far faster than missing medicine alone");
        }

        [Fact]
        public void Consume_PopulationNeverFallsBelowAQuarterOfInitial()
        {
            var (economy, s, _) = CreateMarket(population: 100000);
            s.Stock[(int)Good.Water] = Ample;
            s.Stock[(int)Good.MedicalSupplies] = Ample;
            s.Stock[(int)Good.HeliumThree] = Ample;
            s.Stock[(int)Good.Machinery] = Ample;
            s.Stock[(int)Good.Electronics] = Ample;
            s.Stock[(int)Good.Food] = 0; // starved for the entire run; never replenished

            economy.AdvanceTo(5000);

            Assert.Equal(s.InitialPopulation * 0.25, s.Population, 3);
        }

        [Fact]
        public void Consume_PopulationNeverRisesAboveFourTimesInitial()
        {
            var (economy, s, _) = CreateMarket(population: 100000);
            StockAllNeeds(s, Ample);

            economy.AdvanceTo(20000); // far more than enough years at ~4%/year to hit the cap

            Assert.Equal(s.InitialPopulation * 4, s.Population, 3);
        }

        // ---------------------------------------------------------------- Fence (haven smuggling)

        [Fact]
        public void Fence_DrainsStockAboveTargetAndPaysOwnerHalfBasePrice()
        {
            var (economy, s, owner) = CreateMarket(kind: SettlementKind.Haven);
            double dt = 0.1;
            s.ExtraDemand[(int)Good.Food] = 100; // gives Food a nonzero Target to be "above"
            double target = s.ExtraDemand[(int)Good.Food] * Settlement.CoverDays;
            s.Stock[(int)Good.Food] = target + 1000;
            owner.Treasury = 0;

            economy.AdvanceTo(dt);

            double expectedShare = 1 - Math.Exp(-0.03 * dt);
            double expectedMoved = 1000 * expectedShare;
            Assert.Equal(target + 1000 - expectedMoved, s.Stock[(int)Good.Food], 6);
            Assert.Equal(expectedMoved * GoodInfo.Of(Good.Food).BasePrice * 0.5, owner.Treasury, 6);
        }

        [Fact]
        public void Fence_StockAtOrBelowTarget_DoesNotDrainOrPay()
        {
            var (economy, s, owner) = CreateMarket(kind: SettlementKind.Haven);
            s.ExtraDemand[(int)Good.Food] = 100;
            double target = s.ExtraDemand[(int)Good.Food] * Settlement.CoverDays;
            s.Stock[(int)Good.Food] = target; // exactly at target: nothing to smuggle
            owner.Treasury = 0;

            economy.AdvanceTo(0.1);

            Assert.Equal(target, s.Stock[(int)Good.Food], 6);
            Assert.Equal(0, owner.Treasury, 9);
        }

        [Fact]
        public void Fence_NonHavenSettlements_NeverFence()
        {
            var (economy, s, owner) = CreateMarket(kind: SettlementKind.Colony);
            s.ExtraDemand[(int)Good.Food] = 100;
            double target = s.ExtraDemand[(int)Good.Food] * Settlement.CoverDays;
            s.Stock[(int)Good.Food] = target + 1000;
            owner.Treasury = 0;

            economy.AdvanceTo(0.1);

            Assert.Equal(target + 1000, s.Stock[(int)Good.Food], 6);
            Assert.Equal(0, owner.Treasury, 9);
        }

        // ---------------------------------------------------------------- Review: facility level drift

        [Fact]
        public void Review_HighMarginAndUtilisation_GrowsFacilityLevelByThreePercent()
        {
            var (economy, s, _) = CreateMarket();
            var f = AddMine(s, Good.Platinum, 1000); // trivial inputs, a very high-value output: huge margin
            s.Stock[(int)Good.Machinery] = Ample;
            s.Stock[(int)Good.HeliumThree] = Ample;

            economy.AdvanceTo(10); // exactly one 10-day review cycle

            Assert.Equal(f.BaseLevel * 1.03, f.Level, 6);
        }

        [Fact]
        public void Review_GlutStockFullWarehouseZerosUtilisation_ShrinksFacilityLevelByTwoPercent()
        {
            var (economy, s, _) = CreateMarket();
            var f = AddFacility(s, IndustryKind.Smelter);
            s.Stock[(int)Good.NickelIron] = Ample;
            s.Stock[(int)Good.Titanium] = Ample;
            s.Stock[(int)Good.HeliumThree] = Ample;
            double capacity = 800 * Settlement.StorageDays; // this facility's own output rate drives capacity
            s.Stock[(int)Good.Alloys] = capacity; // warehouse already full: nowhere for output to go

            economy.AdvanceTo(10);

            Assert.Equal(f.BaseLevel * 0.98, f.Level, 6);
        }

        [Fact]
        public void Review_SustainedHighMargin_LevelClampsAtThreeTimesBaseLevel()
        {
            var (economy, s, _) = CreateMarket();
            var f = AddMine(s, Good.Platinum, 1000);
            s.Stock[(int)Good.Machinery] = Ample;
            s.Stock[(int)Good.HeliumThree] = Ample;
            s.ExtraDemand[(int)Good.Platinum] = Ample; // a sink, so the warehouse never fills as Level grows

            economy.AdvanceTo(1000); // many review cycles: far more than needed to reach the cap

            Assert.Equal(f.BaseLevel * 3, f.Level, 6);
        }

        [Fact]
        public void Review_SustainedGlut_LevelClampsAtFourTenthsOfBaseLevel()
        {
            var (economy, s, _) = CreateMarket();
            var f = AddFacility(s, IndustryKind.Smelter);
            s.Stock[(int)Good.NickelIron] = Ample;
            s.Stock[(int)Good.Titanium] = Ample;
            s.Stock[(int)Good.HeliumThree] = Ample;
            s.Stock[(int)Good.Alloys] = 800 * Settlement.StorageDays;

            economy.AdvanceTo(1000); // many review cycles: far more than needed to reach the floor

            Assert.Equal(f.BaseLevel * 0.4, f.Level, 6);
        }

        [Fact]
        public void Review_ShipyardFacility_IsNeverGrownOrShrunkByTheLevelDriftRule()
        {
            var (economy, s, owner) = CreateMarket();
            var yard = AddFacility(s, IndustryKind.Shipyard);
            owner.Treasury = 0; // no money to build, so only the level-drift rule could move Level

            economy.AdvanceTo(10);

            Assert.Equal(yard.BaseLevel, yard.Level, 9);
        }

        // ---------------------------------------------------------------- Review: shipyard builds

        [Fact]
        public void Review_ChooseBuildReturnsNull_NeverBuilds()
        {
            var (economy, s, owner) = CreateMarket();
            AddFacility(s, IndustryKind.Shipyard);
            s.BuildPoints = 400000; // at the Produce-tick cap, so it is not itself disturbed by the tick
            owner.Treasury = 1e12;
            economy.ChooseBuild = _ => null;
            bool built = false;
            economy.ShipBuilt += (_, __, ___) => built = true;

            economy.AdvanceTo(10);

            Assert.False(built);
            Assert.Null(s.OnSlip);
            Assert.Equal(400000, s.BuildPoints, 3);
            Assert.Equal(1e12, owner.Treasury, 3);
        }

        [Fact]
        public void Review_BuildPointsBelowHullCost_DoesNotBuildButStillTracksTheSlip()
        {
            var (economy, s, owner) = CreateMarket();
            AddFacility(s, IndustryKind.Shipyard);
            s.BuildPoints = Economy.HullCost(ShipClass.Freighter) - 1;
            owner.Treasury = 1e12;
            economy.ChooseBuild = _ => ShipClass.Freighter;
            bool built = false;
            economy.ShipBuilt += (_, __, ___) => built = true;

            economy.AdvanceTo(10);

            Assert.False(built);
            Assert.Equal(ShipClass.Freighter, s.OnSlip);
            Assert.Equal(Economy.HullCost(ShipClass.Freighter) - 1, s.BuildPoints, 3);
        }

        [Fact]
        public void Review_TreasuryBelowHullPrice_DoesNotBuildOrSpendBuildPoints()
        {
            var (economy, s, owner) = CreateMarket();
            AddFacility(s, IndustryKind.Shipyard);
            double cost = Economy.HullCost(ShipClass.Freighter);
            s.BuildPoints = cost + 1000;
            owner.Treasury = Economy.HullPrice(ShipClass.Freighter) - 1;
            economy.ChooseBuild = _ => ShipClass.Freighter;
            bool built = false;
            economy.ShipBuilt += (_, __, ___) => built = true;

            economy.AdvanceTo(10);

            Assert.False(built);
            Assert.Equal(cost + 1000, s.BuildPoints, 3); // untouched: points are only spent once the build commits
        }

        [Fact]
        public void Review_PointsAndTreasurySuffice_BuildsSpendsBothAndFiresShipBuilt()
        {
            var (economy, s, owner) = CreateMarket(startJd: 1000);
            AddFacility(s, IndustryKind.Shipyard);
            double cost = Economy.HullCost(ShipClass.Freighter);
            double price = Economy.HullPrice(ShipClass.Freighter);
            s.BuildPoints = cost + 500;
            owner.Treasury = price + 2_000_000;
            economy.ChooseBuild = _ => ShipClass.Freighter;
            Settlement builtAt = null;
            ShipClass builtClass = default;
            double builtJd = -1;
            economy.ShipBuilt += (yard, cls, jd) => { builtAt = yard; builtClass = cls; builtJd = jd; };

            economy.AdvanceTo(1010); // one review cycle past startJd

            Assert.Same(s, builtAt);
            Assert.Equal(ShipClass.Freighter, builtClass);
            Assert.Equal(1010, builtJd, 6);
            Assert.Equal(ShipClass.Freighter, s.OnSlip);
            Assert.Equal(500, s.BuildPoints, 3);
            Assert.Equal(2_000_000, owner.Treasury, 3);
        }

        // ---------------------------------------------------------------- Capture

        [Fact]
        public void Capture_ChangesOwnerResetsDaysSinceCaptureAndKeepsThirtyPercentOfBuildPoints()
        {
            var (economy, s, originalOwner) = CreateMarket();
            s.DaysSinceCapture = 500;
            s.BuildPoints = 10000;
            var attacker = new Faction("Attacker", "ATK", default);

            economy.Capture(s, attacker);

            Assert.Same(attacker, s.Owner);
            Assert.NotSame(originalOwner, s.Owner);
            Assert.Equal(0, s.DaysSinceCapture, 9);
            Assert.Equal(3000, s.BuildPoints, 9);
        }
    }
}
