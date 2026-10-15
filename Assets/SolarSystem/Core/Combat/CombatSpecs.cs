using System;
using System.Collections.Generic;

namespace SolarSystem.Core
{
    public enum SubsystemKind
    {
        LaserTurret,
        HeavyLaser,
        MissileLauncher,
        PointDefense,
        FighterGuns,
        Torpedo,

        /// <summary>A fixed spinal kinetic cannon: no turret traverse, so the hull itself must aim it
        /// (see <see cref="Battle.BroadsideFacing"/>) -- the tradeoff for the single hardest-hitting,
        /// longest-ranged non-missile shot in the game.</summary>
        Railgun,

        /// <summary>Launches drill-tipped boarding pods that punch into a hull and put marines aboard.</summary>
        BoardingPods,

        /// <summary>A breacher's drill prow: it rams and its assault bay opens into the target.</summary>
        Drill,
        Engine,
        ShieldGenerator,
        Bridge,
        Hangar,
        Reactor,
    }

    /// <summary>What a weapon does. Ranges are km, damage is hit points, cooldowns are seconds.</summary>
    public sealed class WeaponSpec
    {
        public double RangeKm;
        public double Damage;
        public double Cooldown;
        public double Accuracy;
        public bool Missile;
        public double MissileSpeedKmS;
        public int Salvo = 1;
        public int Ammo;

        /// <summary>Point defence: shoots missiles and fighters first.</summary>
        public bool AntiSmall;

        /// <summary>Muzzle velocity (km/s) of a direct-fire kinetic round -- a railgun slug or an
        /// autocannon burst. Zero for an energy weapon: a laser beam crosses its range effectively
        /// instantly, so <see cref="Battle.Fire"/> resolves it the instant it's aimed rather than
        /// spawning a travelling <see cref="KineticRound"/>.</summary>
        public double MuzzleSpeedKmS;

        public static readonly WeaponSpec Laser = new WeaponSpec { RangeKm = 16, Damage = 45, Cooldown = 3.0, Accuracy = 0.8 };
        public static readonly WeaponSpec Heavy = new WeaponSpec { RangeKm = 24, Damage = 190, Cooldown = 9.0, Accuracy = 0.72 };
        public static readonly WeaponSpec Missiles = new WeaponSpec { RangeKm = 40, Damage = 260, Cooldown = 24, Accuracy = 1, Missile = true, MissileSpeedKmS = 1.4, Salvo = 2, Ammo = 16 };
        public static readonly WeaponSpec PointDefense = new WeaponSpec { RangeKm = 3.5, Damage = 10, Cooldown = 0.35, Accuracy = 0.5, AntiSmall = true };

        /// <summary>A fighter's rapid-firing forward autocannon: a dumb kinetic round with a real,
        /// if short, flight time across its short range -- not a hitscan.</summary>
        public static readonly WeaponSpec FighterGuns = new WeaponSpec { RangeKm = 1.6, Damage = 14, Cooldown = 0.3, Accuracy = 0.55, MuzzleSpeedKmS = 3.5 };
        public static readonly WeaponSpec Torpedo = new WeaponSpec { RangeKm = 4, Damage = 420, Cooldown = 40, Accuracy = 1, Missile = true, MissileSpeedKmS = 0.9, Salvo = 1, Ammo = 2 };

        /// <summary>A kinetic spinal gun: the longest non-missile range and the hardest single hit
        /// outside a torpedo, at the slowest cooldown -- a precise, patient weapon built around one
        /// shot landing hard rather than sustained fire. Hypervelocity, but still slow enough next to
        /// its own long range to take a real second or two in flight.</summary>
        public static readonly WeaponSpec Railgun = new WeaponSpec { RangeKm = 30, Damage = 260, Cooldown = 12, Accuracy = 0.85, MuzzleSpeedKmS = 14 };

        /// <summary>Boarding pods: slow, few, and stopped by shields; each carries a squad of marines.</summary>
        public static readonly WeaponSpec Pods = new WeaponSpec { RangeKm = 10, Damage = 60, Cooldown = 50, Accuracy = 1, Missile = true, MissileSpeedKmS = 0.8, Salvo = 2, Ammo = 6 };

