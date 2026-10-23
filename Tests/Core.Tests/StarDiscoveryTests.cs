using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// U.SPC-T456: every real system of docs/nearest_stars_2027.csv, and every void station between them, is in the galaxy from
    /// the start, split by distance from Sol: within 21 ly charted and known as always, past it Undiscovered, a state of its own
    /// that keeps the star off the charts, the galaxy map, the FTL targets and courses and out of every voyage, until a pilot or a
    /// group between the stars comes near enough to discover it.
    /// </summary>
    [TestFixture]
    public class StarDiscoveryTests
    {
        const double StartJd = 2461324.0;

        static bool Charted(Galaxy g, string name) => g.NearbyStars.Any(n => n.Name == name);

        [Test]
        public void EveryRealSystemAndVoidStationStartsInTheGalaxySplitAtTwentyOneLightYears()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var everywhere = VoidStations.Destinations().Concat(VoidStations.Stations()).ToList();
            Assert.That(everywhere.Count, Is.GreaterThan(123 + 50), "the chart's systems and the stations between them");
            foreach (var e in everywhere)
            {
                bool running = g.SimulatedAs(e) != null, charted = Charted(g, e.Name), hidden = g.IsUndiscovered(e.Name);
                Assert.That(new[] { running, charted, hidden }.Count(b => b), Is.EqualTo(1), e.Name + " is in the galaxy once");
                bool far = e.PositionLy.Length > StarDiscovery.KnownToLy;
                Assert.That(hidden, Is.EqualTo(far), e.Name + " at " + e.PositionLy.Length.ToString("0.0") + " ly");
                Assert.That(g.DiscoveryOf(e.Name), Is.EqualTo(far ? DiscoveryState.Undiscovered : DiscoveryState.Discovered), e.Name);
            }
            Assert.That(StarDiscovery.KnownToLy, Is.EqualTo(21));
            Assert.That(g.Undiscovered.Select(n => n.Name), Does.Contain("Vega").And.Contain("TRAPPIST-1").And.Contain("Aldebaran"));
            Assert.That(Charted(g, "Altair") && Charted(g, "Gliese 581") && Charted(g, "Kapteyn's Star"), Is.True, "the known neighbourhood");
            // Void stations go by the stars about them: those out in the undiscovered dark are undiscovered too.
            Assert.That(g.Undiscovered.Count(n => n.Name.StartsWith(VoidStations.Prefix)), Is.EqualTo(
                VoidStations.Stations().Count(s => s.PositionLy.Length > StarDiscovery.KnownToLy)));
            Assert.That(g.Discoveries, Is.Empty);
        }

        [Test]
        public void AnUndiscoveredStarIsOnNoChartAndNoCourseCanBeSetForIt()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var vega = g.Undiscovered.First(n => n.Name == "Vega");

            Assert.That(Charted(g, "Vega"), Is.False, "not on the charts (the interstellar view and the galaxy map draw these)");
            Assert.That(FtlTargets.All(g, sol).Select(t => t.Name), Has.None.EqualTo("Vega"), "no FTL target");
            Assert.That(g.Realize("Vega", StartJd), Is.Null, "nobody builds it or sets out for it");
            Assert.That(g.Realize(SystemSources.Entry("Vega", vega.PositionLy, g.Seed), StartJd), Is.Null);
            Assert.That(g.DeepSpace.TryStar("Vega", out _), Is.False);

            // It is there all the same: its well pulls a group flying past out, but a course being planned cannot reckon with it.
            Assert.That(DeepSpace.StarWells(g.DeepSpace, new Cruise(), vega.Origin, 1).Select(f => f.Name), Does.Contain("Vega"));
            Assert.That(DeepSpace.StarWells(g.DeepSpace, new Cruise { Plan = true }, vega.Origin, 1).Select(f => f.Name), Has.None.EqualTo("Vega"));

            // A pilot coming near a far star does not chart it out of turn either: only discovery does.
            g.Explore(vega.PositionLy + new DVec3(StarDiscovery.Range.Value + 1, 0, 0), StartJd);
            Assert.That(g.IsUndiscovered("Vega"), Is.True);
            Assert.That(Charted(g, "Vega"), Is.False);
        }

        [Test]
        public void APilotComingNearDiscoversAStarAndItIsChartedFromThen()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var vega = g.Undiscovered.First(n => n.Name == "Vega");
            StarDiscoveredEvent heard = null;
            g.StarDiscovered += e => heard = heard ?? e;
            int version = g.ChartVersion;

            // Within the discovery range of it (but outside the system streaming radius, so it stays a plain star).
            g.Viewpoint = GalacticPosition.FromLy(vega.PositionLy + new DVec3(0, StarDiscovery.Range.Value - 0.5, 0));
            g.Update(StartJd + 1);

            Assert.That(g.IsUndiscovered("Vega"), Is.False);
            Assert.That(g.DiscoveryOf("Vega"), Is.EqualTo(DiscoveryState.Discovered));
            Assert.That(Charted(g, "Vega"), Is.True, "on the charts now");
            Assert.That(g.ChartVersion, Is.GreaterThan(version), "the views know to redraw");
            Assert.That(g.Discoveries.Select(d => d.Name), Does.Contain("Vega"));
            Assert.That(heard, Is.Not.Null);
            Assert.That(heard.By, Is.EqualTo("you"));
            Assert.That(FtlTargets.All(g, sol).Select(t => t.Name), Does.Contain("Vega"), "an FTL target now");
            Assert.That(g.DeepSpace.Events.Any(e => e.Text.Contains("Vega is discovered")), Is.True);

            // Discovered for good: going away does not lose it, and it is built like any charted star.
            g.Viewpoint = GalacticPosition.Origin;
            g.Update(StartJd + 2);
            Assert.That(Charted(g, "Vega"), Is.True);
            Assert.That(g.Discover("Vega", StartJd + 2), Is.False, "found once");
            Assert.That(g.Realize("Vega", StartJd + 2), Is.Not.Null);
            Assert.That(Charted(g, "Vega"), Is.False, "shown once, as the system it is now");
        }

        [Test]
        public void AnythingOutBetweenTheStarsDiscoversWhatItComesNear()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var station = g.Undiscovered.Where(n => n.Name.StartsWith(VoidStations.Prefix)).OrderBy(n => n.PositionLy.Length).First();
            int before = g.Undiscovered.Count;
            int found = g.DiscoverAbout(station.Origin, StartJd, "the Test Ship");
            Assert.That(found, Is.GreaterThanOrEqualTo(1));
            Assert.That(g.IsUndiscovered(station.Name), Is.False);
            Assert.That(g.Undiscovered.Count, Is.EqualTo(before - found));
            Assert.That(g.Undiscovered.All(n => n.Origin.DistanceLy(station.Origin) > StarDiscovery.Range.Value), Is.True, "everything in range found");
            Assert.That(g.Discoveries.Last().By, Is.EqualTo("the Test Ship"));
            Assert.That(StarDiscovery.Lookouts, Does.Contain((StarDiscovery.Lookout)StarDiscovery.Cruises));
        }

        [Test]
        public void TheWholeChartCanBeFoundFromTheKnownNeighbourhoodByGoingToEachStarAsItIsFound()
        {
            var g = Galaxy.CreateDefault(StartJd);
            // Visit every known star in turn, discovering about each; the dark never holds a star out of reach.
            for (int round = 0; round < 200 && g.Undiscovered.Count > 0; round++)
            {
                int left = g.Undiscovered.Count;
                foreach (var n in g.NearbyStars.ToList()) g.DiscoverAbout(n.Origin, StartJd, "a survey");
                if (g.Undiscovered.Count == left) break;
            }
            Assert.That(g.Undiscovered.Select(n => n.Name), Is.Empty);
        }
    }
}
