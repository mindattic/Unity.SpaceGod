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

        public static readonly WeaponSpec Laser = new WeaponSpec { RangeKm = 16, Damage = 45, Cooldown = 3.0, Accuracy = 0.8 };
        public static readonly WeaponSpec Heavy = new WeaponSpec { RangeKm = 24, Damage = 190, Cooldown = 9.0, Accuracy = 0.72 };
        public static readonly WeaponSpec Missiles = new WeaponSpec { RangeKm = 40, Damage = 260, Cooldown = 24, Accuracy = 1, Missile = true, MissileSpeedKmS = 1.4, Salvo = 2, Ammo = 16 };
        public static readonly WeaponSpec PointDefense = new WeaponSpec { RangeKm = 3.5, Damage = 10, Cooldown = 0.35, Accuracy = 0.5, AntiSmall = true };
        public static readonly WeaponSpec FighterGuns = new WeaponSpec { RangeKm = 1.6, Damage = 14, Cooldown = 0.3, Accuracy = 0.55 };
        public static readonly WeaponSpec Torpedo = new WeaponSpec { RangeKm = 4, Damage = 420, Cooldown = 40, Accuracy = 1, Missile = true, MissileSpeedKmS = 0.9, Salvo = 1, Ammo = 2 };
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

        public SubsystemDef(SubsystemKind kind, double x, double y, double z, bool facesAft = false)
        {
            Kind = kind;
            X = x;
            Y = y;
            Z = z;
            FacesAft = facesAft;
            MaxHp = DefaultHp(kind);
            Weapon = DefaultWeapon(kind);
        }

        public bool IsWeapon => Weapon != null;

        /// <summary>Hardpoints are built to take punishment; a capital ship loses guns one at a time.</summary>
        static double DefaultHp(SubsystemKind k) => BaseHp(k) * (k == SubsystemKind.FighterGuns || k == SubsystemKind.Torpedo ? 1 : 3);

        static double BaseHp(SubsystemKind k)
        {
            switch (k)
            {
                case SubsystemKind.LaserTurret: return 220;
                case SubsystemKind.HeavyLaser: return 380;
                case SubsystemKind.MissileLauncher: return 260;
                case SubsystemKind.PointDefense: return 90;
                case SubsystemKind.Engine: return 300;
                case SubsystemKind.ShieldGenerator: return 260;
                case SubsystemKind.Bridge: return 320;
                case SubsystemKind.Hangar: return 520;
                case SubsystemKind.Reactor: return 650;
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
                case SubsystemKind.MissileLauncher: return WeaponSpec.Missiles;
                case SubsystemKind.PointDefense: return WeaponSpec.PointDefense;
                case SubsystemKind.FighterGuns: return WeaponSpec.FighterGuns;
                case SubsystemKind.Torpedo: return WeaponSpec.Torpedo;
                default: return null;
            }
        }

        public static string Name(SubsystemKind k)
        {
            switch (k)
            {
                case SubsystemKind.LaserTurret: return "Laser turret";
                case SubsystemKind.HeavyLaser: return "Heavy laser";
                case SubsystemKind.MissileLauncher: return "Missile launcher";
                case SubsystemKind.PointDefense: return "Point defence";
                case SubsystemKind.FighterGuns: return "Guns";
                case SubsystemKind.Torpedo: return "Torpedo";
                case SubsystemKind.Engine: return "Engine";
                case SubsystemKind.ShieldGenerator: return "Shield generator";
                case SubsystemKind.Bridge: return "Bridge";
                case SubsystemKind.Hangar: return "Hangar";
                default: return "Reactor";
            }
        }
    }

    /// <summary>A hull's fighting qualities and hardpoint layout (positions match the procedural models).</summary>
    public sealed class CombatSpec
    {
        public double Hull, Shield, ShieldRegen, Armor;

        /// <summary>Tactical speed and acceleration in battle, km/s and km/s².</summary>
        public double MaxSpeed, Accel;

        public double TurnRateDegS;
        public SubsystemDef[] Layout;

        /// <summary>Fighter squadrons a carrier can launch (6 craft each).</summary>
        public int Squadrons;

        static readonly Dictionary<ShipClass, CombatSpec> Specs = new Dictionary<ShipClass, CombatSpec>
        {
            [ShipClass.Carrier] = new CombatSpec
            {
                Hull = 26000, Shield = 11000, ShieldRegen = 75, Armor = 6, MaxSpeed = 0.07, Accel = 0.0025, TurnRateDegS = 4, Squadrons = 3,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.Bridge, 0.07, 0.14, -0.11),
                    new SubsystemDef(SubsystemKind.Hangar, 0, 0.012, 0.44),
                    new SubsystemDef(SubsystemKind.Hangar, 0.125, -0.01, 0.12),
                    new SubsystemDef(SubsystemKind.Hangar, -0.125, -0.01, 0.12),
                    new SubsystemDef(SubsystemKind.LaserTurret, 0.09, 0.055, 0.3),
                    new SubsystemDef(SubsystemKind.LaserTurret, -0.09, 0.055, 0.3),
                    new SubsystemDef(SubsystemKind.MissileLauncher, 0.12, 0.035, -0.06),
                    new SubsystemDef(SubsystemKind.MissileLauncher, -0.12, 0.035, -0.06),
                    new SubsystemDef(SubsystemKind.PointDefense, 0.1, 0.055, 0.42),
                    new SubsystemDef(SubsystemKind.PointDefense, -0.1, 0.055, 0.42),
                    new SubsystemDef(SubsystemKind.PointDefense, 0.12, 0.035, -0.3),
                    new SubsystemDef(SubsystemKind.PointDefense, -0.12, 0.035, -0.3),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, -0.06, 0.07, -0.22),
                    new SubsystemDef(SubsystemKind.Engine, -0.075, 0, -0.49),
                    new SubsystemDef(SubsystemKind.Engine, -0.027, 0, -0.49),
                    new SubsystemDef(SubsystemKind.Engine, 0.027, 0, -0.49),
                    new SubsystemDef(SubsystemKind.Engine, 0.075, 0, -0.49),
                    new SubsystemDef(SubsystemKind.Reactor, 0, -0.01, -0.3),
                },
            },
            [ShipClass.Battleship] = new CombatSpec
            {
                Hull = 30000, Shield = 13000, ShieldRegen = 90, Armor = 10, MaxSpeed = 0.09, Accel = 0.003, TurnRateDegS = 5,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.HeavyLaser, 0, 0.045, 0.20),
                    new SubsystemDef(SubsystemKind.HeavyLaser, 0, 0.06, 0.07),
                    new SubsystemDef(SubsystemKind.HeavyLaser, 0, 0.045, -0.28, true),
                    new SubsystemDef(SubsystemKind.LaserTurret, 0.06, 0.05, -0.02),
                    new SubsystemDef(SubsystemKind.LaserTurret, -0.06, 0.05, -0.02),
                    new SubsystemDef(SubsystemKind.MissileLauncher, 0.07, 0.0, 0.14),
                    new SubsystemDef(SubsystemKind.MissileLauncher, -0.07, 0.0, 0.14),
                    new SubsystemDef(SubsystemKind.PointDefense, 0.075, 0.03, 0.27),
                    new SubsystemDef(SubsystemKind.PointDefense, -0.075, 0.03, 0.27),
                    new SubsystemDef(SubsystemKind.PointDefense, 0.075, 0.03, -0.36),
                    new SubsystemDef(SubsystemKind.PointDefense, -0.075, 0.03, -0.36),
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.105, -0.11),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.05, -0.36),
                    new SubsystemDef(SubsystemKind.Engine, -0.042, 0, -0.48),
                    new SubsystemDef(SubsystemKind.Engine, 0, 0, -0.48),
                    new SubsystemDef(SubsystemKind.Engine, 0.042, 0, -0.48),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.22),
                },
            },
            [ShipClass.Cruiser] = new CombatSpec
            {
                Hull = 13000, Shield = 6000, ShieldRegen = 54, Armor = 6, MaxSpeed = 0.14, Accel = 0.005, TurnRateDegS = 8,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.LaserTurret, 0, 0.03, 0.15),
                    new SubsystemDef(SubsystemKind.LaserTurret, 0, 0.03, 0.0),
                    new SubsystemDef(SubsystemKind.MissileLauncher, 0.1, 0.014, -0.05),
                    new SubsystemDef(SubsystemKind.MissileLauncher, -0.1, 0.014, -0.05),
                    new SubsystemDef(SubsystemKind.PointDefense, 0.045, 0.025, 0.3),
                    new SubsystemDef(SubsystemKind.PointDefense, -0.045, 0.025, 0.3),
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.06, -0.17),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.035, -0.32),
                    new SubsystemDef(SubsystemKind.Engine, -0.025, 0, -0.47),
                    new SubsystemDef(SubsystemKind.Engine, 0.025, 0, -0.47),
                    new SubsystemDef(SubsystemKind.Engine, 0.1, 0, -0.225),
                    new SubsystemDef(SubsystemKind.Engine, -0.1, 0, -0.225),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.3),
                },
            },
            [ShipClass.Destroyer] = new CombatSpec
            {
                Hull = 5500, Shield = 2500, ShieldRegen = 30, Armor = 3, MaxSpeed = 0.24, Accel = 0.009, TurnRateDegS = 14,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.LaserTurret, 0, 0.03, 0.12),
                    new SubsystemDef(SubsystemKind.MissileLauncher, 0, 0.07, -0.25),
                    new SubsystemDef(SubsystemKind.PointDefense, 0.05, 0.02, 0.2),
                    new SubsystemDef(SubsystemKind.PointDefense, -0.05, 0.02, 0.2),
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.055, -0.08),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.03, -0.32),
                    new SubsystemDef(SubsystemKind.Engine, -0.035, 0, -0.47),
                    new SubsystemDef(SubsystemKind.Engine, 0.035, 0, -0.47),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.25),
                },
            },
            [ShipClass.Freighter] = new CombatSpec
            {
                Hull = 4500, Shield = 750, ShieldRegen = 9, Armor = 2, MaxSpeed = 0.05, Accel = 0.0015, TurnRateDegS = 3,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.PointDefense, 0, 0.07, 0),
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.03, 0.43),
                    new SubsystemDef(SubsystemKind.Engine, 0, 0, -0.47),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.35),
                },
            },
            [ShipClass.Miner] = new CombatSpec
            {
                Hull = 6500, Shield = 500, ShieldRegen = 6, Armor = 4, MaxSpeed = 0.04, Accel = 0.0012, TurnRateDegS = 3,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.09, 0.3),
                    new SubsystemDef(SubsystemKind.Engine, -0.05, 0, -0.45),
                    new SubsystemDef(SubsystemKind.Engine, 0.05, 0, -0.45),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.3),
                },
            },
            [ShipClass.Courier] = new CombatSpec
            {
                Hull = 1300, Shield = 600, ShieldRegen = 12, Armor = 0, MaxSpeed = 0.35, Accel = 0.02, TurnRateDegS = 30,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.035, 0.05),
                    new SubsystemDef(SubsystemKind.Engine, 0, 0, -0.44),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.2),
                },
            },
        };

        public static CombatSpec Of(ShipClass c) => Specs.TryGetValue(c, out var s) ? s : Specs[ShipClass.Destroyer];

        /// <summary>A settlement's orbital defence platform: a static gun station.</summary>
        public static readonly CombatSpec Platform = new CombatSpec
        {
            Hull = 21000, Shield = 11000, ShieldRegen = 66, Armor = 8, MaxSpeed = 0, Accel = 0, TurnRateDegS = 0,
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
                new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.25, 0),
                new SubsystemDef(SubsystemKind.Reactor, 0, 0, 0),
            },
        };

        public const double PlatformLengthKm = 1.4;
    }
}
