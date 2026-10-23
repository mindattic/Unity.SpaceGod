using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>A part a yard builds into a hull: what it is called, what it is built from and what it weighs. Both a
    /// hardpoint's module (<see cref="ModuleSpec"/>) and a facility (<see cref="FacilityKind"/>) are one.</summary>
    public interface IPartSpec
    {
        string Name { get; }

        /// <summary>The bill of materials, tonnes, for a hull of scale 1 (a destroyer) if the part grows with the hull.</summary>
        (Good good, double tonnes)[] Bill { get; }

        /// <summary>Materials for a hull of <paramref name="scale"/>, tonnes.</summary>
        double Tonnes(int i, double scale);

        /// <summary>Installed mass on a hull of <paramref name="scale"/>, tonnes.</summary>
        double Mass(double scale);
    }

    /// <summary>
    /// One kind of facility (U.SPC-T470): a thing inside the body of a ship, not on its skin. A facility has no
    /// position on the hull and no firing arc, only a size: the share of the hull's internal space it takes
    /// (<see cref="Loadout.InternalSpace"/>), in sections (one section is what an internal bay was before facilities).
    /// Every kind is data in <see cref="FacilityKinds"/>: its size, mass and bill of materials, which hull categories
    /// may fit it, whether a yard fits and strips it, the boarding compartment it gives the hull's interior, and what it
    /// does to the hull's fighting spec (<see cref="Effect"/>). A new facility (a marine habitat, an assault bay, a
    /// radiator, a drive room) is a new registered kind, never an edit to the code that fits the first.
    /// </summary>
    public sealed class FacilityKind : IPartSpec
    {
        /// <summary>A stable name for saves and bindings ("armour-plating", "hangar").</summary>
        public readonly string Id;

        public readonly string Name;

        /// <summary>Sections of internal space it takes. A hull whose free space is smaller cannot fit it at all.</summary>
        public readonly double Size;

        /// <summary>Installed mass, tonnes (per unit of hull scale if <see cref="Scaled"/>).</summary>
        public readonly double MassTonnes;

        /// <summary>Whether the bill and mass grow with the hull (<see cref="Loadout.Scale"/>) or are one fixed fitting.</summary>
        public readonly bool Scaled;

        public readonly (Good good, double tonnes)[] Bill;

        /// <summary>The hull categories that may fit it (null: any).</summary>
        public readonly HullCategory[] Categories;

        /// <summary>Whether a yard fits it into free space and strips it out again. False for the hull's structure as
        /// designed (its bridge, engineering, quarters, hangar and holds), which comes with the frame.</summary>
        public readonly bool Fittable;

        /// <summary>The boarding compartment it is in the hull's interior (<see cref="Interior.For(IReadOnlyList{FacilitySite}, double, double)"/>),
        /// or null for one that is not a space of its own (plate, racks, a scanner).</summary>
        public readonly CompartmentKind? Compartment;

        /// <summary>The module it is, for the facilities that were internal-bay modules before (U.SPC-T65): their effects
        /// are still read by <see cref="Loadout.Count(ModuleKind)"/> (plate in <see cref="Loadout.Build"/>, racks in the
        /// holds, scanners in <see cref="SensorSuite"/>, a brig's berths).</summary>
        public readonly ModuleKind? Module;

        /// <summary>
        /// What one of it does to the hull's fighting spec as <see cref="Loadout.Build"/> makes it, beyond its mass:
        /// called with +1 for each fitted over the design and -1 for each of the design's own taken out. Null for one
        /// whose effect is read elsewhere (by <see cref="Module"/>, or by the system that uses it).
        /// </summary>
        public readonly Action<CombatSpec, Loadout, int> Effect;

        public FacilityKind(string id, string name, double size, double massTonnes, bool scaled, (Good good, double tonnes)[] bill,
                            HullCategory[] categories = null, bool fittable = true, CompartmentKind? compartment = null,
                            ModuleKind? module = null, Action<CombatSpec, Loadout, int> effect = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? id;
            Size = Math.Max(0, size);
            MassTonnes = massTonnes;
            Scaled = scaled;
            Bill = bill ?? Array.Empty<(Good, double)>();
            Categories = categories;
            Fittable = fittable;
            Compartment = compartment;
            Module = module;
            Effect = effect;
        }

        /// <summary>A facility that is one of the former internal-bay modules, with that module's name, bill and mass.</summary>
        public static FacilityKind OfModule(string id, ModuleKind module, double size, HullCategory[] categories = null)
        {
            var m = ModuleSpec.Of(module);
            return new FacilityKind(id, m.Name, size, m.MassTonnes, m.Scaled, m.Bill, categories, true, null, module);
        }

        string IPartSpec.Name => Name;
        (Good good, double tonnes)[] IPartSpec.Bill => Bill;

        public double Tonnes(int i, double scale) => Bill[i].tonnes * (Scaled ? scale : 1);

        public double Mass(double scale) => MassTonnes * (Scaled ? scale : 1);

        /// <summary>Whether a hull of this category may carry it at all.</summary>
        public bool AllowedOn(HullCategory category)
        {
            if (Categories == null) return true;
            foreach (var c in Categories) if (c == category) return true;
            return false;
        }
    }

    /// <summary>
    /// The registry of facility kinds (U.SPC-T470). The hull's structure (bridge, engineering, quarters, hangar deck,
    /// holds, batteries' magazines, the bow and midships sections, a breacher's assault bay) and the four fittable
    /// facilities that were internal-bay modules (armour plate, cargo racks, a scanner suite, a brig) are registered
    /// here; new kinds plug in with <see cref="Register"/>.
    /// </summary>
    public static class FacilityKinds
    {
        static readonly List<FacilityKind> All = new List<FacilityKind>();
        static readonly Dictionary<string, FacilityKind> ById = new Dictionary<string, FacilityKind>(StringComparer.OrdinalIgnoreCase);

        // The structure every hull is built around (not fittable: it comes with the frame).
        public const string Bridge = "bridge", Engineering = "engineering", Quarters = "quarters", Hangar = "hangar", Hold = "hold",
                            Battery = "battery", Bow = "bow", Midships = "midships", AssaultBay = "assault-bay";

        // What a yard fits into free space.
        public const string ArmourPlating = "armour-plating", CargoRacks = "cargo-racks", Scanner = "scanner-suite", Brig = "brig";

        static FacilityKinds()
        {
            Register(Structure(Bridge, "Bridge", 1, CompartmentKind.Bridge));
            Register(Structure(Engineering, "Engineering", 2, CompartmentKind.Engineering));
            Register(Structure(Quarters, "Crew quarters", 2, CompartmentKind.Quarters));
            Register(Structure(Hangar, "Hangar deck", 4, CompartmentKind.Hangar));
            Register(Structure(Hold, "Hold", 3, CompartmentKind.Hold));
            Register(Structure(Battery, "Battery magazine", 2, CompartmentKind.Battery));
            Register(Structure(Bow, "Bow section", 1, CompartmentKind.Bow));
            Register(Structure(Midships, "Midships", 1, CompartmentKind.Midships));
            Register(Structure(AssaultBay, "Assault bay", 2, CompartmentKind.AssaultBay));

            Register(FacilityKind.OfModule(ArmourPlating, ModuleKind.ArmourPlating, 1));
            Register(FacilityKind.OfModule(CargoRacks, ModuleKind.CargoRack, 1));
            Register(FacilityKind.OfModule(Scanner, ModuleKind.Scanner, 1));
            Register(FacilityKind.OfModule(Brig, ModuleKind.Brig, 1));
        }

        static FacilityKind Structure(string id, string name, double size, CompartmentKind compartment) =>
            new FacilityKind(id, name, size, 0, false, null, null, false, compartment);

        /// <summary>Adds a kind (or replaces the one with its id).</summary>
        public static void Register(FacilityKind kind)
        {
            if (kind == null) throw new ArgumentNullException(nameof(kind));
            if (ById.TryGetValue(kind.Id, out var old)) All.Remove(old);
            ById[kind.Id] = kind;
            All.Add(kind);
        }

        public static IReadOnlyList<FacilityKind> Catalogue => All;

        public static FacilityKind Of(string id) => id != null && ById.TryGetValue(id, out var k) ? k : null;

        /// <summary>The facility that is <paramref name="module"/>, or null if the module goes on a hardpoint.</summary>
        public static FacilityKind ForModule(ModuleKind module)
        {
            foreach (var k in All) if (k.Module == module) return k;
            return null;
        }
    }

    /// <summary>
    /// One facility in a hull (U.SPC-T470): its kind, the name its space goes by aboard ("Forward hold", "Cockpit"), where
    /// that space lies for the boarding interior (ship lengths along the keel, nose +0.5, and height) and how it joins the
    /// rest: on to the previous space along the keel (<see cref="Keel"/>), off another of the layout's facilities, or, for
    /// one a yard fitted later, off midships (<see cref="Amidships"/>). It has no firing arc: only a hardpoint
    /// (<see cref="Hardpoint"/>) has one.
    /// </summary>
    public sealed class FacilitySite
    {
        public readonly string Kind;
        public readonly string Name;
        public readonly double Z, Y;

        /// <summary><see cref="Keel"/>: the next space along the keel from the one before it; <see cref="Amidships"/>: a
        /// space opening off midships; otherwise the index of the facility in the same layout it branches off.</summary>
        public readonly int Link;

        public const int Keel = -1, Amidships = -2;

        public FacilitySite(string kind, string name = null, double z = 0, double y = 0, int link = Keel)
        {
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            Name = name ?? FacilityKinds.Of(kind)?.Name ?? kind;
            Z = z;
            Y = y;
            Link = link;
        }

        /// <summary>A facility a yard fits into a hull's free space (it opens off midships, if it is a space at all).</summary>
        public static FacilitySite Fitted(FacilityKind kind) => new FacilitySite(kind.Id, kind.Name, 0, 0, Amidships);

        public FacilityKind Def => FacilityKinds.Of(Kind);

        /// <summary>The space it takes, sections.</summary>
        public double Size => Def?.Size ?? 0;
    }

    /// <summary>
    /// The facilities each hull is designed with (U.SPC-T470): its bridge, engineering, quarters, hangar, holds and the
    /// rest, laid out along the keel as the boarding interior has them, and the free space each class has for fitted
    /// facilities beyond them. A new frame can bring its own layout (<see cref="HullFrame"/>); a new class registers one
    /// with <see cref="Register"/>.
    /// </summary>
    public static class FacilityLayouts
    {
        static readonly Dictionary<ShipClass, FacilitySite[]> ByClass = new Dictionary<ShipClass, FacilitySite[]>
        {
            [ShipClass.Carrier] = new[]
            {
                new FacilitySite(FacilityKinds.Bow, "Bow", 0.4), new FacilitySite(FacilityKinds.Hangar, "Hangar deck", 0.15),
                new FacilitySite(FacilityKinds.Quarters, "Crew quarters", -0.02), new FacilitySite(FacilityKinds.Midships, "Midships", -0.18),
                new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.4), new FacilitySite(FacilityKinds.Bridge, "Island bridge", -0.11, 0.14, 3),
            },
            [ShipClass.Battleship] = new[]
            {
                new FacilitySite(FacilityKinds.Bow, "Bow", 0.38), new FacilitySite(FacilityKinds.Battery, "Forward battery", 0.16),
                new FacilitySite(FacilityKinds.Quarters, "Crew quarters", 0.0), new FacilitySite(FacilityKinds.Midships, "Midships", -0.16),
                new FacilitySite(FacilityKinds.Battery, "Aft battery", -0.28), new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.42),
                new FacilitySite(FacilityKinds.Bridge, "Bridge", -0.11, 0.1, 3),
            },
            [ShipClass.Cruiser] = new[]
            {
                new FacilitySite(FacilityKinds.Bow, "Bow", 0.3), new FacilitySite(FacilityKinds.Quarters, "Crew quarters", 0.05),
                new FacilitySite(FacilityKinds.Midships, "Midships", -0.12), new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.38),
                new FacilitySite(FacilityKinds.Bridge, "Bridge", -0.17, 0.06, 2),
            },
            [ShipClass.Destroyer] = new[]
            {
                new FacilitySite(FacilityKinds.Bow, "Bow", 0.25), new FacilitySite(FacilityKinds.Midships, "Midships", -0.05),
                new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.36), new FacilitySite(FacilityKinds.Bridge, "Bridge", -0.08, 0.05, 1),
            },
            // The lane interdictor (U.SPC-T479): its projector room amidships inside the ring, between its two reactors.
            [ShipClass.Interdictor] = new[]
            {
                new FacilitySite(FacilityKinds.Bow, "Bow", 0.3), new FacilitySite(FacilityKinds.Midships, "Projector room", -0.05),
                new FacilitySite(FacilityKinds.Engineering, "Reactor rooms", -0.3), new FacilitySite(FacilityKinds.Bridge, "Bridge", 0.18, 0.05, 1),
            },
            [ShipClass.Freighter] = new[]
            {
                new FacilitySite(FacilityKinds.Bridge, "Cockpit", 0.43), new FacilitySite(FacilityKinds.Hold, "Forward hold", 0.15),
                new FacilitySite(FacilityKinds.Hold, "Aft hold", -0.15), new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.42),
            },
            [ShipClass.Miner] = new[]
            {
                new FacilitySite(FacilityKinds.Bridge, "Cockpit", 0.3, 0.05), new FacilitySite(FacilityKinds.Hold, "Ore hold", 0),
                new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.35),
            },
            // No hold: the claw gantry forward, the cockpit over it, then a long engine room for its big drive.
            [ShipClass.OreTug] = new[]
            {
                new FacilitySite(FacilityKinds.Bow, "Claw gantry", 0.35), new FacilitySite(FacilityKinds.Bridge, "Cockpit", 0.15, 0.05),
                new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.3),
            },
            // A work boat (U.SPC-T481): the bridge forward, the workshop that services a lane's rings, a big engine room.
            [ShipClass.LaneTender] = new[]
            {
                new FacilitySite(FacilityKinds.Bridge, "Bridge", 0.32, 0.05), new FacilitySite(FacilityKinds.Midships, "Ring workshop", 0.05),
                new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.3),
            },
            // Multi-crew (U.SPC-T467): the flight deck, then the gun deck its turret crews work from.
            [ShipClass.Gunship] = new[]
            {
                new FacilitySite(FacilityKinds.Bridge, "Flight deck", 0.2, 0.03), new FacilitySite(FacilityKinds.Battery, "Gun deck", 0),
                new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.3),
            },
            [ShipClass.Breacher] = new[]
            {
                new FacilitySite(FacilityKinds.AssaultBay, "Assault bay", 0.25), new FacilitySite(FacilityKinds.Bridge, "Bridge", 0, 0.03),
                new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.3),
            },
        };

        /// <summary>A small hull's: a cockpit and its engineering.</summary>
        public static readonly FacilitySite[] Small =
        {
            new FacilitySite(FacilityKinds.Bridge, "Cockpit", 0.1), new FacilitySite(FacilityKinds.Engineering, "Engineering", -0.3),
        };

        /// <summary>A station's or satellite's: a gun deck around a control room and its reactor.</summary>
        public static readonly FacilitySite[] Platform =
        {
            new FacilitySite(FacilityKinds.Battery, "Gun deck", 0.15, 0.15), new FacilitySite(FacilityKinds.Bridge, "Control", 0, 0),
            new FacilitySite(FacilityKinds.Engineering, "Reactor", -0.15, -0.15),
        };

        /// <summary>The facilities a ship of the class is designed with.</summary>
        public static IReadOnlyList<FacilitySite> For(ShipClass c) => ByClass.TryGetValue(c, out var l) ? l : Small;

        /// <summary>Sets (or replaces) the facilities a class is designed with.</summary>
        public static void Register(ShipClass c, FacilitySite[] layout) => ByClass[c] = layout ?? throw new ArgumentNullException(nameof(layout));

        /// <summary>Free internal space a hull of each class has for fitted facilities beyond its structure, sections
        /// (as many as it had internal bays before U.SPC-T470).</summary>
        public static double SpareSpace(ShipClass c)
        {
            switch (c)
            {
                case ShipClass.Carrier:
                case ShipClass.Battleship: return 6;
                case ShipClass.Cruiser:
                case ShipClass.Freighter: return 4;
                case ShipClass.Destroyer:
                case ShipClass.Miner:
                case ShipClass.Explorer:
                case ShipClass.LaneTender:
                case ShipClass.Interdictor: return 3;
                case ShipClass.Breacher:
                case ShipClass.Gunship:
                case ShipClass.Courier:
                case ShipClass.OreTug: return 2;
                default: return 1;
            }
        }

        /// <summary>The space a layout's facilities take, sections.</summary>
        public static double SpaceOf(IEnumerable<FacilitySite> layout)
        {
            double n = 0;
            if (layout != null) foreach (var s in layout) n += s.Size;
            return n;
        }
    }
}
