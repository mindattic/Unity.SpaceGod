using System;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// U.SPC-T98: colonisation. Pressure comes from the population model (growth at the cap, migrants turned
    /// away from full places); a faction with a ship's worth of people with no room, a free viable site and the
    /// money sends a colony ship with colonists and starter goods; the outpost it founds grows into a colony as
    /// people and facilities grow, and one that cannot feed itself shrinks to its hard core and is left a ruin.
    /// The markets are built by hand (no world) so each rule is seen on its own.
    /// </summary>
    [TestFixture]
    public class ColonisationTests
    {
        const double Ample = 1e9;

        sealed class Setup
        {
            public Economy Economy;
            public Colonisation Colonisation;
            public Faction Owner;
            public Settlement Home;
        }

        /// <summary>A faction with one home port well stocked with everything, and colonisation switched on.</summary>
        static Setup Create(double homeBase = 5e5, double homePeople = 5e5, double treasury = 5e9, params ColonySite[] sites)
        {
            var economy = new Economy(new Diplomacy(), 0);
            var owner = new Faction("Owner", "OWN", default) { Treasury = treasury };
            var home = economy.Add("Home", 1, SettlementKind.Colony, owner, homeBase);
            home.Population = homePeople;
            for (int i = 0; i < GoodInfo.Count; i++) home.Stock[i] = Ample;
            var c = new Colonisation();
            c.Sites.AddRange(sites);
            economy.Colonisation = c;
            economy.Open(home);
            return new Setup { Economy = economy, Colonisation = c, Owner = owner, Home = home };
        }

        static ColonySite Ice(int body, string name = "Ice", bool nitrogen = true) => new ColonySite
        {
            Body = body, Name = name, Zone = HabitableZone.None, Capacity = 4e5,
            Resources = nitrogen ? new[] { Good.Water, Good.Nitrogen, Good.Hydrocarbons } : new[] { Good.Water },
        };

        static ColonySite Temperate(int body, string name = "Garden") => new ColonySite
        {
            Body = body, Name = name, Zone = HabitableZone.Conservative, Capacity = 2e6,
            Resources = new[] { Good.Water, Good.Nitrogen, Good.Titanium },
        };

        static ColonySite Scorched(int body, string name = "Cinder") => new ColonySite
        {
            Body = body, Name = name, Zone = HabitableZone.None, Capacity = 4e5,
            Resources = new[] { Good.NickelIron, Good.Titanium, Good.Copper },
        };

        // ---------------------------------------------------------------- the survey

        [Test]
        public void Survey_ReadsTheZoneAndTheGroundFromTheCatalogue()
        {
            var sol = BodyCatalog.CreateSolSystem();
            Assert.That(ColonySite.Survey(sol, sol["Sun"].Index), Is.Null, "a star is no site");
            Assert.That(ColonySite.Survey(sol, sol["Jupiter"].Index), Is.Null, "nor a gas giant");

            var earth = ColonySite.Survey(sol, sol["Earth"].Index);
            Assert.That(earth.Zone, Is.EqualTo(HabitableZone.Conservative));
            Assert.That(earth.Viable, Is.True);

            var moon = ColonySite.Survey(sol, sol["Moon"].Index);
            Assert.That(moon.Zone, Is.EqualTo(HabitableZone.Conservative), "a moon takes its planet's sunlight");

            var mercury = ColonySite.Survey(sol, sol["Mercury"].Index);
            Assert.That(mercury.Has(Good.Water), Is.False);
            Assert.That(mercury.Viable, Is.False, "a scorched world with no ice and outside the zone is no place to found a colony");
            Assert.That(mercury.Score, Is.EqualTo(0));

            var pluto = ColonySite.Survey(sol, sol["Pluto"].Index);
            Assert.That(pluto.Zone, Is.EqualTo(HabitableZone.None));
            Assert.That(pluto.Has(Good.Water) && pluto.Has(Good.Nitrogen), Is.True, "a big cold world has its ices");
            Assert.That(pluto.Viable, Is.True);
            Assert.That(earth.Score, Is.GreaterThan(pluto.Score), "the habitable zone counts most");
        }

        // ---------------------------------------------------------------- pressure from the population model

        [Test]
        public void Pressure_IsTheGrowthASettlementAtItsCapCannotTake()
        {
            var t = Create(homeBase: 5e5, homePeople: 2e6);
            t.Colonisation.Sites.Clear();
            var roomy = t.Economy.Add("Roomy", 2, SettlementKind.Colony, t.Owner, 5e5);
            for (int i = 0; i < GoodInfo.Count; i++) roomy.Stock[i] = Ample;
            t.Economy.Open(roomy);

            t.Economy.AdvanceTo(30);

            Assert.That(t.Home.Population, Is.EqualTo(2e6).Within(1), "a settlement at its cap stays there");
            Assert.That(t.Home.Overflow, Is.GreaterThan(1000), "and its growth is people with nowhere to go");
            Assert.That(roomy.Overflow, Is.EqualTo(0), "a settlement with room grows instead");
            Assert.That(roomy.Population, Is.GreaterThan(5e5));
        }

        [Test]
        public void Pressure_BuildsBeforeTheCap_InACrowdedPlace()
        {
            var t = Create(homeBase: 5e5, homePeople: 1.6e6);
            t.Colonisation.Sites.Clear();
            t.Economy.AdvanceTo(30);
            Assert.That(t.Home.Population, Is.GreaterThan(1.6e6), "a crowded place still grows");
            Assert.That(t.Home.Overflow, Is.GreaterThan(0), "but some of those born would rather go");
            Assert.That(People.Crowding(t.Home), Is.InRange(0.4, 0.6));
        }

        [Test]
        public void Pressure_CountsMigrantsTurnedAwayFromAFullPlace()
        {
            var economy = new Economy(new Diplomacy(), 0);
            var f = new Faction("F", "F", default);
            var poor = economy.Add("Poor", 1, SettlementKind.Colony, f, 1e6);
            var full = economy.Add("Full", 2, SettlementKind.Colony, f, 1e5);
            full.Population = full.InitialPopulation * People.MaxGrowth;
            poor.Wellbeing = 0.5;
            full.Wellbeing = 1;

            People.Migrate(economy.Settlements, economy.Diplomacy, 10);

            Assert.That(full.Population, Is.EqualTo(4e5).Within(1e-6), "nobody gets into a full place");
            Assert.That(poor.Population, Is.EqualTo(1e6).Within(1e-6));
            Assert.That(poor.Overflow, Is.GreaterThan(0), "those turned away are people with nowhere to go");
        }

        [Test]
        public void Pressure_IsHeldToAShareOfThePeople()
        {
            var economy = new Economy(new Diplomacy(), 0);
            var s = economy.Add("S", 1, SettlementKind.Colony, new Faction("F", "F", default), 1000);
            People.AddOverflow(s, 1e6);
            Assert.That(s.Overflow, Is.EqualTo(1000 * People.OverflowShare).Within(1e-9));
        }

        // ---------------------------------------------------------------- founding rules

        [Test]
        public void Founding_WaitsForAShipsWorthOfPeopleWithNoRoom()
        {
            var t = Create(sites: Ice(5));
            t.Home.Overflow = Colonisation.PartySize * 0.6;
            Assert.That(t.Colonisation.TryLaunch(t.Economy, t.Owner, 0), Is.Null, "not enough people want to go");

            t.Home.Overflow = Colonisation.PartySize * 1.2;
            var ship = t.Colonisation.TryLaunch(t.Economy, t.Owner, 0);
            Assert.That(ship, Is.Not.Null);
            Assert.That(ship.Site.Body, Is.EqualTo(5));
        }

        [Test]
        public void Founding_NeverHappensWithoutPressure_HoweverRichOrLong()
        {
            // A settlement with room to grow, years of sim time, a fortune and a fine site: nobody leaves.
            var t = Create(homeBase: 5e5, homePeople: 5e5, treasury: 1e12, sites: Temperate(5));
            t.Economy.AdvanceTo(3 * 365.25);
            Assert.That(t.Colonisation.Voyages, Is.Empty);
            Assert.That(t.Economy.Settlements.Count, Is.EqualTo(1));
            Assert.That(t.Home.Overflow, Is.EqualTo(0));
        }

        [Test]
        public void Founding_PrefersTheHabitableZone_AndNeverAWorldWithNoWater()
        {
            var t = Create(sites: new[] { Scorched(4), Ice(5), Temperate(6) });
            t.Home.Overflow = Colonisation.PartySize * 2;
            var ship = t.Colonisation.TryLaunch(t.Economy, t.Owner, 0);
            Assert.That(ship.Site.Name, Is.EqualTo("Garden"));

            var dry = Create(sites: Scorched(4));
            dry.Home.Overflow = Colonisation.PartySize * 2;
            Assert.That(dry.Colonisation.TryLaunch(dry.Economy, dry.Owner, 0), Is.Null, "no water, no zone: no colony");
        }

        [Test]
        public void Founding_SkipsTakenSites_AndNeedsTheMoney()
        {
            var t = Create(sites: new[] { Temperate(6), Ice(5) });
            t.Economy.Add("Squatters", 6, SettlementKind.Colony, new Faction("Other", "O", default), 1000);
            t.Home.Overflow = Colonisation.PartySize * 2;
            var ship = t.Colonisation.TryLaunch(t.Economy, t.Owner, 0);
            Assert.That(ship.Site.Body, Is.EqualTo(5), "the garden world is taken: the ice moon it is");

            var poor = Create(treasury: Colonisation.FoundingCost * 0.5, sites: Ice(5));
            poor.Home.Overflow = Colonisation.PartySize * 2;
            Assert.That(poor.Colonisation.TryLaunch(poor.Economy, poor.Owner, 0), Is.Null);
        }

        [Test]
        public void Founding_OneShipAtATime_AndPiratesNever()
        {
            var t = Create(sites: new[] { Ice(5), Ice(6, "Other ice") });
            t.Home.Overflow = Colonisation.PartySize * 3;
            Assert.That(t.Colonisation.TryLaunch(t.Economy, t.Owner, 0), Is.Not.Null);
            Assert.That(t.Colonisation.TryLaunch(t.Economy, t.Owner, 0), Is.Null, "the faction waits for its ship to land");

            var p = Create(sites: Ice(5));
            p.Owner.IsPirate = true;
            p.Home.Overflow = Colonisation.PartySize * 3;
            Assert.That(p.Colonisation.TryLaunch(p.Economy, p.Owner, 0), Is.Null);
        }

        // ---------------------------------------------------------------- the colony ship

        [Test]
        public void ColonyShip_CarriesColonistsAndStarterGoods_AndFoundsAnOutpostWhenItLands()
        {
            var t = Create(homeBase: 5e5, homePeople: 2e6, sites: Ice(5));
            t.Colonisation.TravelDays = (from, body, jd) => 40;
            double people = t.Home.Population, treasury = t.Owner.Treasury, food = t.Home.Stock[(int)Good.Food];
            t.Home.Overflow = Colonisation.PartySize;

            var ship = t.Colonisation.TryLaunch(t.Economy, t.Owner, 0);

            Assert.That(ship.Colonists, Is.EqualTo(Colonisation.PartySize).Within(1e-6));
            Assert.That(t.Home.Population, Is.EqualTo(people - ship.Colonists).Within(1e-6), "the colonists leave home");
            Assert.That(t.Home.Overflow, Is.EqualTo(0).Within(1e-6));
            double foodAboard = ship.Cargo[(int)Good.Food];
            Assert.That(foodAboard, Is.EqualTo(ship.Colonists * People.NeedOf(SettlementKind.MiningOutpost, Good.Food) * Colonisation.StarterDays).Within(1e-6));
            Assert.That(ship.Cargo[(int)Good.MedicalSupplies], Is.GreaterThan(0));
            Assert.That(ship.Cargo[(int)Good.Water], Is.GreaterThan(0));
            Assert.That(t.Home.Stock[(int)Good.Food], Is.EqualTo(food - foodAboard).Within(1e-3), "the starter goods are bought from home");
            Assert.That(t.Owner.Treasury, Is.LessThan(treasury - Colonisation.FoundingCost), "the faction pays for the voyage and the goods");
            Assert.That(ship.ArriveJd, Is.EqualTo(40));
            Assert.That(t.Economy.Settlements.Count, Is.EqualTo(1), "nothing is founded until it lands");

            t.Economy.AdvanceTo(60);

            Assert.That(t.Colonisation.Voyages, Does.Not.Contain(ship), "it has landed");
            var outpost = t.Economy.Settlements.Single(s => s.Body == 5);
            Assert.That(outpost.Stage, Is.EqualTo(ColonyStage.Outpost));
            Assert.That(outpost.Kind, Is.EqualTo(SettlementKind.MiningOutpost));
            Assert.That(outpost.Owner, Is.SameAs(t.Owner));
            Assert.That(outpost.Population, Is.GreaterThan(Colonisation.PartySize * 0.98));
            Assert.That(outpost.Facilities.Any(f => f.Recipe.Kind == IndustryKind.Farm), Is.True, "farms to feed itself");
            Assert.That(outpost.Facilities.Any(f => f.Recipe.Kind == IndustryKind.Mine && f.Recipe.Outputs[0].good == Good.Water), Is.True, "ice to melt");
            Assert.That(outpost.Facilities.Any(f => f.Recipe.Kind == IndustryKind.Mine && f.Recipe.Outputs[0].good == Good.Hydrocarbons), Is.True, "and a mine on its ore");
            Assert.That(outpost.Stock[(int)Good.MedicalSupplies], Is.GreaterThan(0), "the starter goods are landed");
        }

        [Test]
        public void ColonyShip_FindingItsSiteTaken_CarriesItsColonistsHome()
        {
            var t = Create(homeBase: 5e5, homePeople: 2e6, sites: Ice(5));
            t.Home.Overflow = Colonisation.PartySize;
            var ship = t.Colonisation.TryLaunch(t.Economy, t.Owner, 0);
            double home = t.Home.Population;
            t.Economy.Add("Claim jumpers", 5, SettlementKind.MiningOutpost, new Faction("Other", "O", default), 3000);

            Assert.That(t.Colonisation.Arrive(t.Economy, ship, ship.ArriveJd), Is.Null);
            Assert.That(t.Home.Population, Is.EqualTo(home + ship.Colonists).Within(1e-6));
        }

        // ---------------------------------------------------------------- growth

        [Test]
        public void Outpost_FedAndFull_IsBuiltOut_AndGrowsIntoAColony()
        {
            var rich = Ice(5);
            rich.Resources = new[] { Good.Water, Good.Nitrogen, Good.Hydrocarbons, Good.Titanium };
            var t = Create(sites: rich);
            var outpost = Colonisation.Found(t.Economy, t.Owner, t.Colonisation.Sites[0], 8000, 0);
            t.Economy.Open(outpost);
            int works = outpost.Facilities.Count;
            double farm = outpost.Facilities.First(f => f.Recipe.Kind == IndustryKind.Farm).BaseLevel;

            outpost.Population = outpost.InitialPopulation * People.MaxGrowth * 0.8;
            Assert.That(t.Colonisation.TryExpand(outpost), Is.True);
            Assert.That(outpost.InitialPopulation, Is.EqualTo(8000 * Colonisation.ExpandFactor).Within(1e-6), "the habitat is built out");
            Assert.That(outpost.Facilities.Count, Is.GreaterThan(works), "and a mine opened on the next ore");
            Assert.That(outpost.Stage, Is.EqualTo(ColonyStage.Outpost), "not a colony yet");

            outpost.Population = Colonisation.ColonyPopulation * 1.1;
            Colonisation.Provision(outpost);
            Assert.That(outpost.Facilities.First(f => f.Recipe.Kind == IndustryKind.Farm).BaseLevel, Is.GreaterThan(farm), "the farms grow with the people");
            Assert.That(outpost.Jobs, Is.GreaterThanOrEqualTo(Colonisation.ColonyJobs));
            Assert.That(t.Colonisation.TryPromote(outpost), Is.True);
            Assert.That(outpost.Stage, Is.EqualTo(ColonyStage.Colony));
            Assert.That(outpost.Kind, Is.EqualTo(SettlementKind.Colony));
        }

        [Test]
        public void Outpost_NotYetFull_OrHungry_IsNotBuiltOut()
        {
            var t = Create(sites: Ice(5));
            var outpost = Colonisation.Found(t.Economy, t.Owner, t.Colonisation.Sites[0], 8000, 0);
            outpost.Population = outpost.InitialPopulation * People.MaxGrowth * 0.5;
            Assert.That(t.Colonisation.TryExpand(outpost), Is.False, "room to spare");
            outpost.Population = outpost.InitialPopulation * People.MaxGrowth * 0.9;
            outpost.Wellbeing = 0.6;
            Assert.That(t.Colonisation.TryExpand(outpost), Is.False, "nobody builds out a hungry outpost");
            Assert.That(t.Colonisation.TryPromote(outpost), Is.False, "too few people for a colony");
        }

        [Test]
        public void Outpost_ReinforcedFromACrowdedHome_GrowsIntoAColonyOverSimTime()
        {
            // Home is at its cap: its growth is pressure, carried out by ship after ship to the young outpost.
            var t = Create(homeBase: 5e5, homePeople: 2e6, treasury: 1e11, sites: Ice(5));
            t.Colonisation.TravelDays = (from, body, jd) => 30;

            t.Economy.AdvanceTo(6 * 365.25);

            var outpost = t.Economy.Settlements.Single(s => s.Body == 5);
            Assert.That(outpost.Stage, Is.EqualTo(ColonyStage.Colony), $"{outpost.Population:N0} people, {outpost.Jobs:N0} jobs");
            Assert.That(outpost.Population, Is.GreaterThanOrEqualTo(Colonisation.ColonyPopulation));
            Assert.That(outpost.InitialPopulation, Is.GreaterThan(Colonisation.PartySize), "its habitat has been built out");
            Assert.That(outpost.Wellbeing, Is.GreaterThan(0.9));
        }

        // ---------------------------------------------------------------- failure

        [Test]
        public void Outpost_WithNoNitrogenAndNoSupply_ShrinksToARuin()
        {
            // An ice rock too small to hold nitrogen: the farms run on the nitrogen it was landed with, then stop.
            var t = Create(homeBase: 5e5, homePeople: 2e6, sites: Ice(5, "Pebble", nitrogen: false));
            t.Colonisation.TravelDays = (from, body, jd) => 30;
            t.Home.Overflow = Colonisation.PartySize;
            var ship = t.Colonisation.TryLaunch(t.Economy, t.Owner, 0);
            // No more colonists will come to prop it up: home is no longer short of room.
            t.Home.Population = t.Home.InitialPopulation;
            t.Economy.AdvanceTo(40);
            var outpost = t.Economy.Settlements.Single(s => s.Body == 5);
            Assert.That(outpost.Stage, Is.EqualTo(ColonyStage.Outpost));
            double founded = outpost.Population;
            double before = t.Home.Population;

            double min = founded;
            bool ruined = false;
            for (double jd = 50; jd < 5 * 365.25 && !ruined; jd += 10)
            {
                t.Economy.AdvanceTo(jd);
                ruined = outpost.Stage == ColonyStage.Ruin;
                if (!ruined) min = Math.Min(min, outpost.Population);
            }

            Assert.That(ruined, Is.True, "a colony that cannot feed itself fails");
            Assert.That(min, Is.LessThan(founded * 0.3), "it shrank to its hard core first");
            Assert.That(outpost.Fallen, Is.True);
            Assert.That(outpost.Population, Is.EqualTo(0));
            Assert.That(outpost.Facilities, Is.Empty);
            Assert.That(t.Home.Population, Is.GreaterThan(before), "its last settlers went home");
            Assert.That(Colonisation.Occupied(t.Economy, 5, outpost.RuinedJd + 1), Is.True, "a fresh ruin is not resettled at once");
            Assert.That(Colonisation.Occupied(t.Economy, 5, outpost.RuinedJd + Colonisation.RuinYears * 366), Is.False, "an old one may be");
        }

        [Test]
        public void ReFoundedSettlement_IsNumberedWithNoGap_U_SPC_T320()
        {
            var t = Create(sites: Ice(5, "Pebble"));
            var first = Colonisation.Found(t.Economy, t.Owner, t.Colonisation.Sites[0], 8000, 0);
            Assert.That(first.Name, Is.EqualTo("Pebble Landing"), "a world's first settlement is its landing");
            first.Stage = ColonyStage.Ruin;

            var second = Colonisation.Found(t.Economy, t.Owner, t.Colonisation.Sites[0], 8000, 1);
            Assert.That(second.Name, Is.EqualTo("New Pebble 2"), "the first re-founding is numbered, not left bare");
            second.Stage = ColonyStage.Ruin;

            var third = Colonisation.Found(t.Economy, t.Owner, t.Colonisation.Sites[0], 8000, 2);
            Assert.That(third.Name, Is.EqualTo("New Pebble 3"), "numbering carries on with no gap at 2");
        }

        [Test]
        public void Ruin_TakesNoMigrants_AndHasNoMarket()
        {
            var t = Create(sites: Ice(5));
            var outpost = Colonisation.Found(t.Economy, t.Owner, t.Colonisation.Sites[0], 8000, 0);
            t.Economy.Open(outpost);
            outpost.Population = outpost.InitialPopulation * People.MinShare;
            outpost.Wellbeing = 0.3;
            Assert.That(Colonisation.Failing(outpost), Is.True);
            t.Colonisation.Abandon(t.Economy, outpost, 100);

            Assert.That(People.CanMigrate(t.Economy.Diplomacy, t.Home, outpost), Is.False);
            t.Economy.AdvanceTo(30);
            Assert.That(outpost.Population, Is.EqualTo(0), "nobody comes back to a ruin");
            Assert.That(outpost.Stock.Sum(), Is.EqualTo(0));
        }
    }
}
