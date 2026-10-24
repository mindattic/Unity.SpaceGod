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

        /// <summary>A phaser-style beam array (U.SPC-T488): a versatile medium-range energy weapon its captain tunes in battle,
        /// a narrow lance for hulls or a wide sweep for fighters and missiles (see <see cref="EnergyWeapons"/>).</summary>
        PhaserArray,

        /// <summary>A plasma cannon (U.SPC-T488): a devastating short-range bolt whose damage bleeds off over distance.</summary>
        PlasmaCannon,

        /// <summary>An antimatter (photon) torpedo launcher (U.SPC-T489): a heavy guided warhead at medium range for frigates
        /// and capitals, the hardest single hit in the game, tough but not too tough for a screen to shoot down.</summary>
        AntimatterTorpedo,

        /// <summary>A rotary flak cannon (U.SPC-T489): a destroyer's short-ranged screen gun, whose shells burst among
        /// missiles and fighters and hurt everything hostile in the burst.</summary>
        FlakCannon,

        /// <summary>An interceptor missile launcher (U.SPC-T489): fast guided rounds that run down incoming missiles and
        /// fighters before they reach the hulls the screen is guarding.</summary>
        InterceptorMissile,
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
        /// <summary>The share of <see cref="Damage"/> a shot still does at the very edge of <see cref="RangeKm"/>, falling off
        /// evenly from full at the muzzle (a plasma bolt bleeding off over distance, U.SPC-T488); 1, the default, for a weapon
        /// that hits as hard at any range.</summary>
        public double FalloffEdgeShare = 1;

        /// <summary>What a shot does landing <paramref name="km"/> from the muzzle (see <see cref="FalloffEdgeShare"/>).</summary>
        public double DamageAt(double km)
        {
            if (FalloffEdgeShare >= 1 || RangeKm <= 0) return Damage;
            double f = Math.Max(0, Math.Min(1, km / RangeKm));
            return Damage * (1 - (1 - Math.Max(0, FalloffEdgeShare)) * f);
        }

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

        /// <summary>A flak shell's burst, km (U.SPC-T489): every hostile missile and fighter within it is hurt, not only the one
        /// it was laid on. Zero for every weapon that hits only what it hits.</summary>
        public double BurstKm;

        /// <summary>An antimatter torpedo (U.SPC-T489, see <see cref="ScreenWeapons"/> for its settings): medium-ranged, quicker
        /// than a tube's torpedo and nearly twice the warhead, four to a launcher.</summary>
        public static readonly WeaponSpec AntimatterTorpedo = new WeaponSpec { RangeKm = 20, Damage = 1600, Cooldown = 35, Accuracy = 1, Missile = true, MissileSpeedKmS = 1.3, Salvo = 1, Ammo = 4 };

        /// <summary>A rotary flak cannon (U.SPC-T489): a shell every 0.8 s out to 5 km, each bursting over
        /// <see cref="BurstKm"/> (times <see cref="ScreenWeapons.FlakBurst"/>) among the missiles and fighters it was laid on.</summary>
        public static readonly WeaponSpec Flak = new WeaponSpec { RangeKm = 5, Damage = 30, Cooldown = 0.8, Accuracy = 0.6, AntiSmall = true, BurstKm = 0.4 };

        /// <summary>An interceptor missile (U.SPC-T489): a fast, agile round that hunts an incoming missile (or a fighter) out
        /// to 14 km, its warhead enough to kill any one of them.</summary>
        public static readonly WeaponSpec Interceptor = new WeaponSpec { RangeKm = 14, Damage = 80, Cooldown = 3, Accuracy = 1, Missile = true, MissileSpeedKmS = 3.2, Salvo = 1, Ammo = 20, AntiSmall = true, SeeksSmallCraft = true };

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
                case SubsystemKind.PhaserArray: return 260;
                case SubsystemKind.PlasmaCannon: return 400;
                case SubsystemKind.AntimatterTorpedo: return 300;
                case SubsystemKind.FlakCannon: return 110;
                case SubsystemKind.InterceptorMissile: return 120;
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
                case SubsystemKind.PhaserArray: return EnergyWeapons.PhaserLance;
                case SubsystemKind.PlasmaCannon: return EnergyWeapons.PlasmaCannon;
                case SubsystemKind.AntimatterTorpedo: return WeaponSpec.AntimatterTorpedo;
                case SubsystemKind.FlakCannon: return WeaponSpec.Flak;
                case SubsystemKind.InterceptorMissile: return WeaponSpec.Interceptor;
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
                case SubsystemKind.PhaserArray: return "Phaser array";
                case SubsystemKind.PlasmaCannon: return "Plasma cannon";
                case SubsystemKind.AntimatterTorpedo: return "Antimatter torpedo";
                case SubsystemKind.FlakCannon: return "Flak cannon";
                case SubsystemKind.InterceptorMissile: return "Interceptor missiles";
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

        /// <summary>People aboard: the ship's company, and the marines who board and repel boarders. The marines are the
        /// complement its marine habitats quarter (U.SPC-T465, <see cref="FacilityLayouts.Complement(ShipClass, CombatSpec)"/>).</summary>
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
                Crew = 3800,
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
                    // A dorsal antimatter torpedo launcher on the bow (U.SPC-T489): a capital's heavy guided warhead.
                    new SubsystemDef(SubsystemKind.AntimatterTorpedo, 0, 0.06, 0.36, arcCenterDeg: 0, arcWidthDeg: 90),
                },
            } },
            [ShipClass.Battleship] = new[] { new CombatSpec
            {
                DesignName = "Battleship", LengthKm = 2.4,
                MassKt = 30000, MainThrustMN = 90000, RetroThrustMN = 67500, LateralThrustMN = 31500, VerticalThrustMN = 31500, PitchYawTorqueMNKm = 3140000, RollTorqueMNKm = 785000,
                Hull = 30000, Shield = 13000, ShieldRegen = 90, Armor = 10, MaxSpeed = 0.09, Accel = 0.003, Decel = 0.003,
                BoostSpeedMultiplier = 1.3, BoostDurationS = 32, TurnRateDegS = 5,
                Crew = 2400,
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
                    // A dorsal antimatter torpedo launcher ahead of the forward battery (U.SPC-T489).
                    new SubsystemDef(SubsystemKind.AntimatterTorpedo, 0, 0.05, 0.33, arcCenterDeg: 0, arcWidthDeg: 90),
                },
            } },
            [ShipClass.Cruiser] = new[] { new CombatSpec
            {
                DesignName = "Cruiser", LengthKm = 1.2,
                MassKt = 6000, MainThrustMN = 30000, RetroThrustMN = 22500, LateralThrustMN = 12000, VerticalThrustMN = 12000, PitchYawTorqueMNKm = 335000, RollTorqueMNKm = 83800,
                Hull = 13000, Shield = 6000, ShieldRegen = 54, Armor = 6, MaxSpeed = 0.14, Accel = 0.005, Decel = 0.006,
                BoostSpeedMultiplier = 1.4, BoostDurationS = 28, TurnRateDegS = 8,
                Crew = 900,
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
                Crew = 280,
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
                    // The anti-fighter screen beside its point defence (U.SPC-T489): a rotary flak cannon on each beam and an
                    // interceptor launcher amidships, the umbrella that stops missile swarms and fighters short of the capitals.
                    new SubsystemDef(SubsystemKind.FlakCannon, 0.06, 0.03, 0.05),
                    new SubsystemDef(SubsystemKind.FlakCannon, -0.06, 0.03, 0.05),
                    new SubsystemDef(SubsystemKind.InterceptorMissile, 0, 0.05, -0.15),
                },
            } },
            // A truss freighter (U.SPC-T482, see BulkFreighters): a bare spine with pods and fuel spheres clamped along it and one
            // big engine astern. No armour and no weapons, since every kilogram goes to cargo (its escorts carry the guns), and
            // a weak drive for its mass, so off the lanes it is slow to speed up and slow down. Its shields only turn debris.
            [ShipClass.Freighter] = new[] { new CombatSpec
            {
                DesignName = "Freighter", LengthKm = 0.6,
                MassKt = 900, MainThrustMN = 13500, RetroThrustMN = 9000, LateralThrustMN = 7200, VerticalThrustMN = 7200, PitchYawTorqueMNKm = 101000, RollTorqueMNKm = 25200,
                Hull = 4500, Shield = 750, ShieldRegen = 9, Armor = 0, MaxSpeed = 0.05, Accel = 0.0008, Decel = 0.0008,
                BoostSpeedMultiplier = 1.25, BoostDurationS = 36, TurnRateDegS = 3,
                Crew = 40,
                DryMassTonnes = 9000, PropellantTonnes = BulkFreighters.StockSpheres * BulkFreighters.SphereTonnes, ExhaustVelocityKmS = 2500,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.03, 0.43),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.05, 0.3, arcCenterDeg: 0, arcWidthDeg: 90),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0.1, 0.05, -0.1, arcCenterDeg: 90, arcWidthDeg: 90),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.05, -0.4, arcCenterDeg: 180, arcWidthDeg: 90),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, -0.1, 0.05, -0.1, arcCenterDeg: 270, arcWidthDeg: 90),
                    new SubsystemDef(SubsystemKind.Engine, 0, 0, -0.47),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.38),
                },
            } },
            [ShipClass.Miner] = new[] { new CombatSpec
            {
                DesignName = "Ore hauler", LengthKm = 0.3,
                MassKt = 500, MainThrustMN = 14000, RetroThrustMN = 10500, LateralThrustMN = 5600, VerticalThrustMN = 5600, PitchYawTorqueMNKm = 11500, RollTorqueMNKm = 2860,
                Hull = 6500, Shield = 500, ShieldRegen = 6, Armor = 4, MaxSpeed = 0.04, Accel = 0.0012, Decel = 0.0014,
                BoostSpeedMultiplier = 1.2, BoostDurationS = 38, TurnRateDegS = 3,
                Crew = 30,
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
                Crew = 20,
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
                Crew = 6,
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
                Crew = 2,
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
                Crew = 40,
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
                Crew = 8,
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
                Crew = 12,
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
            [ShipClass.Interceptor] = new[] { new CombatSpec
            {
                // The interceptor (U.SPC-T484): smaller, faster and quicker to turn than the Fighter, one pilot, light twin
                // guns and a missile rack, and thin plating: it hunts craft and screens bombers, it does not trade blows.
                DesignName = "Interceptor", LengthKm = 0.07,
                MassKt = 2.5, MainThrustMN = 175, RetroThrustMN = 130, LateralThrustMN = 95, VerticalThrustMN = 95, PitchYawTorqueMNKm = 60, RollTorqueMNKm = 15,
                Hull = 1100, Shield = 450, ShieldRegen = 10, Armor = 0, MaxSpeed = 0.36, Accel = 0.024, Decel = 0.026,
                BoostSpeedMultiplier = 1.8, BoostDurationS = 12, TurnRateDegS = 32,
                Crew = 1,
                DryMassTonnes = 35, PropellantTonnes = 25, ExhaustVelocityKmS = 3200,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.Autocannon, -0.07, 0, 0.12, arcCenterDeg: 0, arcWidthDeg: 40),
                    new SubsystemDef(SubsystemKind.Autocannon, 0.07, 0, 0.12, arcCenterDeg: 0, arcWidthDeg: 40),
                    new SubsystemDef(SubsystemKind.MissileLauncher, 0, -0.03, 0),
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.03, 0.08),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.025, 0.03, arcCenterDeg: 0, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.025, -0.2, arcCenterDeg: 180, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.Engine, 0, 0, -0.44),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.2),
                },
            } },
            [ShipClass.Bomber] = new[] { new CombatSpec
            {
                // The bomber (U.SPC-T484): slow, heavily plated and shielded, a pilot, a bombardier and a tail gunner, with
                // a pair of torpedo tubes for capital ships (they hit hardest once a capital's shields are down, see
                // StrikeTorpedoes), a defensive gun and point defence astern. It needs an escort.
                DesignName = "Bomber", LengthKm = 0.14,
                MassKt = 9, MainThrustMN = 300, RetroThrustMN = 225, LateralThrustMN = 110, VerticalThrustMN = 110, PitchYawTorqueMNKm = 380, RollTorqueMNKm = 95,
                Hull = 3200, Shield = 1100, ShieldRegen = 14, Armor = 3, MaxSpeed = 0.26, Accel = 0.012, Decel = 0.013,
                BoostSpeedMultiplier = 1.5, BoostDurationS = 12, TurnRateDegS = 18,
                Crew = 3,
                DryMassTonnes = 160, PropellantTonnes = 70, ExhaustVelocityKmS = 3000,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.TorpedoTube, -0.08, -0.03, 0.1, arcCenterDeg: 0, arcWidthDeg: 40),
                    new SubsystemDef(SubsystemKind.TorpedoTube, 0.08, -0.03, 0.1, arcCenterDeg: 0, arcWidthDeg: 40),
                    new SubsystemDef(SubsystemKind.Autocannon, 0, -0.02, 0.3, arcCenterDeg: 0, arcWidthDeg: 40),
                    new SubsystemDef(SubsystemKind.PointDefense, 0, 0.04, -0.3),
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.04, 0.15),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.035, 0.05, arcCenterDeg: 0, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.035, -0.2, arcCenterDeg: 180, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.Engine, -0.05, 0, -0.45),
                    new SubsystemDef(SubsystemKind.Engine, 0.05, 0, -0.45),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.25),
                },
            } },
            [ShipClass.GatePicket] = new[] { new CombatSpec
            {
                // The gate picket (U.SPC-T480): a sensor mast with a drive, not a warship. A light gun and point defence for
                // what strays near it, good shields to last until word is out, and a long-range search suite (see
                // SensorSuite.Picket) that is the whole reason it is there.
                DesignName = "Gate picket", LengthKm = 0.12,
                MassKt = 20, MainThrustMN = 700, RetroThrustMN = 520, LateralThrustMN = 350, VerticalThrustMN = 350, PitchYawTorqueMNKm = 260, RollTorqueMNKm = 65,
                Hull = 1500, Shield = 1100, ShieldRegen = 16, Armor = 1, MaxSpeed = 0.3, Accel = 0.014, Decel = 0.016,
                BoostSpeedMultiplier = 1.5, BoostDurationS = 16, TurnRateDegS = 22,
                Crew = 14,
                DryMassTonnes = 700, PropellantTonnes = 500, ExhaustVelocityKmS = 3500,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.Autocannon, 0, -0.02, 0.3, arcCenterDeg: 0, arcWidthDeg: 60),
                    new SubsystemDef(SubsystemKind.PointDefense, 0, 0.05, 0.05),
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.04, 0.15),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.04, 0.15, arcCenterDeg: 0, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.04, -0.25, arcCenterDeg: 180, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.Engine, 0, 0, -0.46),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.2),
                },
            } },
            [ShipClass.LaneTender] = new[] { new CombatSpec
            {
                // The lane tender (U.SPC-T481): a maintenance hull that keeps a hyperlane's rings powered and aligned. No
                // guns at all: a big reactor to feed the rings it works on, heavy shields and plate to ride out what comes for
                // it, and a crew of engineers rather than gunners.
                DesignName = "Lane tender", LengthKm = 0.26,
                MassKt = 120, MainThrustMN = 3600, RetroThrustMN = 2700, LateralThrustMN = 1800, VerticalThrustMN = 1800, PitchYawTorqueMNKm = 2200, RollTorqueMNKm = 550,
                Hull = 3400, Shield = 1500, ShieldRegen = 16, Armor = 3, MaxSpeed = 0.2, Accel = 0.008, Decel = 0.01,
                BoostSpeedMultiplier = 1.3, BoostDurationS = 24, TurnRateDegS = 12,
                Crew = 30,
                DryMassTonnes = 3600, PropellantTonnes = 3000, ExhaustVelocityKmS = 3500,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.06, 0.32),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.05, 0.2, arcCenterDeg: 0, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.05, -0.25, arcCenterDeg: 180, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.Engine, -0.05, 0, -0.46),
                    new SubsystemDef(SubsystemKind.Engine, 0.05, 0, -0.46),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.1),
                },
            } },
            [ShipClass.Interdictor] = new[] { new CombatSpec
            {
                // The lane interdictor (U.SPC-T479): a destroyer-sized spine round the field projector that throws ships out
                // of a hyperlane, two reactors to feed it, and little left over for guns: it pulls a mark out for the rest of
                // its group to fight, and keeps itself alive with its shields and point defence.
                DesignName = "Lane interdictor", LengthKm = 0.38,
                MassKt = 500, MainThrustMN = 4200, RetroThrustMN = 3150, LateralThrustMN = 1700, VerticalThrustMN = 1700, PitchYawTorqueMNKm = 9000, RollTorqueMNKm = 2250,
                Hull = 4200, Shield = 3000, ShieldRegen = 35, Armor = 3, MaxSpeed = 0.22, Accel = 0.008, Decel = 0.010,
                BoostSpeedMultiplier = 1.4, BoostDurationS = 20, TurnRateDegS = 12,
                Crew = 160,
                DryMassTonnes = 7000, PropellantTonnes = 4500, ExhaustVelocityKmS = 4000,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.Autocannon, 0, 0.04, 0.3, arcCenterDeg: 0, arcWidthDeg: 120),
                    new SubsystemDef(SubsystemKind.MissileLauncher, 0, 0.06, -0.3, arcCenterDeg: 180, arcWidthDeg: 150),
                    new SubsystemDef(SubsystemKind.PointDefense, 0.06, 0.02, 0.1),
                    new SubsystemDef(SubsystemKind.PointDefense, -0.06, 0.02, 0.1),
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.05, 0.18),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.03, 0.2, arcCenterDeg: 0, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.03, -0.2, arcCenterDeg: 180, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.Engine, -0.035, 0, -0.47),
                    new SubsystemDef(SubsystemKind.Engine, 0.035, 0, -0.47),
                    // The field projector's two reactors, either side of the ring amidships.
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, 0.05),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.15),
                },
            } },
            [ShipClass.CommandCruiser] = new[] { new CombatSpec
            {
                // The command cruiser (U.SPC-T477): a battlecruiser's hull between a cruiser and a battleship, whose space and
                // power go to the sensor array, the communications suite and the targeting computers inside it (see
                // FacilityLayouts and FleetCoordination) rather than guns: a cruiser's battery, no torpedoes and no boarding
                // pods, but heavy shields, two reactors and a big crew with the fleet's staff aboard.
                DesignName = "Command cruiser", LengthKm = 1.6,
                MassKt = 12000, MainThrustMN = 48000, RetroThrustMN = 36000, LateralThrustMN = 18000, VerticalThrustMN = 18000, PitchYawTorqueMNKm = 900000, RollTorqueMNKm = 225000,
                Hull = 17000, Shield = 9000, ShieldRegen = 70, Armor = 7, MaxSpeed = 0.12, Accel = 0.0045, Decel = 0.005,
                BoostSpeedMultiplier = 1.35, BoostDurationS = 30, TurnRateDegS = 7,
                Crew = 1400,
                DryMassTonnes = 70000, PropellantTonnes = 35000, ExhaustVelocityKmS = 4000,
                Layout = new[]
                {
                    new SubsystemDef(SubsystemKind.LaserTurret, 0, 0.04, 0.22, arcCenterDeg: 0, arcWidthDeg: 120),
                    new SubsystemDef(SubsystemKind.LaserTurret, 0, 0.04, -0.3, true, arcCenterDeg: 180, arcWidthDeg: 120),
                    new SubsystemDef(SubsystemKind.MissileLauncher, 0.09, 0.02, 0.0, arcCenterDeg: 90, arcWidthDeg: 150),
                    new SubsystemDef(SubsystemKind.MissileLauncher, -0.09, 0.02, 0.0, arcCenterDeg: 270, arcWidthDeg: 150),
                    new SubsystemDef(SubsystemKind.PointDefense, 0.05, 0.03, 0.3),
                    new SubsystemDef(SubsystemKind.PointDefense, -0.05, 0.03, 0.3),
                    new SubsystemDef(SubsystemKind.PointDefense, 0.06, 0.03, -0.32),
                    new SubsystemDef(SubsystemKind.PointDefense, -0.06, 0.03, -0.32),
                    // The flag bridge, buried amidships under the array's mast.
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.05, 0.05),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.05, 0.34, arcCenterDeg: 0, arcWidthDeg: 60),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0.08, 0.05, 0.12, arcCenterDeg: 60, arcWidthDeg: 60),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0.08, 0.05, -0.2, arcCenterDeg: 120, arcWidthDeg: 60),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.05, -0.42, arcCenterDeg: 180, arcWidthDeg: 60),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, -0.08, 0.05, -0.2, arcCenterDeg: 240, arcWidthDeg: 60),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, -0.08, 0.05, 0.12, arcCenterDeg: 300, arcWidthDeg: 60),
                    new SubsystemDef(SubsystemKind.Engine, -0.04, 0, -0.48),
                    new SubsystemDef(SubsystemKind.Engine, 0, 0, -0.48),
                    new SubsystemDef(SubsystemKind.Engine, 0.04, 0, -0.48),
                    // Two reactors: the array and the suite draw as much as a battery would.
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.1),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.3),
                },
            } },
            [ShipClass.Frigate] = new[] { new CombatSpec
            {
                // The frigate (U.SPC-T475, see AlphaStrike): smaller and lighter than a destroyer, with a quarter again its
                // drive on half its mass, so it is the fastest warship there is; it pays for it in plate (thin hull, light
                // shields, almost no armour) and in guns (none but point defence). Its whole battery is forward-firing
                // launchers fed from deep magazines: four long-range anti-ship missile racks and two antimatter (photon)
                // torpedo launchers, loosed together in one alpha strike.
                DesignName = "Frigate", LengthKm = 0.32,
                MassKt = 350, MainThrustMN = 7000, RetroThrustMN = 5250, LateralThrustMN = 2800, VerticalThrustMN = 2800, PitchYawTorqueMNKm = 5400, RollTorqueMNKm = 1350,
                Hull = 2600, Shield = 1400, ShieldRegen = 20, Armor = 1, MaxSpeed = 0.38, Accel = 0.02, Decel = 0.022,
                BoostSpeedMultiplier = 1.6, BoostDurationS = 30, TurnRateDegS = 20,
                Crew = 120,
                DryMassTonnes = 4500, PropellantTonnes = 4000, ExhaustVelocityKmS = 4000,
                Layout = new[]
                {
                    // The anti-ship missile racks, either side of the spine, bearing forward.
                    new SubsystemDef(SubsystemKind.MissileLauncher, 0.06, 0.035, 0.15, arcCenterDeg: 0, arcWidthDeg: 150) { Weapon = AlphaStrike.AntiShipMissile },
                    new SubsystemDef(SubsystemKind.MissileLauncher, -0.06, 0.035, 0.15, arcCenterDeg: 0, arcWidthDeg: 150) { Weapon = AlphaStrike.AntiShipMissile },
                    new SubsystemDef(SubsystemKind.MissileLauncher, 0.06, -0.035, 0.15, arcCenterDeg: 0, arcWidthDeg: 150) { Weapon = AlphaStrike.AntiShipMissile },
                    new SubsystemDef(SubsystemKind.MissileLauncher, -0.06, -0.035, 0.15, arcCenterDeg: 0, arcWidthDeg: 150) { Weapon = AlphaStrike.AntiShipMissile },
                    // Two antimatter (photon) torpedo launchers in the bow.
                    new SubsystemDef(SubsystemKind.AntimatterTorpedo, 0.03, 0, 0.42, arcCenterDeg: 0, arcWidthDeg: 90) { Weapon = AlphaStrike.MagazineTorpedo },
                    new SubsystemDef(SubsystemKind.AntimatterTorpedo, -0.03, 0, 0.42, arcCenterDeg: 0, arcWidthDeg: 90) { Weapon = AlphaStrike.MagazineTorpedo },
                    new SubsystemDef(SubsystemKind.PointDefense, 0.04, 0.03, 0.28),
                    new SubsystemDef(SubsystemKind.PointDefense, -0.04, 0.03, -0.2),
                    new SubsystemDef(SubsystemKind.Bridge, 0, 0.045, -0.02),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.03, 0.2, arcCenterDeg: 0, arcWidthDeg: 180),
                    new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.03, -0.3, arcCenterDeg: 180, arcWidthDeg: 180),
                    // Engine-heavy: four big bells astern, a third of the hull's length in engine rooms.
                    new SubsystemDef(SubsystemKind.Engine, -0.045, 0.025, -0.47),
                    new SubsystemDef(SubsystemKind.Engine, 0.045, 0.025, -0.47),
                    new SubsystemDef(SubsystemKind.Engine, -0.045, -0.025, -0.47),
                    new SubsystemDef(SubsystemKind.Engine, 0.045, -0.025, -0.47),
                    new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.32),
                },
            } },
        };

        /// <summary>Every class's marines are what its design's habitats quarter (U.SPC-T465, <see cref="MarineHabitats"/>), not
        /// a flat figure per class: a hull with no habitat carries none and repels boarders with its crew alone.</summary>
        static CombatSpec()
        {
            foreach (var kv in Specs)
                foreach (var design in kv.Value) design.Marines = FacilityLayouts.Complement(kv.Key, design);
        }

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
            // Its garrison is what its barracks quarter (U.SPC-T465).
            Crew = 220, Marines = FacilityLayouts.Complement(FacilityLayouts.Platform, 1),
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
