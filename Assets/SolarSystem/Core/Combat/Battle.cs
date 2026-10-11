using System;
using System.Collections.Generic;

namespace SolarSystem.Core
{
    public enum UnitKind
    {
        Ship,
        Platform,
        Fighter,
    }

    /// <summary>One combatant in a battle. Positions are km from the battle's anchor, ecliptic axes.</summary>
    public sealed class BattleUnit
    {
        public int Id;
        public UnitKind Kind;
        public int Side;
        public Faction Faction;
        public Ship Ship;
        public Settlement Platform;
        public ShipCombat Combat;
        public double LengthKm;

        public DVec3 Pos, Vel;
        public DVec3 Forward = DVec3.UnitX;

        public BattleUnit Target;
        public double RetargetIn;
        public bool Fled, Dead;
        public bool Fleeing;

        /// <summary>Formation: the unit this one keeps station on, and where (km, in the leader's frame).</summary>
        public BattleUnit Leader;
        public DVec3 Slot;

        /// <summary>Per-unit randomness: circling direction and a phase for station-keeping wander.</summary>
        public double Wander, Circle;

        /// <summary>Fighter-only state (see the fighter AI).</summary>
        public FighterState Fighter;

        public bool Present => !Dead && !Fled;
        public double Radius => LengthKm * 0.5;
        public string Name => Ship != null ? Ship.Name : Platform != null ? Platform.Name + " defence platform" : "Fighter";

        /// <summary>Right and up axes of the hull (nose along <see cref="Forward"/>, up toward ecliptic north).</summary>
        public void Basis(out DVec3 right, out DVec3 up)
        {
            var upRef = Math.Abs(Forward.Z) > 0.98 ? DVec3.UnitX : DVec3.UnitZ;
            right = DVec3.Cross(Forward, upRef).Normalized;
            up = DVec3.Cross(right, Forward);
        }

        /// <summary>Battle-local position of a hardpoint.</summary>
        public DVec3 PointOf(SubsystemDef d)
        {
            Basis(out var right, out var up);
            return Pos + (right * d.X + up * d.Y + Forward * d.Z) * LengthKm;
        }
    }

    public sealed class Missile
    {
        public DVec3 Pos, Vel;
        public BattleUnit Target;
        public Subsystem TargetSub;
        public double Damage, Speed, Hp, Life;
        public int Side;
        public bool Torpedo;
        public bool Gone;
    }

    public enum FxKind
    {
        Beam,
        BeamMiss,
        PointDefense,
        Guns,
        ShieldHit,
        Impact,
        SubsystemDestroyed,
        MissileKill,
        UnitDestroyed,
        MissileLaunch,
    }

    /// <summary>Something to draw: a beam, a hit, an explosion. A and B are battle-local km.</summary>
    public struct CombatFx
    {
        public int Seq;
        public FxKind Kind;
        public DVec3 A, B;
        public double Size;
        public int Side;
        public BattleUnit Unit;
        public int Sub;
    }

    /// <summary>
    /// A fight at one body. It runs a tactical simulation in a small local space (tens of km) anchored
    /// in orbit: each hull manoeuvres to its weapons' range, every hardpoint picks targets and fires,
    /// lasers and missiles hit particular subsystems on the far hull, point defence shoots missiles and
    /// fighters down, and damaged ships break off and run. Outcomes are real: what dies is gone, what
    /// runs carries its damage home. Seconds are simulated seconds; at high time warp the battle takes
    /// coarser steps rather than holding the clock back.
    /// </summary>
    public sealed class Battle
    {
        public readonly int Id;
        public readonly int Body;
        public readonly string Name;

        /// <summary>Where the fight is: an offset (km, ecliptic) from the body's centre, fixed in its frame.</summary>
        public readonly DVec3 Anchor;

        public readonly double StartJd;
        public double Seconds;
        public readonly List<BattleUnit> Units = new List<BattleUnit>();
        public readonly List<Missile> Missiles = new List<Missile>();
        public readonly List<CombatFx> Fx = new List<CombatFx>();
        public readonly Faction[] SideLeaders = new Faction[2];

        public bool Over { get; private set; }

