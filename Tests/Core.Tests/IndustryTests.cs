using System;
using SolarSystem.Core;
using Xunit;

namespace SolarSystem.Core.Tests
{
    /// <summary>
    /// Covers U.SPC-T31: the production tick in <see cref="Economy.AdvanceTo"/> / Produce, plus the
    /// <see cref="Recipe"/> and <see cref="Facility"/> types it drives. Facilities are attached
    /// directly to a bare <see cref="Settlement"/> (population 0 unless a test needs it) so each tick
    /// is isolated from trading, pricing reviews and population growth.
    /// </summary>
    public class IndustryTests
    {
        /// <summary>Stock large enough that a tick never meaningfully depletes it, so a test can hold
        /// one input fixed while varying another.</summary>
        const double Ample = 1e9;

        static (Economy economy, Settlement settlement) CreateMarket(double startJd = 0, double population = 0)
        {
            var economy = new Economy(new Diplomacy(), startJd);
            var owner = new Faction("Owner", "OWN", default);
            var settlement = economy.Add("Test", body: 1, SettlementKind.Colony, owner, population);
            return (economy, settlement);
        }

        static Facility AddFacility(Settlement s, IndustryKind kind, double level = 1)
        {
            var facility = new Facility(Recipe.Of(kind), level);
            s.Facilities.Add(facility);
            return facility;
        }

        // ---------------------------------------------------------------- Recipe.Limiting

        [Fact]
        public void RecipeLimiting_MarksInputsAtLeastTwoPercentOfTotalAsBulk()
        {
            // Farm: Water 700, Nitrogen 60, HeliumThree 0.4 -- total 760.4, 2% cutoff ~15.2.
            var recipe = Recipe.Of(IndustryKind.Farm);

            Assert.True(recipe.Limiting[0]); // Water: bulk feedstock
            Assert.True(recipe.Limiting[1]); // Nitrogen: bulk feedstock
            Assert.False(recipe.Limiting[2]); // HeliumThree: a trace consumable, below the cutoff
        }

        [Fact]
        public void RecipeLimiting_BoundaryAtExactlyTwoPercentCountsAsBulk()
        {
            // Total = 100; 2% of total = 2, exactly matching the second input's rate.
            var recipe = new Recipe(IndustryKind.MachineWorks, "Test",
                new[] { (Good.Alloys, 98.0), (Good.Copper, 2.0) }, new[] { (Good.Machinery, 1.0) });

            Assert.True(recipe.Limiting[0]);
            Assert.True(recipe.Limiting[1]); // the ">=" cutoff includes the boundary case
        }

        [Fact]
        public void RecipeLimiting_JustBelowTwoPercentIsMinor()
        {
            // Total = 100; the second input is 1% of it, just under the cutoff.
            var recipe = new Recipe(IndustryKind.MachineWorks, "Test",
                new[] { (Good.Alloys, 99.0), (Good.Copper, 1.0) }, new[] { (Good.Machinery, 1.0) });

            Assert.True(recipe.Limiting[0]);
            Assert.False(recipe.Limiting[1]);
        }

        [Fact]
        public void RecipeLimiting_IsNeverTrueForMineRecipes_EvenWhenInputsAreProportionallyBulk()
        {
            // Both of a Mine's inputs are well above the 2% cutoff by proportion (1.2 and 0.08 out of
            // 1.28 total), so if the Mine override were missing this would come back all-true.
            var recipe = Recipe.Mine(Good.NickelIron, 1000);

            Assert.All(recipe.Limiting, Assert.False);
        }

        // ---------------------------------------------------------------- Produce: inputs

        [Fact]
        public void Produce_ShortLimitingInput_CapsOutputProportionally()
        {
            var (economy, s) = CreateMarket();
            var f = AddFacility(s, IndustryKind.Smelter);
            double dt = 0.1;
            double needNickelIron = 2200 * dt; // level 1
            s.Stock[(int)Good.NickelIron] = needNickelIron * 0.4; // only 40% of what's needed
            s.Stock[(int)Good.Titanium] = Ample;
            s.Stock[(int)Good.HeliumThree] = Ample;
            s.ExtraDemand[(int)Good.Alloys] = Ample; // keep the warehouse from ever binding

            economy.AdvanceTo(dt);

            Assert.Equal(0.4, f.Utilisation, 9);
            Assert.Equal(800 * dt * 0.4, s.Stock[(int)Good.Alloys], 6);
            Assert.Equal(0, s.Stock[(int)Good.NickelIron], 6); // the short input is fully drained
        }

