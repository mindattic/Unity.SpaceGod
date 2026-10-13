using SolarSystem.Core;
using Xunit;

namespace SolarSystem.Core.Tests
{
    public class PilotFlightTests
    {
        static ShipCombat Combat(ShipClass c) => new ShipCombat(CombatSpec.Of(c), c);

        [Fact]
        public void ThrottleAcceleratesTowardMaxSpeedButNeverPastTheHullsAccelCap()
        {
            var combat = Combat(ShipClass.Cruiser);
            var spec = combat.Spec;
            var pos = DVec3.Zero;
            var vel = DVec3.Zero;
            var forward = DVec3.UnitX;
            var input = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1 };

            PilotFlight.Step(ref pos, ref vel, ref forward, combat, input, 1.0);

            Assert.True(vel.Length <= spec.Accel * 1.0 + 1e-9, "velocity exceeded one second's worth of acceleration");
            Assert.True(vel.Length > 0, "throttle produced no thrust at all");

            for (int i = 0; i < 100000; i++)
                PilotFlight.Step(ref pos, ref vel, ref forward, combat, input, 1.0);
            Assert.InRange(vel.Length, spec.MaxSpeed * 0.999, spec.MaxSpeed * 1.001);
        }

        [Fact]
        public void ThrottleDecelerateUsesTheHullsOwnDecelRate()
        {
            var combat = Combat(ShipClass.Destroyer);
            var spec = combat.Spec;
            var pos = DVec3.Zero;
            var vel = DVec3.UnitX * spec.MaxSpeed;
            var forward = DVec3.UnitX;
            var input = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 0 };

            PilotFlight.Step(ref pos, ref vel, ref forward, combat, input, 1.0);

            Assert.InRange(vel.Length, spec.MaxSpeed - spec.Decel * 1.0 - 1e-9, spec.MaxSpeed - spec.Decel * 1.0 + 1e-9);
        }

        [Fact]
        public void BoostRaisesTheSpeedCeilingUntilItDepletesThenRecharges()
        {
            var combat = Combat(ShipClass.Destroyer);
            var spec = combat.Spec;
            var pos = DVec3.Zero;
            var vel = DVec3.Zero;
            var forward = DVec3.UnitX;
            var input = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1, Boost = true };

            // Well into the boost window (but before it runs out), speed has climbed past the un-boosted ceiling.
            int early = (int)(spec.BoostDurationS * 0.9);
            for (int i = 0; i < early; i++)
                PilotFlight.Step(ref pos, ref vel, ref forward, combat, input, 1.0);
            Assert.True(vel.Length > spec.MaxSpeed, "boosting never exceeded the normal speed ceiling");
            Assert.True(combat.BoostFraction < 1 && combat.BoostFraction > 0, "boost charge did not drain while held");

            // Held long enough, boost charge runs out entirely and the ceiling reverts to normal.
            for (int i = 0; i < 100000; i++)
                PilotFlight.Step(ref pos, ref vel, ref forward, combat, input, 1.0);
            Assert.Equal(0, combat.BoostFraction, 3);
            Assert.InRange(vel.Length, spec.MaxSpeed * 0.999, spec.MaxSpeed * 1.001);

            // Letting go recharges it.
            var noBoost = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1, Boost = false };
            for (int i = 0; i < 100000; i++)
                PilotFlight.Step(ref pos, ref vel, ref forward, combat, noBoost, 1.0);
            Assert.Equal(1, combat.BoostFraction, 3);
        }

        [Fact]
        public void ForwardTurnsTowardTheDesiredHeadingButNeverFasterThanTheHullsTurnRate()
        {
            var combat = Combat(ShipClass.Battleship);
            var spec = combat.Spec;
            var pos = DVec3.Zero;
            var vel = DVec3.Zero;
            var forward = DVec3.UnitX;
            var input = new PilotInput { DesiredForward = DVec3.UnitY };

            PilotFlight.Step(ref pos, ref vel, ref forward, combat, input, 1.0);

            double turnedDeg = System.Math.Acos(System.Math.Max(-1, System.Math.Min(1, DVec3.Dot(DVec3.UnitX, forward)))) * Astro.Rad2Deg;
            Assert.True(turnedDeg <= spec.TurnRateDegS * 1.0 + 1e-6, "turned faster than the hull's turn rate allows");
            Assert.True(turnedDeg > 0, "forward never turned at all");
        }

        [Fact]
        public void StrafeMovesSidewaysWithoutRequiringAnyTurn()
        {
            var combat = Combat(ShipClass.Cruiser);
            var spec = combat.Spec;
            var pos = DVec3.Zero;
            var vel = DVec3.Zero;
            var forward = DVec3.UnitX;
            var input = new PilotInput { DesiredForward = DVec3.UnitX, StrafeRight = 1 };
            for (int i = 0; i < 1000; i++)
                PilotFlight.Step(ref pos, ref vel, ref forward, combat, input, 1.0);

            Assert.True(forward.Equals(DVec3.UnitX), "strafing should not have turned the hull");
            Assert.True(pos.LengthSquared > 0, "strafe input produced no lateral motion");
            Assert.InRange(vel.Length, spec.MaxSpeed * PilotFlight.StrafeSpeedFraction * 0.99, spec.MaxSpeed * PilotFlight.StrafeSpeedFraction * 1.01);
        }
    }
}
