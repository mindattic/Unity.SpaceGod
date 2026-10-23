using System;

namespace Unity.SpaceGod.Core
{
    public enum FighterPhase
    {
        Launch,
        Approach,
        AttackRun,
        BreakOff,
        Dogfight,

        /// <summary>Flying home to land on the carrier.</summary>
        Return,
    }

    /// <summary>A fighter's pilot and squadron: who leads, where it flies in the formation, how it flies.</summary>
    public sealed class FighterState
    {
        public BattleUnit Carrier;
        public BattleUnit Lead;
        public int Squadron;
        public int Slot;
        public bool Bomber;

        /// <summary>Its class (U.SPC-T424): what it is built for and how it flies.</summary>
        public FighterRole Role;

        /// <summary>What its squadron's lead has told it to do (U.SPC-T424, see <see cref="Wing"/>).</summary>
        public WingOrder Order;
        public FighterPhase Phase;
        public double PhaseTime;

        /// <summary>The pilot: how good a shot, how quick to react (seconds of lag), how tidy in formation (0..1).</summary>
        public double Skill, Reaction, Discipline;

        /// <summary>Where the current run starts and where the break-off heads, battle-local km.</summary>
        public DVec3 RunEntry, BreakPoint;

        public double Seed;
    }

    /// <summary>The player's stick on a launched craft they have taken (U.SPC-T422, see <see cref="FighterSeat"/>).</summary>
    public sealed class FighterControl
    {
        /// <summary>Where the player wants the nose (unit, ecliptic axes); zero holds the current heading.</summary>
        public DVec3 Aim;

        /// <summary>The share of the craft's top speed it holds along its nose, 0-1.</summary>
        public double Throttle;

        /// <summary>The second trigger, held this step: it fires <see cref="SecondaryGroup"/>.</summary>
        public bool Secondary;

        /// <summary>The guided weapons the second trigger fires (U.SPC-T424, see <see cref="WeaponGroups"/>).</summary>
        public WeaponGroup SecondaryGroup = WeaponGroup.Missiles;

        /// <summary>Strafe and roll (each -1..1), the afterburner, and flight assist switched off (U.SPC-T424).</summary>
        public double StrafeRight, StrafeUp, Roll;
        public bool Boost, FlightAssistOff;

        /// <summary>The craft's roof and spin, carried from step to step by its flight model (U.SPC-T424).</summary>
        public DVec3 Up, AngularVelocity;
    }

    /// <summary>
    /// Carrier fighters. A carrier launches its squadrons (six craft each, of the classes in
    /// <see cref="FighterCraft.SquadronMix"/>: interceptors, heavy fighters, bombers) as it enters a battle. Each
    /// squadron's lead flies attack runs on an enemy capital ship (approach from the side, or for bombers from where its
    /// weak points lie open, dive at it guns firing or torpedo away, pass close, break off, swing round from a new side)
    /// or, for fighters with enemy fighters about, dogfights. Wingmen fly a loose vee on the lead: each pilot has their
    /// own wander, reaction lag and discipline, so formations breathe and stretch the way human-flown ones do, and spread
    /// out on the run; told to (<see cref="Wing"/>), they break off and fight on their own. A lead that dies is replaced
    /// by its senior wingman. Pilots share their reactor as the fight needs (<see cref="PowerDistribution"/>) and throw
    /// flares at missiles closing on them.
    /// </summary>
    public static class FighterAI
    {
        public const double FighterLengthKm = 0.016, BomberLengthKm = 0.024;

        public static readonly CombatSpec Interceptor = FighterCraft.Interceptor.Spec;