        [Fact]
        public void Produce_ShortMinorInput_LowersEfficiencyToTheFloor()
        {
            var (economy, s) = CreateMarket();
            var f = AddFacility(s, IndustryKind.Smelter);
            double dt = 0.1;
            s.Stock[(int)Good.NickelIron] = Ample;
            s.Stock[(int)Good.Titanium] = Ample;
            s.Stock[(int)Good.HeliumThree] = 0; // the minor input is entirely unavailable
            s.ExtraDemand[(int)Good.Alloys] = Ample;

            economy.AdvanceTo(dt);

            Assert.Equal(Recipe.MinorFloor, f.Utilisation, 9); // run stays 1; efficiency floors out
            Assert.Equal(800 * dt * Recipe.MinorFloor, s.Stock[(int)Good.Alloys], 6);
        }

        [Fact]
        public void Produce_PartiallyShortMinorInput_ScalesEfficiencyLinearlyAboveTheFloor()
        {
            var (economy, s) = CreateMarket();
            var f = AddFacility(s, IndustryKind.Smelter);
            double dt = 0.1;
            double needHeliumThree = 0.8 * dt;
            s.Stock[(int)Good.NickelIron] = Ample;
            s.Stock[(int)Good.Titanium] = Ample;
            s.Stock[(int)Good.HeliumThree] = needHeliumThree * 0.5; // half of what's needed
            s.ExtraDemand[(int)Good.Alloys] = Ample;

            economy.AdvanceTo(dt);

            double expected = Recipe.MinorFloor + (1 - Recipe.MinorFloor) * 0.5;
            Assert.Equal(expected, f.Utilisation, 9);
            Assert.InRange(f.Utilisation, Recipe.MinorFloor, 1.0);
        }

        [Fact]
        public void Produce_UtilisationIsRunTimesEfficiency()
        {
            var (economy, s) = CreateMarket();
            var f = AddFacility(s, IndustryKind.Smelter);
            double dt = 0.1;
            s.Stock[(int)Good.NickelIron] = 2200 * dt * 0.5; // limiting input at 50% -> run = 0.5
            s.Stock[(int)Good.Titanium] = Ample;
            s.Stock[(int)Good.HeliumThree] = 0; // minor input exhausted -> efficiency floors
            s.ExtraDemand[(int)Good.Alloys] = Ample;

            economy.AdvanceTo(dt);

            double expectedRun = 0.5, expectedEfficiency = Recipe.MinorFloor;
            Assert.Equal(expectedRun * expectedEfficiency, f.Utilisation, 9);
            Assert.Equal(800 * dt * expectedRun * expectedEfficiency, s.Stock[(int)Good.Alloys], 6);
        }

        // ---------------------------------------------------------------- Produce: capacity

        [Fact]
        public void Produce_FullWarehouse_ThrottlesOutputToExactlyFillCapacity()
        {
            var (economy, s) = CreateMarket();
            var f = AddFacility(s, IndustryKind.Smelter);
            double dt = 0.1;
            s.Stock[(int)Good.NickelIron] = Ample;
            s.Stock[(int)Good.Titanium] = Ample;
            s.Stock[(int)Good.HeliumThree] = Ample;
            // No ExtraDemand this time: capacity is driven purely by this facility's own output rate.
            double capacity = 800 * Settlement.StorageDays;
            s.Stock[(int)Good.Alloys] = capacity - 5; // only 5 tonnes of room left

            economy.AdvanceTo(dt);

            Assert.Equal(capacity, s.Stock[(int)Good.Alloys], 6);
            Assert.Equal(5.0 / (800 * dt), f.Utilisation, 9); // run throttled to room / uncapped output
        }

        // ---------------------------------------------------------------- Produce: capture disruption

        [Fact]
        public void Produce_FreshlyCapturedSettlement_RunsIndustryAtAQuarter()
        {
            var (economy, s) = CreateMarket();
            s.DaysSinceCapture = 0;
            var f = AddFacility(s, IndustryKind.Smelter);
            double dt = 0.1;
            s.Stock[(int)Good.NickelIron] = Ample;
            s.Stock[(int)Good.Titanium] = Ample;
            s.Stock[(int)Good.HeliumThree] = Ample;
            s.ExtraDemand[(int)Good.Alloys] = Ample;

            economy.AdvanceTo(dt);

            Assert.Equal(0.25, f.Utilisation, 9);
        }

