using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace UnitySpace.Core.Tests
{
    /// <summary>U.SPC-T90: the twelve nearby systems settled, each with its own economy and traffic.</summary>
    [TestFixture]
    public class NearbySocietiesTests
    {
        const double StartJd = 2461324.0;

        static IEnumerable<string> Systems() => NearbySystems.All().Select(s => s.Name);

        static SystemSpec SpecOf(string name) => NearbySystems.All().First(s => s.Name == name);

        [Test]
        public void EverySystemIsSettledOnItsRealBodiesWherePlausible()
        {
            foreach (var spec in NearbySystems.All())
            {
                var plain = StarSystem.Create(spec, StartJd);
                var settled = StarSystem.CreateSettled(SpecOf(spec.Name), StartJd);
                var w = settled.World;
                Assert.That(settled.Settled, Is.True, spec.Name);

                // The real bodies are untouched: the same bodies, in the same order, on the same orbits.
                for (int i = 0; i < plain.Catalog.Count; i++)
                {
                    var a = plain.Catalog[i];
                    var b = settled.Catalog[i];
                    Assert.That(b.Name, Is.EqualTo(a.Name), spec.Name);
                    if (a.Elements != null)
                    {
                        Assert.That(b.Elements.AKm, Is.EqualTo(a.Elements.AKm), b.Name);
                        Assert.That(b.Elements.E, Is.EqualTo(a.Elements.E), b.Name);
                    }
                    Assert.That(b.Gm, Is.EqualTo(a.Gm), b.Name);
                    Assert.That(b.RadiusKm, Is.EqualTo(a.RadiusKm), b.Name);
                    Assert.That((settled.Ephemeris.Helio(i) - plain.Ephemeris.Helio(i)).Length, Is.LessThan(1e-3), b.Name);
                }
                // Anything added is an invented station on its own orbit, and says so.
                for (int i = plain.Catalog.Count; i < settled.Catalog.Count; i++)
                {
                    Assert.That(settled.Catalog[i].Kind, Is.EqualTo(BodyKind.Station), settled.Catalog[i].Name);
                    Assert.That(settled.Catalog[i].Status, Is.EqualTo(BodyStatus.Fictional), settled.Catalog[i].Name);
                    Assert.That(settled.Catalog[i].Summary, Does.StartWith("Fiction."), settled.Catalog[i].Name);
                }

                foreach (var s in w.Economy.Settlements)
                {
                    var body = settled.Catalog[s.Body];
                    // Never on a star; a colony only on a habitable-zone world.
                    Assert.That(body.IsStar, Is.False, s.Name);
                    if (s.Kind == SettlementKind.Colony)
                        Assert.That(body.HabitableZone, Is.Not.EqualTo(HabitableZone.None), $"{s.Name} on {body.Name} is outside the habitable zone");
                    // A world too hot or cold to live on is mined from outposts and skimmers, not colonised.
                    if (body.Kind != BodyKind.Station && body.HabitableZone == HabitableZone.None)
                        Assert.That(s.Kind, Is.Not.EqualTo(SettlementKind.Colony), s.Name);
                }
                // Markets, a shipyard or two, and its own traffic of every kind.
                var ports = w.Economy.Settlements.Where(s => s.Kind != SettlementKind.Haven).ToList();
                Assert.That(ports.Count, Is.GreaterThanOrEqualTo(2), spec.Name);
                int yards = ports.Count(s => s.Facilities.Any(f => f.Recipe.Kind == IndustryKind.Shipyard));
                Assert.That(yards, Is.InRange(1, 2), spec.Name);
                var ships = w.Fleet.Ships.Where(s => !s.Destroyed && !s.IsEscort).ToList();
                foreach (var role in new[] { ShipRole.Trader, ShipRole.Miner, ShipRole.Courier, ShipRole.Patrol })
                    Assert.That(ships.Any(s => s.Role == role), Is.True, $"{spec.Name} has no {role}");
                // Every port can be reached from every other by the system's lanes.
                var lanes = w.Fleet.Lanes;
                var systems = ports.Select(s => lanes.SystemOf(s.Body)).Distinct().ToList();
                foreach (int a in systems)
                    foreach (int b in systems)
                        if (a != b) Assert.That(lanes.Route(a, b, StartJd), Is.Not.Null, $"{spec.Name}: no lane route from {settled.Catalog[a].Name} to {settled.Catalog[b].Name}");
                TestContext.Out.WriteLine($"{spec.Name,-17} {spec.DistanceLy,5:0.00} ly  {w.Economy.Settlements.Sum(s => s.Population) / 1e3,6:N0}k people in {ports.Count} ports, " +
                                          $"{ships.Count} ships ({string.Join(", ", ships.GroupBy(s => s.Role).Select(g => g.Count() + " " + g.Key))})");
            }
        }

        [Test]
        public void TheFrontierThinsWithDistanceFromSol()
        {
            var people = NearbySystems.All().ToDictionary(s => s.Name, s => NearbySocieties.For(s).Population);
            var distance = NearbySystems.All().ToDictionary(s => s.Name, s => s.DistanceLy);
            var centauri = StarSystem.CreateAlphaCentauri(StartJd).World.Economy.Settlements.Sum(s => s.Population);
            foreach (var (name, p) in people)
            {
                // Never as built up as Centauri, and no more than its share at that distance of a system twice as rich.
                Assert.That(p, Is.LessThan(centauri), name);
                Assert.That(p, Is.LessThan(2 * centauri * SocietySpec.Frontier(distance[name])), name);
            }
            // The nearest few hold far more people than the farthest few.
            var byDistance = people.OrderBy(kv => distance[kv.Key]).Select(kv => kv.Value).ToList();
            Assert.That(byDistance.Take(4).Average(), Is.GreaterThan(1.5 * byDistance.Skip(8).Average()));
            Assert.That(SocietySpec.Frontier(AlphaCentauri.DistanceLy), Is.EqualTo(1).Within(1e-9));
            Assert.That(SocietySpec.Frontier(12.5), Is.LessThan(0.13));
        }

        [Test]
        public void EachSystemMakesALittleMoreOfEveryGoodThanItUses()
        {
            foreach (var spec in NearbySystems.All())
            {
                var w = StarSystem.CreateSettled(spec, StartJd).World;
                var ports = w.Economy.Settlements.Where(s => s.Kind != SettlementKind.Haven).ToList();
                for (int g = 0; g < GoodInfo.Count; g++)
                {
                    double demand = ports.Sum(s => s.PersonNeeds.Where(n => (int)n.good == g).Sum(n => n.perPersonDay) * s.Population +
                                                   s.Facilities.Sum(f => f.Recipe.Inputs.Where(i => (int)i.good == g).Sum(i => i.perDay) * f.BaseLevel));
                    double supply = ports.Sum(s => s.Facilities.Sum(f => f.Recipe.Outputs.Where(o => (int)o.good == g).Sum(o => o.perDay) * f.BaseLevel));
                    if (demand > 0) Assert.That(supply, Is.GreaterThanOrEqualTo(demand * 1.1), $"{spec.Name}: {(Good)g} made {supply:0.0} t/day, used {demand:0.0}");
                }
                // Every settlement has the hands to run what it was given.
                foreach (var s in ports)
                    Assert.That(s.Staffing, Is.GreaterThan(0.6), $"{spec.Name}: {s.Name} is short of workers");
            }
        }

        [Test]
        public void ASocietyIsOnlyFictionOnTopOfTheCatalogue()
        {
            // The plain catalogue entry stays unsettled; settling it is a separate step.
            foreach (var spec in NearbySystems.All())
                Assert.That(spec.Stations, Is.Empty, spec.Name);
            var built = StarSystem.Create(NearbySystems.TauCeti(), StartJd);
            Assert.That(built.Settled, Is.False);
        }
    }

    /// <summary>U.SPC-T90: a year of every nearby society, each in its own world, without its economy collapsing.</summary>
    [TestFixture, Parallelizable(ParallelScope.Children)]
    public class NearbySocietyYearTests
    {
        const double StartJd = 2461324.0;

        static IEnumerable<string> Systems() => NearbySystems.All().Select(s => s.Name);

        [TestCaseSource(nameof(Systems))]
        public void RunsAYearWithoutCollapse(string name)
        {
            var spec = NearbySystems.All().First(s => s.Name == name);
            var sys = StarSystem.CreateSettled(spec, StartJd);
            var w = sys.World;
            // Run as a system the player is not in runs: six-hourly, at coarse detail.
            w.Coarse = true;
            var settlements = w.Economy.Settlements;
            var hungryDays = new double[settlements.Count];
            var initial = settlements.Select(s => s.Population).ToArray();
            int merchantsAtStart = w.Fleet.Ships.Count(s => !s.Destroyed && w.IsTrader(s));
            const double step = 0.25;
            for (double jd = StartJd + step; jd <= StartJd + 365 + 1e-9; jd += step)
            {
                sys.Update(jd);
                foreach (var s in settlements)
                {
                    for (int g = 0; g < GoodInfo.Count; g++)
                    {
                        Assert.That(double.IsNaN(s.Stock[g]) || s.Stock[g] < -1e-6, Is.False, $"{s.Name} {(Good)g} stock {s.Stock[g]}");
                        Assert.That(double.IsNaN(s.Price[g]) || double.IsInfinity(s.Price[g]), Is.False, $"{s.Name} {(Good)g} price");
                    }
                    if (s.Kind != SettlementKind.Haven && jd > StartJd + 90 &&
                        (s.Satisfaction[(int)Good.Food] < 0.9 || s.Satisfaction[(int)Good.Water] < 0.9)) hungryDays[s.Id] += step;
                }
            }

            var traders = w.Fleet.Ships.Where(s => !s.Destroyed && w.IsTrader(s)).ToList();
            int trips = traders.Sum(t => t.TripsCompleted);
            foreach (var s in settlements)
            {
                var prices = string.Join(" ", Enumerable.Range(0, GoodInfo.Count).Where(g => s.Demand[g] > 0 || s.Supply[g] > 0)
                    .Select(g => $"{(Good)g}:{s.Price[g] / GoodInfo.Of((Good)g).BasePrice:0.00}"));
                TestContext.Out.WriteLine($"{s.Name,-18} {s.Kind,-13} pop {initial[s.Id] / 1e3,6:N1}k -> {s.Population / 1e3,6:N1}k  staffed {s.Staffing:0.00}  hungry {hungryDays[s.Id],4:0}d  {prices}");
            }
            foreach (var f in w.Fleet.Factions) TestContext.Out.WriteLine($"{f.Name,-26} treasury {f.Treasury / 1e6,9:N0} M");
            TestContext.Out.WriteLine($"{name}: {traders.Count} merchants (from {merchantsAtStart}), {trips} trips, {w.Fleet.Ships.Count(s => !s.Destroyed)} ships, " +
                                      $"{w.Events.Count(e => e.Kind == WorldEventKind.Launch)} launched, {w.War.Battles.Count} battles");

            foreach (var s in settlements.Where(s => s.Kind != SettlementKind.Haven))
            {
                Assert.That(hungryDays[s.Id], Is.LessThan(30), $"{s.Name} went hungry or thirsty for {hungryDays[s.Id]} days");
                Assert.That(s.Population, Is.GreaterThan(initial[s.Id] * 0.95), $"{s.Name} lost its people");
                Assert.That(s.Fallen, Is.False, s.Name);
            }
            // Trade kept moving and paid: the merchant fleet is still there, working, and its owners solvent.
            Assert.That(traders.Count, Is.GreaterThanOrEqualTo(merchantsAtStart * 0.6), "the merchant fleet withered away");
            Assert.That(trips, Is.GreaterThan(traders.Count * 4), "hardly any trade");
            foreach (var f in w.Fleet.Factions.Where(f => !f.IsPirate))
                Assert.That(f.Treasury, Is.GreaterThan(0), f.Name + " went broke");
        }
    }
}
