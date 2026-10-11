using System;
using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>A hardpoint's live state: health, reload, what it is shooting at and where it points.</summary>
    public sealed class Subsystem
    {
        public readonly SubsystemDef Def;
        public readonly int Index;
        public double Hp;
        public double Cooldown;
        public int Ammo;

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
        public double Shield;
        public double LastHitSeconds = -999;

        /// <summary>Fighters left aboard (carriers); losses are made good at a yard.</summary>
        public int Fighters;

        public ShipCombat(CombatSpec spec)
        {
            Spec = spec;
            Hull = spec.Hull;
            Shield = spec.Shield;
            Fighters = spec.Squadrons * 6;
            for (int i = 0; i < spec.Layout.Length; i++) Subsystems.Add(new Subsystem(spec.Layout[i], i));
        }

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

        public bool Armed
        {
            get
            {
                foreach (var s in Subsystems)
                    if (s.Def.IsWeapon && !s.Destroyed && s.Kind != SubsystemKind.PointDefense) return true;
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
                    if (w == null || s.Destroyed) continue;
                    dps += w.Damage * w.Salvo / w.Cooldown * (w.AntiSmall ? 0.3 : 1);
                }
                dps += Fighters * 3;
                return dps * (0.3 + 0.7 * HullFraction) * (Spec.Hull + Spec.Shield) / 1000;
            }
        }

        /// <summary>Repairs up to <paramref name="share"/> of the hull and every hardpoint by the same share; restocks ammo.</summary>
        public void Repair(double share)
        {
            Hull = Math.Min(Spec.Hull, Hull + Spec.Hull * share);
            foreach (var s in Subsystems)
            {
                s.Hp = Math.Min(s.Def.MaxHp, Math.Max(s.Hp, 0) + s.Def.MaxHp * share * 1.5);
                if (s.Def.Weapon != null) s.Ammo = s.Def.Weapon.Ammo;
            }
            Shield = Spec.Shield;
        }

        public void RestoreFighters(int count) => Fighters = Math.Min(Spec.Squadrons * 6, Fighters + count);
    }
}
