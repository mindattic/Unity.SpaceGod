using System;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Interstellar navigation (U.SPC-T420): a course plotted to any star or beacon, with the run out beyond the rim, the
    /// charge, the light-years, any interdiction on the line, the ETA and the reaction mass, all as the trip is then
    /// flown; the FTL autopilot that flies it; and the time compression a long trip is flown at.
    /// </summary>
    [TestFixture]
    public class FtlNavigationTests
    {
        const double StartJd = 2461324.0;

        static (Galaxy g, StarSystem a, StarSystem b) Relays(double ly = 4)
        {
            var g = new Galaxy();
            var a = StarSystem.CreateVoidBase("Void Base West", new DVec3(0, 0, 0), StartJd, g.Polities);
            var b = StarSystem.CreateVoidBase("Void Base East", new DVec3(ly, 0, 0), StartJd, g.Polities);
            g.Systems.Add(a);
            g.Systems.Add(b);
            return (g, a, b);
        }

        static Faction Navy() => new Faction("Test Navy", "TNS", default) { IsMilitary = true };

        static Ship Spawn(StarSystem s, string name, ShipClass cls, DVec3 atKm, DVec3 forward) =>
            s.World.Fleet.SpawnAt(name, cls, Navy(), s.Catalog.Root.Index, atKm, forward, StartJd);

        static DVec3 BeyondRim(StarSystem s) => new DVec3(HelmFtl.RimKm(s) * 1.1, 0, 0);

        [Test]
        public void ACourseFromBeyondTheRimIsTheJumpTheHelmThenFlies()
        {
            var (g, a, b) = Relays(4);
            var ship = Spawn(a, "TNS Meridian", ShipClass.Battleship, BeyondRim(a), DVec3.UnitX);
            var at = ship.Orbit.Center;
            double tanks = ship.Propellant;
            var course = FtlCourse.Plot(g, a, ship, at, FtlTarget.OfStar(b), StartJd);
            Assert.That(course.Flyable, Is.True, course.Refusal);
            Assert.That(course.NeedsOutRun, Is.False);
            Assert.That(course.DepartureKm, Is.EqualTo(at));
            Assert.That(course.Interdictions, Is.Empty);
            Assert.That(course.SpoolDays, Is.EqualTo(CruiseDrives.Capital.SpoolDays).Within(1e-12));
            Assert.That(course.Ly, Is.EqualTo(4).Within(0.01));
            Assert.That(course.TotalDays, Is.EqualTo(course.SpoolDays + course.FtlDays).Within(1e-12));
            Assert.That(course.EtaJd, Is.EqualTo(StartJd + course.TotalDays).Within(1e-12));
            Assert.That(course.FuelTonnes, Is.GreaterThan(0).And.LessThan(tanks));

            // Flown at the helm, it drops out when the plot said, having burnt what the plot said.
            var ftl = new HelmFtl();
            Assert.That(ftl.Spool(ship, a, course.Target, at, DVec3.UnitX, false, StartJd), Is.True, ftl.AbortReason);
            var c = ftl.EngageInterstellar(g, a, ship, at, ftl.ChargedJd);
            Assert.That(c, Is.Not.Null, ftl.AbortReason);
            Assert.That(c.ArriveJd, Is.EqualTo(course.EtaJd).Within(1e-6));
            Assert.That(tanks - ship.Propellant, Is.EqualTo(course.JumpFuelTonnes).Within(1e-6));
        }

        [Test]
        public void InsideTheRimTheCourseRunsOutBeyondItFirst()
        {
            var (g, a, b) = Relays(4);
            var from = new DVec3(5e7, 3e7, 0);
            var ship = Spawn(a, "TNS Meridian", ShipClass.Battleship, from, DVec3.UnitY);
            var course = FtlCourse.Plot(g, a, ship, from, FtlTarget.OfStar(b), StartJd);
            Assert.That(course.Flyable, Is.True, course.Refusal);
            Assert.That(course.NeedsOutRun, Is.True);
            // Out beyond the rim, clear to jump, on the star's side facing the target.
            Assert.That(HelmFtl.WellRefusal(a, course.DepartureKm, g), Is.Null);
            Assert.That(course.DepartureKm.X, Is.GreaterThan(HelmFtl.RimKm(a)));
            Assert.That(course.OutRunKm, Is.EqualTo((course.DepartureKm - from).Length).Within(1e-6));
            // Flip and burn, at the hull's own thrust or gentler, never burning more than half the reaction mass the jump leaves.
            double accel = course.OutRunAccelKmS2;
            Assert.That(accel, Is.GreaterThan(0).And.LessThanOrEqualTo(ship.Spec.AccelG * 9.80665e-3 + 1e-15));
            Assert.That(course.OutRunDays * Astro.SecondsPerDay, Is.EqualTo(2 * Math.Sqrt(course.OutRunKm / accel)).Within(1));
            Assert.That(course.OutRunFuelTonnes, Is.GreaterThan(0).And.LessThanOrEqualTo((ship.Propellant - course.JumpFuelTonnes) * FtlCourse.OutRunShare + 1e-6));
            Assert.That(course.OutRunFuelTonnes, Is.EqualTo(Propulsion.PropellantFor(ship, Propulsion.BrachistochroneDeltaV(accel, course.OutRunKm))).Within(1e-3));

            // A hull with the thrust to spare and the tanks full flies it at its own acceleration.
            ship.Propellant = ship.PropellantCapacity;
            var close = FtlCourse.Plot(g, a, ship, new DVec3(HelmFtl.RimKm(a) * 0.98, 0, 0), FtlTarget.OfStar(b), StartJd);
            Assert.That(close.OutRunAccelKmS2, Is.EqualTo(ship.Spec.AccelG * 9.80665e-3).Within(1e-12));
            Assert.That(course.FuelTonnes, Is.EqualTo(course.OutRunFuelTonnes + course.JumpFuelTonnes).Within(1e-9));
            Assert.That(course.TotalDays, Is.EqualTo(course.OutRunDays + course.CoolDays + course.SpoolDays + course.FtlDays + course.CrossingDays).Within(1e-12));
        }

        [Test]
        public void ACourseThatCannotBeFlownSaysWhy()
        {
            var (g, a, b) = Relays(4);
            var fighter = Spawn(a, "Gnat", ShipClass.Fighter, BeyondRim(a), DVec3.UnitX);
            Assert.That(FtlCourse.Plot(g, a, fighter, fighter.Orbit.Center, FtlTarget.OfStar(b), StartJd).Refusal, Does.Contain("no FTL drive"));

            var ship = Spawn(a, "TNS Meridian", ShipClass.Battleship, BeyondRim(a), DVec3.UnitX);
            Assert.That(FtlCourse.Plot(g, a, ship, ship.Orbit.Center, FtlTarget.OfStar(a), StartJd).Refusal, Does.Contain("within a system"));
            Assert.That(FtlCourse.Plot(g, a, ship, ship.Orbit.Center, null, StartJd).Refusal, Does.Contain("No star"));

            // Nearly dry: out of range, the distance still worked out.
            ship.Propellant = ship.PropellantCapacity * 0.021;
            var dry = FtlCourse.Plot(g, a, ship, ship.Orbit.Center, FtlTarget.OfStar(b), StartJd);
            Assert.That(dry.Flyable, Is.False);
            Assert.That(dry.Refusal, Does.Contain("range"));
            Assert.That(dry.Ly, Is.EqualTo(4).Within(0.01));

            // And the autopilot will not take a course it cannot fly.
            var pilot = new FtlAutopilot();
            Assert.That(pilot.Engage(dry, ship.Orbit.Center, false, StartJd), Is.False);
            Assert.That(pilot.Active, Is.False);
            Assert.That(pilot.Reason, Does.Contain("range"));
        }

        [Test]
        public void AGravityProjectorOnTheLineIsPlottedAndTheTripKeepsToThePlot()
        {
            var (g, a, b) = Relays(4);
            var ship = Spawn(a, "TNS Meridian", ShipClass.Battleship, BeyondRim(a), DVec3.UnitX);
            var at = ship.Orbit.Center;
            // Off the 0.05-day step grid's points (U.SPC-T457: a step that ends inside a field misses it).
            var mid = GalacticPosition.FromLy(new DVec3(2.02, 0, 0));
            g.DeepSpace.AddProjector(new GravityProjector { Name = "Snare", Position = mid, RadiusKm = 2e9 });
            double tanks = ship.Propellant;
            var course = FtlCourse.Plot(g, a, ship, at, FtlTarget.OfStar(b), StartJd);
            Assert.That(course.Flyable, Is.True, course.Refusal);
            Assert.That(course.Interdictions, Is.EqualTo(new[] { "Snare" }));
            Assert.That(course.CrossingDays, Is.EqualTo(4e9 / FtlRules.CrossingSpeedKmS.Value / Astro.SecondsPerDay).Within(1e-3));

            var ftl = new HelmFtl();
            Assert.That(ftl.Spool(ship, a, course.Target, at, DVec3.UnitX, false, StartJd), Is.True, ftl.AbortReason);
            double charged = ftl.ChargedJd;
            var c = ftl.EngageInterstellar(g, a, ship, at, charged);
            Assert.That(c, Is.Not.Null);
            bool pulled = false;
            double arrived = double.NaN;
            for (double t = charged; t < course.EtaJd + 2 && double.IsNaN(arrived); t += 0.01)
            {
                g.Update(t);
                pulled |= c.Crossing;
                if (!g.DeepSpace.Cruises.Contains(c)) arrived = t;
            }
            Assert.That(pulled, Is.True, "never pulled out by the projector");
            Assert.That(arrived, Is.EqualTo(course.EtaJd).Within(0.02), "dropped out off the plotted ETA");
            Assert.That(tanks - ship.Propellant, Is.EqualTo(course.JumpFuelTonnes).Within(1e-6));
        }

        [Test]
        public void TheAutopilotFliesOutTurnsOntoTheStarAndHandsTheChargeToTheDrive()
        {
            var (g, a, b) = Relays(4);
            var from = new DVec3(5e7, 0, 0);
            var ship = Spawn(a, "TNS Meridian", ShipClass.Battleship, from, DVec3.UnitY);
            var course = FtlCourse.Plot(g, a, ship, from, FtlTarget.OfStar(b), StartJd);
            double tanks = ship.Propellant;
            var pilot = new FtlAutopilot();
            Assert.That(pilot.Engage(course, from, false, StartJd), Is.True, pilot.Reason);
            Assert.That(pilot.Stage, Is.EqualTo(FtlAutopilotStage.OutRun));
            Assert.That(tanks - ship.Propellant, Is.EqualTo(course.OutRunFuelTonnes).Within(1e-9), "the run out is paid for");
            Assert.That(pilot.RunEndJd - StartJd, Is.EqualTo(course.OutRunDays).Within(1e-9));

            // Closed form: from rest, half way at half time, at rest again at the far end.
            double half = (StartJd + pilot.RunEndJd) / 2;
            Assert.That(pilot.PositionKm(StartJd), Is.EqualTo(from));
            Assert.That((pilot.PositionKm(half) - DVec3.Lerp(from, course.DepartureKm, 0.5)).Length, Is.LessThan(1));
            Assert.That(pilot.VelocityKmS(StartJd).Length, Is.LessThan(1e-9));
            double peak = pilot.VelocityKmS(half).Length;
            Assert.That(peak, Is.EqualTo(course.OutRunAccelKmS2 * (half - StartJd) * Astro.SecondsPerDay).Within(peak * 1e-6));
            Assert.That(pilot.Step(false, half), Is.EqualTo(FtlAutopilotEvent.None));

            Assert.That(pilot.Step(false, pilot.RunEndJd), Is.EqualTo(FtlAutopilotEvent.Arrived));
            Assert.That(pilot.Stage, Is.EqualTo(FtlAutopilotStage.Aligning));
            var at = pilot.PositionKm(pilot.RunEndJd);
            Assert.That((at - course.DepartureKm).Length, Is.LessThan(1e-3));

            // The nose swung round a step at a time, until it is on the star.
            var forward = DVec3.UnitY * -1;
            int steps = 0;
            while (pilot.Turn(ref forward, at, 10) != FtlAutopilotEvent.Aligned && steps < 100) steps++;
            Assert.That(steps, Is.InRange(8, 30), "turns at its rate");
            Assert.That(HelmFtl.OffNoseDeg(course.Target, a, at, forward), Is.LessThan(HelmFtl.AlignDeg));

            // The drive spools there, for the plotted star.
            var ftl = new HelmFtl();
            Assert.That(ftl.Spool(ship, a, course.Target, at, forward, false, pilot.RunEndJd, g), Is.True, ftl.AbortReason);
            pilot.Spooling();
            Assert.That(pilot.Stage, Is.EqualTo(FtlAutopilotStage.Spooling));
        }

        [Test]
        public void AnAlignedAutopilotHoldsCoolingUntilTheDriveHasCooled()
        {
            var (g, a, b) = Relays(4);
            var from = new DVec3(5e7, 0, 0);
            var ship = Spawn(a, "TNS Meridian", ShipClass.Battleship, from, DVec3.UnitX);
            var course = FtlCourse.Plot(g, a, ship, from, FtlTarget.OfStar(b), StartJd);
            var pilot = new FtlAutopilot();
            Assert.That(pilot.Engage(course, from, false, StartJd), Is.True);
            ship.FtlCoolUntilJd = pilot.RunEndJd + 0.5;
            var at = course.DepartureKm;
            var forward = course.Bearing(at, out _);
            var ftl = new HelmFtl();
            Assert.That(ftl.Spool(ship, a, course.Target, at, forward, false, pilot.RunEndJd, g), Is.False, "the drive is still cooling");
            Assert.That(ftl.AbortReason, Does.Contain("cooling"));
            pilot.Step(false, pilot.RunEndJd);
            pilot.Cooling();
            Assert.That(pilot.Stage, Is.EqualTo(FtlAutopilotStage.Cooling));
            Assert.That(pilot.Active, Is.True);
            Assert.That(pilot.Step(false, pilot.RunEndJd + 0.25), Is.EqualTo(FtlAutopilotEvent.None));
            Assert.That(pilot.Stage, Is.EqualTo(FtlAutopilotStage.Cooling));
            Assert.That(ftl.Spool(ship, a, course.Target, at, forward, false, FtlRules.CoolUntil(ship), g), Is.True, ftl.AbortReason);
        }

        [Test]
        public void AFightLetsGoOfTheAutopilot()
        {
            var (g, a, b) = Relays(4);
            var from = new DVec3(5e7, 0, 0);
            var ship = Spawn(a, "TNS Meridian", ShipClass.Battleship, from, DVec3.UnitX);
            var course = FtlCourse.Plot(g, a, ship, from, FtlTarget.OfStar(b), StartJd);
            var pilot = new FtlAutopilot();
            Assert.That(pilot.Engage(course, from, true, StartJd), Is.False, "nor will it take the helm in one");
            Assert.That(pilot.Engage(course, from, false, StartJd), Is.True);
            Assert.That(pilot.Step(true, StartJd + 0.01), Is.EqualTo(FtlAutopilotEvent.Disengaged));
            Assert.That(pilot.Active, Is.False);
            Assert.That(pilot.Reason, Does.Contain("fight"));
        }

        [Test]
        public void ADisengagedRunRefundsTheUnflownOutRunFuel()
        {
            var (g, a, b) = Relays(4);
            var from = new DVec3(5e7, 0, 0);
            var ship = Spawn(a, "TNS Meridian", ShipClass.Battleship, from, DVec3.UnitX);
            var course = FtlCourse.Plot(g, a, ship, from, FtlTarget.OfStar(b), StartJd);
            Assert.That(course.NeedsOutRun && course.OutRunFuelTonnes > 0, Is.True);
            double before = ship.Propellant;
            var pilot = new FtlAutopilot();
            Assert.That(pilot.Engage(course, from, false, StartJd), Is.True);
            Assert.That(ship.Propellant, Is.EqualTo(before - course.OutRunFuelTonnes).Within(1e-6));
            double half = StartJd + (pilot.RunEndJd - StartJd) / 2;
            pilot.Disengage("test", half);
            Assert.That(ship.Propellant, Is.EqualTo(before - course.OutRunFuelTonnes / 2).Within(1e-6), "half the run flown, half refunded");

            pilot = new FtlAutopilot();
            ship.Propellant = before;
            pilot.Engage(course, from, false, StartJd);
            pilot.Disengage("test", StartJd);
            Assert.That(ship.Propellant, Is.EqualTo(before).Within(1e-6), "broken at once, nothing lost");
        }

        [Test]
        public void ALongTripIsCompressedToTheMenusRealSecondsOnTheSimClock()
        {
            // Four days in about 45 real seconds wants at least 7,680 sim seconds a second: six hours a second.
            int i = FtlNavigation.WarpFor(4, 0, 45);
            Assert.That(SimClock.Levels[i].SimSecondsPerSecond, Is.EqualTo(6 * 3600));
            Assert.That(4 * Astro.SecondsPerDay / SimClock.Levels[i].SimSecondsPerSecond, Is.LessThanOrEqualTo(45));
            Assert.That(4 * Astro.SecondsPerDay / SimClock.Levels[i - 1].SimSecondsPerSecond, Is.GreaterThan(45));
            // A century-long crossing runs at the clock's top speed; nothing left, or a pace already set faster, is kept.
            Assert.That(FtlNavigation.WarpFor(36525, 0, 45), Is.EqualTo(SimClock.Levels.Length - 1));
            Assert.That(FtlNavigation.WarpFor(0, 3), Is.EqualTo(3));
            Assert.That(FtlNavigation.WarpFor(4, 8, 45), Is.EqualTo(8));
            // The menu's own value by default.
            Assert.That(FtlNavigation.WarpFor(4, 0), Is.EqualTo(FtlNavigation.WarpFor(4, 0, FtlNavigation.TripRealSeconds.Value)));
        }

        [Test]
        public void StarsAndBeaconsAreOfferedNearestFirstAndCycled()
        {
            var g = new Galaxy();
            var sol = StarSystem.CreateSol(StartJd, g.Polities);
            var near = StarSystem.CreateVoidBase("Void Base Near", new DVec3(3, 0, 0), StartJd, g.Polities);
            var far = StarSystem.CreateVoidBase("Void Base Far", new DVec3(0, 9, 0), StartJd, g.Polities);
            g.Systems.Add(sol);
            g.Systems.Add(far);
            g.Systems.Add(near);
            var list = FtlNavigation.Nearest(g, sol, DVec3.Zero);
            Assert.That(list.Select(t => t.System).Take(2), Is.EqualTo(new[] { near, far }));
            Assert.That(list[0].IsBeacon, Is.True, "a void station is a beacon");
            Assert.That(list[0].KindName, Is.EqualTo("beacon"));
            Assert.That(FtlTarget.OfStar(sol).IsBeacon, Is.False);

            Assert.That(FtlNavigation.Cycle(list, null, 1).System, Is.SameAs(near));
            Assert.That(FtlNavigation.Cycle(list, list[0], 1).System, Is.SameAs(far));
            Assert.That(FtlNavigation.Cycle(list, list[0], -1), Is.SameAs(list[list.Count - 1]));
            Assert.That(FtlNavigation.Cycle(list, FtlTarget.OfStar(near), 1).System, Is.SameAs(far), "the same place, however it was found");
        }

        [Test]
        public void TheNoseSwingsTowardItsTargetNoFasterThanAsked()
        {
            var turned = FtlAutopilot.TurnToward(DVec3.UnitX, DVec3.UnitY, 10 * Astro.Deg2Rad);
            Assert.That(Math.Acos(DVec3.Dot(turned, DVec3.UnitX)) * Astro.Rad2Deg, Is.EqualTo(10).Within(1e-6));
            Assert.That(turned.Z, Is.EqualTo(0).Within(1e-9));
            Assert.That(FtlAutopilot.TurnToward(DVec3.UnitX, DVec3.UnitY, Math.PI), Is.EqualTo(DVec3.UnitY));
            // Dead astern still turns.
            var back = FtlAutopilot.TurnToward(DVec3.UnitX, -DVec3.UnitX, 0.5);
            Assert.That(Math.Acos(DVec3.Dot(back, DVec3.UnitX)), Is.EqualTo(0.5).Within(1e-6));
        }
    }
}
