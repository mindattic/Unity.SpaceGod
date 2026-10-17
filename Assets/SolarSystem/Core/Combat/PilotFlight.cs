using System;

namespace SolarSystem.Core
{
    /// <summary>
    /// One frame of player flight input, already translated into pure flight terms by the Runtime
    /// input layer, so the integration math below stays engine-free and unit-testable.
    /// </summary>
    public struct PilotInput
    {
        /// <summary>The direction the player wants the nose turned toward (unit vector, ecliptic axes).
        /// Zero means no stick input: flight assist then stops the nose swinging, while with assist off
        /// the hull keeps whatever spin it has.</summary>
        public DVec3 DesiredForward;

        /// <summary>Forward/reverse thrust, -1..1.</summary>
        public double Throttle;

        /// <summary>Lateral and vertical strafe thrust in the hull's own frame, each -1..1.</summary>
        public double StrafeRight, StrafeUp;

        /// <summary>Roll, -1..1: positive rolls clockwise as seen from behind (right wing down).</summary>
        public double Roll;

        public bool Boost;

        /// <summary>Holding the cruise key: see <see cref="PilotCruiseState"/>.</summary>
        public bool Cruise;

        /// <summary>True whenever the hull is in an active battle (set by the Runtime layer from
        /// <c>ship.Battle</c>/<c>BattleUnit.PlayerControlled</c>) -- cruise is a pure open-space travel
        /// tier and must drop the instant combat is live, win or lose.</summary>
        public bool InCombat;

        /// <summary>Flight assist switched off (it is on by default): throttle and strafe become raw
        /// thrust, nothing counter-thrusts, and the hull drifts on its momentum -- for slides past a
        /// target and turn-and-burns. See <see cref="PilotFlight.Step"/>.</summary>
        public bool FlightAssistOff;
    }

    /// <summary>
    /// Ramp state for the cruise drive (see <see cref="PilotFlight.Step"/>): a second, much stronger
    /// drive tier for open-space travel, distinct from tactical boost. Persists across frames inside
    /// <see cref="PilotState"/>.
    /// </summary>
    public struct PilotCruiseState
    {
        /// <summary>0 (tactical flight) .. 1 (full cruise thrust) -- eases up over
        /// <see cref="PilotTuning.CruiseRampS"/> while engaged, but drops to 0 instantly the moment any
        /// disqualifying condition fires (damage, combat, throttling down, or turning hard), rather
        /// than easing back down -- cruise is meant to feel like it snaps off, not coast down.</summary>
        public double RampT;

        double lastHull;
        bool hasLastHull;

        /// <summary>Whether cruise is contributing any extra thrust at all right now.</summary>
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
    /// A hull's design turned into what flight needs (U.SPC-T59): how hard each set of thrusters can
    /// push it (km/s²) and how hard its attitude thrusters can spin it (rad/s²), from the mass, thrust,
    /// torque and moments of inertia on its <see cref="CombatSpec"/>. A design with no mass (a defence
    /// platform) cannot move at all.
    /// </summary>
    public readonly struct HullDynamics
    {
        public readonly double MassKt;

        /// <summary>Linear acceleration each thruster set gives, km/s²: the main drive along the nose,
        /// the retro thrusters against it, and the manoeuvre thrusters sideways and up/down.</summary>
        public readonly double MainAccel, RetroAccel, LateralAccel, VerticalAccel;

        /// <summary>Angular acceleration the attitude thrusters give, rad/s²: torque over moment of inertia.</summary>
        public readonly double PitchYawAngularAccel, RollAngularAccel;

        public HullDynamics(double massKt, double mainAccel, double retroAccel, double lateralAccel, double verticalAccel, double pitchYawAngularAccel, double rollAngularAccel)
        {
            MassKt = massKt;
            MainAccel = mainAccel;
            RetroAccel = retroAccel;
            LateralAccel = lateralAccel;
            VerticalAccel = verticalAccel;
            PitchYawAngularAccel = pitchYawAngularAccel;
            RollAngularAccel = rollAngularAccel;
        }

        /// <summary>MN over kt gives m/s²; the flight model runs in km and seconds.</summary>
        const double MetresPerKm = 1000;

        public static HullDynamics Of(CombatSpec spec)
        {
            double m = spec.MassKt;
            if (m <= 0) return default;
            double iPy = spec.PitchYawInertia, iRoll = spec.RollInertia;
            return new HullDynamics(
                m,
                spec.MainThrustMN / (m * MetresPerKm),
                spec.RetroThrustMN / (m * MetresPerKm),
                spec.LateralThrustMN / (m * MetresPerKm),
                spec.VerticalThrustMN / (m * MetresPerKm),
                iPy > 0 ? spec.PitchYawTorqueMNKm / (iPy * MetresPerKm) : 0,
                iRoll > 0 ? spec.RollTorqueMNKm / (iRoll * MetresPerKm) : 0);
        }
    }

