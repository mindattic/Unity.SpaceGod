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

        /// <summary>A dedicated star fighter: twin forward guns, no cargo to speak of, built to escort
        /// capital ships and freighters and to dogfight -- distinct from the Courier/Breacher hulls,
        /// which double as light/heavy "fighters" but are really trade and assault craft that happen
        /// to fight well.</summary>
        Fighter,
    }

    public enum ShipRole
    {
        Trader,
        Miner,
        Courier,
        Patrol,
        Escort,

        /// <summary>
        /// An independent pilot (a yacht, a survey ship, a prospector) going where it pleases: out to
        /// open space near a world to drift a while, over to a neighbouring world, now and then further.
        /// </summary>
        Free,
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

        /// <summary>Waiting off a docking ring to be cleared down, edging in along the holding line.</summary>
        Queued,

        /// <summary>Dropping from the ring straight down to its city, through the atmosphere.</summary>
        Descent,

        /// <summary>Down at the city's port (not drawn; it trades here).</summary>
        Landed,

        /// <summary>Climbing back up to the ring to leave.</summary>
        Ascent,
    }

    public sealed class ShipClassSpec
    {
        public ShipClass Class { get; }
        public string DisplayName { get; }

        /// <summary>Sustained drive acceleration in g. Torch-drive fiction; the kinematics are real.</summary>
        public double AccelG { get; }

        public bool IsMilitary { get; }

        ShipClassSpec(ShipClass c, string name, double accelG, bool military)
        {
            Class = c;
            DisplayName = name;
            AccelG = accelG;
            IsMilitary = military;
        }

        static readonly ShipClassSpec[] Specs =
        {
            new ShipClassSpec(ShipClass.Carrier, "Carrier", 0.3, true),
            new ShipClassSpec(ShipClass.Battleship, "Battleship", 0.3, true),
            new ShipClassSpec(ShipClass.Cruiser, "Cruiser", 0.3, true),
            new ShipClassSpec(ShipClass.Destroyer, "Destroyer", 0.3, true),
            new ShipClassSpec(ShipClass.Freighter, "Freighter", 0.1, false),
            new ShipClassSpec(ShipClass.Miner, "Ore hauler", 0.06, false),
            new ShipClassSpec(ShipClass.Courier, "Courier", 1.2, false),
            new ShipClassSpec(ShipClass.Breacher, "Breacher", 0.5, true),
            new ShipClassSpec(ShipClass.Fighter, "Fighter", 1.0, true),
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

        /// <summary>Goods its law bans: a patrol scan that finds them in a hold confiscates them as contraband
        /// (stolen goods always are; see <see cref="Law.ContrabandIn"/>).</summary>
        public readonly HashSet<Good> Contraband = new HashSet<Good>();

        /// <summary>
        /// Trained spacers it can put aboard: new hulls and refits draw their crews and marines from it,
        /// recruiting tops it up, and crew brought home from escape pods or ransomed back return to it
        /// (see <see cref="Crews"/>). Infinite (never short) until a world musters real reserves.
        /// </summary>
        public double Manpower = double.PositiveInfinity;

        /// <summary>
        /// How its crews feel about the war, 1 the baseline: heavy losses wear it down (crews then strike
        /// their colours sooner, see <see cref="Morale.Resolve"/>), rescues lift it, and it recovers with time.
        /// </summary>
        public double CrewMorale = 1;

        /// <summary>Spacers it has lost for good (died aboard, in a pod or as hostages), and brought home from pods.</summary>
        public double CrewLost, CrewRescued;

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

        /// <summary>
        /// Centre of the circle, offset from the body along its spin axis: zero for an orbit; for a
        /// point fixed over a place on a turning world (a docking ring) the circle is its parallel of latitude.
        /// </summary>
        public DVec3 Center;

        public double PhaseAt(double jd) => PhaseAtEpoch + RateRadPerDay * (jd - EpochJd);

        public DVec3 OffsetAt(double jd)
        {
            double p = PhaseAt(jd);
            return Center + (U * Math.Cos(p) + V * Math.Sin(p)) * RadiusKm;
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

        /// <summary>This hull's own length — per-design (the mesh/stat variant this ship was built as), not per-class.</summary>
        public double LengthKm => Combat.Spec.LengthKm;

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

        /// <summary>The star this ride swings clear of and its curve's shape, fixed when it joined the lane.</summary>
        public int LaneStar = -1, LaneShape = -1;

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

        /// <summary>Who stripped it and left it adrift (raiders some of whom may still be aboard).</summary>
        public Faction DerelictBy;

        /// <summary>What a salvager still finds aboard a derelict: what the raiders left of the cargo, and
        /// the ship's own stores. The player takes it crate by crate (see <see cref="SalvageSession"/>).</summary>
        public readonly List<SalvageStack> Salvage = new List<SalvageStack>();

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

        /// <summary>The player's personal grid-based cargo hold (<see cref="CargoHold"/>) -- separate
        /// from the single-slot cargo fields above that the AI trade loop owns, and built only once
        /// asked for, since only the ship currently being piloted ever needs one.</summary>
        public CargoHold PersonalHold => personalHold ??= EmptyHold(Loadout == null ? 0 : Loadout.Count(ModuleKind.CargoRack) * Loadout.RackRows);
        CargoHold personalHold;

        CargoHold EmptyHold(int extraRows)
        {
            var spec = CargoHoldSpec.Of(Spec.Class);
            return new CargoHold(spec.Width, spec.Height + extraRows);
        }

        /// <summary>The grid hold rebuilt with <paramref name="extraRows"/> rows of racks (U.SPC-T65), every crate
        /// where it was (or, if its cells are gone, wherever it first fits); null if they will not all fit.</summary>
        internal CargoHold ResizedHold(int extraRows)
        {
            var hold = EmptyHold(extraRows);
            if (personalHold == null) return hold;
            var moved = new List<CargoItem>();
            foreach (var item in personalHold.Items)
                if (hold.Add(item.Good, item.X, item.Y, item.Rotated) == null) moved.Add(item);
            foreach (var item in moved)
                if (hold.AddFirstFit(item.Good) == null) return null;
            return hold;
        }

        internal void SetHold(CargoHold hold) => personalHold = hold;

        /// <summary>What a yard has fitted into this hull's slots (U.SPC-T65), or null while it is as built.</summary>
        public Loadout Loadout;

        /// <summary>Prisoners locked in the brig, and the bounty and ransom they will fetch handed over in port.</summary>
        public double Prisoners, PrisonerBounty;

        /// <summary>Running trade profit, credits.</summary>
        public double Credits;

        /// <summary>Running dark (U.SPC-T66): transponder off, active systems down, hull heat damped, so its signature
        /// is a fraction of what it was (see <see cref="Signatures"/>). A burn still shows.</summary>
        public bool SilentRunning;

        // ---------------------------------------------------------------- propulsion (see Propulsion)

        /// <summary>The design whose drive and tanks this hull has (its combat design, or its class's first).</summary>
        CombatSpec Design => Combat?.Spec ?? CombatSpec.Of(Spec.Class);

        /// <summary>Mass with empty tanks and an empty hold, tonnes.</summary>
        public double DryMassTonnes => Design.DryMassTonnes;

        /// <summary>What the tanks hold when full, tonnes of reaction mass.</summary>
        public double PropellantCapacity => Design.PropellantTonnes;

        /// <summary>The speed the drive throws reaction mass out at, km/s.</summary>
        public double ExhaustVelocityKmS => Math.Max(1, Design.ExhaustVelocityKmS);

        /// <summary>Reaction mass in the tanks, tonnes (a new hull leaves its yard full).</summary>
        public double Propellant
        {
            get => propellant ?? PropellantCapacity;
            set => propellant = Math.Max(0, Math.Min(PropellantCapacity, value));
        }
        double? propellant;

        /// <summary>How full the tanks are, 0..1.</summary>
        public double PropellantFraction => PropellantCapacity > 0 ? Propellant / PropellantCapacity : 0;

        /// <summary>Tonnes in the holds: the trade cargo and whatever is in the personal grid hold.</summary>
        public double CarriedTonnes
        {
            get
            {
                double t = HasCargo ? CargoTonnes : 0;
                if (personalHold != null) foreach (var item in personalHold.Items) t += item.Tonnes;
                return t;
            }
        }

        /// <summary>All-up mass now: hull, tanks and holds, tonnes.</summary>
        public double MassTonnes => DryMassTonnes + Propellant + CarriedTonnes;

        /// <summary>The delta-v (km/s) left in the tanks at the ship's present mass (Tsiolkovsky).</summary>
        public double DeltaVBudgetKmS => Propulsion.DeltaV(ExhaustVelocityKmS, MassTonnes, MassTonnes - Propellant);

        /// <summary>Ran dry in flight: drifting dead where the tanks gave out, calling for a tanker.</summary>
        public bool OutOfPropellant;
        public double OutOfPropellantSinceJd;

        /// <summary>The propellant the current transit was charged when it was planned (refunded in part on a diversion).</summary>
        public double LegPropellant;

        /// <summary>When the current transit runs dry part way (its tanks could not cover it all), or NaN.</summary>
        public double StrandJd = double.NaN;

        /// <summary>Lost in battle: kept in the list (ids are indices) but no longer simulated or drawn.</summary>
        public bool Destroyed;

        /// <summary>Set alongside <see cref="Destroyed"/> when a jump gate retires this hull here
        /// because it carried on into another system's fleet (see <see cref="GateTransit"/>) rather
        /// than because it was actually lost -- so the Runtime draws nothing for it instead of a wreck.</summary>
        public bool GateDeparted;

        /// <summary>The interstellar voyage this ship (a flagship, for a group) is on, or null; see <see cref="InterstellarTraffic"/>.</summary>
        public Voyage Voyage;

        /// <summary>
        /// Bound for one of this system's jump gates: the gate's fixed position from the system root
        /// (km). Its next trip is a burn to that point rather than to a world; null otherwise.
        /// </summary>
        public DVec3? GateKm;

        /// <summary>Hull, shields and hardpoints, carried between battles.</summary>
        public ShipCombat Combat;

        /// <summary>A free ship's next trip is a hop out to open space near the world it is at, not to another world.</summary>
        public bool Wander;

        /// <summary>The docking ring a merchant is landing through (or has landed through), and where it waits to be cleared.</summary>
        public DockingRing Ring;
        public ParkingOrbit HoldPoint;

        /// <summary>On the ground or on its way down or up (not in orbit, cannot be attacked).</summary>
        public bool Planetside => Phase == ShipPhase.Descent || Phase == ShipPhase.Landed || Phase == ShipPhase.Ascent;

        /// <summary>The battle this ship is fighting in, or null.</summary>
        public Battle Battle;

        public bool IsEscort => Leader != null;

        /// <summary>The ship whose state this ship shares (itself unless it is an escort).</summary>
        public Ship Controller => Leader ?? this;

        /// <summary>
        /// A copy for a jump gate to hand to the destination system's fleet (see <see cref="GateTransit"/>):
        /// everything that makes it feel like the same ship to the player -- name, hull class, combat
        /// state (hull, shields, hardpoints), cargo and the personal hold, credits, propellant -- but none of the
        /// origin system's scheduling state (orbit, route, journey), which the caller sets fresh for
        /// wherever the ship actually arrives. Nor the cargo's booking: that market (and the contract
        /// bonus it posted) belongs to the origin system's economy, so the goods arrive unbooked.
        /// </summary>
        internal Ship CloneForGateArrival() => new Ship
        {
            Name = Name,
            Spec = Spec,
            Faction = Faction,
            Combat = Combat,
            CapacityTonnes = CapacityTonnes,
            HasCargo = HasCargo,
            CargoGood = CargoGood,
            CargoTonnes = CargoTonnes,
            CargoCost = CargoCost,
            Cargo = Cargo,
            Credits = Credits,
            TripsCompleted = TripsCompleted,
            personalHold = personalHold,
            propellant = propellant,
            Loadout = Loadout?.Copy(),
            Prisoners = Prisoners,
            PrisonerBounty = PrisonerBounty,
            SilentRunning = SilentRunning,
        };

        public override string ToString() => Name;
    }

    /// <summary>Kinematic snapshot of a ship at an instant.</summary>
    public readonly struct ShipState
    {
        /// <summary>Heliocentric ecliptic position, km.</summary>
        public readonly DVec3 HelioKm;

        /// <summary>Where the nose points (ecliptic, unit) -- prograde throughout a transit, including
        /// its braking half (no visual 180 to burn retrograde; see <see cref="Fleet.BaseStateOf"/>).</summary>
        public readonly DVec3 Facing;

        public readonly double SpeedKmS;
        public readonly bool Burning;

        /// <summary>Fraction of the transit distance covered (0..1); 0 while parked.</summary>
        public readonly double Progress;

        /// <summary>Entry heating, 0..1: the plasma sheath of a ship descending through an atmosphere.</summary>
        public readonly float Heat;

        public ShipState(DVec3 helio, DVec3 facing, double speed, bool burning, double progress, float heat = 0)
        {
            HelioKm = helio;
            Facing = facing;
            SpeedKmS = speed;
            Burning = burning;
            Progress = progress;
            Heat = heat;
        }
    }
}
