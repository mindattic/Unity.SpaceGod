using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A kind of asteroid a capture tug can bring in, and what grinding and separating one yields (U.SPC-T330).
    /// The shares are the belt's rough mix by spectral class; the yields are assumed bulk fractions of each element
    /// by mass (carbonaceous rock is a tenth water and a few percent organics, a stony one a fifth free metal, a
    /// metallic one nearly all nickel-iron with platinum-group metals in the tens of parts per million). Everything
    /// not listed is slag. New kinds plug in through <see cref="Register"/>; nothing that runs the pipeline needs
    /// to change.
    /// </summary>
    public sealed class AsteroidClass
    {
        public string Name { get; }

        /// <summary>How often a tug finds one, relative to the other kinds.</summary>
        public double Share { get; }

        /// <summary>Bulk density, tonnes per cubic metre (rubble piles included).</summary>
        public double Density { get; }

        /// <summary>Mass fraction of each element the separators can recover.</summary>
        public (Good good, double fraction)[] Yield { get; }

        public AsteroidClass(string name, double share, double density, params (Good, double)[] yield)
        {
            Name = name;
            Share = share;
            Density = density;
            Yield = yield;
        }

        public static readonly AsteroidClass Carbonaceous = new AsteroidClass("C-type", 0.6, 1.4,
            (Good.Water, 0.12), (Good.Hydrocarbons, 0.04), (Good.Nitrogen, 0.002), (Good.NickelIron, 0.12),
            (Good.Titanium, 0.001), (Good.Copper, 0.0001), (Good.Zinc, 0.0001), (Good.RareEarths, 0.00002), (Good.Platinum, 0.000002));

        public static readonly AsteroidClass Stony = new AsteroidClass("S-type", 0.3, 2.7,
            (Good.NickelIron, 0.20), (Good.Titanium, 0.004), (Good.Copper, 0.0002), (Good.Zinc, 0.0002),
            (Good.RareEarths, 0.00005), (Good.Platinum, 0.000005));

        public static readonly AsteroidClass Metallic = new AsteroidClass("M-type", 0.1, 5.0,
            (Good.NickelIron, 0.88), (Good.Copper, 0.0005), (Good.Zinc, 0.0003), (Good.RareEarths, 0.00001), (Good.Platinum, 0.00003));

        /// <summary>
        /// A block of comet ice (U.SPC-T483), cut from an icy body or a dead comet and pushed in by an ore tug: mostly water,
        /// with frozen ammonia and organics, light and loose. Not one of the belt rocks <see cref="Pick"/> draws: only the
        /// tugs bring ice, an operation's <see cref="MiningOperation.TugIceShare"/> of their loads.
        /// </summary>
        public static readonly AsteroidClass CometIce = new AsteroidClass("Comet ice", 0, 0.6,
            (Good.Water, 0.8), (Good.Hydrocarbons, 0.06), (Good.Nitrogen, 0.03));

        static readonly List<AsteroidClass> registered = new List<AsteroidClass> { Carbonaceous, Stony, Metallic };

        public static IReadOnlyList<AsteroidClass> All => registered;

        /// <summary>Adds a kind of rock to what the tugs can find.</summary>
        public static void Register(AsteroidClass kind)
        {
            if (kind != null && !registered.Contains(kind)) registered.Add(kind);
        }

        /// <summary>One kind, drawn by <see cref="Share"/>.</summary>
        public static AsteroidClass Pick(Random rng)
        {
            double total = 0;
            foreach (var k in registered) total += k.Share;
            double r = rng.NextDouble() * total;
            foreach (var k in registered)
            {
                r -= k.Share;
                if (r < 0) return k;
            }
            return registered[registered.Count - 1];
        }
    }

    /// <summary>An asteroid a tug has brought in to a refinery, waiting for (or going through) the grinders.</summary>
    public sealed class CapturedRock
    {
        public string Designation;
        public AsteroidClass Class;
        public double DiameterM;
        public double Tonnes;

        /// <summary>Tonnes not yet ground.</summary>
        public double Remaining;

        public double CapturedJd;
    }

    /// <summary>
    /// A depot that holds separated elements for its owner's builds: a cache on a remote moon, out of the way and lightly
    /// guarded. Raided now and then (see <see cref="BeltMining.WarehouseRaidPerDay"/>), but far less often than the
    /// convoys that fill it are robbed on the way.
    /// </summary>
    public sealed class MaterialWarehouse
    {
        public string Name;
        public Faction Owner;
        public int Body;
        public double CapacityTonnes = 50e6;
        public readonly double[] Stock = new double[GoodInfo.Count];

        public int Raids;
        public double TonnesLostToRaids;

        public double StoredTonnes
        {
            get
            {
                double t = 0;
                foreach (var s in Stock) t += s;
                return t;
            }
        }

        public override string ToString() => Name;
    }

    public enum ConvoyState
    {
        EnRoute,
        Delivered,

        /// <summary>Taken by raiders on the way: the cargo is theirs.</summary>
        Stolen,
    }

    /// <summary>A raw-material convoy: one load of separated elements from a refinery to a warehouse.</summary>
    public sealed class MaterialConvoy
    {
        public string Name;
        public MiningOperation From;
        public MaterialWarehouse To;
        public readonly double[] Cargo = new double[GoodInfo.Count];
        public double DepartJd, ArriveJd;
        public ConvoyState State;

        /// <summary>The raiders who took it, for a stolen convoy.</summary>
        public Faction TakenBy;

        public double Tonnes
        {
            get
            {
                double t = 0;
                foreach (var c in Cargo) t += c;
                return t;
            }
        }
    }

    /// <summary>The heavy lanes an ore tug's haul can take between two bodies (U.SPC-T483): km along the lanes, and km pushed to and from their ramps.</summary>
    public readonly struct HeavyLanePath
    {
        public readonly double LaneKm, LegKm;
        public readonly string Name;

        public HeavyLanePath(double laneKm, double legKm, string name)
        {
            LaneKm = laneKm;
            LegKm = legKm;
            Name = name;
        }
    }

    /// <summary>
    /// One ore tug's haul (U.SPC-T483): latched onto a captured asteroid or a block of comet ice, pushing it from the
    /// field to a refinery (where it joins the grinders' backlog) or, for ice, straight to a depot (melted there). Slow
    /// in proportion to its load (see <see cref="BeltMining.TugAccel"/>), and a target for raiders the whole way.
    /// </summary>
    public sealed class TugHaul
    {
        public string Name;
        public MiningOperation Operation;
        public CapturedRock Load;
        public int FromBody, ToBody;

        /// <summary>The depot an ice block goes to; null for a load brought in to the refinery.</summary>
        public MaterialWarehouse ToDepot;

        /// <summary>The heavy lanes it rides, or null when it pushes the whole way through open space.</summary>
        public string HeavyLane;

        public bool ViaHeavyLane => HeavyLane != null;

        /// <summary>The tug's acceleration with its load, m/s².</summary>
        public double AccelMs2;

        /// <summary>When it set out, when it arrives with its load, and when the tug is back for another (the empty run home).</summary>
        public double DepartJd, ArriveJd, FreeJd;

        public ConvoyState State;

        /// <summary>The raiders who took the tug and its load, if they did.</summary>
        public Faction TakenBy;

        public override string ToString() => Name;
    }

    /// <summary>
    /// One faction's belt works: capture tugs bringing asteroids in to a refinery, grinders reducing them to dust,
    /// separators splitting the dust into elements, and convoys hauling the elements to a warehouse.
    /// </summary>
    public sealed class MiningOperation
    {
        public string Name;
        public Faction Owner;

        /// <summary>Where the refinery sits (a belt body such as Ceres).</summary>
        public int RefineryBody;

        /// <summary>Where its convoys take what it separates.</summary>
        public MaterialWarehouse Destination;

        /// <summary>Capture tugs at work, and the days each takes to find, match orbits with and bring in one rock.</summary>
        public int CaptureTugs = 2;
        public double CaptureDays = 24;

        /// <summary>Smallest and largest rock a tug can move, metres across.</summary>
        public double MinRockM = 40, MaxRockM = 140;

        /// <summary>The most rocks parked waiting for the grinders; the tugs stand down beyond it.</summary>
        public int MaxBacklog = 4;

        /// <summary>Tonnes a day the grinders reduce to dust, and the separators split into elements.</summary>
        public double GrindTonnesPerDay = 20000, SeparateTonnesPerDay = 20000;

        /// <summary>Share of each element in the dust the separators recover; the rest goes out with the slag.</summary>
        public double SeparationEfficiency = 0.9;

        /// <summary>A convoy sails with this many tonnes, or with whatever is waiting once it has waited <see cref="MaxHoldDays"/>.</summary>
        public double ConvoyTonnes = 40000, MaxHoldDays = 30;

        /// <summary>Share of an escort's full protection its convoys sail with (0 for none, 1 for a full escort).</summary>
        public double EscortShare;

        /// <summary>
        /// Ore tugs (<see cref="ShipClass.OreTug"/>) at work (U.SPC-T483): each latches onto a far bigger load than a capture
        /// tug's, a rock or a block of ice of <see cref="TugMinLoadM"/> to <see cref="TugMaxLoadM"/> across, and pushes it in,
        /// slowly. Raiders who take one keep the tug too.
        /// </summary>
        public int OreTugs;

        /// <summary>Where the tugs find their loads: the field around this body (-1: around the refinery).</summary>
        public int TugSourceBody = -1;

        /// <summary>Share of the tugs' loads that are blocks of comet ice rather than belt rock.</summary>
        public double TugIceShare;

        /// <summary>Where ice blocks go, melted there into water and volatiles; null: to the refinery with the rock.</summary>
        public MaterialWarehouse IceDepot;

        /// <summary>Smallest and largest load an ore tug latches onto, metres across (capped by what its route can carry).</summary>
        public double TugMinLoadM = 120, TugMaxLoadM = 300;

        public int TugHaulsSent, TugHaulsDelivered, TugsLost;
        public double TugTonnesDelivered;

        public readonly List<CapturedRock> Backlog = new List<CapturedRock>();

        /// <summary>Dust waiting for the separators, and the tonnes of each element it holds.</summary>
        public double Dust;
        public readonly double[] DustContent = new double[GoodInfo.Count];

        /// <summary>Separated elements waiting for a convoy.</summary>
        public readonly double[] Separated = new double[GoodInfo.Count];

        public int RocksCaptured, ConvoysSent;
        public double TonnesCaptured, TonnesGround, TonnesSeparated, Slag;

        internal double[] TugReadyJd;
        internal double LastConvoyJd = double.NaN;

        public double WaitingTonnes
        {
            get
            {
                double t = 0;
                foreach (var s in Separated) t += s;
                return t;
            }
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// A kind of megastructure, and its bill of separated elements (U.SPC-T330). New kinds plug in through
    /// <see cref="Register"/>; what one does once finished is its <see cref="Completed"/> handler.
    /// </summary>
    public sealed class MegastructureSpec
    {
        public string Name { get; }

        /// <summary>Tonnes of each good the whole build takes.</summary>
        public double[] Bill { get; }

        /// <summary>The fewest days the build can take, however much is to hand: it draws at most a <see cref="MinDays"/>th of its bill a day.</summary>
        public double MinDays { get; }

        /// <summary>
        /// Run when a build of this kind is finished; it may start another (a Dyson swarm adds one ring of collectors at a
        /// time). Not saved: specs are the build's static tables, written by name.
        /// </summary>
        public Action<BeltMining, MegastructureProject, double> Completed;

        public MegastructureSpec(string name, double minDays, params (Good, double)[] bill)
        {
            Name = name;
            MinDays = minDays;
            Bill = new double[GoodInfo.Count];
            foreach (var (g, t) in bill) Bill[(int)g] += t;
        }

        public double BillTonnes
        {
            get
            {
                double t = 0;
                foreach (var b in Bill) t += b;
                return t;
            }
        }

        /// <summary>A yard's slips, docks and gantries: on completion the settlement at its site gains a level of shipbuilding.</summary>
        public static readonly MegastructureSpec Shipyard = new MegastructureSpec("Shipyard", 120,
            (Good.NickelIron, 400e3), (Good.Titanium, 3e3), (Good.Copper, 300))
        {
            Completed = (mining, p, jd) => mining.YardBuilt?.Invoke(p, jd),
        };

        /// <summary>A tether from a world's surface to beyond its stationary orbit: steel anchor and counterweight, a carbon-fibre ribbon.</summary>
        public static readonly MegastructureSpec SpaceElevator = new MegastructureSpec("Space elevator", 365,
            (Good.NickelIron, 1.5e6), (Good.Hydrocarbons, 400e3), (Good.Titanium, 10e3), (Good.Copper, 2e3));

        /// <summary>
        /// One ring of a Dyson swarm's collectors. Finishing one lays down the next, so a builder who keeps the
        /// material coming slowly closes its star in.
        /// </summary>
        public static readonly MegastructureSpec DysonRing = new MegastructureSpec("Dyson swarm ring", 730,
            (Good.NickelIron, 20e6), (Good.Titanium, 50e3), (Good.Copper, 30e3), (Good.RareEarths, 500), (Good.Platinum, 20))
        {
            Completed = (mining, p, jd) => mining.Begin(DysonRing, p.Builder, p.SiteBody, p.BaseName, jd, p.Stage + 1),
        };

        static readonly List<MegastructureSpec> registered = new List<MegastructureSpec> { Shipyard, SpaceElevator, DysonRing };

        public static IReadOnlyList<MegastructureSpec> All => registered;

        public static void Register(MegastructureSpec spec)
        {
            if (spec != null && !registered.Contains(spec)) registered.Add(spec);
        }

        public static MegastructureSpec Find(string name)
        {
            foreach (var s in registered)
                if (s.Name == name) return s;
            return null;
        }
    }

    /// <summary>One megastructure going up, built from what its builder's warehouses hold.</summary>
    public sealed class MegastructureProject
    {
        public MegastructureSpec Spec;
        public Faction Builder;
        public int SiteBody;

        /// <summary>Its name without a stage (a Dyson swarm's rings share one).</summary>
        public string BaseName;
        public int Stage = 1;
        public string Name;

        public readonly double[] Used = new double[GoodInfo.Count];
        public double StartJd, DoneJd = double.NaN;

        public bool Done => !double.IsNaN(DoneJd);

        /// <summary>Share built: the share of its scarcest material delivered, 0..1.</summary>
        public double Progress
        {
            get
            {
                double p = 1;
                for (int g = 0; g < GoodInfo.Count; g++)
                    if (Spec.Bill[g] > 0) p = Math.Min(p, Used[g] / Spec.Bill[g]);
                return p;
            }
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// The asteroid-belt mining pipeline that feeds every megastructure build (U.SPC-T330):
    ///  1. Capture: each <see cref="MiningOperation"/>'s tugs bring rocks of a few tens to a hundred-odd metres in from
    ///     the belt to its refinery, one every <see cref="MiningOperation.CaptureDays"/> or so each, of a kind drawn from
    ///     the registered <see cref="AsteroidClass"/>es. Its ore tugs (<see cref="ShipClass.OreTug"/>, U.SPC-T483) latch
    ///     onto far bigger loads, rock or blocks of <see cref="AsteroidClass.CometIce"/>, and push them in slowly (load
    ///     mass governs their acceleration, <see cref="TugAccel"/>): rock to the refinery, ice to the refinery or straight
    ///     to a depot. Through open space a tug takes at most <see cref="OpenSpaceMaxTonnes"/>; where a heavy lane
    ///     (<see cref="Lane.Heavy"/>) joins the field and the destination it carries bigger loads, and far faster.
    ///  2. Grind: the grinders reduce the waiting rocks to dust, which carries the elements its rocks held.
    ///  3. Separate: the separators split the dust into elements (goods) and slag.
    ///  4. Ship: a convoy carries a load of elements to the operation's <see cref="MaterialWarehouse"/>, often on a
    ///     remote moon, taking days in proportion to the distance at the time it sails.
    ///  5. Build: each <see cref="MegastructureProject"/> draws what its bill needs from its builder's warehouses; when
    ///     every tonne is in, it is finished and its kind's <see cref="MegastructureSpec.Completed"/> handler runs.
    /// Raiders prey on the chain where it is weakest: a convoy or a tug and its load (<see cref="TugTheftPerDay"/>) on
    /// the way is robbed far more often
    /// (<see cref="ConvoyTheftPerDay"/>) than a warehouse is raided (<see cref="WarehouseRaidPerDay"/>), both in
    /// proportion to how many raiders are about (<see cref="PiratePressure"/>), less for an escorted convoy or a
    /// warehouse its owner guards.
    ///
    /// The pipeline runs in whole days off its own random stream, and reaches the world it sits in only through the
    /// hooks below, so it runs the same on its own (the tests) as wired into a <see cref="World"/>.
    /// </summary>
    public sealed class BeltMining
    {
        readonly Random rng;
        double nextDayJd = double.NaN;
        int rockSerial, convoySerial, haulSerial;

        public readonly List<MiningOperation> Operations = new List<MiningOperation>();
        public readonly List<MaterialWarehouse> Warehouses = new List<MaterialWarehouse>();
        public readonly List<MegastructureProject> Projects = new List<MegastructureProject>();

        /// <summary>Every convoy, en route or done (trimmed to the latest few hundred finished ones).</summary>
        public readonly List<MaterialConvoy> Convoys = new List<MaterialConvoy>();

        /// <summary>Chance a day, at full <see cref="PiratePressure"/>, that raiders take a convoy on the way.</summary>
        public const double ConvoyTheftPerDay = 0.005;

        /// <summary>Chance a day, at full <see cref="PiratePressure"/>, that raiders hit an unguarded warehouse: well under a convoy's.</summary>
        public const double WarehouseRaidPerDay = 0.0008;

        /// <summary>Share of what a warehouse holds that a raid carries off.</summary>
        public const double RaidShare = 0.35;

        /// <summary>A full escort cuts a convoy's risk by this share; a guarded warehouse's risk is cut by <see cref="GuardedRaidCut"/>.</summary>
        public const double EscortCut = 0.75, GuardedRaidCut = 0.6;

        /// <summary>How fast a convoy covers the distance from refinery to warehouse, AU a day, and the shortest trip, days.</summary>
        public const double ConvoyAuPerDay = 0.1, MinTransitDays = 2;

        /// <summary>Tonnes in a sphere of <paramref name="diameterM"/> metres at <paramref name="density"/> tonnes a cubic metre.</summary>
        static double RockTonnes(double diameterM, double density) => Math.PI / 6 * diameterM * diameterM * diameterM * density;

        /// <summary>A body's heliocentric position (km) on a date: for a convoy's trip time. Null: every trip is <see cref="MinTransitDays"/>.</summary>
        public Func<int, double, DVec3> Position;

        /// <summary>How many raiders are about, 0 (none) to 1 (as bad as it gets). Null: none.</summary>
        public Func<double> PiratePressure;

        /// <summary>The raiders who would take a convoy or raid a warehouse. Null: nobody in particular.</summary>
        public Func<Faction> Raiders;

        /// <summary>Whether a warehouse's owner guards it (gun platforms or a garrison at its body).</summary>
        public Func<MaterialWarehouse, bool> Guarded;

        /// <summary>Where the pipeline's news goes: (date, text, body).</summary>
        public Action<double, string, int> Log;

        /// <summary>A shipyard was finished: its world gives the settlement at the site a yard (see <see cref="MegastructureSpec.Shipyard"/>).</summary>
        public Action<MegastructureProject, double> YardBuilt;

        public event Action<MaterialConvoy> ConvoyStolen;
        public event Action<MaterialWarehouse, double> WarehouseRaided;
        public event Action<MegastructureProject> ProjectCompleted;

        /// <summary>Tonnes of each good raiders have taken, convoys, tugs' loads and warehouses together.</summary>
        public readonly double[] Plundered = new double[GoodInfo.Count];

        public int ConvoysStolen, ConvoysDelivered, WarehouseRaids;

        // ---------------------------------------------------------------- ore tugs (U.SPC-T483)

        /// <summary>Every ore tug's haul, under way or done (trimmed to the latest few hundred finished ones).</summary>
        public readonly List<TugHaul> Hauls = new List<TugHaul>();

        /// <summary>Chance a day, at full <see cref="PiratePressure"/>, that raiders take a tug and its load on the way.</summary>
        public const double TugTheftPerDay = 0.002;

        /// <summary>How far out in the field a tug finds its load, km, before it is pushed to the source body's neighbourhood.</summary>
        public const double TugLocalHaulKm = 0.05 * Astro.AuKm;

        /// <summary>The biggest load a tug will push through open space, tonnes; a heavy lane carries up to <see cref="Hyperlanes.HeavyLaneMaxTonnes"/>.</summary>
        public const double OpenSpaceMaxTonnes = 20e6;

        /// <summary>The heavy lanes between two bodies on a date, or null for none. Null: no heavy lanes anywhere.</summary>
        public Func<int, int, double, HeavyLanePath?> HeavyLane;

        public event Action<TugHaul> TugTaken;

        public int TugsTaken, TugHaulsDelivered;

        /// <summary>An ore tug's own mass, tonnes: its stock design, dry, with full tanks.</summary>
        public static double TugMassTonnes
        {
            get
            {
                var spec = CombatSpec.Of(ShipClass.OreTug);
                return spec.DryMassTonnes + spec.PropellantTonnes;
            }
        }

        /// <summary>
        /// An ore tug's acceleration pushing <paramref name="loadTonnes"/>, m/s²: its drive's thrust (its class's unladen
        /// acceleration times its own mass) over the tug and its load together.
        /// </summary>
        public static double TugAccel(double loadTonnes)
        {
            double tug = TugMassTonnes;
            return ShipClassSpec.Of(ShipClass.OreTug).AccelG * 9.80665 * tug / (tug + Math.Max(0, loadTonnes));
        }

        /// <summary>Days to push <paramref name="loadTonnes"/> over <paramref name="km"/>: accelerate half way, brake the rest.</summary>
        public static double PushDays(double km, double loadTonnes)
        {
            if (km <= 0) return 0;
            return 2 * Math.Sqrt(km * 1000 / TugAccel(loadTonnes)) / Astro.SecondsPerDay;
        }

        /// <summary>Metres across of a sphere of <paramref name="tonnes"/> at <paramref name="density"/>.</summary>
        static double RockDiameter(double tonnes, double density) => Math.Pow(6 * tonnes / (Math.PI * density), 1.0 / 3);

        public BeltMining(int seed)
        {
            rng = new Random(seed);
        }

        // ---------------------------------------------------------------- setting up

        public MaterialWarehouse AddWarehouse(string name, Faction owner, int body, double capacityTonnes = 50e6)
        {
            var w = new MaterialWarehouse { Name = name, Owner = owner, Body = body, CapacityTonnes = capacityTonnes };
            Warehouses.Add(w);
            return w;
        }

        public MiningOperation AddOperation(string name, Faction owner, int refineryBody, MaterialWarehouse destination)
        {
            var op = new MiningOperation { Name = name, Owner = owner, RefineryBody = refineryBody, Destination = destination };
            Operations.Add(op);
            return op;
        }

        /// <summary>Lays down a megastructure of <paramref name="spec"/> for <paramref name="builder"/> at <paramref name="siteBody"/>.</summary>
        public MegastructureProject Begin(MegastructureSpec spec, Faction builder, int siteBody, string name, double jd, int stage = 1)
        {
            var p = new MegastructureProject
            {
                Spec = spec, Builder = builder, SiteBody = siteBody, BaseName = name, Stage = stage, StartJd = jd,
                Name = stage > 1 ? $"{name} ({spec.Name.ToLowerInvariant()} {stage})" : name,
            };
            Projects.Add(p);
            return p;
        }

        // ---------------------------------------------------------------- running

        /// <summary>Runs the pipeline to <paramref name="jd"/>, a whole day at a time.</summary>
        public void Update(double jd)
        {
            if (double.IsNaN(nextDayJd)) nextDayJd = jd;
            while (nextDayJd <= jd)
            {
                Day(nextDayJd);
                nextDayJd += 1;
            }
        }

        void Day(double jd)
        {
            double pressure = Math.Max(0, Math.Min(1, PiratePressure?.Invoke() ?? 0));
            foreach (var op in Operations)
            {
                Capture(op, jd);
                Grind(op);
                Separate(op);
                Ship(op, jd);
                SendTugs(op, jd);
            }
            MoveConvoys(jd, pressure);
            MoveHauls(jd, pressure);
            RaidWarehouses(jd, pressure);
            Build(jd);
        }

        void Capture(MiningOperation op, double jd)
        {
            if (op.TugReadyJd == null || op.TugReadyJd.Length != op.CaptureTugs)
            {
                op.TugReadyJd = new double[op.CaptureTugs];
                // The tugs are spread out over their first trip rather than all bringing a rock in on the same day.
                for (int i = 0; i < op.CaptureTugs; i++) op.TugReadyJd[i] = jd + op.CaptureDays * (i + 1.0) / op.CaptureTugs;
            }
            for (int i = 0; i < op.TugReadyJd.Length; i++)
            {
                if (jd < op.TugReadyJd[i] || op.Backlog.Count >= op.MaxBacklog) continue;
                var kind = AsteroidClass.Pick(rng);
                double d = op.MinRockM + (op.MaxRockM - op.MinRockM) * rng.NextDouble();
                double t = RockTonnes(d, kind.Density);
                op.Backlog.Add(new CapturedRock
                {
                    Designation = $"{op.Name} rock {++rockSerial}", Class = kind, DiameterM = d, Tonnes = t, Remaining = t, CapturedJd = jd,
                });
                op.RocksCaptured++;
                op.TonnesCaptured += t;
                op.TugReadyJd[i] = jd + op.CaptureDays * (0.6 + 0.8 * rng.NextDouble());
            }
        }

        static void Grind(MiningOperation op)
        {
            double left = op.GrindTonnesPerDay;
            while (left > 0 && op.Backlog.Count > 0)
            {
                var rock = op.Backlog[0];
                double t = Math.Min(left, rock.Remaining);
                rock.Remaining -= t;
                left -= t;
                op.Dust += t;
                op.TonnesGround += t;
                foreach (var (g, f) in rock.Class.Yield) op.DustContent[(int)g] += t * f;
                if (rock.Remaining <= 1e-6) op.Backlog.RemoveAt(0);
            }
        }

        static void Separate(MiningOperation op)
        {
            if (op.Dust <= 0) return;
            double t = Math.Min(op.SeparateTonnesPerDay, op.Dust);
            double share = t / op.Dust;
            double recovered = 0;
            for (int g = 0; g < GoodInfo.Count; g++)
            {
                double content = op.DustContent[g] * share;
                op.DustContent[g] -= content;
                double got = content * op.SeparationEfficiency;
                op.Separated[g] += got;
                recovered += got;
            }
            op.Dust -= t;
            if (op.Dust < 1e-6)
            {
                op.Dust = 0;
                Array.Clear(op.DustContent, 0, op.DustContent.Length);
            }
            op.TonnesSeparated += recovered;
            op.Slag += t - recovered;
        }

        void Ship(MiningOperation op, double jd)
        {
            var to = op.Destination;
            if (to == null) return;
            if (double.IsNaN(op.LastConvoyJd)) op.LastConvoyJd = jd;
            double waiting = op.WaitingTonnes;
            if (waiting <= 0) return;
            if (waiting < op.ConvoyTonnes && jd - op.LastConvoyJd < op.MaxHoldDays) return;
            // Nothing sails for a warehouse with no room, nor more than it can still take (counting what is already on the way to it).
            double room = to.CapacityTonnes - to.StoredTonnes;
            foreach (var c in Convoys)
                if (c.State == ConvoyState.EnRoute && c.To == to) room -= c.Tonnes;
            double load = Math.Min(Math.Min(waiting, op.ConvoyTonnes), room);
            if (load <= 0) return;

            var convoy = new MaterialConvoy { Name = $"{op.Name} convoy {++convoySerial}", From = op, To = to, DepartJd = jd };
            double share = load / waiting;
            for (int g = 0; g < GoodInfo.Count; g++)
            {
                double t = op.Separated[g] * share;
                convoy.Cargo[g] = t;
                op.Separated[g] -= t;
            }
            convoy.ArriveJd = jd + TransitDays(op.RefineryBody, to.Body, jd);
            Convoys.Add(convoy);
            op.ConvoysSent++;
            op.LastConvoyJd = jd;
        }

        /// <summary>Days a convoy takes from <paramref name="from"/> to <paramref name="to"/> sailing on <paramref name="jd"/>.</summary>
        public double TransitDays(int from, int to, double jd)
        {
            if (Position == null || from == to) return MinTransitDays;
            double au = (Position(to, jd) - Position(from, jd)).Length / Astro.AuKm;
            return Math.Max(MinTransitDays, au / ConvoyAuPerDay);
        }

        void MoveConvoys(double jd, double pressure)
        {
            foreach (var c in Convoys)
            {
                if (c.State != ConvoyState.EnRoute) continue;
                double risk = ConvoyTheftPerDay * pressure * (1 - EscortCut * Clamp01(c.From?.EscortShare ?? 0));
                if (risk > 0 && rng.NextDouble() < risk)
                {
                    c.State = ConvoyState.Stolen;
                    c.TakenBy = Raiders?.Invoke();
                    for (int g = 0; g < GoodInfo.Count; g++) Plundered[g] += c.Cargo[g];
                    ConvoysStolen++;
                    Log?.Invoke(jd, $"Raiders{By(c.TakenBy)} took the {c.Name} on its way to the {c.To.Name}: {c.Tonnes:N0} t of raw materials gone", c.To.Body);
                    ConvoyStolen?.Invoke(c);
                    continue;
                }
                if (jd < c.ArriveJd) continue;
                c.State = ConvoyState.Delivered;
                for (int g = 0; g < GoodInfo.Count; g++) c.To.Stock[g] += c.Cargo[g];
                ConvoysDelivered++;
            }
            // Keep the history bounded: drop the oldest finished convoys.
            int finished = 0;
            foreach (var c in Convoys)
                if (c.State != ConvoyState.EnRoute) finished++;
            for (int i = 0; i < Convoys.Count && finished > 300; i++)
            {
                if (Convoys[i].State == ConvoyState.EnRoute) continue;
                Convoys.RemoveAt(i--);
                finished--;
            }
        }

        // ---------------------------------------------------------------- ore tugs (U.SPC-T483)

        /// <summary>Km a tug pushes through open space from the field around <paramref name="from"/> to <paramref name="to"/>.</summary>
        double OpenKm(int from, int to, double jd)
        {
            double km = TugLocalHaulKm;
            if (Position != null && from != to) km += (Position(to, jd) - Position(from, jd)).Length;
            return km;
        }

        /// <summary>
        /// Days an ore tug takes to push <paramref name="tonnes"/> from the field around <paramref name="from"/> to
        /// <paramref name="to"/> setting out on <paramref name="jd"/>: by heavy lane where one joins them and it is the
        /// quicker (or the only) way, else through open space. <paramref name="heavy"/> is the heavy lanes taken, if any;
        /// infinite when the load is too big for every way open to it.
        /// </summary>
        public double HaulDays(int from, int to, double tonnes, double jd, out HeavyLanePath? heavy)
        {
            heavy = null;
            double open = tonnes <= OpenSpaceMaxTonnes ? PushDays(OpenKm(from, to, jd), tonnes) : double.PositiveInfinity;
            var lane = from == to ? null : HeavyLane?.Invoke(from, to, jd);
            if (lane is HeavyLanePath h && tonnes <= Hyperlanes.HeavyLaneMaxTonnes)
            {
                double byLane = PushDays(TugLocalHaulKm, tonnes) + 2 * PushDays(h.LegKm / 2, tonnes) + Hyperlanes.HeavyRideDays(h.LaneKm);
                if (byLane <= open)
                {
                    heavy = h;
                    return byLane;
                }
            }
            return open;
        }

        /// <summary>The biggest load a tug can take from <paramref name="from"/> to <paramref name="to"/>: a heavy lane's, where one joins them.</summary>
        public double MaxLoadTonnes(int from, int to, double jd) =>
            from != to && HeavyLane?.Invoke(from, to, jd) != null ? Hyperlanes.HeavyLaneMaxTonnes : OpenSpaceMaxTonnes;

        /// <summary>Sends every idle ore tug of <paramref name="op"/> out to latch onto a load and push it in.</summary>
        void SendTugs(MiningOperation op, double jd)
        {
            if (op.OreTugs <= 0) return;
            int busy = 0, inbound = 0;
            foreach (var h in Hauls)
            {
                if (h.Operation != op || h.State == ConvoyState.Stolen) continue;
                if (jd < h.FreeJd) busy++;
                if (h.State == ConvoyState.EnRoute && h.ToDepot == null) inbound++;
            }
            for (; busy < op.OreTugs; busy++)
            {
                bool ice = op.TugIceShare > 0 && rng.NextDouble() < op.TugIceShare;
                var kind = ice ? AsteroidClass.CometIce : AsteroidClass.Pick(rng);
                var depot = ice ? op.IceDepot : null;
                if (depot == null)
                {
                    // The grinders' yard is full (counting what is on the way): the tugs stand down.
                    if (op.Backlog.Count + inbound >= op.MaxBacklog) return;
                }
                else if (depot.StoredTonnes >= depot.CapacityTonnes) return;

                int from = op.TugSourceBody >= 0 ? op.TugSourceBody : op.RefineryBody;
                int to = depot?.Body ?? op.RefineryBody;
                double d = op.TugMinLoadM + (op.TugMaxLoadM - op.TugMinLoadM) * rng.NextDouble();
                double t = RockTonnes(d, kind.Density), cap = MaxLoadTonnes(from, to, jd);
                if (t > cap)
                {
                    // Too big for any way in: the tug cuts (or picks) one that is not.
                    t = cap;
                    d = RockDiameter(t, kind.Density);
                }
                double days = HaulDays(from, to, t, jd, out var heavy);
                if (double.IsInfinity(days)) return;
                // Home again empty, the same way, at its unladen acceleration.
                double back = HaulDays(from, to, 0, jd, out _);
                var haul = new TugHaul
                {
                    Name = $"{op.Name} tug haul {++haulSerial}", Operation = op, FromBody = from, ToBody = to, ToDepot = depot,
                    HeavyLane = heavy?.Name, AccelMs2 = TugAccel(t), DepartJd = jd, ArriveJd = jd + Math.Max(1, days),
                    Load = new CapturedRock
                    {
                        Designation = ice ? $"{op.Name} ice block {haulSerial}" : $"{op.Name} rock {++rockSerial}",
                        Class = kind, DiameterM = d, Tonnes = t, Remaining = t,
                    },
                };
                haul.FreeJd = haul.ArriveJd + Math.Max(1, back);
                Hauls.Add(haul);
                op.TugHaulsSent++;
                if (depot == null) inbound++;
            }
        }

        void MoveHauls(double jd, double pressure)
        {
            foreach (var h in Hauls)
            {
                if (h.State != ConvoyState.EnRoute) continue;
                var op = h.Operation;
                double risk = TugTheftPerDay * pressure * (1 - EscortCut * Clamp01(op?.EscortShare ?? 0));
                if (risk > 0 && rng.NextDouble() < risk)
                {
                    // Raiders take the tug and what it holds: the load is theirs, the hull lost to its operation.
                    h.State = ConvoyState.Stolen;
                    h.TakenBy = Raiders?.Invoke();
                    foreach (var (g, f) in h.Load.Class.Yield) Plundered[(int)g] += h.Load.Tonnes * f;
                    if (op != null)
                    {
                        op.OreTugs = Math.Max(0, op.OreTugs - 1);
                        op.TugsLost++;
                    }
                    TugsTaken++;
                    Log?.Invoke(jd, $"Raiders{By(h.TakenBy)} seized an ore tug of the {op?.Name} and the {h.Load.Tonnes:N0} t of {h.Load.Class.Name.ToLowerInvariant()} it was pushing", h.ToBody);
                    TugTaken?.Invoke(h);
                    continue;
                }
                if (jd < h.ArriveJd) continue;
                h.State = ConvoyState.Delivered;
                TugHaulsDelivered++;
                if (op == null) continue;
                op.TugHaulsDelivered++;
                op.TugTonnesDelivered += h.Load.Tonnes;
                if (h.ToDepot != null)
                {
                    // Ice is melted at the depot: its water and volatiles go into stock, the grit is dumped. Never past its room.
                    var w = h.ToDepot;
                    double room = Math.Max(0, w.CapacityTonnes - w.StoredTonnes), got = 0;
                    foreach (var (_, f) in h.Load.Class.Yield) got += h.Load.Tonnes * f * op.SeparationEfficiency;
                    double share = got > room ? room / got : 1;
                    foreach (var (g, f) in h.Load.Class.Yield) w.Stock[(int)g] += h.Load.Tonnes * f * op.SeparationEfficiency * share;
                }
                else
                {
                    // Parked at the refinery for the grinders, like a capture tug's catch.
                    h.Load.CapturedJd = jd;
                    op.Backlog.Add(h.Load);
                    op.RocksCaptured++;
                    op.TonnesCaptured += h.Load.Tonnes;
                }
            }
            // Keep the history bounded: drop the oldest finished hauls whose tugs are home (or gone).
            int finished = 0;
            foreach (var h in Hauls)
                if (h.State == ConvoyState.Stolen || (h.State == ConvoyState.Delivered && jd >= h.FreeJd)) finished++;
            for (int i = 0; i < Hauls.Count && finished > 300; i++)
            {
                var h = Hauls[i];
                if (!(h.State == ConvoyState.Stolen || (h.State == ConvoyState.Delivered && jd >= h.FreeJd))) continue;
                Hauls.RemoveAt(i--);
                finished--;
            }
        }

        void RaidWarehouses(double jd, double pressure)
        {
            foreach (var w in Warehouses)
            {
                if (w.StoredTonnes <= 0) continue;
                bool guarded = Guarded?.Invoke(w) ?? false;
                double risk = WarehouseRaidPerDay * pressure * (guarded ? 1 - GuardedRaidCut : 1);
                if (risk <= 0 || rng.NextDouble() >= risk) continue;
                double taken = 0;
                for (int g = 0; g < GoodInfo.Count; g++)
                {
                    double t = w.Stock[g] * RaidShare;
                    w.Stock[g] -= t;
                    Plundered[g] += t;
                    taken += t;
                }
                w.Raids++;
                w.TonnesLostToRaids += taken;
                WarehouseRaids++;
                Log?.Invoke(jd, $"Raiders{By(Raiders?.Invoke())} broke into the {w.Name} and carried off {taken:N0} t of raw materials", w.Body);
                WarehouseRaided?.Invoke(w, taken);
            }
        }

        void Build(double jd)
        {
            // A list, not a foreach: a finished project may lay down the next.
            for (int i = 0; i < Projects.Count; i++)
            {
                var p = Projects[i];
                if (p.Done) continue;
                for (int g = 0; g < GoodInfo.Count; g++)
                {
                    double need = p.Spec.Bill[g] - p.Used[g];
                    if (need <= 1e-9) continue;
                    double want = Math.Min(need, p.Spec.Bill[g] / Math.Max(1, p.Spec.MinDays));
                    foreach (var w in Warehouses)
                    {
                        if (want <= 0) break;
                        if (w.Owner != p.Builder) continue;
                        double t = Math.Min(want, w.Stock[g]);
                        w.Stock[g] -= t;
                        p.Used[g] += t;
                        want -= t;
                    }
                }
                if (p.Progress < 1 - 1e-9) continue;
                p.DoneJd = jd;
                Log?.Invoke(jd, $"{p.Builder?.Name ?? "Someone"} finished the {p.Name}", p.SiteBody);
                p.Spec.Completed?.Invoke(this, p, jd);
                ProjectCompleted?.Invoke(p);
            }
        }

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        static string By(Faction f) => f == null ? "" : " of the " + f.Name;
    }
}
