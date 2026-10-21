using System;
using System.Collections.Generic;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    [TestFixture]
    public class AlphaCentauriTests
    {
        const double StartJd = 2461324.0;


        static Ephemeris Ephemeris() => new Ephemeris(BodyCatalog.CreateAlphaCentauri());

        [Test]
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
                Assert.That(balance.Length < 1e-6 * ra.Length, Is.True, "the barycentre moved");
                double sep = (rb - ra).Length / Astro.AuKm;
                min = Math.Min(min, sep);
                max = Math.Max(max, sep);
            }
            TestContext.Out.WriteLine($"separation {min:0.00} to {max:0.00} AU");
            Assert.That(min, Is.InRange(11.15, 11.25));
            Assert.That(max, Is.InRange(35.35, 35.45));

            eph.Update(StartJd);
            var now = eph.Helio(b) - eph.Helio(a);
            eph.Update(StartJd + period);
            Assert.That((eph.Helio(b) - eph.Helio(a) - now).Length < 1e-6 * now.Length, Is.True, "the orbit does not close in 79.76 years");

            // Periastron in May 2035 (1875.66 + 2 × 79.762).
            eph.Update(Astro.J2000 + (1875.66 + 2 * 79.762 - 2000) * 365.25);
            Assert.That((eph.Helio(b) - eph.Helio(a)).Length / Astro.AuKm, Is.InRange(11.15, 11.25));
        }

        [Test]
        public void TheAbOrbitLooksFromEarthAsMeasured()
        {
            // Akeson et al.: i = 79.2° to the sky. The orbit normal must make that angle with the line of sight.
            var normal = AlphaCentauri.BinaryNormal;
            double angle = Math.Acos(Math.Abs(DVec3.Dot(normal, AlphaCentauri.Direction))) * Astro.Rad2Deg;
            TestContext.Out.WriteLine($"orbit normal is {angle:0.00}° from the line of sight");
            Assert.That(angle, Is.InRange(79.0, 79.5));
        }

        [Test]
        public void ProximaIsThirteenThousandAuOutAndItsPlanetsKeepTheirPeriods()
        {
            var eph = Ephemeris();
            var c = eph.Catalog;
            eph.Update(Astro.J2000);
            var proxima = eph.Helio(c["Proxima Centauri"].Index);
            TestContext.Out.WriteLine($"Proxima is {proxima.Length / Astro.AuKm:N0} AU from A-B");
            Assert.That((proxima - AlphaCentauri.ProximaOffsetKm).Length < 1e-6 * proxima.Length, Is.True, "Proxima is not where the sky puts it");
            Assert.That(proxima.Length / Astro.AuKm, Is.InRange(11000, 14000));

            int b = c["Proxima b"].Index, d = c["Proxima d"].Index;
            var b0 = eph.Relative(b);
            var d0 = eph.Relative(d);
            Assert.That(b0.Length / Astro.AuKm, Is.InRange(0.0475, 0.0495));
            Assert.That(d0.Length / Astro.AuKm, Is.InRange(0.0287, 0.0289));
            eph.Update(Astro.J2000 + 11.18465);
            Assert.That((eph.Relative(b) - b0).Length < 1e-6 * b0.Length, Is.True);
            eph.Update(Astro.J2000 + 5.12338);
            Assert.That((eph.Relative(d) - d0).Length < 1e-6 * d0.Length, Is.True);

            Assert.That(c["Proxima c"].Status, Is.EqualTo(BodyStatus.Disputed));
            Assert.That(c["Alpha Centauri Ab"].Status, Is.EqualTo(BodyStatus.Candidate));
            Assert.That(c.Root.Kind, Is.EqualTo(BodyKind.Barycentre));
        }

        [Test]
        public void TheSunShinesInCassiopeiaFromAlphaCentauri()
        {
            double ly = AlphaCentauri.PositionLy.Length;
            Assert.That(ly, Is.InRange(4.3439, 4.3441));
            Astro.RaDecOf(-AlphaCentauri.PositionLy, out double ra, out double dec);
            TestContext.Out.WriteLine($"the Sun from Alpha Centauri: RA {ra / 15:0.000} h, Dec {dec:+0.00;-0.00}°");
            // Antipode of RA 14h39.6m, Dec -60°50': RA 2h39.6m, Dec +60°50', in Cassiopeia.
            Assert.That(ra, Is.InRange(Astro.Hms(2, 39, 0), Astro.Hms(2, 40, 12)));
            Assert.That(dec, Is.InRange(60.8, 60.9));
            Assert.That(Galaxy.Neighbours().Count(), Is.EqualTo(21));
            var sirius = Galaxy.Neighbours().First(s => s.Name == "Sirius");
            Assert.That(sirius.AbsoluteMagnitude, Is.InRange(1.3, 1.5));
        }

        [Test]
        public void TheLanesTieEveryPortTogetherAndCrossToProximaInDays()
        {
            var eph = Ephemeris();
            eph.Update(StartJd);
            var lanes = Hyperlanes.CreateAlphaCentauri(eph);
            var c = eph.Catalog;
            var systems = lanes.Lanes.SelectMany(l => new[] { l.A, l.B }).Distinct().ToList();
            foreach (int a in systems)
                foreach (int b in systems)
                    if (a != b) Assert.That(lanes.Route(a, b, StartJd), Is.Not.Null);
            // Every body orbiting a star directly is its own system.
            Assert.That(lanes.SystemOf(c["Rigil Gate"].Index), Is.EqualTo(c["Rigil Gate"].Index));
            Assert.That(lanes.SystemOf(c["Proxima b"].Index), Is.EqualTo(c["Proxima b"].Index));
            var trunk = lanes.Lanes.First(l => l.Name == "Rigil Gate–Proxima b");
            Assert.That(trunk.Hub, Is.EqualTo(0));
            double km = lanes.LaneLengthKm(trunk, StartJd);
            double days = Hyperlanes.RideDays(km);
            TestContext.Out.WriteLine($"trunk {km / Astro.AuKm:N0} AU, ridden in {days:0.0} days");
            Assert.That(days <= Hyperlanes.MaxRideDays, Is.True);
            // Lanes inside Proxima's system keep clear of the star, in its own (scaled) keep-out.
            var inner = lanes.Lanes.First(l => l.Name == "Proxima b–Proxima d");
            Assert.That(inner.Hub, Is.EqualTo(c["Proxima Centauri"].Index));
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
                            Assert.That(clear > lanes.KeepOutKm(s) * 0.9, Is.True, $"{lane.Name} passes {clear / Astro.AuKm:0.000} AU from {c[s].Name}");
                        }
                    }
                }
            }
        }

        [Test]
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
                    Assert.That(double.IsNaN(st.HelioKm.X) || double.IsInfinity(st.HelioKm.X), Is.False, $"{s.Name} is nowhere ({s.Phase})");
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
                TestContext.Out.WriteLine($"{Astro.ToDateTime(e.Jd):yyyy-MM-dd}  {e.Text}");
            foreach (var s in w.Economy.Settlements)
                TestContext.Out.WriteLine($"{s.Name,-16} pop {s.Population,9:N0}  food {s.Stock[(int)Good.Food],9:N0} t");
            TestContext.Out.WriteLine($"traders {traders.Count}, trips {trips}, credits {credits:N0}, lane rides {laneRides} ({trunkRides} on the trunk), ambushes {ambushes}");
            Assert.That(trips > 100, Is.True, "hardly any trade");
            Assert.That(credits > 0, Is.True, "the merchants lost money");
            Assert.That(trunkRides > 10, Is.True, "nobody crossed to Proxima");
            Expect.All(w.Economy.Settlements.Where(s => s.Kind != SettlementKind.Haven), s => Assert.That(s.Stock[(int)Good.Food] > 0, Is.True, $"{s.Name} starved"));
        }

        [Test]
        public void BothSystemsRunTogetherOnOneClock()
        {
            var g = Galaxy.CreateDefault(StartJd);
            Assert.That(g.Systems.Count, Is.EqualTo(14));
            Assert.That(Galaxy.DistanceLy(g.Systems[0], g.Systems[1]), Is.InRange(4.3439, 4.3441));
            for (double jd = StartJd; jd < StartJd + 10; jd += 0.1) g.Update(jd);
            Expect.All(g.Systems, s => Assert.That(s.Ephemeris.Jd, Is.EqualTo(StartJd + 9.9).Within(1e-6)));
        }
    }
}
