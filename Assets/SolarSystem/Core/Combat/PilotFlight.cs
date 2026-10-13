using System;

namespace SolarSystem.Core
{
    /// <summary>
    /// One frame of player flight input, already translated into pure flight terms by the Runtime
    /// input layer, so the integration math below stays engine-free and unit-testable.
    /// </summary>
    public struct PilotInput
    {
        /// <summary>The direction the player wants the nose turned toward (unit vector, ecliptic axes).</summary>
        public DVec3 DesiredForward;

        /// <summary>Forward/reverse thrust, -1..1.</summary>
        public double Throttle;

        /// <summary>Lateral and vertical strafe thrust in the hull's own frame, each -1..1.</summary>
        public double StrafeRight, StrafeUp;

        public bool Boost;
    }

    /// <summary>
    /// Flies a player-piloted hull: turns toward the player's desired heading at the hull's own turn
    /// rate, and accelerates toward a thrust-commanded velocity at the hull's own acceleration cap —
    /// the same numeric shape as the AI's <c>Battle.Move</c> and <c>FighterAI.Steer</c>, just driven
    /// by a human instead of a goal point, so a hull handles the same whoever is flying it.
    /// </summary>
    public static class PilotFlight
    {
        /// <summary>Strafe thrusters are much weaker than the main drive.</summary>
        public const double StrafeSpeedFraction = 0.35;

        /// <summary>Boost drains twice as fast as it recharges.</summary>
        const double RechargeFraction = 0.5;

        public static void Step(ref DVec3 pos, ref DVec3 vel, ref DVec3 forward, ShipCombat combat, in PilotInput input, double dt)
        {
            var spec = combat.Spec;
            TurnToward(ref forward, input.DesiredForward, spec.TurnRateDegS, dt);

            // Holding boost only ever drains it (never recharges, even once empty); recharge only
            // resumes once released — otherwise holding it down at empty would flicker on and off.
            bool wantsBoost = input.Boost && spec.BoostDurationS > 0;
            bool boosting = wantsBoost && combat.BoostFraction > 0;
            if (spec.BoostDurationS > 0)
            {
                double rate = 1 / spec.BoostDurationS;
                combat.BoostFraction = wantsBoost
                    ? Math.Max(0, combat.BoostFraction - rate * dt)
                    : Math.Min(1, combat.BoostFraction + rate * RechargeFraction * dt);
            }
            double boostMult = boosting ? spec.BoostSpeedMultiplier : 1;
            double maxSpeed = spec.MaxSpeed * boostMult;

            var up = Math.Abs(DVec3.Dot(forward, DVec3.UnitZ)) > 0.98 ? DVec3.UnitX : DVec3.UnitZ;
            var right = DVec3.Cross(forward, up).Normalized;
            var trueUp = DVec3.Cross(right, forward).Normalized;

            var desired = forward * (input.Throttle * maxSpeed)
                + right * (input.StrafeRight * maxSpeed * StrafeSpeedFraction)
                + trueUp * (input.StrafeUp * maxSpeed * StrafeSpeedFraction);

            var dv = desired - vel;
            // Boosting is a stronger burn too (more thrust, not just a higher redline), so the extra
            // speed is actually reachable within the boost's own duration.
            double accel = (desired.LengthSquared >= vel.LengthSquared ? spec.Accel : spec.Decel) * boostMult;
            double step = accel * dt;
            vel += dv.Length > step ? dv.Normalized * step : dv;
            pos += vel * dt;
        }

        static void TurnToward(ref DVec3 forward, DVec3 dir, double degPerSec, double dt)
        {
            if (dir.LengthSquared < 1e-12) return;
            var want = dir.Normalized;
            double cos = Math.Max(-1, Math.Min(1, DVec3.Dot(forward, want)));
            double angle = Math.Acos(cos);
            double max = degPerSec * Astro.Deg2Rad * dt;
            if (angle <= max || angle < 1e-6)
            {
                forward = want;
                return;
            }
            forward = DVec3.Lerp(forward, want, max / angle).Normalized;
        }
    }
}
