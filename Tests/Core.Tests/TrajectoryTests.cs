using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// U.SPC-T95, real orbital mechanics for traffic: the conic toolkit (universal-variable propagation,
    /// a Lambert solver, Hohmann transfers and their windows, patched-conic escape and capture) checked
    /// against textbook numbers, and the fleet flying drive-appropriate trajectories with it: slow boats
    /// on transfer orbits that leave and arrive in real parking orbits, torches on flip-and-burns paying
    /// their gravity corrections, and the planner trading delta-v against time.
    /// </summary>
    [TestFixture]
    public class TrajectoryTests
    {
        const double EarthGm = 398600.435;
        const double SunGm = 1.32712440018e11;
        const double MarsGm = 42828.37;
        const double MarsAu = 1.523679;
        const double StartJd = 2461324.0;

        // ---------------------------------------------------------------- Hohmann

        [Test]
        public void TheEarthToMarsHohmannTransferTakesAbout259Days()
        {
            double r1 = Astro.AuKm, r2 = MarsAu * Astro.AuKm;
            double days = Conics.HohmannTime(SunGm, r1, r2) / Astro.SecondsPerDay;
            // Half the period of an ellipse with a = 1.2618 AU: 0.5 · 1.2618^1.5 years.
            double analytic = 0.5 * Math.Pow((1 + MarsAu) / 2, 1.5) * 365.25637;
            Assert.That(days, Is.EqualTo(analytic).Within(0.5));
            Assert.That(days, Is.InRange(258, 260));
        }

        [Test]
        public void HohmannBurnsMatchTheTextbook()
        {
            // Heliocentric: 2.94 km/s onto the transfer at Earth, 2.65 km/s to match Mars.
            Conics.HohmannDeltaV(SunGm, Astro.AuKm, MarsAu * Astro.AuKm, out double dv1, out double dv2);
            Assert.That(dv1, Is.EqualTo(2.94).Within(0.02));
            Assert.That(dv2, Is.EqualTo(2.65).Within(0.02));

            // LEO (300 km) to GEO: 2.43 + 1.47 = 3.89 km/s in 5.26 hours.
            Conics.HohmannDeltaV(EarthGm, 6678, 42164, out double leo, out double geo);
            Assert.That(leo + geo, Is.EqualTo(3.89).Within(0.02));
            Assert.That(Conics.HohmannTime(EarthGm, 6678, 42164) / 3600, Is.EqualTo(5.26).Within(0.02));
        }

        [Test]
        public void MarsWindowsComeRoundEvery780DaysWithMars44DegreesAhead()
        {
            Assert.That(Conics.SynodicPeriod(365.256, 686.98), Is.EqualTo(779.9).Within(0.5));
            double lead = Conics.HohmannLeadAngle(SunGm, Astro.AuKm, MarsAu * Astro.AuKm) * Astro.Rad2Deg;
            Assert.That(lead, Is.EqualTo(44.3).Within(0.3));
            // Inbound, Earth must trail Mars: the lead is negative.
            Assert.That(Conics.HohmannLeadAngle(SunGm, MarsAu * Astro.AuKm, Astro.AuKm), Is.LessThan(0));
        }

        [Test]
        public void TheWaitForAWindowIsWhenThePhaseComesRoundToTheLead()
        {
            double n1 = Astro.TwoPi / 365.256, n2 = Astro.TwoPi / 686.98, lead = 44.3 * Astro.Deg2Rad;
            // Mars 50° ahead: the gap closes at n1 - n2, 5.7° to go.
            double wait = Conics.WaitForPhase(50 * Astro.Deg2Rad, lead, n1, n2);
            Assert.That(wait, Is.EqualTo(5.7 * Astro.Deg2Rad / (n1 - n2)).Within(1e-6));
            // Just past the window: nearly a whole synodic period.
            double missed = Conics.WaitForPhase(lead - 0.01, lead, n1, n2);
            Assert.That(missed, Is.EqualTo(Conics.SynodicPeriod(365.256, 686.98) - 0.01 / (n1 - n2)).Within(0.01));
        }

        // ---------------------------------------------------------------- propagation and Lambert

        [Test]
        public void UniversalVariablePropagationMatchesCurtisExample3_7()
        {
            Conics.Propagate(new DVec3(7000, -12124, 0), new DVec3(2.6679, 4.6210, 0), EarthGm, 3600, out var r, out var v);
            Assert.That(r.X, Is.EqualTo(-3297.8).Within(2));
            Assert.That(r.Y, Is.EqualTo(7413.4).Within(2));
            Assert.That(v.X, Is.EqualTo(-8.2977).Within(0.002));
            Assert.That(v.Y, Is.EqualTo(-0.96309).Within(0.002));
        }

        [Test]
        public void PropagationAgreesWithKeplersEquationAndRunsBackwards()
        {
            var k = new KeplerState(Astro.AuKm * 1.3, 0.3, 0.1, 0.5, 1.0, 0.2, Math.Sqrt(SunGm / Math.Pow(Astro.AuKm * 1.3, 3)) * Astro.SecondsPerDay);
            var r0 = Kepler.Position(k);
            var v0 = Kepler.Velocity(k) / Astro.SecondsPerDay;
            double dt = 200 * Astro.SecondsPerDay;
            Conics.Propagate(r0, v0, SunGm, dt, out var r, out var v);
            var expected = Kepler.Position(k.WithMeanAnomaly(k.MeanAnomaly + k.MeanMotion * 200));
            Assert.That((r - expected).Length, Is.LessThan(1), "a few km in 1.3 AU after 200 days");
            Conics.Propagate(r, v, SunGm, -dt, out var back, out _);
            Assert.That((back - r0).Length, Is.LessThan(1), "and back again");
            // A hyperbola too: energy is kept.
            var rh = new DVec3(7000, 0, 0);
            var vh = new DVec3(0, 12, 1);
            Conics.Propagate(rh, vh, EarthGm, 5 * 3600, out var r2, out var v2);
            double e0 = vh.LengthSquared / 2 - EarthGm / rh.Length, e1 = v2.LengthSquared / 2 - EarthGm / r2.Length;
            Assert.That(e1, Is.EqualTo(e0).Within(1e-8));
        }

        [Test]
        public void LambertMatchesCurtisExample5_2()
        {
            bool ok = Conics.Lambert(new DVec3(5000, 10000, 2100), new DVec3(-14600, 2500, 7000), 3600, EarthGm, DVec3.UnitZ, out var v1, out var v2);
            Assert.That(ok, Is.True);
            Assert.That(v1.X, Is.EqualTo(-5.9925).Within(0.001));
            Assert.That(v1.Y, Is.EqualTo(1.9254).Within(0.001));
            Assert.That(v1.Z, Is.EqualTo(3.2456).Within(0.001));
            Assert.That(v2.X, Is.EqualTo(-3.3125).Within(0.001));
            Assert.That(v2.Y, Is.EqualTo(-4.1966).Within(0.001));
            Assert.That(v2.Z, Is.EqualTo(-0.38529).Within(0.001));
        }

        [Test]
        public void ALambertArcPropagatedForItsFlightTimeLandsOnItsTarget()
        {
            var rng = new Random(7);
            int checkedArcs = 0;
            for (int i = 0; i < 60; i++)
            {
                double a1 = rng.NextDouble() * Astro.TwoPi, a2 = a1 + 0.2 + rng.NextDouble() * 5.8;
                var r1 = new DVec3(Math.Cos(a1), Math.Sin(a1), 0.05 * (rng.NextDouble() - 0.5)) * (Astro.AuKm * (0.4 + rng.NextDouble() * 4));
                var r2 = new DVec3(Math.Cos(a2), Math.Sin(a2), 0.05 * (rng.NextDouble() - 0.5)) * (Astro.AuKm * (0.4 + rng.NextDouble() * 4));
                // From very fast (hyperbolic) to slow arcs.
                double tof = (5 + rng.NextDouble() * 900) * Astro.SecondsPerDay;
                if (!Conics.Lambert(r1, r2, tof, SunGm, DVec3.UnitZ, out var v1, out var v2)) continue;
                Conics.Propagate(r1, v1, SunGm, tof, out var r, out var v);
                Assert.That((r - r2).Length / r2.Length, Is.LessThan(1e-6), $"arc {i}");
                Assert.That((v - v2).Length / v2.Length, Is.LessThan(1e-5), $"arc {i}");
                // Prograde about +z.
                Assert.That(DVec3.Cross(r1, v1).Z, Is.GreaterThan(0), $"arc {i} goes round prograde");
                checkedArcs++;
            }
            Assert.That(checkedArcs, Is.EqualTo(60), "short way and long way, elliptic and hyperbolic");
        }

        [Test]
        public void LambertBetweenCircularOrbitsHalfAWayRoundIsTheHohmannTransfer()
        {
            double r1 = Astro.AuKm, r2 = MarsAu * Astro.AuKm;
            double tof = Conics.HohmannTime(SunGm, r1, r2);
            // Just short of opposite (exactly opposite leaves the plane undefined): the Hohmann ellipse.
            double theta = 179.9 * Astro.Deg2Rad;
            var a = new DVec3(r1, 0, 0);
            var b = new DVec3(r2 * Math.Cos(theta), r2 * Math.Sin(theta), 0);
            Assert.That(Conics.Lambert(a, b, tof, SunGm, DVec3.UnitZ, out var v1, out var v2), Is.True);
            double vc1 = Conics.CircularSpeed(SunGm, r1), vc2 = Conics.CircularSpeed(SunGm, r2);
            Conics.HohmannDeltaV(SunGm, r1, r2, out double dv1, out double dv2);
            Assert.That(v1.Length - vc1, Is.EqualTo(dv1).Within(0.05));
            Assert.That(vc2 - v2.Length, Is.EqualTo(dv2).Within(0.05));
            Assert.That(Math.Abs(v1.X) / v1.Length, Is.LessThan(0.01), "it leaves tangentially, at perihelion");
        }

        [Test]
        public void LambertRefusesTheDegenerateCases()
        {
            var r = new DVec3(Astro.AuKm, 0, 0);
            Assert.That(Conics.Lambert(r, r, 1e6, SunGm, DVec3.UnitZ, out _, out _), Is.False, "same point");
            Assert.That(Conics.Lambert(r, -r * 1.5, 1e6, SunGm, DVec3.UnitZ, out _, out _), Is.False, "exactly opposite: no plane");
            Assert.That(Conics.Lambert(r, new DVec3(0, Astro.AuKm, 0), -1, SunGm, DVec3.UnitZ, out _, out _), Is.False, "negative time");
        }

        // ---------------------------------------------------------------- patched conics

        [Test]
        public void EarthsAndMarssSpheresOfInfluenceAreTheTextbookOnes()
        {
            Assert.That(Conics.SoiRadius(Astro.AuKm, EarthGm, SunGm), Is.EqualTo(925000).Within(5000));
            Assert.That(Conics.SoiRadius(MarsAu * Astro.AuKm, MarsGm, SunGm), Is.EqualTo(577000).Within(5000));
        }

        [Test]
        public void TheEscapeBurnFromLowOrbitToMarsIsAbout3_6KmS()
        {
            Conics.HohmannDeltaV(SunGm, Astro.AuKm, MarsAu * Astro.AuKm, out double vInf, out double vInfMars);
            double leo = 6678;
            double burn = Conics.EscapeBurn(EarthGm, leo, vInf, Conics.CircularSpeed(EarthGm, leo));
            Assert.That(burn, Is.EqualTo(3.59).Within(0.03));
            // Far less than the 2.94 km/s plus the 11 km/s escape speed it would take to do it in two steps.
            Assert.That(burn, Is.LessThan(vInf + Conics.EscapeSpeed(EarthGm, leo) - Conics.CircularSpeed(EarthGm, leo)));
            // And capture into a 4.75-radius Mars orbit.
            double rp = 3389.5 * 4.75;
            Assert.That(Conics.EscapeBurn(MarsGm, rp, vInfMars, Conics.CircularSpeed(MarsGm, rp)), Is.InRange(1.5, 2.2));
        }

        [Test]
        public void AnEscapeHyperbolaStartsAtPeriapsisAndLeavesAlongItsExcessVelocity()
        {
            var vInf = new DVec3(1, 2, 0.2).Normalized * 3.0;
            var h = Hyperbola.Departure(EarthGm, 7000, vInf, DVec3.UnitZ);
            Assert.That(h.PositionAt(0).Length, Is.EqualTo(7000).Within(1e-6));
            double far = h.TimeToRadius(5e6);
            Assert.That(h.PositionAt(far).Length, Is.EqualTo(5e6).Within(1));
            var heading = (h.PositionAt(far * 1.01) - h.PositionAt(far)).Normalized;
            Assert.That(DVec3.Dot(heading, vInf.Normalized), Is.GreaterThan(0.999), "outbound along v∞");
            var speed = (h.PositionAt(far + 1) - h.PositionAt(far - 1)).Length / 2;
            Assert.That(speed, Is.EqualTo(Math.Sqrt(9 + 2 * EarthGm / 5e6)).Within(0.01), "vis-viva on the hyperbola");

            var c = Hyperbola.Arrival(EarthGm, 7000, vInf, DVec3.UnitZ);
            var incoming = (c.PositionAt(-far) - c.PositionAt(-far * 1.01)).Normalized;
            Assert.That(DVec3.Dot(incoming, vInf.Normalized), Is.GreaterThan(0.999), "inbound along v∞");
            Assert.That(c.PositionAt(0).Length, Is.EqualTo(7000).Within(1e-6));
        }

        [Test]
        public void ATorchPaysGravityLossClimbingOutOfAWell()
        {
            // From rest at 2.5 Earth radii at 0.1 g: a few km/s, more for a weaker drive or a deeper start.
            double loss = Conics.GravityLossLeaving(EarthGm, 6371 * 2.5, 0.1 * 9.80665e-3);
            Assert.That(loss, Is.InRange(4, 10));
            Assert.That(Conics.GravityLossLeaving(EarthGm, 6371 * 2.5, 0.3 * 9.80665e-3), Is.LessThan(loss));
            Assert.That(Conics.GravityLossLeaving(EarthGm, 6371 * 10, 0.1 * 9.80665e-3), Is.LessThan(loss));
        }

        // ---------------------------------------------------------------- the fleet

        sealed class Run
        {
            public Ephemeris Eph;
            public Fleet Fleet;
            public Ship Ship;
        }

        /// <summary>One ship of <paramref name="cls"/> parked at <paramref name="from"/> and sent to <paramref name="to"/> and back (no lanes, no rings).</summary>
        static Run Shuttle(ShipClass cls, string from, string to, bool transfers = true, Action<Ship> setup = null)
        {
            var r = new Run { Eph = new Ephemeris(BodyCatalog.CreateSolSystem()) };
            r.Eph.Update(StartJd);
            r.Fleet = new Fleet(r.Eph, 5) { BurnsPropellant = s => true, AvoidCollisions = false, OrbitalTransfers = transfers };
            int a = r.Eph.Catalog[from].Index, b = r.Eph.Catalog[to].Index;
            r.Fleet.ChooseNext = (s, here, jd) => here == a ? b : a;
            var f = r.Fleet.AddFaction("Traders", "MV", default);
            r.Ship = r.Fleet.AddShip("Shuttle", cls, f, cls == ShipClass.Courier ? ShipRole.Courier : ShipRole.Trader);
            r.Ship.Ports = new[] { a, b };
            setup?.Invoke(r.Ship);
            r.Fleet.Station(r.Ship, a, StartJd);
            return r;
        }

        static double StepUntil(Run r, Func<bool> done, double days, double step = 0.01, double from = StartJd)
        {
            for (double jd = from; jd < from + days; jd += step)
            {
                r.Eph.Update(jd);
                r.Fleet.Update(jd);
                if (done()) return jd;
            }
            Assert.Fail("never happened in " + days + " days");
            return double.NaN;
        }

        [Test]
        public void AFreighterFliesATransferOrbitFromParkingOrbitToParkingOrbit()
        {
            var r = Shuttle(ShipClass.Freighter, "Earth", "Moon");
            var ship = r.Ship;
            var parked = ship.Orbit;
            StepUntil(r, () => ship.Phase == ShipPhase.Transit, 30);
            Assert.That(Fleet.DriveOf(ship), Is.EqualTo(DriveKind.LowThrust));
            Assert.That(ship.Conic, Is.Not.Null, "a slow boat falls along a transfer orbit");
            var c = ship.Conic;
            double depart = ship.PhaseStartJd, arrive = ship.PhaseEndJd;
            Assert.That(arrive - depart, Is.EqualTo(c.Days).Within(1e-9));
            // Earth to the Moon by transfer orbit: a day or few, not the flip-and-burn's half day.
            Assert.That(c.Days, Is.InRange(0.8, 8));
            Assert.That(c.Capture, Is.Not.Null, "it drops into the Moon's sphere of influence on a capture hyperbola");
            Assert.That(c.DeltaVKmS, Is.InRange(3, 20), "a few km/s, against tens for a flip-and-burn");
            Assert.That(Propulsion.TransitDeltaV(ship), Is.EqualTo(c.DeltaVKmS));

            // It leaves exactly from where its parking orbit had it, and arrives exactly in its new one.
            int earth = r.Eph.Catalog["Earth"].Index, moon = r.Eph.Catalog["Moon"].Index;
            r.Eph.Update(depart);
            var start = r.Fleet.StateOf(ship, depart).HelioKm;
            Assert.That((start - (r.Eph.Helio(earth) + parked.OffsetAt(depart))).Length, Is.LessThan(1));
            r.Eph.Update(arrive);
            var end = r.Fleet.StateOf(ship, arrive).HelioKm;
            Assert.That((end - (r.Eph.Helio(moon) + ship.Orbit.OffsetAt(arrive))).Length, Is.LessThan(1));

            // The track is continuous, and the drive is lit only for the burns at the ends.
            DVec3 last = start;
            int coasting = 0, samples = 400;
            for (int i = 1; i <= samples; i++)
            {
                double jd = depart + c.Days * i / samples;
                r.Eph.Update(jd);
                var st = r.Fleet.StateOf(ship, jd);
                Assert.That((st.HelioKm - last).Length, Is.LessThan(30000), $"no jump at sample {i}");
                last = st.HelioKm;
                if (!st.Burning) coasting++;
            }
            Assert.That(coasting, Is.GreaterThan(samples * 0.8), "it coasts most of the way");

            // And on arrival it is parked in that orbit.
            StepUntil(r, () => ship.Phase == ShipPhase.Parked && ship.Orbit.Body == moon, 20, 0.01, depart);
            Assert.That(ship.Conic, Is.Null);
        }

        [Test]
        public void ACourierKeepsItsFlipAndBurnAndPaysItsGravityCorrection()
        {
            var r = Shuttle(ShipClass.Courier, "Earth", "Moon");
            var ship = r.Ship;
            StepUntil(r, () => ship.Phase == ShipPhase.Transit, 30);
            Assert.That(Fleet.DriveOf(ship), Is.EqualTo(DriveKind.Torch));
            Assert.That(ship.Conic, Is.Null, "a torch flies its flip-and-burn");
            Assert.That(ship.GravityLossKmS, Is.InRange(0.3, 15), "holding its line through Earth's and the Moon's wells");
            double dv = Propulsion.TransitDeltaV(ship);
            double straight = (ship.AccelKmPerDay2 * (ship.PhaseEndJd - ship.PhaseStartJd) + (ship.ArrivalVelocity - ship.DepartureVelocity).Length) / Astro.SecondsPerDay;
            Assert.That(dv, Is.EqualTo(straight + ship.GravityLossKmS).Within(1e-9));
        }

        [Test]
        public void WithTransfersOffEveryoneFliesTheClassicFlipAndBurn()
        {
            var r = Shuttle(ShipClass.Freighter, "Earth", "Moon", transfers: false);
            var ship = r.Ship;
            StepUntil(r, () => ship.Phase == ShipPhase.Transit, 30);
            Assert.That(ship.Conic, Is.Null);
            Assert.That(ship.GravityLossKmS, Is.EqualTo(0));
        }

        [Test]
        public void TheTransferOrbitCostsFarLessPropellantThanTheFlipAndBurn()
        {
            var slow = Shuttle(ShipClass.Freighter, "Earth", "Moon");
            StepUntil(slow, () => slow.Ship.Phase == ShipPhase.Transit, 30);
            var classic = Shuttle(ShipClass.Freighter, "Earth", "Moon", transfers: false);
            StepUntil(classic, () => classic.Ship.Phase == ShipPhase.Transit, 30);
            Assert.That(slow.Ship.LegPropellant, Is.LessThan(classic.Ship.LegPropellant * 0.5));
            Assert.That(slow.Ship.PhaseEndJd - slow.Ship.PhaseStartJd, Is.GreaterThan(classic.Ship.PhaseEndJd - classic.Ship.PhaseStartJd),
                "and takes longer: time traded for propellant");
        }

        [Test]
        public void CheapFreightToMarsFallsAcrossOnAPatchedConicRatherThanBurningHard()
        {
            var r = Shuttle(ShipClass.Freighter, "Earth", "Mars");
            var ship = r.Ship;
            int mars = r.Eph.Catalog["Mars"].Index;
            Assert.That(ship.PlannedConicDays, Is.GreaterThan(0), "it planned a transfer orbit for its first trip");
            double planned = ship.PlannedConicDays;
            StepUntil(r, () => ship.Phase == ShipPhase.Transit, 900, 0.5);
            var c = ship.Conic;
            Assert.That(c, Is.Not.Null, "it falls to Mars on a transfer orbit under the Sun");
            Assert.That(ship.FrameBody, Is.EqualTo(0));
            Assert.That(c.Escape, Is.Not.Null, "climbing out of Earth's sphere of influence on an escape hyperbola");
            Assert.That(c.Capture, Is.Not.Null, "and dropping into Mars's on a capture hyperbola");
            Assert.That(c.Days, Is.InRange(25, 330), "weeks to months, not the flip-and-burn's days");
            Assert.That(c.Days, Is.EqualTo(planned).Within(1e-6).Or.InRange(planned * 0.25, planned * 1.25));
            TestContext.Out.WriteLine($"waited {ship.PhaseStartJd - StartJd:F0} d, flew {c.Days:F0} d on {c.DeltaVKmS:F1} km/s (escape {c.DepartureBurnKmS:F1}, capture {c.ArrivalBurnKmS:F1})");
            Assert.That(c.DeltaVKmS, Is.LessThan(300), "around a hundred km/s, not the flip-and-burn's near thousand");

            // The SOI handoff: inside Earth's sphere it rides with Earth, far out it is on the Sun's conic.
            double tEdge = c.Escape.TimeToRadius(c.FromSoiKm);
            var inside = c.OffsetAt(tEdge * 0.2);
            var earthThen = Conics.PositionAfter(c.FromR0, c.FromV0, c.Mu, tEdge * 0.2);
            Assert.That((inside - earthThen).Length, Is.LessThan(c.FromSoiKm));
            var outside = c.OffsetAt(c.Days * 0.5);
            Assert.That((outside - Conics.PositionAfter(c.R0, c.V0, c.Mu, c.Days * 0.5 - c.ExitDays)).Length, Is.LessThan(1), "pure heliocentric conic mid-way");
            // Each piece starts where the last left off: no jump at either handoff.
            const double eps = 1e-7;
            Assert.That((c.OffsetAt(c.ExitDays - eps) - c.OffsetAt(c.ExitDays + eps)).Length, Is.LessThan(5));
            Assert.That((c.OffsetAt(c.Days - c.EntryDays - eps) - c.OffsetAt(c.Days - c.EntryDays + eps)).Length, Is.LessThan(5));
            Assert.That(c.CorrectionBurnKmS, Is.LessThan(c.DeltaVKmS * 0.5), "the trims at the handoffs are small beside the burns");

            // It leaves exactly from its parking orbit (the escape burn at its own spot there), and flies
            // smoothly: no speed along the way above what its conic and hyperbolas give it.
            Assert.That((c.OffsetAt(0) - (c.FromR0 + c.Escape.P * c.Escape.Rp)).Length, Is.LessThan(1e-3));
            double top = Math.Max(Math.Max(c.V0.Length, c.V1.Length), Math.Max(
                (c.FromV0 + c.Escape.Q * c.Escape.PeriapsisSpeed).Length, (c.ToV1 + c.Capture.Q * c.Capture.PeriapsisSpeed).Length));
            for (int i = 0; i < 2000; i++)
            {
                double t0 = c.Days * i / 2000, t1 = c.Days * (i + 1) / 2000;
                double speed = (c.OffsetAt(t1) - c.OffsetAt(t0)).Length / (t1 - t0);
                Assert.That(speed, Is.LessThan(top * 1.15), $"speed at {t0:F2} d");
            }

            StepUntil(r, () => ship.Phase == ShipPhase.Parked && ship.Orbit.Body == mars, 400, 0.5, ship.PhaseStartJd);
        }

        [Test]
        public void ASlowBoatThatJustMissedItsWindowWaitsInPortForTheNextOne()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            int earth = eph.Catalog["Earth"].Index, mars = eph.Catalog["Mars"].Index;
            double lead = Conics.HohmannLeadAngle(SunGm, Astro.AuKm, MarsAu * Astro.AuKm);
            double Phase(double jd)
            {
                eph.Update(jd);
                var a = eph.Helio(earth);
                var b = eph.Helio(mars);
                return Math.Atan2(DVec3.Cross(a, b).Z, DVec3.Dot(a, b));
            }
            // A date months after the next Earth-to-Mars window.
            double missed = StartJd;
            for (double jd = StartJd; jd < StartJd + 800; jd += 1)
                if (Phase(jd) < lead - 80 * Astro.Deg2Rad && Phase(jd - 1) >= lead - 80 * Astro.Deg2Rad) { missed = jd; break; }
            Assert.That(missed, Is.GreaterThan(StartJd));

            var r = new Run { Eph = new Ephemeris(BodyCatalog.CreateSolSystem()) };
            r.Eph.Update(missed);
            // Nothing pressing: a day of its time is worth next to nothing against propellant.
            r.Fleet = new Fleet(r.Eph, 5) { BurnsPropellant = s => true, AvoidCollisions = false, OrbitalTransfers = true, TimeValue = s => 1 };
            r.Fleet.ChooseNext = (s, here, jd) => here == earth ? mars : earth;
            var f = r.Fleet.AddFaction("Traders", "MV", default);
            var ship = r.Fleet.AddShip("Patience", ShipClass.Freighter, f, ShipRole.Trader);
            ship.Ports = new[] { earth, mars };
            r.Fleet.Station(ship, earth, missed);
            Assert.That(double.IsNaN(ship.PlannedConicDepartJd), Is.False);
            double wait = ship.PlannedConicDepartJd - missed;
            TestContext.Out.WriteLine($"waits {wait:F0} d, flies {ship.PlannedConicDays:F0} d, phase {Phase(ship.PlannedConicDepartJd) * Astro.Rad2Deg:F1} vs lead {lead * Astro.Rad2Deg:F1}");
            Assert.That(wait, Is.InRange(300, 800), "it waits most of a synodic period for the next window");
            double phaseAtDeparture = Phase(ship.PlannedConicDepartJd);
            Assert.That(Math.Abs(phaseAtDeparture - lead) * Astro.Rad2Deg, Is.LessThan(30), "and leaves with Mars about 44° ahead");
            Assert.That(ship.PlannedConicDays, Is.InRange(180, 330), "on a near-Hohmann transfer of the better part of a year");
        }

        [Test]
        public void UrgentCargoPaysForSpeedWhereCheapFreightTakesTheSlowBoat()
        {
            var cheap = Shuttle(ShipClass.Freighter, "Earth", "Mars", setup: s =>
            {
                s.HasCargo = true;
                s.CargoTonnes = 2000;
                s.CargoCost = 20000;
            });
            var urgent = Shuttle(ShipClass.Freighter, "Earth", "Mars", setup: s =>
            {
                s.HasCargo = true;
                s.CargoTonnes = 2000;
                s.CargoCost = 2000000;
                s.CargoBonus = 500000;
            });
            Assert.That(Fleet.TimeValuePerDay(urgent.Ship), Is.GreaterThan(Fleet.TimeValuePerDay(cheap.Ship) * 10));
            Assert.That(cheap.Ship.PlannedConicDays, Is.GreaterThan(0), "cheap freight plans a transfer orbit");
            Assert.That(urgent.Ship.PlannedConicDays, Is.LessThan(0), "urgent cargo judges the flip-and-burn worth it");

            int earth = cheap.Eph.Catalog["Earth"].Index, mars = cheap.Eph.Catalog["Mars"].Index;
            var slowTrip = cheap.Fleet.EstimateTrip(cheap.Ship, earth, mars, StartJd);
            var fastTrip = urgent.Fleet.EstimateTrip(urgent.Ship, earth, mars, StartJd);
            Assert.That(fastTrip.Days, Is.LessThan(slowTrip.Days / 3), "the urgent one flies there in days");
            TestContext.Out.WriteLine($"cheap: {slowTrip.Days:F1} d on {slowTrip.DeltaVKmS:F0} km/s; urgent: {fastTrip.Days:F1} d on {fastTrip.DeltaVKmS:F0} km/s");
            Assert.That(fastTrip.DeltaVKmS, Is.GreaterThan(slowTrip.DeltaVKmS * 5), "and pays for it in delta-v");

            StepUntil(urgent, () => urgent.Ship.Phase == ShipPhase.Transit, 30);
            Assert.That(urgent.Ship.Conic, Is.Null, "a flip-and-burn");
            Assert.That(urgent.Ship.PhaseEndJd - urgent.Ship.PhaseStartJd, Is.LessThan(30));
        }

        [Test]
        public void ThePlannersEstimateOfATransferIsInLineWithWhatIsFlown()
        {
            var r = Shuttle(ShipClass.Freighter, "Earth", "Moon");
            var ship = r.Ship;
            int earth = r.Eph.Catalog["Earth"].Index, moon = r.Eph.Catalog["Moon"].Index;
            var estimate = r.Fleet.EstimateTrip(ship, earth, moon, StartJd);
            StepUntil(r, () => ship.Phase == ShipPhase.Transit, 30);
            double flown = Propulsion.TransitDeltaV(ship);
            Assert.That(estimate.DeltaVKmS, Is.InRange(flown * 0.3, flown * 3));
            Assert.That(estimate.Days, Is.InRange((ship.PhaseEndJd - ship.PhaseStartJd) * 0.3, (ship.PhaseEndJd - ship.PhaseStartJd) * 3 + 1.5));
        }

        [Test]
        public void InTheLivingSystemHaulersFlyTransferOrbitsAndWarshipsFlipAndBurn()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            int conics = 0, torches = 0, militaryConics = 0;
            for (double jd = StartJd; jd < StartJd + 20; jd += 0.25)
            {
                w.Update(jd);
                foreach (var s in w.Fleet.Ships)
                {
                    if (s.Destroyed || s.Phase != ShipPhase.Transit) continue;
                    if (s.Conic != null)
                    {
                        conics++;
                        if (Fleet.DriveOf(s) == DriveKind.Torch) militaryConics++;
                    }
                    else if (Fleet.DriveOf(s) == DriveKind.Torch) torches++;
                }
            }
            Assert.That(conics, Is.GreaterThan(0), "slow boats are out on transfer orbits");
            Assert.That(torches, Is.GreaterThan(0), "torches still flip and burn");
            Assert.That(militaryConics, Is.EqualTo(0));
        }
    }
}