        /// <summary>Marines per boarding pod.</summary>
        public const double PodMarines = 12;
    }

    /// <summary>One hardpoint on a hull: its kind, where it sits (ship lengths, nose +Z, up +Y) and its toughness.</summary>
    public sealed class SubsystemDef
    {
        public SubsystemKind Kind;
        public double X, Y, Z;
        public double MaxHp;
        public WeaponSpec Weapon;

        /// <summary>Turrets facing aft rest pointing backwards.</summary>
        public bool FacesAft;

        /// <summary>Yaw-plane mount arc, degrees (0 = bow, 90 = starboard, 180 = stern, 270 = port). For a
        /// <see cref="SubsystemKind.ShieldGenerator"/> this is the arc it covers; for a weapon, the arc it
        /// can fire into. The default (0, 360) covers every direction, matching omnidirectional coverage.</summary>
        public double ArcCenterDeg, ArcWidthDeg = 360;

        public SubsystemDef(SubsystemKind kind, double x, double y, double z, bool facesAft = false, double arcCenterDeg = 0, double arcWidthDeg = 360)
        {
            Kind = kind;
            X = x;
            Y = y;
            Z = z;
            FacesAft = facesAft;
            ArcCenterDeg = arcCenterDeg;
            ArcWidthDeg = arcWidthDeg;
            MaxHp = DefaultHp(kind);
            Weapon = DefaultWeapon(kind);
        }

        public bool IsWeapon => Weapon != null;

        /// <summary>Whether a yaw angle (degrees, 0 = bow) falls within this mount's arc.</summary>
        public bool ContainsYaw(double yawDeg)
        {
            double diff = Astro.WrapDegrees360(yawDeg - ArcCenterDeg);
            if (diff > 180) diff -= 360;
            return Math.Abs(diff) <= ArcWidthDeg * 0.5;
        }

        /// <summary>Hardpoints are built to take punishment; a capital ship loses guns one at a time.</summary>
        static double DefaultHp(SubsystemKind k) => BaseHp(k) * (k == SubsystemKind.FighterGuns || k == SubsystemKind.Torpedo ? 1 : 3);

        static double BaseHp(SubsystemKind k)
        {
            switch (k)
            {
                case SubsystemKind.LaserTurret: return 220;
                case SubsystemKind.HeavyLaser: return 380;
                case SubsystemKind.Railgun: return 420;
                case SubsystemKind.MissileLauncher: return 260;
                case SubsystemKind.PointDefense: return 90;
                case SubsystemKind.Engine: return 300;
                case SubsystemKind.ShieldGenerator: return 260;
                case SubsystemKind.Bridge: return 320;
                case SubsystemKind.Hangar: return 520;
                case SubsystemKind.Reactor: return 650;
                case SubsystemKind.BoardingPods: return 200;
                case SubsystemKind.Drill: return 600;
                case SubsystemKind.FighterGuns: return 30;
                case SubsystemKind.Torpedo: return 30;
                default: return 200;
            }
        }

        static WeaponSpec DefaultWeapon(SubsystemKind k)
        {
            switch (k)
            {
                case SubsystemKind.LaserTurret: return WeaponSpec.Laser;
                case SubsystemKind.HeavyLaser: return WeaponSpec.Heavy;
                case SubsystemKind.Railgun: return WeaponSpec.Railgun;
                case SubsystemKind.MissileLauncher: return WeaponSpec.Missiles;
                case SubsystemKind.PointDefense: return WeaponSpec.PointDefense;
                case SubsystemKind.FighterGuns: return WeaponSpec.FighterGuns;
                case SubsystemKind.Torpedo: return WeaponSpec.Torpedo;
                case SubsystemKind.BoardingPods: return WeaponSpec.Pods;
                default: return null;
            }
        }