        [Fact]
        public void Produce_IndustryRecoversGraduallyOverSixtyDaysAfterCapture()
        {
            var (economy, s) = CreateMarket();
            s.DaysSinceCapture = 0;
            var f = AddFacility(s, IndustryKind.Smelter);
            s.Stock[(int)Good.NickelIron] = Ample;
            s.Stock[(int)Good.Titanium] = Ample;
            s.Stock[(int)Good.HeliumThree] = Ample;
            s.ExtraDemand[(int)Good.Alloys] = Ample;

            economy.AdvanceTo(30); // halfway through the 60-day recovery

            double daysAtLastProduce = s.DaysSinceCapture - Economy.TickDays;
            double expected = Math.Min(1, 0.25 + daysAtLastProduce / 60.0);
            Assert.Equal(expected, f.Utilisation, 9);
            Assert.InRange(f.Utilisation, 0.26, 0.99); // recovering, not instantly fixed nor still at 25%

            economy.AdvanceTo(70); // past the 60-day recovery window

            Assert.Equal(1.0, f.Utilisation, 9);
        }

        [Fact]
        public void Produce_LifeSupportFacilities_IgnoreCaptureDisruptionAndMinorInputShortages()
        {
            var (economy, s) = CreateMarket();
            s.DaysSinceCapture = 0; // a non-lifeSupport facility would run at only 25% here
            var farm = AddFacility(s, IndustryKind.Farm);
            var waterMine = new Facility(Recipe.Mine(Good.Water, 1000), 1);
            s.Facilities.Add(waterMine);
            double dt = 0.1;
            // The farm's bulk (Limiting) inputs stay ample; only its minor HeliumThree feed runs dry.
            s.Stock[(int)Good.Water] = Ample;
            s.Stock[(int)Good.Nitrogen] = Ample;
            s.Stock[(int)Good.HeliumThree] = 0;
            // A Mine's inputs are never Limiting, so the water mine's own feed can be bone dry too.
            s.Stock[(int)Good.Machinery] = 0;
            // Inflate Water's target so its capacity never binds despite the ample stock above.
            s.ExtraDemand[(int)Good.Water] = Ample;

            economy.AdvanceTo(dt);

            Assert.Equal(1.0, farm.Utilisation, 9);
            Assert.Equal(1.0, waterMine.Utilisation, 9);
        }

        // ---------------------------------------------------------------- Produce: shipyards

        [Fact]
        public void Produce_Shipyard_AccruesBuildPointsCappedAtFourHundredThousand()
        {
            var (economy, s) = CreateMarket();
            AddFacility(s, IndustryKind.Shipyard, level: 1000);
            s.Stock[(int)Good.Alloys] = Ample;
            s.Stock[(int)Good.Electronics] = Ample;
            s.Stock[(int)Good.Machinery] = Ample;

            economy.AdvanceTo(5); // 20 ticks at 75,000 points each: well past the cap

            Assert.Equal(400000, s.BuildPoints);
        }

        // ---------------------------------------------------------------- AdvanceTo: large gaps

        [Fact]
        public void AdvanceTo_LargeGap_TakesCoarseTicksAndStaysStable()
        {
            var (economy, s) = CreateMarket(population: 50000);
            AddFacility(s, IndustryKind.Farm);
            AddFacility(s, IndustryKind.Shipyard, level: 50);
            s.Stock[(int)Good.Water] = 10000;
            s.Stock[(int)Good.Nitrogen] = 5000;
            s.Stock[(int)Good.HeliumThree] = 100;
            s.Stock[(int)Good.Alloys] = 10000;
            s.Stock[(int)Good.Electronics] = 5000;
            s.Stock[(int)Good.Machinery] = 5000;
            s.Stock[(int)Good.Food] = 10000;
            s.Stock[(int)Good.MedicalSupplies] = 10;

            // A huge jump: per AdvanceTo's comment, ticks coarsen instead of looping day by day.
            economy.AdvanceTo(50000);

            Assert.Equal(50000, economy.Jd, 9);
            for (int g = 0; g < GoodInfo.Count; g++)
            {
                Assert.False(double.IsNaN(s.Stock[g]) || double.IsInfinity(s.Stock[g]), $"{(Good)g} stock unstable: {s.Stock[g]}");
                Assert.True(s.Stock[g] >= -1e-6, $"{(Good)g} stock went negative: {s.Stock[g]}");
                Assert.False(double.IsNaN(s.Price[g]) || double.IsInfinity(s.Price[g]), $"{(Good)g} price unstable: {s.Price[g]}");
            }
            Assert.InRange(s.BuildPoints, 0, 400000);
            Assert.InRange(s.Population, s.InitialPopulation * 0.25, s.InitialPopulation * 4);
        }
    }
}
