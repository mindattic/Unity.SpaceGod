using SolarSystem.Core;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    [TestFixture]
    public class PilotFlightTests
    {
        static ShipCombat Combat(ShipClass c) => new ShipCombat(CombatSpec.Of(c), c);

        [Test]
        public void ThrottleAcceleratesTowardMaxSpeedButNeverPastTheHullsAccelCap()
        {
            var combat = Combat(ShipClass.Cruiser);
            var tuning = PilotTuning.Of(ShipClass.Cruiser);
            var pos = DVec3.Zero;
            var vel = DVec3.Zero;
            var forward = DVec3.UnitX;
            double angularRate = 0;
            var cruise = new PilotCruiseState();
            var input = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1 };

            PilotFlight.Step(ref pos, ref vel, ref forward, ref angularRate, ref cruise, combat, tuning, input, 1.0);

            Assert.That(vel.Length <= tuning.Accel * 1.0 + 1e-9, Is.True, "velocity exceeded one second's worth of acceleration");
            Assert.That(vel.Length > 0, Is.True, "throttle produced no thrust at all");

            for (int i = 0; i < 100000; i++)
                PilotFlight.Step(ref pos, ref vel, ref forward, ref angularRate, ref cruise, combat, tuning, input, 1.0);
            Assert.That(vel.Length, Is.InRange(tuning.MaxSpeed * 0.999, tuning.MaxSpeed * 1.001));
        }

        [Test]
        public void ThrottleDecelerateUsesTheHullsOwnDecelRate()
        {
            var combat = Combat(ShipClass.Destroyer);
            var tuning = PilotTuning.Of(ShipClass.Destroyer);
            var pos = DVec3.Zero;
            var vel = DVec3.UnitX * tuning.MaxSpeed;
            var forward = DVec3.UnitX;
            double angularRate = 0;
            var cruise = new PilotCruiseState();
            var input = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 0 };

            PilotFlight.Step(ref pos, ref vel, ref forward, ref angularRate, ref cruise, combat, tuning, input, 1.0);

            Assert.That(vel.Length, Is.InRange(tuning.MaxSpeed - tuning.Decel * 1.0 - 1e-9, tuning.MaxSpeed - tuning.Decel * 1.0 + 1e-9));
        }

        [Test]
        public void BoostRaisesTheSpeedCeilingUntilItDepletesThenRecharges()
        {
            var combat = Combat(ShipClass.Destroyer);
            var tuning = PilotTuning.Of(ShipClass.Destroyer);
            var pos = DVec3.Zero;
            var vel = DVec3.Zero;
            var forward = DVec3.UnitX;
            double angularRate = 0;
            var cruise = new PilotCruiseState();
            var input = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1, Boost = true };

            // Well into the boost window (but before it runs out), speed has climbed past the un-boosted ceiling.
            int early = (int)(tuning.BoostDurationS * 0.9);
            for (int i = 0; i < early; i++)
                PilotFlight.Step(ref pos, ref vel, ref forward, ref angularRate, ref cruise, combat, tuning, input, 1.0);
            Assert.That(vel.Length > tuning.MaxSpeed, Is.True, "boosting never exceeded the normal speed ceiling");
            Assert.That(combat.BoostFraction < 1 && combat.BoostFraction > 0, Is.True, "boost charge did not drain while held");

            // Held long enough, boost charge runs out entirely and the ceiling reverts to normal.
            for (int i = 0; i < 100000; i++)
                PilotFlight.Step(ref pos, ref vel, ref forward, ref angularRate, ref cruise, combat, tuning, input, 1.0);
            Assert.That(combat.BoostFraction, Is.EqualTo(0).Within(1e-3));
            Assert.That(vel.Length, Is.InRange(tuning.MaxSpeed * 0.999, tuning.MaxSpeed * 1.001));

            // Letting go recharges it.
            var noBoost = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1, Boost = false };
            for (int i = 0; i < 100000; i++)
                PilotFlight.Step(ref pos, ref vel, ref forward, ref angularRate, ref cruise, combat, tuning, noBoost, 1.0);
            Assert.That(combat.BoostFraction, Is.EqualTo(1).Within(1e-3));
        }

        [Test]
        public void ForwardTurnsTowardTheDesiredHeadingButNeverFasterThanTheHullsTurnRate()
        {
            var combat = Combat(ShipClass.Battleship);
            var tuning = PilotTuning.Of(ShipClass.Battleship);
            var pos = DVec3.Zero;
            var vel = DVec3.Zero;
            var forward = DVec3.UnitX;
            double angularRate = 0;
            var cruise = new PilotCruiseState();
            var input = new PilotInput { DesiredForward = DVec3.UnitY };

            PilotFlight.Step(ref pos, ref vel, ref forward, ref angularRate, ref cruise, combat, tuning, input, 1.0);

            double turnedDeg = System.Math.Acos(System.Math.Max(-1, System.Math.Min(1, DVec3.Dot(DVec3.UnitX, forward)))) * Astro.Rad2Deg;
            Assert.That(turnedDeg <= tuning.TurnRateDegS * 1.0 + 1e-6, Is.True, "turned faster than the hull's turn rate allows");
            Assert.That(turnedDeg > 0, Is.True, "forward never turned at all");
        }

        [Test]
        public void TurningRampsUpAngularRateInsteadOfSnappingToFullRateImmediately()
        {
            var combat = Combat(ShipClass.Battleship);
            var tuning = PilotTuning.Of(ShipClass.Battleship);
            var pos = DVec3.Zero;
            var vel = DVec3.Zero;
            var forward = DVec3.UnitX;
            double angularRate = 0;
            var cruise = new PilotCruiseState();
            var input = new PilotInput { DesiredForward = DVec3.UnitY };
            const double dt = 0.1;

            PilotFlight.Step(ref pos, ref vel, ref forward, ref angularRate, ref cruise, combat, tuning, input, dt);

            double turnedDeg = System.Math.Acos(System.Math.Max(-1, System.Math.Min(1, DVec3.Dot(DVec3.UnitX, forward)))) * Astro.Rad2Deg;
            Assert.That(turnedDeg < tuning.TurnRateDegS * dt - 1e-9, Is.True, "a hull with angular ramp-up should turn less than its steady-state rate on the very first frame");
            Assert.That(angularRate > 0 && angularRate < tuning.TurnRateDegS, Is.True, "angular rate should be mid-ramp, neither zero nor already at the hull's max");

            // Held well past the ramp time but nowhere near alignment yet (so it hasn't started
            // braking into the target), the angular rate saturates at the hull's own max.
            for (int i = 0; i < 19; i++)
                PilotFlight.Step(ref pos, ref vel, ref forward, ref angularRate, ref cruise, combat, tuning, input, dt);
            Assert.That(angularRate, Is.EqualTo(tuning.TurnRateDegS).Within(1e-3));
        }

        [Test]
        public void StrafeMovesSidewaysWithoutRequiringAnyTurn()
        {
            var combat = Combat(ShipClass.Cruiser);
            var tuning = PilotTuning.Of(ShipClass.Cruiser);
            var pos = DVec3.Zero;
            var vel = DVec3.Zero;
            var forward = DVec3.UnitX;
            double angularRate = 0;
            var cruise = new PilotCruiseState();
            var input = new PilotInput { DesiredForward = DVec3.UnitX, StrafeRight = 1 };
            for (int i = 0; i < 1000; i++)
                PilotFlight.Step(ref pos, ref vel, ref forward, ref angularRate, ref cruise, combat, tuning, input, 1.0);

            Assert.That(forward.Equals(DVec3.UnitX), Is.True, "strafing should not have turned the hull");
            Assert.That(pos.LengthSquared > 0, Is.True, "strafe input produced no lateral motion");
            Assert.That(vel.Length, Is.InRange(tuning.MaxSpeed * PilotFlight.StrafeSpeedFraction * 0.99, tuning.MaxSpeed * PilotFlight.StrafeSpeedFraction * 1.01));
        }

        [Test]
        public void CruiseRampsUpToFarBeyondMaxSpeedWhenHeldStraightAtFullThrottle()
        {
            var combat = Combat(ShipClass.Courier);
            var tuning = PilotTuning.Of(ShipClass.Courier);
            var pos = DVec3.Zero;
            var vel = DVec3.Zero;
            var forward = DVec3.UnitX;
            double angularRate = 0;
            var cruise = new PilotCruiseState();
            var input = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1, Cruise = true };

            // Early in the ramp: already engaged, but nowhere near the cruise ceiling yet.
            PilotFlight.Step(ref pos, ref vel, ref forward, ref angularRate, ref cruise, combat, tuning, input, 0.1);
            Assert.That(cruise.Active, Is.True, "holding cruise straight at full throttle should engage the ramp");
            Assert.That(vel.Length < tuning.MaxSpeed * tuning.CruiseSpeedMultiplier * 0.5, Is.True, "cruise should not jump straight to full speed");

            for (int i = 0; i < 100000; i++)
                PilotFlight.Step(ref pos, ref vel, ref forward, ref angularRate, ref cruise, combat, tuning, input, 1.0);
            Assert.That(vel.Length, Is.InRange(tuning.MaxSpeed * tuning.CruiseSpeedMultiplier * 0.999, tuning.MaxSpeed * tuning.CruiseSpeedMultiplier * 1.001));
            Assert.That(tuning.MaxSpeed * tuning.CruiseSpeedMultiplier > tuning.MaxSpeed * tuning.BoostSpeedMultiplier, Is.True, "cruise should out-pace tactical boost, not just match it");
        }

        [Test]
        public void CruiseDropsOutInstantlyTheMomentTheHullTakesDamage()
        {
            var combat = Combat(ShipClass.Courier);
            var tuning = PilotTuning.Of(ShipClass.Courier);
            var pos = DVec3.Zero;
            var vel = DVec3.Zero;
            var forward = DVec3.UnitX;
            double angularRate = 0;
            var cruise = new PilotCruiseState();
            var input = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1, Cruise = true };

            for (int i = 0; i < 100000; i++)
                PilotFlight.Step(ref pos, ref vel, ref forward, ref angularRate, ref cruise, combat, tuning, input, 1.0);
            Assert.That(cruise.Active, Is.True, "should have reached full cruise before taking damage");

            combat.Hull -= 1;
            PilotFlight.Step(ref pos, ref vel, ref forward, ref angularRate, ref cruise, combat, tuning, input, 1.0);
            Assert.That(cruise.Active, Is.False, "a weapon hit should drop cruise instantly even though the key is still held");
        }

        [Test]
        public void CruiseDropsOutInstantlyWhenTheHullEntersCombat()
        {
            var combat = Combat(ShipClass.Courier);
            var tuning = PilotTuning.Of(ShipClass.Courier);
            var pos = DVec3.Zero;
            var vel = DVec3.Zero;
            var forward = DVec3.UnitX;
            double angularRate = 0;
            var cruise = new PilotCruiseState();
            var input = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1, Cruise = true };

            for (int i = 0; i < 100000; i++)
                PilotFlight.Step(ref pos, ref vel, ref forward, ref angularRate, ref cruise, combat, tuning, input, 1.0);
            Assert.That(cruise.Active, Is.True);

            input.InCombat = true;
            PilotFlight.Step(ref pos, ref vel, ref forward, ref angularRate, ref cruise, combat, tuning, input, 1.0);
            Assert.That(cruise.Active, Is.False, "entering an active battle should drop cruise instantly");
        }

        [Test]
        public void CruiseDropsOutInstantlyWhenThrottlingDownOrTurningHard()
        {
            var tuningLow = PilotTuning.Of(ShipClass.Courier);
            var combatThrottle = Combat(ShipClass.Courier);
            var pos = DVec3.Zero;
            var vel = DVec3.Zero;
            var forward = DVec3.UnitX;
            double angularRate = 0;
            var cruise = new PilotCruiseState();
            var input = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1, Cruise = true };
            for (int i = 0; i < 100000; i++)
                PilotFlight.Step(ref pos, ref vel, ref forward, ref angularRate, ref cruise, combatThrottle, tuningLow, input, 1.0);
            Assert.That(cruise.Active, Is.True);

            input.Throttle = 0.2;
            PilotFlight.Step(ref pos, ref vel, ref forward, ref angularRate, ref cruise, combatThrottle, tuningLow, input, 1.0);
            Assert.That(cruise.Active, Is.False, "throttling down should drop cruise instantly");

            // Separately: holding full throttle but aiming sharply away from the current heading.
            var combatTurn = Combat(ShipClass.Courier);
            var pos2 = DVec3.Zero;
            var vel2 = DVec3.Zero;
            var forward2 = DVec3.UnitX;
            double angularRate2 = 0;
            var cruise2 = new PilotCruiseState();
            var straight = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1, Cruise = true };
            for (int i = 0; i < 100000; i++)
                PilotFlight.Step(ref pos2, ref vel2, ref forward2, ref angularRate2, ref cruise2, combatTurn, tuningLow, straight, 1.0);
            Assert.That(cruise2.Active, Is.True);

            var hardTurn = new PilotInput { DesiredForward = DVec3.UnitY, Throttle = 1, Cruise = true };
            PilotFlight.Step(ref pos2, ref vel2, ref forward2, ref angularRate2, ref cruise2, combatTurn, tuningLow, hardTurn, 1.0);
            Assert.That(cruise2.Active, Is.False, "turning hard away from the current heading should drop cruise instantly");
        }

        [Test]
        public void CruiseNeverEngagesWhileBoosting()
        {
            var combat = Combat(ShipClass.Courier);
            var tuning = PilotTuning.Of(ShipClass.Courier);
            var pos = DVec3.Zero;
            var vel = DVec3.Zero;
            var forward = DVec3.UnitX;
            double angularRate = 0;
            var cruise = new PilotCruiseState();
            var input = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1, Cruise = true, Boost = true };

            for (int i = 0; i < 10; i++)
                PilotFlight.Step(ref pos, ref vel, ref forward, ref angularRate, ref cruise, combat, tuning, input, 1.0);
            Assert.That(cruise.Active, Is.False, "boosting and cruising are different speed tiers and should not combine");
        }
    }
}