    /// <summary>
    /// Everything about a piloted hull's motion that carries from one frame to the next: where it is
    /// and how fast it is going (km, km/s, in its body's frame, ecliptic axes), its full attitude
    /// (nose and roof, so roll is a real axis) and how fast it is spinning, and the cruise drive's ramp.
    /// </summary>
    public struct PilotState
    {
        public DVec3 Pos, Vel;

        /// <summary>Unit nose and roof directions, kept perpendicular.</summary>
        public DVec3 Forward, Up;

        /// <summary>Spin, rad/s, about a world-space axis (right-hand rule).</summary>
        public DVec3 AngularVelocity;

        public PilotCruiseState Cruise;

        /// <summary>The hull's starboard side: with ecliptic +Z up and the nose on +X, this is -Y.</summary>
        public DVec3 Right => DVec3.Cross(Forward, Up).Normalized;

        /// <summary>How fast the nose is swinging (pitch and yaw together), deg/s.</summary>
        public double TurnRateDegS => (AngularVelocity - Forward * DVec3.Dot(AngularVelocity, Forward)).Length * Astro.Rad2Deg;

        /// <summary>How fast the hull is rolling about its nose, deg/s (positive = clockwise from behind).</summary>
        public double RollRateDegS => DVec3.Dot(AngularVelocity, Forward) * Astro.Rad2Deg;

        /// <summary>A hull at rest in attitude, nose on <paramref name="forward"/> and roof as near the
        /// ecliptic's north as the nose allows (the same level the renderer drew hulls at before roll).</summary>
        public static PilotState At(DVec3 pos, DVec3 vel, DVec3 forward)
        {
            var f = forward.LengthSquared > 1e-18 ? forward.Normalized : DVec3.UnitX;
            return new PilotState { Pos = pos, Vel = vel, Forward = f, Up = LevelUp(f) };
        }

        /// <summary>The roof direction for a level hull flying along <paramref name="forward"/>.</summary>
        public static DVec3 LevelUp(DVec3 forward)
        {
            var reference = Math.Abs(DVec3.Dot(forward, DVec3.UnitZ)) > 0.98 ? DVec3.UnitX : DVec3.UnitZ;
            return (reference - forward * DVec3.Dot(reference, forward)).Normalized;
        }
    }

    /// <summary>
    /// Flies a player-piloted hull with Newtonian physics (U.SPC-T59): its thrusters and attitude
    /// thrusters apply forces and torques to a body with mass and moments of inertia, and the hull
    /// carries its momentum, linear and angular, from frame to frame.
    ///
    /// Flight assist (on by default) is the flight computer: it reads the throttle and strafe as a
    /// commanded velocity (the hull's set point, up to <see cref="PilotTuning.MaxSpeed"/>) and fires
    /// whichever thrusters bring the real velocity to it -- cancelling drift and gravity as it goes,
    /// within what the thrusters can push -- and holds the roll rate to the roll input. Off, the
    /// throttle and strafe are raw thrust, nothing cancels anything, and the hull slides on whatever
    /// heading it was going, so it can turn its nose and fire across its own track or flip and burn
    /// to brake. Either way the nose follows the stick, as fast as the hull's torque allows.
    /// </summary>
    public static class PilotFlight
    {
        /// <summary>Flight assist's strafe set point, as a fraction of its forward one.</summary>
        public const double StrafeSpeedFraction = 0.35;

        /// <summary>Boost drains twice as fast as it recharges.</summary>
        const double RechargeFraction = 0.5;

        /// <summary>Heading drift from the desired forward still read as "flying roughly straight";
        /// anything sharper is a deliberate turn and drops cruise.</summary>
        const double CruiseMaxHeadingDriftDeg = 5;

        /// <summary>Throttle below this reads as throttling down out of cruise, not holding it floored.</summary>
        const double CruiseMinThrottle = 0.9;

        /// <summary>A body's gravity reaches a piloted hull only this close (in body radii): near enough
        /// to matter against a hull's thrust (a quarter of surface gravity at the edge of it, three
        /// radii being where ships park), out of the way of ordinary flying elsewhere.</summary>
        public const double GravityReachRadii = 4;

        /// <summary>The outer part of that reach (as a fraction of it) over which gravity eases in, so
        /// crossing the edge is not a jolt.</summary>
        const double GravityFadeFraction = 0.25;

