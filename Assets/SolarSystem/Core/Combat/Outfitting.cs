using System;
using System.Collections.Generic;

namespace SolarSystem.Core
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

        /// <summary>Racks in a bay: more trade hold and two more rows in the grid hold.</summary>
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
        TurretDeployer,
    }

    /// <summary>The kinds of slot a hull offers: its weapon hardpoints (light ones take point defence, a laser or an
    /// autocannon, heavy ones any turret), its shield and engine mounts, its internal bays, and a small craft's gun
    /// mounts (an autocannon, a light railgun or a laser) and utility mount (missiles, an EMP launcher, mines or
    /// sentry turrets) (U.SPC-T271).</summary>
    public enum SlotKind { LightMount, HeavyMount, ShieldMount, EngineMount, InternalBay, GunMount, UtilityMount }

    /// <summary>One module: what it is called, what a yard builds it from (tonnes of alloys, electronics,
    /// machinery and munitions, scaled with the hull for plate, drives, generators and racks) and its mass.</summary>
    public sealed class ModuleSpec
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
            new ModuleSpec(ModuleKind.TurretDeployer, "Turret deployer", 45, false, (Good.Alloys, 18), (Good.Electronics, 10), (Good.Machinery, 6), (Good.Munitions, 3)),
        };

        public static ModuleSpec Of(ModuleKind k) => All[(int)k];

        public static IReadOnlyList<ModuleSpec> Catalogue => All;

        /// <summary>Materials for a hull of <paramref name="scale"/>, tonnes.</summary>
        public double Tonnes(int i, double scale) => Bill[i].tonnes * (Scaled ? scale : 1);

        public double Mass(double scale) => MassTonnes * (Scaled ? scale : 1);

        /// <summary>Whether a module goes in a slot (a railgun is spinal: only a mount that bears on the bow takes one).</summary>
        public static bool Fits(ModuleKind m, SlotKind slot, bool bearsOnBow)
        {
            switch (slot)
            {
                case SlotKind.LightMount: return m == ModuleKind.PointDefense || m == ModuleKind.LaserTurret || m == ModuleKind.Autocannon;
                case SlotKind.HeavyMount:
                    return m == ModuleKind.LaserTurret || m == ModuleKind.HeavyLaser || m == ModuleKind.MissileLauncher ||
                           m == ModuleKind.PointDefense || m == ModuleKind.Autocannon || m == ModuleKind.LightRailgun || (m == ModuleKind.Railgun && bearsOnBow);
                case SlotKind.GunMount: return m == ModuleKind.Autocannon || m == ModuleKind.LightRailgun || m == ModuleKind.LaserTurret;
                case SlotKind.UtilityMount:
                    return m == ModuleKind.MissileLauncher || m == ModuleKind.EmpLauncher || m == ModuleKind.MineLayer || m == ModuleKind.TurretDeployer;
                case SlotKind.ShieldMount: return m == ModuleKind.ShieldGenerator || m == ModuleKind.HardenedShield;
                case SlotKind.EngineMount: return m == ModuleKind.Engine || m == ModuleKind.TorchEngine;
                default: return m == ModuleKind.ArmourPlating || m == ModuleKind.CargoRack || m == ModuleKind.Scanner || m == ModuleKind.Brig;
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
                case ModuleKind.TurretDeployer: return SubsystemKind.TurretDeployer;
                case ModuleKind.ShieldGenerator:
                case ModuleKind.HardenedShield: return SubsystemKind.ShieldGenerator;
                default: return SubsystemKind.Engine;
            }
        }
    }

    /// <summary>One slot of a hull: its kind, and (for a mount) which hardpoint of the layout it is and what the design put there.</summary>
    public sealed class OutfitSlot
    {
        public readonly SlotKind Kind;

        /// <summary>The hardpoint's index in <see cref="CombatSpec.Layout"/>, or -1 for an internal bay.</summary>
        public readonly int LayoutIndex;

        /// <summary>What the design was built with here (null for a bay, which starts empty).</summary>
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
                    case SlotKind.ShieldMount: return "Shield mount";
                    case SlotKind.EngineMount: return "Engine mount";
                    default: return "Internal bay";
                }
            }
        }
    }

    /// <summary>
    /// What is fitted into each of a hull's slots (U.SPC-T65), over the design it was built as. The slots come
    /// from the design: each refittable hardpoint (guns and launchers, point defence, shield generators,
    /// engines) plus a number of internal bays by class. Bridges, reactors, hangars, drills, pod launchers and
    /// a fighter's guns are part of the hull and are not refitted. <see cref="Build"/> turns the loadout into
    /// the hull's <see cref="CombatSpec"/>: its hardpoints, shield, hull, armour, drive and mass.
    /// </summary>
    public sealed class Loadout
    {
        public readonly CombatSpec Base;
        public readonly ShipClass Class;
        public readonly List<OutfitSlot> Slots = new List<OutfitSlot>();
        public readonly ModuleKind?[] Fitted;

        /// <summary>The trade hold the hull was built with, tonnes, before any racks.</summary>
        public readonly double BaseCapacityTonnes;

        public Loadout(CombatSpec design, ShipClass cls, double baseCapacityTonnes)
        {
            Base = design;
            Class = cls;
            BaseCapacityTonnes = baseCapacityTonnes;
            for (int i = 0; i < design.Layout.Length; i++)
            {
                var d = design.Layout[i];
                var slot = SlotOf(d.Kind, cls);
                if (slot == null) continue;
                Slots.Add(new OutfitSlot(slot.Value, i, StockOf(d.Kind), d.ContainsYaw(0)));
            }
            for (int i = 0; i < InternalBays(cls); i++) Slots.Add(new OutfitSlot(SlotKind.InternalBay, -1, null, false));
            Fitted = new ModuleKind?[Slots.Count];
            for (int i = 0; i < Slots.Count; i++) Fitted[i] = Slots[i].Stock;
        }

        static SlotKind? SlotOf(SubsystemKind k, ShipClass cls)
        {
            bool small = cls == ShipClass.Fighter || cls == ShipClass.Courier || cls == ShipClass.Breacher;
            switch (k)
            {
                // A small craft's missile rack is its utility mount, not a warship's turret mount.
                case SubsystemKind.MissileLauncher when small: return SlotKind.UtilityMount;
                case SubsystemKind.Autocannon:
                case SubsystemKind.LightRailgun: return SlotKind.GunMount;
                case SubsystemKind.EmpLauncher:
                case SubsystemKind.MineLayer:
                case SubsystemKind.TurretDeployer: return SlotKind.UtilityMount;
                case SubsystemKind.PointDefense: return SlotKind.LightMount;
                case SubsystemKind.LaserTurret:
                case SubsystemKind.HeavyLaser:
                case SubsystemKind.MissileLauncher:
                case SubsystemKind.Railgun: return SlotKind.HeavyMount;
                case SubsystemKind.ShieldGenerator: return SlotKind.ShieldMount;
                case SubsystemKind.Engine: return SlotKind.EngineMount;
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
                case SubsystemKind.TurretDeployer: return ModuleKind.TurretDeployer;
                default: return null;
            }
        }

        /// <summary>Internal bays a hull of each class has room for.</summary>
        public static int InternalBays(ShipClass c)
        {
            switch (c)
            {
                case ShipClass.Carrier:
                case ShipClass.Battleship: return 6;
                case ShipClass.Cruiser:
                case ShipClass.Freighter: return 4;
                case ShipClass.Destroyer:
                case ShipClass.Miner: return 3;
                case ShipClass.Breacher:
                case ShipClass.Courier: return 2;
                default: return 1;
            }
        }

        /// <summary>How big the hull is for plate, drives, generators and racks: 1 for a destroyer (9,000 t dry),
        /// about 4 for a carrier, a sixth for a courier.</summary>
        public double Scale => HullScale(Base);

        public static double HullScale(CombatSpec design) => Math.Max(0.05, Math.Sqrt(Math.Max(0, design.DryMassTonnes) / 9000));

        public int Count(ModuleKind k)
        {
            int n = 0;
            foreach (var f in Fitted) if (f == k) n++;
            return n;
        }

        /// <summary>Whether anything differs from the design as built.</summary>
        public bool IsStock
        {
            get
            {
                for (int i = 0; i < Slots.Count; i++) if (Fitted[i] != Slots[i].Stock) return false;
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
                return mass;
            }
        }

        /// <summary>
        /// The hull's fighting spec with this loadout: each refitted hardpoint swapped in place (same position and
        /// arc), shield and regen shared by the generators' grades, hull and armour raised by plate, the drive's
        /// thrust by its engines' mean grade, and every added tonne of dry mass slowing it.
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
                if (slot.LayoutIndex < 0 || !(Fitted[i] is ModuleKind m)) continue;
                double rating = m == ModuleKind.HardenedShield ? HardenedRating : m == ModuleKind.TorchEngine ? TorchRating : 1;
                if (slot.Kind == SlotKind.ShieldMount) ratings += rating;
                if (slot.Kind == SlotKind.EngineMount) { engines++; thrust += rating; }
                if (m == slot.Stock) continue;
                var was = Base.Layout[slot.LayoutIndex];
                var def = new SubsystemDef(ModuleSpec.SubsystemOf(m), was.X, was.Y, was.Z, was.FacesAft, was.ArcCenterDeg, was.ArcWidthDeg) { Rating = rating };
                if (m == ModuleKind.HardenedShield) def.MaxHp *= 1.4;
                if (m == ModuleKind.TorchEngine) def.MaxHp *= 1.2;
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

        /// <summary>A copy for a hull that is handed on (a jump gate's arrival).</summary>
        public Loadout Copy()
        {
            var l = new Loadout(Base, Class, BaseCapacityTonnes);
            Array.Copy(Fitted, l.Fitted, Fitted.Length);
            return l;
        }
    }

    /// <summary>Why a refit did or did not go through.</summary>
    public enum OutfitOutcome { Ok, NotAYard, CannotDock, Busy, NoSuchSlot, WontFit, AlreadyFitted, NoStock, CantAfford, HoldTooFull, BrigOccupied }

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
    /// Ship outfitting at yards (U.SPC-T65). At a shipyard (or, for raiders, a haven) a ship's owner buys a
    /// module and has it fitted into one of the hull's slots: a gun or launcher into a weapon hardpoint,
    /// a hardened generator into a shield mount, a torch drive into an engine mount, or armour plate, cargo
    /// racks, a scanner suite or a brig into an internal bay. The module's alloys, electronics, machinery and
    /// munitions come out of that yard's own market at its prices (so a yard short of electronics cannot fit
    /// shields, and one where they are dear charges for it), the yard's labour is a quarter of the materials'
    /// worth, and the part taken out is sold back into the market for half its materials. The hull's
    /// <see cref="CombatSpec"/> is rebuilt from the loadout (<see cref="Loadout.Build"/>), carrying its damage,
    /// people and stores over. The player does this at the helm; navies and raiders upgrade their warships
    /// while they refit at their own yards (<see cref="AiRefit"/>).
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
        /// Fits <paramref name="module"/> into the first empty slot that takes it, as part of a new hull's build (no
        /// yard bill: the hull's price covers it). False if no slot is free for it.
        /// </summary>
        public static bool Install(Ship ship, ModuleKind module)
        {
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

        /// <summary>Extra trade hold this ship's racks give, tonnes.</summary>
        public static double RackTonnes(Loadout l, int racks) => l.BaseCapacityTonnes * Loadout.RackCapacityShare * racks;

        /// <summary>
        /// What fitting <paramref name="module"/> (null: emptying an internal bay) into slot <paramref name="slot"/> of
        /// the ship costs at <paramref name="yard"/>, at that market's prices and stock now.
        /// </summary>
        public static OutfitQuote Quote(Ship ship, Settlement yard, int slot, ModuleKind? module)
        {
            var l = LoadoutOf(ship);
            if (l == null || yard == null || slot < 0 || slot >= l.Slots.Count) return default;
            double scale = l.Scale;
            double materials = 0, labour = 0, tradeIn = 0;
            bool inStock = true;
            Good shortOf = Good.Alloys;
            if (module is ModuleKind m)
            {
                var spec = ModuleSpec.Of(m);
                for (int i = 0; i < spec.Bill.Length; i++)
                {
                    var g = spec.Bill[i].good;
                    double t = spec.Tonnes(i, scale);
                    materials += yard.PurchaseCost(g, t) * (1 + Economy.PortTax);
                    labour += t * GoodInfo.Of(g).BasePrice * LabourShare;
                    if (inStock && yard.Available(g) < t) { inStock = false; shortOf = g; }
                }
            }
            if (l.Fitted[slot] is ModuleKind old)
            {
                var spec = ModuleSpec.Of(old);
                double health = PartHealth(ship, l.Slots[slot]);
                for (int i = 0; i < spec.Bill.Length; i++)
                    tradeIn += yard.SaleValue(spec.Bill[i].good, spec.Tonnes(i, scale) * TradeInShare * health) * (1 - Economy.PortTax);
            }
            return new OutfitQuote(materials, labour, tradeIn, inStock, shortOf);
        }

        /// <summary>The health of what is fitted in a slot (a shot-away gun fetches nothing).</summary>
        static double PartHealth(Ship ship, OutfitSlot slot)
        {
            if (slot.LayoutIndex < 0 || ship.Combat == null || slot.LayoutIndex >= ship.Combat.Subsystems.Count) return 1;
            return ship.Combat.Subsystems[slot.LayoutIndex].Health;
        }

        /// <summary>Whether <paramref name="module"/> may go in slot <paramref name="slot"/> (null only into a bay).</summary>
        public static bool CanFit(Loadout l, int slot, ModuleKind? module)
        {
            if (l == null || slot < 0 || slot >= l.Slots.Count) return false;
            var s = l.Slots[slot];
            if (module is ModuleKind m) return ModuleSpec.Fits(m, s.Kind, s.BearsOnBow);
            return s.Kind == SlotKind.InternalBay;
        }

        /// <summary>
        /// Fits <paramref name="module"/> (null: strips an internal bay) into slot <paramref name="slot"/> of
        /// <paramref name="ship"/> at <paramref name="yard"/>: buys the materials from its market (they leave its
        /// stock and count as demand there), pays the yard's labour and the port's tax, sells the old part back,
        /// and rebuilds the hull's combat spec. Nothing changes unless it returns <see cref="OutfitOutcome.Ok"/>.
        /// The ship must be out of battle; the caller checks it is alongside the yard.
        /// </summary>
        public static OutfitOutcome Fit(Economy economy, Ship ship, Settlement yard, int slot, ModuleKind? module, out OutfitQuote quote)
        {
            quote = default;
            if (!IsYard(yard)) return OutfitOutcome.NotAYard;
            if (ship == null || ship.Combat == null || ship.Destroyed || ship.Derelict) return OutfitOutcome.Busy;
            if (economy != null && !economy.CanTrade(ship, yard)) return OutfitOutcome.CannotDock;
            if (ship.Battle != null || ship.Combat.Interior.UnderBoarding) return OutfitOutcome.Busy;
            var l = LoadoutOf(ship);
            if (slot < 0 || slot >= l.Slots.Count) return OutfitOutcome.NoSuchSlot;
            if (!CanFit(l, slot, module)) return OutfitOutcome.WontFit;
            if (l.Fitted[slot] == module) return OutfitOutcome.AlreadyFitted;

            // What the hull would carry afterwards: racks and brig cells taken out must leave room for what is in them.
            int racks = l.Count(ModuleKind.CargoRack) + (module == ModuleKind.CargoRack ? 1 : 0) - (l.Fitted[slot] == ModuleKind.CargoRack ? 1 : 0);
            int brigs = l.Count(ModuleKind.Brig) + (module == ModuleKind.Brig ? 1 : 0) - (l.Fitted[slot] == ModuleKind.Brig ? 1 : 0);
            double capacity = l.BaseCapacityTonnes + RackTonnes(l, racks);
            CargoHold resized = null;
            if (l.Fitted[slot] == ModuleKind.CargoRack)
            {
                if (ship.HasCargo && ship.CargoTonnes > capacity + 1e-9) return OutfitOutcome.HoldTooFull;
                resized = ship.ResizedHold(racks * Loadout.RackRows);
                if (resized == null) return OutfitOutcome.HoldTooFull;
            }
            if (ship.Prisoners + ship.CaptainsHeld > brigs * l.BrigBerthsEach + 1e-9) return OutfitOutcome.BrigOccupied;

            quote = Quote(ship, yard, slot, module);
            if (!quote.InStock) return OutfitOutcome.NoStock;
            if (quote.Net > 0 && ship.Faction.Treasury < quote.Net) return OutfitOutcome.CantAfford;

            double scale = l.Scale;
            if (module is ModuleKind m)
            {
                var spec = ModuleSpec.Of(m);
                for (int i = 0; i < spec.Bill.Length; i++)
                {
                    var g = spec.Bill[i].good;
                    double t = spec.Tonnes(i, scale);
                    double price = yard.PurchaseCost(g, t);
                    yard.Stock[(int)g] = Math.Max(0, yard.Stock[(int)g] - t);
                    yard.ExtraDemand[(int)g] += t;
                    yard.TradeVolumeCredits += price;
                    yard.Owner.Treasury += price * Economy.PortTax;
                }
            }
            if (l.Fitted[slot] is ModuleKind old)
            {
                var spec = ModuleSpec.Of(old);
                double health = PartHealth(ship, l.Slots[slot]);
                for (int i = 0; i < spec.Bill.Length; i++)
                {
                    var g = spec.Bill[i].good;
                    double t = spec.Tonnes(i, scale) * TradeInShare * health;
                    double value = yard.SaleValue(g, t);
                    yard.Stock[(int)g] += t;
                    yard.Owner.Treasury += value * Economy.PortTax;
                }
            }
            yard.Owner.Treasury += quote.Labour;
            ship.Faction.Treasury -= quote.Net;
            yard.RefreshPrices();

            l.Fitted[slot] = module;
            int changed = l.Slots[slot].LayoutIndex;
            ship.Combat = ship.Combat.Refitted(l.Build(), l.Class, changed);
            ship.CapacityTonnes = capacity;
            if (resized != null || module == ModuleKind.CargoRack) ship.SetHold(resized ?? ship.ResizedHold(racks * Loadout.RackRows));
            return OutfitOutcome.Ok;
        }

        // ---------------------------------------------------------------- the AI's refits

        /// <summary>
        /// A navy's or raiders' warship refitting at its own yard takes one upgrade a visit, when the owner is
        /// flush (<see cref="AiFlush"/> times its price, and two new hulls' worth besides) and the yard has the
        /// materials to spare (<see cref="AiStockMargin"/>): plate first
        /// (navies want a scanner and a brig too; raiders a brig and racks for plunder), then hardened shields,
        /// heavier guns on a big hull, then torch drives. Returns what was fitted, or null.
        /// </summary>
        public static ModuleKind? AiRefit(Economy economy, Ship ship, Settlement yard)
        {
            var l = LoadoutOf(ship);
            if (l == null) return null;
            foreach (var (slot, module) in AiWishes(ship, l))
            {
                var q = Quote(ship, yard, slot, module);
                if (!q.InStock || !Surplus(yard, module, l.Scale)) continue;
                if (ship.Faction.Treasury < q.Net * AiFlush + 2 * Economy.HullPrice(ship.Spec.Class)) continue;
                if (Fit(economy, ship, yard, slot, module, out _) == OutfitOutcome.Ok) return module;
            }
            return null;
        }

        /// <summary>How many times an upgrade's price an owner must hold (besides two new hulls) before the AI buys it.</summary>
        public const double AiFlush = 50;

        /// <summary>The AI only buys out of a yard's surplus: it leaves <see cref="AiStockMargin"/> times the module's
        /// materials on the shelves, so its upgrades never starve the yard's own industry, repairs or new hulls.</summary>
        public const double AiStockMargin = 4;

        static bool Surplus(Settlement yard, ModuleKind module, double scale)
        {
            var spec = ModuleSpec.Of(module);
            for (int i = 0; i < spec.Bill.Length; i++)
                if (yard.Available(spec.Bill[i].good) < spec.Tonnes(i, scale) * AiStockMargin) return false;
            return true;
        }

        static IEnumerable<(int slot, ModuleKind module)> AiWishes(Ship ship, Loadout l)
        {
            var bays = ship.Faction.IsPirate
                ? new[] { ModuleKind.ArmourPlating, ModuleKind.Brig, ModuleKind.CargoRack, ModuleKind.ArmourPlating, ModuleKind.ArmourPlating, ModuleKind.ArmourPlating }
                : new[] { ModuleKind.ArmourPlating, ModuleKind.Scanner, ModuleKind.ArmourPlating, ModuleKind.Brig, ModuleKind.ArmourPlating, ModuleKind.ArmourPlating };
            int bay = 0;
            for (int i = 0; i < l.Slots.Count; i++)
            {
                if (l.Slots[i].Kind != SlotKind.InternalBay) continue;
                if (l.Fitted[i] == null && bay < bays.Length) yield return (i, bays[bay]);
                bay++;
            }
            bool big = ship.Spec.Class == ShipClass.Cruiser || ship.Spec.Class == ShipClass.Battleship || ship.Spec.Class == ShipClass.Carrier;
            for (int i = 0; i < l.Slots.Count; i++)
                if (l.Slots[i].Kind == SlotKind.ShieldMount && l.Fitted[i] == ModuleKind.ShieldGenerator) yield return (i, ModuleKind.HardenedShield);
            if (big)
                for (int i = 0; i < l.Slots.Count; i++)
                    if (l.Slots[i].Kind == SlotKind.HeavyMount && l.Fitted[i] == ModuleKind.LaserTurret) yield return (i, ModuleKind.HeavyLaser);
            for (int i = 0; i < l.Slots.Count; i++)
                if (l.Slots[i].Kind == SlotKind.EngineMount && l.Fitted[i] == ModuleKind.Engine) yield return (i, ModuleKind.TorchEngine);
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
