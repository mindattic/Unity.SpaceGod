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

        /// <summary>Holding the cruise key: see <see cref="PilotCruiseState"/>.</summary>
        public bool Cruise;

        /// <summary>True whenever the hull is in an active battle (set by the Runtime layer from
        /// <c>ship.Battle</c>/<c>BattleUnit.PlayerControlled</c>) -- cruise is a pure open-space travel
        /// tier and must drop the instant combat is live, win or lose.</summary>
        public bool InCombat;
    }

    /// <summary>
    /// Ramp state for the cruise drive (see <see cref="PilotFlight.Step"/>): a second, much faster
    /// speed tier for open-space travel, distinct from tactical boost. Persists across frames like
    /// <see cref="PilotInput"/>'s caller-owned position/velocity/forward do, so it lives in its own
    /// small struct the Runtime layer keeps alongside them.
    /// </summary>
    public struct PilotCruiseState
    {
        /// <summary>0 (tactical flight) .. 1 (full cruise speed) -- eases up over
        /// <see cref="PilotTuning.CruiseRampS"/> while engaged, but drops to 0 instantly the moment any
        /// disqualifying condition fires (damage, combat, throttling down, or turning hard), rather
        /// than easing back down -- cruise is meant to feel like it snaps off, not coast down.</summary>
        public double RampT;

        double lastHull;
        bool hasLastHull;

        /// <summary>Whether cruise is contributing any extra speed at all right now.</summary>
        public bool Active => RampT > 0;

        /// <summary>Tracks the hull's own health between frames purely to notice a hit; has nothing to
        /// do with repairs or destruction, which <see cref="ShipCombat"/> already owns.</summary>
        internal bool NoteHullAndCheckDamage(double hull)
        {
            bool tookDamage = hasLastHull && hull < lastHull - 1e-9;
            lastHull = hull;
            hasLastHull = true;
            return tookDamage;
        }
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

        /// <summary>Heading drift from the desired forward still read as "flying roughly straight";
        /// anything sharper is a deliberate turn and drops cruise.</summary>
        const double CruiseMaxHeadingDriftDeg = 5;

        /// <summary>Throttle below this reads as throttling down out of cruise, not holding it floored.</summary>
        const double CruiseMinThrottle = 0.9;

        public static void Step(ref DVec3 pos, ref DVec3 vel, ref DVec3 forward, ref double angularRateDegS, ref PilotCruiseState cruise, ShipCombat combat, in PilotTuning tuning, in PilotInput input, double dt)
        {
            var forwardBeforeTurn = forward;
            TurnToward(ref forward, input.DesiredForward, tuning.TurnRateDegS, tuning.AngularRampS, ref angularRateDegS, dt);

            // Holding boost only ever drains it (never recharges, even once empty); recharge only
            // resumes once released — otherwise holding it down at empty would flicker on and off.
            bool wantsBoost = input.Boost && tuning.BoostDurationS > 0;
            bool boosting = wantsBoost && combat.BoostFraction > 0;
            if (tuning.BoostDurationS > 0)
            {
                double rate = 1 / tuning.BoostDurationS;
                combat.BoostFraction = wantsBoost
                    ? Math.Max(0, combat.BoostFraction - rate * dt)
                    : Math.Min(1, combat.BoostFraction + rate * RechargeFraction * dt);
            }
            double boostMult = boosting ? tuning.BoostSpeedMultiplier : 1;

            double cruiseMult = StepCruise(ref cruise, input, forwardBeforeTurn, combat, tuning, dt);
            double maxSpeed = tuning.MaxSpeed * boostMult * cruiseMult;

            var up = Math.Abs(DVec3.Dot(forward, DVec3.UnitZ)) > 0.98 ? DVec3.UnitX : DVec3.UnitZ;
            var right = DVec3.Cross(forward, up).Normalized;
            var trueUp = DVec3.Cross(right, forward).Normalized;

            var desired = forward * (input.Throttle * maxSpeed)
                + right * (input.StrafeRight * maxSpeed * StrafeSpeedFraction)
                + trueUp * (input.StrafeUp * maxSpeed * StrafeSpeedFraction);

            var dv = desired - vel;
            // Boosting (or cruising) is a stronger burn too (more thrust, not just a higher redline),
            // so the extra speed is actually reachable within the boost's/cruise's own ramp.
            double accel = (desired.LengthSquared >= vel.LengthSquared ? tuning.Accel : tuning.Decel) * boostMult * cruiseMult;
            double step = accel * dt;
            vel += dv.Length > step ? dv.Normalized * step : dv;
            pos += vel * dt;
        }

        /// <summary>
        /// Advances the cruise drive's ramp and returns this frame's speed multiplier (1 when not
        /// cruising). Engaging requires holding <see cref="PilotInput.Cruise"/> at near-full throttle
        /// while flying close to straight, with no boost and no active battle; once engaged it eases
        /// from 1x to <see cref="PilotTuning.CruiseSpeedMultiplier"/> over
        /// <see cref="PilotTuning.CruiseRampS"/> seconds. Any disqualifying condition -- including this
        /// frame's hit, checked here rather than left to the caller since <see cref="ShipCombat.Hull"/>
        /// is already in hand -- snaps the ramp back to zero immediately rather than easing it down.
        /// </summary>
        static double StepCruise(ref PilotCruiseState cruise, in PilotInput input, DVec3 forwardBeforeTurn, ShipCombat combat, in PilotTuning tuning, double dt)
        {
            bool tookDamage = cruise.NoteHullAndCheckDamage(combat.Hull);
            if (tuning.CruiseSpeedMultiplier <= 1 || tuning.CruiseRampS <= 0)
            {
                cruise.RampT = 0;
                return 1;
            }

            bool headingStraight = input.DesiredForward.LengthSquared < 1e-12
                || DVec3.Dot(forwardBeforeTurn, input.DesiredForward.Normalized) > Math.Cos(CruiseMaxHeadingDriftDeg * Astro.Deg2Rad);
            bool eligible = input.Cruise && !input.Boost && !input.InCombat && !tookDamage
                && input.Throttle >= CruiseMinThrottle && headingStraight;

            if (!eligible)
            {
                cruise.RampT = 0;
                return 1;
            }
            cruise.RampT = Math.Min(1, cruise.RampT + dt / tuning.CruiseRampS);
            return 1 + (tuning.CruiseSpeedMultiplier - 1) * cruise.RampT;
        }

        /// <summary>Turns toward <paramref name="dir"/> with angular inertia: <paramref name="angularRateDegS"/>
        /// ramps toward <paramref name="degPerSec"/> over <paramref name="rampS"/> seconds (the rotational
        /// equivalent of the hull's linear accel cap) rather than snapping straight to the max rate, and
        /// ramps back down as the hull nears alignment so it eases in rather than oscillating around the
        /// target. A non-positive <paramref name="rampS"/> turns at the full rate at once.</summary>
        static void TurnToward(ref DVec3 forward, DVec3 dir, double degPerSec, double rampS, ref double angularRateDegS, double dt)
        {
            if (rampS <= 0) { angularRateDegS = degPerSec; }

            if (dir.LengthSquared < 1e-12)
            {
                if (rampS > 0) angularRateDegS = Math.Max(0, angularRateDegS - degPerSec / rampS * dt);
                return;
            }
            var want = dir.Normalized;
            double cos = Math.Max(-1, Math.Min(1, DVec3.Dot(forward, want)));
            double angleDeg = Math.Acos(cos) * Astro.Rad2Deg;

            if (rampS > 0)
            {
                double angularAccel = degPerSec / rampS;
                // Brake early enough to settle exactly on the target instead of overshooting and
                // oscillating back -- same idea as the linear dv cap just below, applied to rotation.
                double brakingAngleDeg = angularRateDegS * angularRateDegS / (2 * angularAccel);
                angularRateDegS = brakingAngleDeg >= angleDeg
                    ? Math.Max(0, angularRateDegS - angularAccel * dt)
                    : Math.Min(degPerSec, angularRateDegS + angularAccel * dt);
            }

            double maxDeg = angularRateDegS * dt;
            if (angleDeg <= maxDeg || angleDeg < 1e-6)
            {
                forward = want;
                return;
            }
            forward = DVec3.Lerp(forward, want, maxDeg / angleDeg).Normalized;
        }
    }
}
