using System;
using System.Collections.Generic;
using UnitySpace.Core;
using NUnit.Framework;

namespace UnitySpace.Core.Tests
{
    /// <summary>
    /// Covers U.SPC-T32: <see cref="Economy.Consume"/> (feeding people and growing or shrinking
    /// population), <see cref="Economy.Fence"/> (haven smuggling), the 10-day <see cref="Economy.Review"/>
    /// (facility level drift and shipyard builds) and <see cref="Economy.Capture"/>. Settlements are
    /// built directly (no <see cref="Economy.Initialise"/>) so each test starts from known stock and
    /// population, isolated from trading and the production tick where that is not under test.
    /// </summary>
    [TestFixture]
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

        /// <summary>
        /// Houses enough people at <paramref name="s"/> to crew its facilities <paramref name="headroom"/>
        /// times over (U.SPC-T56: a facility runs only with its workers), so that even a population that
        /// starves down to its floor over a long test still staffs them.
        /// </summary>
        static void Staff(Settlement s, double headroom = 4)
        {
            s.Population = s.InitialPopulation = s.Jobs / (People.LabourForceShare * People.IndustrialShare) * headroom;
        }

        static void StockAllNeeds(Settlement s, double amount)
        {
            foreach (var (g, _) in s.PersonNeeds) s.Stock[(int)g] = amount;
        }

        // ---------------------------------------------------------------- Consume: satisfaction

        [Test]
        public void Consume_PartialStock_SatisfactionIsShareOfNeedMet()
        {
            var (economy, s, _) = CreateMarket(population: 100000);
            double dt = 0.1;
            StockAllNeeds(s, Ample);
            double needFood = s.PersonNeeds[0].perPersonDay * s.Population * dt;
            Assert.That(s.PersonNeeds[0].good, Is.EqualTo(Good.Food));
            s.Stock[(int)Good.Food] = needFood * 0.5; // only half of what the colony needs this tick

            economy.AdvanceTo(dt);

            Assert.That(s.Satisfaction[(int)Good.Food], Is.EqualTo(0.5).Within(1e-6));
            Assert.That(s.Stock[(int)Good.Food], Is.EqualTo(0).Within(1e-9)); // everything on hand was eaten
            Assert.That(s.Satisfaction[(int)Good.Water], Is.EqualTo(1.0).Within(1e-9)); // ample, untouched by the shortage
            Assert.That(s.Satisfaction[(int)Good.Alloys], Is.EqualTo(1.0).Within(1e-9)); // not a person need: always 1
        }

        [Test]
        public void Consume_NoNeedThisTick_SatisfactionStaysAtOne()
        {
            var (economy, s, _) = CreateMarket(population: 0); // nobody to feed
            s.Stock[(int)Good.Food] = 0;

            economy.AdvanceTo(0.1);

            Assert.That(s.Satisfaction[(int)Good.Food], Is.EqualTo(1.0).Within(1e-9));
        }

        // ---------------------------------------------------------------- Consume: population growth

        [Test]
        public void Consume_FedWateredAndMedicated_GrowsAtTheFullFourPercentAnnualRate()
        {
            var (economy, s, _) = CreateMarket(population: 100000);
            double dt = 0.1;
            StockAllNeeds(s, Ample);
            double initial = s.Population;

            economy.AdvanceTo(dt);

            double expectedRate = 0.00011; // health = 1: 0.00011 * (0.5 + 0.5) - 0
            double expected = initial * (1 + expectedRate * dt);
            Assert.That(s.Population, Is.EqualTo(expected).Within(1e-3));
            Assert.That(s.Population > initial, Is.True);
            // Roughly 4%/year: 0.00011 * 365 ~= 0.0402.
            Assert.That(expectedRate * 365, Is.InRange(0.039, 0.041));
        }

