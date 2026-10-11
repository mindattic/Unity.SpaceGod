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

            // Reported speed is relative to the transit frame, and matches the motion.
            const double h = 1e-5;
            DVec3 Rel(double jd) => fleet.StateOf(ship, jd).HelioKm - eph.HelioAt(ship.FrameBody, jd);
            double numeric = (Rel(mid + h) - Rel(mid - h)).Length / (2 * h) / Astro.SecondsPerDay;
            Assert.True(Math.Abs(numeric - st.SpeedKmS) < 1e-4 * st.SpeedKmS, $"reported {st.SpeedKmS:0.000} km/s, moving at {numeric:0.000}");
        }

        [Fact]
        public void TransitsLeaveAndArriveAtTheirParkingOrbitsVelocity()
        {
            var (fleet, eph) = Create();
            int matched = 0;
            foreach (var ship in fleet.Ships.Where(s => !s.IsEscort && s.Phase == ShipPhase.Transit))
            {
                if (ship.DepartureVelocity.LengthSquared == 0) continue;
                matched++;
                const double h = 1e-7;
                var origin = ship.OriginBody;
                var destination = ship.Orbit.Body;
                double t0 = ship.PhaseStartJd, t1 = ship.PhaseEndJd;
                DVec3 Rel(double jd, int body) => fleet.StateOf(ship, jd).HelioKm - eph.HelioAt(body, jd);
                var leave = (Rel(t0 + h, origin) - Rel(t0, origin)) / h;
                var parked = ship.DepartureOrbit.VelocityAt(t0);
                Assert.True((leave - parked).Length < 0.02 * parked.Length + 50, $"{ship.Name} leaves at {(leave - parked).Length:0} km/day off");
                var arrive = (Rel(t1, destination) - Rel(t1 - h, destination)) / h;
                var orbit = ship.Orbit.VelocityAt(t1);
                Assert.True((arrive - orbit).Length < 0.02 * orbit.Length + 50, $"{ship.Name} arrives at {(arrive - orbit).Length:0} km/day off");
            }
            Assert.True(matched > 5);
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
            var end = eph.HelioAt(ship.FrameBody, arrive) + Fleet.TransitOffset(ship, 1);
            var target = eph.HelioAt(ship.DestinationBody, arrive) + ship.Orbit.OffsetAt(arrive);
            Assert.True((end - target).Length < 1.0);
        }
    }
}
