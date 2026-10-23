using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A group (flagship first, then its escorts) crossing open space between the stars under its own cruise drive
    /// (U.SPC-T412, see <see cref="CruiseDriveSpec"/>). Like a ship inside a gate it belongs to no system's fleet while it
    /// is out here (its hulls in the system it left were retired and these are their copies, see
    /// <see cref="GateTransit"/> for why), but unlike a gate passage it is somewhere the whole way: a real place in the
    /// galaxy (<see cref="PositionAt"/>), moving at a real speed, where it can meet other groups and find what lies out
    /// here, and from where it flies into the far system at the edge of its star's well.
    /// </summary>
    public sealed class Cruise
    {
        /// <summary>Flagship first, then its escorts.</summary>
        public readonly List<Ship> Ships = new List<Ship>();

        /// <summary>Which of <see cref="Ships"/> were hired fighters in the system it left (so the far system's escorts take them on).</summary>
        public readonly HashSet<Ship> Hired = new HashSet<Ship>();

        /// <summary>The system it set out from.</summary>
        public StarSystem From;

        /// <summary>The system it is bound for, or null when it is bound for a star nobody simulates (see <see cref="ToName"/>).</summary>
        public StarSystem To;

        /// <summary>The name of where it is bound: the system's, or the plain star's.</summary>
        public string ToName;

        /// <summary>The drive it flies on (the flagship's).</summary>
        public CruiseDriveSpec Drive;

        /// <summary>Where it will drop out: the edge of the far star's well, on the side it comes in from.</summary>
        public GalacticPosition Target;

        /// <summary>This leg: where it began, where it ends (the <see cref="Target"/>), when its charge began, and its speed through it.</summary>
        public GalacticPosition Start, End;
        public double LegJd;
        public CruiseProfile Profile;

        /// <summary>Dropped out in open space (to fight, to salvage, or at a plain star it was bound for), until <see cref="HoldUntilJd"/>
        /// (infinite: until it is given somewhere else to go, see <see cref="DeepSpace.Retarget"/>).</summary>
        public bool Holding;
        public double HoldUntilJd;

        /// <summary>When it first engaged, from its home system.</summary>
        public double SetOutJd;

        /// <summary>The news it carries from the system it left (U.SPC-T100), handed over where it arrives.</summary>
        public List<SystemReport> News;

        public Ship Leader => Ships[0];

        /// <summary>When this leg ends (it drops out at its <see cref="End"/>); infinite while it holds.</summary>
        public double ArriveJd => Holding ? double.PositiveInfinity : LegJd + Profile.TotalDays;

        /// <summary>The way this leg heads, ecliptic (unit).</summary>
        public DVec3 Direction => End.KmFrom(Start).Normalized;

        /// <summary>Under way at <paramref name="jd"/>: past its charge and not yet dropped out.</summary>
        public bool Engaged(double jd) => !Holding && jd >= LegJd + Profile.SpoolDays && jd < ArriveJd;

        /// <summary>Where it is at <paramref name="jd"/>: on its leg's line, by its speed profile, from where it began.</summary>
        public GalacticPosition PositionAt(double jd)
        {
            if (Holding || jd <= LegJd + Profile.SpoolDays) return Start;
            if (jd >= ArriveJd) return End;
            return Start.Offset(Direction * (Profile.LyAt(jd - LegJd) * Astro.LightYearKm));
        }

        /// <summary>Speed at <paramref name="jd"/>, km/s.</summary>
        public double SpeedKmS(double jd) =>
            Holding ? 0 : Profile.SpeedAt(jd - LegJd) * Astro.LightYearKm / Astro.SecondsPerDay;

        /// <summary>Light-years still to go to its <see cref="Target"/>.</summary>
        public double LyToGo(double jd) => Target.DistanceLy(PositionAt(jd));

        /// <summary>Sets a new leg from <paramref name="from"/> to <see cref="Target"/>, its charge starting at <paramref name="jd"/>.</summary>
        internal void SetLeg(GalacticPosition from, double jd)
        {
            Holding = false;
            HoldUntilJd = double.NaN;
            Start = from;
            End = Target;
            LegJd = jd;
            Profile = CruiseProfile.For(Drive, Target.DistanceLy(from));
        }

        public override string ToString() => Leader + " bound for " + ToName;
    }

    /// <summary>What lies out between the stars for a passing group to find.</summary>
    public enum DeepSpaceObjectKind
    {
        /// <summary>What is left of a hull destroyed out here.</summary>
        Wreck,

        /// <summary>A hull left drifting whole, its crew gone.</summary>
        Derelict,
    }

    /// <summary>Something in open space between the stars (U.SPC-T412): a wreck or a derelict, where it was left, and what is aboard.</summary>
    public sealed class DeepSpaceObject
    {
        public DeepSpaceObjectKind Kind;
        public string Name;
        public GalacticPosition Position;

        /// <summary>What a salvager takes off it, credits.</summary>
        public double SalvageCredits;

        /// <summary>Whose hull it was.</summary>
        public Faction LostBy;

        /// <summary>When it was left out here.</summary>
        public double SinceJd;

        public override string ToString() => Name;
    }

    /// <summary>A line of what has happened out between the stars, for the logs and the HUD.</summary>
    public sealed class DeepSpaceEvent
    {
        public double Jd;
        public string Text;
        public GalacticPosition Where;
    }

    /// <summary>
    /// Something that can happen to the groups out between the stars (U.SPC-T412): asked once a <see cref="DeepSpace.StepDays"/>
    /// step for what happened in it. A new kind of deep-space encounter plugs in by registering a factory in
    /// <see cref="DeepSpace.EncounterKinds"/>, never by editing the loop that runs them.
    /// </summary>
    public interface IDeepSpaceEncounter
    {
        void Step(DeepSpace space, double fromJd, double toJd, Random rng);
    }

    /// <summary>
    /// The open space between the star systems (U.SPC-T412): the groups crossing it under their own cruise drives
    /// (<see cref="Cruise"/>), the wrecks and derelicts left out here, and what happens when they meet. It is not empty
    /// and it does not stop: every step the groups fly on, the encounters (<see cref="IDeepSpaceEncounter"/>) are run,
    /// and a group at the end of its way drops out at the edge of its star's well and flies into that system as any
    /// ship there would, from the far side of the star it came from. A group bound for a star nobody simulates drops
    /// out there and holds in open space until it is sent on (<see cref="ReachedStar"/>, <see cref="Retarget"/>).
    ///
    /// Positions are <see cref="GalacticPosition"/>s, sector and offset, so a group a long way from Sol is placed as
    /// exactly as one next to it.
    /// </summary>
    public sealed class DeepSpace
    {
        readonly Galaxy galaxy;
        readonly Random rng;
        readonly List<Cruise> cruises = new List<Cruise>();
        readonly List<DeepSpaceObject> objects = new List<DeepSpaceObject>();
        readonly List<DeepSpaceEvent> events = new List<DeepSpaceEvent>();
        double nextMarkJd = double.NaN;

        /// <summary>The groups out between the stars now, under way or holding.</summary>
        public IReadOnlyList<Cruise> Cruises => cruises;

        /// <summary>The wrecks and derelicts out between the stars.</summary>
        public IReadOnlyList<DeepSpaceObject> Objects => objects;

        /// <summary>What has happened out here lately, oldest first (the last <see cref="MaxEvents"/>).</summary>
        public IReadOnlyList<DeepSpaceEvent> Events => events;

        /// <summary>Every hull out between the stars now (in no system's fleet until it drops out into one).</summary>
        public IEnumerable<Ship> InFlight()
        {
            foreach (var c in cruises)
                foreach (var s in c.Ships)
                    yield return s;
        }

        /// <summary>The encounters run every step: one of each kind in <see cref="EncounterKinds"/>, made when the deep space was.</summary>
        public readonly List<IDeepSpaceEncounter> Encounters = new List<IDeepSpaceEncounter>();

        /// <summary>The kinds of encounter every new deep space runs (the extension point: add a factory to add a kind).</summary>
        public static readonly List<Func<IDeepSpaceEncounter>> EncounterKinds = new List<Func<IDeepSpaceEncounter>>
        {
            () => new CruiseMeeting(),
            () => new WreckSalvage(),
        };

        /// <summary>How often (days) the encounters are run: about every hour and a quarter, on a fixed grid, so what
        /// happens never depends on the frame rate.</summary>
        public const double StepDays = 0.05;

        /// <summary>How many lines of <see cref="Events"/> are kept.</summary>
        public const int MaxEvents = 200;

        /// <summary>The most wrecks and derelicts kept: past it, the oldest are lost to the dark.</summary>
        public const int MaxObjects = 400;

        /// <summary>Raised when a group drops out into a simulated system and is in its fleet: (the group, when). The traffic takes it from there.</summary>
        public event Action<Cruise, double> Arrived;

        /// <summary>Raised when a group drops out at a plain star it was bound for and holds there: (the group, when).</summary>
        public event Action<Cruise, double> ReachedStar;

        public DeepSpace(Galaxy galaxy, int seed = 4127)
        {
            this.galaxy = galaxy;
            rng = new Random(seed);
            foreach (var make in EncounterKinds) Encounters.Add(make());
        }

        // ---------------------------------------------------------------- setting out

        /// <summary>
        /// Where a ship in <paramref name="from"/> engages its drive for <paramref name="target"/>, km from the system's root:
        /// at the edge of the star's well, on the line to the target.
        /// </summary>
        public static DVec3 DeparturePointKm(StarSystem from, GalacticPosition target)
        {
            var way = target.KmFrom(from.Origin);
            var dir = way.LengthSquared > 0 ? way.Normalized : DVec3.UnitX;
            return dir * CruiseDrives.WellLimitKm(from);
        }

        /// <summary>Where a group from <paramref name="from"/> drops out at <paramref name="star"/>: the edge of its well
        /// (<paramref name="wellKm"/> out), on the side facing the way it comes in.</summary>
        public static GalacticPosition ArrivalPoint(GalacticPosition star, GalacticPosition from, double wellKm)
        {
            var back = from.KmFrom(star);
            var dir = back.LengthSquared > 0 ? back.Normalized : DVec3.UnitX;
            return star.Offset(dir * wellKm);
        }

        /// <summary>
        /// Puts <paramref name="ships"/> (copies, flagship first, already out of every fleet) into open space at
        /// <paramref name="fromKm"/> in <paramref name="from"/> (km from its root), bound for <paramref name="to"/> on
        /// <paramref name="drive"/>; it charges from <paramref name="jd"/>.
        /// </summary>
        public Cruise Launch(IEnumerable<Ship> ships, StarSystem from, DVec3 fromKm, StarSystem to, CruiseDriveSpec drive, double jd,
            List<SystemReport> news = null)
        {
            var start = from.At(fromKm);
            return Launch(ships, from, start, to, to.Name, ArrivalPoint(to.Origin, start, CruiseDrives.WellLimitKm(to)), drive, jd, news);
        }

        /// <summary>As <see cref="Launch(IEnumerable{Ship}, StarSystem, DVec3, StarSystem, CruiseDriveSpec, double, List{SystemReport})"/>, but bound
        /// for a plain star (one nobody simulates yet): it drops out at the edge of its well and holds there.</summary>
        public Cruise LaunchToStar(IEnumerable<Ship> ships, StarSystem from, DVec3 fromKm, NearbyStar star, CruiseDriveSpec drive, double jd,
            List<SystemReport> news = null)
        {
            var start = from.At(fromKm);
            return Launch(ships, from, start, null, star.Name, ArrivalPoint(star.Origin, start, CruiseDrives.WellLimitKm(null)), drive, jd, news);
        }

        Cruise Launch(IEnumerable<Ship> ships, StarSystem from, GalacticPosition start, StarSystem to, string toName, GalacticPosition target,
            CruiseDriveSpec drive, double jd, List<SystemReport> news)
        {
            if (drive == null) throw new ArgumentNullException(nameof(drive), "A cruise needs a drive");
            var c = new Cruise { From = from, To = to, ToName = toName, Drive = drive, Target = target, SetOutJd = jd, News = news };
            c.Ships.AddRange(ships);
            if (c.Ships.Count == 0) throw new ArgumentException("A cruise needs a ship", nameof(ships));
            foreach (var s in c.Ships)
            {
                s.Destroyed = false;
                s.GateDeparted = false;
            }
            c.SetLeg(start, jd);
            cruises.Add(c);
            Log(jd, $"{c.Leader.Name}{More(c)} engage{(c.Ships.Count > 1 ? "" : "s")} {drive.Name.ToLowerInvariant()}s out of {from.Name} for {toName}, " +
                    $"{c.Profile.Ly.ToString("0.00", CultureInfo.InvariantCulture)} ly, due in {c.Profile.TotalDays.ToString("0.0", CultureInfo.InvariantCulture)} days", start);
            return c;
        }

        static string More(Cruise c) => c.Ships.Count > 1 ? $" and {c.Ships.Count - 1} more" : "";

        /// <summary>Sends <paramref name="c"/> on to <paramref name="to"/> from where it is, its charge beginning at <paramref name="jd"/>.</summary>
        public void Retarget(Cruise c, StarSystem to, double jd)
        {
            var here = c.PositionAt(jd);
            c.To = to;
            c.ToName = to.Name;
            c.Target = ArrivalPoint(to.Origin, here, CruiseDrives.WellLimitKm(to));
            c.SetLeg(here, jd);
            Log(jd, $"{c.Leader.Name}{More(c)} set{(c.Ships.Count > 1 ? "" : "s")} out from open space for {to.Name}, " +
                    $"{c.Profile.Ly.ToString("0.00", CultureInfo.InvariantCulture)} ly", here);
        }

        /// <summary>Drops <paramref name="c"/> out of its cruise where it is at <paramref name="jd"/>, for <paramref name="days"/> (then on
        /// toward its target again, charging first), or for good with an infinite hold.</summary>
        public void DropOut(Cruise c, double jd, double days)
        {
            var here = c.PositionAt(jd);
            c.Start = c.End = here;
            c.Holding = true;
            c.HoldUntilJd = jd + days;
        }

        // ---------------------------------------------------------------- the step

        /// <summary>
        /// Everything out between the stars to <paramref name="jd"/>: groups holding go on when their hold is up, groups due
        /// drop out at their stars (at the moment each is due, in that order), and the encounters run on the fixed grid of
        /// <see cref="StepDays"/>.
        /// </summary>
        public void Update(double jd)
        {
            if (double.IsNaN(jd)) return;
            if (double.IsNaN(nextMarkJd) || cruises.Count == 0) nextMarkJd = Mark(jd) + StepDays;
            while (nextMarkJd <= jd)
            {
                double mark = nextMarkJd;
                Due(mark);
                foreach (var e in Encounters) e.Step(this, mark - StepDays, mark, rng);
                nextMarkJd += StepDays;
            }
            Due(jd);
        }

        static double Mark(double jd) => Math.Floor(jd / StepDays + 1e-9) * StepDays;

        /// <summary>Holds that are up, and arrivals due, by <paramref name="jd"/>, in the order they fall.</summary>
        void Due(double jd)
        {
            while (true)
            {
                Cruise next = null;
                double when = double.PositiveInfinity;
                foreach (var c in cruises)
                {
                    double t = c.Holding ? c.HoldUntilJd : c.ArriveJd;
                    if (t <= jd && t < when)
                    {
                        when = t;
                        next = c;
                    }
                }
                if (next == null) return;
                if (next.Holding) next.SetLeg(next.Start, when);
                else Drop(next, when);
            }
        }

        /// <summary>At the end of its way: into the far system's fleet, flying in from the well's edge, or holding at a plain star.</summary>
        void Drop(Cruise c, double jd)
        {
            if (c.To == null)
            {
                c.Start = c.End = c.Target;
                c.Holding = true;
                c.HoldUntilJd = double.PositiveInfinity;
                Log(jd, $"{c.Leader.Name}{More(c)} drop{(c.Ships.Count > 1 ? "" : "s")} out at {c.ToName}", c.Target);
                ReachedStar?.Invoke(c, jd);
                return;
            }
            cruises.Remove(c);
            var system = c.To;
            var world = system.World;
            var fleet = world.Fleet;
            var at = system.LocalKm(c.Target);
            // Facing in, toward the star, as it comes out of its cruise.
            var forward = at.LengthSquared > 0 ? (-at).Normalized : DVec3.UnitX;
            var up = Math.Abs(forward.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX;
            var right = DVec3.Cross(forward, up).Normalized;
            foreach (var s in c.Ships)
            {
                s.Orbit = new ParkingOrbit
                {
                    Body = system.Catalog.Root.Index, RadiusKm = 0, Center = at, U = right, V = forward,
                    EpochJd = jd, PhaseAtEpoch = 0, RateRadPerDay = 0, Fixed = true,
                };
                s.Phase = ShipPhase.Parked;
                s.PhaseStartJd = jd;
                s.PhaseEndJd = jd;
                s.DestinationBody = s.Orbit.Body;
                s.Ports = world.FreeShipPorts();
                s.GateKm = null;
                fleet.Receive(s);
                // A fresh id here: the group keeps its own identity from ids this fleet handed out (as at a gate, U.SPC-T363).
                s.BandId = s.Leader == null ? s.Id : s.Leader.BandId;
                if (c.Hired.Contains(s)) world.Escorts?.MarkHired(s);
            }
            world.Log(jd, $"{c.Leader.Name}{More(c)} of {c.Leader.Faction.Name} drop{(c.Ships.Count > 1 ? "" : "s")} out of cruise at the edge of {system.Name}, " +
                          $"in from {c.From?.Name ?? "open space"}", WorldEventKind.Info, system.Catalog.Root.Index);
            Arrived?.Invoke(c, jd);
            // Nobody sent it here on business: a free ship, it finds its own way in.
            var leader = c.Leader;
            if (leader.Voyage == null && !leader.Destroyed)
            {
                leader.Role = leader.Role == ShipRole.Escort ? ShipRole.Free : leader.Role;
                fleet.ScheduleNext(leader, jd);
            }
            foreach (var s in c.Ships) world.NotifyAdded(s);
        }

        // ---------------------------------------------------------------- what happens out here

        /// <summary>Notes <paramref name="text"/> in the deep-space log.</summary>
        public void Log(double jd, string text, GalacticPosition where)
        {
            events.Add(new DeepSpaceEvent { Jd = jd, Text = text, Where = where });
            if (events.Count > MaxEvents) events.RemoveRange(0, events.Count - MaxEvents);
        }

        /// <summary>Leaves <paramref name="o"/> out here (the oldest lost when there are too many).</summary>
        public void Add(DeepSpaceObject o)
        {
            objects.Add(o);
            if (objects.Count > MaxObjects) objects.RemoveRange(0, objects.Count - MaxObjects);
        }

        /// <summary>Takes <paramref name="o"/> away (salvaged).</summary>
        public void Remove(DeepSpaceObject o) => objects.Remove(o);

        /// <summary>Whether <paramref name="a"/> and <paramref name="b"/> would fight on sight (the one diplomacy table every system shares).</summary>
        public bool Hostile(Faction a, Faction b) => galaxy.Polities.Diplomacy.Hostile(a, b);

        /// <summary>
        /// Destroys <paramref name="ship"/> of <paramref name="c"/> out here at <paramref name="jd"/>, leaving its wreck: the
        /// next hull takes the lead (and the voyage) if it was the flagship, and the group is gone when none is left.
        /// </summary>
        public void Destroy(Cruise c, Ship ship, double jd, string how)
        {
            int i = c.Ships.IndexOf(ship);
            if (i < 0) return;
            var where = c.PositionAt(jd);
            c.Ships.RemoveAt(i);
            c.Hired.Remove(ship);
            ship.Destroyed = true;
            if (ship.Combat != null) ship.Combat.Hull = 0;
            Add(new DeepSpaceObject
            {
                Kind = DeepSpaceObjectKind.Wreck, Name = "the wreck of the " + ship.Name, Position = where.Offset(new DVec3(i * 3, 0, 0)),
                SalvageCredits = WreckValue(ship), LostBy = ship.Faction, SinceJd = jd,
            });
            Log(jd, $"{ship.Name} of {ship.Faction.Name} is destroyed in open space {how}", where);
            if (c.Ships.Count == 0)
            {
                cruises.Remove(c);
                return;
            }
            if (i == 0)
            {
                // The next hull takes command, and what the group was out here to do.
                var heir = c.Ships[0];
                heir.Leader = null;
                heir.Voyage = ship.Voyage;
                heir.Role = ship.Role;
                heir.Ports = ship.Ports;
                foreach (var s in c.Ships)
                    if (s != heir) s.Leader = heir;
                ship.Voyage = null;
            }
        }

        /// <summary>What a salvager gets off a wreck: some of its cargo's worth, and its parts.</summary>
        public static double WreckValue(Ship ship) =>
            WreckPartsCredits * (ship.Spec != null && ship.Spec.IsMilitary ? 3 : 1) + (ship.HasCargo ? Math.Max(0, ship.CargoCost) * 0.4 : 0);

        /// <summary>What a wreck's parts are worth to a salvager, credits (three times as much for a warship's).</summary>
        public const double WreckPartsCredits = 40000;

        /// <summary>
        /// How close (km) the paths of <paramref name="a"/> and <paramref name="b"/> come between <paramref name="fromJd"/> and
        /// <paramref name="toJd"/>: the nearest point of their separation, taken as straight over so short a step.
        /// </summary>
        public static double ClosestKm(Cruise a, Cruise b, double fromJd, double toJd) =>
            Closest(a.PositionAt(fromJd).KmFrom(b.PositionAt(fromJd)), a.PositionAt(toJd).KmFrom(b.PositionAt(toJd)));

        /// <summary>How close (km) <paramref name="c"/> passes <paramref name="place"/> between <paramref name="fromJd"/> and <paramref name="toJd"/>.</summary>
        public static double ClosestKm(Cruise c, GalacticPosition place, double fromJd, double toJd) =>
            Closest(c.PositionAt(fromJd).KmFrom(place), c.PositionAt(toJd).KmFrom(place));

        static double Closest(DVec3 p0, DVec3 p1)
        {
            var d = p1 - p0;
            double len2 = d.LengthSquared;
            double t = len2 > 0 ? Math.Max(0, Math.Min(1, -DVec3.Dot(p0, d) / len2)) : 0;
            return (p0 + d * t).Length;
        }

        /// <summary>A group's fighting strength out here: its hulls' together.</summary>
        public static double Strength(Cruise c)
        {
            double s = 0;
            foreach (var ship in c.Ships)
                if (!ship.Destroyed && ship.Combat != null) s += ship.Combat.Strength;
            return s;
        }
    }

    /// <summary>
    /// Hostile groups whose paths cross between the stars (U.SPC-T412): a navy and its enemy's ships, or raiders and a
    /// hull worth their while, within <see cref="RangeKm"/> of each other. The stronger hand may pull the other out of
    /// cruise and fight. Raiders who win rob the hold and let the hull go (U.SPC-T374: a hull is worth more taken than
    /// shot apart); a navy that wins leaves the beaten group wrecked out here. The loser's escorts are lost with it.
    /// Either way the winners hold a while (the fight, the boarding) and then go on.
    /// </summary>
    public sealed class CruiseMeeting : IDeepSpaceEncounter
    {
        /// <summary>How near (km) two groups' paths must come for either to pull the other out of cruise: about a light-hour.</summary>
        public const double RangeKm = 1e9;

        /// <summary>How long a fight out here keeps the winners from going on, days.</summary>
        public const double FightDays = 0.25;

        /// <summary>A hand must be at least this share as strong as the other to start a fight.</summary>
        public const double NerveRatio = 0.5;

        public void Step(DeepSpace space, double fromJd, double toJd, Random rng)
        {
            if (space.Cruises.Count < 2) return;
            // Over the groups as they stood at the start of the step: a fight may take one out of the list.
            var list = new List<Cruise>(space.Cruises);
            var done = new HashSet<Cruise>();
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    var a = list[i];
                    var b = list[j];
                    // One fight a step for any group.
                    if (done.Contains(a) || done.Contains(b) || a.Ships.Count == 0 || b.Ships.Count == 0) continue;
                    if (!(a.Engaged(toJd) || a.Engaged(fromJd)) && !(b.Engaged(toJd) || b.Engaged(fromJd))) continue;
                    if (!space.Hostile(a.Leader.Faction, b.Leader.Faction)) continue;
                    if (DeepSpace.ClosestKm(a, b, fromJd, toJd) > RangeKm) continue;
                    if (!Resolve(space, a, b, toJd, rng)) continue;
                    done.Add(a);
                    done.Add(b);
                }
        }

        static bool Aggressive(Ship leader) => leader.Faction.IsPirate || leader.Spec.IsMilitary;

        static bool Loot(Cruise c)
        {
            foreach (var s in c.Ships)
                if (s.HasCargo || s.PlunderTonnes > 0) return true;
            return false;
        }

        /// <summary>Whether a fight happens between <paramref name="a"/> and <paramref name="b"/>, and its outcome.</summary>
        static bool Resolve(DeepSpace space, Cruise a, Cruise b, double jd, Random rng)
        {
            double sa = DeepSpace.Strength(a), sb = DeepSpace.Strength(b);
            // Who would start it: one with guns and the will; of two, the stronger.
            bool aWill = Aggressive(a.Leader) && sa > 0 && sa >= sb * NerveRatio && (!a.Leader.Faction.IsPirate || Loot(b));
            bool bWill = Aggressive(b.Leader) && sb > 0 && sb >= sa * NerveRatio && (!b.Leader.Faction.IsPirate || Loot(a));
            if (!aWill && !bWill) return false;
            var attacker = aWill && (!bWill || sa >= sb) ? a : b;
            var defender = attacker == a ? b : a;
            double sAtt = attacker == a ? sa : sb, sDef = attacker == a ? sb : sa;
            double win = sDef <= 0 ? 1 : sAtt / (sAtt + sDef);
            bool attackerWins = rng.NextDouble() < win;
            var where = attacker.PositionAt(jd);
            space.Log(jd, $"{attacker.Leader.Name} of {attacker.Leader.Faction.Name} pulls {defender.Leader.Name} of {defender.Leader.Faction.Name} out of cruise", where);
            var winner = attackerWins ? attacker : defender;
            var loser = attackerWins ? defender : attacker;
            double sLoser = attackerWins ? sDef : sAtt, sWinner = attackerWins ? sAtt : sDef;
            // The winners pay for it.
            double hurt = sLoser + sWinner > 0 ? 0.35 * sLoser / (sLoser + sWinner) : 0;
            foreach (var s in winner.Ships)
                if (s.Combat != null)
                    s.Combat.Hull = Math.Max(Math.Min(s.Combat.Hull, s.Combat.Spec.Hull * 0.05), s.Combat.Hull - s.Combat.Spec.Hull * hurt);
            if (attackerWins && attacker.Leader.Faction.IsPirate && defender.Leader.Faction != null && !defender.Leader.Spec.IsMilitary)
            {
                // Raiders rob a hull they have run down rather than destroy it.
                double tonnes = 0;
                var robber = attacker.Leader;
                robber.Plunder ??= new double[GoodInfo.Count];
                foreach (var s in defender.Ships)
                {
                    if (s.HasCargo)
                    {
                        robber.Plunder[(int)s.CargoGood] += s.CargoTonnes;
                        tonnes += s.CargoTonnes;
                        s.HasCargo = false;
                        s.CargoTonnes = 0;
                        s.CargoCost = 0;
                        s.Cargo = null;
                    }
                    if (s.Plunder != null)
                        for (int g = 0; g < s.Plunder.Length && g < robber.Plunder.Length; g++)
                        {
                            robber.Plunder[g] += s.Plunder[g];
                            tonnes += s.Plunder[g];
                            s.Plunder[g] = 0;
                        }
                }
                space.Log(jd, $"{robber.Name}'s raiders rob {defender.Leader.Name} of {tonnes.ToString("N0", CultureInfo.InvariantCulture)} t and let it go", where);
                space.DropOut(defender, jd, FightDays);
            }
            else
            {
                foreach (var s in new List<Ship>(loser.Ships)) space.Destroy(loser, s, jd, $"by {winner.Leader.Faction.Name}");
            }
            if (winner.Ships.Count > 0) space.DropOut(winner, jd, FightDays);
            return true;
        }
    }

    /// <summary>
    /// A group passing within <see cref="RangeKm"/> of a wreck or derelict (U.SPC-T412) drops out to strip it, keeps what
    /// it finds (the flagship's purse and its flag's treasury), and goes on after <see cref="SalvageDays"/>. A war fleet
    /// on campaign does not stop for it.
    /// </summary>
    public sealed class WreckSalvage : IDeepSpaceEncounter
    {
        /// <summary>How near (km) a group must pass for its sensors to pick out a wreck: about a light-hour.</summary>
        public const double RangeKm = 1e9;

        /// <summary>How long stripping a wreck takes, days.</summary>
        public const double SalvageDays = 0.5;

        public void Step(DeepSpace space, double fromJd, double toJd, Random rng)
        {
            if (space.Objects.Count == 0) return;
            foreach (var c in new List<Cruise>(space.Cruises))
            {
                if (c.Ships.Count == 0 || !(c.Engaged(toJd) || c.Engaged(fromJd))) continue;
                var leader = c.Leader;
                if (leader.Voyage?.Purpose == VoyagePurpose.Campaign) continue;
                foreach (var o in space.Objects)
                {
                    if (o.SinceJd >= toJd || DeepSpace.ClosestKm(c, o.Position, fromJd, toJd) > RangeKm) continue;
                    leader.Credits += o.SalvageCredits;
                    leader.Faction.Treasury += o.SalvageCredits;
                    space.Remove(o);
                    space.Log(toJd, $"{leader.Name} of {leader.Faction.Name} drops out to strip {o.Name}: " +
                                    $"{o.SalvageCredits.ToString("N0", CultureInfo.InvariantCulture)} cr of salvage", o.Position);
                    space.DropOut(c, toJd, SalvageDays);
                    break;
                }
            }
        }
    }
}
