using System;

namespace SolarSystem.Core
{
    public enum ShipClass
    {
        Carrier,
        Battleship,
        Cruiser,
        Destroyer,
        Freighter,
        Miner,
        Courier,
    }

    public enum ShipRole
    {
        Trader,
        Miner,
        Courier,
        Patrol,
        Escort,
    }

    public enum ShipPhase
    {
        Parked,
        Transit,
    }

    public sealed class ShipClassSpec
    {
        public ShipClass Class { get; }
        public string DisplayName { get; }
        public double LengthKm { get; }

        /// <summary>Sustained drive acceleration in g. Torch-drive fiction; the kinematics are real.</summary>
        public double AccelG { get; }

        public bool IsMilitary { get; }

        ShipClassSpec(ShipClass c, string name, double lengthKm, double accelG, bool military)
        {
            Class = c;
            DisplayName = name;
            LengthKm = lengthKm;
            AccelG = accelG;
            IsMilitary = military;
        }

        static readonly ShipClassSpec[] Specs =
        {
            new ShipClassSpec(ShipClass.Carrier, "Carrier", 3.2, 0.3, true),
            new ShipClassSpec(ShipClass.Battleship, "Battleship", 2.4, 0.3, true),
            new ShipClassSpec(ShipClass.Cruiser, "Cruiser", 1.2, 0.3, true),
            new ShipClassSpec(ShipClass.Destroyer, "Destroyer", 0.45, 0.3, true),
            new ShipClassSpec(ShipClass.Freighter, "Freighter", 0.6, 0.1, false),
            new ShipClassSpec(ShipClass.Miner, "Ore hauler", 0.3, 0.06, false),
            new ShipClassSpec(ShipClass.Courier, "Courier", 0.08, 1.2, false),
        };

        public static ShipClassSpec Of(ShipClass c) => Specs[(int)c];
    }

    public sealed class Faction
    {
        public string Name { get; }
        public string Prefix { get; }
        public Rgb Color { get; }

        public Faction(string name, string prefix, Rgb color)
        {
            Name = name;
            Prefix = prefix;
            Color = color;
        }

        public override string ToString() => Name;
    }

    /// <summary>A circular parking orbit around a body, in a plane given by an orthonormal pair (U, V).</summary>
    public sealed class ParkingOrbit
    {
        public int Body;
        public double RadiusKm;
        public DVec3 U;
        public DVec3 V;
        public double EpochJd;
        public double PhaseAtEpoch;
        public double RateRadPerDay;

        public double PhaseAt(double jd) => PhaseAtEpoch + RateRadPerDay * (jd - EpochJd);

        public DVec3 OffsetAt(double jd)
        {
            double p = PhaseAt(jd);
            return (U * Math.Cos(p) + V * Math.Sin(p)) * RadiusKm;
        }

        public DVec3 TangentAt(double jd)
        {
            double p = PhaseAt(jd);
            return -U * Math.Sin(p) + V * Math.Cos(p);
        }

        /// <summary>Orbital velocity relative to the body, km/day.</summary>
        public DVec3 VelocityAt(double jd) => TangentAt(jd) * (RateRadPerDay * RadiusKm);

        /// <summary>The phase at which the orbit position points most nearly along <paramref name="direction"/>.</summary>
        public double PhaseFacing(DVec3 direction) => Math.Atan2(DVec3.Dot(direction, V), DVec3.Dot(direction, U));

        public double SpeedKmS => RateRadPerDay * RadiusKm / Astro.SecondsPerDay;
    }

    public sealed class Ship
    {
        public int Id;
        public string Name;
        public ShipClassSpec Spec;
        public Faction Faction;
        public ShipRole Role;

        /// <summary>Escorts fly in formation on a leader; slot is in leader lengths (x right, y up, z forward).</summary>
        public Ship Leader;
        public DVec3 FormationSlot;

        /// <summary>Patrol waypoints (body indices), visited in order.</summary>
        public int[] Route;
        public int RouteIndex;

        /// <summary>Ports a trader or courier may visit; for miners, element 0 is the home rock.</summary>
        public int[] Ports;

        public ShipPhase Phase;
        public ParkingOrbit Orbit;
        public ParkingOrbit DepartureOrbit;
        public int OriginBody = -1;
        public int DestinationBody = -1;
        public double PhaseStartJd;
        public double PhaseEndJd;

        /// <summary>
        /// The transit is flown in this body's frame: the Sun between planets, the common parent for
        /// a hop within one planet's system (Earth to the Moon, Io to Europa).
        /// </summary>
        public int FrameBody;

        /// <summary>
        /// Velocity (km/day, relative to the frame) the ship leaves with and arrives at: those of its
        /// departure and arrival parking orbits. The drift they imply is added to the path.
        /// </summary>
        public DVec3 DepartureVelocity;
        public DVec3 ArrivalVelocity;

        /// <summary>The flip-and-burn path, relative to the frame body before drift is added.</summary>
        public DVec3[] Path;
        public double[] PathCumulative;
        public double PathLength;
        public double AccelKmPerDay2;

        public string Cargo;
        public int TripsCompleted;

        public bool IsEscort => Leader != null;

        /// <summary>The ship whose state this ship shares (itself unless it is an escort).</summary>
        public Ship Controller => Leader ?? this;

        public override string ToString() => Name;
    }

    /// <summary>Kinematic snapshot of a ship at an instant.</summary>
    public readonly struct ShipState
    {
        /// <summary>Heliocentric ecliptic position, km.</summary>
        public readonly DVec3 HelioKm;

        /// <summary>Where the nose points (ecliptic, unit). Opposite the velocity during the braking half of a flip-and-burn.</summary>
        public readonly DVec3 Facing;

        public readonly double SpeedKmS;
        public readonly bool Burning;

        /// <summary>Fraction of the transit distance covered (0..1); 0 while parked.</summary>
        public readonly double Progress;

        public ShipState(DVec3 helio, DVec3 facing, double speed, bool burning, double progress)
        {
            HelioKm = helio;
            Facing = facing;
            SpeedKmS = speed;
            Burning = burning;
            Progress = progress;
        }
    }
}
