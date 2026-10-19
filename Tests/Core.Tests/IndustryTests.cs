using System;
using UnitySpace.Core;
using NUnit.Framework;

namespace UnitySpace.Core.Tests
{
    /// <summary>
    /// Covers U.SPC-T31: the production tick in <see cref="Economy.AdvanceTo"/> / Produce, plus the
    /// <see cref="Recipe"/> and <see cref="Facility"/> types it drives. Facilities are attached
    /// directly to a bare <see cref="Settlement"/> (population 0 unless a test needs it) so each tick
    /// is isolated from trading, pricing reviews and population growth.
    /// </summary>
    [TestFixture]
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

        /// <summary>
        /// Houses enough people at <paramref name="s"/> to crew its facilities <paramref name="headroom"/>
        /// times over (U.SPC-T56: a facility runs only with its workers), so that even a population that
        /// starves down to its floor over a long test still staffs them.
        /// </summary>
        static void Staff(Settlement s, double headroom = 4)
        {
            s.Population = s.InitialPopulation = s.Jobs / (People.LabourForceShare * People.IndustrialShare) * headroom;
        }

        // ---------------------------------------------------------------- Recipe.Limiting

        [Test]
        public void RecipeLimiting_MarksInputsAtLeastTwoPercentOfTotalAsBulk()
        {
            // Farm: Water 700, Nitrogen 60, HeliumThree 0.4 -- total 760.4, 2% cutoff ~15.2.
            var recipe = Recipe.Of(IndustryKind.Farm);

            Assert.That(recipe.Limiting[0], Is.True); // Water: bulk feedstock
            Assert.That(recipe.Limiting[1], Is.True); // Nitrogen: bulk feedstock
            Assert.That(recipe.Limiting[2], Is.False); // HeliumThree: a trace consumable, below the cutoff
        }

        [Test]
        public void RecipeLimiting_BoundaryAtExactlyTwoPercentCountsAsBulk()
        {
            // Total = 100; 2% of total = 2, exactly matching the second input's rate.
            var recipe = new Recipe(IndustryKind.MachineWorks, "Test",
                new[] { (Good.Alloys, 98.0), (Good.Copper, 2.0) }, new[] { (Good.Machinery, 1.0) });

            Assert.That(recipe.Limiting[0], Is.True);
            Assert.That(recipe.Limiting[1], Is.True); // the ">=" cutoff includes the boundary case
        }

        [Test]
        public void RecipeLimiting_JustBelowTwoPercentIsMinor()
        {
            // Total = 100; the second input is 1% of it, just under the cutoff.
            var recipe = new Recipe(IndustryKind.MachineWorks, "Test",
                new[] { (Good.Alloys, 99.0), (Good.Copper, 1.0) }, new[] { (Good.Machinery, 1.0) });

            Assert.That(recipe.Limiting[0], Is.True);
            Assert.That(recipe.Limiting[1], Is.False);
        }

        [Test]
        public void RecipeLimiting_IsNeverTrueForMineRecipes_EvenWhenInputsAreProportionallyBulk()
        {
            // Both of a Mine's inputs are well above the 2% cutoff by proportion (1.2 and 0.08 out of
            // 1.28 total), so if the Mine override were missing this would come back all-true.
            var recipe = Recipe.Mine(Good.NickelIron, 1000);

            Expect.All(recipe.Limiting, l => Assert.That(l, Is.False));
        }

        // ---------------------------------------------------------------- Produce: inputs

        [Test]
        public void Produce_ShortLimitingInput_CapsOutputProportionally()
        {
            var (economy, s) = CreateMarket();
            var f = AddFacility(s, IndustryKind.Smelter);
            Staff(s);
            double dt = 0.1;
            double needNickelIron = 2200 * dt; // level 1
            s.Stock[(int)Good.NickelIron] = needNickelIron * 0.4; // only 40% of what's needed
            s.Stock[(int)Good.Titanium] = Ample;
            s.Stock[(int)Good.HeliumThree] = Ample;
            s.ExtraDemand[(int)Good.Alloys] = Ample; // keep the warehouse from ever binding

            economy.AdvanceTo(dt);

            Assert.That(f.Utilisation, Is.EqualTo(0.4).Within(1e-9));
            Assert.That(s.Stock[(int)Good.Alloys], Is.EqualTo(800 * dt * 0.4).Within(1e-6));
            Assert.That(s.Stock[(int)Good.NickelIron], Is.EqualTo(0).Within(1e-6)); // the short input is fully drained
        }

