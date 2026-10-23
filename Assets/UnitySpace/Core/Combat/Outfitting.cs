using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>What a yard can fit into a hull (U.SPC-T65).</summary>
    public enum ModuleKind
    {
        LaserTurret,
        HeavyLaser,
        Railgun,
        MissileLauncher,
        PointDefense,

        /// <summary>A stock shield generator, as the design was built with.</summary>
        ShieldGenerator,

        /// <summary>A heavier generator: half again the stock one's share of shield and regen.</summary>
        HardenedShield,

        /// <summary>A stock fusion drive, as the design was built with.</summary>
        Engine,

        /// <summary>A bigger drive: 30% more thrust, at a little more mass.</summary>
        TorchEngine,

        /// <summary>Plate bolted over the hull: more hull points and armour, and mass to push.</summary>
        ArmourPlating,

        /// <summary>Racks in the hull's free space (a facility, U.SPC-T470): more trade hold and two more rows in the grid hold.</summary>
        CargoRack,

        /// <summary>A sensor suite: half the hull's base scanner range again and finer resolution (see <see cref="SensorSuite.Of"/>),
        /// so contacts are seen further off and made out sooner, and a warship's sweep for hidden havens reaches a million km further.</summary>
        Scanner,

        /// <summary>Cells for prisoners taken off prizes, handed over in port for bounty or ransom.</summary>
        Brig,

        // The dogfight arsenal (U.SPC-T271).
        Autocannon,
        LightRailgun,
        EmpLauncher,
        MineLayer,
        AutoTurret,

        /// <summary>A capital torpedo tube (U.SPC-T377), with its first six rounds.</summary>
        TorpedoTube,

        /// <summary>A capital ship's spinal tachyon lance (U.SPC-T487, see <see cref="TachyonLances"/>): a bow mount of a
        /// capital hull only, built only at the greatest yards, and dear.</summary>
        TachyonLance,

        /// <summary>A phaser-style beam array (U.SPC-T488): destroyers and cruisers only, on a weapon or light mount.</summary>
        PhaserArray,

        /// <summary>A plasma cannon (U.SPC-T488): the brawlers' heavy gun (battleships, breachers), on a weapon mount.</summary>
        PlasmaCannon,
    }

    /// <summary>The kinds of hardpoint a hull offers on its surface: its weapon hardpoints (light ones take point defence, a
    /// laser or an autocannon, heavy ones any turret), its shield and engine mounts, a small craft's gun mounts (an
    /// autocannon, a light railgun or a laser) and utility mount (missiles, an EMP launcher, mines or an auto turret)
    /// (U.SPC-T271), and a capital hull's torpedo tubes (a tube or a missile launcher, U.SPC-T377). What is inside the
    /// hull is not a slot but a facility (<see cref="FacilityKind"/>, U.SPC-T470).</summary>
    public enum SlotKind { LightMount, HeavyMount, ShieldMount, EngineMount, GunMount, UtilityMount, TorpedoTube }

    /// <summary>One module: what it is called, what a yard builds it from (tonnes of alloys, electronics,
    /// machinery and munitions, scaled with the hull for plate, drives, generators and racks) and its mass.</summary>
    public sealed class ModuleSpec : IPartSpec
    {
        public readonly ModuleKind Kind;
        public readonly string Name;

        /// <summary>The bill of materials, tonnes, for a hull of scale 1 (a destroyer) if <see cref="Scaled"/>.</summary>
        public readonly (Good good, double tonnes)[] Bill;

        /// <summary>Installed mass, tonnes (per unit of hull scale if <see cref="Scaled"/>).</summary>
        public readonly double MassTonnes;

        /// <summary>Whether the bill and mass grow with the hull (plate, drives, generators, racks) or are one fixed part (a gun).</summary>
        public readonly bool Scaled;

        ModuleSpec(ModuleKind kind, string name, double mass, bool scaled, params (Good, double)[] bill)
        {
            Kind = kind;
            Name = name;
            MassTonnes = mass;
            Scaled = scaled;
            Bill = bill;
        }

        static readonly ModuleSpec[] All =
        {
            new ModuleSpec(ModuleKind.LaserTurret, "Laser turret", 120, false, (Good.Alloys, 40), (Good.Electronics, 12), (Good.Machinery, 6)),
            new ModuleSpec(ModuleKind.HeavyLaser, "Heavy laser", 300, false, (Good.Alloys, 90), (Good.Electronics, 30), (Good.Machinery, 14)),
            new ModuleSpec(ModuleKind.Railgun, "Railgun", 420, false, (Good.Alloys, 120), (Good.Electronics, 20), (Good.Machinery, 30)),
            // A launcher comes with its first full load: 16 rounds at the 1.5 t a round a yard rearms at.
            new ModuleSpec(ModuleKind.MissileLauncher, "Missile launcher", 160, false, (Good.Alloys, 50), (Good.Electronics, 15), (Good.Machinery, 10), (Good.Munitions, 24)),
            new ModuleSpec(ModuleKind.PointDefense, "Point defence", 40, false, (Good.Alloys, 15), (Good.Electronics, 8), (Good.Machinery, 3), (Good.Munitions, 2)),
            new ModuleSpec(ModuleKind.ShieldGenerator, "Shield generator", 60, true, (Good.Alloys, 30), (Good.Electronics, 20)),
            new ModuleSpec(ModuleKind.HardenedShield, "Hardened shield", 90, true, (Good.Alloys, 50), (Good.Electronics, 45), (Good.Machinery, 5)),
            new ModuleSpec(ModuleKind.Engine, "Fusion drive", 150, true, (Good.Alloys, 60), (Good.Machinery, 30), (Good.Electronics, 5)),
            new ModuleSpec(ModuleKind.TorchEngine, "Torch drive", 190, true, (Good.Alloys, 90), (Good.Machinery, 55), (Good.Electronics, 12)),
            new ModuleSpec(ModuleKind.ArmourPlating, "Armour plating", 450, true, (Good.Alloys, 300), (Good.Machinery, 4)),
            new ModuleSpec(ModuleKind.CargoRack, "Cargo racks", 120, true, (Good.Alloys, 80), (Good.Machinery, 10)),
            new ModuleSpec(ModuleKind.Scanner, "Scanner suite", 25, false, (Good.Electronics, 25), (Good.Alloys, 10)),
            new ModuleSpec(ModuleKind.Brig, "Brig", 80, false, (Good.Alloys, 40), (Good.Electronics, 6), (Good.Machinery, 4)),
            new ModuleSpec(ModuleKind.Autocannon, "Autocannon", 20, false, (Good.Alloys, 8), (Good.Machinery, 3), (Good.Munitions, 2)),
            new ModuleSpec(ModuleKind.LightRailgun, "Light railgun", 35, false, (Good.Alloys, 12), (Good.Electronics, 6), (Good.Machinery, 5)),
            new ModuleSpec(ModuleKind.EmpLauncher, "EMP launcher", 25, false, (Good.Alloys, 6), (Good.Electronics, 14), (Good.Munitions, 2)),
            new ModuleSpec(ModuleKind.MineLayer, "Mine layer", 40, false, (Good.Alloys, 10), (Good.Machinery, 4), (Good.Munitions, 9)),
            new ModuleSpec(ModuleKind.AutoTurret, "Auto turret", 30, false, (Good.Alloys, 10), (Good.Electronics, 9), (Good.Machinery, 4), (Good.Munitions, 2)),
            // A tube comes with its six torpedoes at 6 t a round.
            new ModuleSpec(ModuleKind.TorpedoTube, "Torpedo tube", 260, false, (Good.Alloys, 70), (Good.Electronics, 18), (Good.Machinery, 16), (Good.Munitions, 36)),
            // The tachyon lance (U.SPC-T487): its own power plant and capacitor banks, and the platinum and rare earths of
            // its emitter, some eight times a railgun's worth.
            new ModuleSpec(ModuleKind.TachyonLance, "Tachyon lance", 950, false, (Good.Alloys, 260), (Good.Electronics, 180), (Good.Machinery, 90), (Good.Platinum, 30), (Good.RareEarths, 45)),
            // Energy weapons (U.SPC-T488): an array is mostly emitters and their power conditioning; a plasma cannon a
            // magnetic bottle, its injectors and heavy plate round both.
            new ModuleSpec(ModuleKind.PhaserArray, "Phaser array", 200, false, (Good.Alloys, 55), (Good.Electronics, 40), (Good.Machinery, 10)),
            new ModuleSpec(ModuleKind.PlasmaCannon, "Plasma cannon", 380, false, (Good.Alloys, 120), (Good.Electronics, 30), (Good.Machinery, 28)),
        };

        public static ModuleSpec Of(ModuleKind k) => All[(int)k];

        /// <summary>Whether <paramref name="yard"/> can build <paramref name="m"/>: any yard builds anything but a tachyon
        /// lance, which only the greatest lawful yards can (<see cref="TachyonLances.YardBuilds"/>).</summary>
        public static bool YardBuilds(ModuleKind m, Settlement yard) => m != ModuleKind.TachyonLance || TachyonLances.YardBuilds(yard);

        public static IReadOnlyList<ModuleSpec> Catalogue => All;

        /// <summary>Materials for a hull of <paramref name="scale"/>, tonnes.</summary>
        public double Tonnes(int i, double scale) => Bill[i].tonnes * (Scaled ? scale : 1);

        public double Mass(double scale) => MassTonnes * (Scaled ? scale : 1);

        string IPartSpec.Name => Name;
        (Good good, double tonnes)[] IPartSpec.Bill => Bill;

        /// <summary>The facility this module is (plate, racks, a scanner suite, a brig: they go inside the hull, not on a
        /// hardpoint), or null.</summary>
        public FacilityKind Facility => FacilityKinds.ForModule(Kind);

        /// <summary>Whether a module goes in a slot (a railgun is spinal: only a mount that bears on the bow takes one).</summary>
        public static bool Fits(ModuleKind m, SlotKind slot, bool bearsOnBow)
        {
            switch (slot)
            {
                // A phaser array is light enough for a point-defence mount: a destroyer's screen sweeps fighters from one.
                case SlotKind.LightMount: return m == ModuleKind.PointDefense || m == ModuleKind.LaserTurret || m == ModuleKind.Autocannon || m == ModuleKind.AutoTurret || m == ModuleKind.PhaserArray;
                case SlotKind.HeavyMount:
                    return m == ModuleKind.LaserTurret || m == ModuleKind.HeavyLaser || m == ModuleKind.MissileLauncher ||
                           m == ModuleKind.PointDefense || m == ModuleKind.Autocannon || m == ModuleKind.LightRailgun ||
                           m == ModuleKind.PhaserArray || m == ModuleKind.PlasmaCannon ||
                           ((m == ModuleKind.Railgun || m == ModuleKind.TorpedoTube || m == ModuleKind.TachyonLance) && bearsOnBow);
                // A torpedo tube is a fixed launcher: it takes a tube or a missile launcher, nothing that traverses.
                case SlotKind.TorpedoTube: return m == ModuleKind.TorpedoTube || m == ModuleKind.MissileLauncher;
                case SlotKind.GunMount: return m == ModuleKind.Autocannon || m == ModuleKind.LightRailgun || m == ModuleKind.LaserTurret;
                case SlotKind.UtilityMount:
                    return m == ModuleKind.MissileLauncher || m == ModuleKind.EmpLauncher || m == ModuleKind.MineLayer || m == ModuleKind.AutoTurret;
                case SlotKind.ShieldMount: return m == ModuleKind.ShieldGenerator || m == ModuleKind.HardenedShield;
                case SlotKind.EngineMount: return m == ModuleKind.Engine || m == ModuleKind.TorchEngine;
                default: return false;
            }
        }

        /// <summary>The hardpoint kind a mount module becomes in the hull's layout.</summary>
        public static SubsystemKind SubsystemOf(ModuleKind m)
        {
            switch (m)
            {
                case ModuleKind.LaserTurret: return SubsystemKind.LaserTurret;
                case ModuleKind.HeavyLaser: return SubsystemKind.HeavyLaser;
                case ModuleKind.Railgun: return SubsystemKind.Railgun;
                case ModuleKind.MissileLauncher: return SubsystemKind.MissileLauncher;
                case ModuleKind.PointDefense: return SubsystemKind.PointDefense;
                case ModuleKind.Autocannon: return SubsystemKind.Autocannon;
                case ModuleKind.LightRailgun: return SubsystemKind.LightRailgun;
                case ModuleKind.EmpLauncher: return SubsystemKind.EmpLauncher;
                case ModuleKind.MineLayer: return SubsystemKind.MineLayer;
                case ModuleKind.AutoTurret: return SubsystemKind.AutoTurret;
                case ModuleKind.TorpedoTube: return SubsystemKind.TorpedoTube;
                case ModuleKind.TachyonLance: return SubsystemKind.TachyonLance;
                case ModuleKind.PhaserArray: return SubsystemKind.PhaserArray;
                case ModuleKind.PlasmaCannon: return SubsystemKind.PlasmaCannon;
                case ModuleKind.ShieldGenerator:
                case ModuleKind.HardenedShield: return SubsystemKind.ShieldGenerator;
                default: return SubsystemKind.Engine;
            }
        }
    }

    /// <summary>One hardpoint slot of a hull: its kind, which hardpoint of the layout it is and what the design put there.</summary>
    public sealed class OutfitSlot
    {
        public readonly SlotKind Kind;

        /// <summary>The hardpoint's index in <see cref="CombatSpec.Layout"/>.</summary>
        public readonly int LayoutIndex;

        /// <summary>What the design was built with here.</summary>
        public readonly ModuleKind? Stock;

        /// <summary>A mount that bears on the bow, so it can take a spinal railgun.</summary>
        public readonly bool BearsOnBow;

        public OutfitSlot(SlotKind kind, int layoutIndex, ModuleKind? stock, bool bearsOnBow)
        {
            Kind = kind;
            LayoutIndex = layoutIndex;
            Stock = stock;
            BearsOnBow = bearsOnBow;
        }

        public string Name
        {
            get
            {
                switch (Kind)
                {
                    case SlotKind.LightMount: return "Light mount";
                    case SlotKind.HeavyMount: return "Weapon mount";
                    case SlotKind.GunMount: return "Gun mount";
                    case SlotKind.UtilityMount: return "Utility mount";
                    case SlotKind.TorpedoTube: return "Torpedo tube";
                    case SlotKind.ShieldMount: return "Shield mount";
                    case SlotKind.EngineMount: return "Engine mount";
                    default: return Kind.ToString();
                }
            }
        }
    }

    /// <summary>
    /// What is fitted into a hull (U.SPC-T65), over the design it was built as, in two kinds of part (U.SPC-T470).
    /// Its hardpoints (<see cref="Slots"/>) are places on the hull's surface with a position and an arc: each refittable
    /// mount of the design (guns and launchers, point defence, torpedo tubes, shield generators, engines). Its
    /// facilities (<see cref="Facilities"/>) are inside the body of the ship and have only a size: the structure the
    /// hull is designed with (bridge, engineering, quarters, hangar, holds) and whatever a yard fits into the free
    /// space left over (plate, racks, a scanner suite, a brig, and any facility kind registered later), each taking a
    /// share of the hull's limited <see cref="InternalSpace"/>. Bridges, reactors, hangars, drills, pod launchers and a
    /// fighter's guns are part of the hull and are not refitted. <see cref="Build"/> turns the loadout into the hull's
    /// <see cref="CombatSpec"/>: its hardpoints, shield, hull, armour, drive and mass.
    /// </summary>
    public sealed class Loadout
    {
        public readonly CombatSpec Base;

        /// <summary>The hull's class, or null for a station or satellite (U.SPC-T377), which has none.</summary>
        public readonly ShipClass? Class;

        /// <summary>What the hull is (U.SPC-T470), for the facilities only some hulls may fit.</summary>
        public readonly HullCategory Category;

        /// <summary>The hull's hardpoints.</summary>
        public readonly List<OutfitSlot> Slots = new List<OutfitSlot>();
        public readonly ModuleKind?[] Fitted;

        /// <summary>The facilities inside the hull now (U.SPC-T470): the design's own first, in its layout's order, then
        /// those fitted since.</summary>
        public readonly List<FacilitySite> Facilities = new List<FacilitySite>();

        /// <summary>The facilities the hull was designed with.</summary>
        public readonly IReadOnlyList<FacilitySite> StockFacilities;

        /// <summary>The hull's whole internal space, sections: its design's facilities and the free space beyond them.</summary>
        public readonly double InternalSpace;

        /// <summary>The trade hold the hull was built with, tonnes, before any racks.</summary>
        public readonly double BaseCapacityTonnes;

        /// <summary>A small craft, whose missile rack is a utility mount rather than a turret mount.</summary>
        public readonly bool SmallCraft;

        public Loadout(CombatSpec design, ShipClass cls, double baseCapacityTonnes)
            : this(design, cls, HullFrames.CategoryOf(cls), baseCapacityTonnes, IsSmallCraft(cls), FacilityLayouts.For(cls), FacilityLayouts.SpareSpace(cls)) { }

        /// <summary>A loadout for any built hull (U.SPC-T377): a ship of a class, or a station or satellite (no class),
        /// with the facilities it is designed with and the free space it has beyond them (sections).</summary>
        public Loadout(CombatSpec design, ShipClass? cls, HullCategory category, double baseCapacityTonnes, bool smallCraft,
                       IReadOnlyList<FacilitySite> facilities, double spareSpace)
        {
            Base = design;
            Class = cls;
            Category = category;
            BaseCapacityTonnes = baseCapacityTonnes;
            SmallCraft = smallCraft;
            for (int i = 0; i < design.Layout.Length; i++)
            {
                var d = design.Layout[i];
                var slot = SlotOf(d.Kind, smallCraft);
                if (slot == null) continue;
                Slots.Add(new OutfitSlot(slot.Value, i, StockOf(d.Kind), d.ContainsYaw(0)));
            }
            Fitted = new ModuleKind?[Slots.Count];
            for (int i = 0; i < Slots.Count; i++) Fitted[i] = Slots[i].Stock;
            StockFacilities = facilities ?? Array.Empty<FacilitySite>();
            Facilities.AddRange(StockFacilities);
            InternalSpace = FacilityLayouts.SpaceOf(StockFacilities) + Math.Max(0, spareSpace);
        }

        /// <summary>The hulls flown as fighters, whose missile rack is a utility mount.</summary>
        public static bool IsSmallCraft(ShipClass cls) =>
            cls == ShipClass.Fighter || cls == ShipClass.Interceptor || cls == ShipClass.Bomber || cls == ShipClass.Courier || cls == ShipClass.Gunship
            || cls == ShipClass.Breacher;

        /// <summary>The kind of slot a hardpoint of the design becomes, or null if it is part of the hull (a bridge, a reactor).</summary>
        public static SlotKind? SlotOf(SubsystemKind k, bool small)
        {
            switch (k)
            {
                // A small craft's missile rack is its utility mount, not a warship's turret mount.
                case SubsystemKind.MissileLauncher when small: return SlotKind.UtilityMount;
                case SubsystemKind.Autocannon:
                case SubsystemKind.LightRailgun: return SlotKind.GunMount;
                case SubsystemKind.EmpLauncher:
                case SubsystemKind.MineLayer:
                case SubsystemKind.AutoTurret: return SlotKind.UtilityMount;
                case SubsystemKind.PointDefense: return SlotKind.LightMount;
                case SubsystemKind.LaserTurret:
                case SubsystemKind.HeavyLaser:
                case SubsystemKind.MissileLauncher:
                case SubsystemKind.PhaserArray:
                case SubsystemKind.PlasmaCannon:
                case SubsystemKind.Railgun:
                case SubsystemKind.TachyonLance: return SlotKind.HeavyMount;
                case SubsystemKind.ShieldGenerator: return SlotKind.ShieldMount;
                case SubsystemKind.Engine: return SlotKind.EngineMount;
                case SubsystemKind.TorpedoTube: return SlotKind.TorpedoTube;
                default: return null;
            }
        }

        static ModuleKind? StockOf(SubsystemKind k)
        {
            switch (k)
            {
                case SubsystemKind.PointDefense: return ModuleKind.PointDefense;
                case SubsystemKind.LaserTurret: return ModuleKind.LaserTurret;
                case SubsystemKind.HeavyLaser: return ModuleKind.HeavyLaser;
                case SubsystemKind.MissileLauncher: return ModuleKind.MissileLauncher;
                case SubsystemKind.Railgun: return ModuleKind.Railgun;
                case SubsystemKind.ShieldGenerator: return ModuleKind.ShieldGenerator;
                case SubsystemKind.Engine: return ModuleKind.Engine;
                case SubsystemKind.Autocannon: return ModuleKind.Autocannon;
                case SubsystemKind.LightRailgun: return ModuleKind.LightRailgun;
                case SubsystemKind.EmpLauncher: return ModuleKind.EmpLauncher;
                case SubsystemKind.MineLayer: return ModuleKind.MineLayer;
                case SubsystemKind.AutoTurret: return ModuleKind.AutoTurret;
                case SubsystemKind.TorpedoTube: return ModuleKind.TorpedoTube;
                case SubsystemKind.TachyonLance: return ModuleKind.TachyonLance;
                case SubsystemKind.PhaserArray: return ModuleKind.PhaserArray;
                case SubsystemKind.PlasmaCannon: return ModuleKind.PlasmaCannon;
                default: return null;
            }
        }

        /// <summary>How big the hull is for plate, drives, generators and racks: 1 for a destroyer (9,000 t dry),
        /// about 4 for a carrier, a sixth for a courier.</summary>
        public double Scale => HullScale(Base);

        public static double HullScale(CombatSpec design) => Math.Max(0.05, Math.Sqrt(Math.Max(0, design.DryMassTonnes) / 9000));

        /// <summary>Internal space the facilities take now, sections.</summary>
        public double UsedSpace => FacilityLayouts.SpaceOf(Facilities);

        /// <summary>Internal space left for another facility, sections.</summary>
        public double FreeSpace => Math.Max(0, InternalSpace - UsedSpace);

        /// <summary>Hardpoints fitted with <paramref name="k"/>, plus facilities that are that module (plate, racks, a
        /// scanner suite, a brig).</summary>
        public int Count(ModuleKind k)
        {
            int n = 0;
            foreach (var f in Fitted) if (f == k) n++;
            foreach (var f in Facilities) if (f.Def?.Module == k) n++;
            return n;
        }

        /// <summary>Facilities of kind <paramref name="id"/> in the hull.</summary>
        public int CountFacility(string id)
        {
            int n = 0;
            foreach (var f in Facilities) if (string.Equals(f.Kind, id, StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }

        /// <summary>The design's own facilities no longer in the hull.</summary>
        public IEnumerable<FacilitySite> RemovedFacilities
        {
            get
            {
                foreach (var s in StockFacilities) if (!Facilities.Contains(s)) yield return s;
            }
        }

        /// <summary>The facilities fitted over the design.</summary>
        public IEnumerable<FacilitySite> AddedFacilities
        {
            get
            {
                foreach (var s in Facilities) if (!IsStockFacility(s)) yield return s;
            }
        }

        /// <summary>Whether a facility in the hull is one of the design's own.</summary>
        public bool IsStockFacility(FacilitySite s)
        {
            foreach (var x in StockFacilities) if (ReferenceEquals(x, s)) return true;
            return false;
        }

        /// <summary>Whether anything differs from the design as built.</summary>
        public bool IsStock
        {
            get
            {
                for (int i = 0; i < Slots.Count; i++) if (Fitted[i] != Slots[i].Stock) return false;
                if (Facilities.Count != StockFacilities.Count) return false;
                for (int i = 0; i < Facilities.Count; i++) if (!ReferenceEquals(Facilities[i], StockFacilities[i])) return false;
                return true;
            }
        }

        /// <summary>Armour each plate adds (flat damage off every hit) and the share of the design's hull points.</summary>
        public const double ArmourPerPlate = 2, HullPerPlate = 0.08;

        /// <summary>A hardened generator's grade, and a torch drive's, against stock.</summary>
        public const double HardenedRating = 1.5, TorchRating = 1.3;

        /// <summary>Extra trade hold each rack adds, as a share of the hull's own, and grid hold rows.</summary>
        public const double RackCapacityShare = 0.15;
        public const int RackRows = 2;

        /// <summary>Extra reach each scanner suite gives a warship's sweep for hidden havens, km (half its base
        /// <see cref="SensorSuite.Military"/> range on a <see cref="Signatures.Haven"/> signature).</summary>
        public const double ScannerRangeKm = 1e6;

        /// <summary>Prisoners each brig holds (more on a bigger hull).</summary>
        public int BrigBerthsEach => (int)Math.Round(20 * Math.Max(1, Scale));

        /// <summary>Dry mass the loadout adds over the design as built, tonnes.</summary>
        public double AddedMassTonnes
        {
            get
            {
                double scale = Scale, mass = 0;
                for (int i = 0; i < Slots.Count; i++)
                {
                    if (Fitted[i] == Slots[i].Stock) continue;
                    if (Fitted[i] is ModuleKind now) mass += ModuleSpec.Of(now).Mass(scale);
                    if (Slots[i].Stock is ModuleKind was) mass -= ModuleSpec.Of(was).Mass(scale);
                }
                foreach (var f in AddedFacilities) mass += f.Def?.Mass(scale) ?? 0;
                foreach (var f in RemovedFacilities) mass -= f.Def?.Mass(scale) ?? 0;
                return mass;
            }
        }

        /// <summary>
        /// The hull's fighting spec with this loadout: each refitted hardpoint swapped in place (same position and
        /// arc), shield and regen shared by the generators' grades, hull and armour raised by plate, each facility's own
        /// <see cref="FacilityKind.Effect"/>, the drive's thrust by its engines' mean grade, and every added tonne of dry
        /// mass slowing it.
        /// </summary>
        public CombatSpec Build()
        {
            if (IsStock) return Base;
            var spec = Base.Clone();
            double scale = Scale;
            int baseGenerators = 0, engines = 0;
            double ratings = 0, thrust = 0;
            foreach (var d in Base.Layout) if (d.Kind == SubsystemKind.ShieldGenerator) baseGenerators++;
            for (int i = 0; i < Slots.Count; i++)
            {
                var slot = Slots[i];
                if (!(Fitted[i] is ModuleKind m)) continue;
                double rating = m == ModuleKind.HardenedShield ? HardenedRating : m == ModuleKind.TorchEngine ? TorchRating : 1;
                if (slot.Kind == SlotKind.ShieldMount) ratings += rating;
                if (slot.Kind == SlotKind.EngineMount) { engines++; thrust += rating; }
                if (m == slot.Stock) continue;
                var was = Base.Layout[slot.LayoutIndex];
                var def = new SubsystemDef(ModuleSpec.SubsystemOf(m), was.X, was.Y, was.Z, was.FacesAft, was.ArcCenterDeg, was.ArcWidthDeg) { Rating = rating };
                if (m == ModuleKind.HardenedShield) def.MaxHp *= 1.4;
                if (m == ModuleKind.TorchEngine) def.MaxHp *= 1.2;
                // A lance is spinal: whatever the mount's arc was, it fires only down the bow (U.SPC-T487).
                if (m == ModuleKind.TachyonLance) def.ArcWidthDeg = Math.Min(was.ArcWidthDeg, TachyonLances.ArcWidthDeg);
                spec.Layout[slot.LayoutIndex] = def;
            }
            if (baseGenerators > 0)
            {
                spec.Shield = Base.Shield / baseGenerators * ratings;
                spec.ShieldRegen = Base.ShieldRegen / baseGenerators * ratings;
            }
            int plates = Count(ModuleKind.ArmourPlating);
            spec.Armor = Base.Armor + plates * ArmourPerPlate;
            spec.Hull = Base.Hull * (1 + plates * HullPerPlate);
            foreach (var f in AddedFacilities) f.Def?.Effect?.Invoke(spec, this, 1);
            foreach (var f in RemovedFacilities) f.Def?.Effect?.Invoke(spec, this, -1);

            double dry = Math.Max(1, Base.DryMassTonnes + AddedMassTonnes);
            double heavier = Base.DryMassTonnes > 0 ? dry / Base.DryMassTonnes : 1;
            double drive = engines > 0 ? thrust / engines : 1;
            spec.DryMassTonnes = dry;
            spec.MassKt = Base.MassKt * heavier;
            spec.MainThrustMN = Base.MainThrustMN * drive;
            spec.RetroThrustMN = Base.RetroThrustMN * drive;
            spec.Accel = Base.Accel * drive / heavier;
            spec.Decel = Base.Decel * drive / heavier;
            spec.MaxSpeed = Base.MaxSpeed * Math.Sqrt(drive / heavier);
            spec.TurnRateDegS = Base.TurnRateDegS / Math.Sqrt(heavier);
            return spec;
        }

        /// <summary>Why <paramref name="kind"/> could or could not go into this hull's free space now.</summary>
        public OutfitOutcome CanAdd(FacilityKind kind)
        {
            if (kind == null || !kind.Fittable || !kind.AllowedOn(Category)) return OutfitOutcome.WontFit;
            if (kind.Size > FreeSpace + 1e-9) return OutfitOutcome.NoRoom;
            return OutfitOutcome.Ok;
        }

        /// <summary>Whether the facility at <paramref name="index"/> may be taken out (a fittable one; never the
        /// structure the hull is built around).</summary>
        public bool CanRemove(int index) => index >= 0 && index < Facilities.Count && Facilities[index].Def?.Fittable == true;

        /// <summary>An independent copy, fitted the same way (a newly built hull's own loadout).</summary>
        public Loadout Copy()
        {
            var l = new Loadout(Base, Class, Category, BaseCapacityTonnes, SmallCraft, StockFacilities, InternalSpace - FacilityLayouts.SpaceOf(StockFacilities));
            Array.Copy(Fitted, l.Fitted, Fitted.Length);
            l.Facilities.Clear();
            l.Facilities.AddRange(Facilities);
            return l;
        }
    }

    /// <summary>Why a refit did or did not go through (<see cref="NoRoom"/>: the facility is bigger than the hull's free space).</summary>
    public enum OutfitOutcome { Ok, NotAYard, CannotDock, Busy, NoSuchSlot, WontFit, AlreadyFitted, NoStock, CantAfford, HoldTooFull, BrigOccupied, YardCannotBuild, NoRoom }

    /// <summary>What a refit costs at a yard: the new module's materials bought from its market, the yard's labour,
    /// and what the part taken out fetches sold back into it.</summary>
    public readonly struct OutfitQuote
    {
        public readonly double Materials, Labour, TradeIn;

        /// <summary>Whether the yard's market can spare every material the new module takes.</summary>
        public readonly bool InStock;

        /// <summary>The first material it is short of, when it is not <see cref="InStock"/>.</summary>
        public readonly Good Short;

        public OutfitQuote(double materials, double labour, double tradeIn, bool inStock, Good shortOf)
        {
            Materials = materials;
            Labour = labour;
            TradeIn = tradeIn;
            InStock = inStock;
            Short = shortOf;
        }

        /// <summary>What the owner pays, net of the trade-in (negative: the yard pays out).</summary>
        public double Net => Materials + Labour - TradeIn;
    }

    /// <summary>
    /// Ship outfitting at yards (U.SPC-T65). At a shipyard (or, for raiders, a haven) a ship's owner buys a part and has
    /// it fitted: on one of the hull's hardpoints a gun or launcher, a hardened generator on a shield mount, a torch drive
    /// on an engine mount (<see cref="Fit"/>); or, into the hull's free internal space, a facility (U.SPC-T470): armour
    /// plate, cargo racks, a scanner suite, a brig or any other registered <see cref="FacilityKind"/> the hull has room
    /// for (<see cref="FitFacility"/>, and <see cref="StripFacility"/> to take one out). The part's alloys, electronics,
    /// machinery and munitions come out of that yard's own market at its prices (so a yard short of electronics cannot
    /// fit shields, and one where they are dear charges for it), the yard's labour is a quarter of the materials' worth,
    /// and the part taken out is sold back into the market for half its materials. The hull's <see cref="CombatSpec"/>
    /// is rebuilt from the loadout (<see cref="Loadout.Build"/>), carrying its damage, people and stores over. The player
    /// does this at the helm; navies and raiders upgrade their warships while they refit at their own yards
    /// (<see cref="AiRefit"/>).
    /// </summary>
    public static class Outfitting
    {
        /// <summary>The yard's labour, as a share of the materials' base worth.</summary>
        public const double LabourShare = 0.25;

        /// <summary>Share of a removed part's materials a yard takes back (the rest is scrap).</summary>
        public const double TradeInShare = 0.5;

        /// <summary>Credits a pirate prisoner fetches a navy port as bounty, and anyone else's people as ransom.</summary>
        public const double PirateBounty = 50000, Ransom = 20000;

        /// <summary>Share of a taken prize's crew its captors lock up, as far as their brigs have room.</summary>
        public const double PrisonerShare = 0.5;

        /// <summary>A place that fits modules: any shipyard, and a raider haven's black-market slips.</summary>
        public static bool IsYard(Settlement s) => s != null && (s.HasShipyard || s.Kind == SettlementKind.Haven) && !s.Fallen;

        /// <summary>The ship's loadout, made from its design (stock) the first time it is asked for.</summary>
        public static Loadout LoadoutOf(Ship ship)
        {
            if (ship.Loadout == null && ship.Combat != null)
                ship.Loadout = new Loadout(ship.Combat.Spec, ship.Spec.Class, ship.CapacityTonnes);
            return ship.Loadout;
        }

        /// <summary>How far this ship's scanners pick out a hidden haven, km: its suite's reach on a haven's signature
        /// (<see cref="Sensors.HavenReachKm"/>; <see cref="War.ScanRangeKm"/> for a warship as built).</summary>
        public static double ScanRangeKm(Ship ship) => Sensors.HavenReachKm(ship);

        /// <summary>Prisoners this ship's brigs hold.</summary>
        public static double BrigCapacity(Ship ship) => ship?.Loadout == null ? 0 : ship.Loadout.Count(ModuleKind.Brig) * ship.Loadout.BrigBerthsEach;

        /// <summary>Free berths in this ship's brigs: their capacity less the prisoners and captains already in them.</summary>
        public static double BrigRoom(Ship ship) => ship == null ? 0 : Math.Max(0, BrigCapacity(ship) - ship.Prisoners - ship.CaptainsHeld);

        /// <summary>
        /// Fits <paramref name="module"/> as part of a new hull's build (no yard bill: the hull's price covers it): a
        /// facility module (a brig, plate, racks, a scanner) into the hull's free space, any other into the first empty
        /// hardpoint that takes it. False if there is no room for it.
        /// </summary>
        public static bool Install(Ship ship, ModuleKind module)
        {
            var facility = FacilityKinds.ForModule(module);
            if (facility != null) return Install(ship, facility);
            var l = LoadoutOf(ship);
            if (l == null) return false;
            for (int i = 0; i < l.Slots.Count; i++)
            {
                if (l.Fitted[i] != null || !CanFit(l, i, module)) continue;
                l.Fitted[i] = module;
                ship.Combat = ship.Combat.Refitted(l.Build(), l.Class, l.Slots[i].LayoutIndex);
                return true;
            }
            return false;
        }

        /// <summary>Fits a facility into the hull's free space as part of its build (no yard bill). False if it has no room
        /// for it or may not carry it.</summary>
        public static bool Install(Ship ship, FacilityKind kind)
        {
            var l = LoadoutOf(ship);
            if (l == null || l.CanAdd(kind) != OutfitOutcome.Ok) return false;
            l.Facilities.Add(FacilitySite.Fitted(kind));
            var spec = l.Build();
            ship.Combat = ship.Combat.Refitted(spec, l.Class, -1, InteriorAfter(ship, l, kind, spec));
            return true;
        }

        /// <summary>Extra trade hold this ship's racks give, tonnes.</summary>
        public static double RackTonnes(Loadout l, int racks) => l.BaseCapacityTonnes * Loadout.RackCapacityShare * racks;

        /// <summary>
        /// What fitting <paramref name="module"/> onto hardpoint <paramref name="slot"/> of the ship costs at
        /// <paramref name="yard"/>, at that market's prices and stock now.
        /// </summary>
        public static OutfitQuote Quote(Ship ship, Settlement yard, int slot, ModuleKind? module)
        {
            var l = LoadoutOf(ship);
            if (l == null || yard == null || slot < 0 || slot >= l.Slots.Count) return default;
            return Price(yard, l.Scale, module is ModuleKind m ? ModuleSpec.Of(m) : null,
                l.Fitted[slot] is ModuleKind old ? ModuleSpec.Of(old) : null, PartHealth(ship, l.Slots[slot]));
        }

        /// <summary>What fitting a facility of <paramref name="kind"/> into the ship's free space costs at <paramref name="yard"/>.</summary>
        public static OutfitQuote QuoteFacility(Ship ship, Settlement yard, FacilityKind kind)
        {
            var l = LoadoutOf(ship);
            if (l == null || yard == null || kind == null) return default;
            return Price(yard, l.Scale, kind, null, 1);
        }

        /// <summary>What taking out the ship's facility number <paramref name="index"/> fetches (or costs) at <paramref name="yard"/>.</summary>
        public static OutfitQuote QuoteStrip(Ship ship, Settlement yard, int index)
        {
            var l = LoadoutOf(ship);
            if (l == null || yard == null || index < 0 || index >= l.Facilities.Count) return default;
            return Price(yard, l.Scale, null, l.Facilities[index].Def, 1);
        }

        static OutfitQuote Price(Settlement yard, double scale, IPartSpec add, IPartSpec remove, double health)
        {
            double materials = 0, labour = 0, tradeIn = 0;
            bool inStock = true;
            Good shortOf = Good.Alloys;
            if (add != null)
                for (int i = 0; i < add.Bill.Length; i++)
                {
                    var g = add.Bill[i].good;
                    double t = add.Tonnes(i, scale);
                    materials += yard.PurchaseCost(g, t) * (1 + Economy.PortTax);
                    labour += t * GoodInfo.Of(g).BasePrice * LabourShare;
                    if (inStock && yard.Available(g) < t) { inStock = false; shortOf = g; }
                }
            if (remove != null)
                for (int i = 0; i < remove.Bill.Length; i++)
                    tradeIn += yard.SaleValue(remove.Bill[i].good, remove.Tonnes(i, scale) * TradeInShare * health) * (1 - Economy.PortTax);
            return new OutfitQuote(materials, labour, tradeIn, inStock, shortOf);
        }

        /// <summary>The health of what is fitted in a slot (a shot-away gun fetches nothing).</summary>
        static double PartHealth(Ship ship, OutfitSlot slot)
        {
            if (slot.LayoutIndex < 0 || ship.Combat == null || slot.LayoutIndex >= ship.Combat.Subsystems.Count) return 1;
            return ship.Combat.Subsystems[slot.LayoutIndex].Health;
        }

        /// <summary>Whether <paramref name="module"/> may go on hardpoint <paramref name="slot"/> (a hardpoint is never left
        /// empty; a facility goes into free space, <see cref="Loadout.CanAdd"/>).</summary>
        public static bool CanFit(Loadout l, int slot, ModuleKind? module)
        {
            if (l == null || slot < 0 || slot >= l.Slots.Count || !(module is ModuleKind m)) return false;
            var s = l.Slots[slot];
            // A tachyon lance goes only into a capital hull (U.SPC-T487).
            if (m == ModuleKind.TachyonLance && !TachyonLances.CanCarry(l.Class)) return false;
            // A role-bound weapon (a phaser array, a plasma cannon) goes only on the hulls of its role (U.SPC-T488).
            return ModuleSpec.Fits(m, s.Kind, s.BearsOnBow) && EnergyWeapons.RoleAllows(m, l.Class);
        }

        /// <summary>Whether the ship can be refitted at the yard now: a yard that lets it dock, out of battle and not boarded.</summary>
        static OutfitOutcome Ready(Economy economy, Ship ship, Settlement yard)
        {
            if (!IsYard(yard)) return OutfitOutcome.NotAYard;
            if (ship == null || ship.Combat == null || ship.Destroyed || ship.Derelict) return OutfitOutcome.Busy;
            if (economy != null && !economy.CanTrade(ship, yard)) return OutfitOutcome.CannotDock;
            if (ship.Battle != null || ship.Combat.Interior.UnderBoarding) return OutfitOutcome.Busy;
            return OutfitOutcome.Ok;
        }

        /// <summary>
        /// Fits <paramref name="module"/> onto hardpoint <paramref name="slot"/> of <paramref name="ship"/> at
        /// <paramref name="yard"/>: buys the materials from its market (they leave its stock and count as demand there),
        /// pays the yard's labour and the port's tax, sells the old part back, and rebuilds the hull's combat spec.
        /// Nothing changes unless it returns <see cref="OutfitOutcome.Ok"/>. The ship must be out of battle; the caller
        /// checks it is alongside the yard.
        /// </summary>
        public static OutfitOutcome Fit(Economy economy, Ship ship, Settlement yard, int slot, ModuleKind? module, out OutfitQuote quote)
        {
            quote = default;
            var ready = Ready(economy, ship, yard);
            if (ready != OutfitOutcome.Ok) return ready;
            var l = LoadoutOf(ship);
            if (slot < 0 || slot >= l.Slots.Count) return OutfitOutcome.NoSuchSlot;
            if (!CanFit(l, slot, module)) return OutfitOutcome.WontFit;
            if (l.Fitted[slot] == module) return OutfitOutcome.AlreadyFitted;
            if (module is ModuleKind built && !ModuleSpec.YardBuilds(built, yard)) return OutfitOutcome.YardCannotBuild;

            quote = Quote(ship, yard, slot, module);
            var add = ModuleSpec.Of(module.Value);
            var remove = l.Fitted[slot] is ModuleKind old ? ModuleSpec.Of(old) : null;
            var paid = Charge(ship, yard, l.Scale, add, remove, PartHealth(ship, l.Slots[slot]), quote);
            if (paid != OutfitOutcome.Ok) return paid;

            l.Fitted[slot] = module;
            ship.Combat = ship.Combat.Refitted(l.Build(), l.Class, l.Slots[slot].LayoutIndex);
            return OutfitOutcome.Ok;
        }

        /// <summary>
        /// Fits a facility of <paramref name="kind"/> into the free internal space of <paramref name="ship"/> at
        /// <paramref name="yard"/> (U.SPC-T470), bought from the yard's market as <see cref="Fit"/> buys a gun. It must be
        /// a kind the hull may carry (<see cref="OutfitOutcome.WontFit"/>) and fit in the space left
        /// (<see cref="OutfitOutcome.NoRoom"/>: strip something else first).
        /// </summary>
        public static OutfitOutcome FitFacility(Economy economy, Ship ship, Settlement yard, FacilityKind kind, out OutfitQuote quote)
        {
            quote = default;
            var ready = Ready(economy, ship, yard);
            if (ready != OutfitOutcome.Ok) return ready;
            var l = LoadoutOf(ship);
            var can = l.CanAdd(kind);
            if (can != OutfitOutcome.Ok) return can;

            quote = QuoteFacility(ship, yard, kind);
            var paid = Charge(ship, yard, l.Scale, kind, null, 1, quote);
            if (paid != OutfitOutcome.Ok) return paid;

            l.Facilities.Add(FacilitySite.Fitted(kind));
            Rebuild(ship, l, kind, null);
            return OutfitOutcome.Ok;
        }

        /// <summary>
        /// Takes facility number <paramref name="index"/> out of <paramref name="ship"/> at <paramref name="yard"/>, sold
        /// back for half its materials. Only a fitted facility comes out (never the hull's own structure), racks only
        /// when what is in them fits the hold that is left, and a brig only when it is empty.
        /// </summary>
        public static OutfitOutcome StripFacility(Economy economy, Ship ship, Settlement yard, int index, out OutfitQuote quote)
        {
            quote = default;
            var ready = Ready(economy, ship, yard);
            if (ready != OutfitOutcome.Ok) return ready;
            var l = LoadoutOf(ship);
            if (index < 0 || index >= l.Facilities.Count) return OutfitOutcome.NoSuchSlot;
            if (!l.CanRemove(index)) return OutfitOutcome.WontFit;
            var kind = l.Facilities[index].Def;

            // What the hull would carry afterwards: racks and brig cells taken out must leave room for what is in them.
            CargoHold resized = null;
            if (kind.Module == ModuleKind.CargoRack)
            {
                int racks = l.Count(ModuleKind.CargoRack) - 1;
                if (ship.HasCargo && ship.CargoTonnes > l.BaseCapacityTonnes + RackTonnes(l, racks) + 1e-9) return OutfitOutcome.HoldTooFull;
                resized = ship.ResizedHold(racks * Loadout.RackRows);
                if (resized == null) return OutfitOutcome.HoldTooFull;
            }
            if (kind.Module == ModuleKind.Brig && ship.Prisoners + ship.CaptainsHeld > (l.Count(ModuleKind.Brig) - 1) * l.BrigBerthsEach + 1e-9)
                return OutfitOutcome.BrigOccupied;

            quote = QuoteStrip(ship, yard, index);
            var paid = Charge(ship, yard, l.Scale, null, kind, 1, quote);
            if (paid != OutfitOutcome.Ok) return paid;

            l.Facilities.RemoveAt(index);
            Rebuild(ship, l, kind, resized);
            return OutfitOutcome.Ok;
        }

        /// <summary>Settles a refit's bill with the yard: buys the new part's materials out of its market, sells the old
        /// part's back into it, pays labour and tax. Nothing is paid unless it returns <see cref="OutfitOutcome.Ok"/>.</summary>
        static OutfitOutcome Charge(Ship ship, Settlement yard, double scale, IPartSpec add, IPartSpec remove, double health, OutfitQuote quote)
        {
            if (!quote.InStock) return OutfitOutcome.NoStock;
            if (quote.Net > 0 && ship.Faction.Treasury < quote.Net) return OutfitOutcome.CantAfford;
            if (add != null)
                for (int i = 0; i < add.Bill.Length; i++)
                {
                    var g = add.Bill[i].good;
                    double t = add.Tonnes(i, scale);
                    double price = yard.PurchaseCost(g, t);
                    yard.Stock[(int)g] = Math.Max(0, yard.Stock[(int)g] - t);
                    yard.ExtraDemand[(int)g] += t;
                    yard.TradeVolumeCredits += price;
                    yard.Owner.Treasury += price * Economy.PortTax;
                }
            if (remove != null)
                for (int i = 0; i < remove.Bill.Length; i++)
                {
                    var g = remove.Bill[i].good;
                    double t = remove.Tonnes(i, scale) * TradeInShare * health;
                    double value = yard.SaleValue(g, t);
                    yard.Stock[(int)g] += t;
                    yard.Owner.Treasury += value * Economy.PortTax;
                }
            yard.Owner.Treasury += quote.Labour;
            ship.Faction.Treasury -= quote.Net;
            yard.RefreshPrices();
            return OutfitOutcome.Ok;
        }

        /// <summary>The hull after a facility of <paramref name="kind"/> went in or came out: its combat spec, its interior
        /// (when the facility is a space of its own) and, for racks, both its holds.</summary>
        static void Rebuild(Ship ship, Loadout l, FacilityKind kind, CargoHold resized)
        {
            var spec = l.Build();
            ship.Combat = ship.Combat.Refitted(spec, l.Class, -1, InteriorAfter(ship, l, kind, spec));
            if (kind.Module != ModuleKind.CargoRack) return;
            int racks = l.Count(ModuleKind.CargoRack);
            ship.CapacityTonnes = l.BaseCapacityTonnes + RackTonnes(l, racks);
            ship.SetHold(resized ?? ship.ResizedHold(racks * Loadout.RackRows));
        }

        /// <summary>A new interior for the hull when a facility that is a compartment went in or came out (its people
        /// carried over, and any marines the new <paramref name="spec"/> adds or loses), or null to keep the one it has.</summary>
        static Interior InteriorAfter(Ship ship, Loadout l, FacilityKind kind, CombatSpec spec)
        {
            if (kind?.Compartment == null || ship.Combat == null) return null;
            var was = ship.Combat.Interior;
            return Interior.For(l.Facilities, was.Crew, Math.Max(0, was.Marines + spec.Marines - ship.Combat.Spec.Marines));
        }

        // ---------------------------------------------------------------- the AI's refits

        /// <summary>
        /// A navy's or raiders' warship refitting at its own yard takes one upgrade a visit, when the owner is
        /// flush (<see cref="AiFlush"/> times its price, and two new hulls' worth besides) and the yard has the
        /// materials to spare (<see cref="AiStockMargin"/>): the facilities it wants first, as far as its free space goes
        /// (<see cref="NavyFacilities"/>: plate, a scanner and a brig; <see cref="RaiderFacilities"/>: plate, a brig and
        /// racks for plunder), then hardened shields, heavier guns on a big hull, then torch drives. Returns what was
        /// fitted (a <see cref="ModuleSpec"/> or a <see cref="FacilityKind"/>), or null.
        /// </summary>
        public static IPartSpec AiRefit(Economy economy, Ship ship, Settlement yard)
        {
            var l = LoadoutOf(ship);
            if (l == null) return null;
            foreach (var (slot, module, facility) in AiWishes(ship, l))
            {
                IPartSpec part = facility != null ? (IPartSpec)facility : ModuleSpec.Of(module);
                var q = facility != null ? QuoteFacility(ship, yard, facility) : Quote(ship, yard, slot, module);
                if (!q.InStock || !Surplus(yard, part, l.Scale)) continue;
                if (ship.Faction.Treasury < q.Net * AiFlush + 2 * Economy.HullPrice(ship.Spec.Class)) continue;
                var outcome = facility != null ? FitFacility(economy, ship, yard, facility, out _) : Fit(economy, ship, yard, slot, module, out _);
                if (outcome == OutfitOutcome.Ok) return part;
            }
            return null;
        }

        /// <summary>How many times an upgrade's price an owner must hold (besides two new hulls) before the AI buys it.</summary>
        public const double AiFlush = 50;

        /// <summary>The AI only buys out of a yard's surplus: it leaves <see cref="AiStockMargin"/> times the module's
        /// materials on the shelves, so its upgrades never starve the yard's own industry, repairs or new hulls.</summary>
        public const double AiStockMargin = 4;

        /// <summary>The facilities a navy's warship wants in its free space, in order (facility kind ids; a new kind joins
        /// the AI's designs by being added here).</summary>
        public static readonly List<string> NavyFacilities = new List<string>
        {
            FacilityKinds.ArmourPlating, FacilityKinds.Scanner, FacilityKinds.ArmourPlating, FacilityKinds.Brig, FacilityKinds.ArmourPlating, FacilityKinds.ArmourPlating,
        };

        /// <summary>The facilities a raider wants in its free space, in order.</summary>
        public static readonly List<string> RaiderFacilities = new List<string>
        {
            FacilityKinds.ArmourPlating, FacilityKinds.Brig, FacilityKinds.CargoRacks, FacilityKinds.ArmourPlating, FacilityKinds.ArmourPlating, FacilityKinds.ArmourPlating,
        };

        static bool Surplus(Settlement yard, IPartSpec part, double scale)
        {
            for (int i = 0; i < part.Bill.Length; i++)
                if (yard.Available(part.Bill[i].good) < part.Tonnes(i, scale) * AiStockMargin) return false;
            return true;
        }

        static IEnumerable<(int slot, ModuleKind module, FacilityKind facility)> AiWishes(Ship ship, Loadout l)
        {
            // The facilities it wants that it has not got yet, in order, so far as each still fits.
            var have = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in l.AddedFacilities)
            {
                have.TryGetValue(f.Kind, out int n);
                have[f.Kind] = n + 1;
            }
            foreach (var id in ship.Faction.IsPirate ? RaiderFacilities : NavyFacilities)
            {
                if (have.TryGetValue(id, out int n) && n > 0) { have[id] = n - 1; continue; }
                var kind = FacilityKinds.Of(id);
                if (l.CanAdd(kind) == OutfitOutcome.Ok) yield return (-1, default, kind);
            }
            bool big = ship.Spec.Class == ShipClass.Cruiser || ship.Spec.Class == ShipClass.Battleship || ship.Spec.Class == ShipClass.Carrier;
            for (int i = 0; i < l.Slots.Count; i++)
                if (l.Slots[i].Kind == SlotKind.ShieldMount && l.Fitted[i] == ModuleKind.ShieldGenerator) yield return (i, ModuleKind.HardenedShield, null);
            if (big)
                for (int i = 0; i < l.Slots.Count; i++)
                    if (l.Slots[i].Kind == SlotKind.HeavyMount && l.Fitted[i] == ModuleKind.LaserTurret) yield return (i, ModuleKind.HeavyLaser, null);
            for (int i = 0; i < l.Slots.Count; i++)
                if (l.Slots[i].Kind == SlotKind.EngineMount && l.Fitted[i] == ModuleKind.Engine) yield return (i, ModuleKind.TorchEngine, null);
            // Last of all, once everything else is fitted: one tachyon lance for a capital hull, in place of a bow heavy
            // laser, where the yard can build one and the navy is flush enough for it (U.SPC-T487).
            if (TachyonLances.CanCarry(l.Class) && l.Count(ModuleKind.TachyonLance) == 0)
                for (int i = 0; i < l.Slots.Count; i++)
                    if (l.Slots[i].Kind == SlotKind.HeavyMount && l.Slots[i].BearsOnBow && l.Fitted[i] == ModuleKind.HeavyLaser)
                    {
                        yield return (i, ModuleKind.TachyonLance, null);
                        break;
                    }
        }

        // ---------------------------------------------------------------- prisoners

        /// <summary>
        /// The captors lock up a share of a taken prize's crew in their brigs, as far as there is room: each
        /// prisoner is worth <see cref="PirateBounty"/> if they are raiders taken by anyone else, or
        /// <see cref="Ransom"/> otherwise, paid when they are handed over in port. Returns the prisoners taken.
        /// </summary>
        public static double TakePrisoners(Ship prize, IEnumerable<Ship> captors)
        {
            if (prize?.Combat == null) return 0;
            double taken = 0;
            // The share is of the crew there were before anyone took any, not of those the last captor left.
            double crew = prize.Combat.Interior.Crew;
            foreach (var c in captors)
            {
                if (c == null || c == prize || c.Destroyed) continue;
                double room = BrigRoom(c);
                double want = Math.Floor(crew * PrisonerShare - taken + 1e-9);
                double n = Math.Floor(Math.Min(room, want) + 1e-9);
                if (n < 1) continue;
                n = Math.Floor(prize.Combat.Interior.TakeCrew(n) + 1e-9);
                if (n < 1) continue;
                c.Prisoners += n;
                c.PrisonerBounty += n * (prize.Faction.IsPirate && !c.Faction.IsPirate ? PirateBounty : Ransom);
                taken += n;
            }
            return taken;
        }

        /// <summary>Hands the ship's prisoners over at <paramref name="port"/> (one it may dock at) for their bounty
        /// and ransom, paid to its owner. Returns the credits.</summary>
        public static double HandOverPrisoners(Economy economy, Ship ship, Settlement port)
        {
            if (ship == null || port == null || ship.Prisoners <= 0 || port.Fallen) return 0;
            if (economy != null && !economy.CanTrade(ship, port)) return 0;
            double paid = ship.PrisonerBounty;
            ship.Faction.Treasury += paid;
            ship.Prisoners = 0;
            ship.PrisonerBounty = 0;
            return paid;
        }
    }
}