        [Test]
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
            Assert.That(s.Population, Is.EqualTo(expected).Within(1e-3));
            Assert.That(s.Population < initial, Is.True);
            // Roughly a 5%/year decline: 0.000145 * 365 ~= 0.053.
            Assert.That(-expectedRate * 365, Is.InRange(0.05, 0.056));
        }

        [Test]
        public void Consume_Starving_ShrinksMuchFasterThanMissingMedicineAlone()
        {
            var (economy, s, _) = CreateMarket(population: 100000);
            double dt = 0.1;
            StockAllNeeds(s, Ample);
            s.Stock[(int)Good.Food] = 0; // starving: sustenance drops well below 0.98
            double initial = s.Population;

            economy.AdvanceTo(dt);

            Assert.That(s.Satisfaction[(int)Good.Food] < 0.98, Is.True);
            double expectedRate = (0 - 0.98) * 0.004; // sustenance = 0 (no food at all this tick)
            double expected = initial * (1 + expectedRate * dt);
            Assert.That(s.Population, Is.EqualTo(expected).Within(1e-3));
            double starvingDrop = initial - s.Population;

            // Compare against the no-medicine decline from the same starting population.
            var (economy2, s2, _) = CreateMarket(population: 100000);
            StockAllNeeds(s2, Ample);
            s2.Stock[(int)Good.MedicalSupplies] = 0;
            economy2.AdvanceTo(dt);
            double noMedicineDrop = initial - s2.Population;

            Assert.That(starvingDrop > noMedicineDrop * 10, Is.True, "hunger should shrink population far faster than missing medicine alone");
        }

        [Test]
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

            Assert.That(s.Population, Is.EqualTo(s.InitialPopulation * 0.25).Within(1e-3));
        }

        [Test]
        public void Consume_PopulationNeverRisesAboveFourTimesInitial()
        {
            var (economy, s, _) = CreateMarket(population: 100000);
            StockAllNeeds(s, Ample);

            economy.AdvanceTo(20000); // far more than enough years at ~4%/year to hit the cap

            Assert.That(s.Population, Is.EqualTo(s.InitialPopulation * 4).Within(1e-3));
        }

        // ---------------------------------------------------------------- Fence (haven smuggling)

        [Test]
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
            Assert.That(s.Stock[(int)Good.Food], Is.EqualTo(target + 1000 - expectedMoved).Within(1e-6));
            Assert.That(owner.Treasury, Is.EqualTo(expectedMoved * GoodInfo.Of(Good.Food).BasePrice * 0.5).Within(1e-6));
        }

        [Test]
        public void Fence_StockAtOrBelowTarget_DoesNotDrainOrPay()
        {
            var (economy, s, owner) = CreateMarket(kind: SettlementKind.Haven);
            s.ExtraDemand[(int)Good.Food] = 100;
            double target = s.ExtraDemand[(int)Good.Food] * Settlement.CoverDays;
            s.Stock[(int)Good.Food] = target; // exactly at target: nothing to smuggle
            owner.Treasury = 0;

            economy.AdvanceTo(0.1);

            Assert.That(s.Stock[(int)Good.Food], Is.EqualTo(target).Within(1e-6));
            Assert.That(owner.Treasury, Is.EqualTo(0).Within(1e-9));
        }

        [Test]
        public void Fence_NonHavenSettlements_NeverFence()
        {
            var (economy, s, owner) = CreateMarket(kind: SettlementKind.Colony);
            s.ExtraDemand[(int)Good.Food] = 100;
            double target = s.ExtraDemand[(int)Good.Food] * Settlement.CoverDays;
            s.Stock[(int)Good.Food] = target + 1000;
            owner.Treasury = 0;

            economy.AdvanceTo(0.1);

            Assert.That(s.Stock[(int)Good.Food], Is.EqualTo(target + 1000).Within(1e-6));
            Assert.That(owner.Treasury, Is.EqualTo(0).Within(1e-9));
        }

        // ---------------------------------------------------------------- Review: facility level drift

        [Test]
        public void Review_HighMarginAndUtilisation_GrowsFacilityLevelByThreePercent()
        {
            var (economy, s, _) = CreateMarket();
            var f = AddMine(s, Good.Platinum, 1000); // trivial inputs, a very high-value output: huge margin
            Staff(s);
            s.Stock[(int)Good.Machinery] = Ample;
            s.Stock[(int)Good.HeliumThree] = Ample;

            economy.AdvanceTo(10); // exactly one 10-day review cycle

            Assert.That(f.Level, Is.EqualTo(f.BaseLevel * 1.03).Within(1e-6));
        }

        [Test]
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

            Assert.That(f.Level, Is.EqualTo(f.BaseLevel * 0.98).Within(1e-6));
        }

        [Test]
        public void Review_SustainedHighMargin_LevelClampsAtThreeTimesBaseLevel()
        {
            var (economy, s, _) = CreateMarket();
            var f = AddMine(s, Good.Platinum, 1000);
            Staff(s, headroom: 12); // room to crew three times the base level even at the population floor
            s.Stock[(int)Good.Machinery] = Ample;
            s.Stock[(int)Good.HeliumThree] = Ample;
            s.ExtraDemand[(int)Good.Platinum] = Ample; // a sink, so the warehouse never fills as Level grows

            economy.AdvanceTo(1000); // many review cycles: far more than needed to reach the cap

            Assert.That(f.Level, Is.EqualTo(f.BaseLevel * 3).Within(1e-6));
        }

        [Test]
        public void Review_SustainedGlut_LevelClampsAtFourTenthsOfBaseLevel()
        {
            var (economy, s, _) = CreateMarket();
            var f = AddFacility(s, IndustryKind.Smelter);
            s.Stock[(int)Good.NickelIron] = Ample;
            s.Stock[(int)Good.Titanium] = Ample;
            s.Stock[(int)Good.HeliumThree] = Ample;
            s.Stock[(int)Good.Alloys] = 800 * Settlement.StorageDays;

            economy.AdvanceTo(1000); // many review cycles: far more than needed to reach the floor

            Assert.That(f.Level, Is.EqualTo(f.BaseLevel * 0.4).Within(1e-6));
        }

        [Test]
        public void Review_ShipyardFacility_IsNeverGrownOrShrunkByTheLevelDriftRule()
        {
            var (economy, s, owner) = CreateMarket();
            var yard = AddFacility(s, IndustryKind.Shipyard);
            owner.Treasury = 0; // no money to build, so only the level-drift rule could move Level

            economy.AdvanceTo(10);

            Assert.That(yard.Level, Is.EqualTo(yard.BaseLevel).Within(1e-9));
        }

        // ---------------------------------------------------------------- Review: shipyard builds

        [Test]
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

            Assert.That(built, Is.False);
            Assert.That(s.OnSlip, Is.Null);
            Assert.That(s.BuildPoints, Is.EqualTo(400000).Within(1e-3));
            Assert.That(owner.Treasury, Is.EqualTo(1e12).Within(1e-3));
        }

        [Test]
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

            Assert.That(built, Is.False);
            Assert.That(s.OnSlip, Is.EqualTo(ShipClass.Freighter));
            Assert.That(s.BuildPoints, Is.EqualTo(Economy.HullCost(ShipClass.Freighter) - 1).Within(1e-3));
        }

        [Test]
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

            Assert.That(built, Is.False);
            Assert.That(s.BuildPoints, Is.EqualTo(cost + 1000).Within(1e-3)); // untouched: points are only spent once the build commits
        }

        [Test]
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

            Assert.That(builtAt, Is.SameAs(s));
            Assert.That(builtClass, Is.EqualTo(ShipClass.Freighter));
            Assert.That(builtJd, Is.EqualTo(1010).Within(1e-6));
            Assert.That(s.OnSlip, Is.EqualTo(ShipClass.Freighter));
            Assert.That(s.BuildPoints, Is.EqualTo(500).Within(1e-3));
            Assert.That(owner.Treasury, Is.EqualTo(2_000_000).Within(1e-3));
        }

        // ---------------------------------------------------------------- Capture

        [Test]
        public void Capture_ChangesOwnerResetsDaysSinceCaptureAndKeepsThirtyPercentOfBuildPoints()
        {
            var (economy, s, originalOwner) = CreateMarket();
            s.DaysSinceCapture = 500;
            s.BuildPoints = 10000;
            var attacker = new Faction("Attacker", "ATK", default);

            economy.Capture(s, attacker);

            Assert.That(s.Owner, Is.SameAs(attacker));
            Assert.That(s.Owner, Is.Not.SameAs(originalOwner));
            Assert.That(s.DaysSinceCapture, Is.EqualTo(0).Within(1e-9));
            Assert.That(s.BuildPoints, Is.EqualTo(3000).Within(1e-9));
        }
    }
}
