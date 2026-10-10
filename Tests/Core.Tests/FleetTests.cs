using System;
using System.Linq;
using SolarSystem.Core;
using Xunit;

namespace SolarSystem.Core.Tests
{
    public class FleetTests
    {
        const double StartJd = 2461324.0;

        static (Fleet fleet, Ephemeris eph) Create()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return (Fleet.CreateDefault(eph, StartJd), eph);
        }

        [Fact]
        public void DefaultFleetHasEveryClassAndSomeShipsUnderway()
        {
            var (fleet, _) = Create();
            foreach (ShipClass c in Enum.GetValues(typeof(ShipClass)))
                Assert.Contains(fleet.Ships, s => s.Spec.Class == c);
            Assert.True(fleet.Ships.Count >= 40);
            Assert.Contains(fleet.Ships, s => !s.IsEscort && s.Phase == ShipPhase.Transit);
            Assert.Contains(fleet.Ships, s => !s.IsEscort && s.Phase == ShipPhase.Parked);
        }

        [Fact]
        public void ShipsStayOutsideBodiesAndFiniteForTwoYears()
        {
            var (fleet, eph) = Create();
            var catalog = eph.Catalog;
            for (double jd = StartJd; jd < StartJd + 730; jd += 0.37)
            {
                eph.Update(jd);
                fleet.Update(jd);
                foreach (var ship in fleet.Ships)
                {
                    var st = fleet.StateOf(ship, jd);
                    Assert.False(double.IsNaN(st.HelioKm.X) || double.IsInfinity(st.HelioKm.X), ship.Name);
                    if (ship.Controller.Phase == ShipPhase.Parked)
                    {
                        var body = catalog[ship.Controller.Orbit.Body];
                        Assert.True((st.HelioKm - eph.Helio(body.Index)).Length > body.RadiusKm * 2, ship.Name);
                    }
                    Assert.True(st.HelioKm.Length > Fleet.SunKeepOutKm * 0.5, $"{ship.Name} flew into the Sun");
                }
            }
        }

        [Fact]
        public void PositionIsContinuousAcrossDepartureAndArrival()
        {
            var (fleet, eph) = Create();
            var ship = fleet.Ships.First(s => s.Role == ShipRole.Trader);
            for (int legs = 0; legs < 6; legs++)
            {
                double boundary = ship.PhaseEndJd;
                const double eps = 1e-7;
                eph.Update(boundary - eps);
                fleet.Update(boundary - eps);
                var before = fleet.StateOf(ship, boundary - eps).HelioKm;
                eph.Update(boundary + eps);
                fleet.Update(boundary + eps);
                var after = fleet.StateOf(ship, boundary + eps).HelioKm;
                Assert.True((after - before).Length < 50, $"leg {legs}: jump of {(after - before).Length:F0} km");
            }
        }

        [Fact]
        public void TransitTimeMatchesBrachistochrone()
        {
            var (fleet, eph) = Create();
            var ship = fleet.Ships.First(s => !s.IsEscort && s.Phase == ShipPhase.Transit);
            double t = ship.PhaseEndJd - ship.PhaseStartJd;
            double expected = 2 * Math.Sqrt(ship.PathLength / ship.AccelKmPerDay2);
            Assert.Equal(expected, t, 6);

            double mid = ship.PhaseStartJd + t / 2;
            eph.Update(mid);
            var st = fleet.StateOf(ship, mid);
            Assert.InRange(st.Progress, 0.499, 0.501);
            Assert.Equal(ship.AccelKmPerDay2 * t / 2 / Astro.SecondsPerDay, st.SpeedKmS, 3);
        }

        [Fact]
        public void EscortsShareTheirLeadersPosition()
        {
            var (fleet, eph) = Create();
            var escort = fleet.Ships.First(s => s.IsEscort);
            var a = fleet.StateOf(escort, StartJd).HelioKm;
            var b = fleet.StateOf(escort.Leader, StartJd).HelioKm;
            Assert.Equal(a, b);
        }

        [Fact]
        public void ArrivalsMeetTheMovingTarget()
        {
            var (fleet, eph) = Create();
            var ship = fleet.Ships.First(s => !s.IsEscort && s.Phase == ShipPhase.Transit);
            double arrive = ship.PhaseEndJd;
            var end = ship.Path[ship.Path.Length - 1];
            var target = eph.HelioAt(ship.DestinationBody, arrive) + ship.Orbit.OffsetAt(arrive);
            Assert.True((end - target).Length < 1.0);
        }
    }
}
