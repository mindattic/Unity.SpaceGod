using System;

namespace SolarSystem.Core
{
    /// <summary>
    /// A fixed structure at a system's outer edge, paired one-for-one with a gate in another system: a
    /// piloted ship that flies to one and activates it emerges at the other (see <see cref="GateTransit"/>).
    /// This is a flyable, in-world alternative to the menu-driven STAR SYSTEMS jump that the camera uses
    /// on its own (Runtime's StarTravel/Galaxy navigator, left untouched) -- not a replacement for it.
    ///
    /// Placed at <see cref="RadiusKm"/> from the system's own root rather than anywhere near the real
    /// scale of a light-year crossing: a piloted hull's top speed (<see cref="PilotTuning"/>, even
    /// cruising) is tuned for dogfighting range, not interplanetary distances, and piloting runs the
    /// simulation clock at real time with no warp (see <c>SolarSystemApp.TogglePiloting</c>) -- so the
    /// gate has to sit somewhere an actual flight stick can reach in minutes, not light-years.
    /// </summary>
    public sealed class JumpGate
    {
        public string Name { get; }

        /// <summary>Fixed position relative to the system's own root (its star or barycentre), km, in
        /// the same ecliptic axes as its ephemeris. The root never moves in its own frame, so this is
        /// constant for the life of the system.</summary>
        public DVec3 PositionKm { get; }

        /// <summary>The system this gate leads to; set once, after both ends of a pair exist (see
        /// <see cref="Galaxy.CreateDefault"/>). Null means this gate has no working pair yet.</summary>
        public StarSystem Destination { get; internal set; }

        /// <summary>How far out a gate sits from its system's root: above the surface of a star (inside
        /// the Sun would be nowhere a ship can fly), from the point itself for a barycentre; see
        /// <see cref="Galaxy.GateDistanceKm"/>.</summary>
        public const double RadiusKm = 5000;

        /// <summary>A ship must be within this of the gate's position to activate it.</summary>
        public const double ActivationRangeKm = 300;

        /// <summary>Clear of the far gate on arrival, toward that system's centre, so an arriving ship
        /// doesn't appear exactly coincident with the structure it just came out of.</summary>
        public const double ArrivalInsetKm = 150;

        public JumpGate(string name, DVec3 positionKm)
        {
            Name = name;
            PositionKm = positionKm;
        }

        public bool InRange(DVec3 shipHelioKm) => (shipHelioKm - PositionKm).Length <= ActivationRangeKm;

        /// <summary>Whether a ship at <paramref name="shipHelioKm"/> (relative to this gate's own
        /// system root) can activate this gate right now: paired, and close enough.</summary>
        public bool CanActivate(DVec3 shipHelioKm) => Destination?.Gate != null && InRange(shipHelioKm);

        /// <summary>The direction an arriving ship faces: from the far gate back toward that system's
        /// own root, so it comes through facing into the system rather than out into the void.</summary>
        public DVec3 ArrivalForward => Destination == null ? DVec3.UnitX : -Destination.Gate.PositionKm.Normalized;

        /// <summary>Where a ship taking this gate appears, relative to the destination system's root:
        /// just inside the far gate, facing <see cref="ArrivalForward"/>.</summary>
        public DVec3 ArrivalOffsetKm => Destination == null ? DVec3.Zero : Destination.Gate.PositionKm + ArrivalForward * ArrivalInsetKm;

        public override string ToString() => Name;
    }
}