        /// <summary>True until the battle's first advance: it does not run in the frame it starts, so an observer can slow the clock first.</summary>
        public bool Fresh = true;

        /// <summary>The side that held the field (0 or 1), or -1 if both broke off.</summary>
        public int Winner { get; private set; } = -1;

        public int FxSeq { get; private set; }

        /// <summary>Raised when a unit is destroyed (the world books the loss).</summary>
        public Action<Battle, BattleUnit> UnitDestroyed;

        /// <summary>Fighter AI hook, run each step for every fighter (see FighterAI).</summary>
        public Action<Battle, BattleUnit, double> FighterStep;

        /// <summary>Carriers launch their squadrons through this (see FighterAI).</summary>
        public Action<Battle, BattleUnit> Launch;

        public const double EscapeKm = 55;
        public const double MaxStep = 1.0;
        const int MaxStepsPerAdvance = 500;
        const double MaxSeconds = 4 * 3600;

        readonly Random rng;
        int nextUnitId;
        double accumulator;

        public Battle(int id, int body, string name, DVec3 anchor, double startJd, int seed)
        {
            Id = id;
            Body = body;
            Name = name;
            Anchor = anchor;
            StartJd = startJd;
            rng = new Random(seed);
        }

        public Random Rng => rng;

        // ---------------------------------------------------------------- membership

        public BattleUnit AddShip(Ship ship, int side, DVec3 pos, DVec3 forward)
        {
            var u = NewUnit(UnitKind.Ship, side, ship.Faction, ship.Combat, ship.Spec.LengthKm, pos, forward);
            u.Ship = ship;
            ship.Battle = this;
            if (ship.Combat.Spec.Squadrons > 0) Launch?.Invoke(this, u);
            return u;
        }

        public BattleUnit AddPlatform(Settlement s, ShipCombat platform, int side, DVec3 pos)
        {
            var u = NewUnit(UnitKind.Platform, side, s.Owner, platform, CombatSpec.PlatformLengthKm, pos, DVec3.UnitX);
            u.Platform = s;
            return u;
        }

        public BattleUnit NewUnit(UnitKind kind, int side, Faction faction, ShipCombat combat, double lengthKm, DVec3 pos, DVec3 forward)
        {
            var u = new BattleUnit
            {
                Id = nextUnitId++, Kind = kind, Side = side, Faction = faction, Combat = combat, LengthKm = lengthKm,
                Pos = pos, Forward = forward.LengthSquared > 0 ? forward.Normalized : DVec3.UnitX,
                RetargetIn = rng.NextDouble() * 2, Wander = rng.NextDouble() * 100, Circle = rng.Next(2) == 0 ? -1 : 1,
            };
            if (side >= 0 && side < 2 && SideLeaders[side] == null) SideLeaders[side] = faction;
            Units.Add(u);
            return u;
        }

        public BattleUnit UnitOf(Ship ship)
        {
            foreach (var u in Units)
                if (u.Ship == ship) return u;
            return null;
        }

        public bool Hostile(BattleUnit a, BattleUnit b) => a.Side != b.Side && a.Present && b.Present;

        // ---------------------------------------------------------------- stepping

        public void Advance(double seconds)
        {
            if (Over || seconds <= 0) return;
            accumulator += seconds;
            int steps = (int)Math.Ceiling(accumulator / MaxStep - 1e-9);
            double dt = MaxStep;
            if (steps > MaxStepsPerAdvance)
            {
                // Far behind (a year per second): take coarser steps, up to 20 s; drop the rest.
                dt = Math.Min(20, accumulator / MaxStepsPerAdvance);
                steps = MaxStepsPerAdvance;
            }
            for (int i = 0; i < steps && !Over; i++)
            {
                double step = Math.Min(dt, accumulator);
                if (step <= 1e-9) break;
                Step(step);
                accumulator -= step;
            }
            if (accumulator > dt * 2) accumulator = 0;
        }

