using System;
using System.Collections.Generic;
using System.Linq;
using SolarSystem.Core;
using Xunit;
using Xunit.Abstractions;

namespace SolarSystem.Core.Tests
{
    public class AlphaCentauriTests
    {
        const double StartJd = 2461324.0;
        readonly ITestOutputHelper output;

        public AlphaCentauriTests(ITestOutputHelper output) => this.output = output;

        static Ephemeris Ephemeris() => new Ephemeris(BodyCatalog.CreateAlphaCentauri());

        [Fact]
        public void AAndBSwingBetweenPeriastronAndApastronOnOppositeSidesOfTheirBarycentre()
        {
            var eph = Ephemeris();
            var c = eph.Catalog;
            int a = c["Alpha Centauri A"].Index, b = c["Alpha Centauri B"].Index;
            double min = double.MaxValue, max = 0;
            double period = 79.762 * 365.25;
            for (double jd = StartJd; jd < StartJd + period; jd += 5)
            {
                eph.Update(jd);
                var ra = eph.Helio(a);
                var rb = eph.Helio(b);
                // The barycentre stays put: m_A r_A + m_B r_B = 0.
                var balance = ra * AlphaCentauri.MassA + rb * AlphaCentauri.MassB;
                Assert.True(balance.Length < 1e-6 * ra.Length, "the barycentre moved");
                double sep = (rb - ra).Length / Astro.AuKm;
                min = Math.Min(min, sep);
                max = Math.Max(max, sep);
            }
            output.WriteLine($"separation {min:0.00} to {max:0.00} AU");
            Assert.InRange(min, 11.15, 11.25);
            Assert.InRange(max, 35.35, 35.45);

            eph.Update(StartJd);
            var now = eph.Helio(b) - eph.Helio(a);
            eph.Update(StartJd + period);
            Assert.True((eph.Helio(b) - eph.Helio(a) - now).Length < 1e-6 * now.Length, "the orbit does not close in 79.76 years");

            // Periastron in May 2035 (1875.66 + 2 × 79.762).
            eph.Update(Astro.J2000 + (1875.66 + 2 * 79.762 - 2000) * 365.25);
            Assert.InRange((eph.Helio(b) - eph.Helio(a)).Length / Astro.AuKm, 11.15, 11.25);
        }

        [Fact]
        public void TheAbOrbitLooksFromEarthAsMeasured()
        {
            // Akeson et al.: i = 79.2° to the sky. The orbit normal must make that angle with the line of sight.
            var normal = AlphaCentauri.BinaryNormal;
            double angle = Math.Acos(Math.Abs(DVec3.Dot(normal, AlphaCentauri.Direction))) * Astro.Rad2Deg;
            output.WriteLine($"orbit normal is {angle:0.00}° from the line of sight");
            Assert.InRange(angle, 79.0, 79.5);
        }

        [Fact]
        public void ProximaIsThirteenThousandAuOutAndItsPlanetsKeepTheirPeriods()
        {
            var eph = Ephemeris();
            var c = eph.Catalog;
            eph.Update(Astro.J2000);
            var proxima = eph.Helio(c["Proxima Centauri"].Index);
            output.WriteLine($"Proxima is {proxima.Length / Astro.AuKm:N0} AU from A-B");
            Assert.True((proxima - AlphaCentauri.ProximaOffsetKm).Length < 1e-6 * proxima.Length, "Proxima is not where the sky puts it");
            Assert.InRange(proxima.Length / Astro.AuKm, 11000, 14000);

            int b = c["Proxima b"].Index, d = c["Proxima d"].Index;
            var b0 = eph.Relative(b);
            var d0 = eph.Relative(d);
            Assert.InRange(b0.Length / Astro.AuKm, 0.0475, 0.0495);
            Assert.InRange(d0.Length / Astro.AuKm, 0.0287, 0.0289);
            eph.Update(Astro.J2000 + 11.18465);
            Assert.True((eph.Relative(b) - b0).Length < 1e-6 * b0.Length);
            eph.Update(Astro.J2000 + 5.12338);
            Assert.True((eph.Relative(d) - d0).Length < 1e-6 * d0.Length);

            Assert.Equal(BodyStatus.Disputed, c["Proxima c"].Status);
            Assert.Equal(BodyStatus.Candidate, c["Alpha Centauri Ab"].Status);
            Assert.Equal(BodyKind.Barycentre, c.Root.Kind);
        }

        [Fact]
        public void TheSunShinesInCassiopeiaFromAlphaCentauri()
        {
            double ly = AlphaCentauri.PositionLy.Length;
            Assert.InRange(ly, 4.3439, 4.3441);
            Astro.RaDecOf(-AlphaCentauri.PositionLy, out double ra, out double dec);
            output.WriteLine($"the Sun from Alpha Centauri: RA {ra / 15:0.000} h, Dec {dec:+0.00;-0.00}°");
            // Antipode of RA 14h39.6m, Dec -60°50': RA 2h39.6m, Dec +60°50', in Cassiopeia.
            Assert.InRange(ra, Astro.Hms(2, 39, 0), Astro.Hms(2, 40, 12));
            Assert.InRange(dec, 60.8, 60.9);
            Assert.Equal(21, Galaxy.Neighbours().Count());
            var sirius = Galaxy.Neighbours().First(s => s.Name == "Sirius");
            Assert.InRange(sirius.AbsoluteMagnitude, 1.3, 1.5);
        }

