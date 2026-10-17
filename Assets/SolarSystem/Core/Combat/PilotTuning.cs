using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>
    /// How a hull handles under a human's hands, independent of <see cref="CombatSpec"/>'s AI-battle
    /// balance numbers -- a Freighter tuned to feel fine as a slow AI hauler (three degrees a second)
    /// is not a Freighter that's fun to actually fly. Keyed by <see cref="ShipClass"/> rather than by
    /// design, since the arcade feel is a class-wide call, not a per-hull balance knob.
    ///
    /// Since U.SPC-T59 the hull's push and spin come from its own design (mass, thrust, torque and
    /// inertia on <see cref="CombatSpec"/>, see <see cref="HullDynamics"/>); what is left here is what
    /// the flight computer does with them: the speeds and turn rates flight assist holds the hull to,
    /// and the boost and cruise drive tiers.
    /// </summary>
    public readonly struct PilotTuning
    {
        /// <summary>The speed flight assist flies the hull at with the throttle floored, km/s: its
        /// set point, not a hard limit -- with assist off nothing caps the speed a hull can burn up to.</summary>
        public readonly double MaxSpeed;

        /// <summary>The fastest the attitude computer swings the nose (pitch/yaw), deg/s; how quickly
        /// it gets there is the hull's own torque over its inertia.</summary>
        public readonly double TurnRateDegS;

        /// <summary>Boost lights an afterburner: main-drive thrust times this while the charge lasts
        /// (draining over <see cref="BoostDurationS"/>, recharging otherwise), and flight assist's set
        /// point rises by the same factor while it burns.</summary>
        public readonly double BoostSpeedMultiplier, BoostDurationS;

        /// <summary>The cruise drive (see <see cref="PilotCruiseState"/>) spools main-drive thrust up
        /// to this many times normal, and flight assist's set point with it -- deliberately far above
        /// anything boost reaches, since cruise exists to cover open-space distances that would
        /// otherwise take real-world days at tactical speeds, not to win a fight.</summary>
        public readonly double CruiseSpeedMultiplier;

        /// <summary>Seconds of spooling up from rest to full cruise thrust once engaged.</summary>
        public readonly double CruiseRampS;

        /// <summary>The fastest flight assist lets the hull roll, deg/s (a multiple of the turn rate:
        /// a long hull rolls far more readily than it turns, see <see cref="CombatSpec.RollInertia"/>).</summary>
        public double RollRateDegS => TurnRateDegS * RollRateFactor;

        public const double RollRateFactor = 1.5;

        public PilotTuning(double maxSpeed, double turnRateDegS, double boostSpeedMultiplier, double boostDurationS, double cruiseSpeedMultiplier, double cruiseRampS)
        {
            MaxSpeed = maxSpeed;
            TurnRateDegS = turnRateDegS;
            BoostSpeedMultiplier = boostSpeedMultiplier;
            BoostDurationS = boostDurationS;
            CruiseSpeedMultiplier = cruiseSpeedMultiplier;
            CruiseRampS = cruiseRampS;
        }

        static readonly Dictionary<ShipClass, PilotTuning> ByClass = new Dictionary<ShipClass, PilotTuning>
        {
            // Star fighters: Everspace 2-paced, a full about-face in a second or so. Cruise spools up
            // fast too -- small, nimble hulls should feel quick to get going in open space.
            [ShipClass.Courier] = new PilotTuning(0.45, 150, 2.2, 14, 10, 2.0),
            [ShipClass.Breacher] = new PilotTuning(0.4, 110, 2.0, 22, 9, 2.2),
            // The dedicated dogfighter: trades a hair of the Courier's top speed for the tightest turn
            // in the game -- built to out-turn everything else in a fight.
            [ShipClass.Fighter] = new PilotTuning(0.42, 165, 2.3, 15, 9, 1.8),
            [ShipClass.Freighter] = new PilotTuning(0.18, 75, 1.6, 36, 8, 3.0),
            [ShipClass.Miner] = new PilotTuning(0.16, 70, 1.6, 38, 8, 3.0),

            // Capital ships: still weighty -- you're captaining a warship, not dogfighting in it --
            // but sharper than their AI-battle turn rates so they're not sluggish to the point of
            // being unplayable. Their huge pitch/yaw inertia is what sells the weight: the nose takes
            // real time to start swinging. Cruise ramps take longest here.
            [ShipClass.Carrier] = new PilotTuning(0.07, 8, 1.3, 30, 5, 5.0),
            [ShipClass.Battleship] = new PilotTuning(0.09, 10, 1.3, 32, 5, 4.5),
            [ShipClass.Cruiser] = new PilotTuning(0.14, 16, 1.4, 28, 6, 4.0),
            // Destroyer stays the nose-forward skirmisher (see Battle.BroadsideFacing).
            [ShipClass.Destroyer] = new PilotTuning(0.24, 28, 1.5, 25, 7, 3.5),
        };

        public static PilotTuning Of(ShipClass c) =>
            ByClass.TryGetValue(c, out var t) ? t : new PilotTuning(0.1, 20, 1.3, 20, 6, 3.0);
    }
}