        [Test]
        public void Produce_ShortMinorInput_LowersEfficiencyToTheFloor()
        {
            var (economy, s) = CreateMarket();
            var f = AddFacility(s, IndustryKind.Smelter);
            Staff(s);
            double dt = 0.1;
            s.Stock[(int)Good.NickelIron] = Ample;
            s.Stock[(int)Good.Titanium] = Ample;
            s.Stock[(int)Good.HeliumThree] = 0; // the minor input is entirely unavailable
            s.ExtraDemand[(int)Good.Alloys] = Ample;

            economy.AdvanceTo(dt);

            Assert.That(f.Utilisation, Is.EqualTo(Recipe.MinorFloor).Within(1e-9)); // run stays 1; efficiency floors out
            Assert.That(s.Stock[(int)Good.Alloys], Is.EqualTo(800 * dt * Recipe.MinorFloor).Within(1e-6));
        }

        [Test]
        public void Produce_PartiallyShortMinorInput_ScalesEfficiencyLinearlyAboveTheFloor()
        {
            var (economy, s) = CreateMarket();
            var f = AddFacility(s, IndustryKind.Smelter);
            Staff(s);
            double dt = 0.1;
            double needHeliumThree = 0.8 * dt;
            s.Stock[(int)Good.NickelIron] = Ample;
            s.Stock[(int)Good.Titanium] = Ample;
            s.Stock[(int)Good.HeliumThree] = needHeliumThree * 0.5; // half of what's needed
            s.ExtraDemand[(int)Good.Alloys] = Ample;

            economy.AdvanceTo(dt);

            double expected = Recipe.MinorFloor + (1 - Recipe.MinorFloor) * 0.5;
            Assert.That(f.Utilisation, Is.EqualTo(expected).Within(1e-9));
            Assert.That(f.Utilisation, Is.InRange(Recipe.MinorFloor, 1.0));
        }

        [Test]
        public void Produce_UtilisationIsRunTimesEfficiency()
        {
            var (economy, s) = CreateMarket();
            var f = AddFacility(s, IndustryKind.Smelter);
            Staff(s);
            double dt = 0.1;
            s.Stock[(int)Good.NickelIron] = 2200 * dt * 0.5; // limiting input at 50% -> run = 0.5
            s.Stock[(int)Good.Titanium] = Ample;
            s.Stock[(int)Good.HeliumThree] = 0; // minor input exhausted -> efficiency floors
            s.ExtraDemand[(int)Good.Alloys] = Ample;

            economy.AdvanceTo(dt);

            double expectedRun = 0.5, expectedEfficiency = Recipe.MinorFloor;
            Assert.That(f.Utilisation, Is.EqualTo(expectedRun * expectedEfficiency).Within(1e-9));
            Assert.That(s.Stock[(int)Good.Alloys], Is.EqualTo(800 * dt * expectedRun * expectedEfficiency).Within(1e-6));
        }

        // ---------------------------------------------------------------- Produce: capacity

        [Test]
        public void Produce_FullWarehouse_ThrottlesOutputToExactlyFillCapacity()
        {
            var (economy, s) = CreateMarket();
            var f = AddFacility(s, IndustryKind.Smelter);
            Staff(s);
            double dt = 0.1;
            s.Stock[(int)Good.NickelIron] = Ample;
            s.Stock[(int)Good.Titanium] = Ample;
            s.Stock[(int)Good.HeliumThree] = Ample;
            // No ExtraDemand this time: capacity is driven purely by this facility's own output rate.
            double capacity = 800 * Settlement.StorageDays;
            s.Stock[(int)Good.Alloys] = capacity - 5; // only 5 tonnes of room left

            economy.AdvanceTo(dt);

            Assert.That(s.Stock[(int)Good.Alloys], Is.EqualTo(capacity).Within(1e-6));
            Assert.That(f.Utilisation, Is.EqualTo(5.0 / (800 * dt)).Within(1e-9)); // run throttled to room / uncapped output
        }