        public static string Name(SubsystemKind k)
        {
            switch (k)
            {
                case SubsystemKind.LaserTurret: return "Laser turret";
                case SubsystemKind.HeavyLaser: return "Heavy laser";
                case SubsystemKind.Railgun: return "Railgun";
                case SubsystemKind.MissileLauncher: return "Missile launcher";
                case SubsystemKind.PointDefense: return "Point defence";
                case SubsystemKind.FighterGuns: return "Guns";
                case SubsystemKind.Torpedo: return "Torpedo";
                case SubsystemKind.Engine: return "Engine";
                case SubsystemKind.ShieldGenerator: return "Shield generator";
                case SubsystemKind.Bridge: return "Bridge";
                case SubsystemKind.Hangar: return "Hangar";
                case SubsystemKind.BoardingPods: return "Boarding pods";
                case SubsystemKind.Drill: return "Drill prow";
                default: return "Reactor";
            }
        }
    }

    /// <summary>A hull design's fighting qualities and hardpoint layout (positions match its mesh). Several
    /// designs can share a <see cref="ShipClass"/> (its broad category); each ship picks one at construction
    /// and keeps it for its lifetime, so two ships of the same class can fly and fight differently.</summary>
    public sealed class CombatSpec
    {
        /// <summary>A display name for this specific design, distinct from its class.</summary>
        public string DesignName;

        /// <summary>Which mesh renders this design; null/empty falls back to the class's procedural hull.</summary>
        public string MeshId;

        /// <summary>This design's own length — ships of the same class can be different sizes.</summary>
        public double LengthKm;

        public double Hull, Shield, ShieldRegen, Armor;

        /// <summary>Tactical speed and acceleration/deceleration in battle, km/s and km/s².</summary>
        public double MaxSpeed, Accel, Decel;

        /// <summary>Boost multiplies MaxSpeed while held, draining over BoostDurationS and recharging otherwise.</summary>
        public double BoostSpeedMultiplier = 1, BoostDurationS;

        public double TurnRateDegS;
        public SubsystemDef[] Layout;

        /// <summary>Fighter squadrons a carrier can launch (6 craft each).</summary>
        public int Squadrons;

        /// <summary>People aboard: the ship's company, and the marines who board and repel boarders.</summary>
        public double Crew, Marines;

