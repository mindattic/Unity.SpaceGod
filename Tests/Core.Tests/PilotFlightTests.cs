using System;
using UnitySpace.Core;
using NUnit.Framework;

namespace UnitySpace.Core.Tests
{
    [TestFixture]
    public class PilotFlightTests
    {
        static ShipCombat Combat(ShipClass c) => new ShipCombat(CombatSpec.Of(c), c);

        static HullDynamics Hull(ShipClass c) => HullDynamics.Of(CombatSpec.Of(c));

        static double AngleDeg(DVec3 a, DVec3 b) => Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(a.Normalized, b.Normalized)))) * Astro.Rad2Deg;

        /// <summary>A hull of class <paramref name="c"/> with its own combat state, flying in empty space (no gravity).</summary>
        sealed class Rig
        {
            public PilotState S;
            public readonly ShipCombat Combat;
            public readonly HullDynamics Hull;
            public readonly PilotTuning Tuning;

            public Rig(ShipClass c, DVec3 vel = default, DVec3? forward = null)
            {
                Combat = PilotFlightTests.Combat(c);
                Hull = PilotFlightTests.Hull(c);
                Tuning = PilotTuning.Of(c);
                S = PilotState.At(DVec3.Zero, vel, forward ?? DVec3.UnitX);
            }

            public void Fly(PilotInput input, double dt, int steps = 1, DVec3 gravity = default)
            {
                for (int i = 0; i < steps; i++)
                    PilotFlight.Step(ref S, Combat, Hull, Tuning, input, gravity, dt);
            }
        }

        // ------------------------------------------------------------ the hull's own physics

        [Test]
        public void EveryFlyableDesignHasMassThrustOnEveryAxisAndInertia()
        {
            foreach (ShipClass c in Enum.GetValues(typeof(ShipClass)))
            {
                var spec = CombatSpec.Of(c);
                Assert.That(spec.MassKt, Is.GreaterThan(0), c + " has no mass");
                Assert.That(spec.PitchYawInertia, Is.GreaterThan(spec.RollInertia), c + ": a long hull should roll more readily than it turns");
                var h = HullDynamics.Of(spec);
                Assert.That(h.MainAccel, Is.GreaterThan(h.RetroAccel), c + ": the main drive should out-push the retro thrusters");
                Assert.That(h.RetroAccel, Is.GreaterThan(0), c + " cannot brake");
                Assert.That(h.LateralAccel, Is.GreaterThan(0).And.LessThan(h.MainAccel), c + " lateral thrust");
                Assert.That(h.VerticalAccel, Is.GreaterThan(0).And.LessThan(h.MainAccel), c + " vertical thrust");
                Assert.That(h.PitchYawAngularAccel, Is.GreaterThan(0), c + " cannot turn");
                Assert.That(h.RollAngularAccel, Is.GreaterThan(0), c + " cannot roll");
            }
            // Heavier hulls are slower to spin up: a carrier's nose is far harder to start swinging than a fighter's.
            Assert.That(Hull(ShipClass.Carrier).PitchYawAngularAccel, Is.LessThan(Hull(ShipClass.Fighter).PitchYawAngularAccel / 50));
            Assert.That(HullDynamics.Of(CombatSpec.Platform).MainAccel, Is.EqualTo(0), "a defence platform has no drive");
        }

        [Test]
        public void ThrustIsForceOverMass()
        {
            var spec = CombatSpec.Of(ShipClass.Cruiser);
            var h = HullDynamics.Of(spec);
            // MN / kt = m/s², so km/s² is that over a thousand.
            Assert.That(h.MainAccel, Is.EqualTo(spec.MainThrustMN / spec.MassKt / 1000).Within(1e-15));
            Assert.That(h.PitchYawAngularAccel, Is.EqualTo(spec.PitchYawTorqueMNKm / (spec.MassKt * spec.LengthKm * spec.LengthKm / 12) / 1000).Within(1e-15));
        }

        // ------------------------------------------------------------ momentum (U.SPC-T59)

        [Test]
        public void WithNoThrustAndNoGravityMomentumIsConservedExactly()
        {
            var v0 = new DVec3(0.12, -0.05, 0.03);
            var rig = new Rig(ShipClass.Destroyer, v0);
            // Set it spinning too: a slow tumble about an off-axis direction.
            rig.S.AngularVelocity = new DVec3(0.01, 0.02, -0.015);
            var w0 = rig.S.AngularVelocity;
            var p0 = rig.S.Pos;
            var idle = new PilotInput { FlightAssistOff = true };

            const double dt = 0.05;
            const int steps = 4000;
            rig.Fly(idle, dt, steps);

            double m = rig.Hull.MassKt;
            Assert.That((rig.S.Vel * m - v0 * m).Length, Is.LessThan(1e-12), "linear momentum changed with no force on the hull");
            Assert.That((rig.S.Pos - (p0 + v0 * (dt * steps))).Length, Is.LessThan(1e-9), "the hull did not coast in a straight line");
            Assert.That((rig.S.AngularVelocity - w0).Length, Is.LessThan(1e-12), "spin changed with no torque on the hull");
            Assert.That(DVec3.Dot(rig.S.Forward, rig.S.Up), Is.EqualTo(0).Within(1e-9), "attitude lost its shape while tumbling");
            Assert.That(rig.S.Forward.Length, Is.EqualTo(1).Within(1e-9));
        }

        [Test]
        public void ABurnChangesMomentumByExactlyItsImpulse()
        {
            var rig = new Rig(ShipClass.Cruiser);
            var burn = new PilotInput { Throttle = 1, FlightAssistOff = true };
            const double dt = 0.1;
            rig.Fly(burn, dt, 300);

            // Δp = F·t: the main drive's thrust (MN = kt·m/s²) for 30 s, in kt·km/s.
            double impulse = CombatSpec.Of(ShipClass.Cruiser).MainThrustMN / 1000 * 30;
            Assert.That(rig.S.Vel.Length * rig.Hull.MassKt, Is.EqualTo(impulse).Within(impulse * 1e-9));
            Assert.That(AngleDeg(rig.S.Vel, DVec3.UnitX), Is.LessThan(1e-9), "the burn should push straight along the nose");
            // With assist off there is no set point: a long burn runs far past flight assist's cruising speed.
            rig.Fly(burn, dt, 3000);
            Assert.That(rig.S.Vel.Length, Is.GreaterThan(rig.Tuning.MaxSpeed * 5));
        }

        // ------------------------------------------------------------ flight assist off: drift (U.SPC-T59)

        [Test]
        public void FlightAssistOffTheHullSlidesOnItsOldHeadingWhileTheNoseTurns()
        {
            var rig = new Rig(ShipClass.Fighter, DVec3.UnitX * 0.3);
            var turnAway = new PilotInput { DesiredForward = DVec3.UnitY, FlightAssistOff = true };
            rig.Fly(turnAway, 0.05, 200);

            Assert.That(AngleDeg(rig.S.Forward, DVec3.UnitY), Is.LessThan(1e-6), "the nose should have come round");
            Assert.That((rig.S.Vel - DVec3.UnitX * 0.3).Length, Is.LessThan(1e-12), "with assist off, turning must not bend the track");
            Assert.That(rig.S.Pos.X, Is.EqualTo(0.3 * 10).Within(1e-9), "the hull kept sliding along its old line");
        }

        [Test]
        public void FlightAssistOffATurnAndBurnBrakesAHullToAStop()
        {
            const double speed = 0.3;
            var rig = new Rig(ShipClass.Fighter, DVec3.UnitX * speed);
            // Flip end for end with the drive off ...
            rig.Fly(new PilotInput { DesiredForward = -DVec3.UnitX, FlightAssistOff = true }, 0.02, 200);
            Assert.That(AngleDeg(rig.S.Forward, -DVec3.UnitX), Is.LessThan(1e-6));
            Assert.That(rig.S.Vel.X, Is.EqualTo(speed).Within(1e-12), "the flip itself must not touch the hull's speed");

            // ... then burn the main drive against the motion for exactly as long as it takes.
            double burnS = speed / rig.Hull.MainAccel;
            int steps = (int)Math.Round(burnS / 0.01);
            rig.Fly(new PilotInput { DesiredForward = -DVec3.UnitX, Throttle = 1, FlightAssistOff = true }, burnS / steps, steps);
            Assert.That(rig.S.Vel.Length, Is.LessThan(1e-9), "a burn of v/a against the motion should stop the hull dead");
        }

        [Test]
        public void FlightAssistOffNothingStopsAHullThatHasBeenSetDrifting()
        {
            var rig = new Rig(ShipClass.Courier);
            rig.Fly(new PilotInput { DesiredForward = DVec3.UnitX, StrafeRight = 1, FlightAssistOff = true }, 0.1, 20);
            var drift = rig.S.Vel;
            Assert.That(DVec3.Dot(drift, rig.S.Right), Is.GreaterThan(0), "strafe should push the hull to starboard");

            rig.Fly(new PilotInput { DesiredForward = DVec3.UnitX, FlightAssistOff = true }, 0.1, 500);
            Assert.That((rig.S.Vel - drift).Length, Is.LessThan(1e-12), "letting go of the strafe with assist off should leave the drift as it was");
        }

        [Test]
        public void FlightAssistOffARollKeepsGoingUntilItIsCountered()
        {
            var rig = new Rig(ShipClass.Fighter);
            rig.Fly(new PilotInput { DesiredForward = DVec3.UnitX, Roll = 1, FlightAssistOff = true }, 0.01, 5);
            double rate = rig.S.RollRateDegS;
            Assert.That(rate, Is.GreaterThan(0), "roll input should spin the hull up");

            var upBefore = rig.S.Up;
            rig.Fly(new PilotInput { DesiredForward = DVec3.UnitX, FlightAssistOff = true }, 0.01, 10);
            Assert.That(rig.S.RollRateDegS, Is.EqualTo(rate).Within(1e-9), "nothing should damp a roll with assist off");
            Assert.That(AngleDeg(rig.S.Up, upBefore), Is.GreaterThan(0), "the hull should still be rolling");
            Assert.That(AngleDeg(rig.S.Forward, DVec3.UnitX), Is.LessThan(1e-9), "a roll turns about the nose and must not move it");
        }

        // ------------------------------------------------------------ flight assist on: settling (U.SPC-T59)

        [Test]
        public void FlightAssistOnKillsDriftAndSettlesOnTheCommandedVelocityWithoutOvershoot()
        {
            var rig = new Rig(ShipClass.Destroyer, new DVec3(0, 0.2, 0.05));
            var input = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1 };
            double lastSide = double.MaxValue;
            for (int i = 0; i < 2000; i++)
            {
                rig.Fly(input, 0.05);
                double side = Math.Sqrt(Math.Pow(DVec3.Dot(rig.S.Vel, rig.S.Right), 2) + Math.Pow(DVec3.Dot(rig.S.Vel, rig.S.Up), 2));
                Assert.That(side, Is.LessThanOrEqualTo(lastSide + 1e-12), "the sideways drift should only ever shrink");
                Assert.That(DVec3.Dot(rig.S.Vel, rig.S.Forward), Is.LessThanOrEqualTo(rig.Tuning.MaxSpeed + 1e-12), "assist overshot its set point");
                lastSide = side;
            }
            Assert.That((rig.S.Vel - DVec3.UnitX * rig.Tuning.MaxSpeed).Length, Is.LessThan(1e-12), "assist should settle exactly on the commanded velocity");
        }

        [Test]
        public void FlightAssistOnLettingGoOfEverythingBringsTheHullToRest()
        {
            var rig = new Rig(ShipClass.Cruiser, new DVec3(0.1, -0.08, 0.02));
            rig.S.AngularVelocity = new DVec3(0, 0, 0.05);
            rig.Fly(new PilotInput(), 0.05, 4000);
            Assert.That(rig.S.Vel.Length, Is.LessThan(1e-12), "assist should counter-thrust the hull to a stop");
            Assert.That(rig.S.AngularVelocity.Length, Is.LessThan(1e-12), "assist should stop the hull spinning");
        }

        [Test]
        public void FlightAssistOnCounterThrustIsLimitedByEachThrusterSet()
        {
            // Sliding sideways: the only thrusters that can kill it are the weak manoeuvre ones.
            var rig = new Rig(ShipClass.Battleship, DVec3.UnitY * 0.05);
            var before = rig.S.Vel;
            rig.Fly(new PilotInput { DesiredForward = DVec3.UnitX }, 1.0);
            Assert.That((before - rig.S.Vel).Length, Is.EqualTo(rig.Hull.LateralAccel).Within(1e-12), "braking a slide is limited to the lateral thrusters");
        }

        [Test]
        public void FlightAssistOnTheRollStopsWhenTheInputIsReleased()
        {
            var rig = new Rig(ShipClass.Fighter);
            rig.Fly(new PilotInput { DesiredForward = DVec3.UnitX, Roll = 1 }, 0.01, 100);
            Assert.That(rig.S.RollRateDegS, Is.EqualTo(rig.Tuning.RollRateDegS).Within(1e-9), "assist holds the roll at the roll rate");
            rig.Fly(new PilotInput { DesiredForward = DVec3.UnitX }, 0.01, 100);
            Assert.That(rig.S.RollRateDegS, Is.EqualTo(0).Within(1e-9), "assist should stop the roll once released");
            Assert.That(DVec3.Dot(rig.S.Up, rig.S.Forward), Is.EqualTo(0).Within(1e-9));
        }

        [Test]
        public void RollingTurnsTheStrafeAxesWithTheHull()
        {
            var rig = new Rig(ShipClass.Fighter);
            // Roll a quarter turn clockwise (from behind): the roof now points where starboard was.
            var starboard = rig.S.Right;
            for (int i = 0; i < 2000 && AngleDeg(rig.S.Up, starboard) > 0.5; i++)
                rig.Fly(new PilotInput { DesiredForward = DVec3.UnitX, Roll = 0.2 }, 0.005);
            rig.Fly(new PilotInput { DesiredForward = DVec3.UnitX }, 0.005, 400);
            Assert.That(AngleDeg(rig.S.Up, starboard), Is.LessThan(2), "a clockwise roll should bring the roof round to starboard");

            rig.S.Vel = DVec3.Zero;
            rig.Fly(new PilotInput { DesiredForward = DVec3.UnitX, StrafeUp = 1 }, 0.1, 100);
            Assert.That(AngleDeg(rig.S.Vel, starboard), Is.LessThan(2), "strafing 'up' on a rolled hull should push along its own roof");
        }

        // ------------------------------------------------------------ gravity (U.SPC-T59)

        const double EarthGm = 398600.435, EarthRadiusKm = 6371.0;

        [Test]
        public void GravityIsInverseSquareCloseInAndAbsentFarOut()
        {
            var atSurface = PilotFlight.Gravity(new DVec3(EarthRadiusKm, 0, 0), EarthGm, EarthRadiusKm);
            Assert.That(atSurface.Length, Is.EqualTo(0.00982).Within(0.00002), "Earth's surface gravity is 9.8 m/s²");
            Assert.That(atSurface.X, Is.LessThan(0), "gravity pulls toward the body");

            var twoRadii = PilotFlight.Gravity(new DVec3(0, 2 * EarthRadiusKm, 0), EarthGm, EarthRadiusKm);
            Assert.That(twoRadii.Length, Is.EqualTo(atSurface.Length / 4).Within(1e-12), "inverse-square");

            Assert.That(PilotFlight.Gravity(new DVec3(0, 0, EarthRadiusKm * (PilotFlight.GravityReachRadii + 0.1)), EarthGm, EarthRadiusKm), Is.EqualTo(DVec3.Zero));
            var nearEdge = PilotFlight.Gravity(new DVec3(0, 0, EarthRadiusKm * (PilotFlight.GravityReachRadii - 0.01)), EarthGm, EarthRadiusKm);
            Assert.That(nearEdge.Length, Is.GreaterThan(0).And.LessThan(EarthGm / Math.Pow(EarthRadiusKm * PilotFlight.GravityReachRadii, 2) * 0.1), "gravity eases in at the edge of its reach rather than switching on");
        }

        [Test]
        public void FlightAssistOffAHullFallsTowardANearbyBody()
        {
            var rig = new Rig(ShipClass.Courier);
            rig.S.Pos = new DVec3(2 * EarthRadiusKm, 0, 0);
            var idle = new PilotInput { FlightAssistOff = true };
            for (int i = 0; i < 100; i++)
                rig.Fly(idle, 0.1, 1, PilotFlight.Gravity(rig.S.Pos, EarthGm, EarthRadiusKm));
            double g = EarthGm / Math.Pow(2 * EarthRadiusKm, 2);
            Assert.That(rig.S.Vel.X, Is.EqualTo(-g * 10).Within(g * 10 * 0.01), "ten seconds' free fall at two Earth radii");
            Assert.That(rig.S.Pos.X, Is.LessThan(2 * EarthRadiusKm));
        }

        [Test]
        public void FlightAssistOnHoldsAHullAgainstGravityWhenItsThrustersCan()
        {
            var rig = new Rig(ShipClass.Fighter);
            var start = new DVec3(0, 0, 2 * EarthRadiusKm);
            rig.S.Pos = start;
            for (int i = 0; i < 600; i++)
                rig.Fly(new PilotInput { DesiredForward = DVec3.UnitX }, 0.1, 1, PilotFlight.Gravity(rig.S.Pos, EarthGm, EarthRadiusKm));
            Assert.That((rig.S.Pos - start).Length, Is.LessThan(1e-9), "assist should hover a hull whose thrusters out-pull gravity");
        }

        [Test]
        public void FlightAssistOnAHullTooWeakForGravitySinksAndComesToRestOnTheSurface()
        {
            var rig = new Rig(ShipClass.Carrier);
            double floor = EarthRadiusKm + 1;
            rig.S.Pos = new DVec3(0, 0, EarthRadiusKm + 50);
            bool landed = false;
            for (int i = 0; i < 20000 && !landed; i++)
            {
                rig.Fly(new PilotInput { DesiredForward = DVec3.UnitX }, 0.1, 1, PilotFlight.Gravity(rig.S.Pos, EarthGm, EarthRadiusKm));
                landed = PilotFlight.KeepAboveSurface(ref rig.S, DVec3.Zero, floor);
            }
            Assert.That(rig.Hull.VerticalAccel, Is.LessThan(EarthGm / (EarthRadiusKm * EarthRadiusKm)), "the premise: a carrier's thrusters cannot hold it up at the surface");
            Assert.That(landed, Is.True, "a carrier near the surface should sink onto it");
            Assert.That(rig.S.Pos.Length, Is.EqualTo(floor).Within(1e-9));
            Assert.That(DVec3.Dot(rig.S.Vel, rig.S.Pos.Normalized), Is.GreaterThanOrEqualTo(0), "resting on the surface, not falling through it");
        }

        // ------------------------------------------------------------ assisted flight feel (kept from before)

        [Test]
        public void ThrottleAcceleratesTowardMaxSpeedButNeverPastTheMainDrivesPush()
        {
            var rig = new Rig(ShipClass.Cruiser);
            var input = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1 };

            rig.Fly(input, 1.0);
            Assert.That(rig.S.Vel.Length <= rig.Hull.MainAccel * 1.0 + 1e-9, Is.True, "velocity exceeded one second's worth of the main drive");
            Assert.That(rig.S.Vel.Length > 0, Is.True, "throttle produced no thrust at all");

            rig.Fly(input, 1.0, 100000);
            Assert.That(rig.S.Vel.Length, Is.InRange(rig.Tuning.MaxSpeed * 0.999, rig.Tuning.MaxSpeed * 1.001));
        }

        [Test]
        public void ThrottlingDownBrakesOnTheRetroThrusters()
        {
            var rig = new Rig(ShipClass.Destroyer, DVec3.UnitX * PilotTuning.Of(ShipClass.Destroyer).MaxSpeed);
            rig.Fly(new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 0 }, 1.0);
            Assert.That(rig.S.Vel.Length, Is.EqualTo(rig.Tuning.MaxSpeed - rig.Hull.RetroAccel).Within(1e-9));
        }

        [Test]
        public void BoostIsMoreThrustThatCarriesTheHullPastItsCruisingSpeedUntilItDepletesThenRecharges()
        {
            var rig = new Rig(ShipClass.Destroyer);
            var input = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1, Boost = true };

            rig.Fly(input, 1.0);
            Assert.That(rig.S.Vel.Length, Is.EqualTo(rig.Hull.MainAccel * rig.Tuning.BoostSpeedMultiplier).Within(1e-12), "boost should be extra thrust from the first second");

            // Well into the boost window (but before it runs out), speed has climbed past the un-boosted set point.
            int early = (int)(rig.Tuning.BoostDurationS * 0.9) - 1;
            rig.Fly(input, 1.0, early);
            Assert.That(rig.S.Vel.Length > rig.Tuning.MaxSpeed, Is.True, "boosting never exceeded the normal speed");
            Assert.That(rig.Combat.BoostFraction < 1 && rig.Combat.BoostFraction > 0, Is.True, "boost charge did not drain while held");

            // Held long enough, boost charge runs out entirely and assist brings the hull back down.
            rig.Fly(input, 1.0, 100000);
            Assert.That(rig.Combat.BoostFraction, Is.EqualTo(0).Within(1e-3));
            Assert.That(rig.S.Vel.Length, Is.InRange(rig.Tuning.MaxSpeed * 0.999, rig.Tuning.MaxSpeed * 1.001));

            // Letting go recharges it.
            rig.Fly(new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1 }, 1.0, 100000);
            Assert.That(rig.Combat.BoostFraction, Is.EqualTo(1).Within(1e-3));
        }

        [Test]
        public void ForwardTurnsTowardTheDesiredHeadingButNeverFasterThanTheHullsTurnRate()
        {
            var rig = new Rig(ShipClass.Battleship);
            rig.Fly(new PilotInput { DesiredForward = DVec3.UnitY }, 1.0);

            double turnedDeg = AngleDeg(DVec3.UnitX, rig.S.Forward);
            Assert.That(turnedDeg <= rig.Tuning.TurnRateDegS * 1.0 + 1e-6, Is.True, "turned faster than the hull's turn rate allows");
            Assert.That(turnedDeg > 0, Is.True, "forward never turned at all");
        }

        [Test]
        public void TurningSpinsUpAtTheHullsOwnAngularAccelerationAndSettlesOnTheHeading()
        {
            var rig = new Rig(ShipClass.Battleship);
            var input = new PilotInput { DesiredForward = DVec3.UnitY };
            const double dt = 0.1;

            rig.Fly(input, dt);
            double alphaDeg = rig.Hull.PitchYawAngularAccel * Astro.Rad2Deg;
            Assert.That(rig.S.TurnRateDegS, Is.EqualTo(alphaDeg * dt).Within(1e-9), "a heavy hull spins up at torque over inertia, not straight to its full rate");
            Assert.That(rig.S.TurnRateDegS < rig.Tuning.TurnRateDegS, Is.True);

            // Held well past the spin-up but nowhere near alignment yet, the turn rate saturates at the hull's max.
            rig.Fly(input, dt, 19);
            Assert.That(rig.S.TurnRateDegS, Is.EqualTo(rig.Tuning.TurnRateDegS).Within(1e-3));

            // And it brakes onto the heading rather than swinging past it.
            double worst = 0;
            for (int i = 0; i < 2000; i++)
            {
                rig.Fly(input, dt);
                worst = Math.Max(worst, DVec3.Dot(rig.S.Forward, -DVec3.UnitX));
            }
            Assert.That(AngleDeg(rig.S.Forward, DVec3.UnitY), Is.LessThan(1e-6));
            Assert.That(worst, Is.LessThan(1e-6), "the nose overshot past the heading");
            Assert.That(rig.S.TurnRateDegS, Is.EqualTo(0).Within(1e-9));
        }

        [Test]
        public void TheAimIsPulledBackToWithinItsLeadOfTheNose()
        {
            var lead = 10 * Astro.Deg2Rad;
            var near = PilotFlight.RotateAbout(DVec3.UnitX, DVec3.UnitZ, 5 * Astro.Deg2Rad);
            Assert.That(PilotFlight.ClampAim(near, DVec3.UnitX, lead).Equals(near), Is.True, "an aim inside the lead was moved");

            var far = PilotFlight.ClampAim(DVec3.UnitY, DVec3.UnitX, lead);
            Assert.That(AngleDeg(far, DVec3.UnitX), Is.EqualTo(10).Within(1e-9));
            Assert.That(far.Y > 0 && Math.Abs(far.Z) < 1e-12, Is.True, "clamped off the great circle toward the aim");

            var astern = PilotFlight.ClampAim(-DVec3.UnitX, DVec3.UnitX, lead);
            Assert.That(AngleDeg(astern, DVec3.UnitX), Is.EqualTo(10).Within(1e-9));
        }

        [Test]
        public void EveryHullsAimLeadsFarEnoughToReachItsFullTurnRate()
        {
            foreach (ShipClass c in Enum.GetValues(typeof(ShipClass)))
            {
                var hull = Hull(c);
                if (hull.PitchYawAngularAccel <= 0) continue;
                var tuning = PilotTuning.Of(c);
                double rate = tuning.TurnRateDegS * Astro.Deg2Rad;
                double lead = PilotFlight.AimLeadRad(hull, tuning);
                Assert.That(Math.Sqrt(2 * hull.PitchYawAngularAccel * lead), Is.GreaterThanOrEqualTo(rate * (1 - 1e-9)), c + ": the lead caps the turn below the hull's rate");
                Assert.That(lead, Is.GreaterThanOrEqualTo(PilotFlight.MinAimLeadDeg * Astro.Deg2Rad));
            }
        }

        [Test]
        public void LettingGoOfTheStickAfterAFlickStopsTheNoseWithinItsBrakingAngle()
        {
            foreach (var c in new[] { ShipClass.Fighter, ShipClass.Freighter, ShipClass.Cruiser, ShipClass.Battleship })
            {
                var rig = new Rig(c);
                double lead = PilotFlight.AimLeadRad(rig.Hull, rig.Tuning);
                const double dt = 1 / 60.0;
                // The stick flung half way round, then left alone: the aim is held to its lead each frame, as the helm does.
                var aim = PilotFlight.ClampAim(-DVec3.UnitY, rig.S.Forward, lead);
                rig.Fly(new PilotInput { DesiredForward = aim }, dt);
                var stoppedAt = aim;
                for (int i = 0; i < 60 * 120; i++)
                    rig.Fly(new PilotInput { DesiredForward = aim }, dt);

                Assert.That(AngleDeg(rig.S.Forward, stoppedAt), Is.LessThan(1e-6), c + ": the nose did not settle on the clamped aim");
                Assert.That(AngleDeg(rig.S.Forward, DVec3.UnitX), Is.LessThanOrEqualTo(lead * Astro.Rad2Deg + 1e-6), c + ": the nose swung on past where the aim was let go");
            }
        }

        [Test]
        public void StrafeMovesSidewaysWithoutRequiringAnyTurn()
        {
            var rig = new Rig(ShipClass.Cruiser);
            rig.Fly(new PilotInput { DesiredForward = DVec3.UnitX, StrafeRight = 1 }, 1.0, 1000);

            Assert.That(rig.S.Forward.Equals(DVec3.UnitX), Is.True, "strafing should not have turned the hull");
            Assert.That(rig.S.Pos.LengthSquared > 0, Is.True, "strafe input produced no lateral motion");
            Assert.That(rig.S.Vel.Length, Is.InRange(rig.Tuning.MaxSpeed * PilotFlight.StrafeSpeedFraction * 0.99, rig.Tuning.MaxSpeed * PilotFlight.StrafeSpeedFraction * 1.01));
        }

        [Test]
        public void CruiseRampsUpToFarBeyondMaxSpeedWhenHeldStraightAtFullThrottle()
        {
            var rig = new Rig(ShipClass.Courier);
            var input = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1, Cruise = true };

            // Early in the ramp: already engaged, but nowhere near the cruise speed yet.
            rig.Fly(input, 0.1);
            Assert.That(rig.S.Cruise.Active, Is.True, "holding cruise straight at full throttle should engage the ramp");
            Assert.That(rig.S.Vel.Length < rig.Tuning.MaxSpeed * rig.Tuning.CruiseSpeedMultiplier * 0.5, Is.True, "cruise should not jump straight to full speed");

            rig.Fly(input, 1.0, 100000);
            Assert.That(rig.S.Vel.Length, Is.InRange(rig.Tuning.MaxSpeed * rig.Tuning.CruiseSpeedMultiplier * 0.999, rig.Tuning.MaxSpeed * rig.Tuning.CruiseSpeedMultiplier * 1.001));
            Assert.That(rig.Tuning.CruiseSpeedMultiplier > rig.Tuning.BoostSpeedMultiplier, Is.True, "cruise should out-pace tactical boost, not just match it");
        }

        [Test]
        public void CruiseIsMainDriveThrustThatKeepsBuildingSpeedWithAssistOff()
        {
            var rig = new Rig(ShipClass.Courier);
            var input = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1, Cruise = true, FlightAssistOff = true };
            rig.Fly(input, 0.1, 1000);
            Assert.That(rig.S.Cruise.Active, Is.True);
            Assert.That(rig.S.Vel.Length, Is.GreaterThan(rig.Tuning.MaxSpeed * rig.Tuning.CruiseSpeedMultiplier * 2), "with no assist set point, cruise thrust keeps accelerating the hull");
        }

        [Test]
        public void CruiseDropsOutInstantlyTheMomentTheHullTakesDamage()
        {
            var rig = new Rig(ShipClass.Courier);
            var input = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1, Cruise = true };
            rig.Fly(input, 1.0, 100000);
            Assert.That(rig.S.Cruise.Active, Is.True, "should have reached full cruise before taking damage");

            rig.Combat.Hull -= 1;
            rig.Fly(input, 1.0);
            Assert.That(rig.S.Cruise.Active, Is.False, "a weapon hit should drop cruise instantly even though the key is still held");
        }

        [Test]
        public void CruiseDropsOutInstantlyWhenTheHullEntersCombat()
        {
            var rig = new Rig(ShipClass.Courier);
            var input = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1, Cruise = true };
            rig.Fly(input, 1.0, 100000);
            Assert.That(rig.S.Cruise.Active, Is.True);

            input.InCombat = true;
            rig.Fly(input, 1.0);
            Assert.That(rig.S.Cruise.Active, Is.False, "entering an active battle should drop cruise instantly");
        }

        [Test]
        public void CruiseDropsOutInstantlyWhenThrottlingDownOrTurningHard()
        {
            var rig = new Rig(ShipClass.Courier);
            var input = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1, Cruise = true };
            rig.Fly(input, 1.0, 100000);
            Assert.That(rig.S.Cruise.Active, Is.True);

            input.Throttle = 0.2;
            rig.Fly(input, 1.0);
            Assert.That(rig.S.Cruise.Active, Is.False, "throttling down should drop cruise instantly");

            // Separately: holding full throttle but aiming sharply away from the current heading.
            var turning = new Rig(ShipClass.Courier);
            turning.Fly(new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1, Cruise = true }, 1.0, 100000);
            Assert.That(turning.S.Cruise.Active, Is.True);

            turning.Fly(new PilotInput { DesiredForward = DVec3.UnitY, Throttle = 1, Cruise = true }, 1.0);
            Assert.That(turning.S.Cruise.Active, Is.False, "turning hard away from the current heading should drop cruise instantly");
        }

        [Test]
        public void CruiseNeverEngagesWhileBoosting()
        {
            var rig = new Rig(ShipClass.Courier);
            rig.Fly(new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1, Cruise = true, Boost = true }, 1.0, 10);
            Assert.That(rig.S.Cruise.Active, Is.False, "boosting and cruising are different drive tiers and should not combine");
        }
    }
}
