using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>A hardpoint's live state: health, reload, what it is shooting at and where it points.</summary>
    public sealed class Subsystem
    {
        public readonly SubsystemDef Def;
        public readonly int Index;
        public double Hp;
        public double Cooldown;
        public int Ammo;

        /// <summary>This generator's own shield charge and its cap (an even share of the hull's total),
        /// meaningful only for <see cref="SubsystemKind.ShieldGenerator"/>; set by <see cref="ShipCombat"/>.</summary>
        public double Shield, MaxShield;

        /// <summary>Battle-clock seconds this generator's arc was last hit, for its own regen pause.</summary>
        public double LastHitSeconds = -999;

        /// <summary>World direction (ecliptic) the mount is aimed, for turret rendering; zero when idle.</summary>
        public DVec3 Aim;

        public Subsystem(SubsystemDef def, int index)
        {
            Def = def;
            Index = index;
            Hp = def.MaxHp;
            Ammo = def.Weapon?.Ammo ?? 0;
        }

        public SubsystemKind Kind => Def.Kind;
        public bool Destroyed => Hp <= 0;
        public double Health => Math.Max(0, Hp / Def.MaxHp);

        /// <summary>
        /// This generator's arc's live charge as a fraction of its cap, 0 to 1: what its shield ring wedge and the
        /// bubble a hit raises on its arc are shown at (U.SPC-T40). Zero for a collapsed or shot-away generator,
        /// whose arc then shows no bubble, and for anything that is not a shield generator.
        /// </summary>
        public double ShieldFraction => Kind == SubsystemKind.ShieldGenerator && !Destroyed && MaxShield > 0 && Shield > 0
            ? Math.Min(1, Shield / MaxShield) : 0;
    }

    /// <summary>
    /// The damage a hull carries between battles: hull points, shields and every hardpoint. Repairs
    /// at a friendly yard restore it, paid for in alloys and electronics from the yard's market.
    /// </summary>
    public sealed class ShipCombat
    {
        public readonly CombatSpec Spec;
        public readonly List<Subsystem> Subsystems = new List<Subsystem>();
        public double Hull;

        /// <summary>Fighters left aboard (carriers); losses are made good at a yard.</summary>
        public int Fighters;

        /// <summary>Boost charge, 0-1: drains over <see cref="CombatSpec.BoostDurationS"/> while held and
        /// recharges (twice as slowly) otherwise. Shared by AI and player flight so boost behaves the same
        /// whoever is flying.</summary>
        public double BoostFraction = 1;

        /// <summary>The inside of the hull: compartments, who is in them, and any boarding fight.</summary>
        public readonly Interior Interior;

        /// <summary>
        /// How fit to fight the hull's war fleet is for what it has left of its propellant, munitions and spares (U.SPC-T93),
        /// 1 in supply down to <see cref="FleetSupply.ReadinessFloor"/> with nothing left: its guns reload that much slower in
        /// battle, its crew strike their colours sooner, and it mends itself in the field that much slower. Set each day by
        /// <see cref="MilitaryLogistics"/> for a fleet on campaign; 1 again at any port of its own.
        /// </summary>
        public double Readiness = 1;

        /// <param name="hull">The ship class, or null for a defence platform.</param>
        public ShipCombat(CombatSpec spec, ShipClass? hull = null) : this(spec, hull, null) { }

        ShipCombat(CombatSpec spec, ShipClass? hull, Interior interior)
        {
            Spec = spec;
            Hull = spec.Hull;
            Fighters = spec.Squadrons * 6;
            for (int i = 0; i < spec.Layout.Length; i++) Subsystems.Add(new Subsystem(spec.Layout[i], i));
            double ratings = GeneratorRatings;
            if (ratings > 0)
                foreach (var s in Subsystems)
                    if (s.Kind == SubsystemKind.ShieldGenerator) s.MaxShield = s.Shield = spec.Shield * s.Def.Rating / ratings;
            Interior = interior ?? (hull.HasValue ? Interior.For(hull.Value, spec.Crew, spec.Marines) : Interior.ForPlatform(spec.Crew, spec.Marines));
        }

        /// <summary>
        /// The same hull after a yard refit (U.SPC-T65) to <paramref name="spec"/>, whose hardpoint list lines up
        /// with the old one slot for slot: its people (the same <see cref="Interior"/>), the share of its hull
        /// still standing, its fighters and boost carry over, and so does every hardpoint but
        /// <paramref name="changed"/> (its health, rounds and its share of shield charge); the part fitted at
        /// <paramref name="changed"/> is new, whole and loaded.
        /// </summary>
        public ShipCombat Refitted(CombatSpec spec, ShipClass hull, int changed)
        {
            var c = new ShipCombat(spec, hull, Interior)
            {
                Hull = spec.Hull * HullFraction,
                Fighters = Math.Min(spec.Squadrons * 6, Fighters),
                BoostFraction = BoostFraction,
                Readiness = Readiness,
            };
            for (int i = 0; i < c.Subsystems.Count && i < Subsystems.Count; i++)
            {
                if (i == changed) continue;
                var was = Subsystems[i];
                var now = c.Subsystems[i];
                if (was.Kind != now.Kind) continue;
                now.Hp = now.Def.MaxHp * (was.Hp <= 0 ? 0 : was.Health);
                now.Ammo = Math.Min(now.Ammo, was.Ammo);
                now.Cooldown = was.Cooldown;
                now.LastHitSeconds = was.LastHitSeconds;
                if (now.Kind == SubsystemKind.ShieldGenerator) now.Shield = was.MaxShield > 0 ? now.MaxShield * Math.Min(1, was.Shield / was.MaxShield) : 0;
            }
            return c;
        }

        /// <summary>The hull's shield generators' grades summed: each holds and regenerates its rating's share.</summary>
        double GeneratorRatings
        {
            get
            {
                double total = 0;
                foreach (var s in Subsystems) if (s.Kind == SubsystemKind.ShieldGenerator) total += s.Def.Rating;
                return total;
            }
        }

        /// <summary>
        /// Crippled but not destroyed: no working engines, a hull nearly gone, or a lost bridge on a
        /// badly holed ship. A disabled ship drifts and can be boarded.
        /// </summary>
        public bool Disabled => Alive && (EngineFactor <= 0 || HullFraction < 0.12 || (!BridgeIntact && HullFraction < 0.4));

        /// <summary>Marines aboard and free to go boarding (those not already fighting off boarders).</summary>
        public double MarinesAvailable => Interior.UnderBoarding ? 0 : Interior.Marines;

        public double HullFraction => Math.Max(0, Hull / Spec.Hull);
        public bool Alive => Hull > 0;

        public bool HasShieldGenerator
        {
            get
            {
                foreach (var s in Subsystems)
                    if (s.Kind == SubsystemKind.ShieldGenerator && !s.Destroyed) return true;
                return false;
            }
        }

        /// <summary>Total shield charge across every generator — a coarse summary (HUD display, AI
        /// heuristics); combat itself should hit a specific generator via <see cref="GeneratorFor"/>.</summary>
        public double Shield
        {
            get
            {
                double total = 0;
                foreach (var s in Subsystems) if (s.Kind == SubsystemKind.ShieldGenerator) total += s.Shield;
                return total;
            }
        }

        /// <summary>The live shield generator covering a yaw angle (degrees, 0 = bow), or null if none does.</summary>
        public Subsystem GeneratorFor(double yawDeg)
        {
            foreach (var s in Subsystems)
                if (s.Kind == SubsystemKind.ShieldGenerator && !s.Destroyed && s.Def.ContainsYaw(yawDeg)) return s;
            return null;
        }

        public void ZeroAllShields()
        {
            foreach (var s in Subsystems) if (s.Kind == SubsystemKind.ShieldGenerator) s.Shield = 0;
        }

        /// <summary>Resets every generator's regen-pause timer (a ship entering a fresh battle starts able to regen).</summary>
        public void ResetShieldTimers()
        {
            foreach (var s in Subsystems) if (s.Kind == SubsystemKind.ShieldGenerator) s.LastHitSeconds = -999;
        }

        /// <summary>Regens every live generator that has not been hit in the last 8 battle-seconds, each
        /// toward its own cap, sharing the hull's total regen rate across its generators by their ratings (evenly, on a stock hull).</summary>
        public void RegenShields(double nowSeconds, double dt)
        {
            double ratings = GeneratorRatings;
            if (ratings <= 0) return;
            foreach (var s in Subsystems)
            {
                if (s.Kind != SubsystemKind.ShieldGenerator || s.Destroyed) continue;
                if (nowSeconds - s.LastHitSeconds > 8) s.Shield = Math.Min(s.MaxShield, s.Shield + Spec.ShieldRegen * s.Def.Rating / ratings * dt);
            }
        }

        public bool Armed
        {
            get
            {
                // Mines, sentry turrets and EMP rounds are a hull's means of getting away, not of fighting (U.SPC-T271).
                foreach (var s in Subsystems)
                    if (s.Def.IsWeapon && !s.Destroyed && s.Kind != SubsystemKind.PointDefense && (!s.Def.Weapon.Missile || s.Ammo > 0) && !s.Def.Weapon.Supporting) return true;
                return false;
            }
        }

        /// <summary>Share of engines still working (1 if the hull has none).</summary>
        public double EngineFactor
        {
            get
            {
                int total = 0, alive = 0;
                foreach (var s in Subsystems)
                {
                    if (s.Kind != SubsystemKind.Engine) continue;
                    total++;
                    if (!s.Destroyed) alive++;
                }
                return total == 0 ? 1 : (double)alive / total;
            }
        }

        public bool BridgeIntact
        {
            get
            {
                foreach (var s in Subsystems)
                    if (s.Kind == SubsystemKind.Bridge) return !s.Destroyed;
                return true;
            }
        }

        /// <summary>Fraction of everything (hull and hardpoints) that is damaged, 0 = pristine.</summary>
        public double Damage
        {
            get
            {
                double missing = 1 - HullFraction;
                foreach (var s in Subsystems) missing += (1 - s.Health) * 0.1;
                return Math.Min(1, missing / (1 + Subsystems.Count * 0.1));
            }
        }

        /// <summary>Rough fighting strength for strategic planning: weapon damage per second, scaled by health.</summary>
        public double Strength
        {
            get
            {
                if (!Alive) return 0;
                double dps = 0;
                foreach (var s in Subsystems)
                {
                    var w = s.Def.Weapon;
                    if (w == null || s.Destroyed || w.Supporting) continue;
                    dps += w.Damage * w.Salvo / w.Cooldown * (w.AntiSmall ? 0.3 : 1);
                }
                dps += Fighters * 3;
                return dps * (0.3 + 0.7 * HullFraction) * (Spec.Hull + Spec.Shield) / 1000;
            }
        }

        /// <summary>Repairs up to <paramref name="share"/> of the hull and every hardpoint by the same share.
        /// Does not restock ammo or crew: callers that owe a cost for those (<see cref="War"/>'s yard refit)
        /// gate them separately; callers with no such cost (defence platforms) call <see cref="FreeResupply"/> too.</summary>
        public void Repair(double share)
        {
            Hull = Math.Min(Spec.Hull, Hull + Spec.Hull * share);
            foreach (var s in Subsystems)
            {
                s.Hp = Math.Min(s.Def.MaxHp, Math.Max(s.Hp, 0) + s.Def.MaxHp * share * 1.5);
                if (s.Kind == SubsystemKind.ShieldGenerator) s.Shield = s.MaxShield;
            }
        }

        /// <summary>Fully restocks ammo and musters crew/marines back to spec, free. Only for callers (defence
        /// platforms) with no costed resupply logic of their own; ships are resupplied via <see cref="War"/>'s refit.</summary>
        public void FreeResupply()
        {
            foreach (var s in Subsystems)
                if (s.Def.Weapon != null) s.Ammo = s.Def.Weapon.Ammo;
            Interior.Muster(Spec.Crew, Spec.Marines);
        }

        public void RestoreFighters(int count) => Fighters = Math.Min(Spec.Squadrons * 6, Fighters + count);
    }
}
