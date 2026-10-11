using System;

namespace SolarSystem.Core
{
    public enum FighterPhase
    {
        Launch,
        Approach,
        AttackRun,
        BreakOff,
        Dogfight,
    }

    /// <summary>A fighter's pilot and squadron: who leads, where it flies in the formation, how it flies.</summary>
    public sealed class FighterState
    {
        public BattleUnit Carrier;
        public BattleUnit Lead;
        public int Squadron;
        public int Slot;
        public bool Bomber;
        public FighterPhase Phase;
        public double PhaseTime;

        /// <summary>The pilot: how good a shot, how quick to react (seconds of lag), how tidy in formation (0..1).</summary>
        public double Skill, Reaction, Discipline;

        /// <summary>Where the current run starts and where the break-off heads, battle-local km.</summary>
        public DVec3 RunEntry, BreakPoint;

        public double Seed;
    }

    /// <summary>
    /// Carrier fighters. A carrier launches its squadrons (six craft each: two interceptor squadrons
    /// to one of bombers) as it enters a battle. Each squadron's lead flies attack runs on an enemy
    /// capital ship (approach from the side, dive at it guns firing or torpedo away, pass close, break
    /// off, swing round from a new side) or, for interceptors with enemy fighters about, dogfights.
    /// Wingmen fly a loose vee on the lead: each pilot has their own wander, reaction lag and
    /// discipline, so formations breathe and stretch the way human-flown ones do, and spread out on
    /// the run. A lead that dies is replaced by its senior wingman.
    /// </summary>
    public static class FighterAI
    {
        public const double FighterLengthKm = 0.016, BomberLengthKm = 0.024;

        public static readonly CombatSpec Interceptor = new CombatSpec
        {
            Hull = 70, Shield = 30, ShieldRegen = 2, Armor = 0, MaxSpeed = 0.35, Accel = 0.25, TurnRateDegS = 120,
            Layout = new[]
            {
                new SubsystemDef(SubsystemKind.FighterGuns, 0.2, 0, 0.4),
                new SubsystemDef(SubsystemKind.FighterGuns, -0.2, 0, 0.4),
            },
        };

        public static readonly CombatSpec BomberSpec = new CombatSpec
        {
            Hull = 130, Shield = 50, ShieldRegen = 2, Armor = 1, MaxSpeed = 0.26, Accel = 0.15, TurnRateDegS = 80,
            Layout = new[]
            {
                new SubsystemDef(SubsystemKind.FighterGuns, 0, 0.05, 0.4),
                new SubsystemDef(SubsystemKind.Torpedo, 0, -0.1, 0),
            },
        };

        const int SquadronSize = 6;

        /// <summary>A carrier entering battle puts every fighter it has aboard into space.</summary>
        public static void Launch(Battle b, BattleUnit carrier)
        {
            if (carrier == null || carrier.Combat.Fighters <= 0) return;
            int hangars = 0;
            foreach (var s in carrier.Combat.Subsystems)
                if (s.Kind == SubsystemKind.Hangar && !s.Destroyed) hangars++;
            if (hangars == 0) return;
            var rng = b.Rng;
            int count = carrier.Combat.Fighters;
            carrier.Combat.Fighters = 0;
            int squadrons = (count + SquadronSize - 1) / SquadronSize;
            carrier.Basis(out var right, out var up);
            for (int q = 0; q < squadrons; q++)
            {
                bool bomber = q % 3 == 2;
                BattleUnit lead = null;
                for (int k = 0; k < SquadronSize && count > 0; k++, count--)
                {
                    var spec = bomber ? BomberSpec : Interceptor;
                    // Out of the bow and side bays, fanning ahead of the carrier.
                    var pos = carrier.PointOf(carrier.Combat.Subsystems.Find(s => s.Kind == SubsystemKind.Hangar && !s.Destroyed).Def)
                              + carrier.Forward * (0.3 + 0.15 * k) + right * ((q - 1) * 0.6 + (k % 2 == 0 ? 0.1 : -0.1) * k) + up * (0.05 * k);
                    var u = b.NewUnit(UnitKind.Fighter, carrier.Side, carrier.Faction, new ShipCombat(spec), bomber ? BomberLengthKm : FighterLengthKm, pos, carrier.Forward);
                    u.Vel = carrier.Vel + carrier.Forward * 0.25;
                    u.Fighter = new FighterState
                    {
                        Carrier = carrier, Lead = lead, Squadron = q, Slot = k, Bomber = bomber, Phase = FighterPhase.Launch,
                        Skill = 0.4 + rng.NextDouble() * 0.6, Reaction = 0.4 + rng.NextDouble() * 1.4, Discipline = 0.45 + rng.NextDouble() * 0.5,
                        Seed = rng.NextDouble() * 1000,
                    };
                    if (lead == null) lead = u;
                }
            }
        }