        /// <summary>
        /// Advances the hull by <paramref name="dt"/> seconds: turns it with its attitude thrusters,
        /// works out this frame's thrust (flight assist's counter-thrust toward the commanded velocity,
        /// or the raw stick with assist off), adds <paramref name="gravityKmS2"/> (see
        /// <see cref="Gravity"/>), and integrates velocity then position (semi-implicit Euler, which
        /// with no force carries momentum exactly).
        /// </summary>
        public static void Step(ref PilotState s, ShipCombat combat, in HullDynamics hull, in PilotTuning tuning, in PilotInput input, DVec3 gravityKmS2, double dt)
        {
            if (dt <= 0) return;
            var forwardBeforeTurn = s.Forward;
            Rotate(ref s, hull, tuning, input, dt);

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
            // Boost (an afterburner) and cruise (the drive spooled up) are more main-drive thrust; the
            // speed comes from burning it, and flight assist's set point rises with the tier.
            double driveMult = (boosting ? tuning.BoostSpeedMultiplier : 1)
                * StepCruise(ref s.Cruise, input, forwardBeforeTurn, combat, tuning, dt);

            var f = s.Forward;
            var u = s.Up;
            var r = s.Right;
            double throttle = Clamp(input.Throttle), strafeR = Clamp(input.StrafeRight), strafeU = Clamp(input.StrafeUp);
            double main = hull.MainAccel * driveMult;

            DVec3 thrust;
            if (input.FlightAssistOff)
            {
                thrust = f * (throttle >= 0 ? throttle * main : throttle * hull.RetroAccel)
                    + r * (strafeR * hull.LateralAccel)
                    + u * (strafeU * hull.VerticalAccel);
            }
            else
            {
                double setPoint = tuning.MaxSpeed * driveMult;
                var commanded = f * (throttle * setPoint)
                    + r * (strafeR * setPoint * StrafeSpeedFraction)
                    + u * (strafeU * setPoint * StrafeSpeedFraction);
                // Exactly the push that lands on the commanded velocity this frame, gravity cancelled,
                // then cut to what each set of thrusters can actually give.
                var need = (commanded - s.Vel) / dt - gravityKmS2;
                thrust = f * Math.Max(-hull.RetroAccel, Math.Min(main, DVec3.Dot(need, f)))
                    + r * Limit(DVec3.Dot(need, r), hull.LateralAccel)
                    + u * Limit(DVec3.Dot(need, u), hull.VerticalAccel);
            }

            s.Vel += (thrust + gravityKmS2) * dt;
            s.Pos += s.Vel * dt;
        }

        /// <summary>
        /// One body's pull on a hull <paramref name="fromBodyKm"/> from its centre, km/s²: inverse-square
        /// within <see cref="GravityReachRadii"/> of it (easing in over the outer part of that reach),
        /// nothing beyond. Inside the body it holds at its surface value. The caller sums this over the
        /// bodies near the hull.
        /// </summary>
        public static DVec3 Gravity(DVec3 fromBodyKm, double gmKm3S2, double radiusKm)
        {
            double d = fromBodyKm.Length;
            double reach = radiusKm * GravityReachRadii;
            if (gmKm3S2 <= 0 || radiusKm <= 0 || d >= reach || d < 1e-9) return DVec3.Zero;
            double rr = Math.Max(d, radiusKm);
            double g = gmKm3S2 / (rr * rr);
            double fade = Math.Min(1, (reach - d) / (reach * GravityFadeFraction));
            return fromBodyKm * (-g * fade / d);
        }

        /// <summary>
        /// Holds the hull at least <paramref name="floorKm"/> from a body's centre
        /// (<paramref name="bodyKm"/>, in the hull's own frame): a hull that sinks onto the surface comes
        /// to rest on it, its downward speed lost and its sideways speed kept. True if it touched.
        /// </summary>
        public static bool KeepAboveSurface(ref PilotState s, DVec3 bodyKm, double floorKm) =>
            KeepAboveSurface(ref s, bodyKm, floorKm, out _);

        /// <summary>As <see cref="KeepAboveSurface(ref PilotState, DVec3, double)"/>, also giving the downward
        /// speed the hull lost on the surface, km/s: how hard it came down (see
        /// <see cref="Collisions.SurfaceImpactShareTJ"/>, U.SPC-T60).</summary>
        public static bool KeepAboveSurface(ref PilotState s, DVec3 bodyKm, double floorKm, out double sinkKmS)
        {
            sinkKmS = 0;
            var rel = s.Pos - bodyKm;
            double d = rel.Length;
            if (floorKm <= 0 || d >= floorKm) return false;
            var n = d > 1e-9 ? rel / d : s.Up;
            s.Pos = bodyKm + n * floorKm;
            double down = DVec3.Dot(s.Vel, n);
            if (down < 0)
            {
                s.Vel -= n * down;
                sinkKmS = -down;
            }
            return true;
        }