        static readonly Dictionary<ShipClass, CombatSpec[]> Specs = new Dictionary<ShipClass, CombatSpec[]>
        {
            [ShipClass.Carrier] = new[] { new CombatSpec
            {
                DesignName = "Carrier", LengthKm = 3.2,
                Hull = 26000, Shield = 11000, ShieldRegen = 75, Armor = 6, MaxSpeed = 0.07, Accel = 0.0025, Decel = 0.0025,
                BoostSpeedMultiplier = 1.3, BoostDurationS = 30, TurnRateDegS = 4, Squadrons = 3,
                Crew = 3800, Marines = 320,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.BoardingPods, 0, -0.035, 0.25),
                    new SubsystemDef(SubsystemKind.Bridge, 0.07, 0.14, -0.11),
                    new SubsystemDef(SubsystemKind.Hangar, 0, 0.012, 0.44),
                    new SubsystemDef(SubsystemKind.Hangar, 0.125, -0.01, 0.12),
                    new SubsystemDef(SubsystemKind.Hangar, -0.125, -0.01, 0.12),
                    new SubsystemDef(SubsystemKind.LaserTurret, 0.09, 0.055, 0.3, arcCenterDeg: 90, arcWidthDeg: 150),
                    new SubsystemDef(SubsystemKind.LaserTurret, -0.09, 0.055, 0.3, arcCenterDeg: 270, arcWidthDeg: 150),
                    new SubsystemDef(SubsystemKind.MissileLauncher, 0.12, 0.035, -0.06, arcCenterDeg: 90, arcWidthDeg: 150),
                    new SubsystemDef(SubsystemKind.MissileLauncher, -0.12, 0.035, -0.06, arcCenterDeg: 270, arcWidthDeg: 150),
                    new SubsystemDef(SubsystemKind.PointDefense, 0.1, 0.055, 0.42),
                    new SubsystemDef(SubsystemKind.PointDefense, -0.1, 0.055, 0.42),
                    new SubsystemDef(SubsystemKind.PointDefense, 0.12, 0.035, -0.3),
                    new SubsystemDef(SubsystemKind.PointDefense, -0.12, 0.035, -0.3),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.05, 0.4, arcCenterDeg: 0, arcWidthDeg: 45),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0.08, 0.05, 0.25, arcCenterDeg: 45, arcWidthDeg: 45),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0.1, 0.05, 0, arcCenterDeg: 90, arcWidthDeg: 45),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0.08, 0.05, -0.3, arcCenterDeg: 135, arcWidthDeg: 45),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.05, -0.45, arcCenterDeg: 180, arcWidthDeg: 45),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, -0.08, 0.05, -0.3, arcCenterDeg: 225, arcWidthDeg: 45),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, -0.1, 0.05, 0, arcCenterDeg: 270, arcWidthDeg: 45),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, -0.08, 0.05, 0.25, arcCenterDeg: 315, arcWidthDeg: 45),
                    new SubsystemDef(SubsystemKind.Engine, -0.075, 0, -0.49),
                    new SubsystemDef(SubsystemKind.Engine, -0.027, 0, -0.49),
                    new SubsystemDef(SubsystemKind.Engine, 0.027, 0, -0.49),
                    new SubsystemDef(SubsystemKind.Engine, 0.075, 0, -0.49),
                    new SubsystemDef(SubsystemKind.Reactor, 0, -0.01, -0.3),
                },
            } },
            [ShipClass.Battleship] = new[] { new CombatSpec
            {
                DesignName = "Battleship", LengthKm = 2.4,
                Hull = 30000, Shield = 13000, ShieldRegen = 90, Armor = 10, MaxSpeed = 0.09, Accel = 0.003, Decel = 0.003,
                BoostSpeedMultiplier = 1.3, BoostDurationS = 32, TurnRateDegS = 5,
                Crew = 2400, Marines = 260,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.BoardingPods, 0, -0.045, -0.3),
                    new SubsystemDef(SubsystemKind.HeavyLaser, 0, 0.045, 0.20, arcCenterDeg: 0, arcWidthDeg: 120),
                    new SubsystemDef(SubsystemKind.HeavyLaser, 0, 0.06, 0.07, arcCenterDeg: 0, arcWidthDeg: 120),
                    new SubsystemDef(SubsystemKind.HeavyLaser, 0, 0.045, -0.28, true, arcCenterDeg: 180, arcWidthDeg: 120),
                    new SubsystemDef(SubsystemKind.LaserTurret, 0.06, 0.05, -0.02, arcCenterDeg: 90, arcWidthDeg: 150),
                    new SubsystemDef(SubsystemKind.LaserTurret, -0.06, 0.05, -0.02, arcCenterDeg: 270, arcWidthDeg: 150),
                    new SubsystemDef(SubsystemKind.MissileLauncher, 0.07, 0.0, 0.14, arcCenterDeg: 90, arcWidthDeg: 150),
                    new SubsystemDef(SubsystemKind.MissileLauncher, -0.07, 0.0, 0.14, arcCenterDeg: 270, arcWidthDeg: 150),
                    new SubsystemDef(SubsystemKind.PointDefense, 0.075, 0.03, 0.27),
                    new SubsystemDef(SubsystemKind.PointDefense, -0.075, 0.03, 0.27),
                    new SubsystemDef(SubsystemKind.PointDefense, 0.075, 0.03, -0.36),
                    new SubsystemDef(SubsystemKind.PointDefense, -0.075, 0.03, -0.36),
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.105, -0.11),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.06, 0.35, arcCenterDeg: 0, arcWidthDeg: 45),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0.07, 0.06, 0.2, arcCenterDeg: 45, arcWidthDeg: 45),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0.09, 0.06, -0.05, arcCenterDeg: 90, arcWidthDeg: 45),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0.07, 0.06, -0.3, arcCenterDeg: 135, arcWidthDeg: 45),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.06, -0.44, arcCenterDeg: 180, arcWidthDeg: 45),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, -0.07, 0.06, -0.3, arcCenterDeg: 225, arcWidthDeg: 45),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, -0.09, 0.06, -0.05, arcCenterDeg: 270, arcWidthDeg: 45),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, -0.07, 0.06, 0.2, arcCenterDeg: 315, arcWidthDeg: 45),
                    new SubsystemDef(SubsystemKind.Engine, -0.042, 0, -0.48),
                    new SubsystemDef(SubsystemKind.Engine, 0, 0, -0.48),
                    new SubsystemDef(SubsystemKind.Engine, 0.042, 0, -0.48),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.22),
                },
            } },
            [ShipClass.Cruiser] = new[] { new CombatSpec
            {
                DesignName = "Cruiser", LengthKm = 1.2,
                Hull = 13000, Shield = 6000, ShieldRegen = 54, Armor = 6, MaxSpeed = 0.14, Accel = 0.005, Decel = 0.006,
                BoostSpeedMultiplier = 1.4, BoostDurationS = 28, TurnRateDegS = 8,
                Crew = 900, Marines = 140,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.BoardingPods, 0, -0.03, 0.05),
                    new SubsystemDef(SubsystemKind.LaserTurret, 0, 0.03, 0.15, arcCenterDeg: 0, arcWidthDeg: 120),
                    new SubsystemDef(SubsystemKind.LaserTurret, 0, 0.03, 0.0, arcCenterDeg: 0, arcWidthDeg: 120),
                    new SubsystemDef(SubsystemKind.MissileLauncher, 0.1, 0.014, -0.05, arcCenterDeg: 90, arcWidthDeg: 150),
                    new SubsystemDef(SubsystemKind.MissileLauncher, -0.1, 0.014, -0.05, arcCenterDeg: 270, arcWidthDeg: 150),
                    new SubsystemDef(SubsystemKind.PointDefense, 0.045, 0.025, 0.3),
                    new SubsystemDef(SubsystemKind.PointDefense, -0.045, 0.025, 0.3),
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.06, -0.17),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.04, 0.3, arcCenterDeg: 0, arcWidthDeg: 60),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0.08, 0.04, 0.05, arcCenterDeg: 60, arcWidthDeg: 60),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0.08, 0.04, -0.25, arcCenterDeg: 120, arcWidthDeg: 60),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.04, -0.4, arcCenterDeg: 180, arcWidthDeg: 60),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, -0.08, 0.04, -0.25, arcCenterDeg: 240, arcWidthDeg: 60),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, -0.08, 0.04, 0.05, arcCenterDeg: 300, arcWidthDeg: 60),
                    new SubsystemDef(SubsystemKind.Engine, -0.025, 0, -0.47),
                    new SubsystemDef(SubsystemKind.Engine, 0.025, 0, -0.47),
                    new SubsystemDef(SubsystemKind.Engine, 0.1, 0, -0.225),
                    new SubsystemDef(SubsystemKind.Engine, -0.1, 0, -0.225),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.3),
                },
            } },
            [ShipClass.Destroyer] = new[] { new CombatSpec
            {
                DesignName = "Destroyer", LengthKm = 0.45,
                Hull = 5500, Shield = 2500, ShieldRegen = 30, Armor = 3, MaxSpeed = 0.24, Accel = 0.009, Decel = 0.011,
                BoostSpeedMultiplier = 1.5, BoostDurationS = 25, TurnRateDegS = 14,
                Crew = 280, Marines = 50,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.BoardingPods, 0, -0.035, 0.0),
                    // A fixed spinal railgun rather than a turret: the Destroyer's whole identity is
                    // closing fast to bring its nose to bear, then landing one hard shot.
                    new SubsystemDef(SubsystemKind.Railgun, 0, 0.03, 0.3, arcCenterDeg: 0, arcWidthDeg: 120),
                    new SubsystemDef(SubsystemKind.MissileLauncher, 0, 0.07, -0.25, arcCenterDeg: 0, arcWidthDeg: 150),
                    new SubsystemDef(SubsystemKind.PointDefense, 0.05, 0.02, 0.2),
                    new SubsystemDef(SubsystemKind.PointDefense, -0.05, 0.02, 0.2),
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.055, -0.08),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.03, 0.25, arcCenterDeg: 0, arcWidthDeg: 90),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0.07, 0.03, -0.1, arcCenterDeg: 90, arcWidthDeg: 90),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.03, -0.4, arcCenterDeg: 180, arcWidthDeg: 90),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, -0.07, 0.03, -0.1, arcCenterDeg: 270, arcWidthDeg: 90),
                    new SubsystemDef(SubsystemKind.Engine, -0.035, 0, -0.47),
                    new SubsystemDef(SubsystemKind.Engine, 0.035, 0, -0.47),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.25),
                },
            } },
            [ShipClass.Freighter] = new[] { new CombatSpec
            {
                DesignName = "Freighter", LengthKm = 0.6,
                Hull = 4500, Shield = 750, ShieldRegen = 9, Armor = 2, MaxSpeed = 0.05, Accel = 0.0015, Decel = 0.0018,
                BoostSpeedMultiplier = 1.25, BoostDurationS = 36, TurnRateDegS = 3,
                Crew = 40, Marines = 4,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.PointDefense, 0, 0.07, 0),
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.03, 0.43),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.05, 0.3, arcCenterDeg: 0, arcWidthDeg: 90),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0.1, 0.05, -0.1, arcCenterDeg: 90, arcWidthDeg: 90),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.05, -0.4, arcCenterDeg: 180, arcWidthDeg: 90),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, -0.1, 0.05, -0.1, arcCenterDeg: 270, arcWidthDeg: 90),
                    new SubsystemDef(SubsystemKind.Engine, 0, 0, -0.47),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.35),
                },
            } },
            [ShipClass.Miner] = new[] { new CombatSpec
            {
                DesignName = "Ore hauler", LengthKm = 0.3,
                Hull = 6500, Shield = 500, ShieldRegen = 6, Armor = 4, MaxSpeed = 0.04, Accel = 0.0012, Decel = 0.0014,
                BoostSpeedMultiplier = 1.2, BoostDurationS = 38, TurnRateDegS = 3,
                Crew = 30, Marines = 0,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.09, 0.3),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.08, 0.2, arcCenterDeg: 0, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.08, -0.3, arcCenterDeg: 180, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.Engine, -0.05, 0, -0.45),
                    new SubsystemDef(SubsystemKind.Engine, 0.05, 0, -0.45),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.3),
                },
            } },
            [ShipClass.Breacher] = new[] { new CombatSpec
            {
                DesignName = "Breacher", LengthKm = 0.18,
                Hull = 3000, Shield = 900, ShieldRegen = 15, Armor = 12, MaxSpeed = 0.3, Accel = 0.012, Decel = 0.014,
                BoostSpeedMultiplier = 1.6, BoostDurationS = 22, TurnRateDegS = 18,
                Crew = 20, Marines = 70,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.Drill, 0, 0, 0.45),
                    // The "heavy fighter": tankier and harder-hitting than the Courier but far slower
                    // to reload, piloted the same way as any other forward-mounted gun.
                    new SubsystemDef(SubsystemKind.HeavyLaser, 0, 0.03, 0.3, arcCenterDeg: 0, arcWidthDeg: 50),
                    new SubsystemDef(SubsystemKind.PointDefense, 0, 0.06, 0.05),
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.07, -0.1),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.05, 0.1, arcCenterDeg: 0, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.05, -0.3, arcCenterDeg: 180, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.Engine, -0.06, 0, -0.46),
                    new SubsystemDef(SubsystemKind.Engine, 0.06, 0, -0.46),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.25),
                },
            } },
            [ShipClass.Courier] = new[] { new CombatSpec
            {
                DesignName = "Courier", LengthKm = 0.08,
                Hull = 1300, Shield = 600, ShieldRegen = 12, Armor = 0, MaxSpeed = 0.35, Accel = 0.02, Decel = 0.024,
                BoostSpeedMultiplier = 1.8, BoostDurationS = 14, TurnRateDegS = 30,
                Crew = 6, Marines = 0,
                Layout = new[]
                {
                    // The "light fighter": the fastest, most agile hull in the game carries the
                    // fastest-firing forward gun to match -- rapid, low-damage autocannon fire, not a
                    // single hard-hitting shot.
                    new SubsystemDef(SubsystemKind.FighterGuns, 0, -0.01, 0.1, arcCenterDeg: 0, arcWidthDeg: 40),
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.035, 0.05),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.03, 0.03, arcCenterDeg: 0, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.03, -0.2, arcCenterDeg: 180, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.Engine, 0, 0, -0.44),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.2),
                },
            } },
            [ShipClass.Fighter] = new[] { new CombatSpec
            {
                DesignName = "Fighter", LengthKm = 0.1,
                Hull = 1800, Shield = 700, ShieldRegen = 12, Armor = 1, MaxSpeed = 0.32, Accel = 0.018, Decel = 0.02,
                BoostSpeedMultiplier = 1.7, BoostDurationS = 16, TurnRateDegS = 26,
                Crew = 2, Marines = 0,
                Layout = new[]
                {
                    // A dedicated dogfighter: twin forward guns rather than the Courier's single gun,
                    // traded against a lighter hull than either existing "fighter" archetype.
                    new SubsystemDef(SubsystemKind.FighterGuns, -0.09, 0, 0.1, arcCenterDeg: 0, arcWidthDeg: 40),
                    new SubsystemDef(SubsystemKind.FighterGuns, 0.09, 0, 0.1, arcCenterDeg: 0, arcWidthDeg: 40),
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.035, 0.05),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.03, 0.03, arcCenterDeg: 0, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.03, -0.2, arcCenterDeg: 180, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.Engine, 0, 0, -0.44),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.2),
                },
            } },
        };

        public static CombatSpec Of(ShipClass c) => Specs.TryGetValue(c, out var s) ? s[0] : Specs[ShipClass.Destroyer][0];

        /// <summary>Picks one of this class's designs at random, for a ship that will keep it for its lifetime.</summary>
        public static CombatSpec RandomOf(ShipClass c, Random rng)
        {
            var designs = Specs.TryGetValue(c, out var s) ? s : Specs[ShipClass.Destroyer];
            return designs[rng.Next(designs.Length)];
        }

        /// <summary>A settlement's orbital defence platform: a static gun station.</summary>
        public static readonly CombatSpec Platform = new CombatSpec
        {
            DesignName = "Defence platform", LengthKm = PlatformLengthKm,
            Hull = 21000, Shield = 11000, ShieldRegen = 66, Armor = 8, MaxSpeed = 0, Accel = 0, Decel = 0, TurnRateDegS = 0,
            Crew = 220, Marines = 60,
            Layout = new[]
            {
                new SubsystemDef(SubsystemKind.HeavyLaser, 0, 0.18, 0.12),
                new SubsystemDef(SubsystemKind.HeavyLaser, 0, -0.18, -0.12),
                new SubsystemDef(SubsystemKind.MissileLauncher, 0.3, 0, 0),
                new SubsystemDef(SubsystemKind.MissileLauncher, -0.3, 0, 0),
                new SubsystemDef(SubsystemKind.PointDefense, 0.2, 0.1, 0.25),
                new SubsystemDef(SubsystemKind.PointDefense, -0.2, 0.1, -0.25),
                new SubsystemDef(SubsystemKind.PointDefense, 0.2, -0.1, -0.25),
                new SubsystemDef(SubsystemKind.PointDefense, -0.2, -0.1, 0.25),
                new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.25, 0.2, arcCenterDeg: 0, arcWidthDeg: 90),
                new SubsystemDef(SubsystemKind.ShieldGenerator, 0.25, 0.25, 0, arcCenterDeg: 90, arcWidthDeg: 90),
                new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.25, -0.2, arcCenterDeg: 180, arcWidthDeg: 90),
                new SubsystemDef(SubsystemKind.ShieldGenerator, -0.25, 0.25, 0, arcCenterDeg: 270, arcWidthDeg: 90),
                new SubsystemDef(SubsystemKind.Reactor, 0, 0, 0),
            },
        };

        public const double PlatformLengthKm = 1.4;
    }
}