        public static void Step(Battle b, BattleUnit u, double dt)
        {
            var f = u.Fighter;
            f.PhaseTime += dt;
            if (f.Lead != null && !f.Lead.Present) f.Lead = NewLead(b, u);
            if (f.Lead == null) Lead(b, u, dt);
            else Wingman(b, u, dt);
        }

        /// <summary>The senior surviving wingman takes over the squadron.</summary>
        static BattleUnit NewLead(Battle b, BattleUnit u)
        {
            BattleUnit best = null;
            foreach (var o in b.Units)
            {
                if (!o.Present || o.Kind != UnitKind.Fighter || o.Fighter.Squadron != u.Fighter.Squadron || o.Fighter.Carrier != u.Fighter.Carrier) continue;
                if (best == null || o.Fighter.Slot < best.Fighter.Slot) best = o;
            }
            if (best == null || best == u) return null;
            return best;
        }

        // ---------------------------------------------------------------- the lead

        static void Lead(Battle b, BattleUnit u, double dt)
        {
            var f = u.Fighter;
            var rng = b.Rng;
            if (u.Target == null || !u.Target.Present || (f.Phase != FighterPhase.AttackRun && rng.NextDouble() < dt / 20))
            {
                u.Target = ChooseTarget(b, u);
                if (f.Phase == FighterPhase.Dogfight || f.Phase == FighterPhase.AttackRun) f.Phase = FighterPhase.Approach;
            }
            var t = u.Target;
            DVec3 goal;
            double speed = u.Combat.Spec.MaxSpeed;

            switch (f.Phase)
            {
                case FighterPhase.Launch:
                    goal = f.Carrier.Pos + f.Carrier.Forward * 4 + DVec3.UnitZ * 0.6;
                    if (f.PhaseTime > 7) Switch(f, FighterPhase.Approach);
                    break;

                case FighterPhase.Approach:
                    if (t == null)
                    {
                        goal = (f.Carrier.Present ? f.Carrier.Pos : DVec3.Zero) + new DVec3(Math.Cos(b.Seconds * 0.1 + f.Seed), Math.Sin(b.Seconds * 0.1 + f.Seed), 0.2) * 3;
                        speed *= 0.6;
                        break;
                    }
                    if (t.Kind == UnitKind.Fighter)
                    {
                        Switch(f, FighterPhase.Dogfight);
                        goal = t.Pos;
                        break;
                    }
                    if (f.RunEntry.LengthSquared == 0 || f.PhaseTime < dt * 1.5) f.RunEntry = PickEntry(b, u, t);
                    goal = t.Pos + f.RunEntry;
                    if ((u.Pos - goal).Length < 1.0 || f.PhaseTime > 60) Switch(f, FighterPhase.AttackRun);
                    break;

                case FighterPhase.AttackRun:
                {
                    if (t == null)
                    {
                        Switch(f, FighterPhase.Approach);
                        goal = u.Pos + u.Forward;
                        break;
                    }
                    // Dive at the target, aiming to pass a few hundred metres off its hull.
                    var to = t.Pos - u.Pos;
                    double dist = to.Length;
                    var side = DVec3.Cross(to, DVec3.UnitZ);
                    if (side.LengthSquared < 1e-9) side = DVec3.UnitX;
                    goal = t.Pos + side.Normalized * (t.Radius + 0.35) * Math.Sign(Math.Sin(f.Seed)) + t.Vel * (dist / speed);
                    bool past = DVec3.Dot(u.Vel, to) < 0;
                    if ((past && dist > t.Radius + 0.8) || f.PhaseTime > 45)
                    {
                        var away = (u.Pos - t.Pos).Normalized;
                        f.BreakPoint = t.Pos + (away + DVec3.UnitZ * (rng.NextDouble() - 0.3)).Normalized * (7 + rng.NextDouble() * 3);
                        Switch(f, FighterPhase.BreakOff);
                    }
                    break;
                }

                case FighterPhase.BreakOff:
                    goal = f.BreakPoint;
                    if ((u.Pos - goal).Length < 1.2 || f.PhaseTime > 35)
                    {
                        f.RunEntry = DVec3.Zero;
                        Switch(f, FighterPhase.Approach);
                    }
                    break;

                default:
                {
                    // Dogfight: pursue with lead, jinking a little.
                    if (t == null || t.Kind != UnitKind.Fighter)
                    {
                        Switch(f, FighterPhase.Approach);
                        goal = u.Pos + u.Forward;
                        break;
                    }
                    double dist = (t.Pos - u.Pos).Length;
                    var jink = new DVec3(Math.Sin(b.Seconds * 1.7 + f.Seed), Math.Sin(b.Seconds * 1.3 + f.Seed * 2), Math.Sin(b.Seconds * 0.9 + f.Seed * 3)) * 0.15;
                    goal = t.Pos + t.Vel * Math.Min(dist / speed, 3) + jink;
                    break;
                }
            }
            Steer(b, u, goal, speed, dt);
            Torpedo(b, u);
        }

