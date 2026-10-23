using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
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

        /// <summary>A fittable rapid-fire kinetic gun (U.SPC-T271): a fighter's main gun, or a light mount's on a bigger hull.</summary>
        Autocannon,

        /// <summary>A fighter-sized railgun: a slow, hard, fast-flying slug for a gun mount (U.SPC-T271).</summary>
        LightRailgun,

        /// <summary>Fires a homing electromagnetic burst that knocks out the drives and shields of what it bursts by (U.SPC-T271).</summary>
        EmpLauncher,

        /// <summary>Lays proximity mines astern (U.SPC-T271).</summary>
        MineLayer,

        /// <summary>An automated gun on its own turret mount (U.SPC-T278): it traverses, picks its targets and fires by
        /// itself, whoever is flying the hull and whether or not the trigger is held.</summary>
        AutoTurret,

        /// <summary>A capital torpedo tube (U.SPC-T377): a fixed launcher that fires one heavy, slow, hard-to-stop
        /// torpedo down the bow at a time. A carrier bomber's slung <see cref="Torpedo"/> is a different, smaller thing.</summary>
        TorpedoTube,

        /// <summary>Throws flare-and-chaff clouds astern that draw off missiles homing on the craft (U.SPC-T424, see <see cref="Decoy"/>).</summary>
        Countermeasures,

        /// <summary>A capital ship's spinal tachyon lance (U.SPC-T487, see <see cref="TachyonLances"/>): its shot lands the
        /// instant it is fired, at the longest reach of any gun, but it is fixed down the bow, charges slowly and starves
        /// the shields while it does.</summary>
        TachyonLance,
    }

    /// <summary>What a non-gun weapon puts out into the battle instead of firing at a target.</summary>
    public enum DeployKind { None, Mine, Decoy }

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

        /// <summary>A tachyon lance (U.SPC-T487): no travel time and no lead, and a hit as sure at the end of its reach
        /// as close in.</summary>
        public bool Lance;

        /// <summary>The tachyon lance: the longest reach of any gun and a hit second only to a capital torpedo, landing
        /// the instant it is fired, on a long recharge. Its figures are the <see cref="TachyonLances"/> settings'.</summary>
        public static readonly WeaponSpec TachyonLance = new WeaponSpec { RangeKm = 60, Damage = 650, Cooldown = 60, Accuracy = 0.95, Lance = true };

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

        /// <summary>An electromagnetic burst (<see cref="EmpLauncher"/>): seconds of dead drives and shields it leaves a
        /// small hull it bursts beside (a big one shrugs it off sooner), and how near the burst it reaches, km.</summary>
        public double EmpSeconds, EmpRadiusKm;

        /// <summary>What it puts out into the battle (a mine, a sentry turret) instead of firing at a target.</summary>
        public DeployKind Deploy;

        /// <summary>Mines, sentry turrets and EMP rounds: a hull's means of getting away or holding ground, not of
        /// fighting, so they make no hull armed, count for nothing in its strength and turn no broadside.</summary>
        public bool Supporting => Deploy != DeployKind.None || EmpSeconds > 0;

        /// <summary>A fittable autocannon: the fighter's rapid kinetic gun, as a module (U.SPC-T271).</summary>
        public static readonly WeaponSpec Autocannon = new WeaponSpec { RangeKm = 1.6, Damage = 14, Cooldown = 0.3, Accuracy = 0.55, MuzzleSpeedKmS = 3.5 };

        /// <summary>A fighter-sized railgun: four times the reach of an autocannon and a slug that hits five times as
        /// hard and flies far faster, one shot every two seconds -- the sniper's gun of a dogfight.</summary>
        public static readonly WeaponSpec LightRailgun = new WeaponSpec { RangeKm = 6, Damage = 80, Cooldown = 2.0, Accuracy = 0.85, MuzzleSpeedKmS = 9 };

        /// <summary>A homing EMP round: no damage, but a fighter it bursts beside drifts dead for eight seconds.</summary>
        public static readonly WeaponSpec Emp = new WeaponSpec { RangeKm = 5, Damage = 0, Cooldown = 18, Accuracy = 1, Missile = true, MissileSpeedKmS = 1.6, Salvo = 1, Ammo = 4, EmpSeconds = 8, EmpRadiusKm = 0.5 };

        /// <summary>Proximity mines: <see cref="RangeKm"/> is the blast's reach, <see cref="Damage"/> its force at the centre.</summary>
        public static readonly WeaponSpec Mines = new WeaponSpec { RangeKm = 0.6, Damage = 300, Cooldown = 3, Accuracy = 1, Ammo = 6, Deploy = DeployKind.Mine };

        /// <summary>An auto turret: a light, fast-traversing kinetic gun its own fire control lays on whatever hostile
        /// comes near, a fighter as readily as a hull (U.SPC-T278).</summary>
        public static readonly WeaponSpec AutoTurret = new WeaponSpec { RangeKm = 1.8, Damage = 9, Cooldown = 0.25, Accuracy = 0.6, MuzzleSpeedKmS = 3.5 };

        /// <summary>A capital torpedo (U.SPC-T377): the hardest single hit in the game, slow enough that point defence
        /// has time to work on it, and only six in the tube's magazine.</summary>
        public static readonly WeaponSpec HeavyTorpedo = new WeaponSpec { RangeKm = 32, Damage = 900, Cooldown = 45, Accuracy = 1, Missile = true, MissileSpeedKmS = 1.0, Salvo = 1, Ammo = 6 };

        /// <summary>A small craft's dogfight missile (U.SPC-T424): short-ranged, quick and agile enough to run down another
        /// fighter once the lock is made, and light enough for point defence or a flare to stop.</summary>
        public static readonly WeaponSpec DogfightMissile = new WeaponSpec { RangeKm = 3.5, Damage = 70, Cooldown = 3, Accuracy = 1, Missile = true, MissileSpeedKmS = 1.1, Salvo = 1, Ammo = 2, SeeksSmallCraft = true };

        /// <summary>A small craft's flare-and-chaff dispenser (U.SPC-T424): <see cref="Ammo"/> clouds, one every <see cref="Cooldown"/> seconds.</summary>
        public static readonly WeaponSpec Flares = new WeaponSpec { RangeKm = 0, Damage = 0, Cooldown = 2, Accuracy = 1, Ammo = 5, Deploy = DeployKind.Decoy };

        /// <summary>A guided round whose seeker locks onto small craft as readily as onto hulls (U.SPC-T424).</summary>
        public bool SeeksSmallCraft;

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

        /// <summary>The grade of what is fitted here (U.SPC-T65), 1 for the design's own part: a hardened
        /// shield generator's share of the hull's shield and regen against a stock one's, or a torch drive's
        /// thrust against a stock engine's (see <see cref="Loadout"/>). Ignored for other kinds.</summary>
        public double Rating = 1;

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
        static double DefaultHp(SubsystemKind k) => BaseHp(k) * (k == SubsystemKind.FighterGuns || k == SubsystemKind.Torpedo || k == SubsystemKind.Autocannon ? 1 : 3);

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
                case SubsystemKind.Autocannon: return 60;
                case SubsystemKind.LightRailgun: return 40;
                case SubsystemKind.EmpLauncher:
                case SubsystemKind.MineLayer: return 30;
                case SubsystemKind.AutoTurret: return 50;
                case SubsystemKind.TorpedoTube: return 280;
                case SubsystemKind.Countermeasures: return 30;
                case SubsystemKind.TachyonLance: return 450;
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
                case SubsystemKind.Autocannon: return WeaponSpec.Autocannon;
                case SubsystemKind.LightRailgun: return WeaponSpec.LightRailgun;
                case SubsystemKind.EmpLauncher: return WeaponSpec.Emp;
                case SubsystemKind.MineLayer: return WeaponSpec.Mines;
                case SubsystemKind.AutoTurret: return WeaponSpec.AutoTurret;
                case SubsystemKind.TorpedoTube: return WeaponSpec.HeavyTorpedo;
                case SubsystemKind.Countermeasures: return WeaponSpec.Flares;
                case SubsystemKind.TachyonLance: return TachyonLances.Spec;
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
                case SubsystemKind.Autocannon: return "Autocannon";
                case SubsystemKind.LightRailgun: return "Light railgun";
                case SubsystemKind.EmpLauncher: return "EMP launcher";
                case SubsystemKind.MineLayer: return "Mine layer";
                case SubsystemKind.AutoTurret: return "Auto turret";
                case SubsystemKind.TorpedoTube: return "Torpedo tube";
                case SubsystemKind.Countermeasures: return "Countermeasures";
                case SubsystemKind.TachyonLance: return "Tachyon lance";
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

        /// <summary>This design's own length — ships of the same class can be different sizes.</summary>
        public double LengthKm;

        public double Hull, Shield, ShieldRegen, Armor;

        /// <summary>Tactical speed and acceleration/deceleration in battle, km/s and km/s².</summary>
        public double MaxSpeed, Accel, Decel;

        /// <summary>Boost multiplies MaxSpeed while held, draining over BoostDurationS and recharging otherwise.</summary>
        public double BoostSpeedMultiplier = 1, BoostDurationS;

        public double TurnRateDegS;

        /// <summary>The hull's real drive (U.SPC-T59), what a piloted ship flies on in
        /// <see cref="PilotFlight"/>: its mass in kilotonnes, the thrust of its main drive (along the nose),
        /// its retro thrusters (against it) and its manoeuvre thrusters per axis (sideways and up/down),
        /// all in meganewtons, so an acceleration in km/s² is thrust / (mass × 1000). The AI's battle
        /// kinematics stay on <see cref="MaxSpeed"/>/<see cref="Accel"/>/<see cref="Decel"/>; these were
        /// set so each class's main drive pushes it as hard as its pilot tuning did before the hull had
        /// mass, with retro thrust a quarter weaker and manoeuvre thrust well under half.</summary>
        public double MassKt, MainThrustMN, RetroThrustMN, LateralThrustMN, VerticalThrustMN;

        /// <summary>Attitude torque, MN·km: about the pitch and yaw axes (one figure, the hull being
        /// long and roughly round in section) and about the roll axis along its length.</summary>
        public double PitchYawTorqueMNKm, RollTorqueMNKm;

        /// <summary>Beam as a fraction of <see cref="LengthKm"/>, for the roll moment of inertia.</summary>
        public double BeamFraction = 0.2;

        /// <summary>Moment of inertia about the pitch and yaw axes, kt·km²: a uniform rod of the hull's
        /// length (m L² / 12).</summary>
        public double PitchYawInertia => MassKt * LengthKm * LengthKm / 12;

        /// <summary>Moment of inertia about the roll axis, kt·km²: a uniform cylinder of the hull's beam
        /// (m r² / 2), far smaller than pitch/yaw, so a hull rolls much more readily than it turns.</summary>
        public double RollInertia
        {
            get
            {
                double r = LengthKm * BeamFraction * 0.5;
                return MassKt * r * r / 2;
            }
        }

        public SubsystemDef[] Layout;

        /// <summary>Fighter squadrons a carrier can launch (6 craft each).</summary>
        public int Squadrons;

        /// <summary>People aboard: the ship's company, and the marines who board and repel boarders.</summary>
        public double Crew, Marines;

        /// <summary>
        /// Propulsion (see <see cref="Propulsion"/>): the hull's mass with empty tanks and an empty hold, its
        /// tanks' capacity, both in tonnes, and the speed its fusion drive throws reaction mass out at, km/s.
        /// Torch-drive fiction on real rocketry: the rocket equation sets what every manoeuvre costs.
        /// </summary>
        public double DryMassTonnes, PropellantTonnes, ExhaustVelocityKmS;

        static readonly Dictionary<ShipClass, CombatSpec[]> Specs = new Dictionary<ShipClass, CombatSpec[]>
        {
            [ShipClass.Carrier] = new[] { new CombatSpec
            {
                DesignName = "Carrier", LengthKm = 3.2,
                MassKt = 40000, MainThrustMN = 100000, RetroThrustMN = 75000, LateralThrustMN = 35000, VerticalThrustMN = 35000, PitchYawTorqueMNKm = 4770000, RollTorqueMNKm = 1190000,
                Hull = 26000, Shield = 11000, ShieldRegen = 75, Armor = 6, MaxSpeed = 0.07, Accel = 0.0025, Decel = 0.0025,
                BoostSpeedMultiplier = 1.3, BoostDurationS = 30, TurnRateDegS = 4, Squadrons = 3,
                Crew = 3800, Marines = 320,
                DryMassTonnes = 140000, PropellantTonnes = 70000, ExhaustVelocityKmS = 4000,
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
                MassKt = 30000, MainThrustMN = 90000, RetroThrustMN = 67500, LateralThrustMN = 31500, VerticalThrustMN = 31500, PitchYawTorqueMNKm = 3140000, RollTorqueMNKm = 785000,
                Hull = 30000, Shield = 13000, ShieldRegen = 90, Armor = 10, MaxSpeed = 0.09, Accel = 0.003, Decel = 0.003,
                BoostSpeedMultiplier = 1.3, BoostDurationS = 32, TurnRateDegS = 5,
                Crew = 2400, Marines = 260,
                DryMassTonnes = 120000, PropellantTonnes = 60000, ExhaustVelocityKmS = 4000,
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
                    // A pair of bow torpedo tubes under the forward heavy battery (U.SPC-T377).
                    new SubsystemDef(SubsystemKind.TorpedoTube, 0.03, -0.025, 0.46, arcCenterDeg: 0, arcWidthDeg: 60),
                    new SubsystemDef(SubsystemKind.TorpedoTube, -0.03, -0.025, 0.46, arcCenterDeg: 0, arcWidthDeg: 60),
                },
            } },
            [ShipClass.Cruiser] = new[] { new CombatSpec
            {
                DesignName = "Cruiser", LengthKm = 1.2,
                MassKt = 6000, MainThrustMN = 30000, RetroThrustMN = 22500, LateralThrustMN = 12000, VerticalThrustMN = 12000, PitchYawTorqueMNKm = 335000, RollTorqueMNKm = 83800,
                Hull = 13000, Shield = 6000, ShieldRegen = 54, Armor = 6, MaxSpeed = 0.14, Accel = 0.005, Decel = 0.006,
                BoostSpeedMultiplier = 1.4, BoostDurationS = 28, TurnRateDegS = 8,
                Crew = 900, Marines = 140,
                DryMassTonnes = 45000, PropellantTonnes = 22000, ExhaustVelocityKmS = 4000,
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
                    // One bow torpedo tube in the keel (U.SPC-T377).
                    new SubsystemDef(SubsystemKind.TorpedoTube, 0, -0.02, 0.45, arcCenterDeg: 0, arcWidthDeg: 60),
                },
            } },
            [ShipClass.Destroyer] = new[] { new CombatSpec
            {
                DesignName = "Destroyer", LengthKm = 0.45,
                MassKt = 700, MainThrustMN = 6300, RetroThrustMN = 4720, LateralThrustMN = 2520, VerticalThrustMN = 2520, PitchYawTorqueMNKm = 14400, RollTorqueMNKm = 3610,
                Hull = 5500, Shield = 2500, ShieldRegen = 30, Armor = 3, MaxSpeed = 0.24, Accel = 0.009, Decel = 0.011,
                BoostSpeedMultiplier = 1.5, BoostDurationS = 25, TurnRateDegS = 14,
                Crew = 280, Marines = 50,
                DryMassTonnes = 9000, PropellantTonnes = 5000, ExhaustVelocityKmS = 4000,
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
                MassKt = 900, MainThrustMN = 27000, RetroThrustMN = 20200, LateralThrustMN = 10800, VerticalThrustMN = 10800, PitchYawTorqueMNKm = 101000, RollTorqueMNKm = 25200,
                Hull = 4500, Shield = 750, ShieldRegen = 9, Armor = 2, MaxSpeed = 0.05, Accel = 0.0015, Decel = 0.0018,
                BoostSpeedMultiplier = 1.25, BoostDurationS = 36, TurnRateDegS = 3,
                Crew = 40, Marines = 4,
                DryMassTonnes = 9000, PropellantTonnes = 6000, ExhaustVelocityKmS = 2500,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.PointDefense, 0, 0.07, 0),
                    // Its utility mount: mines laid astern for whatever chases it (U.SPC-T271).
                    new SubsystemDef(SubsystemKind.MineLayer, 0, -0.04, -0.45, facesAft: true, arcCenterDeg: 180),
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
                MassKt = 500, MainThrustMN = 14000, RetroThrustMN = 10500, LateralThrustMN = 5600, VerticalThrustMN = 5600, PitchYawTorqueMNKm = 11500, RollTorqueMNKm = 2860,
                Hull = 6500, Shield = 500, ShieldRegen = 6, Armor = 4, MaxSpeed = 0.04, Accel = 0.0012, Decel = 0.0014,
                BoostSpeedMultiplier = 1.2, BoostDurationS = 38, TurnRateDegS = 3,
                Crew = 30, Marines = 0,
                DryMassTonnes = 8000, PropellantTonnes = 7000, ExhaustVelocityKmS = 2200,
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
                MassKt = 120, MainThrustMN = 5400, RetroThrustMN = 4050, LateralThrustMN = 2700, VerticalThrustMN = 2700, PitchYawTorqueMNKm = 4150, RollTorqueMNKm = 1040,
                Hull = 3000, Shield = 900, ShieldRegen = 15, Armor = 12, MaxSpeed = 0.3, Accel = 0.012, Decel = 0.014,
                BoostSpeedMultiplier = 1.6, BoostDurationS = 22, TurnRateDegS = 18,
                Crew = 20, Marines = 70,
                DryMassTonnes = 1800, PropellantTonnes = 900, ExhaustVelocityKmS = 3500,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.Drill, 0, 0, 0.45),
                    // A heavy forward gun to clear its way in, piloted the same way as any other forward-mounted gun.
                    new SubsystemDef(SubsystemKind.HeavyLaser, 0, 0.03, 0.3, arcCenterDeg: 0, arcWidthDeg: 50),
                    new SubsystemDef(SubsystemKind.PointDefense, 0, 0.06, 0.05),
                    // Its utility mount: an auto turret on the spine, covering it from fighters while it closes to latch (U.SPC-T278).
                    new SubsystemDef(SubsystemKind.AutoTurret, 0, 0.06, -0.2),
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
                MassKt = 3, MainThrustMN = 150, RetroThrustMN = 113, LateralThrustMN = 75, VerticalThrustMN = 75, PitchYawTorqueMNKm = 34.9, RollTorqueMNKm = 8.73,
                Hull = 1300, Shield = 600, ShieldRegen = 12, Armor = 0, MaxSpeed = 0.35, Accel = 0.02, Decel = 0.024,
                BoostSpeedMultiplier = 1.8, BoostDurationS = 14, TurnRateDegS = 30,
                Crew = 6, Marines = 0,
                DryMassTonnes = 220, PropellantTonnes = 180, ExhaustVelocityKmS = 4500,
                Layout = new[]
                {
                    // The "light fighter": the fastest, most agile hull in the game carries the
                    // fastest-firing forward gun to match -- rapid, low-damage autocannon fire, not a
                    // single hard-hitting shot.
                    new SubsystemDef(SubsystemKind.Autocannon, 0, -0.01, 0.1, arcCenterDeg: 0, arcWidthDeg: 40),
                    // Its utility mount: an EMP launcher to stop what it cannot outgun (U.SPC-T271).
                    new SubsystemDef(SubsystemKind.EmpLauncher, 0, -0.03, 0),
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
                MassKt = 4, MainThrustMN = 220, RetroThrustMN = 165, LateralThrustMN = 110, VerticalThrustMN = 110, PitchYawTorqueMNKm = 120, RollTorqueMNKm = 30,
                Hull = 1800, Shield = 700, ShieldRegen = 12, Armor = 1, MaxSpeed = 0.32, Accel = 0.018, Decel = 0.02,
                BoostSpeedMultiplier = 1.7, BoostDurationS = 16, TurnRateDegS = 26,
                Crew = 2, Marines = 0,
                DryMassTonnes = 60, PropellantTonnes = 30, ExhaustVelocityKmS = 3000,
                Layout = new[]
                {
                    // A dedicated dogfighter: twin forward guns rather than the Courier's single gun,
                    // traded against a lighter hull than either existing "fighter" archetype.
                    new SubsystemDef(SubsystemKind.Autocannon, -0.09, 0, 0.1, arcCenterDeg: 0, arcWidthDeg: 40),
                    new SubsystemDef(SubsystemKind.Autocannon, 0.09, 0, 0.1, arcCenterDeg: 0, arcWidthDeg: 40),
                    // Its utility mount: a missile rack (U.SPC-T271).
                    new SubsystemDef(SubsystemKind.MissileLauncher, 0, -0.03, 0),
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.035, 0.05),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.03, 0.03, arcCenterDeg: 0, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.03, -0.2, arcCenterDeg: 180, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.Engine, 0, 0, -0.44),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.2),
                },
            } },
            [ShipClass.Explorer] = new[] { new CombatSpec
            {
                // The deep-space explorer (U.SPC-T419): long-legged rather than fast or tough, a big reactor and tanks
                // for its FTL drive and months out between the stars, a light gun and heavy shields for what it meets there.
                DesignName = "Explorer", LengthKm = 0.22,
                MassKt = 60, MainThrustMN = 1800, RetroThrustMN = 1350, LateralThrustMN = 900, VerticalThrustMN = 900, PitchYawTorqueMNKm = 900, RollTorqueMNKm = 225,
                Hull = 2600, Shield = 1600, ShieldRegen = 20, Armor = 2, MaxSpeed = 0.3, Accel = 0.012, Decel = 0.014,
                BoostSpeedMultiplier = 1.5, BoostDurationS = 18, TurnRateDegS = 20,
                Crew = 40, Marines = 0,
                DryMassTonnes = 2400, PropellantTonnes = 2600, ExhaustVelocityKmS = 4500,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.Autocannon, 0, -0.02, 0.35, arcCenterDeg: 0, arcWidthDeg: 60),
                    new SubsystemDef(SubsystemKind.PointDefense, 0, 0.05, 0.1),
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.06, 0.2),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.04, 0.2, arcCenterDeg: 0, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.04, -0.25, arcCenterDeg: 180, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.Engine, -0.04, 0, -0.46),
                    new SubsystemDef(SubsystemKind.Engine, 0.04, 0, -0.46),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.2),
                },
            } },
            [ShipClass.Gunship] = new[] { new CombatSpec
            {
                // The heavy fighter (U.SPC-T467): an armoured multi-crew brawler, slower and stiffer than a light fighter
                // but plated like a small warship, with a spinal light railgun for corvette armour and two crewed laser
                // turrets (dorsal and ventral, all round) that keep firing whichever way the pilot points the nose.
                DesignName = "Gunship", LengthKm = 0.14,
                MassKt = 80, MainThrustMN = 4000, RetroThrustMN = 3000, LateralThrustMN = 2000, VerticalThrustMN = 2000, PitchYawTorqueMNKm = 1900, RollTorqueMNKm = 475,
                Hull = 2600, Shield = 1100, ShieldRegen = 16, Armor = 10, MaxSpeed = 0.3, Accel = 0.013, Decel = 0.015,
                BoostSpeedMultiplier = 1.6, BoostDurationS = 20, TurnRateDegS = 20,
                Crew = 8, Marines = 0,
                DryMassTonnes = 1400, PropellantTonnes = 700, ExhaustVelocityKmS = 3500,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.LightRailgun, 0, -0.02, 0.4, arcCenterDeg: 0, arcWidthDeg: 40),
                    new SubsystemDef(SubsystemKind.LaserTurret, 0, 0.06, 0.1),
                    new SubsystemDef(SubsystemKind.LaserTurret, 0, -0.06, -0.05),
                    new SubsystemDef(SubsystemKind.PointDefense, 0, 0.06, -0.2),
                    // Its utility mount: a missile rack (a small craft's, see Loadout.IsSmallCraft).
                    new SubsystemDef(SubsystemKind.MissileLauncher, 0, -0.04, 0.15),
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.05, 0.2),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.04, 0.15, arcCenterDeg: 0, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.04, -0.3, arcCenterDeg: 180, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.Engine, -0.06, 0, -0.46),
                    new SubsystemDef(SubsystemKind.Engine, 0.06, 0, -0.46),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.25),
                },
            } },
            [ShipClass.OreTug] = new[] { new CombatSpec
            {
                // The ore tug (U.SPC-T483): a big engine with claws and almost nothing else. Unladen it is quick for its size
                // (three main drives on a short frame); latched onto a rock of millions of tonnes it barely creeps (see
                // BeltMining.TugAccel). No hold, no guns: a skeleton crew, plate over the reactor, and shields for the grit.
                DesignName = "Ore tug", LengthKm = 0.2,
                MassKt = 200, MainThrustMN = 12000, RetroThrustMN = 6000, LateralThrustMN = 2400, VerticalThrustMN = 2400, PitchYawTorqueMNKm = 2000, RollTorqueMNKm = 500,
                Hull = 5000, Shield = 400, ShieldRegen = 5, Armor = 6, MaxSpeed = 0.05, Accel = 0.0016, Decel = 0.0012,
                BoostSpeedMultiplier = 1.2, BoostDurationS = 30, TurnRateDegS = 4,
                Crew = 12, Marines = 0,
                DryMassTonnes = 3000, PropellantTonnes = 2500, ExhaustVelocityKmS = 3000,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.08, 0.2),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.07, 0.25, arcCenterDeg: 0, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.07, -0.3, arcCenterDeg: 180, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.Engine, -0.08, 0, -0.45),
                    new SubsystemDef(SubsystemKind.Engine, 0, 0, -0.47),
                    new SubsystemDef(SubsystemKind.Engine, 0.08, 0, -0.45),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.25),
                },
            } },
        };

        /// <summary>A copy to refit (U.SPC-T65): every figure the same, the hardpoint list its own array
        /// (sharing the unchanged <see cref="SubsystemDef"/>s, which are never mutated).</summary>
        public CombatSpec Clone()
        {
            var c = (CombatSpec)MemberwiseClone();
            c.Layout = (SubsystemDef[])Layout.Clone();
            return c;
        }

        public static CombatSpec Of(ShipClass c) => Specs.TryGetValue(c, out var s) ? s[0] : Specs[ShipClass.Destroyer][0];

        /// <summary>Every design of a class (the construction catalogue lists each as its own hull frame, U.SPC-T377).</summary>
        public static IReadOnlyList<CombatSpec> DesignsOf(ShipClass c) => Specs.TryGetValue(c, out var s) ? s : Array.Empty<CombatSpec>();

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
