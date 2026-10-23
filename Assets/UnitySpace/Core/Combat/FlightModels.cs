using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The families of craft that fly by rules of their own at the helm (U.SPC-T17, see
    /// docs/FLIGHT_MODELS.md). A family is a way of flying, not a hull: every <see cref="ShipClass"/>
    /// belongs to exactly one (<see cref="FlightModelRegistry.FamilyOf"/>), and each family has one
    /// <see cref="IFlightModel"/>. This is where the fighter lineup splits into light and heavy.
    /// </summary>
    public enum FlightFamily
    {
        /// <summary>Star Conflict-style dogfighters (the Fighter, and for now the Courier): the track
        /// carves after the nose, strafing is strong, and a turn is tightest at the corner speed.</summary>
        LightFighter,

        /// <summary>Everspace 2-style heavy fighters: the Gunship (U.SPC-T467), and for now the Breacher, an assault
        /// craft flown the same way until U.SPC-T466 makes it a fired boarding round.</summary>
        HeavyFighter,

        /// <summary>Freighters and miners: heavy, deliberate haulers.</summary>
        Hauler,

        /// <summary>Capital ships (Carrier, Battleship, Cruiser, Destroyer): naval broadside handling.</summary>
        Capital,
    }

    /// <summary>
    /// How one family of craft turns the pilot's input into motion each frame (U.SPC-T17). Every model
    /// flies the same <see cref="PilotState"/> on the same hull (<see cref="HullDynamics"/>) and class
    /// tuning (<see cref="PilotTuning"/>), so a hull handed from one model to another carries on where it
    /// was; what differs is the rules in between. A model keeps no state of its own between frames
    /// (everything that carries is on <see cref="PilotState"/> or <see cref="ShipCombat"/>), so one
    /// instance serves every hull of its family. A new way of flying is a new implementation registered
    /// for its family, never a branch in <see cref="PilotFlight"/>.
    /// </summary>
    public interface IFlightModel
    {
        /// <summary>A short name for the HUD and logs.</summary>
        string Name { get; }

        /// <summary>Advances the hull by <paramref name="dt"/> seconds; see <see cref="PilotFlight.Step"/>
        /// for what each argument means.</summary>
        void Step(ref PilotState s, ShipCombat combat, in HullDynamics hull, in PilotTuning tuning, in PilotInput input, DVec3 gravityKmS2, double dt);
    }

    /// <summary>
    /// The shared Newtonian model every hull flew before U.SPC-T17 (U.SPC-T59, <see cref="PilotFlight.Step"/>),
    /// unchanged: the families with no model of their own yet fly it.
    /// </summary>
    public sealed class NewtonianFlightModel : IFlightModel
    {
        public static readonly NewtonianFlightModel Instance = new NewtonianFlightModel();

        public string Name => "Newtonian";

        public void Step(ref PilotState s, ShipCombat combat, in HullDynamics hull, in PilotTuning tuning, in PilotInput input, DVec3 gravityKmS2, double dt) =>
            PilotFlight.Step(ref s, combat, hull, tuning, input, gravityKmS2, dt);
    }

    /// <summary>
    /// The light fighter's handling numbers (U.SPC-T17): data, not code, so a tuning pass or the settings
    /// registry (U.SPC-T431) changes them without touching <see cref="LightFighterFlightModel"/>.
    /// </summary>
    public readonly struct LightFighterHandling
    {
        /// <summary>With flight assist on, how hard the flight computer may push across the nose to pull
        /// the track round after it (the "grip" that makes a turn a carve, not a slide), as a fraction of
        /// the push that holds the track to the nose through the tightest turn at the corner speed
        /// (corner speed times full turn rate): at 1 the track sticks to the nose at or below the corner
        /// speed and slides wide above it. The Newtonian model is held to the manoeuvre thrusters, a few
        /// percent of that.</summary>
        public readonly double Grip;

        /// <summary>Flight assist's strafe set point as a fraction of the forward one (the Newtonian
        /// model's is <see cref="PilotFlight.StrafeSpeedFraction"/>): a light fighter dodges sideways hard.</summary>
        public readonly double StrafeSpeedFraction;

        /// <summary>The corner speed, as a fraction of <see cref="PilotTuning.MaxSpeed"/>: at or below it
        /// the nose swings at the full <see cref="PilotTuning.TurnRateDegS"/>.</summary>
        public readonly double CornerSpeedFraction;

        /// <summary>The turn rate left at full boost speed (<see cref="PilotTuning.MaxSpeed"/> times
        /// <see cref="PilotTuning.BoostSpeedMultiplier"/>) and beyond, as a fraction of the full rate;
        /// between the corner speed and there it tapers linearly. Boost to get somewhere, ease off to turn.</summary>
        public readonly double OverspeedTurnFactor;

        public LightFighterHandling(double grip, double strafeSpeedFraction, double cornerSpeedFraction, double overspeedTurnFactor)
        {
            Grip = grip;
            StrafeSpeedFraction = strafeSpeedFraction;
            CornerSpeedFraction = cornerSpeedFraction;
            OverspeedTurnFactor = overspeedTurnFactor;
        }

        /// <summary>The first-pass numbers, for the owner's review (docs/FLIGHT_MODELS.md).</summary>
        public static readonly LightFighterHandling Default = new LightFighterHandling(
            grip: 1.0, strafeSpeedFraction: 0.5, cornerSpeedFraction: 0.6, overspeedTurnFactor: 0.6);
    }

    /// <summary>
    /// Light fighters, Star Conflict style (U.SPC-T17): arcade-coupled dogfighting. With flight assist on,
    /// (1) the track carves after the nose -- the computer may push across the nose as hard as
    /// <see cref="LightFighterHandling.Grip"/> allows, so a hard turn at the corner speed leaves no
    /// slide and a faster one only a little; (2) strafing is strong (<see cref="LightFighterHandling.StrafeSpeedFraction"/>); and (3) the
    /// nose swings fastest at or below the corner speed and stiffens above it, down to
    /// <see cref="LightFighterHandling.OverspeedTurnFactor"/> of the full rate at full boost, so speed is
    /// something to manage in a fight. With flight assist off the hull is the Newtonian one, unchanged:
    /// the drift and the turn-and-burn stay a pilot's tool.
    /// </summary>
    public sealed class LightFighterFlightModel : IFlightModel
    {
        public LightFighterHandling Handling { get; }

        public LightFighterFlightModel() : this(LightFighterHandling.Default) { }

        public LightFighterFlightModel(LightFighterHandling handling) => Handling = handling;

        public string Name => "Light fighter";

        /// <summary>The fastest the nose may swing at <paramref name="speedKmS"/>, deg/s (see the class remarks).</summary>
        public double TurnRateAt(double speedKmS, in PilotTuning tuning)
        {
            double corner = tuning.MaxSpeed * Handling.CornerSpeedFraction;
            double top = tuning.MaxSpeed * Math.Max(1, tuning.BoostSpeedMultiplier);
            if (speedKmS <= corner || top <= corner) return tuning.TurnRateDegS;
            double t = Math.Min(1, (speedKmS - corner) / (top - corner));
            return tuning.TurnRateDegS * (1 - t * (1 - Handling.OverspeedTurnFactor));
        }

        /// <summary>The most the flight computer pushes across the nose with assist on, km/s² (see
        /// <see cref="LightFighterHandling.Grip"/>).</summary>
        public double GripAccel(in PilotTuning tuning) =>
            Handling.Grip * tuning.MaxSpeed * Handling.CornerSpeedFraction * tuning.TurnRateDegS * Astro.Deg2Rad;

        public void Step(ref PilotState s, ShipCombat combat, in HullDynamics hull, in PilotTuning tuning, in PilotInput input, DVec3 gravityKmS2, double dt)
        {
            if (input.FlightAssistOff)
            {
                PilotFlight.Step(ref s, combat, hull, tuning, input, gravityKmS2, dt);
                return;
            }
            if (dt <= 0) return;

            var turning = new PilotTuning(tuning.MaxSpeed, TurnRateAt(s.Vel.Length, tuning), tuning.BoostSpeedMultiplier,
                tuning.BoostDurationS, tuning.CruiseSpeedMultiplier, tuning.CruiseRampS);
            var forwardBeforeTurn = s.Forward;
            PilotFlight.Rotate(ref s, hull, turning, input, dt);
            double driveMult = PilotFlight.DriveMultiplier(ref s, combat, tuning, input, forwardBeforeTurn, dt);

            double grip = GripAccel(tuning);
            var thrust = PilotFlight.AssistThrust(s, hull, tuning, input, gravityKmS2, driveMult, dt, Handling.StrafeSpeedFraction,
                Math.Max(hull.LateralAccel, grip), Math.Max(hull.VerticalAccel, grip));
            PilotFlight.Integrate(ref s, thrust, gravityKmS2, dt);
        }
    }

    /// <summary>
    /// Which family each <see cref="ShipClass"/> flies as, and which model flies each family (U.SPC-T17):
    /// the one extension point for per-class handling. Both tables are data: a new class is assigned a
    /// family, and a new way of flying is a model registered for its family, without touching the code
    /// that flies the others. <see cref="Default"/> is the game's; tests build their own.
    /// </summary>
    public sealed class FlightModelRegistry
    {
        readonly Dictionary<ShipClass, FlightFamily> families = new Dictionary<ShipClass, FlightFamily>();
        readonly Dictionary<FlightFamily, IFlightModel> models = new Dictionary<FlightFamily, IFlightModel>();

        /// <summary>The model a family with none registered flies: the shared Newtonian one.</summary>
        public IFlightModel Fallback { get; set; } = NewtonianFlightModel.Instance;

        /// <summary>The game's registry: every class in its family, and the light fighters on their own
        /// model; the other families fly the Newtonian model until theirs land (docs/FLIGHT_MODELS.md).</summary>
        public static FlightModelRegistry Default { get; } = CreateDefault();

        public static FlightModelRegistry CreateDefault()
        {
            var r = new FlightModelRegistry();
            r.Assign(ShipClass.Fighter, FlightFamily.LightFighter);
            r.Assign(ShipClass.Courier, FlightFamily.LightFighter);
            r.Assign(ShipClass.Gunship, FlightFamily.HeavyFighter);
            r.Assign(ShipClass.Breacher, FlightFamily.HeavyFighter);
            r.Assign(ShipClass.Freighter, FlightFamily.Hauler);
            r.Assign(ShipClass.Miner, FlightFamily.Hauler);
            r.Assign(ShipClass.Explorer, FlightFamily.Hauler);
            r.Assign(ShipClass.OreTug, FlightFamily.Hauler);
            r.Assign(ShipClass.Carrier, FlightFamily.Capital);
            r.Assign(ShipClass.Battleship, FlightFamily.Capital);
            r.Assign(ShipClass.Cruiser, FlightFamily.Capital);
            r.Assign(ShipClass.Destroyer, FlightFamily.Capital);
            r.Register(FlightFamily.LightFighter, new LightFighterFlightModel());
            return r;
        }

        /// <summary>Puts <paramref name="c"/> in <paramref name="family"/>.</summary>
        public void Assign(ShipClass c, FlightFamily family) => families[c] = family;

        /// <summary>Flies every hull of <paramref name="family"/> with <paramref name="model"/>.</summary>
        public void Register(FlightFamily family, IFlightModel model) =>
            models[family] = model ?? throw new ArgumentNullException(nameof(model));

        /// <summary>The family <paramref name="c"/> flies as; a class never assigned flies as a capital ship.</summary>
        public FlightFamily FamilyOf(ShipClass c) => families.TryGetValue(c, out var f) ? f : FlightFamily.Capital;

        /// <summary>The model flying <paramref name="family"/>.</summary>
        public IFlightModel ModelOf(FlightFamily family) => models.TryGetValue(family, out var m) ? m : Fallback;

        /// <summary>The model flying a hull of class <paramref name="c"/>.</summary>
        public IFlightModel For(ShipClass c) => ModelOf(FamilyOf(c));
    }
}
