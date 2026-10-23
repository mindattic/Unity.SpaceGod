using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// U.SPC-T454: the real nearest star systems out to 100 ly, by the owner's 7 ly divisions (docs/NEAREST_STARS.md), are
    /// catalogue stars at the chart's own positions (docs/nearest_stars_2027.csv, from Gaia and Hipparcos, not the pasted
    /// list), and wherever 7 ly between them would hold no destination a void station stands, so no leg between charted
    /// destinations is longer than 7 ly.
    /// </summary>
    [TestFixture]
    public class NearestStarsTests
    {
        const double StartJd = 2461324.0;

        sealed class Row
        {
            public string Name;
            public DVec3 Ly;
            public double DistLy;
        }

        /// <summary>The chart's rows, read from the repo's docs folder.</summary>
        static List<Row> Chart()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "docs", "nearest_stars_2027.csv"))) dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "docs/nearest_stars_2027.csv is in the repo");
            var rows = new List<Row>();
            foreach (var line in File.ReadAllLines(Path.Combine(dir.FullName, "docs", "nearest_stars_2027.csv")).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var f = Split(line);
                double D(int i) => double.Parse(f[i], CultureInfo.InvariantCulture);
                rows.Add(new Row { Name = f[0], DistLy = D(3), Ly = new DVec3(D(4), D(5), D(6)) });
            }
            return rows;
        }

        static List<string> Split(string line)
        {
            var fields = new List<string>();
            var cur = new System.Text.StringBuilder();
            bool quoted = false;
            foreach (char c in line)
            {
                if (c == '"') quoted = !quoted;
                else if (c == ',' && !quoted)
                {
                    fields.Add(cur.ToString());
                    cur.Clear();
                }
                else cur.Append(c);
            }
            fields.Add(cur.ToString());
            return fields;
        }

        /// <summary>The name a chart row goes by in the game.</summary>
        static string GameName(string chart)
        {
            switch (chart)
            {
                case "Luyten 726-8 (UV/BL Ceti)": return "Gliese 65";
                case "Lacaille 9352 (Gliese 887)": return "Gliese 887";
                case "Mizar & Alcor": return "Mizar";
            }
            int paren = chart.IndexOf(" (", StringComparison.Ordinal);
            return paren < 0 ? chart : chart.Substring(0, paren);
        }

        [Test]
        public void EverySystemOnTheChartIsARealStarAtItsChartedPlace()
        {
            var chart = Chart();
            Assert.That(chart.Count, Is.EqualTo(123));
            var charted = SystemSources.StarsNear(DVec3.Zero, CatalogueStars.ChartedToLy + 1, Galaxy.DefaultSeed).ToDictionary(e => e.Name);
            foreach (var row in chart)
            {
                string name = GameName(row.Name);
                Assert.That(charted.ContainsKey(name), Is.True, name + " is charted");
                var e = charted[name];
                Assert.That(e.Source, Is.Not.EqualTo(ProceduralSystems.SourceName).And.Not.EqualTo(VoidStations.SourceName), name);
                if (e.Source == CatalogueStars.SourceName && e.Tag != "neighbour")
                    Assert.That((e.PositionLy - row.Ly).Length, Is.LessThan(1e-9), name + " at the chart's 2027 position");
                else
                    // A hand-built system or a neighbour keeps its own J2000 catalogue place, within a hair of the chart's.
                    Assert.That((e.PositionLy - row.Ly).Length, Is.LessThan(0.1), name);
            }
            Assert.That(CatalogueStars.Stars().Max(s => s.PositionLy.Length), Is.LessThanOrEqualTo(CatalogueStars.ChartedToLy));
            Assert.That(CatalogueStars.Stars().Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(CatalogueStars.Stars().Count));
        }

        [Test]
        public void ThePastedListsSlipsAreNotInTheChart()
        {
            var stars = CatalogueStars.Stars().ToDictionary(s => s.Name);
            double Ly(string n) => stars[n].PositionLy.Length;
            // Each star once, at its one measured distance.
            Assert.That(Ly("Gliese 176"), Is.EqualTo(30.9).Within(0.1));
            Assert.That(Ly("Gliese 436"), Is.EqualTo(31.9).Within(0.1));
            // Filed where they really are, not where the list put them.
            Assert.That(Ly("Gliese 581"), Is.InRange(14.0, 21.0), "Gliese 581 is in the 14-21 ly division");
            Assert.That(Ly("Wolf 1061"), Is.InRange(14.0, 21.0));
            Assert.That(Ly("Van Maanen's Star"), Is.InRange(14.0, 21.0));
            Assert.That(Ly("Castor"), Is.EqualTo(51.0).Within(0.1));
            Assert.That(stars.Keys, Has.None.EqualTo("Polaris").And.None.EqualTo("HR 8799"), "neither is within 100 ly");
            // And the one distance the game had wrong is right now.
            Assert.That(Galaxy.Neighbours().First(n => n.Name == "Sirius").PositionLy.Length, Is.EqualTo(8.60).Within(0.01));
            Assert.That(Ly("Sirius"), Is.EqualTo(8.60).Within(0.01));
        }

        [Test]
        public void ACatalogueStarKeepsItsValuesOrIsEstimatedAsADwarf()
        {
            var e = SystemSources.StarsNear(DVec3.Zero, 101, 1).First(s => s.Name == "Aldebaran");
            Assert.That(e.Tag, Is.EqualTo("landmark"));
            var spec = SystemSources.Define(e, 1);
            Assert.That(spec.Primary.Star.Spectral, Is.EqualTo("K5III"));
            Assert.That(spec.Primary.Star.RadiusSolar, Is.GreaterThan(40), "a giant, not a main-sequence estimate");
            var wolf = SystemSources.Define(SystemSources.StarsNear(DVec3.Zero, 101, 1).First(s => s.Name == "Wolf 1061"), 1);
            Assert.That(wolf.Primary.Star.MassSolar, Is.InRange(0.2, 0.45), "estimated from its temperature");
            Assert.That(wolf.Primary.Star.Summary, Does.Contain("Gliese 628").And.Contain("estimated"));
            var trappist = SystemSources.Define(SystemSources.StarsNear(DVec3.Zero, 101, 1).First(s => s.Name == "TRAPPIST-1"), 1);
            Assert.That(trappist.Primary.Star.MassSolar, Is.EqualTo(0.0898));
            Assert.That(trappist.DistanceLy, Is.EqualTo(40.7).Within(0.01));
        }

        [Test]
        public void NoLegBetweenChartedDestinationsIsLongerThanSevenLightYears()
        {
            var real = VoidStations.Destinations();
            var stations = VoidStations.Stations();
            Assert.That(stations.Count, Is.GreaterThan(50), "the sparse chart past 28 ly needs stations between its stars");
            var all = real.Select(e => e.PositionLy).Concat(stations.Select(s => s.PositionLy)).ToList();

            // Out from Sol by legs of 7 ly or less, every real system and every station is reached.
            var reached = new bool[all.Count];
            var queue = new Queue<int>();
            reached[0] = true;
            queue.Enqueue(0);
            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                for (int v = 0; v < all.Count; v++)
                    if (!reached[v] && (all[v] - all[u]).Length <= VoidStations.MaxLegLy + 1e-9)
                    {
                        reached[v] = true;
                        queue.Enqueue(v);
                    }
            }
            for (int i = 0; i < real.Count; i++) Assert.That(reached[i], Is.True, real[i].Name + " is reached in legs of 7 ly");
            Assert.That(reached.All(r => r), Is.True);
            Assert.That(VoidStations.MaxLegLy, Is.EqualTo(7));

            // No station where a destination already is, every one numbered outward from Sol, each on a link between two real systems.
            for (int k = 0; k < stations.Count; k++)
            {
                var s = stations[k];
                Assert.That(s.Name, Is.EqualTo(VoidStations.Prefix + " " + (k + 1)));
                foreach (var r in real)
                    Assert.That((r.PositionLy - s.PositionLy).Length, Is.GreaterThan(1), s.Name + " by " + r.Name);
                var (a, b) = VoidStations.Bridges(s.Name);
                Assert.That(real.Any(r => r.Name == a) && real.Any(r => r.Name == b), Is.True, s.Name);
                if (k > 0) Assert.That(s.PositionLy.Length, Is.GreaterThanOrEqualTo(stations[k - 1].PositionLy.Length));
            }
            // Without them some real system would be more than 7 ly from every other.
            Assert.That(real.Any(r => real.All(o => o == r || (o.PositionLy - r.PositionLy).Length > VoidStations.MaxLegLy)), Is.True);
        }

        [Test]
        public void AVoidStationIsChartedAndBuiltAsAVoidBase()
        {
            var station = VoidStations.Stations().Last();
            var near = SystemSources.StarsNear(station.PositionLy, 6, Galaxy.DefaultSeed);
            var e = near.First();
            Assert.That(e.Name, Is.EqualTo(station.Name));
            Assert.That(e.Source, Is.EqualTo(VoidStations.SourceName));
            foreach (var other in near.Where(n => n.Source == ProceduralSystems.SourceName))
                Assert.That((other.PositionLy - station.PositionLy).Length, Is.GreaterThanOrEqualTo(VoidStations.ClearLy), other.Name);

            var built = SystemSources.Build(e, StartJd, Galaxy.DefaultSeed);
            Assert.That(built.IsVoidBase, Is.True);
            Assert.That(built.Name, Is.EqualTo(station.Name));
            Assert.That((built.PositionLy - station.PositionLy).Length, Is.LessThan(1e-9));
            Assert.That(built.Settled, Is.False);

            // A pilot coming near one builds it like any charted destination.
            var g = new Galaxy();
            g.Explore(station.PositionLy + new DVec3(0.5, 0, 0), StartJd);
            Assert.That(g.Find(station.Name)?.IsVoidBase, Is.True);
        }
    }
}
