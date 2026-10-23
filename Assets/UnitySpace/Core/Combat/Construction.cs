using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>The broad kind of thing a hull frame builds (U.SPC-T377): everything that can be built in space.</summary>
    public enum HullCategory
    {
        Fighter, SmallCraft, Freighter, Warship, Battleship, Carrier, Station, Satellite,

        /// <summary>A polity's unarmed service hulls: the lane tenders that keep its hyperlanes running (U.SPC-T481).</summary>
        Support,
    }

    /// <summary>How big a hardpoint's socket is, for the part a model library sets in it: a fighter's gun, a
    /// destroyer's turret, a battleship's or a station's.</summary>
    public enum HardpointSize { Small, Medium, Large }

    /// <summary>
    /// One hardpoint of a hull frame (U.SPC-T377): a place on the hull's surface -- where it sits (hull lengths, nose +Z,
    /// up +Y), which way it faces and fires (its yaw arc), how big it is, what kind of mount it is and what the design
    /// builds onto it. The positions are the same ones the battle fires from, so a model library that sets a turret,
    /// launcher or tube on each socket puts it exactly where the fight thinks it is. What is inside the hull is not a
    /// hardpoint but a facility (<see cref="FacilitySite"/>, U.SPC-T470), with a size and no arc.
    /// </summary>
    public sealed class Hardpoint
    {
        /// <summary>The slot's index in the frame's <see cref="Loadout.Slots"/>.</summary>
        public readonly int SlotIndex;

        /// <summary>The hardpoint's index in the design's <see cref="CombatSpec.Layout"/>.</summary>
        public readonly int LayoutIndex;

        public readonly SlotKind Slot;
        public readonly HardpointSize Size;
        public readonly double X, Y, Z;
        public readonly bool FacesAft;
        public readonly double ArcCenterDeg, ArcWidthDeg;

        /// <summary>A mount that bears on the bow (it can take a spinal railgun or a torpedo tube).</summary>
        public readonly bool BearsOnBow;

        /// <summary>What the design builds here.</summary>
        public readonly ModuleKind? Stock;

        /// <summary>A stable name for the socket within its frame ("heavy-2", "tube-0"), for a model library's prefab
        /// bindings and for saves.</summary>
        public readonly string Socket;

        public Hardpoint(int slotIndex, int layoutIndex, SlotKind slot, HardpointSize size, SubsystemDef def, ModuleKind? stock, bool bearsOnBow, string socket)
        {
            SlotIndex = slotIndex;
            LayoutIndex = layoutIndex;
            Slot = slot;
            Size = size;
            Stock = stock;
            BearsOnBow = bearsOnBow;
            Socket = socket;
            if (def != null)
            {
                X = def.X;
                Y = def.Y;
                Z = def.Z;
                FacesAft = def.FacesAft;
                ArcCenterDeg = def.ArcCenterDeg;
                ArcWidthDeg = def.ArcWidthDeg;
            }
            else ArcWidthDeg = 360;
        }

        /// <summary>Whether a gun, launcher or tube goes here (not a shield or an engine).</summary>
        public bool IsWeaponMount => Slot == SlotKind.LightMount || Slot == SlotKind.HeavyMount || Slot == SlotKind.GunMount ||
                                     Slot == SlotKind.UtilityMount || Slot == SlotKind.TorpedoTube;

        /// <summary>The key a model library binds a part to: what is fitted (or the slot, when empty) and the socket's size,
        /// e.g. "TorpedoTube/Large" or "HeavyMount/Medium".</summary>
        public static string ArtKey(Hardpoint h, ModuleKind? fitted) => (fitted?.ToString() ?? h.Slot.ToString()) + "/" + h.Size;
    }

    /// <summary>
    /// A buildable hull (U.SPC-T377): a design and the parts it offers, for anything built in space -- each ship class's
    /// designs (fighters, couriers and breachers, freighters and ore haulers, destroyers and cruisers, battleships and
    /// carriers), stations and satellites alike. A frame has two kinds of part (U.SPC-T470): its hardpoints on the hull's
    /// surface (<see cref="Hardpoints"/>: guns, launchers, torpedo tubes, point defence, shields, engines, each with a
    /// position and an arc) and its facilities inside the body (<see cref="Facilities"/>: bridge, engineering, quarters,
    /// hangar, holds), with the free internal space beyond them (<see cref="SpareSpace"/>) for the facilities a yard fits.
    /// A yard fits them exactly as it refits a ship (<see cref="Outfitting"/>), and a <see cref="ConstructionPlan"/>
    /// builds one from scratch.
    /// </summary>
    public sealed class HullFrame
    {
        public readonly string Id;
        public readonly string Name;
        public readonly HullCategory Category;

        /// <summary>The ship class it builds, or null for a station or satellite.</summary>
        public readonly ShipClass? Class;

        public readonly CombatSpec Design;

        /// <summary>The facilities the hull is designed with, laid out as its interior has them.</summary>
        public readonly IReadOnlyList<FacilitySite> Facilities;

        /// <summary>Free internal space beyond its own facilities, sections, for the facilities a yard fits.</summary>
        public readonly double SpareSpace;

        public readonly bool SmallCraft;

        /// <summary>Build points (tonnes of alloys through a yard) to lay the hull down as designed.</summary>
        public readonly double BuildPoints;

        /// <summary>The trade hold it is built with, tonnes.</summary>
        public readonly double CapacityTonnes;

        IReadOnlyList<Hardpoint> hardpoints;

        /// <param name="facilities">The facilities it is designed with; null for its class's (<see cref="FacilityLayouts.For"/>),
        /// or a platform's for a station or satellite.</param>
        public HullFrame(string id, string name, HullCategory category, ShipClass? cls, CombatSpec design, double spareSpace, bool smallCraft,
                         double buildPoints, double capacityTonnes = 0, IReadOnlyList<FacilitySite> facilities = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name;
            Category = category;
            Class = cls;
            Design = design ?? throw new ArgumentNullException(nameof(design));
            SpareSpace = Math.Max(0, spareSpace);
            Facilities = facilities ?? (cls is ShipClass c ? FacilityLayouts.For(c) : FacilityLayouts.Platform);
            SmallCraft = smallCraft;
            BuildPoints = buildPoints;
            CapacityTonnes = capacityTonnes;
        }

        /// <summary>A static structure (a station, a satellite): it holds its orbit and has no drive to refit.</summary>
        public bool Static => Design.MaxSpeed <= 0;

        /// <summary>A fresh loadout of this frame as designed.</summary>
        public Loadout NewLoadout() => new Loadout(Design, Class, Category, CapacityTonnes, SmallCraft, Facilities, SpareSpace);

        /// <summary>The hull's whole internal space, sections: its own facilities and the free space beyond them.</summary>
        public double InternalSpace => FacilityLayouts.SpaceOf(Facilities) + SpareSpace;

        /// <summary>Whether a facility of <paramref name="kind"/> could ever go into this hull: one a yard fits, allowed on
        /// its category, and no bigger than its free space.</summary>
        public bool CanCarry(FacilityKind kind) => kind != null && kind.Fittable && kind.AllowedOn(Category) && kind.Size <= SpareSpace + 1e-9;

        /// <summary>Every hardpoint the frame offers, in its loadout's slot order.</summary>
        public IReadOnlyList<Hardpoint> Hardpoints => hardpoints ??= MakeHardpoints();

        IReadOnlyList<Hardpoint> MakeHardpoints()
        {
            var l = NewLoadout();
            var list = new List<Hardpoint>(l.Slots.Count);
            var counts = new Dictionary<SlotKind, int>();
            for (int i = 0; i < l.Slots.Count; i++)
            {
                var s = l.Slots[i];
                counts.TryGetValue(s.Kind, out int n);
                counts[s.Kind] = n + 1;
                list.Add(new Hardpoint(i, s.LayoutIndex, s.Kind, SizeOf(s.Kind), Design.Layout[s.LayoutIndex], s.Stock, s.BearsOnBow, SocketPrefix(s.Kind) + "-" + n));
            }
            return list;
        }

        /// <summary>A socket's size: a fighter's are small; a gun or utility mount and point defence are small on any hull;
        /// other sockets are large on a capital hull or station (a kilometre long or more) and medium otherwise.</summary>
        HardpointSize SizeOf(SlotKind k)
        {
            if (SmallCraft || k == SlotKind.GunMount || k == SlotKind.UtilityMount || k == SlotKind.LightMount) return HardpointSize.Small;
            return Design.LengthKm >= 1 ? HardpointSize.Large : HardpointSize.Medium;
        }

        static string SocketPrefix(SlotKind k)
        {
            switch (k)
            {
                case SlotKind.LightMount: return "light";
                case SlotKind.HeavyMount: return "heavy";
                case SlotKind.ShieldMount: return "shield";
                case SlotKind.EngineMount: return "engine";
                case SlotKind.GunMount: return "gun";
                case SlotKind.UtilityMount: return "utility";
                case SlotKind.TorpedoTube: return "tube";
                default: return k.ToString().ToLowerInvariant();
            }
        }
    }

    /// <summary>
    /// The catalogue of everything that can be built in space (U.SPC-T377). Every ship class's designs are listed as
    /// frames ("ship/Battleship/Battleship"), along with the stations (the defence platform settlements and gates
    /// field, and an orbital station) and satellites (a weapons satellite and a sensor satellite). New hulls plug in
    /// with <see cref="Register"/>: a frame, never an edit to the code that builds the first.
    /// </summary>
    public static class HullFrames
    {
        static readonly List<HullFrame> All = new List<HullFrame>();
        static readonly Dictionary<string, HullFrame> ById = new Dictionary<string, HullFrame>(StringComparer.OrdinalIgnoreCase);

        static HullFrames()
        {
            foreach (ShipClass c in Enum.GetValues(typeof(ShipClass)))
                foreach (var design in CombatSpec.DesignsOf(c))
                    Register(new HullFrame("ship/" + c + "/" + design.DesignName, design.DesignName, CategoryOf(c), c, design,
                        FacilityLayouts.SpareSpace(c), Loadout.IsSmallCraft(c), Economy.HullCost(c), Economy.CapacityOf(c)));

            Register(new HullFrame("station/defence-platform", CombatSpec.Platform.DesignName, HullCategory.Station, null, CombatSpec.Platform,
                2, false, War.PlatformPrice / PricePerBuildPoint));
            Register(new HullFrame("station/orbital-station", OrbitalStation.DesignName, HullCategory.Station, null, OrbitalStation, 8, false, 60000, 50000));
            Register(new HullFrame("satellite/weapons", WeaponsSatellite.DesignName, HullCategory.Satellite, null, WeaponsSatellite, 0, false, 900));
            Register(new HullFrame("satellite/sensor", SensorSatellite.DesignName, HullCategory.Satellite, null, SensorSatellite, 1, false, 400));
        }

        /// <summary>Credits a yard charges per build point (as <see cref="Economy.HullPrice"/> does).</summary>
        public const double PricePerBuildPoint = 1200;

        /// <summary>Adds a frame to the catalogue (or replaces the one with its id).</summary>
        public static void Register(HullFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (ById.TryGetValue(frame.Id, out var old)) All.Remove(old);
            ById[frame.Id] = frame;
            All.Add(frame);
        }

        public static IReadOnlyList<HullFrame> Catalogue => All;

        public static HullFrame Of(string id) => id != null && ById.TryGetValue(id, out var f) ? f : null;

        /// <summary>The frames of one category.</summary>
        public static IEnumerable<HullFrame> InCategory(HullCategory c)
        {
            foreach (var f in All) if (f.Category == c) yield return f;
        }

        /// <summary>The frame a ship class's first (stock) design builds.</summary>
        public static HullFrame ForClass(ShipClass c)
        {
            var design = CombatSpec.Of(c);
            foreach (var f in All) if (f.Class == c && f.Design == design) return f;
            return null;
        }

        public static HullCategory CategoryOf(ShipClass c)
        {
            switch (c)
            {
                case ShipClass.Fighter:
                case ShipClass.Gunship: return HullCategory.Fighter;
                case ShipClass.Interceptor:
                case ShipClass.Bomber: return HullCategory.Fighter;
                case ShipClass.Courier:
                case ShipClass.Breacher: return HullCategory.SmallCraft;
                case ShipClass.Freighter:
                case ShipClass.Miner:
                case ShipClass.OreTug: return HullCategory.Freighter;
                case ShipClass.Battleship: return HullCategory.Battleship;
                case ShipClass.Carrier: return HullCategory.Carrier;
                case ShipClass.LaneTender: return HullCategory.Support;
                default: return HullCategory.Warship;
            }
        }

        // ---------------------------------------------------------------- stations and satellites

        /// <summary>A trade and habitat station's armed hub: heavy turrets about its ring, launchers, torpedo tubes
        /// facing out along its axis, point defence all round and room for eight sections of plate, racks and scanners.</summary>
        public static readonly CombatSpec OrbitalStation = new CombatSpec
        {
            DesignName = "Orbital station", LengthKm = 2.0,
            Hull = 34000, Shield = 14000, ShieldRegen = 70, Armor = 9, MaxSpeed = 0, Accel = 0, Decel = 0, TurnRateDegS = 0,
            Crew = 1400, Marines = 180, DryMassTonnes = 400000,
            Layout = new[]
            {
                new SubsystemDef(SubsystemKind.HeavyLaser, 0.35, 0.05, 0, arcCenterDeg: 90, arcWidthDeg: 180),
                new SubsystemDef(SubsystemKind.HeavyLaser, -0.35, 0.05, 0, arcCenterDeg: 270, arcWidthDeg: 180),
                new SubsystemDef(SubsystemKind.HeavyLaser, 0, 0.05, 0.35, arcCenterDeg: 0, arcWidthDeg: 180),
                new SubsystemDef(SubsystemKind.HeavyLaser, 0, 0.05, -0.35, true, arcCenterDeg: 180, arcWidthDeg: 180),
                new SubsystemDef(SubsystemKind.MissileLauncher, 0.25, 0.08, 0.25),
                new SubsystemDef(SubsystemKind.MissileLauncher, -0.25, 0.08, -0.25),
                new SubsystemDef(SubsystemKind.TorpedoTube, 0, 0, 0.42, arcCenterDeg: 0, arcWidthDeg: 90),
                new SubsystemDef(SubsystemKind.TorpedoTube, 0, 0, -0.42, true, arcCenterDeg: 180, arcWidthDeg: 90),
                new SubsystemDef(SubsystemKind.PointDefense, 0.3, 0.1, 0.3),
                new SubsystemDef(SubsystemKind.PointDefense, -0.3, 0.1, 0.3),
                new SubsystemDef(SubsystemKind.PointDefense, 0.3, 0.1, -0.3),
                new SubsystemDef(SubsystemKind.PointDefense, -0.3, 0.1, -0.3),
                new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.2, 0.2, arcCenterDeg: 0, arcWidthDeg: 90),
                new SubsystemDef(SubsystemKind.ShieldGenerator, 0.2, 0.2, 0, arcCenterDeg: 90, arcWidthDeg: 90),
                new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.2, -0.2, arcCenterDeg: 180, arcWidthDeg: 90),
                new SubsystemDef(SubsystemKind.ShieldGenerator, -0.2, 0.2, 0, arcCenterDeg: 270, arcWidthDeg: 90),
                new SubsystemDef(SubsystemKind.Bridge, 0, 0.25, 0),
                new SubsystemDef(SubsystemKind.Hangar, 0, -0.1, 0),
                new SubsystemDef(SubsystemKind.Reactor, 0, 0, 0),
            },
        };

        /// <summary>An orbital weapons satellite: one laser turret, one missile launcher and point defence on a small
        /// shielded bus, holding its slot in orbit.</summary>
        public static readonly CombatSpec WeaponsSatellite = new CombatSpec
        {
            DesignName = "Weapons satellite", LengthKm = 0.04,
            Hull = 900, Shield = 600, ShieldRegen = 8, Armor = 2, MaxSpeed = 0, Accel = 0, Decel = 0, TurnRateDegS = 0,
            Crew = 0, Marines = 0, DryMassTonnes = 120,
            Layout = new[]
            {
                new SubsystemDef(SubsystemKind.LaserTurret, 0, 0.2, 0.1),
                new SubsystemDef(SubsystemKind.MissileLauncher, 0, -0.2, 0),
                new SubsystemDef(SubsystemKind.PointDefense, 0.25, 0, -0.1),
                new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0, 0, arcCenterDeg: 0, arcWidthDeg: 360),
                new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.3),
            },
        };

        /// <summary>A sensor satellite: point defence for its own protection and room for a scanner suite.</summary>
        public static readonly CombatSpec SensorSatellite = new CombatSpec
        {
            DesignName = "Sensor satellite", LengthKm = 0.03,
            Hull = 500, Shield = 300, ShieldRegen = 5, Armor = 1, MaxSpeed = 0, Accel = 0, Decel = 0, TurnRateDegS = 0,
            Crew = 0, Marines = 0, DryMassTonnes = 60,
            Layout = new[]
            {
                new SubsystemDef(SubsystemKind.PointDefense, 0, 0.25, 0),
                new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0, 0, arcCenterDeg: 0, arcWidthDeg: 360),
                new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.3),
            },
        };
    }

    /// <summary>Why a construction plan's fitting did or did not go through (<see cref="NoRoom"/>: the facility is bigger
    /// than the hull's free internal space).</summary>
    public enum ConstructionOutcome { Ok, NoSuchSocket, WontFit, NoRoom }

    /// <summary>
    /// Building a hull from scratch (U.SPC-T377): pick a <see cref="HullFrame"/>, fit what goes on each of its hardpoints
    /// (a torpedo tube in a bow mount, a heavy laser for a turret) and the facilities that go into its free internal space
    /// (plate, racks, a scanner suite, a brig, U.SPC-T470), and the plan gives the hull's fighting spec, its bill of
    /// materials and its price. A plan launches as a new ship's loadout (<see cref="Apply"/>) or as a station's or
    /// satellite's combat state (<see cref="BuildCombat"/>), its interior built from its facilities.
    /// </summary>
    public sealed class ConstructionPlan
    {
        public readonly HullFrame Frame;
        public readonly Loadout Loadout;

        public ConstructionPlan(HullFrame frame)
        {
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            Loadout = frame.NewLoadout();
        }

        public IReadOnlyList<Hardpoint> Hardpoints => Frame.Hardpoints;

        /// <summary>The facilities inside the hull as planned: the frame's own, then those fitted.</summary>
        public IReadOnlyList<FacilitySite> Facilities => Loadout.Facilities;

        /// <summary>Internal space still free for another facility, sections.</summary>
        public double FreeSpace => Loadout.FreeSpace;

        /// <summary>What is fitted on a hardpoint now.</summary>
        public ModuleKind? FittedAt(int socket) => socket >= 0 && socket < Loadout.Fitted.Length ? Loadout.Fitted[socket] : null;

        /// <summary>Fits <paramref name="module"/> onto hardpoint <paramref name="socket"/>.</summary>
        public ConstructionOutcome Fit(int socket, ModuleKind? module)
        {
            if (socket < 0 || socket >= Loadout.Slots.Count) return ConstructionOutcome.NoSuchSocket;
            if (!Outfitting.CanFit(Loadout, socket, module)) return ConstructionOutcome.WontFit;
            Loadout.Fitted[socket] = module;
            return ConstructionOutcome.Ok;
        }

        /// <summary>Fits a facility of <paramref name="kind"/> into the hull's free internal space.</summary>
        public ConstructionOutcome FitFacility(FacilityKind kind)
        {
            var can = Loadout.CanAdd(kind);
            if (can == OutfitOutcome.NoRoom) return ConstructionOutcome.NoRoom;
            if (can != OutfitOutcome.Ok) return ConstructionOutcome.WontFit;
            Loadout.Facilities.Add(FacilitySite.Fitted(kind));
            return ConstructionOutcome.Ok;
        }

        /// <summary>Takes facility number <paramref name="index"/> out of the plan (a fitted one, not the frame's structure).</summary>
        public ConstructionOutcome RemoveFacility(int index)
        {
            if (index < 0 || index >= Loadout.Facilities.Count) return ConstructionOutcome.NoSuchSocket;
            if (!Loadout.CanRemove(index)) return ConstructionOutcome.WontFit;
            Loadout.Facilities.RemoveAt(index);
            return ConstructionOutcome.Ok;
        }

        /// <summary>Fits <paramref name="module"/> where it goes: a facility module (plate, racks, a scanner, a brig) into
        /// the free space, any other onto the first hardpoint that takes it and is still as designed. Returns the
        /// hardpoint's socket or the facility's index, or -1.</summary>
        public int FitFirst(ModuleKind module)
        {
            if (FacilityKinds.ForModule(module) is FacilityKind kind)
                return FitFacility(kind) == ConstructionOutcome.Ok ? Loadout.Facilities.Count - 1 : -1;
            for (int i = 0; i < Loadout.Slots.Count; i++)
                if (Loadout.Fitted[i] == Loadout.Slots[i].Stock && Loadout.Fitted[i] != module && Outfitting.CanFit(Loadout, i, module))
                {
                    Loadout.Fitted[i] = module;
                    return i;
                }
            return -1;
        }

        /// <summary>The hull's fighting spec as planned.</summary>
        public CombatSpec Build() => Loadout.Build();

        /// <summary>The parts fitted over the design (+1) and the design's own taken out (-1).</summary>
        IEnumerable<(IPartSpec part, double sign)> Changes()
        {
            for (int i = 0; i < Loadout.Slots.Count; i++)
            {
                var stock = Loadout.Slots[i].Stock;
                var now = Loadout.Fitted[i];
                if (now == stock) continue;
                if (now is ModuleKind m) yield return (ModuleSpec.Of(m), 1);
                if (stock is ModuleKind was) yield return (ModuleSpec.Of(was), -1);
            }
            foreach (var f in Loadout.AddedFacilities) if (f.Def != null) yield return (f.Def, 1);
            foreach (var f in Loadout.RemovedFacilities) if (f.Def != null) yield return (f.Def, -1);
        }

        /// <summary>
        /// The materials the build takes, tonnes by good: the frame's alloys (its build points, which cover the design
        /// as drawn), plus every part fitted over the design, less what the design's own parts taken out would have used.
        /// </summary>
        public Dictionary<Good, double> Bill()
        {
            var bill = new Dictionary<Good, double> { [Good.Alloys] = Frame.BuildPoints };
            double scale = Loadout.Scale;
            foreach (var (part, sign) in Changes())
                for (int i = 0; i < part.Bill.Length; i++)
                {
                    var g = part.Bill[i].good;
                    bill.TryGetValue(g, out double t);
                    bill[g] = t + sign * part.Tonnes(i, scale);
                }
            var goods = new List<Good>(bill.Keys);
            foreach (var g in goods) if (bill[g] < 0) bill[g] = 0;
            return bill;
        }

        /// <summary>The build's price at base prices: the frame's yard price plus the base worth of the parts fitted over
        /// the design, less those taken out.</summary>
        public double BasePrice()
        {
            double price = Frame.BuildPoints * HullFrames.PricePerBuildPoint;
            double scale = Loadout.Scale;
            foreach (var (part, sign) in Changes())
                price += sign > 0 ? Worth(part, scale) * (1 + Outfitting.LabourShare) : -Worth(part, scale);
            return Math.Max(0, price);
        }

        static double Worth(IPartSpec spec, double scale)
        {
            double w = 0;
            for (int i = 0; i < spec.Bill.Length; i++) w += spec.Tonnes(i, scale) * GoodInfo.Of(spec.Bill[i].good).BasePrice;
            return w;
        }

        /// <summary>The hull's inside as planned: its facilities' compartments, with the design's crew and marines.</summary>
        Interior InteriorOf(CombatSpec spec) => Interior.For(Loadout.Facilities, spec.Crew, spec.Marines);

        /// <summary>A static structure's (or any hull's) combat state as planned, whole and loaded.</summary>
        public ShipCombat BuildCombat()
        {
            var spec = Build();
            return new ShipCombat(spec, Frame.Class, InteriorOf(spec));
        }

        /// <summary>Makes a newly built ship this plan: its loadout, its combat state and its hold. False if the frame
        /// builds no ship (a station or satellite) or not one of this ship's class.</summary>
        public bool Apply(Ship ship)
        {
            if (ship == null || Frame.Class == null || ship.Spec == null || ship.Spec.Class != Frame.Class) return false;
            ship.Loadout = Loadout.Copy();
            var spec = ship.Loadout.Build();
            ship.Combat = new ShipCombat(spec, Frame.Class, InteriorOf(spec));
            int racks = ship.Loadout.Count(ModuleKind.CargoRack);
            if (Frame.CapacityTonnes > 0) ship.CapacityTonnes = Frame.CapacityTonnes + Outfitting.RackTonnes(ship.Loadout, racks);
            if (racks > 0 && ship.ResizedHold(racks * Loadout.RackRows) is CargoHold hold) ship.SetHold(hold);
            return true;
        }
    }
}
