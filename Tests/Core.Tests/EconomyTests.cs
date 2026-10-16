using System;
using System.Linq;
using SolarSystem.Core;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    [TestFixture]
    public class EconomyTests
    {
        const double StartJd = 2461324.0;


        static World Create()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return World.CreateDefault(eph, StartJd);
        }

        static void Run(World w, double days, double step = 0.5, Action<double> each = null)
        {
            for (double jd = StartJd; jd <= StartJd + days; jd += step)
            {
                w.Ephemeris.Update(jd);
                w.Update(jd);
                each?.Invoke(jd);
            }
        }

        [Test]
        public void EveryGoodIsProducedSomewhereInRoughlyTheAmountUsed()
        {
            var w = Create();
            for (int g = 0; g < GoodInfo.Count; g++)
            {
                double supply = w.Economy.Settlements.Sum(s => s.Supply[g]);
                double demand = w.Economy.Settlements.Sum(s => s.Demand[g]);
                TestContext.Out.WriteLine($"{(Good)g,-16} supply {supply,10:N0}  demand {demand,10:N0}  ratio {supply / Math.Max(demand, 1e-9),6:0.00}");
                if (demand > 0) Assert.That(supply >= demand * 0.95, Is.True, $"{(Good)g}: {supply:N0} t/day made, {demand:N0} used");
                Assert.That(supply > 0, Is.True, $"{(Good)g} is never produced");
            }
        }

        [Test]
        public void PricesRiseWhenStockRunsShort()
        {
            var w = Create();
            var mars = w.Economy.Settlements.First(s => s.Name == "Ares Prime");
            double normal = mars.PriceAt(Good.Food, mars.Target(Good.Food));
            double scarce = mars.PriceAt(Good.Food, mars.Target(Good.Food) * 0.1);
            double glut = mars.PriceAt(Good.Food, mars.Target(Good.Food) * 3);
            Assert.That(normal / GoodInfo.Of(Good.Food).BasePrice, Is.InRange(0.85, 1.25));
            Assert.That(scarce > normal * 2.5, Is.True);
            Assert.That(glut < normal * 0.6, Is.True);
        }

        [Test]
        public void TwoYearsOfTradeKeepEveryoneFedAndTradersInProfit()
        {
            var w = Create();
            var settlements = w.Economy.Settlements;
            var hungerDays = new double[settlements.Count];
            Run(w, 730, 0.5, jd =>
            {
                for (int i = 0; i < settlements.Count; i++)
                {
                    var s = settlements[i];
                    for (int g = 0; g < GoodInfo.Count; g++)
                    {
                        Assert.That(double.IsNaN(s.Stock[g]) || s.Stock[g] < -1e-6, Is.False, $"{s.Name} {(Good)g} stock {s.Stock[g]}");
                        Assert.That(double.IsNaN(s.Price[g]), Is.False);
                    }
                    if (jd > StartJd + 365 && (s.Satisfaction[(int)Good.Food] < 0.9 || s.Satisfaction[(int)Good.Water] < 0.9)) hungerDays[i] += 0.5;
                }
            });

            foreach (var s in settlements)
            {
                var prices = string.Join(" ", Enumerable.Range(0, GoodInfo.Count)
                    .Where(g => s.Demand[g] > 0 || s.Supply[g] > 0)
                    .Select(g => $"{(Good)g}:{s.Price[g] / GoodInfo.Of((Good)g).BasePrice:0.00}"));
                TestContext.Out.WriteLine($"{s.Name,-20} pop {s.Population / 1e3,8:N0}k  hungry {hungerDays[s.Id],5:0}d  {prices}");
            }
            var traders = w.Fleet.Ships.Where(w.IsTrader).ToList();
            foreach (var t in traders.OrderByDescending(t => t.Credits).Take(5).Concat(traders.OrderBy(t => t.Credits).Take(3)))
                TestContext.Out.WriteLine($"{t.Name,-24} {t.Spec.DisplayName,-10} trips {t.TripsCompleted,3}  profit {t.Credits / 1e6,8:N1} M");
            foreach (var f in w.Fleet.Factions) TestContext.Out.WriteLine($"{f.Name,-22} treasury {f.Treasury / 1e9,7:0.00} B");
            TestContext.Out.WriteLine($"ships {w.Fleet.Ships.Count}, built {w.Events.Count(e => e.Kind == WorldEventKind.Launch)}");

            for (int i = 0; i < settlements.Count; i++)
                Assert.That(hungerDays[i] < 60, Is.True, $"{settlements[i].Name} went hungry for {hungerDays[i]} of the last 365 days");
            Assert.That(traders.Sum(t => t.TripsCompleted) > traders.Count * 3, Is.True);
            Assert.That(traders.Count(t => t.Credits > 0) > traders.Count * 0.7, Is.True, "most traders should be in profit");
            foreach (var s in settlements)
                for (int g = 0; g < GoodInfo.Count; g++)
                    Assert.That(s.Price[g] / GoodInfo.Of((Good)g).BasePrice, Is.InRange(0.19, 6.01));
        }

        [Test]
        public void TradersOnlyDockWhereTheyAreWelcome()
        {
            var w = Create();
            Run(w, 200, 1.0);
            foreach (var t in w.Fleet.Ships.Where(w.IsTrader))
            {
                if (t.Destroyed || t.CargoDestination == null) continue;
                Assert.That(w.Diplomacy.CanDock(t.Faction, t.CargoDestination.Owner), Is.True, $"{t.Name} ({t.Faction}) bound for {t.CargoDestination.Name} ({t.CargoDestination.Owner})");
            }
        }
    }
}