        public static readonly CombatSpec BomberSpec = FighterCraft.Bomber.Spec;

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
                var craft = FighterCraft.Of(FighterCraft.RoleOfSquadron(q));
                bool bomber = craft.Role == FighterRole.Bomber;
                BattleUnit lead = null;
                for (int k = 0; k < SquadronSize && count > 0; k++, count--)
                {
                    // Out of the bow and side bays, fanning ahead of the carrier.
                    var pos = carrier.PointOf(carrier.Combat.Subsystems.Find(s => s.Kind == SubsystemKind.Hangar && !s.Destroyed).Def)
                              + carrier.Forward * (0.3 + 0.15 * k) + right * ((q - 1) * 0.6 + (k % 2 == 0 ? 0.1 : -0.1) * k) + up * (0.05 * k);
                    var u = b.NewUnit(UnitKind.Fighter, carrier.Side, carrier.Faction, new ShipCombat(craft.Spec, ShipClass.Courier), craft.LengthKm, pos, carrier.Forward);
                    u.Vel = carrier.Vel + carrier.Forward * 0.25;
                    u.Power = new PowerDistribution();
                    u.Fighter = new FighterState
                    {
                        Carrier = carrier, Lead = lead, Squadron = q, Slot = k, Bomber = bomber, Role = craft.Role, Phase = FighterPhase.Launch,
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
            // Its drives knocked out (an EMP): it drifts on the way it was going until they come back.
            if (u.Stunned > 0)
            {
                u.Pos += u.Vel * dt;
                return;
            }
            f.PhaseTime += dt;
            var carrier = f.Carrier;
            // Recalled: the shooting is over, or the carrier is pulling out.
            if (carrier.Present && !carrier.Captured && (b.Mopping || carrier.Fleeing))
            {
                if (f.Phase != FighterPhase.Return) Switch(f, FighterPhase.Return);
                Land(b, u, dt);
                return;
            }
            if (f.Lead != null && !f.Lead.Present) f.Lead = NewLead(b, u);
            SharePower(b, u);
            if (f.Lead == null) Lead(b, u, dt);
            else Wingman(b, u, dt);
        }

        /// <summary>
        /// A pilot shares its reactor as the fight needs (U.SPC-T424): shields when they are low or something is on its
        /// tail, engines to break off or run home, weapons on the run or in the dogfight, an even share otherwise.
        /// </summary>
        public static void SharePower(Battle b, BattleUnit u)
        {
            var p = u.Power;
            if (p == null) return;
            var f = u.Fighter;
            var lead = f.Lead != null && f.Order == WingOrder.FormUp ? f.Lead.Fighter : f;
            double shields = u.Combat.Spec.Shield > 0 ? u.Combat.Shield / u.Combat.Spec.Shield : 1;
            if (shields < 0.35 || b.MissileInbound(u, Countermeasures.ReachKm) != null) p.Set(1, 4, 1);
            else if (lead.Phase == FighterPhase.Return || lead.Phase == FighterPhase.BreakOff) p.Set(1, 1, 4);
            else if (lead.Phase == FighterPhase.AttackRun || lead.Phase == FighterPhase.Dogfight) p.Set(4, 1, 1);
            else p.Balance();
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

        /// <summary>A target still worth fighting: hostile, present, not struck and not taken.</summary>
        static bool Fair(Battle b, BattleUnit u, BattleUnit t) => t != null && b.Hostile(u, t) && !t.Surrendering && !t.Captured;

        /// <summary>
        /// A craft flying its own runs: a squadron's lead, or a wingman told to fight on its own. With
        /// <paramref name="pinned"/> it fights that target (its lead's, or what is on its lead) instead of choosing.
        /// </summary>
        static void Lead(Battle b, BattleUnit u, double dt, BattleUnit pinned = null)
        {
            var f = u.Fighter;
            var rng = b.Rng;
            if (pinned != null)
            {
                if (u.Target != pinned)
                {
                    u.Target = pinned;
                    if (f.Phase == FighterPhase.Dogfight || f.Phase == FighterPhase.AttackRun) Switch(f, FighterPhase.Approach);
                }
            }
            else
            {
                // A target that has struck its colours or changed hands is no longer one.
                bool lost = !Fair(b, u, u.Target);
                if (lost || (f.Phase != FighterPhase.AttackRun && rng.NextDouble() < dt / 20))
                {
                    u.Target = ChooseTarget(b, u);
                    if (f.Phase == FighterPhase.Dogfight || f.Phase == FighterPhase.AttackRun) Switch(f, FighterPhase.Approach);
                }
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
                if (!b.Hostile(u, o) || o.Surrendering || o.Captured) continue;
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
            // A bomber lines its run up on the side a weak point lies open from (U.SPC-T424): under the reactor's vent,
            // astern of the engines, over the shield emitters.
            if (u.Fighter.Bomber && WeakPoints.AiGoesForThem.Value && WeakPoints.Preferred(t) is Subsystem weak)
            {
                var axis = WeakPoints.Axis(t, WeakPoints.SpecOf(weak.Kind));
                var jitter = new DVec3(b.Rng.NextDouble() - 0.5, b.Rng.NextDouble() - 0.5, b.Rng.NextDouble() - 0.5) * 0.5;
                return (axis + jitter).Normalized * (5 + b.Rng.NextDouble() * 2);
            }
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
            // Only a lead changes phase; a wingman in the vee flies (and fires on) its lead's run, one fighting on its own its own run.
            var phase = f.Lead != null && FlysInFormation(b, u) ? f.Lead.Fighter.Phase : f.Phase;
            // The lead's own retarget check runs once a tick and can lag a target's death or
            // surrender by a tick (both are resolved later in Battle.Step, after fighters fly);
            // re-check the target's own state here so neither the lead nor a wingman copying its
            // target gets a last torpedo off at a hull that is already struck or already gone.
            if (!f.Bomber || phase != FighterPhase.AttackRun || u.Target == null || u.Target.Kind == UnitKind.Fighter
                || !b.Hostile(u, u.Target) || u.Target.Surrendering || u.Target.Captured) return;
            var to = u.Target.Pos - u.Pos;
            double dist = to.Length;
            if (dist > WeaponSpec.Torpedo.RangeKm || DVec3.Dot(u.Forward, to / dist) < TorpedoBoresightCos) return;
            LaunchTorpedoes(b, u, u.Target);
        }

        /// <summary>How near the nose a torpedo's target must be (cosine): about 25 degrees.</summary>
        public const double TorpedoBoresightCos = 0.9;

        /// <summary>
        /// The hardpoint a torpedo goes for (U.SPC-T424): the weak point the player has picked on the hull they have locked;
        /// for a pilot, the most telling weak point lying open from where it is; else wherever the seeker takes it.
        /// </summary>
        public static Subsystem TorpedoAimPoint(Battle b, BattleUnit u, BattleUnit target)
        {
            if (u.PlayerControlled)
            {
                var picked = u.PlayerSubTarget;
                if (target == u.PlayerLockTarget && picked != null && !picked.Destroyed && target.Combat.Subsystems.Contains(picked)) return picked;
            }
            else if (WeakPoints.AiGoesForThem.Value && WeakPoints.BestFrom(target, u.Pos) is Subsystem open) return open;
            return b.PickSubsystem(target, u);
        }

        /// <summary>Every ready torpedo the craft carries, away at <paramref name="target"/>.</summary>
        static void LaunchTorpedoes(Battle b, BattleUnit u, BattleUnit target)
        {
            foreach (var s in u.Combat.Subsystems)
            {
                if (s.Kind != SubsystemKind.Torpedo || s.Destroyed || s.Ammo <= 0 || s.Cooldown > 0) continue;
                s.Ammo--;
                s.Cooldown = s.Def.Weapon.Cooldown;
                var sub = TorpedoAimPoint(b, u, target);
                b.Missiles.Add(new Missile
                {
                    Pos = u.Pos, Vel = u.Vel, Target = target, TargetSub = sub, Damage = s.Def.Weapon.Damage,
                    Speed = s.Def.Weapon.MissileSpeedKmS + u.Vel.Length, Hp = 25, Life = 30, Side = u.Side, Torpedo = true,
                    Kind = MissileKind.Torpedo, SmallCraft = true,
                });
                b.Emit(FxKind.MissileLaunch, u.Pos, u.Pos + u.Forward, 0, u.Side, u, s.Index);
            }
        }

        // ---------------------------------------------------------------- flown by the player (U.SPC-T422)

        /// <summary>
        /// One step of a launched craft the player has taken (<see cref="FighterSeat"/>), flown by its own class's flight
        /// model (U.SPC-T17, U.SPC-T424; see <see cref="FighterCraft.Model"/>): the nose swings toward the stick at the
        /// craft's own rate and spin-up, flight assist holds the throttle's share of its top speed along the nose (strafing,
        /// rolling and the afterburner as the model allows, or off, drifting on its momentum), and engine power scales its
        /// thrust and top speed. The guns fire down the crosshair through the battle's usual player path; the second
        /// trigger fires the selected group: missiles at the locked target once the lock is made (the battle's own path),
        /// torpedoes at the locked hull, or the hostile hull nearest the nose. Its own drives knocked out, it drifts.
        /// </summary>
        public static void FlyManned(Battle b, BattleUnit u, FighterControl stick, double dt)
        {
            // The lock is its target: wingmen in the vee fire on it, and told to, go after it.
            if (u.PlayerLockTarget != null && !u.PlayerLockTarget.Present)
            {
                u.PlayerLockTarget = null;
                u.PlayerSubTarget = null;
            }
            u.Target = u.PlayerLockTarget;
            u.PlayerSecondaryHeld = stick.Secondary && stick.SecondaryGroup == WeaponGroup.Missiles;
            if (u.Stunned > 0)
            {
                u.Pos += u.Vel * dt;
                return;
            }
            var craft = u.Fighter != null ? FighterCraft.Of(u.Fighter.Role) : FighterCraft.Interceptor;
            double engines = PowerDistribution.FactorOf(u, PowerSystem.Engines);
            var hull = HullDynamics.Of(u.Combat.Spec);
            hull = new HullDynamics(hull.MassKt, hull.MainAccel * engines, hull.RetroAccel * engines, hull.LateralAccel * engines, hull.VerticalAccel * engines,
                hull.PitchYawAngularAccel, hull.RollAngularAccel);
            var state = new PilotState
            {
                Pos = u.Pos, Vel = u.Vel, Forward = u.Forward,
                Up = stick.Up.LengthSquared > 1e-12 ? stick.Up : PilotState.LevelUp(u.Forward),
                AngularVelocity = stick.AngularVelocity,
            };
            var input = new PilotInput
            {
                DesiredForward = stick.Aim.LengthSquared > 1e-12 ? stick.Aim.Normalized : DVec3.Zero,
                Throttle = Math.Max(0, Math.Min(1, stick.Throttle)),
                StrafeRight = stick.StrafeRight, StrafeUp = stick.StrafeUp, Roll = stick.Roll,
                Boost = stick.Boost, InCombat = true, FlightAssistOff = stick.FlightAssistOff,
            };
            craft.Model.Step(ref state, u.Combat, hull, craft.Tuning(engines), input, DVec3.Zero, dt);
            u.Pos = state.Pos;
            u.Vel = state.Vel;
            u.Forward = state.Forward;
            stick.Up = state.Up;
            stick.AngularVelocity = state.AngularVelocity;
            if (stick.Secondary && stick.SecondaryGroup == WeaponGroup.Torpedoes && TorpedoTarget(b, u) is BattleUnit target) LaunchTorpedoes(b, u, target);
        }

        /// <summary>The hostile hull (never a fighter, never one that has struck or been taken) a torpedo goes at: the locked
        /// one if it is within torpedo range and the boresight, else the one nearest the nose that is; null if none.</summary>
        public static BattleUnit TorpedoTarget(Battle b, BattleUnit u)
        {
            var locked = u.PlayerLockTarget;
            if (locked != null && locked.Present && locked.Kind != UnitKind.Fighter && Fair(b, u, locked))
            {
                var toLocked = locked.Pos - u.Pos;
                double d = toLocked.Length;
                if (d > 1e-9 && d - locked.Radius <= WeaponSpec.Torpedo.RangeKm && DVec3.Dot(u.Forward, toLocked / d) >= TorpedoBoresightCos) return locked;
            }
            BattleUnit best = null;
            double bestCos = TorpedoBoresightCos;
            foreach (var o in b.Units)
            {
                if (!o.Present || o.Kind == UnitKind.Fighter || !b.Hostile(u, o) || o.Surrendering || o.Captured) continue;
                var to = o.Pos - u.Pos;
                double dist = to.Length;
                if (dist < 1e-9 || dist - o.Radius > WeaponSpec.Torpedo.RangeKm) continue;
                double cos = DVec3.Dot(u.Forward, to / dist);
                if (cos >= bestCos)
                {
                    bestCos = cos;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>Back to the carrier's hangar and aboard; each landing puts a craft back in its hangar.</summary>
        static void Land(Battle b, BattleUnit u, double dt)
        {
            var carrier = u.Fighter.Carrier;
            Subsystem bay = null;
            foreach (var s in carrier.Combat.Subsystems)
                if (s.Kind == SubsystemKind.Hangar && !s.Destroyed) bay = s;
            var deck = bay != null ? carrier.PointOf(bay.Def) : carrier.Pos;
            // Line up astern of the bay and come in matched to the carrier.
            var approach = deck - carrier.Forward * 0.4;
            double toDeck = (deck - u.Pos).Length;
            var goal = toDeck < 0.6 ? deck : approach;
            Steer(b, u, goal + carrier.Vel * 2, u.Combat.Spec.MaxSpeed, dt, carrier.Vel, homing: true);
            if (toDeck < Math.Max(0.12, u.Vel.Length * dt * 1.5))
            {
                u.Docked = true;
                carrier.Combat.RestoreFighters(1);
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

        /// <summary>
        /// What a wingman's order leaves it fighting on its own (U.SPC-T424): its lead's target, whatever is on its lead,
        /// or one of its own; null when it holds the vee.
        /// </summary>
        static bool Independent(Battle b, BattleUnit u, out BattleUnit pinned)
        {
            var f = u.Fighter;
            pinned = null;
            switch (f.Order)
            {
                case WingOrder.AttackMyTarget:
                    pinned = Fair(b, u, f.Lead.Target) ? f.Lead.Target : null;
                    return pinned != null;
                case WingOrder.CoverMe:
                    pinned = Wing.Threat(b, f.Lead);
                    return pinned != null;
                case WingOrder.EngageAtWill:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Whether a wingman is holding the vee on its lead (rather than fighting on its own, as ordered).</summary>
        static bool FlysInFormation(Battle b, BattleUnit u) => u.Fighter.Lead != null && !Independent(b, u, out _);

        static void Wingman(Battle b, BattleUnit u, double dt)
        {
            var f = u.Fighter;
            var lead = f.Lead;
            var lf = lead.Fighter;
            if (Independent(b, u, out var pinned))
            {
                Lead(b, u, dt, pinned);
                return;
            }
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
        static void Steer(Battle b, BattleUnit u, DVec3 goal, double speed, double dt, DVec3? match = null, bool homing = false)
        {
            var spec = u.Combat.Spec;
            // Power to the engines (U.SPC-T424) is speed and thrust.
            double engines = PowerDistribution.FactorOf(u, PowerSystem.Engines);
            speed *= engines;
            var to = goal - u.Pos;
            double dist = to.Length;
            // Stay inside the battle -- except homing on its carrier, wherever that has drifted to.
            if (!homing && u.Pos.Length > Battle.EscapeKm * 0.8) to = -u.Pos;
            double toLength = to.Length;
            var desiredDir = toLength > 1e-6 ? to / toLength : u.Forward;
            double turn = spec.TurnRateDegS * Astro.Deg2Rad * dt;
            double cos = Math.Max(-1, Math.Min(1, DVec3.Dot(u.Forward, desiredDir)));
            double angle = Math.Acos(cos);
            u.Forward = angle <= turn ? desiredDir : DVec3.TurnStep(u.Forward, desiredDir, turn / angle);
            // Throttle back when closing on a slot so wingmen do not overshoot.
            double want = speed;
            // Given a velocity to match (a carrier's, a lead's), even a standing one: an unmoving carrier is still closed on gently.
            if (match is DVec3 m) want = Math.Min(speed, m.Length + dist * 1.2);
            var desiredVel = u.Forward * want;
            var dv = desiredVel - u.Vel;
            double step = spec.Accel * engines * dt;
            u.Vel += dv.Length > step ? dv.Normalized * step : dv;
            u.Pos += u.Vel * dt;
        }
    }
}
