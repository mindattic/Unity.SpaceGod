using System;
using System.Collections.Generic;
using System.Linq;
using SolarSystem.Core;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    /// <summary>Economic events driven by sim state (U.SPC-T58): each shock moves the markets and is reported in the news as it happens.</summary>
    [TestFixture]
    public class MarketShocksTests
    {
        const double StartJd = 2461324.0;

        static World Create()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return World.CreateDefault(eph, StartJd);
        }

        static List<WorldEvent> Watch(World w)
        {
            var news = new List<WorldEvent>();
            w.Logged += news.Add;
            return news;
        }

        /// <summary>Only the shocks and the markets, a day at a time (no fleet or war), so a test sets the scene itself.</summary>
        static void Days(World w, ref double jd, int days, bool markets = true)
        {
            for (int i = 0; i < days; i++)
            {
                jd += 1;
                if (markets) w.Economy.AdvanceTo(jd);
                w.Shocks.Update(jd);
            }
        }

        static Settlement Port(World w, string name) => w.Economy.Settlements.First(s => s.Name == name);

        static Facility OreMine(Settlement s) => s.Facilities.First(f => f.Recipe.Kind == IndustryKind.Mine && GoodInfo.Of(f.Recipe.Outputs[0].good).Category == GoodCategory.Mineral);

        [Test]
        public void AnAccidentCutsAMinesOutputUntilItIsClearedAndBothAreNews()
        {
            var w = Create();
            var news = Watch(w);
            var ares = Port(w, "Ares Prime");
            var mine = OreMine(ares);
            double jd = w.Economy.Jd;
            w.Shocks.AccidentPerDay = 1e9;
            Days(w, ref jd, 1);
            w.Shocks.AccidentPerDay = 0;

            var accident = w.Shocks.Active.First(e => e.Kind == ShockKind.MineAccident && e.Facility == mine);
            Assert.That(mine.Impairment, Is.EqualTo(0.2));
            Assert.That(news.Any(e => e.Kind == WorldEventKind.Economy && e.Text.StartsWith("Accident") && e.Text.Contains("Ares Prime") && e.Body == ares.Body), Is.True);
            Days(w, ref jd, 1);
            Assert.That(mine.Utilisation, Is.LessThanOrEqualTo(0.2 + 1e-9), "the mine works at most a fifth of its capacity");

            Days(w, ref jd, (int)Math.Ceiling(accident.EndJd - jd) + 1);
            Assert.That(mine.Impairment, Is.EqualTo(1));
            Assert.That(w.Shocks.Active.Contains(accident), Is.False);
            Assert.That(news.Any(e => e.Text.Contains("back at work") && e.Text.Contains("Ares Prime")), Is.True);
        }

        [Test]
        public void AWorkedOutSeamCutsTheMineForGoodAndAFreshStrikeComesAfterRepeatedCuts()
        {
            var w = Create();
            var news = Watch(w);
            var ares = Port(w, "Ares Prime");
            var mine = OreMine(ares);
            double jd = w.Economy.Jd;
            w.Shocks.AccidentPerDay = 0;
            Days(w, ref jd, 1, markets: false);
            Assert.That(mine.Reserve, Is.GreaterThan(0), "the seam is surveyed");
            double original = mine.BaseLevel;

            mine.Extracted = mine.Reserve;
            Days(w, ref jd, 1, markets: false);
            Assert.That(mine.BaseLevel, Is.EqualTo(original * 0.6).Within(1e-9));
            Assert.That(mine.Extracted, Is.Zero);
            Assert.That(news.Any(e => e.Kind == WorldEventKind.Economy && e.Text.Contains("playing out") && e.Text.Contains("Ares Prime")), Is.True);

            // Worked out again and again, it does not dwindle to nothing: prospectors find another seam.
            for (int i = 0; i < 3; i++)
            {
                mine.Extracted = mine.Reserve;
                Days(w, ref jd, 1, markets: false);
            }
            Assert.That(news.Any(e => e.Text.Contains("strike a new")), Is.True);
            Assert.That(mine.BaseLevel, Is.GreaterThanOrEqualTo(original * 0.36 - 1e-9));
        }

        [Test]
        public void ABlightSpoilsFoodAndCutsTheHarvestSoFoodGetsDearer()
        {
            var w = Create();
            var news = Watch(w);
            var luna = Port(w, "Tycho City");
            var farm = luna.Facilities.First(f => f.Recipe.Kind == IndustryKind.Farm);
            double jd = w.Economy.Jd;
            w.Shocks.AccidentPerDay = 0;
            double food = luna.Stock[(int)Good.Food], price = luna.Price[(int)Good.Food];
            w.Shocks.BlightPerDay = 1e9;
            Days(w, ref jd, 1, markets: false);
            w.Shocks.BlightPerDay = 0;

            Assert.That(farm.Impairment, Is.EqualTo(0.35));
            int blights = w.Shocks.Active.Count(e => e.Kind == ShockKind.Blight && e.Settlement == luna);
            Assert.That(luna.Stock[(int)Good.Food], Is.EqualTo(food * Math.Pow(0.8, blights)).Within(1e-6), "each blighted farm spoils a fifth of the food");
            Assert.That(luna.Price[(int)Good.Food], Is.GreaterThan(price));
            Assert.That(news.Any(e => e.Kind == WorldEventKind.Economy && e.Text.StartsWith("Blight") && e.Text.Contains("Tycho City")), Is.True);
        }

        [Test]
        public void WorkersStrikeWhenWagesLagPricesAndWinARaise()
        {
            var w = Create();
            var news = Watch(w);
            var ares = Port(w, "Ares Prime");
            double jd = w.Economy.Jd;
            w.Shocks.AccidentPerDay = w.Shocks.BlightPerDay = 0;
            Days(w, ref jd, 1, markets: false);
            // Prices have run away from what wages buy (frozen here: no market ticks).
            ares.WageIndex = ares.CostOfLiving() / 2;
            Assert.That(ares.IndustryFactor, Is.EqualTo(1));
            EconomicShock strike = null;
            for (int i = 0; i < 200 && strike == null; i++)
            {
                Days(w, ref jd, 1, markets: false);
                strike = w.Shocks.Active.FirstOrDefault(e => e.Kind == ShockKind.Strike && e.Settlement == ares);
                if (strike == null) ares.WageIndex = ares.CostOfLiving() / 2;
            }
            Assert.That(strike, Is.Not.Null, "a strike is called");
            Assert.That(strike.StartJd - w.Economy.Jd, Is.GreaterThanOrEqualTo(-1e-9));
            Assert.That(ares.IndustryFactor, Is.EqualTo(0.15));
            Assert.That(news.Any(e => e.Kind == WorldEventKind.Economy && e.Text.StartsWith("Workers at Ares Prime") && e.Text.Contains("strike")), Is.True);

            // Industry stops but life support runs: a strike-bound mine makes almost nothing.
            foreach (var g in new[] { Good.Water, Good.Nitrogen, Good.HeliumThree, Good.Machinery }) ares.Stock[(int)g] = ares.Target(g) * 2;
            ares.Stock[(int)Good.Food] = 0;
            w.Economy.AdvanceTo(jd + 0.5);
            Assert.That(OreMine(ares).Utilisation, Is.LessThanOrEqualTo(0.15 + 1e-9));
            Assert.That(ares.Facilities.First(f => f.Recipe.Kind == IndustryKind.Farm).Utilisation, Is.GreaterThan(0.15));

            double wage = ares.WageIndex;
            jd = w.Economy.Jd;
            Days(w, ref jd, (int)Math.Ceiling(strike.EndJd - jd) + 1, markets: false);
            Assert.That(ares.IndustryFactor, Is.EqualTo(1));
            Assert.That(ares.WageIndex, Is.GreaterThan(wage * 1.2), "the settlement closes most of the gap");
            Assert.That(news.Any(e => e.Text.StartsWith("The strike at Ares Prime (Mars) ends")), Is.True);
        }

        [Test]
        public void ANavyAtWarShortOfEssentialsRationsItsPortsUntilTheFightingStops()
        {
            var w = Create();
            var news = Watch(w);
            double jd = w.Economy.Jd;
            w.Shocks.AccidentPerDay = w.Shocks.BlightPerDay = 0;
            Assert.That(w.Diplomacy.Get(w.Sol, w.Reach), Is.EqualTo(Relation.War));
            var sol = w.Economy.Settlements.Where(s => s.Owner == w.Sol).ToList();
            var earth = Port(w, "Earth Orbital");
            double demand = earth.Demand[(int)Good.Food];
            foreach (var s in sol) s.Stock[(int)Good.Food] = 0;
            Days(w, ref jd, 1, markets: false);

            Assert.That(w.Shocks.Active.Any(e => e.Kind == ShockKind.Rationing && e.Faction == w.Sol), Is.True);
            Assert.That(sol.All(s => s.Ration == 0.8), Is.True);
            Assert.That(w.Economy.Settlements.Where(s => s.Owner == w.Belt).All(s => s.Ration == 1), Is.True);
            Assert.That(news.Any(e => e.Kind == WorldEventKind.Economy && e.Text.StartsWith("The Sol Federation brings in war rationing")), Is.True);
            w.Economy.AdvanceTo(jd + 0.25);
            Assert.That(earth.Demand[(int)Good.Food], Is.LessThan(demand), "rationed people buy less food");

            w.Diplomacy.Set(w.Sol, w.Reach, Relation.Ceasefire);
            jd = w.Economy.Jd;
            Days(w, ref jd, 31, markets: false);
            Assert.That(w.Shocks.Active.Any(e => e.Kind == ShockKind.Rationing), Is.False);
            Assert.That(sol.All(s => s.Ration == 1), Is.True);
            Assert.That(news.Any(e => e.Text.StartsWith("The Sol Federation ends war rationing")), Is.True);
        }

        [Test]
        public void ABesiegerDeclaresABlockadeThroughDiplomacyAndNoShipDocksUntilItLifts()
        {
            var w = Create();
            var news = Watch(w);
            double jd = w.Economy.Jd;
            w.Shocks.AccidentPerDay = w.Shocks.BlightPerDay = 0;
            var titan = Port(w, "Kraken Port");
            Faction besieger = w.Sol;
            w.Shocks.BesiegerOf = s => s == titan ? besieger : null;
            var trader = w.Fleet.Ships.First(s => s.Faction == w.Reach && w.IsTrader(s));
            Assert.That(w.Economy.CanTrade(trader, titan), Is.True);

            Days(w, ref jd, 1, markets: false);
            Assert.That(w.Diplomacy.BlockadedBy(titan), Is.SameAs(w.Sol));
            Assert.That(w.Economy.CanTrade(trader, titan), Is.False, "the owner's own ships are shut out too");
            Assert.That(news.Any(e => e.Kind == WorldEventKind.Economy && e.Text.StartsWith("The Sol Federation declares a blockade of Kraken Port") && e.Body == titan.Body), Is.True);

            // A day or two of the group coming and going does not lift it; gone for three days, it does.
            besieger = null;
            Days(w, ref jd, 2, markets: false);
            Assert.That(w.Diplomacy.BlockadedBy(titan), Is.SameAs(w.Sol));
            Days(w, ref jd, 1, markets: false);
            Assert.That(w.Diplomacy.BlockadedBy(titan), Is.Null);
            Assert.That(w.Economy.CanTrade(trader, titan), Is.True);
            Assert.That(news.Any(e => e.Text.StartsWith("The Sol Federation lifts its blockade of Kraken Port")), Is.True);
        }

        [Test]
        public void ANeutralRunningMunitionsToTheEnemyIsEmbargoedUntilTheWarStops()
        {
            var w = Create();
            var news = Watch(w);
            double jd = w.Economy.Jd;
            w.Shocks.AccidentPerDay = w.Shocks.BlightPerDay = 0;
            var belt = w.Fleet.Ships.First(s => s.Faction == w.Belt && w.IsTrader(s));
            var titan = Port(w, "Kraken Port");
            var earth = Port(w, "Earth Orbital");
            Assert.That(w.Economy.CanTrade(belt, earth), Is.True);

            w.Economy.Sold(belt, titan, Good.Munitions, w.Shocks.EmbargoThreshold * 1.2);
            Days(w, ref jd, 1, markets: false);
            Assert.That(w.Diplomacy.IsEmbargoed(w.Sol, w.Belt), Is.True);
            Assert.That(w.Diplomacy.IsEmbargoed(w.Reach, w.Belt), Is.False);
            Assert.That(w.Economy.CanTrade(belt, earth), Is.False, "Sol ports are closed to Belt ships");
            Assert.That(w.Economy.CanTrade(belt, titan), Is.True);
            Assert.That(news.Any(e => e.Kind == WorldEventKind.Diplomacy && e.Text.StartsWith("The Sol Federation embargoes the Belt Consortium")), Is.True);

            w.Diplomacy.Set(w.Sol, w.Reach, Relation.Ceasefire);
            Days(w, ref jd, 1, markets: false);
            Assert.That(w.Diplomacy.IsEmbargoed(w.Sol, w.Belt), Is.False);
            Assert.That(w.Economy.CanTrade(belt, earth), Is.True);
            Assert.That(news.Any(e => e.Text.StartsWith("The Sol Federation lifts its embargo on the Belt Consortium")), Is.True);
        }

        [Test]
        public void OnlyWarGoodsSoldToANavysEnemyCountAgainstTheSeller()
        {
            var w = Create();
            var belt = w.Fleet.Ships.First(s => s.Faction == w.Belt && w.IsTrader(s));
            double sol = w.Shocks.Contraband(w.Sol, w.Belt), reach = w.Shocks.Contraband(w.Reach, w.Belt);
            w.Economy.Sold(belt, Port(w, "Kraken Port"), Good.Food, 1e6);
            w.Economy.Sold(belt, Port(w, "Ganymede Union"), Good.Munitions, 1e6);
            w.Economy.Sold(belt, Port(w, "Earth Orbital"), Good.Water, 1e6);
            Assert.That(w.Shocks.Contraband(w.Sol, w.Belt), Is.EqualTo(sol));
            Assert.That(w.Shocks.Contraband(w.Reach, w.Belt), Is.EqualTo(reach));
            w.Economy.Sold(belt, Port(w, "Earth Orbital"), Good.Electronics, 1000);
            Assert.That(w.Shocks.Contraband(w.Reach, w.Belt), Is.EqualTo(reach + 500).Within(1e-6), "electronics count half");
        }

        [Test]
        public void APriceSpikeIsReportedOnceWithItsCause()
        {
            var w = Create();
            var news = Watch(w);
            double jd = w.Economy.Jd;
            w.Shocks.AccidentPerDay = w.Shocks.BlightPerDay = 0;
            var titan = Port(w, "Kraken Port");
            w.Shocks.BesiegerOf = s => s == titan ? w.Sol : null;
            for (int i = 0; i < 10; i++)
            {
                titan.Stock[(int)Good.Food] = 0;
                titan.RefreshPrices();
                Days(w, ref jd, 1, markets: false);
            }
            var spikes = news.Where(e => e.Kind == WorldEventKind.Market && e.Text.StartsWith("Food at Kraken Port")).ToList();
            Assert.That(spikes.Count, Is.EqualTo(1));
            Assert.That(spikes[0].Text, Does.Contain("blockaded by the Sol Federation"));
            Assert.That(spikes[0].Body, Is.EqualTo(titan.Body));
        }

        [Test]
        public void ShocksHappenOnTheirOwnInARunningSystemAndKeepItFed()
        {
            var w = Create();
            var news = Watch(w);
            for (double jd = StartJd; jd <= StartJd + 365; jd += 0.5)
            {
                w.Ephemeris.Update(jd);
                w.Update(jd);
            }
            var economic = news.Where(e => e.Kind == WorldEventKind.Economy).ToList();
            TestContext.Out.WriteLine(string.Join("\n", economic.Select(e => e.Text)));
            Assert.That(economic.Count, Is.InRange(5, 400));
            Assert.That(news.Count(e => e.Kind == WorldEventKind.Market), Is.GreaterThan(0));
            // A haven a navy has stormed is emptied by the war, not by hunger (its scanners may find one in a year).
            foreach (var s in w.Economy.Settlements)
                if (!s.Fallen) Assert.That(s.Population, Is.GreaterThan(s.InitialPopulation * 0.5), s.Name);
        }
    }
}
