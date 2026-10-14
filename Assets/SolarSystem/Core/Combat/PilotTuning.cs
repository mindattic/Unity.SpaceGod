using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>
    /// How a hull handles under a human's hands, independent of <see cref="CombatSpec"/>'s AI-battle
    /// balance numbers -- a Freighter tuned to feel fine as a slow AI hauler (three degrees a second)
    /// is not a Freighter that's fun to actually fly. Keyed by <see cref="ShipClass"/> rather than by
    /// design, since the arcade feel is a class-wide call, not a per-hull balance knob.
    /// </summary>
    public readonly struct PilotTuning
    {
        /// <summary>Flight numbers in the same units as <see cref="CombatSpec"/>: km/s, km/s², deg/s.</summary>
        public readonly double MaxSpeed, Accel, Decel, TurnRateDegS, BoostSpeedMultiplier, BoostDurationS;

        /// <summary>Seconds of angular spin-up to go from rest to <see cref="TurnRateDegS"/> -- the
        /// rotational equivalent of <see cref="Accel"/>. Without it, every hull snapped to its full
        /// turn rate the instant input changed, which reads as twitchy for a fighter and absurdly
        /// agile for a capital ship; this is what actually makes capitals feel heavy to throw around,
        /// more than the raw <see cref="TurnRateDegS"/> number does on its own.</summary>
        public readonly double AngularRampS;

        /// <summary>How much faster than <see cref="MaxSpeed"/> the cruise drive tops out at (see
        /// <see cref="PilotCruiseState"/>) -- deliberately far above anything boost reaches, since
        /// cruise exists to cover open-space distances that would otherwise take real-world days at
        /// tactical sub-light speeds, not to win a fight.</summary>
        public readonly double CruiseSpeedMultiplier;

        /// <summary>Seconds of spooling up from rest to full cruise speed once engaged.</summary>
        public readonly double CruiseRampS;

        public PilotTuning(double maxSpeed, double accel, double decel, double turnRateDegS, double boostSpeedMultiplier, double boostDurationS, double angularRampS, double cruiseSpeedMultiplier, double cruiseRampS)
        {
            MaxSpeed = maxSpeed;
            Accel = accel;
            Decel = decel;
            TurnRateDegS = turnRateDegS;
            BoostSpeedMultiplier = boostSpeedMultiplier;
            BoostDurationS = boostDurationS;
            AngularRampS = angularRampS;
            CruiseSpeedMultiplier = cruiseSpeedMultiplier;
            CruiseRampS = cruiseRampS;
        }

        static readonly Dictionary<ShipClass, PilotTuning> ByClass = new Dictionary<ShipClass, PilotTuning>
        {
            // Star fighters: Everspace 2-paced, a full about-face in a second or so. Angular ramp is
            // short enough to read as nearly instant snap-turning. Cruise spools up fast too -- small,
            // nimble hulls should feel quick to get going in open space, not just in a dogfight.
            [ShipClass.Courier] = new PilotTuning(0.45, 0.05, 0.06, 150, 2.2, 14, 0.12, 10, 2.0),
            [ShipClass.Breacher] = new PilotTuning(0.4, 0.045, 0.05, 110, 2.0, 22, 0.15, 9, 2.2),
            // The dedicated dogfighter: trades a hair of the Courier's top speed for the tightest turn
            // and snappiest angular ramp in the game -- built to out-turn everything else in a fight.
            [ShipClass.Fighter] = new PilotTuning(0.42, 0.055, 0.065, 165, 2.3, 15, 0.08, 9, 1.8),
            [ShipClass.Freighter] = new PilotTuning(0.18, 0.03, 0.035, 75, 1.6, 36, 0.35, 8, 3.0),
            [ShipClass.Miner] = new PilotTuning(0.16, 0.028, 0.032, 70, 1.6, 38, 0.4, 8, 3.0),

            // Capital ships: still weighty -- you're captaining a warship, not dogfighting in it --
            // but sharper than their AI-battle turn rates so they're not sluggish to the point of
            // being unplayable. Long angular ramps are what sell the weight: the nose takes real
            // time to start swinging even though the steady-state rate itself is already playable.
            // Cruise ramps take longest here -- a capital ship spooling its drive should feel like a
            // deliberate, committed act, not a tap of a key.
            [ShipClass.Carrier] = new PilotTuning(0.07, 0.0025, 0.0025, 8, 1.3, 30, 1.0, 5, 5.0),
            [ShipClass.Battleship] = new PilotTuning(0.09, 0.003, 0.003, 10, 1.3, 32, 0.8, 5, 4.5),
            [ShipClass.Cruiser] = new PilotTuning(0.14, 0.005, 0.006, 16, 1.4, 28, 0.6, 6, 4.0),
            // Destroyer stays the nose-forward skirmisher (see Battle.BroadsideFacing) -- agile to
            // bring to bear, not just to steady-state turn.
            [ShipClass.Destroyer] = new PilotTuning(0.24, 0.009, 0.011, 28, 1.5, 25, 0.4, 7, 3.5),
        };

        public static PilotTuning Of(ShipClass c) =>
            ByClass.TryGetValue(c, out var t) ? t : new PilotTuning(0.1, 0.01, 0.01, 20, 1.3, 20, 0.3, 6, 3.0);
    }
}
