using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Covers U.SPC-T56: per-person needs from sourced figures (<see cref="People.NeedsOf"/>), population
    /// growth and wellbeing, migration toward supply and wages (<see cref="People.Migrate"/>),
    /// labour-limited output (<see cref="People.AssignLabour"/>), wage taxes into the owner's treasury,
    /// and a treasury that pays for a navy's upkeep, refits and platforms.
    /// </summary>
    [TestFixture]
    public class PeopleAndLabourTests
    {
        const double Ample = 1e9;
        const double StartJd = 2461324.0;

        static Settlement Add(Economy e, string name, Faction owner, double population, SettlementKind kind = SettlementKind.Colony, int body = 1) =>
            e.Add(name, body, kind, owner, population);

        static void StockNeeds(Settlement s, double amount = Ample)
        {
            foreach (var (g, _) in s.PersonNeeds) s.Stock[(int)g] = amount;
        }

        static Facility Smelter(Settlement s, double level = 1)
        {
            var f = new Facility(Recipe.Of(IndustryKind.Smelter), level);
            s.Facilities.Add(f);
            s.Stock[(int)Good.NickelIron] = Ample;
            s.Stock[(int)Good.Titanium] = Ample;
            s.Stock[(int)Good.HeliumThree] = Ample;
            s.ExtraDemand[(int)Good.Alloys] = Ample; // the warehouse never fills
            return f;
        }

        /// <summary>People whose labour (<see cref="People.Labour"/>) is exactly <paramref name="share"/> of the jobs.</summary>
        static double PeopleFor(double jobs, double share) => jobs * share / (People.LabourForceShare * People.IndustrialShare);

        // ---------------------------------------------------------------- needs

        [Test]
        public void Needs_FoodIsTheBvadPlanningValue()
        {
            Assert.That(People.NeedOf(SettlementKind.Colony, Good.Food), Is.EqualTo((2.39 - 0.43 - 0.20) / 1000).Within(1e-12));
        }

        [Test]
        public void Needs_StationWaterIsTheLoopLossAtNinetyEightPercentPlusOxygenMakeUp()
        {
            double oxygen = 0.89 + 0.23 / 6 * 0.231;
            double expected = 5.03 * 0.02 + (oxygen * 9 / 8 - 0.47);
            Assert.That(People.WaterLossKg(SettlementKind.Station), Is.EqualTo(expected).Within(1e-9));
            Assert.That(People.NeedOf(SettlementKind.Station, Good.Water), Is.EqualTo(expected / 1000).Within(1e-12));
            Assert.That(expected, Is.InRange(0.6, 0.7)); // well under a kilogram a day: the ISS loop is nearly closed
        }

        [Test]
        public void Needs_AColonyWithShowersLosesFarMoreWaterThanAStationAndAHavenLosesMoreThanAStation()
        {
            double colony = People.WaterLossKg(SettlementKind.Colony);
            double station = People.WaterLossKg(SettlementKind.Station);
            double haven = People.WaterLossKg(SettlementKind.Haven);
            Assert.That(colony, Is.EqualTo(50 * (1 - 0.935) + People.WaterLossKg(SettlementKind.Station) - 5.03 * 0.02).Within(1e-9));
            Assert.That(colony > station * 4, Is.True);
            Assert.That(haven > station, Is.True);
        }

        [Test]
        public void Needs_NitrogenTopUpReplacesTheNitrogenInLeakedAir()
        {
            double expected = 0.23 / 6 * 0.755 / 1000;
            foreach (SettlementKind kind in Enum.GetValues(typeof(SettlementKind)))
                Assert.That(People.NeedOf(kind, Good.Nitrogen), Is.EqualTo(expected).Within(1e-12));
        }

        [Test]
        public void Needs_PeopleBreatheOutNitrogenDemandAtTheirSettlement()
        {
            var e = new Economy(new Diplomacy(), 0);
            var s = Add(e, "Air", new Faction("O", "O", default), 100000);
            e.Initialise();
            Assert.That(s.Demand[(int)Good.Nitrogen], Is.EqualTo(100000 * People.NeedOf(SettlementKind.Colony, Good.Nitrogen)).Within(1e-9));
        }

        // ---------------------------------------------------------------- growth

        [Test]
        public void Growth_AFedSettlementGrowsAndAStarvingOneShrinks()
        {
            var e = new Economy(new Diplomacy(), 0);
            var owner = new Faction("O", "O", default);
            var fed = Add(e, "Fed", owner, 100000, body: 1);
            var starving = Add(e, "Starving", owner, 100000, body: 2);
            StockNeeds(fed);
            StockNeeds(starving);
            starving.Stock[(int)Good.Food] = 0;

            e.AdvanceTo(5); // short of the first review: no migration yet

            Assert.That(fed.Population > 100000, Is.True);
            Assert.That(starving.Population < 100000 * 0.99, Is.True);
            Assert.That(starving.Wellbeing < 0.9, Is.True, "a month-smoothed wellbeing should already show five days of hunger");
            Assert.That(fed.Wellbeing, Is.EqualTo(1).Within(1e-9));
        }

        // ---------------------------------------------------------------- migration

        [Test]
        public void Migration_PeopleLeaveAStarvingSettlementForAFedOne_AndNobodyIsLostOnTheWay()
        {
            var e = new Economy(new Diplomacy(), 0);
            var owner = new Faction("O", "O", default);
            var fed = Add(e, "Fed", owner, 200000, body: 1);
            var hungry = Add(e, "Hungry", owner, 200000, body: 2);
            fed.Wellbeing = 1;
            hungry.Wellbeing = 0.3;
            double total = fed.Population + hungry.Population;

            People.Migrate(e.Settlements, e.Diplomacy, 10);

            Assert.That(hungry.Population < 200000, Is.True);
            Assert.That(fed.Population > 200000, Is.True);
            Assert.That(fed.Population + hungry.Population, Is.EqualTo(total).Within(1e-6));
            Assert.That(hungry.NetMigration < 0 && fed.NetMigration > 0, Is.True);
            Assert.That(fed.NetMigration, Is.EqualTo(-hungry.NetMigration).Within(1e-9));
        }

        [Test]
        public void Migration_ABiggerGapMovesMorePeople()
        {
            double Moved(double hungryWellbeing)
            {
                var e = new Economy(new Diplomacy(), 0);
                var owner = new Faction("O", "O", default);
                Add(e, "Fed", owner, 200000, body: 1).Wellbeing = 1;
                var hungry = Add(e, "Hungry", owner, 200000, body: 2);
                hungry.Wellbeing = hungryWellbeing;
                People.Migrate(e.Settlements, e.Diplomacy, 10);
                return 200000 - hungry.Population;
            }
            Assert.That(Moved(0.2) > Moved(0.7) * 2, Is.True);
            Assert.That(Moved(0.98), Is.EqualTo(0).Within(1e-9), "a gap under the threshold should move nobody");
        }

        [Test]
        public void Migration_PeopleMoveTowardBetterWages()
        {
            var e = new Economy(new Diplomacy(), 0);
            var owner = new Faction("O", "O", default);
            var rich = Add(e, "Boomtown", owner, 50000, SettlementKind.MiningOutpost, body: 1);
            var poor = Add(e, "Backwater", owner, 50000, SettlementKind.MiningOutpost, body: 2);
            foreach (var s in new[] { rich, poor })
            {
                var f = Smelter(s);
                f.Staffing = 1;
            }
            // Both fully fed; only the pay differs.
            rich.WageBill = 900000;
            poor.WageBill = 10000;

            People.Migrate(e.Settlements, e.Diplomacy, 10);

            Assert.That(rich.Earnings > poor.Earnings, Is.True);
            Assert.That(rich.Attraction > poor.Attraction, Is.True);
            Assert.That(rich.Population > 50000 && poor.Population < 50000, Is.True);
        }

        [Test]
        public void Migration_NobodyCrossesAWarFront()
        {
            var d = new Diplomacy();
            var e = new Economy(d, 0);
            var a = new Faction("A", "A", default);
            var b = new Faction("B", "B", default);
            d.Set(a, b, Relation.War);
            var hungry = Add(e, "Hungry", a, 200000, body: 1);
            var fed = Add(e, "Fed", b, 200000, body: 2);
            hungry.Wellbeing = 0.1;
            fed.Wellbeing = 1;

            People.Migrate(e.Settlements, d, 10);

            Assert.That(hungry.Population, Is.EqualTo(200000).Within(1e-9));
            Assert.That(fed.Population, Is.EqualTo(200000).Within(1e-9));
        }

        [Test]
        public void Migration_PiratesMoveOnlyAmongTheirHavens()
        {
            var e = new Economy(new Diplomacy(), 0);
            var navy = new Faction("N", "N", default);
            var pirates = new Faction("P", "", default) { IsPirate = true };
            var haven = Add(e, "Haven", pirates, 5000, SettlementKind.Haven, body: 1);
            var colony = Add(e, "Colony", navy, 500000, body: 2);
            haven.Wellbeing = 0.1;
            colony.Wellbeing = 1;

            People.Migrate(e.Settlements, e.Diplomacy, 10);

            Assert.That(haven.Population, Is.EqualTo(5000).Within(1e-9));
        }

        [Test]
        public void Migration_StopsAtTheFloorAndTheCap()
        {
            var e = new Economy(new Diplomacy(), 0);
            var owner = new Faction("O", "O", default);
            var hungry = Add(e, "Hungry", owner, 1e6, body: 1);
            var fed = Add(e, "Fed", owner, 1e6, body: 2);
            hungry.Wellbeing = 0;
            fed.Wellbeing = 1;
            hungry.Population = hungry.InitialPopulation * People.MinShare + 10; // ten people above the floor
            fed.Population = fed.InitialPopulation * People.MaxGrowth - 4; // room for four

            People.Migrate(e.Settlements, e.Diplomacy, 1000);

            Assert.That(hungry.Population >= hungry.InitialPopulation * People.MinShare - 1e-6, Is.True);
            Assert.That(fed.Population <= fed.InitialPopulation * People.MaxGrowth + 1e-6, Is.True);
            Assert.That(fed.Population, Is.EqualTo(fed.InitialPopulation * People.MaxGrowth).Within(1e-6));
        }

        [Test]
        public void Migration_OverYearsTheStarvingColonyEmptiesTowardTheFedOne()
        {
            var e = new Economy(new Diplomacy(), 0);
            var owner = new Faction("O", "O", default);
            var fed = Add(e, "Fed", owner, 300000, body: 1);
            var hungry = Add(e, "Hungry", owner, 300000, body: 2);
            StockNeeds(fed);
            StockNeeds(hungry);
            hungry.Stock[(int)Good.Food] = 0;
            for (int day = 1; day <= 365; day++)
            {
                fed.Stock[(int)Good.Food] = Ample; // keep the fed one fed as it grows
                e.AdvanceTo(day);
            }
            Assert.That(hungry.Population, Is.EqualTo(hungry.InitialPopulation * People.MinShare).Within(1));
            Assert.That(fed.Population > 300000 * 1.2, Is.True, "the fed colony should have taken in the hungry one's emigrants");
        }

        // ---------------------------------------------------------------- labour

        [Test]
        public void Labour_AFullCrewRunsAtFullCapacity()
        {
            var e = new Economy(new Diplomacy(), 0);
            var s = Add(e, "Works", new Faction("O", "O", default), 0);
            var f = Smelter(s);
            s.Population = s.InitialPopulation = PeopleFor(f.Workers, 1);
            StockNeeds(s);

            e.AdvanceTo(0.1);

            Assert.That(f.Staffing, Is.EqualTo(1).Within(1e-9));
            Assert.That(f.Utilisation, Is.EqualTo(1).Within(1e-9));
            Assert.That(s.Stock[(int)Good.Alloys], Is.EqualTo(800 * 0.1).Within(1e-6));
        }

        [Test]
        public void Labour_HalfACrewMakesHalfTheOutput()
        {
            var e = new Economy(new Diplomacy(), 0);
            var s = Add(e, "Works", new Faction("O", "O", default), 0);
            var f = Smelter(s);
            s.Population = s.InitialPopulation = PeopleFor(f.Workers, 0.5);
            StockNeeds(s);

            e.AdvanceTo(0.1);

            Assert.That(f.Staffing, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(s.Staffing, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(f.Utilisation, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(s.Stock[(int)Good.Alloys], Is.EqualTo(800 * 0.1 * 0.5).Within(1e-6));
            Assert.That(s.Stock[(int)Good.NickelIron], Is.EqualTo(Ample - 2200 * 0.1 * 0.5).Within(1e-3)); // and uses half the ore
        }

        [Test]
        public void Labour_BelowTheMinimumCrewAFacilityStalls()
        {
            var e = new Economy(new Diplomacy(), 0);
            var s = Add(e, "Works", new Faction("O", "O", default), 0);
            var f = Smelter(s);
            s.Population = s.InitialPopulation = PeopleFor(f.Workers, People.MinimumCrew * 0.9);
            StockNeeds(s);

            e.AdvanceTo(0.1);

            Assert.That(f.Utilisation, Is.EqualTo(0).Within(1e-12));
            Assert.That(s.Stock[(int)Good.Alloys], Is.EqualTo(0).Within(1e-12));
        }

        [Test]
        public void Labour_NobodyHomeNothingRuns()
        {
            var e = new Economy(new Diplomacy(), 0);
            var s = Add(e, "Ghost works", new Faction("O", "O", default), 0);
            var f = Smelter(s);

            e.AdvanceTo(0.1);

            Assert.That(f.Utilisation, Is.EqualTo(0).Within(1e-12));
        }

        [Test]
        public void Labour_LifeSupportIsCrewedFirst()
        {
            var e = new Economy(new Diplomacy(), 0);
            var s = Add(e, "Short-handed", new Faction("O", "O", default), 0);
            var farm = new Facility(Recipe.Of(IndustryKind.Farm), 1);
            s.Facilities.Add(farm);
            var smelter = Smelter(s);
            // Enough hands for the farm and a tenth of the smelter.
            s.Population = s.InitialPopulation = PeopleFor(farm.Workers + smelter.Workers * 0.1, 1);

            People.AssignLabour(s);

            Assert.That(farm.Staffing, Is.EqualTo(1).Within(1e-9));
            Assert.That(smelter.Staffing, Is.EqualTo(0.1).Within(1e-9));
            Assert.That(People.CrewFactor(smelter.Staffing), Is.EqualTo(0)); // under the minimum crew: stalled
        }

        [Test]
        public void Labour_MinesNeedMoreHandsPerTonneForLeanerOres()
        {
            double Workers(Good g) => Recipe.Mine(g, 100).WorkersPerLevel;
            Assert.That(Workers(Good.NickelIron), Is.EqualTo(100 * 365.0 / 12000).Within(1e-9));
            Assert.That(Workers(Good.Copper) > Workers(Good.NickelIron), Is.True);
            Assert.That(Workers(Good.Platinum) > Workers(Good.Copper), Is.True);
        }

        [Test]
        public void Labour_AnUnderstaffedFacilityDoesNotExpandHoweverWellItSells()
        {
            var e = new Economy(new Diplomacy(), 0);
            var s = Add(e, "Short-handed", new Faction("O", "O", default), 0);
            var f = new Facility(Recipe.Mine(Good.Platinum, 1000), 1);
            s.Facilities.Add(f);
            s.Stock[(int)Good.Machinery] = Ample;
            s.Stock[(int)Good.HeliumThree] = Ample;
            s.Population = s.InitialPopulation = PeopleFor(f.Workers, 0.9);
            StockNeeds(s);

            e.AdvanceTo(10); // one review

            Assert.That(f.Level, Is.LessThanOrEqualTo(f.BaseLevel));
        }

        // ---------------------------------------------------------------- wages and taxes

        /// <summary>A priced market (initialised) with one smelter crewed <paramref name="crewShare"/> over, fed and supplied.</summary>
        static (Economy e, Faction owner, Settlement s, Facility f) Works(double crewShare, SettlementKind kind = SettlementKind.Colony, Faction owner = null)
        {
            var e = new Economy(new Diplomacy(), 0);
            owner ??= new Faction("O", "O", default);
            var s = Add(e, "Works", owner, 0, kind);
            var f = new Facility(Recipe.Of(IndustryKind.Smelter), 1);
            s.Facilities.Add(f);
            s.Population = s.InitialPopulation = PeopleFor(f.Workers, crewShare);
            e.Initialise();
            s.Facilities.Clear();
            Smelter(s);
            f = s.Facilities[0];
            StockNeeds(s);
            return (e, owner, s, f);
        }

        [Test]
        public void Wages_TaxOnWagesGoesToTheOwnersTreasury()
        {
            var (e, owner, s, _) = Works(2);
            double before = owner.Treasury;

            e.AdvanceTo(0.25); // one tick

            Assert.That(s.WageBill > 0, Is.True);
            Assert.That(s.WageTax, Is.EqualTo(s.WageBill * People.WageTaxRate).Within(1e-6));
            Assert.That(owner.Treasury - before, Is.EqualTo(s.WageTax * 0.25).Within(1e-3));
        }

        [Test]
        public void Wages_AHalfStaffedEconomyPaysHalfTheTax()
        {
            double Tax(double crewShare)
            {
                var (e, owner, _, _) = Works(crewShare);
                e.AdvanceTo(0.25);
                return owner.Treasury;
            }
            Assert.That(Tax(1) > 0, Is.True);
            Assert.That(Tax(0.5), Is.EqualTo(Tax(1) * 0.5).Within(Tax(1) * 0.02));
        }

        [Test]
        public void Wages_AStalledEconomyPaysNoTax()
        {
            var (e, owner, _, _) = Works(People.MinimumCrew * 0.5);
            e.AdvanceTo(0.25);
            Assert.That(owner.Treasury, Is.EqualTo(0).Within(1e-9));
        }

        [Test]
        public void Wages_AHavenPaysNoTax()
        {
            var pirates = new Faction("P", "", default) { IsPirate = true };
            var (e, _, s, f) = Works(2, SettlementKind.Haven, pirates);

            e.AdvanceTo(0.25);

            Assert.That(f.Utilisation > 0.9, Is.True);
            Assert.That(s.WageTax, Is.EqualTo(0));
        }

        // ---------------------------------------------------------------- the treasury and the navy

        static World CreateWorld()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return World.CreateDefault(eph, StartJd);
        }

        static Ship ParkWarship(World w, Faction faction, ShipClass cls, int body)
        {
            var ship = w.Fleet.AddShip(cls.ToString(), cls, faction, ShipRole.Patrol);
            ship.Orbit = new ParkingOrbit { Body = body };
            ship.Phase = ShipPhase.Parked;
            ship.PhaseStartJd = StartJd;
            return ship;
        }

        [Test]
        public void Navy_WarshipsAndPlatformsCostTheirUpkeepEveryDay()
        {
            var w = CreateWorld();
            var f = w.Fleet.AddFaction("Upkeep Navy", "UPK", default);
            f.IsMilitary = true;
            f.Treasury = 1e10;
            int body = w.Catalog["Pluto"].Index;
            ParkWarship(w, f, ShipClass.Cruiser, body);
            var port = w.Economy.Add("Upkeep Port", body, SettlementKind.Colony, f, 0);
            port.MaxPlatforms = 1;
            port.Platforms.Add(new ShipCombat(CombatSpec.Platform));

            w.War.Update(StartJd + 1.01); // one daily step

            double expected = (Economy.HullPrice(ShipClass.Cruiser) + War.PlatformPrice) * Economy.UpkeepPerYear / 365.25;
            Assert.That(1e10 - f.Treasury, Is.EqualTo(expected).Within(1));
        }

        [Test]
        public void Navy_ABrokeOwnerRaisesNoPlatforms_ASolventOneDoes()
        {
            int Raised(double treasury)
            {
                var w = CreateWorld();
                var f = w.Fleet.AddFaction("Platform Owner", "PLT", default);
                f.Treasury = treasury;
                int body = w.Catalog["Pluto"].Index;
                var port = w.Economy.Add("Bare Port", body, SettlementKind.Colony, f, 0);
                port.MaxPlatforms = 3;
                double jd = StartJd + 0.01;
                for (int day = 0; day < 200; day++)
                {
                    port.Stock[(int)Good.Alloys] = 1e6;
                    port.Stock[(int)Good.Electronics] = 1e6;
                    jd += 1;
                    w.War.Update(jd);
                }
                return port.Platforms.Count;
            }
            Assert.That(Raised(-1e12), Is.EqualTo(0));
            Assert.That(Raised(1e12), Is.GreaterThan(0));
        }

        [Test]
        public void Navy_ABrokeNavyCannotPayTheYardForRepairs()
        {
            (double before, double after) Refit(double treasury)
            {
                var w = CreateWorld();
                var f = w.Fleet.AddFaction("Refit Navy", "RFT", default);
                f.IsMilitary = true;
                f.Treasury = treasury;
                int body = w.Catalog["Pluto"].Index;
                var yard = w.Economy.Add("Refit Yard", body, SettlementKind.Station, f, 0);
                yard.Facilities.Add(new Facility(Recipe.Of(IndustryKind.Shipyard), 1));
                yard.Stock[(int)Good.Alloys] = 1e6;
                yard.Stock[(int)Good.Electronics] = 1e6;
                var ship = ParkWarship(w, f, ShipClass.Destroyer, body);
                ship.Combat.Hull = ship.Combat.Spec.Hull * 0.5;
                double before = ship.Combat.Hull;
                w.War.Update(StartJd + 1.01);
                return (before, ship.Combat.Hull);
            }
            var broke = Refit(-1e12);
            Assert.That(broke.after, Is.EqualTo(broke.before).Within(1e-9));
            var solvent = Refit(1e12);
            Assert.That(solvent.after > solvent.before, Is.True);
        }
    }
}