        // ---------------------------------------------------------------- Produce: capture disruption

        [Test]
        public void Produce_FreshlyCapturedSettlement_RunsIndustryAtAQuarter()
        {
            var (economy, s) = CreateMarket();
            s.DaysSinceCapture = 0;
            var f = AddFacility(s, IndustryKind.Smelter);
            Staff(s);
            double dt = 0.1;
            s.Stock[(int)Good.NickelIron] = Ample;
            s.Stock[(int)Good.Titanium] = Ample;
            s.Stock[(int)Good.HeliumThree] = Ample;
            s.ExtraDemand[(int)Good.Alloys] = Ample;

            economy.AdvanceTo(dt);

            Assert.That(f.Utilisation, Is.EqualTo(0.25).Within(1e-9));
        }

        [Test]
        public void Produce_IndustryRecoversGraduallyOverSixtyDaysAfterCapture()
        {
            var (economy, s) = CreateMarket();
            s.DaysSinceCapture = 0;
            var f = AddFacility(s, IndustryKind.Smelter);
            Staff(s);
            s.Stock[(int)Good.NickelIron] = Ample;
            s.Stock[(int)Good.Titanium] = Ample;
            s.Stock[(int)Good.HeliumThree] = Ample;
            s.ExtraDemand[(int)Good.Alloys] = Ample;

            economy.AdvanceTo(30); // halfway through the 60-day recovery

            double daysAtLastProduce = s.DaysSinceCapture - Economy.TickDays;
            double expected = Math.Min(1, 0.25 + daysAtLastProduce / 60.0);
            Assert.That(f.Utilisation, Is.EqualTo(expected).Within(1e-9));
            Assert.That(f.Utilisation, Is.InRange(0.26, 0.99)); // recovering, not instantly fixed nor still at 25%

            economy.AdvanceTo(70); // past the 60-day recovery window

            Assert.That(f.Utilisation, Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void Produce_LifeSupportFacilities_IgnoreCaptureDisruptionAndMinorInputShortages()
        {
            var (economy, s) = CreateMarket();
            s.DaysSinceCapture = 0; // a non-lifeSupport facility would run at only 25% here
            var farm = AddFacility(s, IndustryKind.Farm);
            var waterMine = new Facility(Recipe.Mine(Good.Water, 1000), 1);
            s.Facilities.Add(waterMine);
            Staff(s);
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

            Assert.That(farm.Utilisation, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(waterMine.Utilisation, Is.EqualTo(1.0).Within(1e-9));
        }

        // ---------------------------------------------------------------- Produce: shipyards

        [Test]
        public void Produce_Shipyard_AccruesBuildPointsCappedAtFourHundredThousand()
        {
            var (economy, s) = CreateMarket();
            AddFacility(s, IndustryKind.Shipyard, level: 1000);
            Staff(s);
            s.Stock[(int)Good.Alloys] = Ample;
            s.Stock[(int)Good.Electronics] = Ample;
            s.Stock[(int)Good.Machinery] = Ample;

            economy.AdvanceTo(5); // 20 ticks at 75,000 points each: well past the cap

            Assert.That(s.BuildPoints, Is.EqualTo(400000));
        }

        // ---------------------------------------------------------------- AdvanceTo: large gaps

        [Test]
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

            Assert.That(economy.Jd, Is.EqualTo(50000).Within(1e-9));
            for (int g = 0; g < GoodInfo.Count; g++)
            {
                Assert.That(double.IsNaN(s.Stock[g]) || double.IsInfinity(s.Stock[g]), Is.False, $"{(Good)g} stock unstable: {s.Stock[g]}");
                Assert.That(s.Stock[g] >= -1e-6, Is.True, $"{(Good)g} stock went negative: {s.Stock[g]}");
                Assert.That(double.IsNaN(s.Price[g]) || double.IsInfinity(s.Price[g]), Is.False, $"{(Good)g} price unstable: {s.Price[g]}");
            }
            Assert.That(s.BuildPoints, Is.InRange(0, 400000));
            Assert.That(s.Population, Is.InRange(s.InitialPopulation * 0.25, s.InitialPopulation * 4));
        }
    }
}