        void Step(double dt)
        {
            Seconds += dt;
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (!u.Present) continue;
                if (u.Kind == UnitKind.Fighter)
                {
                    FighterStep?.Invoke(this, u, dt);
                    continue;
                }
                Think(u, dt);
                Move(u, dt);
            }
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (u.Present) Fire(u, dt);
            }
            StepMissiles(dt);
            foreach (var u in Units)
            {
                if (!u.Present) continue;
                var c = u.Combat;
                if (Seconds - c.LastHitSeconds > 8 && c.HasShieldGenerator)
                    c.Shield = Math.Min(c.Spec.Shield, c.Shield + c.Spec.ShieldRegen * dt);
                if (u.Kind == UnitKind.Ship && (u.Pos.Length > EscapeKm || (u.Fleeing && NearestHostileDistance(u) > 30)))
                    u.Fled = true;
            }
            if (Fx.Count > 600) Fx.RemoveRange(0, Fx.Count - 600);
            CheckOver();
        }

        /// <summary>
        /// Over when one side has nobody left on the field (dead or got away), when neither side has a
        /// gun it is willing to use, or after four hours. Those running are still fair game until they
        /// get clear, so a rout is a pursuit, not an armistice.
        /// </summary>
        void CheckOver()
        {
            bool any0 = false, any1 = false, fight0 = false, fight1 = false;
            foreach (var u in Units)
            {
                if (!u.Present || u.Kind == UnitKind.Fighter) continue;
                bool willing = !u.Fleeing && u.Combat.Armed;
                if (u.Side == 0) { any0 = true; fight0 |= willing; }
                else { any1 = true; fight1 |= willing; }
            }
            if (any0 && any1 && (fight0 || fight1) && Seconds < MaxSeconds) return;
            Over = true;
            Winner = fight0 && !fight1 ? 0 : fight1 && !fight0 ? 1 : any0 && !any1 ? 0 : any1 && !any0 ? 1 : -1;
        }

        double NearestHostileDistance(BattleUnit u)
        {
            double best = double.MaxValue;
            foreach (var o in Units)
                if (Hostile(u, o) && o.Kind != UnitKind.Fighter && o.Combat.Armed)
                    best = Math.Min(best, (o.Pos - u.Pos).Length);
            return best;
        }

        // ---------------------------------------------------------------- decisions

        void Think(BattleUnit u, double dt)
        {
            var c = u.Combat;
            if (u.Kind == UnitKind.Ship)
            {
                bool merchant = u.Ship != null && !u.Ship.Spec.IsMilitary && !u.Faction.IsPirate;
                if (!u.Fleeing && (merchant || c.HullFraction < 0.25 || !c.Armed || Routed(u.Side)))
                    u.Fleeing = true;
            }
            u.RetargetIn -= dt;
            if (u.Target == null || !u.Target.Present || u.RetargetIn <= 0)
            {
                u.Target = PickTarget(u);
                u.RetargetIn = (c.BridgeIntact ? 3 : 7) + rng.NextDouble() * 3;
            }
        }

        bool Routed(int side)
        {
            if (Seconds < 120) return false;
            double mine = 0, theirs = 0;
            foreach (var u in Units)
            {
                if (!u.Present) continue;
                double s = u.Combat.Strength;
                if (u.Side == side) mine += s;
                else theirs += s;
            }
            return theirs > 0 && mine < theirs * 0.22;
        }

        public BattleUnit PickTarget(BattleUnit u)
        {
            BattleUnit best = null;
            double bestScore = double.MaxValue;
            BattleUnit flagship = SideFlagship(u.Side);
            foreach (var o in Units)
            {
                if (!Hostile(u, o)) continue;
                double score = (o.Pos - u.Pos).Length;
                if (o.Kind == UnitKind.Fighter) score += u.Kind == UnitKind.Fighter ? 0 : 25;
                else if (u.Kind == UnitKind.Fighter) score -= 4;
                if (o.Ship != null && !o.Ship.Spec.IsMilitary) score += u.Faction.IsPirate ? -15 : 10;
                if (o.Kind == UnitKind.Platform) score += 6;
                if (flagship != null && flagship != u && flagship.Target == o) score -= 6;
                score *= 0.85 + rng.NextDouble() * 0.3;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = o;
                }
            }
            return best;
        }

        BattleUnit SideFlagship(int side)
        {
            BattleUnit best = null;
            foreach (var u in Units)
                if (u.Present && u.Side == side && u.Kind != UnitKind.Fighter && (best == null || u.Combat.Spec.Hull > best.Combat.Spec.Hull))
                    best = u;
            return best;
        }

        void Move(BattleUnit u, double dt)
        {
            var c = u.Combat;
            double maxSpeed = c.Spec.MaxSpeed * (0.25 + 0.75 * c.EngineFactor);
            if (maxSpeed <= 0)
            {
                u.Vel = DVec3.Zero;
                if (u.Target != null) TurnToward(u, u.Target.Pos - u.Pos, 3, dt);
                return;
            }

            DVec3 desired;
            if (u.Fleeing)
            {
                var away = u.Pos - HostileCentroid(u);
                if (away.LengthSquared < 1e-6) away = u.Pos.LengthSquared > 0 ? u.Pos : DVec3.UnitX;
                desired = away.Normalized * maxSpeed;
            }
            else if (u.Target != null)
            {
                var t = u.Target;
                var d = u.Pos - t.Pos;
                double dist = Math.Max(d.Length, 1e-6);
                double range = PreferredRange(u);
                var dir = d / dist;
                var spot = t.Pos + dir * range;
                var around = DVec3.Cross(DVec3.UnitZ, dir);
                if (around.LengthSquared < 1e-6) around = DVec3.UnitY;
                desired = (spot - u.Pos) * 0.02 + around.Normalized * (maxSpeed * 0.35 * u.Circle);
                if (u.Leader != null && u.Leader.Present && !u.Leader.Fleeing)
                {
                    // Escorts hold loose station on their flagship while it fights.
                    var station = StationOf(u);
                    desired = desired * 0.45 + (station - u.Pos) * 0.03 * 0.55 + u.Leader.Vel * 0.55;
                }
            }
            else
            {
                desired = -u.Pos * 0.01;
            }
            if (desired.Length > maxSpeed) desired = desired.Normalized * maxSpeed;

            double accel = c.Spec.Accel * (0.3 + 0.7 * c.EngineFactor);
            var dv = desired - u.Vel;
            double step = accel * dt;
            u.Vel += dv.Length > step ? dv.Normalized * step : dv;
            u.Pos += u.Vel * dt;

            var face = u.Fleeing || u.Target == null ? u.Vel : u.Target.Pos - u.Pos;
            TurnToward(u, face, c.Spec.TurnRateDegS, dt);
        }

        /// <summary>A unit's station on its leader: slot in the leader's frame plus a slow human wander.</summary>
        public DVec3 StationOf(BattleUnit u)
        {
            var l = u.Leader;
            l.Basis(out var right, out var up);
            double t = Seconds * 0.05 + u.Wander;
            var wander = new DVec3(Math.Sin(t * 1.3), Math.Sin(t * 0.9 + 1.7), Math.Sin(t * 0.7 + 4.1)) * 0.35;
            var s = u.Slot + wander;
            return l.Pos + right * s.X + up * s.Y + l.Forward * s.Z;
        }

        static void TurnToward(BattleUnit u, DVec3 dir, double degPerSec, double dt)
        {
            if (dir.LengthSquared < 1e-12) return;
            var want = dir.Normalized;
            double cos = Math.Max(-1, Math.Min(1, DVec3.Dot(u.Forward, want)));
            double angle = Math.Acos(cos);
            double max = degPerSec * Astro.Deg2Rad * dt;
            if (angle <= max || angle < 1e-6)
            {
                u.Forward = want;
                return;
            }
            double t = max / angle;
            u.Forward = DVec3.Lerp(u.Forward, want, t).Normalized;
        }

        static double PreferredRange(BattleUnit u)
        {
            double best = 0;
            foreach (var s in u.Combat.Subsystems)
            {
                var w = s.Def.Weapon;
                if (w == null || s.Destroyed || w.AntiSmall) continue;
                if (w.Missile && s.Ammo <= 0) continue;
                best = Math.Max(best, w.Missile ? w.RangeKm * 0.5 : w.RangeKm * 0.7);
            }
            return best > 0 ? best : 12;
        }

        DVec3 HostileCentroid(BattleUnit u)
        {
            var sum = DVec3.Zero;
            int n = 0;
            foreach (var o in Units)
            {
                if (!Hostile(u, o)) continue;
                sum += o.Pos;
                n++;
            }
            return n > 0 ? sum / n : DVec3.Zero;
        }

        // ---------------------------------------------------------------- weapons

        void Fire(BattleUnit u, double dt)
        {
            var c = u.Combat;
            double accuracyScale = c.BridgeIntact ? 1 : 0.6;
            foreach (var s in c.Subsystems)
            {
                var w = s.Def.Weapon;
                if (w == null || s.Destroyed) continue;
                s.Cooldown -= dt;
                if (s.Cooldown > 0 || s.Kind == SubsystemKind.Torpedo) continue;
                var origin = u.PointOf(s.Def);

                if (w.AntiSmall)
                {
                    var missile = NearestMissile(u.Side, origin, w.RangeKm);
                    if (missile != null)
                    {
                        s.Aim = (missile.Pos - origin).Normalized;
                        s.Cooldown = w.Cooldown * (0.85 + rng.NextDouble() * 0.3);
                        if (rng.NextDouble() < w.Accuracy * accuracyScale)
                        {
                            missile.Hp -= w.Damage;
                            if (missile.Hp <= 0)
                            {
                                missile.Gone = true;
                                Emit(FxKind.MissileKill, missile.Pos, missile.Pos, 0.08, missile.Side, null, -1);
                            }
                        }
                        Emit(FxKind.PointDefense, origin, missile.Pos, 0, u.Side, u, s.Index);
                        continue;
                    }
                }

                var target = ChooseWeaponTarget(u, origin, w);
                // Fighter guns are fixed forward: only fire with the target in a narrow cone.
                if (target != null && s.Kind == SubsystemKind.FighterGuns && DVec3.Dot(u.Forward, (target.Pos - origin).Normalized) < 0.97) target = null;
                if (target == null)
                {
                    s.Aim = DVec3.Zero;
                    continue;
                }
                var sub = PickSubsystem(target);
                var aimPoint = sub != null ? target.PointOf(sub.Def) : target.Pos;
                s.Aim = (aimPoint - origin).Normalized;
                s.Cooldown = w.Cooldown * (0.85 + rng.NextDouble() * 0.3);

                if (w.Missile)
                {
                    if (s.Ammo <= 0) continue;
                    for (int k = 0; k < w.Salvo && s.Ammo > 0; k++)
                    {
                        s.Ammo--;
                        var spread = new DVec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5) * 0.4;
                        Missiles.Add(new Missile
                        {
                            Pos = origin, Vel = (u.Forward + spread + s.Aim).Normalized * (w.MissileSpeedKmS * 0.3) + u.Vel,
                            Target = target, TargetSub = sub, Damage = w.Damage, Speed = w.MissileSpeedKmS,
                            Hp = s.Kind == SubsystemKind.Torpedo ? 25 : 12, Life = 90, Side = u.Side, Torpedo = s.Kind == SubsystemKind.Torpedo,
                        });
                    }
                    Emit(FxKind.MissileLaunch, origin, origin + s.Aim, 0, u.Side, u, s.Index);
                    continue;
                }

                double dist = (aimPoint - origin).Length;
                double size = Math.Max(0.15, Math.Min(1, target.LengthKm / 0.4));
                if (target.Kind == UnitKind.Fighter) size *= w.AntiSmall ? 1.4 : 0.35;
                double chance = w.Accuracy * accuracyScale * size * (1 - 0.4 * dist / w.RangeKm);
                bool hit = rng.NextDouble() < chance;
                var kind = s.Kind == SubsystemKind.FighterGuns ? FxKind.Guns : w.AntiSmall ? FxKind.PointDefense : hit ? FxKind.Beam : FxKind.BeamMiss;
                if (hit)
                {
                    Emit(kind, origin, aimPoint, w.Damage, u.Side, target, sub?.Index ?? -1);
                    ApplyDamage(target, sub, w.Damage, aimPoint);
                }
                else
                {
                    var miss = aimPoint + new DVec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5) * (target.LengthKm * 2 + 0.3);
                    Emit(kind, origin, origin + (miss - origin).Normalized * Math.Min(w.RangeKm, dist * 1.3), 0, u.Side, null, -1);
                }
            }
        }

        BattleUnit ChooseWeaponTarget(BattleUnit u, DVec3 origin, WeaponSpec w)
        {
            if (w.AntiSmall)
            {
                // No missile in reach: point defence turns on fighters, then anything close.
                BattleUnit near = null;
                double best = w.RangeKm;
                foreach (var o in Units)
                {
                    if (!Hostile(u, o)) continue;
                    double d = (o.Pos - origin).Length - o.Radius;
                    if (d < best && (o.Kind == UnitKind.Fighter || near == null || near.Kind != UnitKind.Fighter))
                    {
                        best = d;
                        near = o;
                    }
                }
                return near;
            }
            var t = u.Target;
            if (t != null && t.Present && (t.Pos - origin).Length - t.Radius <= w.RangeKm && !(t.Kind == UnitKind.Fighter && w.Missile))
                return t;
            BattleUnit any = null;
            double nearest = w.RangeKm;
            foreach (var o in Units)
            {
                if (!Hostile(u, o) || (o.Kind == UnitKind.Fighter && (w.Missile || u.Kind != UnitKind.Fighter))) continue;
                double d = (o.Pos - origin).Length - o.Radius;
                if (d < nearest)
                {
                    nearest = d;
                    any = o;
                }
            }
            return any;
        }

        Missile NearestMissile(int side, DVec3 origin, double range)
        {
            Missile best = null;
            double bestD = range;
            foreach (var m in Missiles)
            {
                if (m.Gone || m.Side == side) continue;
                double d = (m.Pos - origin).Length;
                if (d < bestD)
                {
                    bestD = d;
                    best = m;
                }
            }
            return best;
        }

        /// <summary>Gunners go for weapons first, then shields, engines and the bridge; the reactor is a lucky shot.</summary>
        public Subsystem PickSubsystem(BattleUnit target)
        {
            if (target.Kind == UnitKind.Fighter) return null;
            double total = 0;
            var subs = target.Combat.Subsystems;
            var weights = new double[subs.Count];
            for (int i = 0; i < subs.Count; i++)
            {
                var s = subs[i];
                if (s.Destroyed) continue;
                double w;
                switch (s.Kind)
                {
                    case SubsystemKind.HeavyLaser:
                    case SubsystemKind.LaserTurret:
                    case SubsystemKind.MissileLauncher: w = 3; break;
                    case SubsystemKind.ShieldGenerator: w = target.Combat.Shield > 0 ? 2.5 : 0.5; break;
                    case SubsystemKind.Engine: w = 1.4; break;
                    case SubsystemKind.Hangar: w = target.Combat.Fighters > 0 ? 1.8 : 0.4; break;
                    case SubsystemKind.Bridge: w = 1.1; break;
                    case SubsystemKind.PointDefense: w = 0.8; break;
                    case SubsystemKind.Reactor: w = 0.25; break;
                    default: w = 0.5; break;
                }
                weights[i] = w;
                total += w;
            }
            if (total <= 0) return null;
            double pick = rng.NextDouble() * total;
            for (int i = 0; i < subs.Count; i++)
            {
                pick -= weights[i];
                if (weights[i] > 0 && pick <= 0) return subs[i];
            }
            return null;
        }

        /// <summary>
        /// Shields soak damage first; armour blunts each hit (small guns most); what gets through is
        /// split between the struck hardpoint and the hull. A lost reactor takes the ship with it.
        /// </summary>
        public void ApplyDamage(BattleUnit target, Subsystem sub, double damage, DVec3 at)
        {
            if (!target.Present) return;
            var c = target.Combat;
            c.LastHitSeconds = Seconds;
            if (c.Shield > 0 && c.HasShieldGenerator)
            {
                double soaked = Math.Min(c.Shield, damage);
                c.Shield -= soaked;
                damage -= soaked;
                Emit(FxKind.ShieldHit, at, target.Pos, target.LengthKm, target.Side, target, -1);
                if (damage <= 0) return;
            }
            damage = Math.Max(damage * 0.15, damage - c.Spec.Armor);
            Emit(FxKind.Impact, at, at, damage, target.Side, target, sub?.Index ?? -1);
            if (sub != null && !sub.Destroyed)
            {
                sub.Hp -= damage * 0.65;
                c.Hull -= damage * 0.35;
                if (sub.Hp <= 0)
                {
                    c.Hull -= -sub.Hp;
                    sub.Hp = 0;
                    Emit(FxKind.SubsystemDestroyed, at, at, target.LengthKm * 0.12, target.Side, target, sub.Index);
                    if (sub.Kind == SubsystemKind.Reactor) c.Hull = 0;
                    if (sub.Kind == SubsystemKind.ShieldGenerator) c.Shield = 0;
                    if (sub.Kind == SubsystemKind.Hangar) c.Fighters = Math.Max(0, c.Fighters - 6);
                }
            }
            else c.Hull -= damage;

            if (c.Hull <= 0)
            {
                c.Hull = 0;
                target.Dead = true;
                Emit(FxKind.UnitDestroyed, target.Pos, target.Pos, target.LengthKm, target.Side, target, -1);
                UnitDestroyed?.Invoke(this, target);
            }
        }

        void StepMissiles(double dt)
        {
            foreach (var m in Missiles)
            {
                if (m.Gone) continue;
                m.Life -= dt;
                if (m.Life <= 0 || m.Target == null || !m.Target.Present)
                {
                    m.Gone = true;
                    Emit(FxKind.MissileKill, m.Pos, m.Pos, 0.05, m.Side, null, -1);
                    continue;
                }
                var t = m.Target;
                var aim = m.TargetSub != null && !m.TargetSub.Destroyed ? t.PointOf(m.TargetSub.Def) : t.Pos;
                var to = aim - m.Pos;
                double dist = to.Length;
                // Lead the target a little and close at full speed.
                var lead = aim + t.Vel * Math.Min(dist / Math.Max(m.Speed, 1e-6), 20);
                var want = (lead - m.Pos).Normalized * m.Speed;
                var dv = want - m.Vel;
                double turn = m.Speed * 0.6 * dt;
                m.Vel += dv.Length > turn ? dv.Normalized * turn : dv;
                var start = m.Pos;
                m.Pos += m.Vel * dt;
                // Swept test, so a coarse step cannot pass through the target.
                if (SegmentPointDistance(start, m.Pos, aim) < t.Radius * 0.6 + 0.05)
                {
                    m.Gone = true;
                    ApplyDamage(t, m.TargetSub != null && !m.TargetSub.Destroyed ? m.TargetSub : PickSubsystem(t), m.Damage, aim);
                }
            }
            Missiles.RemoveAll(m => m.Gone);
        }

        static double SegmentPointDistance(DVec3 a, DVec3 b, DVec3 p)
        {
            var d = b - a;
            double len2 = d.LengthSquared;
            double t = len2 > 0 ? Math.Max(0, Math.Min(1, DVec3.Dot(p - a, d) / len2)) : 0;
            return (a + d * t - p).Length;
        }

        public void Emit(FxKind kind, DVec3 a, DVec3 b, double size, int side, BattleUnit unit, int sub)
        {
            Fx.Add(new CombatFx { Seq = ++FxSeq, Kind = kind, A = a, B = b, Size = size, Side = side, Unit = unit, Sub = sub });
        }

        // ---------------------------------------------------------------- placement

        /// <summary>
        /// How far out from <paramref name="body"/> a battle is fought: high enough that its tens of km,
        /// drawn at ship scale in the gameplay view, stay well clear of the inflated world.
        /// </summary>
        public static double AnchorDistanceKm(BodyDefinition body)
        {
            double displayRadius = ScaleModel.GameplayRadius(body.RadiusKm);
            double wanted = displayRadius + 900;
            double lo = body.RadiusKm, hi = body.RadiusKm * 400;
            for (int i = 0; i < 60; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (ScaleModel.GameplayLocalDistance(body, mid) < wanted) lo = mid;
                else hi = mid;
            }
            return Math.Max(hi, body.RadiusKm * 6);
        }

        /// <summary>Battle-local offset to true km from the body; the anchor plus the local position.</summary>
        public DVec3 OffsetFromBody(DVec3 local) => Anchor + local;
    }
}
