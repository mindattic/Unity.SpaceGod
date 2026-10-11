using System;
using System.Linq;
using SolarSystem.Core;
using Xunit;
using Xunit.Abstractions;

namespace SolarSystem.Core.Tests
{
    public class EconomyTests
    {
        const double StartJd = 2461324.0;
        readonly ITestOutputHelper output;

        public EconomyTests(ITestOutputHelper output) => this.output = output;

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

        [Fact]
        public void EveryGoodIsProducedSomewhereInRoughlyTheAmountUsed()
        {
            var w = Create();
            for (int g = 0; g < GoodInfo.Count; g++)
            {
                double supply = w.Economy.Settlements.Sum(s => s.Supply[g]);
                double demand = w.Economy.Settlements.Sum(s => s.Demand[g]);
                output.WriteLine($"{(Good)g,-16} supply {supply,10:N0}  demand {demand,10:N0}  ratio {supply / Math.Max(demand, 1e-9),6:0.00}");
                if (demand > 0) Assert.True(supply >= demand * 0.95, $"{(Good)g}: {supply:N0} t/day made, {demand:N0} used");
                Assert.True(supply > 0, $"{(Good)g} is never produced");
            }
        }

        [Fact]
        public void PricesRiseWhenStockRunsShort()
        {
            var w = Create();
            var mars = w.Economy.Settlements.First(s => s.Name == "Ares Prime");
            double normal = mars.PriceAt(Good.Food, mars.Target(Good.Food));
            double scarce = mars.PriceAt(Good.Food, mars.Target(Good.Food) * 0.1);
            double glut = mars.PriceAt(Good.Food, mars.Target(Good.Food) * 3);
            Assert.InRange(normal / GoodInfo.Of(Good.Food).BasePrice, 0.85, 1.25);
            Assert.True(scarce > normal * 2.5);
            Assert.True(glut < normal * 0.6);
        }

        [Fact]
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
                        Assert.False(double.IsNaN(s.Stock[g]) || s.Stock[g] < -1e-6, $"{s.Name} {(Good)g} stock {s.Stock[g]}");
                        Assert.False(double.IsNaN(s.Price[g]));
                    }
                    if (jd > StartJd + 365 && (s.Satisfaction[(int)Good.Food] < 0.9 || s.Satisfaction[(int)Good.Water] < 0.9)) hungerDays[i] += 0.5;
                }
            });

            foreach (var s in settlements)
            {
                var prices = string.Join(" ", Enumerable.Range(0, GoodInfo.Count)
                    .Where(g => s.Demand[g] > 0 || s.Supply[g] > 0)
                    .Select(g => $"{(Good)g}:{s.Price[g] / GoodInfo.Of((Good)g).BasePrice:0.00}"));
                output.WriteLine($"{s.Name,-20} pop {s.Population / 1e3,8:N0}k  hungry {hungerDays[s.Id],5:0}d  {prices}");
            }
            var traders = w.Fleet.Ships.Where(w.IsTrader).ToList();
            foreach (var t in traders.OrderByDescending(t => t.Credits).Take(5).Concat(traders.OrderBy(t => t.Credits).Take(3)))
                output.WriteLine($"{t.Name,-24} {t.Spec.DisplayName,-10} trips {t.TripsCompleted,3}  profit {t.Credits / 1e6,8:N1} M");
            foreach (var f in w.Fleet.Factions) output.WriteLine($"{f.Name,-22} treasury {f.Treasury / 1e9,7:0.00} B");
            output.WriteLine($"ships {w.Fleet.Ships.Count}, built {w.Events.Count(e => e.Kind == WorldEventKind.Launch)}");

            for (int i = 0; i < settlements.Count; i++)
                Assert.True(hungerDays[i] < 60, $"{settlements[i].Name} went hungry for {hungerDays[i]} of the last 365 days");
            Assert.True(traders.Sum(t => t.TripsCompleted) > traders.Count * 3);
            Assert.True(traders.Count(t => t.Credits > 0) > traders.Count * 0.7, "most traders should be in profit");
            foreach (var s in settlements)
                for (int g = 0; g < GoodInfo.Count; g++)
                    Assert.InRange(s.Price[g] / GoodInfo.Of((Good)g).BasePrice, 0.19, 6.01);
        }

        [Fact]
        public void TradersOnlyDockWhereTheyAreWelcome()
        {
            var w = Create();
            Run(w, 200, 1.0);
            foreach (var t in w.Fleet.Ships.Where(w.IsTrader))
            {
                if (t.Destroyed || t.CargoDestination == null) continue;
                Assert.True(w.Diplomacy.CanDock(t.Faction, t.CargoDestination.Owner), $"{t.Name} ({t.Faction}) bound for {t.CargoDestination.Name} ({t.CargoDestination.Owner})");
            }
        }
    }
}
