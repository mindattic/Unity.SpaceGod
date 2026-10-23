using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The galaxy at full scale (U.SPC-T413): real catalogue stars near Sol, the rest generated from a seed, charted and
    /// built only as a pilot comes near, all through the one system-source extension point.
    /// </summary>
    [TestFixture]
    public class GalaxyGenerationTests
    {
        const double StartJd = 2461324.0;

        /// <summary>A place a thousand light-years from Sol toward the galactic centre, in the plane.</summary>
        static DVec3 Inward(double ly) => GalacticFrame.ToCentre * ly;

        static IEnumerable<StarEntry> Generated(IEnumerable<StarEntry> stars) => stars.Where(s => s.Source == ProceduralSystems.SourceName);

        /// <summary>Half a light-year off a generated star three thousand light-years in, so there is always one to build.</summary>
        static DVec3 BesideAStar(int seed) =>
            Generated(SystemSources.StarsNear(Inward(3000), 15, seed)).First().PositionLy + new DVec3(0.5, 0, 0);

        // ---------------------------------------------------------------- the seed

        [Test]
        public void TheSameSeedMakesTheSameStarsAndAnotherSeedOthers()
        {
            var at = Inward(1000);
            var a = SystemSources.StarsNear(at, 25, 42);
            var b = SystemSources.StarsNear(at, 25, 42);
            var c = SystemSources.StarsNear(at, 25, 43);
            Assert.That(a.Count, Is.GreaterThan(50), "a thousand light-years in, a 25 ly sphere holds a crowd of stars");
            Assert.That(b.Select(s => s.Name), Is.EqualTo(a.Select(s => s.Name)));
            Assert.That(b.Select(s => s.PositionLy), Is.EqualTo(a.Select(s => s.PositionLy)));
            Assert.That(c.Select(s => s.Name).Intersect(a.Select(s => s.Name)), Is.Empty);
            Assert.That(a.Select(s => s.Name).Distinct().Count(), Is.EqualTo(a.Count), "every generated star has a name of its own");
        }

        [Test]
        public void AStarIsTheSameWhicheverWayItIsReached()
        {
            // Charted from two places, in either order, a star in both charts is the same star.
            var p = Inward(5000) + new DVec3(0, 0, 3);
            var q = p + new DVec3(12, -7, 4);
            var fromP = SystemSources.StarsNear(p, 20, 7).ToDictionary(s => s.Name);
            var fromQ = SystemSources.StarsNear(q, 20, 7);
            var shared = fromQ.Where(s => fromP.ContainsKey(s.Name)).ToList();
            Assert.That(shared.Count, Is.GreaterThan(5));
            foreach (var s in shared)
            {
                var o = fromP[s.Name];
                Assert.That(s.PositionLy, Is.EqualTo(o.PositionLy), s.Name);
                Assert.That(s.Spectral, Is.EqualTo(o.Spectral), s.Name);
                Assert.That(s.MassSolar, Is.EqualTo(o.MassSolar), s.Name);
                Assert.That(s.Seed, Is.EqualTo(o.Seed), s.Name);
            }
            // And a cell made alone is the cell made among its neighbours.
            var cell = ProceduralSystems.StarsInCell(7, 3, -4, 5);
            var again = ProceduralSystems.StarsInCell(7, 3, -4, 5);
            Assert.That(again.Select(s => s.Name), Is.EqualTo(cell.Select(s => s.Name)));
        }

        // ---------------------------------------------------------------- real stars near Sol

        [Test]
        public void NearSolTheStarsAreRealOnesAndNoneIsMadeUp()
        {
            var near = SystemSources.StarsNear(DVec3.Zero, 30, Galaxy.DefaultSeed);
            var names = near.Select(s => s.Name).ToList();
            Assert.That(names, Does.Contain("Barnard's Star").And.Contain("Procyon").And.Contain("EZ Aquarii").And.Contain("Altair").And.Contain("Vega"));
            Assert.That(near.First(s => s.Name == "Barnard's Star").Source, Is.EqualTo(HandBuiltSystems.SourceName), "the hand-built entry wins its star");
            Assert.That(near.First(s => s.Name == "EZ Aquarii").Source, Is.EqualTo(CatalogueStars.SourceName));
            Assert.That(names.Count(n => n == "Sirius"), Is.EqualTo(1), "a star two sources list is charted once");
            foreach (var s in Generated(near))
            {
                Assert.That(s.PositionLy.Length, Is.GreaterThanOrEqualTo(CatalogueStars.RadiusLy), s.Name);
                foreach (var real in CatalogueStars.Stars())
                    Assert.That((real.PositionLy - s.PositionLy).Length, Is.GreaterThanOrEqualTo(CatalogueStars.ClearLy), s.Name + " by " + real.Name);
            }
            // Altair is where its catalogue row puts it (the chart's 2027 position, U.SPC-T454).
            var altair = near.First(s => s.Name == "Altair");
            Assert.That(altair.PositionLy.Length, Is.EqualTo(16.698).Within(1e-3));
        }

        // ---------------------------------------------------------------- the shape of the galaxy

        [Test]
        public void StarsFillTheDiscAsThickAsTheNeighbourhoodAndThinOutPastIt()
        {
            Assert.That(GalacticFrame.Density(DVec3.Zero), Is.EqualTo(GalacticFrame.LocalDensity).Within(1).Percent);
            Assert.That(GalacticFrame.Density(GalacticFrame.CentreLy), Is.GreaterThan(GalacticFrame.LocalDensity * 20), "the core is crowded");
            Assert.That(GalacticFrame.Density(Inward(-40000)), Is.LessThan(GalacticFrame.LocalDensity / 10), "the rim is sparse");
            Assert.That(GalacticFrame.Density(GalacticFrame.North * 10000), Is.LessThan(GalacticFrame.LocalDensity * 1e-3), "far above the plane is nearly empty");
            GalacticFrame.Cylindrical(DVec3.Zero, out double r, out double z);
            Assert.That(r, Is.EqualTo(GalacticFrame.SunToCentreLy).Within(1e-6));
            Assert.That(z, Is.EqualTo(GalacticFrame.SunHeightLy).Within(1e-6));

            // Counted out in a stretch of the disc a few hundred light-years off, about what the density says.
            var at = Inward(-300);
            double radius = 40;
            int n = Generated(SystemSources.StarsNear(at, radius, 11)).Count();
            double expected = GalacticFrame.Density(at) * 4.0 / 3 * Math.PI * radius * radius * radius;
            Assert.That(n, Is.EqualTo(expected).Within(15).Percent);

            // And the far side of the galaxy has stars too, placed exactly.
            var far = Inward(2 * GalacticFrame.SunToCentreLy - 2000);
            var there = SystemSources.StarsNear(far, 10, Galaxy.DefaultSeed);
            Assert.That(there, Is.Not.Empty);
            var star = there[0];
            var origin = star.Origin;
            Assert.That(origin.Ly.X, Is.EqualTo(star.PositionLy.X).Within(1e-6));
        }

        [Test]
        public void TheStarsAreMostlyRedDwarfsAsNearTheSun()
        {
            var stars = Generated(SystemSources.StarsNear(Inward(800), 60, 5)).ToList();
            Assert.That(stars.Count, Is.GreaterThan(1500));
            double m = stars.Count(s => s.Spectral.StartsWith("M")) / (double)stars.Count;
            double g = stars.Count(s => s.Spectral.StartsWith("G")) / (double)stars.Count;
            double d = stars.Count(s => s.Spectral.StartsWith("D")) / (double)stars.Count;
            Assert.That(m, Is.EqualTo(0.73).Within(0.04));
            Assert.That(g, Is.EqualTo(0.07).Within(0.02));
            Assert.That(d, Is.EqualTo(0.05).Within(0.02));
            foreach (var s in stars)
            {
                Assert.That(s.MassSolar, Is.GreaterThan(0).And.LessThan(11), s.Name);
                Assert.That(s.LuminositySolar, Is.EqualTo(FrontierSystems.LuminosityOf(s.RadiusSolar, s.TeffK)).Within(1e-9).Percent, s.Name);
                Assert.That(s.Name, Does.StartWith(ProceduralSystems.Prefix + " "));
            }
        }

        // ---------------------------------------------------------------- defining a generated system

        [Test]
        public void AGeneratedStarIsDefinedTheSameEveryTimeAndBuildsLikeAHandBuiltOne()
        {
            var stars = Generated(SystemSources.StarsNear(Inward(1500), 25, 3)).ToList();
            var withPlanets = stars.Select(s => (s, spec: SystemSources.Define(s, 3))).Where(x => x.spec.Planets.Count >= 2).Take(5).ToList();
            Assert.That(withPlanets.Count, Is.EqualTo(5), "most stars have planets");
            foreach (var (star, spec) in withPlanets)
            {
                var again = SystemSources.Define(star, 3);
                Assert.That(again.Planets.Select(p => (p.Name, p.AAu, p.MassEarth)), Is.EqualTo(spec.Planets.Select(p => (p.Name, p.AAu, p.MassEarth))));
                Assert.That(spec.PositionLy.Length, Is.EqualTo(star.PositionLy.Length).Within(1e-9).Percent);
                Assert.That((spec.PositionLy - star.PositionLy).Length, Is.LessThan(1e-6));
                Assert.That(spec.Primary.Star.TeffK, Is.EqualTo(star.TeffK));
                Assert.That(spec.Stations.Single().HabitatSite, Is.True, "a gate in opens it to settlers");

                var built = StarSystemBuilder.Build(spec);
                var eph = new Ephemeris(new BodyCatalog(built.Bodies));
                eph.Update(StartJd);
                var c = eph.Catalog;
                for (int i = 1; i < spec.Planets.Count; i++)
                    Assert.That(spec.Planets[i].AAu, Is.GreaterThan(spec.Planets[i - 1].AAu * 1.39), "spaced as real systems are");
                foreach (var p in spec.Planets)
                {
                    var b = c[p.Name];
                    Assert.That(b.Status, Is.EqualTo(BodyStatus.Fictional), p.Name);
                    Assert.That(b.Elements.At(StartJd).PeriodDays, Is.EqualTo(p.PeriodDays).Within(1e-9).Percent, p.Name);
                    Assert.That(p.AAu * Astro.AuKm, Is.GreaterThan(spec.Primary.Star.RadiusSolar * StarSystemBuilder.SunRadiusKm * 5), p.Name + " clear of its star");
                }
            }
        }

        [Test]
        public void ACatalogueStarWithNoHandBuiltEntryIsBuiltAsItsStar()
        {
            var e = SystemSources.StarsNear(DVec3.Zero, 20, 1).First(s => s.Name == "Altair");
            var spec = SystemSources.Define(e, 1);
            Assert.That(spec.Primary.Star.Spectral, Is.EqualTo("A7V"));
            Assert.That(spec.Primary.Star.LuminositySolar, Is.EqualTo(10.6));
            Assert.That(spec.DistanceLy, Is.EqualTo(16.698).Within(1e-3));
            var ez = SystemSources.Define(SystemSources.StarsNear(DVec3.Zero, 20, 1).First(s => s.Name == "EZ Aquarii"), 1);
            Assert.That(ez.Primary.Star.MassSolar, Is.GreaterThan(0.08).And.LessThan(0.2), "estimated from its temperature");
            Assert.That(ez.Primary.Star.Summary, Does.Contain("estimated"));
            var system = StarSystem.Create(spec, StartJd);
            Assert.That(system.Settled, Is.False);
            Assert.That(system.Origin.DistanceLy(GalacticPosition.Origin), Is.EqualTo(16.698).Within(1e-3));
        }

        // ---------------------------------------------------------------- on demand

        [Test]
        public void TheGalaxyIsChartedAndBuiltOnlyAsAPilotComesNear()
        {
            var g = new Galaxy { Seed = 99 };
            var start = BesideAStar(99);
            g.Update(StartJd);
            Assert.That(g.NearbyStars, Is.Empty, "nothing is charted with no pilot to chart it for");

            g.Viewpoint = GalacticPosition.FromLy(start);
            g.Update(StartJd);
            int version = g.ChartVersion;
            Assert.That(version, Is.GreaterThan(0));
            double chart = GalaxyStreaming.ChartRadius.Value, near = GalaxyStreaming.SystemRadius.Value;
            Assert.That(g.NearbyStars.Count, Is.GreaterThan(20));
            Assert.That(g.NearbyStars.All(n => (n.PositionLy - start).Length <= chart + 1e-9));
            Assert.That(g.Systems, Is.Not.Empty, "the stars nearest the pilot are built");
            Assert.That(g.Systems.All(s => (s.PositionLy - start).Length <= near + 1e-9));
            Assert.That(g.NearbyStars.Any(n => g.Systems.Any(s => s.Name == n.Name)), Is.False, "a star built is not charted twice");
            var built = g.Systems.ToDictionary(s => s.Name, s => s.Catalog.Bodies.Select(b => b.Name).ToList());

            // Standing still, nothing changes.
            g.Update(StartJd + 1);
            Assert.That(g.ChartVersion, Is.EqualTo(version));

            // Flying far off, what was built and charted here is let go, and the new neighbourhood taken on.
            var away = start + new DVec3(0, 0, 200);
            g.Viewpoint = GalacticPosition.FromLy(away);
            g.Update(StartJd + 2);
            Assert.That(g.Systems.Any(s => built.ContainsKey(s.Name)), Is.False);
            Assert.That(g.NearbyStars.All(n => (n.PositionLy - away).Length <= chart + 1e-9));

            // And coming back, every system is built again star for star, planet for planet.
            g.Viewpoint = GalacticPosition.FromLy(start);
            g.Update(StartJd + 3);
            var rebuilt = g.Systems.ToDictionary(s => s.Name, s => s.Catalog.Bodies.Select(b => b.Name).ToList());
            Assert.That(rebuilt.Keys, Is.EquivalentTo(built.Keys));
            foreach (var kv in built) Assert.That(rebuilt[kv.Key], Is.EqualTo(kv.Value), kv.Key);
        }

        [Test]
        public void ASystemSomethingHoldsIsNotLetGo()
        {
            var g = new Galaxy { Seed = 99 };
            var start = BesideAStar(99);
            g.Explore(start, StartJd);
            var star = g.NearbyStars.OrderBy(n => (n.PositionLy - start).Length).First();
            var held = g.Realize(star.Name, StartJd);
            Assert.That(held, Is.Not.Null);
            Assert.That(g.Find(star.Name), Is.SameAs(held));
            Assert.That(g.NearbyStars.Any(n => n.Name == star.Name), Is.False);
            g.Focus = held; // the player is there
            g.Explore(start + new DVec3(500, 0, 0), StartJd + 1);
            Assert.That(g.Systems, Does.Contain(held));
            Assert.That(g.Realize(star.Name, StartJd + 2), Is.SameAs(held), "realized once");
        }

        [Test]
        public void TheDefaultGalaxyChartsTheRealStarsAboutSolWhenAPilotIsThere()
        {
            var g = Galaxy.CreateDefault(StartJd);
            int before = g.NearbyStars.Count;
            int systems = g.Systems.Count;
            g.Explore(DVec3.Zero, StartJd);
            Assert.That(g.Systems.Count, Is.EqualTo(systems), "every system within reach of Sol is already simulated");
            var names = g.NearbyStars.Select(n => n.Name).ToList();
            Assert.That(names, Does.Contain("Kapteyn's Star").And.Contain("Altair").And.Contain("Procyon"));
            Assert.That(names.Distinct().Count(), Is.EqualTo(names.Count));
            Assert.That(names, Has.None.StartWith(ProceduralSystems.Prefix), "nothing made up within the real neighbourhood");
            Assert.That(g.NearbyStars.Count, Is.GreaterThan(before));
            Assert.That(names, Has.None.AnyOf("Sirius", "Tau Ceti", "Barnard's Star"), "a simulated system is not charted again");
        }

        // ---------------------------------------------------------------- the extension point

        sealed class Nebula : SystemSource
        {
            public static readonly DVec3 Centre = new DVec3(-4000, 2500, 300);
            public override string Name => "Test nebula";
            public override int Order => 50;
            public override bool Claims(DVec3 ly) => (ly - Centre).Length < 15;

            public override IEnumerable<StarEntry> StarsNear(DVec3 centreLy, double radiusLy, int seed)
            {
                if ((Centre - centreLy).Length <= radiusLy)
                    yield return new StarEntry { Name = "Nebula Heart", PositionLy = Centre, Spectral = "O9V", MassSolar = 18, RadiusSolar = 7, TeffK = 32000, LuminositySolar = 50000 };
            }

            public override SystemSpec Define(StarEntry star, int seed) =>
                SystemSources.LoneStar(star, new StarSpec
                {
                    Name = star.Name, Spectral = star.Spectral, MassSolar = star.MassSolar, RadiusSolar = star.RadiusSolar, TeffK = star.TeffK,
                    LuminositySolar = star.LuminositySolar, RotationDays = 2, Summary = "The heart of a test nebula.",
                }, "A test region.");
        }

        sealed class Derelict : SystemContent
        {
            public override void Furnish(StarEntry star, SystemSpec spec, int seed)
            {
                if (star.Source != "Test nebula") return;
                spec.Stations.Add(new StationSpec { Name = star.Name + " Derelict", Host = spec.Primary.Star.Name, AAu = 40, Summary = "Test content." });
            }
        }

        [Test]
        public void ANewRegionAndItsContentPlugInWithoutTouchingTheGalaxy()
        {
            var content = new Derelict();
            SystemSources.Register(new Nebula());
            SystemSources.Content.Add(content);
            try
            {
                Assert.That(SystemSources.All.Select(s => s.Name).Take(2), Is.EqualTo(new[] { HandBuiltSystems.SourceName, "Test nebula" }));
                var stars = SystemSources.StarsNear(Nebula.Centre, 20, Galaxy.DefaultSeed);
                Assert.That(stars[0].Name, Is.EqualTo("Nebula Heart"));
                Assert.That(stars[0].Source, Is.EqualTo("Test nebula"));
                Assert.That(stars.Where(s => s.Source == ProceduralSystems.SourceName).All(s => (s.PositionLy - Nebula.Centre).Length >= 15),
                    "the region keeps the made-up stars out");

                var g = new Galaxy();
                g.Viewpoint = GalacticPosition.FromLy(Nebula.Centre + new DVec3(1, 0, 0));
                g.Update(StartJd);
                var heart = g.Find("Nebula Heart");
                Assert.That(heart, Is.Not.Null, "built as the pilot comes near");
                Assert.That(heart.Catalog.Bodies.Select(b => b.Name), Does.Contain("Nebula Heart Derelict"), "with its content");
            }
            finally
            {
                SystemSources.Content.Remove(content);
                SystemSources.Unregister("Test nebula");
            }
            Assert.That(SystemSources.Find("Test nebula"), Is.Null);
        }

        [Test]
        public void TheStreamingSettingsAreInTheMenu()
        {
            Assert.That(GalaxyStreaming.ChartRadius.Group, Is.EqualTo(SettingGroups.Streaming));
            var keys = SettingsRegistry.Default.All.Where(s => s.Group == SettingGroups.Streaming).Select(s => s.Key).ToList();
            Assert.That(keys, Does.Contain(GalaxyStreaming.ChartRadius.Key).And.Contain(GalaxyStreaming.SystemRadius.Key).And.Contain(GalaxyStreaming.MaxCharted.Key));
        }
    }
}