        static BattleUnit ChooseTarget(Battle b, BattleUnit u)
        {
            bool bomber = u.Fighter.Bomber;
            BattleUnit best = null;
            double bestScore = double.MaxValue;
            foreach (var o in b.Units)
            {
                if (!b.Hostile(u, o)) continue;
                if (o.Kind == UnitKind.Fighter && bomber) continue;
                double d = (o.Pos - u.Pos).Length;
                double score = d;
                if (o.Kind == UnitKind.Fighter) score = d < 15 ? d - 6 : d + 10;
                else if (o.Ship != null && !o.Ship.Spec.IsMilitary) score += 8;
                else score -= o.Combat.Spec.Hull / 3000.0;
                score *= 0.8 + b.Rng.NextDouble() * 0.4;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>A run starts 5-7 km off the target, from a side the squadron picks for itself.</summary>
        static DVec3 PickEntry(Battle b, BattleUnit u, BattleUnit t)
        {
            var from = (u.Pos - t.Pos).Normalized;
            var side = DVec3.Cross(from, DVec3.UnitZ);
            if (side.LengthSquared < 1e-9) side = DVec3.UnitX;
            double swing = (b.Rng.NextDouble() - 0.5) * 2.2;
            var dir = (from * Math.Cos(swing) + side.Normalized * Math.Sin(swing) + DVec3.UnitZ * (b.Rng.NextDouble() - 0.4) * 0.6).Normalized;
            return dir * (5 + b.Rng.NextDouble() * 2);
        }

        static void Switch(FighterState f, FighterPhase p)
        {
            f.Phase = p;
            f.PhaseTime = 0;
        }

        static void Torpedo(Battle b, BattleUnit u)
        {
            var f = u.Fighter;
            if (!f.Bomber || f.Phase != FighterPhase.AttackRun || u.Target == null || u.Target.Kind == UnitKind.Fighter) return;
            var to = u.Target.Pos - u.Pos;
            double dist = to.Length;
            if (dist > WeaponSpec.Torpedo.RangeKm || DVec3.Dot(u.Forward, to / dist) < 0.9) return;
            foreach (var s in u.Combat.Subsystems)
            {
                if (s.Kind != SubsystemKind.Torpedo || s.Destroyed || s.Ammo <= 0 || s.Cooldown > 0) continue;
                s.Ammo--;
                s.Cooldown = s.Def.Weapon.Cooldown;
                var sub = b.PickSubsystem(u.Target);
                b.Missiles.Add(new Missile
                {
                    Pos = u.Pos, Vel = u.Vel, Target = u.Target, TargetSub = sub, Damage = s.Def.Weapon.Damage,
                    Speed = s.Def.Weapon.MissileSpeedKmS + u.Vel.Length, Hp = 25, Life = 30, Side = u.Side, Torpedo = true,
                });
                b.Emit(FxKind.MissileLaunch, u.Pos, u.Pos + u.Forward, 0, u.Side, u, s.Index);
            }
        }

        // ---------------------------------------------------------------- wingmen

        /// <summary>V-formation slots behind the lead (km: right, up, forward), alternating sides.</summary>
        static DVec3 SlotOffset(int slot)
        {
            int rank = (slot + 1) / 2;
            double side = slot % 2 == 1 ? 1 : -1;
            return new DVec3(side * 0.16 * rank, 0.03 * rank, -0.14 * rank);
        }

        static void Wingman(Battle b, BattleUnit u, double dt)
        {
            var f = u.Fighter;
            var lead = f.Lead;
            var lf = lead.Fighter;
            u.Target = lead.Target;
            // Station: the slot in the lead's frame, stretched on the run, plus this pilot's own wander.
            lead.Basis(out var right, out var up);
            double spread = lf.Phase == FighterPhase.AttackRun ? 2.6 : lf.Phase == FighterPhase.Dogfight ? 4.0 : 1.0;
            spread *= 1.6 - f.Discipline;
            var slot = SlotOffset(f.Slot) * spread;
            double t = b.Seconds;
            var wander = new DVec3(
                Math.Sin(t * 0.37 + f.Seed) + 0.5 * Math.Sin(t * 1.13 + f.Seed * 1.7),
                Math.Sin(t * 0.29 + f.Seed * 2.3) + 0.5 * Math.Sin(t * 0.91 + f.Seed * 0.4),
                Math.Sin(t * 0.21 + f.Seed * 3.1)) * (0.05 * (1.4 - f.Discipline));
            var station = lead.Pos + right * (slot.X + wander.X) + up * (slot.Y + wander.Y) + lead.Forward * (slot.Z + wander.Z);
            // Reaction lag: they fly to where the lead was heading a moment ago, not where it is now.
            station -= lead.Vel * f.Reaction * 0.08;
            var goal = station + lead.Vel * 0.6;
            Steer(b, u, goal, u.Combat.Spec.MaxSpeed * 1.3, dt, lead.Vel);
            Torpedo(b, u);
        }

        // ---------------------------------------------------------------- flying

        /// <summary>Flies toward a point: the nose turns at the craft's rate and the craft goes where it points.</summary>
        static void Steer(Battle b, BattleUnit u, DVec3 goal, double speed, double dt, DVec3 match = default)
        {
            var spec = u.Combat.Spec;
            var to = goal - u.Pos;
            double dist = to.Length;
            // Stay inside the battle.
            if (u.Pos.Length > Battle.EscapeKm * 0.8) to = -u.Pos;
            var desiredDir = dist > 1e-6 ? to / dist : u.Forward;
            double turn = spec.TurnRateDegS * Astro.Deg2Rad * dt;
            double cos = Math.Max(-1, Math.Min(1, DVec3.Dot(u.Forward, desiredDir)));
            double angle = Math.Acos(cos);
            u.Forward = angle <= turn ? desiredDir : DVec3.Lerp(u.Forward, desiredDir, turn / angle).Normalized;
            // Throttle back when closing on a slot so wingmen do not overshoot.
            double want = speed;
            if (match.LengthSquared > 0) want = Math.Min(speed, match.Length + dist * 1.2);
            var desiredVel = u.Forward * want;
            var dv = desiredVel - u.Vel;
            double step = spec.Accel * dt;
            u.Vel += dv.Length > step ? dv.Normalized * step : dv;
            u.Pos += u.Vel * dt;
        }
    }
}
