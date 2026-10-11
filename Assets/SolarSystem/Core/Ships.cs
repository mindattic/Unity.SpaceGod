using System;
using System.Collections.Generic;

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

        /// <summary>A small assault ship with a drill prow: it rams a hull and disgorges marines.</summary>
        Breacher,
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

        /// <summary>A flip-and-burn under the ship's own drive (a local hop, or to and from a hyperlane ramp).</summary>
        Transit,

        /// <summary>Riding a hyperlane between two planetary systems.</summary>
        Lane,

        /// <summary>Holding station just off a hyperlane, waiting in ambush (pirates).</summary>
        Lurk,

        /// <summary>Thrown out of a hyperlane by an interdiction pulse, drifting beside it.</summary>
        Adrift,
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
            new ShipClassSpec(ShipClass.Breacher, "Breacher", 0.18, 0.5, true),
        };

        public static ShipClassSpec Of(ShipClass c) => Specs[(int)c];
    }

    public sealed class Faction
    {
        public string Name { get; }
        public string Prefix { get; }
        public Rgb Color { get; }

        /// <summary>Pirates trade only at their havens and are hostile to everyone.</summary>
        public bool IsPirate { get; set; }

        /// <summary>A navy that fights wars and builds warships (not the independents).</summary>
        public bool IsMilitary { get; set; }

        /// <summary>How good its marines are, man for man (1 = average). Upgrades raise it.</summary>
        public double MarineQuality { get; set; } = 1;

        /// <summary>Credits from port taxes and its own ships' trade, spent on hulls, repairs and munitions.</summary>
        public double Treasury;

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

        /// <summary>
        /// A fixed point rather than an orbit (a hyperlane ramp): it holds its offset from the body and
        /// planning does not re-phase it to face the incoming ship.
        /// </summary>
        public bool Fixed;

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

        /// <summary>A one-line description of what is in the hold, for the HUD.</summary>
        public string Cargo;
        public int TripsCompleted;

        /// <summary>
        /// An interplanetary trip by hyperlane: the lanes still to ride (lane, from system, to system)
        /// and the body the trip ends at. Null for a local trip.
        /// </summary>
        public List<LaneHop> Journey;
        public int JourneyIndex;
        public int FinalBody = -1;

        /// <summary>While on a lane: which, and the fixed ramp offsets (km, ecliptic) from each end's planet.</summary>
        public LaneHop LaneLeg;
        public DVec3 LaneFromOffset, LaneToOffset;
        public double LaneLengthKm;

        /// <summary>Where along the lane this leg starts and stops (part-way, for a lurk or a resumed ride).</summary>
        public double LaneStartProgress, LaneEndProgress = 1;

        /// <summary>
        /// Off-lane station: the distance fraction along <see cref="LaneLeg"/> and the sideways offset
        /// (km) from the lane line, for a ship lurking in ambush or drifting after being pulled out.
        /// </summary>
        public double LaneHold;
        public DVec3 LaneHoldSide;

        /// <summary>A lurk planned for this trip: stop part-way along this lane and wait.</summary>
        public Lane LurkLane;
        public double LurkAt, LurkDays;

        /// <summary>Boarded and left adrift (cargo taken, crew gone): no longer flies, burns out of the list later.</summary>
        public bool Derelict;
        public double DerelictSinceJd;

        /// <summary>Hold size in tonnes (0 for warships).</summary>
        public double CapacityTonnes;
        public bool HasCargo;
        public Good CargoGood;
        public double CargoTonnes;

        /// <summary>What the cargo cost, and the settlement it was bought for.</summary>
        public double CargoCost;
        public Settlement CargoDestination;

        /// <summary>A supply-contract bonus the destination's owner pays on delivery, credits.</summary>
        public double CargoBonus;

        /// <summary>Running trade profit, credits.</summary>
        public double Credits;

        /// <summary>Lost in battle: kept in the list (ids are indices) but no longer simulated or drawn.</summary>
        public bool Destroyed;

        /// <summary>Hull, shields and hardpoints, carried between battles.</summary>
        public ShipCombat Combat;

        /// <summary>The battle this ship is fighting in, or null.</summary>
        public Battle Battle;

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
