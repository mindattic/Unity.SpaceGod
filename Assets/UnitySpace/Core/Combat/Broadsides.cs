using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    // The capital-ship broadside game (U.SPC-T425): commanding a capital ship as a naval battle, Black Flag / Rebel Galaxy
    // style. Batteries are ordered by the arc they fire into, a volley is called and telegraphed, shields are angled toward
    // a facing, the hull is angled so shots strike its armour obliquely, the crew braces for a broadside and damage control
    // teams patch mounts under fire, and the flagship orders its line into formation. AI captains use the same commands
    // by doctrine. The handling itself (engine telegraph, steerage, keel, full 3D) is the capital flight model's
    // (U.SPC-T499, docs/FLIGHT_MODELS.md); this file is everything a captain does on top of it.

    /// <summary>The four arcs a capital ship's batteries, shields and armour are ordered by (U.SPC-T425).</summary>
    public enum Facing
    {
        Bow,
        Starboard,
        Stern,
        Port,
    }

    /// <summary>What a battery's crews do with a target in its arc (U.SPC-T425).</summary>
    public enum BatteryOrder
    {
        /// <summary>Every gun fires as it bears and is loaded (what a crew does on its own).</summary>
        FireAtWill,

        /// <summary>No gun fires into the arc.</summary>
        HoldFire,

        /// <summary>The guns hold until the battery is loaded, then fire together: a broadside, telegraphed to the target
        /// while it builds, surer and harder than fire at will.</summary>
        Volley,
    }

    /// <summary>Where a capital ship's damage control teams work first (U.SPC-T425).</summary>
    public enum DamageControlFocus
    {
        /// <summary>The worst-hurt mount first, whatever it is.</summary>
        Balanced,

        Weapons,

        /// <summary>Shield generators, and keeping their arcs charging under fire.</summary>
        Shields,

        Engines,
    }

    /// <summary>The formations a flagship orders its line into (U.SPC-T425).</summary>
    public enum FleetFormation
    {
        /// <summary>No order given: escorts keep loose station as they always have.</summary>
        None,

        /// <summary>A column astern of the flagship, every broadside clear to bear on the beam.</summary>
        LineAhead,

        /// <summary>Abreast of the flagship, alternately to starboard and to port, every bow bearing ahead.</summary>
        LineAbreast,

        /// <summary>Stepped back to starboard: bows and starboard batteries both clear.</summary>
        Echelon,

        /// <summary>A ring around the flagship, to take fire and fighters off it.</summary>
        Screen,

        /// <summary>The line breaks up and each ship fights its own fight.</summary>
        EngageAtWill,
    }

    /// <summary>The arcs: which one a bearing lies in, and their names.</summary>
    public static class Facings
    {
        public const int Count = 4;

        public static readonly Facing[] All = { Facing.Bow, Facing.Starboard, Facing.Stern, Facing.Port };

        /// <summary>The arc's centre, deg (0 bow, 90 starboard, 180 stern, 270 port).</summary>
        public static double CenterDeg(Facing f) => (int)f * 90;

        /// <summary>The arc a bearing (deg from the bow, clockwise from above) lies in: each covers 90 deg about its centre.</summary>
        public static Facing Of(double yawDeg)
        {
            double y = Astro.WrapDegrees360(yawDeg);
            return y < 45 || y >= 315 ? Facing.Bow : y < 135 ? Facing.Starboard : y < 225 ? Facing.Stern : Facing.Port;
        }

        /// <summary>The arc of <paramref name="u"/> a battle-local point lies in.</summary>
        public static Facing Toward(BattleUnit u, DVec3 point) => Of(u.YawDegOf(point));

        /// <summary>How far a bearing lies from an arc's centre, deg (0-180).</summary>
        public static double OffDeg(double yawDeg, Facing f)
        {
            double d = Astro.WrapDegrees360(yawDeg - CenterDeg(f));
            return d > 180 ? 360 - d : d;
        }

        public static string Label(Facing f) => f == Facing.Bow ? "bow" : f == Facing.Starboard ? "starboard" : f == Facing.Stern ? "stern" : "port";
    }

    /// <summary>
    /// A capital ship's standing orders and state in the broadside game (U.SPC-T425), on its <see cref="BattleUnit"/>: the
    /// order each arc's battery fires under, the facing its shields are angled to, its crew's brace, where damage control
    /// works, the formation it has ordered its line into, and the broadside it is building (its telegraph).
    /// </summary>
    public sealed class BroadsideCommand
    {
        readonly BatteryOrder[] orders = new BatteryOrder[Facings.Count];

        public BatteryOrder this[Facing f]
        {
            get => orders[(int)f];
            set => orders[(int)f] = value;
        }

        /// <summary>The facing the shields are angled to, or null for an even spread.</summary>
        public Facing? ShieldTrim;

        /// <summary>Seconds the crew stays braced, and until it can brace again.</summary>
        public double BraceLeft, BraceCooldown;

        public bool Braced => BraceLeft > 0;

        public DamageControlFocus Focus;

        /// <summary>The formation this hull has ordered its line into (it leads it).</summary>
        public FleetFormation Formation;

        /// <summary>The telegraph: the arc a volley is building on, the hull it will go at, and the share of its guns loaded
        /// (1 fires it). Null while no volley is building.</summary>
        public Facing? Readying;

        public BattleUnit ReadyingOn;
        public double ReadyingLoaded;

        /// <summary>Keep the guns and drive at full however hot the hull gets (U.SPC-T491, see <see cref="Radiators"/>): past
        /// the heat sinks' limit the crew and systems take the heat instead of the crew throttling down.</summary>
        public bool RunHot;

        /// <summary>Hit points damage control has put back this battle (for the HUD and the tests).</summary>
        public double Repaired;

        /// <summary>This step: the arcs whose volley goes now.</summary>
        internal readonly bool[] Releasing = new bool[Facings.Count];

        public bool Releases(Facing f) => Releasing[(int)f];
    }

    /// <summary>The broadside game's rules (U.SPC-T425): batteries, shield angling, armour angling, the brace, damage
    /// control, formations and turning circles. Tunables are declared settings.</summary>
    [SettingsSource]
    public static class Broadsides
    {
        // ------------------------------------------------------------ settings

        public static readonly RangeSetting ArmourAngling = new RangeSetting("broadside.armourAngling", SettingGroups.Broadsides,
            "Armour angling", 0.4, 0, 1, 0.05,
            "How much a hull angled to the line of fire turns shots aside: the share of a hit lost to a fully glancing strike, and how much thicker the belt reads end-on (0 turns angling off).",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "armor armour angle glance belt broadside capital");

        public static readonly RangeSetting BraceReduction = new RangeSetting("broadside.brace", SettingGroups.Broadsides,
            "Brace", 0.5, 0, 0.9, 0.05,
            "The share of every hit a braced crew shrugs off while the brace lasts (its guns hold their fire while it is braced).",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "brace broadside capital volley");

        public static readonly RangeSetting DamageControlRate = new RangeSetting("broadside.damageControl", SettingGroups.Broadsides,
            "Damage control rate", 1, 0, 3, 0.25,
            "How fast a capital ship's damage control teams patch its mounts, shields and engines in battle: 0 turns damage control off, 1 is as designed.",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + "x", "repair crew damage control capital");

        public static readonly RangeSetting ShieldAngling = new RangeSetting("broadside.shieldAngling", SettingGroups.Broadsides,
            "Shield angling strength", 1.5, 0, 3, 0.25,
            "How much of the shield's charge angling it to a facing draws to that side: 0 leaves the spread even.",
            v => v.ToString("0.##", CultureInfo.InvariantCulture), "shield angle deflector facing capital");

        public static readonly ToggleSetting AiVolleys = new ToggleSetting("broadside.aiVolleys", SettingGroups.Broadsides,
            "AI captains fire broadsides", true,
            "AI capital ships hold their beam batteries for a volley and fire them together, telegraphing it, as the player can.",
            "volley broadside ai doctrine");

        public static readonly ToggleSetting AiAnglesShields = new ToggleSetting("broadside.aiShields", SettingGroups.Broadsides,
            "AI captains angle shields", true,
            "AI capital ships angle their shields toward the hull they are fighting.",
            "shield angle ai doctrine");

        public static readonly ToggleSetting AiBraces = new ToggleSetting("broadside.aiBrace", SettingGroups.Broadsides,
            "AI captains brace", true,
            "AI capital ships brace when a broadside they can see building is about to come in on them.",
            "brace ai doctrine");

        // ------------------------------------------------------------ constants

        /// <summary>The smallest hull commanded as a capital ship, km (a destroyer and up).</summary>
        public static double MinCapitalKm => WeakPoints.MinCapitalKm;

        /// <summary>How long a brace lasts, and how long before the crew can brace again, s.</summary>
        public const double BraceSeconds = 3, BraceCooldownSeconds = 12;

        /// <summary>How much harder, and surer, a crew's volley is than the same guns at will.</summary>
        public const double CrewVolleyDamageBonus = 0.2, CrewVolleyAccuracyBonus = 0.2;

        /// <summary>How loaded a battery building a volley on a hull must be before that hull's AI captain braces.</summary>
        public const double BraceAtLoaded = 0.75;

        /// <summary>People per damage control team, the most teams any hull musters, the hit points a team puts back a
        /// second, and how whole it can make a mount in the field (a refit at a yard does the rest).</summary>
        public const double CrewPerTeam = 250, RepairHpPerTeamS = 1.5, FieldRepairCap = 0.85;

        public const int MaxTeams = 8;

        /// <summary>The share of the hull's whole shield that angling moves between arcs a second.</summary>
        public const double TrimRatePerS = 0.08;

        /// <summary>The thinnest a hit can find the armour by angle: obliquity no more than this multiplies it.</summary>
        public const double MaxObliquity = 3;

        // ------------------------------------------------------------ who commands

        /// <summary>A hull fought as a capital ship: a ship of a destroyer's size and up.</summary>
        public static bool Commands(BattleUnit u) => u != null && u.Kind == UnitKind.Ship && u.LengthKm >= MinCapitalKm;

        /// <summary><paramref name="u"/>'s orders (made on first asking), or null for anything smaller than a capital ship.</summary>
        public static BroadsideCommand Of(BattleUnit u) => !Commands(u) ? null : u.Broadside ??= new BroadsideCommand();

        // ------------------------------------------------------------ batteries

        /// <summary>A gun that fires in broadsides: a live direct-fire mount (not a missile, point defence or a support mount).</summary>
        public static bool IsBatteryGun(Subsystem s)
        {
            var w = s.Def.Weapon;
            // A spinal mount is no battery: the hull lays it, not the arc's gun crews (U.SPC-T490).
            return w != null && !s.Destroyed && !w.Missile && !w.AntiSmall && !w.Supporting && s.Kind != SubsystemKind.BoardingPods && !s.Def.Spinal;
        }

        /// <summary>Whether a mount covers an arc's centre: the guns that make up that arc's battery.</summary>
        public static bool Bears(SubsystemDef d, Facing f) => d.ArcWidthDeg >= 360 || d.ContainsYaw(Facings.CenterDeg(f));

        /// <summary>The guns of <paramref name="u"/>'s <paramref name="f"/> battery, and how many are loaded.</summary>
        public static (int loaded, int guns) Battery(BattleUnit u, Facing f)
        {
            int loaded = 0, guns = 0;
            foreach (var s in u.Combat.Subsystems)
            {
                if (!IsBatteryGun(s) || !Bears(s.Def, f)) continue;
                guns++;
                if (s.Cooldown <= 0) loaded++;
            }
            return (loaded, guns);
        }

        /// <summary>The order the guns of <paramref name="u"/> fire into the arc of a point under (fire at will if it has none).</summary>
        public static BatteryOrder OrderToward(BattleUnit u, DVec3 point) =>
            u.Broadside == null ? BatteryOrder.FireAtWill : u.Broadside[Facings.Toward(u, point)];

        /// <summary>Steps <paramref name="f"/>'s battery to its next order (at will, hold, volley); returns the new one.</summary>
        public static BatteryOrder CycleOrder(BattleUnit u, Facing f)
        {
            var cmd = Of(u);
            if (cmd == null) return BatteryOrder.FireAtWill;
            cmd[f] = (BatteryOrder)(((int)cmd[f] + 1) % 3);
            return cmd[f];
        }

        /// <summary>Puts every battery under <paramref name="order"/>.</summary>
        public static void SetAll(BattleUnit u, BatteryOrder order)
        {
            var cmd = Of(u);
            if (cmd == null) return;
            foreach (var f in Facings.All) cmd[f] = order;
        }

        /// <summary>
        /// Before a hull's guns fire this step: which arcs' volleys go now. A volley goes once the guns covering its arc's
        /// centre are all loaded (with none, as soon as any gun into it is), and every loaded gun firing into the arc goes
        /// with it. The telegraph is cleared, to be raised again by the guns still holding (<see cref="Holding"/>).
        /// </summary>
        public static void BeginVolleys(BattleUnit u)
        {
            var cmd = u.Broadside;
            if (cmd == null) return;
            cmd.Readying = null;
            cmd.ReadyingOn = null;
            cmd.ReadyingLoaded = 0;
            foreach (var f in Facings.All)
            {
                if (cmd[f] != BatteryOrder.Volley)
                {
                    cmd.Releasing[(int)f] = false;
                    continue;
                }
                var (loaded, guns) = Battery(u, f);
                cmd.Releasing[(int)f] = guns == 0 ? true : loaded == guns;
            }
        }

        /// <summary>A gun holds its fire for the volley building on <paramref name="target"/> in <paramref name="f"/>: the
        /// telegraph shows it, with how loaded the battery is.</summary>
        public static void Holding(BattleUnit u, Facing f, BattleUnit target)
        {
            var cmd = u.Broadside;
            if (cmd == null || cmd.Readying != null) return;
            var (loaded, guns) = Battery(u, f);
            cmd.Readying = f;
            cmd.ReadyingOn = target;
            cmd.ReadyingLoaded = guns == 0 ? 1 : (double)loaded / guns;
        }

        // ------------------------------------------------------------ shields

        /// <summary>Angles <paramref name="u"/>'s shields to <paramref name="f"/> (null spreads them evenly again); returns the trim.</summary>
        public static Facing? TrimShields(BattleUnit u, Facing? f)
        {
            var cmd = Of(u);
            if (cmd == null) return null;
            cmd.ShieldTrim = f;
            return f;
        }

        /// <summary>
        /// How much of an even share of the shield a generator covering <paramref name="arcCenterDeg"/> holds with the
        /// shields angled to <paramref name="trim"/>, before the live generators' shares are evened back to a mean of 1:
        /// those facing the trim most squarely hold most.
        /// </summary>
        static double TrimWeight(double arcCenterDeg, Facing? trim)
        {
            if (trim == null) return 1;
            double off = Facings.OffDeg(arcCenterDeg, trim.Value) * Astro.Deg2Rad;
            return 1 + ShieldAngling.Value * Math.Max(0, Math.Cos(off));
        }

        /// <summary>
        /// The shields settle toward their angling: each live generator's cap is its share of the hull's shield times its
        /// trim weight (the weights averaging 1, so the whole stays what it was), and charge flows from those over their
        /// cap to those under it at <see cref="TrimRatePerS"/> of the hull's shield a second.
        /// </summary>
        public static void StepShields(BattleUnit u, double dt)
        {
            var cmd = u.Broadside;
            var c = u.Combat;
            double sum = 0;
            int n = 0;
            foreach (var s in c.Subsystems)
            {
                if (s.Kind != SubsystemKind.ShieldGenerator || s.Destroyed) continue;
                sum += TrimWeight(s.Def.ArcCenterDeg, cmd?.ShieldTrim);
                n++;
            }
            if (n == 0) return;
            double mean = sum / n;
            double over = 0, under = 0;
            foreach (var s in c.Subsystems)
            {
                if (s.Kind != SubsystemKind.ShieldGenerator) continue;
                s.ShieldCapScale = s.Destroyed ? 1 : TrimWeight(s.Def.ArcCenterDeg, cmd?.ShieldTrim) / mean;
                if (s.Destroyed) continue;
                double cap = s.ShieldCap;
                if (s.Shield > cap) over += s.Shield - cap;
                else under += cap - s.Shield;
            }
            double move = Math.Min(Math.Min(over, under), TrimRatePerS * c.Spec.Shield * dt);
            if (move <= 0) return;
            foreach (var s in c.Subsystems)
            {
                if (s.Kind != SubsystemKind.ShieldGenerator || s.Destroyed) continue;
                double cap = s.ShieldCap;
                if (s.Shield > cap) s.Shield -= (s.Shield - cap) / over * move;
                else s.Shield += (cap - s.Shield) / under * move;
            }
        }

        // ------------------------------------------------------------ armour

        /// <summary>Armour per face, as a share of the hull's own rating: the bow is heavier, the stern (the engines) lighter.</summary>
        public static double BowArmour = 1.25, BeamArmour = 1, SternArmour = 0.6;

        /// <summary>
        /// How a hit travelling along <paramref name="incoming"/> meets <paramref name="target"/>'s armour: which face it
        /// strikes (the side, unless it comes in within a few degrees of end-on, the hull being five times as long as it is
        /// broad) and how squarely. Returns the multiplier on the hull's armour rating (the face's thickness over the
        /// cosine of the strike, at most <see cref="MaxObliquity"/>, scaled by <see cref="ArmourAngling"/>) and the share
        /// of the hit that is not turned aside (a fully glancing strike loses <see cref="ArmourAngling"/> of it).
        /// </summary>
        public static (double armour, double through) Angling(BattleUnit target, DVec3 incoming)
        {
            double k = ArmourAngling.Value;
            if (k <= 0 || incoming.LengthSquared < 1e-18) return (1, 1);
            var d = incoming.Normalized;
            target.Basis(out var right, out var up);
            double dx = DVec3.Dot(d, right), dy = DVec3.Dot(d, up), dz = DVec3.Dot(d, target.Forward);
            double lateral = Math.Sqrt(dx * dx + dy * dy), along = Math.Abs(dz);
            double beam = target.Ship?.Combat?.Spec?.BeamFraction ?? 0.2;
            double face, cos;
            if (lateral >= along * beam)
            {
                face = BeamArmour;
                cos = lateral;
            }
            else
            {
                // Coming in from ahead the shot travels aft along the hull and meets the bow.
                face = dz < 0 ? BowArmour : SternArmour;
                cos = along;
            }
            double obliquity = Math.Min(MaxObliquity, 1 / Math.Max(cos, 1 / MaxObliquity));
            double armour = 1 + (face * obliquity - 1) * k;
            double through = 1 - k * (1 - cos);
            return (Math.Max(0, armour), Math.Max(0, Math.Min(1, through)));
        }

        // ------------------------------------------------------------ the brace

        /// <summary>The crew braces for a broadside: <see cref="BraceSeconds"/> of every hit shrugged off by
        /// <see cref="BraceReduction"/>, its guns holding their fire meanwhile. False while the last brace is still wearing off.</summary>
        public static bool Brace(BattleUnit u)
        {
            var cmd = Of(u);
            if (cmd == null || cmd.Braced || cmd.BraceCooldown > 0) return false;
            cmd.BraceLeft = BraceSeconds;
            cmd.BraceCooldown = BraceCooldownSeconds;
            return true;
        }

        /// <summary>The share of a hit that gets past <paramref name="u"/>'s crew: less while braced.</summary>
        public static double BraceFactor(BattleUnit u) => u.Broadside != null && u.Broadside.Braced ? 1 - BraceReduction.Value : 1;

        /// <summary>Whether <paramref name="u"/>'s crews work its guns this step: not while braced (they load, and fire when
        /// the brace is over; point defence fires on its own throughout).</summary>
        public static bool Fires(BattleUnit u) => u.Broadside == null || !u.Broadside.Braced;

        /// <summary>
        /// The broadside about to come in on <paramref name="u"/>: a hostile's volley building on it with most of its guns
        /// loaded, or a hostile helmsman building one with the trigger held while looking its way. Null if none is.
        /// </summary>
        public static BattleUnit IncomingBroadside(Battle b, BattleUnit u)
        {
            double cone = Math.Cos(Battle.VolleyConeDeg * Astro.Deg2Rad);
            foreach (var o in b.Units)
            {
                if (!b.Hostile(u, o) || o.Stunned > 0 || o.Surrendering || o.Captured) continue;
                var cmd = o.Broadside;
                if (cmd != null && cmd.Readying != null && cmd.ReadyingOn == u && cmd.ReadyingLoaded >= BraceAtLoaded) return o;
                if (o.PlayerControlled && o.PlayerHelm && o.PlayerFireHeld && o.VolleyCharge >= Battle.VolleyBuildSeconds * 0.5 && o.PlayerVolleyAim.LengthSquared > 1e-12)
                {
                    var to = u.Pos - o.Pos;
                    if (to.LengthSquared > 1e-12 && DVec3.Dot(o.PlayerVolleyAim.Normalized, to.Normalized) > cone) return o;
                }
            }
            return null;
        }

        // ------------------------------------------------------------ damage control

        /// <summary>The damage control teams <paramref name="u"/> musters: one per <see cref="CrewPerTeam"/> of its crew
        /// (half while boarders are aboard), at most <see cref="MaxTeams"/>.</summary>
        public static int Teams(BattleUnit u)
        {
            var interior = u.Combat.Interior;
            if (interior == null) return 0;
            double crew = interior.Crew * (interior.UnderBoarding ? 0.5 : 1);
            return (int)Math.Min(MaxTeams, Math.Floor(crew / CrewPerTeam));
        }

        /// <summary>Steps where damage control works first; returns the new focus.</summary>
        public static DamageControlFocus CycleFocus(BattleUnit u)
        {
            var cmd = Of(u);
            if (cmd == null) return DamageControlFocus.Balanced;
            cmd.Focus = (DamageControlFocus)(((int)cmd.Focus + 1) % 4);
            return cmd.Focus;
        }

        static bool InFocus(Subsystem s, DamageControlFocus focus)
        {
            switch (focus)
            {
                case DamageControlFocus.Weapons: return s.Def.IsWeapon;
                case DamageControlFocus.Shields: return s.Kind == SubsystemKind.ShieldGenerator;
                case DamageControlFocus.Engines: return s.Kind == SubsystemKind.Engine;
                default: return false;
            }
        }

        /// <summary>
        /// The teams work: each puts back <see cref="RepairHpPerTeamS"/> a second (times <see cref="DamageControlRate"/> and
        /// the fleet's supply readiness) into damaged mounts, those in the focus first and the worst-hurt first among them, up
        /// to <see cref="FieldRepairCap"/> of whole. A mount shot away stays lost until a yard refits it, and the hull itself
        /// is not patched in the field. With the focus on shields, generators keep charging under fire at half their rate.
        /// </summary>
        public static void StepDamageControl(BattleUnit u, double dt)
        {
            var cmd = u.Broadside;
            // Nor with the hull's systems knocked out: the teams are working in the dark.
            if (cmd == null || u.Captured || u.Surrendering || u.Dead || u.Stunned > 0) return;
            double rate = DamageControlRate.Value;
            int teams = Teams(u);
            if (rate <= 0 || teams <= 0) return;
            var c = u.Combat;
            double budget = teams * RepairHpPerTeamS * rate * c.Readiness * dt;
            for (int pass = 0; pass < 8 && budget > 1e-9; pass++)
            {
                Subsystem worst = null;
                double worstScore = double.MaxValue;
                foreach (var s in c.Subsystems)
                {
                    if (s.Destroyed || s.Hp >= s.Def.MaxHp * FieldRepairCap) continue;
                    double score = s.Health - (InFocus(s, cmd.Focus) ? 2 : 0);
                    if (score < worstScore)
                    {
                        worstScore = score;
                        worst = s;
                    }
                }
                if (worst == null) break;
                double fix = Math.Min(budget, worst.Def.MaxHp * FieldRepairCap - worst.Hp);
                worst.Hp += fix;
                cmd.Repaired += fix;
                budget -= fix;
            }
            if (cmd.Focus == DamageControlFocus.Shields)
            {
                double ratings = 0, room = 0;
                foreach (var s in c.Subsystems)
                    if (s.Kind == SubsystemKind.ShieldGenerator && !s.Destroyed)
                    {
                        ratings += s.Def.Rating;
                        room += s.MaxShield - s.Shield;
                    }
                if (ratings <= 0 || u.Stunned > 0) return;
                foreach (var s in c.Subsystems)
                {
                    if (room <= 0) return;
                    if (s.Kind != SubsystemKind.ShieldGenerator || s.Destroyed || s.Shield >= s.ShieldCap) continue;
                    // RegenShields already charges an arc that has not been hit lately: this is for the ones under fire.
                    double add = Math.Min(room, Math.Min(s.ShieldCap - s.Shield, c.Spec.ShieldRegen * s.Def.Rating / ratings * 0.5 * rate * dt));
                    s.Shield += add;
                    room -= add;
                }
            }
        }

        // ------------------------------------------------------------ the step

        /// <summary>
        /// A capital ship's broadside step, run by the battle each step before its shields regenerate: the brace wears off,
        /// an AI captain follows doctrine (beam batteries in volleys, shields angled to its target, a brace when a broadside
        /// is about to come in), the shields settle toward their angling and damage control works.
        /// </summary>
        public static void Step(Battle b, BattleUnit u, double dt)
        {
            var cmd = Of(u);
            if (cmd == null) return;
            cmd.BraceLeft = Math.Max(0, cmd.BraceLeft - dt);
            cmd.BraceCooldown = Math.Max(0, cmd.BraceCooldown - dt);
            if (!u.PlayerControlled && !u.Captured && !u.Surrendering && !u.Combat.Disabled) Doctrine(b, u, cmd);
            StepShields(u, dt);
            StepDamageControl(u, dt);
        }

        /// <summary>What an AI captain orders on its own (each part a setting): its beam batteries fire in volleys and its
        /// bow and stern at will, its shields are angled to the hull it is fighting, and it braces for a broadside about to come in.</summary>
        static void Doctrine(Battle b, BattleUnit u, BroadsideCommand cmd)
        {
            var beam = AiVolleys.Value ? BatteryOrder.Volley : BatteryOrder.FireAtWill;
            cmd[Facing.Bow] = BatteryOrder.FireAtWill;
            cmd[Facing.Stern] = BatteryOrder.FireAtWill;
            cmd[Facing.Starboard] = beam;
            cmd[Facing.Port] = beam;
            var t = u.Target;
            // Toward another capital ship: a small craft is round the hull and gone again before the charge could follow it.
            cmd.ShieldTrim = AiAnglesShields.Value && t != null && t.Present && Commands(t) ? Facings.Toward(u, t.Pos) : (Facing?)null;
            if (AiBraces.Value && !cmd.Braced && cmd.BraceCooldown <= 0 && IncomingBroadside(b, u) != null) Brace(u);
        }

        // ------------------------------------------------------------ formations

        /// <summary>A formation that holds its ships on station (every order but none and engage at will).</summary>
        public static bool IsHeld(FleetFormation f) => f != FleetFormation.None && f != FleetFormation.EngageAtWill;

        /// <summary>Whether <paramref name="u"/> keeps a held formation on its leader.</summary>
        public static bool HoldsFormation(BattleUnit u) =>
            u.Leader != null && u.Leader.Present && !u.Leader.Fleeing && u.Leader.Broadside != null && IsHeld(u.Leader.Broadside.Formation);

        /// <summary>The ships <paramref name="flag"/> can order into formation: its own faction's warships of a destroyer's
        /// size and up on its side, flown by their crews and still fighting, biggest first.</summary>
        public static List<BattleUnit> FormationMembers(Battle b, BattleUnit flag)
        {
            var list = new List<BattleUnit>();
            foreach (var o in b.Units)
            {
                if (o == flag || !o.Present || o.Side != flag.Side || o.Faction != flag.Faction || !Commands(o)) continue;
                if (o.PlayerControlled || o.Captured || o.Surrendering || o.Fleeing || o.Combat.Disabled || o.Latched != null || o.BoardTarget != null) continue;
                if (o.Ship == null || !o.Ship.Spec.IsMilitary) continue;
                list.Add(o);
            }
            list.Sort((a, c) =>
            {
                int h = c.Combat.Spec.Hull.CompareTo(a.Combat.Spec.Hull);
                return h != 0 ? h : a.Id.CompareTo(c.Id);
            });
            return list;
        }

        /// <summary>
        /// The <paramref name="i"/>th of <paramref name="n"/> ships' station in <paramref name="f"/>, km in the flagship's
        /// frame (right, up, forward), <paramref name="spacing"/> km apart.
        /// </summary>
        public static DVec3 SlotFor(FleetFormation f, int i, int n, double spacing)
        {
            int k = i + 1;
            switch (f)
            {
                case FleetFormation.LineAhead: return new DVec3(0, 0, -k * spacing);
                case FleetFormation.LineAbreast:
                    int rank = (i / 2) + 1;
                    return new DVec3((i % 2 == 0 ? 1 : -1) * rank * spacing, 0, 0);
                case FleetFormation.Echelon: return new DVec3(k * spacing * 0.7, 0, -k * spacing * 0.7);
                case FleetFormation.Screen:
                    double a = 2 * Math.PI * i / Math.Max(1, n);
                    double r = spacing * 1.5;
                    return new DVec3(Math.Sin(a) * r, 0, Math.Cos(a) * r);
                default: return DVec3.Zero;
            }
        }

        /// <summary>The distance between ships in a line led by <paramref name="flag"/>, km: half again the longest hull, plus a km.</summary>
        public static double Spacing(BattleUnit flag, List<BattleUnit> members)
        {
            double longest = flag.LengthKm;
            foreach (var m in members) longest = Math.Max(longest, m.LengthKm);
            return longest * 1.5 + 1;
        }

        /// <summary>
        /// <paramref name="flag"/> orders its line into <paramref name="f"/>: every ship it can order (see
        /// <see cref="FormationMembers"/>) takes station on it, or, to engage at will, is cut loose to fight its own fight.
        /// Returns how many ships heard it.
        /// </summary>
        public static int OrderFormation(Battle b, BattleUnit flag, FleetFormation f)
        {
            var cmd = Of(flag);
            if (cmd == null) return 0;
            var members = FormationMembers(b, flag);
            cmd.Formation = f;
            double spacing = Spacing(flag, members);
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (IsHeld(f))
                {
                    m.Leader = flag;
                    m.Slot = SlotFor(f, i, members.Count, spacing);
                }
                else if (m.Leader == flag)
                {
                    m.Leader = null;
                    m.Slot = DVec3.Zero;
                }
            }
            return members.Count;
        }

        public static string Label(FleetFormation f)
        {
            switch (f)
            {
                case FleetFormation.LineAhead: return "line ahead";
                case FleetFormation.LineAbreast: return "line abreast";
                case FleetFormation.Echelon: return "echelon";
                case FleetFormation.Screen: return "screen";
                case FleetFormation.EngageAtWill: return "engage at will";
                default: return "no formation";
            }
        }

        public static string Label(BatteryOrder o) => o == BatteryOrder.HoldFire ? "hold fire" : o == BatteryOrder.Volley ? "volley" : "fire at will";

        public static string Label(DamageControlFocus f) => f == DamageControlFocus.Weapons ? "weapons" : f == DamageControlFocus.Shields ? "shields" : f == DamageControlFocus.Engines ? "engines" : "balanced";

        // ------------------------------------------------------------ turning circles

        /// <summary>The radius of the circle a hull turning at <paramref name="turnDegS"/> traces at <paramref name="speedKmS"/>, km.</summary>
        public static double TurningRadiusKm(double speedKmS, double turnDegS) => turnDegS <= 0 ? double.PositiveInfinity : Math.Abs(speedKmS) / (turnDegS * Astro.Deg2Rad);

        /// <summary>How long a hull turning at <paramref name="turnDegS"/> takes to come about (turn through 180 deg), s.</summary>
        public static double ComeAboutSeconds(double turnDegS) => turnDegS <= 0 ? double.PositiveInfinity : 180 / turnDegS;

        /// <summary>How long <paramref name="u"/>, turning at <paramref name="turnDegS"/>, takes to swing
        /// <paramref name="target"/>'s bearing onto the centre of its <paramref name="f"/> arc, s.</summary>
        public static double SecondsToBear(BattleUnit u, BattleUnit target, Facing f, double turnDegS)
        {
            if (turnDegS <= 0) return double.PositiveInfinity;
            return Facings.OffDeg(u.YawDegOf(target.Pos), f) / turnDegS;
        }
    }
}