        /// <summary>
        /// Advances the cruise drive's ramp and returns this frame's main-drive multiplier (1 when not
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

        /// <summary>
        /// The attitude thrusters, then the spin they leave applied to the hull. Pitch and yaw follow
        /// the stick: the computer swings the nose toward <see cref="PilotInput.DesiredForward"/> no faster
        /// than <see cref="PilotTuning.TurnRateDegS"/>, spinning up and braking at the hull's own angular
        /// acceleration so the nose settles on the heading instead of overshooting -- heavy hulls are
        /// slow to start swinging and slow to stop. Roll is held to the roll input's rate with flight
        /// assist on (so letting go stops the roll); off, roll input is raw torque and a roll, once
        /// started, keeps going until countered. With assist off and no stick input at all, nothing
        /// touches the spin: angular momentum is conserved.
        /// </summary>
        static void Rotate(ref PilotState s, in HullDynamics hull, in PilotTuning tuning, in PilotInput input, double dt)
        {
            var f = s.Forward;
            var w = s.AngularVelocity;
            double wRoll = DVec3.Dot(w, f);
            var wTurn = w - f * wRoll;
            double alpha = hull.PitchYawAngularAccel;
            double maxDw = alpha * dt;

            bool steering = input.DesiredForward.LengthSquared > 1e-12;
            var want = steering ? input.DesiredForward.Normalized : f;
            double angle = 0;
            if (steering || !input.FlightAssistOff)
            {
                var target = DVec3.Zero;
                if (steering)
                {
                    var axis = DVec3.Cross(f, want);
                    double sin = axis.Length, cos = DVec3.Dot(f, want);
                    angle = Math.Atan2(sin, cos);
                    // Dead astern: any axis across the nose will do; the roof is as good as any.
                    if (sin < 1e-9) axis = cos > 0 ? DVec3.Zero : s.Up;
                    if (axis.LengthSquared > 0)
                    {
                        // Fast enough to get there, slow enough to stop on it: v² = 2·α·θ.
                        double rate = Math.Min(tuning.TurnRateDegS * Astro.Deg2Rad, Math.Sqrt(2 * alpha * angle));
                        target = axis.Normalized * rate;
                    }
                }
                var dw = target - wTurn;
                if (dw.Length > maxDw) dw = dw.Normalized * maxDw;
                wTurn += dw;
            }

            // Roll is about the nose; positive input rolls clockwise from behind, a positive spin about it.
            double rollAlpha = hull.RollAngularAccel;
            double rollIn = Clamp(input.Roll);
            if (input.FlightAssistOff)
            {
                wRoll += rollIn * rollAlpha * dt;
            }
            else
            {
                double targetRoll = rollIn * tuning.RollRateDegS * Astro.Deg2Rad;
                wRoll += Limit(targetRoll - wRoll, rollAlpha * dt);
            }

            s.AngularVelocity = wTurn + f * wRoll;
            double spin = s.AngularVelocity.Length * dt;
            if (spin > 0)
            {
                var k = s.AngularVelocity.Normalized;
                s.Forward = RotateAbout(s.Forward, k, spin).Normalized;
                s.Up = RotateAbout(s.Up, k, spin);
            }

            // Arriving on the heading this frame, with little enough swing left to kill in a frame or
            // so: settle exactly on it rather than overshooting by a hair and hunting back and forth.
            double turnRate = wTurn.Length;
            if (steering && angle > 0 && angle <= turnRate * dt + 1e-12 && turnRate <= 3 * maxDw + 1e-12)
            {
                s.Forward = want;
                s.AngularVelocity = want * DVec3.Dot(s.AngularVelocity, want);
            }

            var up = s.Up - s.Forward * DVec3.Dot(s.Up, s.Forward);
            s.Up = up.LengthSquared > 1e-18 ? up.Normalized : PilotState.LevelUp(s.Forward);
        }

        /// <summary>Rodrigues' rotation of <paramref name="v"/> by <paramref name="angle"/> radians about unit axis <paramref name="k"/>.</summary>
        public static DVec3 RotateAbout(DVec3 v, DVec3 k, double angle)
        {
            double c = Math.Cos(angle), sn = Math.Sin(angle);
            return v * c + DVec3.Cross(k, v) * sn + k * (DVec3.Dot(k, v) * (1 - c));
        }

        static double Clamp(double x) => Math.Max(-1, Math.Min(1, x));

        static double Limit(double x, double max) => Math.Max(-max, Math.Min(max, x));
    }
}