        [Fact]
        public void TheLanesTieEveryPortTogetherAndCrossToProximaInDays()
        {
            var eph = Ephemeris();
            eph.Update(StartJd);
            var lanes = Hyperlanes.CreateAlphaCentauri(eph);
            var c = eph.Catalog;
            var systems = lanes.Lanes.SelectMany(l => new[] { l.A, l.B }).Distinct().ToList();
            foreach (int a in systems)
                foreach (int b in systems)
                    if (a != b) Assert.NotNull(lanes.Route(a, b, StartJd));
            // Every body orbiting a star directly is its own system.
            Assert.Equal(c["Rigil Gate"].Index, lanes.SystemOf(c["Rigil Gate"].Index));
            Assert.Equal(c["Proxima b"].Index, lanes.SystemOf(c["Proxima b"].Index));
            var trunk = lanes.Lanes.First(l => l.Name == "Rigil Gate–Proxima b");
            Assert.Equal(0, trunk.Hub);
            double km = lanes.LaneLengthKm(trunk, StartJd);
            double days = Hyperlanes.RideDays(km);
            output.WriteLine($"trunk {km / Astro.AuKm:N0} AU, ridden in {days:0.0} days");
            Assert.True(days <= Hyperlanes.MaxRideDays);
            // Lanes inside Proxima's system keep clear of the star, in its own (scaled) keep-out.
            var inner = lanes.Lanes.First(l => l.Name == "Proxima b–Proxima d");
            Assert.Equal(c["Proxima Centauri"].Index, inner.Hub);
            int proxima = c["Proxima Centauri"].Index;
            for (double jd = StartJd; jd < StartJd + 40; jd += 0.7)
            {
                eph.Update(jd);
                foreach (var lane in lanes.Lanes)
                {
                    var a = lanes.RampHelio(lane, lane.A, jd);
                    var b = lanes.RampHelio(lane, lane.B, jd);
                    for (int i = 0; i <= 64; i++)
                    {
                        var p = lanes.CurveKm(lane, a, b, i / 64.0, jd);
                        for (int s = 0; s < c.Count; s++)
                        {
                            if (!c[s].IsStar) continue;
                            double clear = (p - eph.HelioAt(s, jd)).Length;
                            Assert.True(clear > lanes.KeepOutKm(s) * 0.9, $"{lane.Name} passes {clear / Astro.AuKm:0.000} AU from {c[s].Name}");
                        }
                    }
                }
            }
        }

        [Fact]
        public void TwoYearsOfAlphaCentauriTradeAndRaiding()
        {
            var sys = StarSystem.CreateAlphaCentauri(StartJd);
            var w = sys.World;
            int laneRides = 0, trunkRides = 0;
            var trunk = new HashSet<string> { "Rigil Gate–Proxima b", "Toliman Forge–Proxima b" };
            var seenRides = new HashSet<(int, double)>();
            for (double jd = StartJd; jd <= StartJd + 730; jd += 0.05)
            {
                sys.Update(jd);
                foreach (var s in w.Fleet.Ships)
                {
                    if (s.Destroyed) continue;
                    var st = w.Fleet.StateOf(s, jd);
                    Assert.False(double.IsNaN(st.HelioKm.X) || double.IsInfinity(st.HelioKm.X), $"{s.Name} is nowhere ({s.Phase})");
                    if (!s.IsEscort && s.Phase == ShipPhase.Lane && seenRides.Add((s.Id, s.PhaseStartJd)))
                    {
                        laneRides++;
                        if (trunk.Contains(s.LaneLeg.Lane.Name)) trunkRides++;
                    }
                }
            }
            var traders = w.Fleet.Ships.Where(s => !s.Destroyed && w.IsTrader(s)).ToList();
            int trips = traders.Sum(s => s.TripsCompleted);
            double credits = traders.Sum(s => s.Credits);
            int ambushes = w.Events.Count(e => e.Text.Contains("spring an ambush"));
            foreach (var e in w.Events.Where(e => e.Kind != WorldEventKind.Info).Take(20))
                output.WriteLine($"{Astro.ToDateTime(e.Jd):yyyy-MM-dd}  {e.Text}");
            foreach (var s in w.Economy.Settlements)
                output.WriteLine($"{s.Name,-16} pop {s.Population,9:N0}  food {s.Stock[(int)Good.Food],9:N0} t");
            output.WriteLine($"traders {traders.Count}, trips {trips}, credits {credits:N0}, lane rides {laneRides} ({trunkRides} on the trunk), ambushes {ambushes}");
            Assert.True(trips > 100, "hardly any trade");
            Assert.True(credits > 0, "the merchants lost money");
            Assert.True(trunkRides > 10, "nobody crossed to Proxima");
            Assert.All(w.Economy.Settlements.Where(s => s.Kind != SettlementKind.Haven), s => Assert.True(s.Stock[(int)Good.Food] > 0, $"{s.Name} starved"));
        }

        [Fact]
        public void BothSystemsRunTogetherOnOneClock()
        {
            var g = Galaxy.CreateDefault(StartJd);
            Assert.Equal(2, g.Systems.Count);
            Assert.InRange(Galaxy.DistanceLy(g.Systems[0], g.Systems[1]), 4.3439, 4.3441);
            for (double jd = StartJd; jd < StartJd + 10; jd += 0.1) g.Update(jd);
            Assert.All(g.Systems, s => Assert.Equal(StartJd + 9.9, s.Ephemeris.Jd, 6));
        }
    }
}
