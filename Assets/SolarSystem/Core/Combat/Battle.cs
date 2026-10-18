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

        /// <summary>True while a human is flying this unit's <see cref="Ship"/> directly: <see cref="Battle.Move"/>
        /// skips its own AI steering/position-integration for it (the Runtime pilot loop drives <see cref="Pos"/>,
        /// <see cref="Vel"/> and <see cref="Forward"/> instead every frame) and <see cref="Battle.Fire"/> routes its
        /// forward-facing weapons through <see cref="PlayerFireHeld"/>/<see cref="PlayerLockTarget"/> rather than
        /// AI target selection. Side/aft turrets and point defence are unaffected and keep firing autonomously.</summary>
        public bool PlayerControlled
        {
            get => playerControlled;
            set
            {
                playerControlled = value;
                if (value) PlayerFlown = true;
            }
        }

        bool playerControlled;

        /// <summary>A human flew this unit at some point in the battle (kept after <see cref="PlayerControlled"/> is
        /// cleared, as when the hull is captured), so the law knows whose deeds its were (see <see cref="Law.OnBattleEnded"/>).</summary>
        public bool PlayerFlown { get; private set; }

        /// <summary>While <see cref="PlayerControlled"/>: whether the player is holding the fire trigger this frame.</summary>
        public bool PlayerFireHeld;

        /// <summary>While <see cref="PlayerControlled"/>: the target a forward missile launcher fires at. Direct-fire
        /// weapons ignore this and aim wherever the hull's own nose (<see cref="Forward"/>) is pointed instead.</summary>
        public BattleUnit PlayerLockTarget;

        public BattleUnit Target;
        public double RetargetIn;
        public bool Fled, Dead;
        public bool Fleeing;

        /// <summary>True once this hull has struck its colours: holds fire and waits on its captor's
        /// terms (<see cref="SurrenderOutcome"/>) rather than fighting to the last. See <see cref="Morale"/>.</summary>
        public bool Surrendering;

        /// <summary>Seconds spent hailing since <see cref="Surrendering"/> began; past
        /// <see cref="Battle.SurrenderDecisionSeconds"/> the battle decides terms itself if nobody chooses first.</summary>
        public double SurrenderTimer;

        /// <summary>What its captor decided once it struck its colours, or null while still awaiting terms.</summary>
        public SurrenderTerms? SurrenderOutcome;

        /// <summary>Set once its captor chooses to take the ship: open to boarding exactly like a disabled hull.</summary>
        public bool SurrenderOpenToBoarding;

        /// <summary>This unit's own breaking point, 0-1, rolled once at creation so otherwise-identical
        /// hulls do not all strike their colours on the same tick under the same pressure. See <see cref="Morale.Pressure"/>.</summary>
        public double MoraleThreshold;

        /// <summary>Pirates hailing this (player-flown) hull with "your cargo or your life": who is asking,
        /// or null. Answered through <see cref="Battle.AnswerCargoDemand"/>; unanswered, it lapses into a fight.</summary>
        public Faction CargoDemandedBy;

        /// <summary>Seconds the current <see cref="CargoDemandedBy"/> has gone unanswered.</summary>
        public double CargoDemandTimer;

        /// <summary>Set once a cargo demand has been made of this hull, so pirates ask only once a battle.</summary>
        public bool CargoDemandMade;

        /// <summary>Formation: the unit this one keeps station on, and where (km, in the leader's frame).</summary>
        public BattleUnit Leader;
        public DVec3 Slot;

        /// <summary>Per-unit randomness: circling direction and a phase for station-keeping wander.</summary>
        public double Wander, Circle;

        /// <summary>Fighter-only state (see the fighter AI).</summary>
        public FighterState Fighter;

        /// <summary>Boarding: the disabled enemy this ship is closing on to board, and the one it is latched to.</summary>
        public BattleUnit BoardTarget, Latched;

        /// <summary>Where it is fixed to the latched hull (ship lengths in that hull's frame), and the countdown to opening the airlock.</summary>
        public DVec3 LatchLocal;
        public double LatchTimer;

        /// <summary>Taken by boarders: it has changed sides and carries a prize crew, so it no longer fights.</summary>
        public bool Captured;

        /// <summary>The side a captured hull fought on before it was taken (its <see cref="Side"/> is now its captor's); -1 until then.</summary>
        public int TakenFromSide = -1;

        /// <summary>The side it fought on: the one it was taken from, if it was captured.</summary>
        public int FoughtOn => Captured && TakenFromSide >= 0 ? TakenFromSide : Side;

        /// <summary>A fighter that has landed back aboard its carrier.</summary>
        public bool Docked;

        /// <summary>Seconds left with drives and shields knocked out by an interdiction pulse.</summary>
        public double Stunned;

        public bool Present => !Dead && !Fled && !Docked;
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
        public DVec3 PointOf(SubsystemDef d) => PointLocal(new DVec3(d.X, d.Y, d.Z));

        /// <summary>Battle-local position of a point given in ship lengths (right, up, forward).</summary>
        public DVec3 PointLocal(DVec3 local)
        {
            Basis(out var right, out var up);
            return Pos + (right * local.X + up * local.Y + Forward * local.Z) * LengthKm;
        }

        /// <summary>A battle-local point expressed in ship lengths in this hull's frame.</summary>
        public DVec3 ToLocal(DVec3 point)
        {
            Basis(out var right, out var up);
            var d = (point - Pos) / Math.Max(LengthKm, 1e-9);
            return new DVec3(DVec3.Dot(d, right), DVec3.Dot(d, up), DVec3.Dot(d, Forward));
        }

        /// <summary>Yaw angle (degrees) of a battle-local point relative to this hull's own facing:
        /// 0 = dead ahead, 90 = starboard, 180 = astern, 270 = port.</summary>
        public double YawDegOf(DVec3 point)
        {
            var local = ToLocal(point);
            return Astro.WrapDegrees360(Math.Atan2(local.X, local.Z) * Astro.Rad2Deg);
        }
    }

    public enum MissileKind
    {
        Missile,
        Torpedo,
        Pod,
    }

    /// <summary>A boarding pod or breacher stuck in a hull: where (ship lengths in that hull's frame), whose, and pointing which way.</summary>
    public sealed class Embedded
    {
        public BattleUnit Unit;
        public DVec3 Local;
        public DVec3 Inward;
        public int Side;
        public bool Breacher;
    }

    /// <summary>A battle fought in open space beside a hyperlane, after an interdiction pulse.</summary>
    public sealed class LaneSite
    {
        public LaneHop Hop;
        public DVec3 FromOffset, ToOffset;
        public double Progress;

        /// <summary>Offset of the fight from the lane line, km (the ambushers wait just outside it).</summary>
        public DVec3 Side;
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
        public MissileKind Kind;

        /// <summary>A pod's squad: who they fight for and how good they are.</summary>
        public Faction Owner;
        public double Marines;
    }

    /// <summary>
    /// A railgun slug or autocannon burst in flight: a dumb, non-homing round that was aimed, with a
    /// lead, at where its target was expected to be, and takes real time crossing the range instead of
    /// landing the instant the trigger was pulled. <see cref="Hit"/> is decided at the muzzle, same as a
    /// beam's accuracy roll; a round that "hit" only actually calls <see cref="Battle.ApplyDamage"/> once
    /// it arrives, so a target that dies or peels off first takes nothing. A round that missed keeps
    /// flying past its aim point and fades, rather than never having existed.
    /// </summary>
    public sealed class KineticRound
    {
        public DVec3 Pos, Vel;
        public BattleUnit Target;
        public Subsystem TargetSub;
        public double Damage;

        /// <summary>Seconds left before the round reaches its aim point (where it resolves, if it hit).</summary>
        public double ArriveIn;

        /// <summary>Seconds left before the round is removed -- longer than <see cref="ArriveIn"/> for a
        /// miss, so it keeps travelling past the target before it fades.</summary>
        public double Life;

        public int Side;
        public bool Hit;

        /// <summary>True for a railgun slug, false for fighter autocannon fire -- cosmetic only (Runtime
        /// draws the two differently).</summary>
        public bool Railgun;
        public bool Gone;
    }

    public enum FxKind
    {
        Beam,
        BeamMiss,

        /// <summary>A railgun's hypervelocity kinetic round -- a harder, thinner, whiter streak than a
        /// laser <see cref="Beam"/>.</summary>
        Railgun,
        RailgunMiss,
        PointDefense,
        Guns,
        ShieldHit,
        Impact,
        SubsystemDestroyed,
        MissileKill,
        UnitDestroyed,
        MissileLaunch,

        /// <summary>A pod or breacher holes a hull: a burst of venting atmosphere.</summary>
        Breach,
        PodBounce,
        Captured,
        Latch,

        /// <summary>An interdiction pulse: an expanding electromagnetic shell.</summary>
        Pulse,

        /// <summary>A hull breaking off, weapons powered down, hailing to ask terms.</summary>
        Hail,
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

        /// <summary>The subsystem index concerned, or -1: the hardpoint hit or lost, or for a
        /// <see cref="FxKind.ShieldHit"/> the shield generator whose arc took the hit.</summary>
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

        /// <summary>This battle's line-of-approach axis; fixed at creation so late joiners always use the right axis.</summary>
        public DVec3 ApproachAxis = DVec3.UnitX;

        public readonly double StartJd;
        public double Seconds;
        public readonly List<BattleUnit> Units = new List<BattleUnit>();
        public readonly List<Missile> Missiles = new List<Missile>();
        public readonly List<KineticRound> Rounds = new List<KineticRound>();
        public readonly List<Embedded> Embedded = new List<Embedded>();

        /// <summary>Set for an ambush beside a hyperlane; null for a battle at a body.</summary>
        public LaneSite Site;

        /// <summary>The side that started the fight (every battle the war starts puts its aggressor on side 0); a
        /// lawful ship on the other side was defending itself (see <see cref="Law.OnBattleEnded"/>).</summary>
        public int AggressorSide;

        /// <summary>The side enforcing the law (a patrol interdicting a wanted ship, see <see cref="War.TryStartInterdiction"/>), or -1.</summary>
        public int EnforcingSide = -1;

        /// <summary>Raised when boarders take a unit (the world decides what they do with it).</summary>
        public Action<Battle, BattleUnit, Faction> UnitCaptured;
        public readonly List<CombatFx> Fx = new List<CombatFx>();
        public readonly Faction[] SideLeaders = new Faction[2];

        /// <summary>How much of their usual resolve each side's crews keep (1 normally): facing a feared outlaw
        /// they strike their colours sooner (see <see cref="Outlaws.FearedResolve"/>).</summary>
        public readonly double[] Dread = { 1, 1 };

        public bool Over { get; private set; }

        /// <summary>True until the battle's first advance: it does not run in the frame it starts, so an observer can slow the clock first.</summary>
        public bool Fresh = true;

        /// <summary>The side that held the field (0 or 1), or -1 if both broke off.</summary>
        public int Winner { get; private set; } = -1;

        public int FxSeq { get; private set; }

        /// <summary>Raised when a unit is destroyed (the world books the loss).</summary>
        public Action<Battle, BattleUnit> UnitDestroyed;

        /// <summary>Raised the moment a hull strikes its colours: breaks off, powers down weapons and hails.</summary>
        public Action<Battle, BattleUnit> UnitSurrendered;

        /// <summary>Raised once a captor's terms for a surrendered hull are settled -- auto-decided or
        /// chosen externally via <see cref="OfferSurrenderTerms"/> -- so the world can apply the
        /// consequences (cargo ransomed, a clean getaway; a taken prize follows the normal capture hook).</summary>
        public Action<Battle, BattleUnit, Faction, SurrenderTerms> SurrenderDecided;

        /// <summary>Raised when pirates demand a player-flown hull's cargo (the hull, the pirates).</summary>
        public Action<Battle, BattleUnit, Faction> CargoDemanded;

        /// <summary>Fighter AI hook, run each step for every fighter (see FighterAI).</summary>
        public Action<Battle, BattleUnit, double> FighterStep;

        /// <summary>Carriers launch their squadrons through this (see FighterAI).</summary>
        public Action<Battle, BattleUnit> Launch;

        public const double EscapeKm = 55;
        public const double MaxStep = 1.0;

        /// <summary>How long a hailing hull waits before the battle decides terms itself if nobody has.</summary>
        public const double SurrenderDecisionSeconds = 15;

        /// <summary>How long an AI captor takes to answer a hail when no player-flown hull is there to choose.</summary>
        public const double SurrenderAiDecisionSeconds = 4;

        readonly double[] sideStrength = new double[2];
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
            var u = NewUnit(UnitKind.Ship, side, ship.Faction, ship.Combat, ship.LengthKm, pos, forward);
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
            combat.ResetShieldTimers();
            var u = new BattleUnit
            {
                Id = nextUnitId++, Kind = kind, Side = side, Faction = faction, Combat = combat, LengthKm = lengthKm,
                Pos = pos, Forward = forward.LengthSquared > 0 ? forward.Normalized : DVec3.UnitX,
                RetargetIn = rng.NextDouble() * 2, Wander = rng.NextDouble() * 100, Circle = rng.Next(2) == 0 ? -1 : 1,
                MoraleThreshold = 0.55 + rng.NextDouble() * 0.5,
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
            TallySideStrength();
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
            StepRounds(dt);
            StepBoarding(dt);
            StepSurrenders(dt);
            foreach (var u in Units)
            {
                if (!u.Present) continue;
                var c = u.Combat;
                if (u.Stunned > 0) u.Stunned -= dt;
                else c.RegenShields(Seconds, dt);
                // No escape with boarders fighting aboard (or a breacher stuck in the hull), nor for a hull that has struck
                // its colours or been taken: drifting off with its drives cold is not running.
                if (u.Kind == UnitKind.Ship && u.Latched == null && !c.Disabled && !c.Interior.UnderBoarding && !u.Surrendering && !u.Captured && (u.Pos.Length > EscapeKm || (u.Fleeing && NearestHostileDistance(u) > 30)))
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
                if (!u.Present || u.Kind == UnitKind.Fighter || u.Captured) continue;
                // Marines alone make a combatant only of a hull that will fly a boarding approach: the AI's,
                // never one the player is flying (see Think), which with no guns has nothing left to fight with.
                bool willing = !u.Fleeing && !u.Surrendering && !u.Combat.Disabled && (u.Combat.Armed || (CanBoard(u) && !u.PlayerControlled));
                if (u.Side == 0) { any0 = true; fight0 |= willing; }
                else { any1 = true; fight1 |= willing; }
            }
            bool fighting = any0 && any1 && (fight0 || fight1);
            Mopping = !fighting;
            if (Seconds < MaxSeconds && (fighting || Boarding || FightersAloft || AwaitingSurrenderTerms)) return;
            Over = true;
            Winner = fight0 && !fight1 ? 0 : fight1 && !fight0 ? 1 : any0 && !any1 ? 0 : any1 && !any0 ? 1 : -1;
        }

        /// <summary>A ship our side is boarding, or about to board: worth more taken than destroyed.</summary>
        bool IsPrize(BattleUnit u, BattleUnit target)
        {
            if (target.Combat.Interior.Boarder == u.Faction) return true;
            foreach (var o in Units)
                if (o.Side == u.Side && o.Present && (o.BoardTarget == target || o.Latched == target)) return true;
            return false;
        }

        /// <summary>A raider or warship with marines to spare is a combatant even with no guns (a breacher).</summary>
        bool CanBoard(BattleUnit u) =>
            u.Kind == UnitKind.Ship && (u.Faction.IsPirate || u.Faction.IsMilitary || (Boards != null && Boards(u))) && u.Combat.MarinesAvailable >= 15;

        /// <summary>Whether a unit holds its fire on a disabled foe it wants taken alive (a bounty hunter after its
        /// quarry's captain, U.SPC-T63) while its side can still board it, or null.</summary>
        public Func<BattleUnit, BattleUnit, bool> WantsAlive;

        /// <summary>Whether a unit sends boarders across though its faction is neither a navy nor raiders (bounty hunters), or null.</summary>
        public Func<BattleUnit, bool> Boards;

        /// <summary>Whether <paramref name="u"/> would board <paramref name="target"/>: a navy or raiders board anything,
        /// a hunter only the quarry it wants alive (never a lawful hull: that would be piracy).</summary>
        bool MayBoard(BattleUnit u, BattleUnit target) =>
            u.Faction.IsPirate || u.Faction.IsMilitary || (u.Ship != null && u.Ship.Spec.Class == ShipClass.Breacher) || (WantsAlive != null && WantsAlive(u, target));

        /// <summary>Whether anyone on <paramref name="side"/> can still send a boarding party.</summary>
        bool SideCanBoard(int side)
        {
            foreach (var o in Units)
                if (o.Present && o.Side == side && !o.Captured && !o.Combat.Disabled && CanBoard(o)) return true;
            return false;
        }

        /// <summary>The shooting is over; boarding parties and landing fighters are finishing up.</summary>
        public bool Mopping { get; private set; }

        /// <summary>A boarding fight or a boarding approach is under way somewhere in the battle.</summary>
        bool Boarding
        {
            get
            {
                foreach (var u in Units)
                {
                    if (!u.Present) continue;
                    if (u.Combat.Interior.UnderBoarding || u.BoardTarget != null || u.Latched != null) return true;
                }
                return false;
            }
        }

        /// <summary>Fighters still flying that have a carrier to land on.</summary>
        bool FightersAloft
        {
            get
            {
                foreach (var u in Units)
                    if (u.Kind == UnitKind.Fighter && u.Present && u.Fighter.Carrier.Present && !u.Fighter.Carrier.Captured) return true;
                return false;
            }
        }

        double NearestHostileDistance(BattleUnit u)
        {
            double best = double.MaxValue;
            foreach (var o in Units)
                if (Hostile(u, o) && o.Kind != UnitKind.Fighter && !o.Captured && (o.Combat.Armed || CanBoard(o)))
                    best = Math.Min(best, (o.Pos - u.Pos).Length);
            return best;
        }

        // ---------------------------------------------------------------- decisions

        void Think(BattleUnit u, double dt)
        {
            var c = u.Combat;
            if (u.Captured || u.Latched != null || u.Surrendering) return;
            if (u.Kind == UnitKind.Ship && !u.PlayerControlled && !c.Disabled && StrikesColours(u))
            {
                Surrender(u);
                return;
            }
            if (u.Kind == UnitKind.Ship && u.Faction.IsPirate && !u.PlayerControlled) MaybeDemandCargo(u);
            if (u.Kind == UnitKind.Ship)
            {
                bool merchant = u.Ship != null && !u.Ship.Spec.IsMilitary && !u.Faction.IsPirate;
                // Whether to run is the player's call when the player is flying it.
                if (c.Disabled || u.PlayerControlled) u.Fleeing = false;
                else if (!u.Fleeing && (merchant || c.HullFraction < 0.25 || (!c.Armed && !CanBoard(u)) || Routed(u.Side)))
                    u.Fleeing = true;
                // Only the AI flies a boarding approach (Move leaves a player-flown hull alone), so a
                // player-flown hull never holds a boarding target it would never close on.
                if (u.PlayerControlled) u.BoardTarget = null;
                else ChooseBoarding(u);
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
                if (!Hostile(u, o) || o.Captured || o.Surrendering) continue;
                // A hunter wants its quarry's captain alive: a disabled flagship is boarded, not finished off.
                if (o.Combat.Disabled && WantsAlive != null && WantsAlive(u, o) && SideCanBoard(u.Side)) continue;
                double score = (o.Pos - u.Pos).Length;
                // A disabled hull is a prize, not a threat: shoot it only if nothing else is left.
                if (o.Combat.Disabled) score += 40;
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
            // The Runtime pilot loop is the single source of truth for a player-flown hull's pose;
            // fighting it here would mean the battle sees a ghost position the player never flew to.
            if (u.PlayerControlled) return;
            var c = u.Combat;
            if (u.Latched != null)
            {
                // Fixed to the hull it is boarding.
                u.Pos = u.Latched.PointLocal(u.LatchLocal);
                u.Vel = u.Latched.Vel;
                return;
            }
            if (c.Disabled || u.Captured || u.Surrendering || u.Stunned > 0)
            {
                // Adrift: no drive, slowly losing what way it had.
                u.Vel *= Math.Max(0, 1 - 0.002 * dt);
                u.Pos += u.Vel * dt;
                return;
            }
            if (u.BoardTarget != null && !u.Fleeing)
            {
                Approach(u, dt);
                return;
            }
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

            var face = u.Fleeing || u.Target == null ? u.Vel : BroadsideFacing(u, (u.Target.Pos - u.Pos).Normalized);
            TurnToward(u, face, c.Spec.TurnRateDegS, dt);
        }

        /// <summary>
        /// Which way to point the bow so the hull's strongest ready battery (not on cooldown, with
        /// ammo, its mount not destroyed) bears on the target — a broadside warship turns a flank in,
        /// not its nose. A hull with no real arc spread (every weapon omnidirectional, today's default)
        /// has a single candidate centred on the bow, so it simply points its nose at the target.
        /// </summary>
        DVec3 BroadsideFacing(BattleUnit u, DVec3 toTarget)
        {
            Dictionary<int, double> byArc = null;
            foreach (var s in u.Combat.Subsystems)
            {
                if (!s.Def.IsWeapon || s.Destroyed || s.Kind == SubsystemKind.PointDefense || s.Kind == SubsystemKind.BoardingPods) continue;
                var w = s.Def.Weapon;
                if (w.Missile && s.Ammo <= 0) continue;
                byArc ??= new Dictionary<int, double>();
                int key = (int)Math.Round(Astro.WrapDegrees360(s.Def.ArcCenterDeg));
                byArc.TryGetValue(key, out double sum);
                byArc[key] = sum + w.Damage * w.Salvo / w.Cooldown;
            }
            if (byArc == null) return toTarget;
            int bestArc = 0;
            double best = -1;
            foreach (var kv in byArc)
                if (kv.Value > best) { best = kv.Value; bestArc = kv.Key; }
            return RotateYaw(toTarget, bestArc);
        }

        /// <summary>Rotates a direction by an angle (degrees) around the battle's vertical (ecliptic) axis.</summary>
        static DVec3 RotateYaw(DVec3 v, double angleDeg)
        {
            double rad = angleDeg * Astro.Deg2Rad;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            var axis = DVec3.UnitZ;
            return v * cos + DVec3.Cross(axis, v) * sin + axis * DVec3.Dot(axis, v) * (1 - cos);
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
            u.Forward = DVec3.TurnStep(u.Forward, want, t);
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
            if (u.Captured || u.Surrendering) return;
            double accuracyScale = c.BridgeIntact ? 1 : 0.6;
            foreach (var s in c.Subsystems)
            {
                var w = s.Def.Weapon;
                if (w == null || s.Destroyed) continue;
                s.Cooldown -= dt;
                if (s.Cooldown > 0 || s.Kind == SubsystemKind.Torpedo) continue;
                if (s.Kind == SubsystemKind.BoardingPods)
                {
                    FirePods(u, s);
                    continue;
                }
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

                BattleUnit target;
                if (u.PlayerControlled && IsPlayerAimable(s.Def))
                {
                    // The player's own nose guns: no AI target selection, no autofire. Side/aft
                    // batteries (a broadside hull's real firepower) fall through to the branch below
                    // completely unchanged, so piloting never silences a ship's crewed turrets.
                    target = u.PlayerFireHeld ? (w.Missile ? PlayerLockedTarget(u, w, origin) : NearestAlignedHostile(u, origin, w)) : null;
                }
                else
                {
                    target = ChooseWeaponTarget(u, origin, w);
                    // Hold fire on a prize our own people are boarding or closing on.
                    if (target != null && IsPrize(u, target)) target = null;
                }
                // A mount with a real firing arc (a broadside battery, fixed-forward fighter guns) can
                // only engage a target that actually falls within it.
                if (target != null && s.Def.ArcWidthDeg < 360 && !s.Def.ContainsYaw(u.YawDegOf(target.Pos))) target = null;
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
                var kind = s.Kind == SubsystemKind.FighterGuns ? FxKind.Guns
                    : w.AntiSmall ? FxKind.PointDefense
                    : s.Kind == SubsystemKind.Railgun ? (hit ? FxKind.Railgun : FxKind.RailgunMiss)
                    : hit ? FxKind.Beam : FxKind.BeamMiss;

                if (w.MuzzleSpeedKmS > 0)
                {
                    // A dumb kinetic round, not a hitscan: it takes real time to cross the range, so
                    // fire it off and let FireKineticRound/StepRounds decide what it actually hits.
                    FireKineticRound(u, s, origin, aimPoint, target, sub, w, hit, dist, kind);
                    continue;
                }

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

        /// <summary>
        /// Launches a railgun slug or autocannon round: leads the target's current velocity across the
        /// round's own time of flight (same lead construction a homing missile uses in <see cref="StepMissiles"/>,
        /// just without the homing -- a kinetic round is dumb and flies the straight line it was aimed
        /// down). The hit/miss roll already happened in <see cref="Fire"/>; a hit flies true to the lead
        /// point, a miss gets the same random offset a beam's miss does and keeps going past it. Emits the
        /// muzzle-flash Fx immediately (so it still reads as instant feedback at the gun) but defers the
        /// actual <see cref="ApplyDamage"/> to <see cref="StepRounds"/>, once the round has actually crossed
        /// the intervening space.
        /// </summary>
        void FireKineticRound(BattleUnit u, Subsystem mount, DVec3 origin, DVec3 aimPoint, BattleUnit target, Subsystem targetSub, WeaponSpec w, bool hit, double dist, FxKind kind)
        {
            double speed = w.MuzzleSpeedKmS;
            double leadTime = Math.Min(dist / speed, 20);
            var lead = aimPoint + target.Vel * leadTime;
            var impact = hit ? lead : lead + new DVec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5) * (target.LengthKm * 2 + 0.3);
            var dir = impact - origin;
            double travelKm = dir.Length;
            var vel = travelKm > 1e-9 ? dir / travelKm * speed : u.Forward * speed;
            double arriveIn = travelKm / speed;

            Emit(kind, origin, hit ? impact : origin + (impact - origin).Normalized * Math.Min(w.RangeKm, dist * 1.3), 0, u.Side, null, -1);
            Rounds.Add(new KineticRound
            {
                Pos = origin, Vel = vel, Target = target, TargetSub = targetSub, Damage = w.Damage,
                ArriveIn = arriveIn, Life = arriveIn + (hit ? 0 : 0.5), Side = u.Side, Hit = hit,
                Railgun = mount.Kind == SubsystemKind.Railgun,
            });
        }

        /// <summary>Advances every kinetic round in flight; a round that hit calls <see cref="ApplyDamage"/>
        /// the instant it arrives (skipped if the target died or got clear first), a round that missed just
        /// keeps flying until its own fuse runs out.</summary>
        void StepRounds(double dt)
        {
            foreach (var r in Rounds)
            {
                if (r.Gone) continue;
                r.Pos += r.Vel * dt;
                r.ArriveIn -= dt;
                r.Life -= dt;
                if (r.Hit && r.ArriveIn <= 0)
                {
                    r.Gone = true;
                    // Not into a hull that has since come over to the firer's own side (taken as a prize).
                    if (r.Target.Present && r.Target.Side != r.Side)
                    {
                        var sub = r.TargetSub != null && !r.TargetSub.Destroyed ? r.TargetSub : null;
                        ApplyDamage(r.Target, sub, r.Damage, r.Pos);
                    }
                }
                else if (r.Life <= 0) r.Gone = true;
            }
            Rounds.RemoveAll(r => r.Gone);
        }

        /// <summary>A mount the player can personally aim by pointing the hull's own nose at something --
        /// a forward-centred hardpoint, as opposed to a side or aft battery a crew works autonomously.</summary>
        // Point defence is never the player's to aim: it keeps engaging missiles and small craft on its own.
        static bool IsPlayerAimable(SubsystemDef d) =>
            !(d.Weapon != null && d.Weapon.AntiSmall) && Math.Abs(Astro.WrapDegrees360(d.ArcCenterDeg)) < 0.01;

        /// <summary>Boresight aim-assist for a player's direct-fire guns: the most dead-ahead hostile
        /// within a forgiving cone (real flight sticks aren't pixel-perfect) and the weapon's range.
        /// Picks the best alignment rather than the nearest hit, so a closer target well off-axis
        /// doesn't steal the shot from a distant one sitting right on the reticle.</summary>
        BattleUnit NearestAlignedHostile(BattleUnit u, DVec3 origin, WeaponSpec w)
        {
            const double AimConeCos = 0.985; // ~10 degrees either side of dead ahead
            BattleUnit best = null;
            double bestCos = AimConeCos;
            foreach (var o in Units)
            {
                if (!Hostile(u, o) || !o.Present) continue;
                var to = o.Pos - origin;
                double dist = to.Length;
                if (dist < 1e-6 || dist - o.Radius > w.RangeKm) continue;
                double cos = DVec3.Dot(u.Forward, to.Normalized);
                if (cos > bestCos) { bestCos = cos; best = o; }
            }
            return best;
        }

        /// <summary>A player's locked-on missile target, valid only while it is still hostile, present
        /// and in range -- cleared (by the Runtime pilot loop) the moment it stops being any of those.</summary>
        BattleUnit PlayerLockedTarget(BattleUnit u, WeaponSpec w, DVec3 origin)
        {
            var t = u.PlayerLockTarget;
            if (t == null || !t.Present || !Hostile(u, t)) return null;
            return (t.Pos - origin).Length - t.Radius <= w.RangeKm ? t : null;
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
                    if (!Hostile(u, o) || o.Captured || o.Surrendering) continue;
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
            // Its own target only while that is still a fair one: not struck its colours, not since taken
            // by this side (a capture doesn't clear everyone's target the way a surrender does).
            if (t != null && Hostile(u, t) && !t.Surrendering && !t.Captured && (t.Pos - origin).Length - t.Radius <= w.RangeKm && !(t.Kind == UnitKind.Fighter && w.Missile))
                return t;
            BattleUnit any = null;
            double nearest = w.RangeKm;
            foreach (var o in Units)
            {
                if (!Hostile(u, o) || o.Captured || o.Surrendering || (o.Kind == UnitKind.Fighter && (w.Missile || u.Kind != UnitKind.Fighter))) continue;
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
                    case SubsystemKind.Railgun:
                    case SubsystemKind.MissileLauncher: w = 3; break;
                    case SubsystemKind.ShieldGenerator: w = s.Shield > 0 ? 2.5 : 0.5; break;
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
            var generator = c.GeneratorFor(target.YawDegOf(at));
            if (generator != null)
            {
                generator.LastHitSeconds = Seconds;
                if (generator.Shield > 0)
                {
                    double soaked = Math.Min(generator.Shield, damage);
                    generator.Shield -= soaked;
                    damage -= soaked;
                    // Sub names the generator whose arc took it, so the bubble it raises is that arc's (U.SPC-T40).
                    Emit(FxKind.ShieldHit, at, target.Pos, target.LengthKm, target.Side, target, generator.Index);
                    if (damage <= 0) return;
                }
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
                    if (sub.Kind == SubsystemKind.ShieldGenerator) sub.Shield = 0;
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

        /// <summary>
        /// A collision's damage to a unit (U.SPC-T60): <paramref name="hp"/> (see <see cref="Collisions.DamageHp"/>)
        /// done at battle-local point <paramref name="at"/> to the hardpoints on that side, through its shields there
        /// (<see cref="Collisions.ApplyDamage"/>), with the same effects and the same death as a shot.
        /// </summary>
        public void ApplyImpact(BattleUnit target, DVec3 at, double hp)
        {
            if (!target.Present || hp <= 0) return;
            var c = target.Combat;
            double shieldBefore = c.Shield;
            var generator = c.GeneratorFor(target.YawDegOf(at));
            var lost = new List<Subsystem>();
            double through = Collisions.ApplyDamage(c, target.ToLocal(at), hp, Seconds, lost);
            if (c.Shield < shieldBefore) Emit(FxKind.ShieldHit, at, target.Pos, target.LengthKm, target.Side, target, generator?.Index ?? -1);
            if (through > 0) Emit(FxKind.Impact, at, at, through, target.Side, target, -1);
            foreach (var s in lost) Emit(FxKind.SubsystemDestroyed, at, at, target.LengthKm * 0.12, target.Side, target, s.Index);
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
                // A hull that has struck its colours is no longer shot at, nor boarded until its terms open it.
                bool spared = m.Target != null && m.Target.Surrendering && !(m.Kind == MissileKind.Pod && m.Target.SurrenderOpenToBoarding);
                if (m.Life <= 0 || m.Target == null || !m.Target.Present || m.Target.Side == m.Side || spared)
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
                    if (m.Kind == MissileKind.Pod) PodHit(m, t, aim);
                    else ApplyDamage(t, m.TargetSub != null && !m.TargetSub.Destroyed ? m.TargetSub : PickSubsystem(t), m.Damage, aim);
                }
            }
            Missiles.RemoveAll(m => m.Gone);
        }

        // ---------------------------------------------------------------- boarding

        /// <summary>
        /// Pods go at hulls whose shields are down (they cannot get through a shield); each carries a
        /// squad. A ship keeps a reserve of marines aboard to defend itself.
        /// </summary>
        void FirePods(BattleUnit u, Subsystem s)
        {
            if (s.Ammo <= 0 || u.Combat.MarinesAvailable < WeaponSpec.PodMarines * 2) return;
            BattleUnit target = null;
            double best = s.Def.Weapon.RangeKm;
            foreach (var o in Units)
            {
                if (!Hostile(u, o) || o.Captured || o.Kind == UnitKind.Fighter) continue;
                // A hull waiting on terms isn't fired on, pods included -- unless the terms are to take it.
                if (o.Surrendering && !o.SurrenderOpenToBoarding) continue;
                if (o.Combat.Shield > o.Combat.Spec.Shield * 0.05 && o.Combat.HasShieldGenerator) continue;
                if (o.Combat.Interior.Boarder != null && o.Combat.Interior.Boarder != u.Faction) continue;
                double d = (o.Pos - u.Pos).Length;
                if (o.Combat.Disabled) d *= 0.5;
                if (d < best)
                {
                    best = d;
                    target = o;
                }
            }
            if (target == null) return;
            var origin = u.PointOf(s.Def);
            s.Cooldown = s.Def.Weapon.Cooldown * (0.85 + rng.NextDouble() * 0.3);
            for (int k = 0; k < s.Def.Weapon.Salvo && s.Ammo > 0; k++)
            {
                if (u.Combat.MarinesAvailable < WeaponSpec.PodMarines * 2) break;
                s.Ammo--;
                RemoveMarines(u.Combat.Interior, WeaponSpec.PodMarines);
                var spread = new DVec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5) * 0.3;
                Missiles.Add(new Missile
                {
                    Pos = origin, Vel = ((target.Pos - origin).Normalized + spread).Normalized * 0.3 + u.Vel, Target = target, TargetSub = PickSubsystem(target),
                    Damage = s.Def.Weapon.Damage, Speed = s.Def.Weapon.MissileSpeedKmS, Hp = 40, Life = 60, Side = u.Side, Kind = MissileKind.Pod,
                    Owner = u.Faction, Marines = WeaponSpec.PodMarines,
                });
            }
            Emit(FxKind.MissileLaunch, origin, origin + u.Forward, 0, u.Side, u, s.Index);
        }

        void PodHit(Missile m, BattleUnit t, DVec3 at)
        {
            var c = t.Combat;
            var generator = c.GeneratorFor(t.YawDegOf(at));
            if (generator != null && generator.Shield > 0)
            {
                Emit(FxKind.PodBounce, at, at, t.LengthKm, t.Side, t, -1);
                return;
            }
            // The drill tip punches in; the squad goes through the hole it made.
            ApplyDamage(t, m.TargetSub != null && !m.TargetSub.Destroyed ? m.TargetSub : null, m.Damage, at);
            if (!t.Present) return;
            Embed(t, at, m.Vel, m.Side, false);
            var local = t.ToLocal(at);
            var space = c.Interior.Nearest(local.Z, local.Y);
            c.Interior.Board(m.Owner, m.Owner.MarineQuality, m.Marines, space, true, Seconds, rng);
            Emit(FxKind.Breach, at, at - m.Vel.Normalized * 0.05, t.LengthKm * 0.1, m.Side, t, space.Index);
        }

        void Embed(BattleUnit t, DVec3 at, DVec3 travel, int side, bool breacher)
        {
            var inward = travel.LengthSquared > 0 ? travel.Normalized : (t.Pos - at).Normalized;
            t.Basis(out var right, out var up);
            Embedded.Add(new Embedded
            {
                Unit = t, Local = t.ToLocal(at), Side = side, Breacher = breacher,
                Inward = new DVec3(DVec3.Dot(inward, right), DVec3.Dot(inward, up), DVec3.Dot(inward, t.Forward)),
            });
        }

        static void RemoveMarines(Interior interior, double count)
        {
            foreach (var c in interior.Compartments)
            {
                double take = Math.Min(c.Marines, count);
                c.Marines -= take;
                count -= take;
                if (count <= 0) return;
            }
        }

        /// <summary>
        /// A ship with marines to spare picks a disabled enemy to board, once no healthy enemy warship
        /// is close enough to make it suicide. Breachers are less careful: any hull with its shields
        /// down will do.
        /// </summary>
        void ChooseBoarding(BattleUnit u)
        {
            bool breacher = u.Ship != null && u.Ship.Spec.Class == ShipClass.Breacher;
            if (u.BoardTarget != null)
            {
                var bt = u.BoardTarget;
                // A hull waiting on terms is not boarded until they open it to boarding (as it is not fired on, pods included).
                bool stillOpen = bt.Present && !bt.Captured && (!bt.Surrendering || bt.SurrenderOpenToBoarding) &&
                    (bt.Combat.Disabled || bt.SurrenderOpenToBoarding || bt.Stunned > 10 || (breacher && (bt.Combat.Shield <= bt.Combat.Spec.Shield * 0.25 || !bt.Combat.HasShieldGenerator)));
                if (!stillOpen) u.BoardTarget = null;
            }
            // A boarder that can no longer fly the approach (crippled, or breaking off) gives up the
            // prize, or the battle would wait on a boarding that never comes.
            if (u.Fleeing || u.Combat.Disabled) { u.BoardTarget = null; return; }
            if (u.BoardTarget != null) return;
            bool boarder = breacher || u.Faction.IsPirate || u.Faction.IsMilitary || (Boards != null && Boards(u));
            if (!boarder || u.Combat.MarinesAvailable < 15) return;
            BattleUnit best = null;
            double bestD = double.MaxValue;
            foreach (var o in Units)
            {
                if (!Hostile(u, o) || o.Captured || o.Kind != UnitKind.Ship) continue;
                if (o.Combat.Interior.Boarder != null && o.Combat.Interior.Boarder != u.Faction) continue;
                if (!MayBoard(u, o) || (o.Surrendering && !o.SurrenderOpenToBoarding)) continue;
                bool open = o.Combat.Disabled || o.SurrenderOpenToBoarding || o.Stunned > 10 || (breacher && (o.Combat.Shield <= o.Combat.Spec.Shield * 0.25 || !o.Combat.HasShieldGenerator));
                if (!open) continue;
                if (!breacher && ThreatNear(u, o.Pos, 6)) continue;
                double d = (o.Pos - u.Pos).Length;
                if (d < bestD)
                {
                    bestD = d;
                    best = o;
                }
            }
            u.BoardTarget = best;
        }

        bool ThreatNear(BattleUnit u, DVec3 at, double km)
        {
            foreach (var o in Units)
                if (Hostile(u, o) && !o.Captured && !o.Surrendering && o.Kind != UnitKind.Fighter && o.Combat.Armed && !o.Combat.Disabled && (o.Pos - at).Length < km)
                    return true;
            return false;
        }

        /// <summary>
        /// Closing to board. A boarding ship lines up alongside, matches the target's drift, latches
        /// and after a while opens its airlock. A breacher rams nose first instead and its assault bay
        /// opens straight into the hull.
        /// </summary>
        void Approach(BattleUnit u, double dt)
        {
            var t = u.BoardTarget;
            var c = u.Combat;
            bool breacher = u.Ship != null && u.Ship.Spec.Class == ShipClass.Breacher;
            double maxSpeed = c.Spec.MaxSpeed * (0.25 + 0.75 * c.EngineFactor);
            t.Basis(out var right, out _);
            var dock = breacher ? t.Pos : t.Pos + right * (t.Radius * 0.35 + u.Radius + 0.06);
            var to = dock - u.Pos;
            double dist = to.Length;
            // Arrive matched: slow down as it closes, holding the target's own drift.
            double want = breacher ? maxSpeed : Math.Min(maxSpeed, dist * 0.05 + 0.002);
            var desired = t.Vel + (dist > 1e-6 ? to / dist : DVec3.Zero) * want;
            var dv = desired - u.Vel;
            double step = c.Spec.Accel * (0.3 + 0.7 * c.EngineFactor) * dt * (breacher ? 1 : 1.5);
            u.Vel += dv.Length > step ? dv.Normalized * step : dv;
            u.Pos += u.Vel * dt;
            TurnToward(u, breacher ? to : t.Forward, c.Spec.TurnRateDegS, dt);

            if (breacher)
            {
                if (dist < t.Radius * 0.55 + u.Radius * 0.5)
                {
                    var contact = t.Pos + (u.Pos - t.Pos).Normalized * t.Radius * 0.3;
                    u.Latched = t;
                    u.LatchLocal = t.ToLocal(u.Pos);
                    u.BoardTarget = null;
                    ApplyDamage(t, PickSubsystem(t), 300, contact);
                    if (!t.Present) return;
                    Embed(t, contact, to, u.Side, true);
                    var local = t.ToLocal(contact);
                    var space = t.Combat.Interior.Nearest(local.Z, local.Y);
                    if (!t.Combat.Interior.Captured && (t.Combat.Interior.Boarder == null || t.Combat.Interior.Boarder == u.Faction))
                    {
                        double marines = c.MarinesAvailable * 0.9;
                        RemoveMarines(c.Interior, marines);
                        t.Combat.Interior.Board(u.Faction, u.Faction.MarineQuality * 1.15, marines, space, true, Seconds, rng);
                    }
                    Emit(FxKind.Breach, contact, contact - to.Normalized * 0.1, t.LengthKm * 0.15, u.Side, t, space.Index);
                }
                return;
            }

            if (dist < 0.08 && (u.Vel - t.Vel).Length < 0.01)
            {
                u.LatchTimer += dt;
                if (u.LatchTimer > 20)
                {
                    u.Latched = t;
                    u.LatchLocal = t.ToLocal(u.Pos);
                    u.BoardTarget = null;
                    u.LatchTimer = 0;
                    if (!t.Combat.Interior.Captured && (t.Combat.Interior.Boarder == null || t.Combat.Interior.Boarder == u.Faction))
                    {
                        double marines = c.MarinesAvailable * 0.85;
                        RemoveMarines(c.Interior, marines);
                        var airlock = t.Combat.Interior.Of(CompartmentKind.Midships) ?? t.Combat.Interior.Nearest(0, 0);
                        t.Combat.Interior.Board(u.Faction, u.Faction.MarineQuality, marines, airlock, false, Seconds, rng);
                        Emit(FxKind.Latch, u.Pos, t.Pos, u.LengthKm, u.Side, t, airlock.Index);
                    }
                }
            }
            else u.LatchTimer = 0;
        }

        /// <summary>Runs every fight aboard; releases boarding ships when it is decided.</summary>
        void StepBoarding(double dt)
        {
            foreach (var u in Units)
            {
                if (!u.Present || u.Captured) continue;
                var interior = u.Combat.Interior;
                if (interior.Boarder == null) continue;
                var boarder = interior.Boarder;
                if (u.SurrenderOpenToBoarding) Capture(u, boarder);
                else if (interior.Step(dt, Seconds, rng)) Capture(u, boarder);
            }
            foreach (var u in Units)
            {
                if (u.Latched == null) continue;
                var t = u.Latched;
                if (!t.Present || t.Captured || t.Combat.Interior.Boarder == null)
                {
                    // Done (taken, repelled or gone): cast off.
                    u.Latched = null;
                    u.Vel += (u.Pos - t.Pos).Normalized * 0.01;
                }
            }
        }

        void Capture(BattleUnit u, Faction captor)
        {
            int side = -1;
            foreach (var o in Units)
                if (o.Faction == captor && o.Side != u.Side) side = o.Side;
            if (side < 0) side = 1 - u.Side;
            if (!u.Captured) u.TakenFromSide = u.Side;
            u.Captured = true;
            u.Side = side;
            u.Fleeing = false;
            u.Target = null;
            u.BoardTarget = null;
            // Taken while still hailing: there are no terms left to settle.
            u.Surrendering = false;
            // Its escorts no longer keep station on it (it fights for the other side now), nor it on its old flagship.
            u.Leader = null;
            foreach (var o in Units)
                if (o.Leader == u) o.Leader = null;
            // A surrendered hull is taken without a fight aboard: mark it held so the fight stops there too.
            u.Combat.Interior.Captured = true;
            u.Combat.Interior.Settle();
            Emit(FxKind.Captured, u.Pos, u.Pos, u.LengthKm, side, u, -1);
            UnitCaptured?.Invoke(this, u, captor);
        }

        // ---------------------------------------------------------------- surrender

        /// <summary>A hull has struck its colours and nobody has settled its terms yet.</summary>
        bool AwaitingSurrenderTerms
        {
            get
            {
                foreach (var u in Units)
                    if (u.Present && u.Surrendering && u.SurrenderOutcome == null) return true;
                return false;
            }
        }

        void TallySideStrength()
        {
            sideStrength[0] = sideStrength[1] = 0;
            foreach (var u in Units)
                if (u.Present && !u.Captured && !u.Surrendering && u.Side >= 0 && u.Side < 2)
                    sideStrength[u.Side] += u.Combat.Strength;
        }

        double OutgunnedOf(BattleUnit u) =>
            u.Side < 0 || u.Side > 1 ? 0 : Morale.Outgunned(sideStrength[u.Side], sideStrength[1 - u.Side]);

        static double DamageTollOf(BattleUnit u)
        {
            var c = u.Combat;
            double shield = c.Spec.Shield > 0 ? c.Shield / c.Spec.Shield : 1;
            return Morale.DamageToll(c.HullFraction, shield);
        }

        bool StrikesColours(BattleUnit u) =>
            Morale.StrikesColours(DamageTollOf(u), OutgunnedOf(u),
                Morale.Resolve(u.Faction, u.Ship != null && u.Ship.Spec.IsMilitary) * (u.Side >= 0 && u.Side < 2 ? Dread[u.Side] : 1), u.MoraleThreshold);

        /// <summary>
        /// Strikes <paramref name="u"/>'s colours: it breaks off, powers down its weapons and hails,
        /// and is no longer shot at while it waits on its captor's terms.
        /// </summary>
        public void Surrender(BattleUnit u)
        {
            if (u.Surrendering || !u.Present || u.Captured) return;
            u.Surrendering = true;
            u.SurrenderTimer = 0;
            u.Fleeing = false;
            u.Target = null;
            u.BoardTarget = null;
            foreach (var o in Units)
            {
                if (o.Target == u) o.Target = null;
                if (o.BoardTarget == u && !u.SurrenderOpenToBoarding) o.BoardTarget = null;
            }
            Emit(FxKind.Hail, u.Pos, u.Pos, u.LengthKm, u.Side, u, -1);
            UnitSurrendered?.Invoke(this, u);
        }

        /// <summary>The faction a hull on <paramref name="side"/> surrenders to: the other side's leader.</summary>
        public Faction CaptorOf(int side) => side < 0 || side > 1 ? null : SideLeaders[1 - side];

        /// <summary>True when someone on the side facing <paramref name="u"/> could board it.</summary>
        bool CaptorCanBoard(BattleUnit u)
        {
            foreach (var o in Units)
                // A player-flown hull never flies a boarding approach (see Think), so it can't be the one to take it.
                if (Hostile(u, o) && !o.Captured && !o.Surrendering && !o.Combat.Disabled && !o.PlayerControlled && CanBoard(o) && MayBoard(o, u)) return true;
            return false;
        }

        bool CaptorPlayerPresent(BattleUnit u)
        {
            foreach (var o in Units)
                if (Hostile(u, o) && o.PlayerControlled) return true;
            return false;
        }

        /// <summary>The first hull on the side facing <paramref name="captorSide"/> still waiting for terms, or null.</summary>
        public BattleUnit AwaitingTermsFrom(int captorSide)
        {
            foreach (var u in Units)
                if (u.Present && u.Surrendering && u.SurrenderOutcome == null && u.Side != captorSide) return u;
            return null;
        }

        /// <summary>
        /// The captor's answer to a hull that has struck its colours: hand over the cargo and go, be
        /// boarded and taken, or simply go. Taking it needs someone able to board; with nobody, it is
        /// refused. False when <paramref name="u"/> isn't waiting on terms.
        /// </summary>
        public bool OfferSurrenderTerms(BattleUnit u, SurrenderTerms terms)
        {
            if (u == null || !u.Present || !u.Surrendering || u.SurrenderOutcome != null) return false;
            if (terms == SurrenderTerms.TakeShip && !CaptorCanBoard(u)) return false;
            Settle(u, CaptorOf(u.Side), terms);
            return true;
        }

        void Settle(BattleUnit u, Faction captor, SurrenderTerms terms)
        {
            u.SurrenderOutcome = terms;
            if (terms == SurrenderTerms.TakeShip) u.SurrenderOpenToBoarding = true;
            SurrenderDecided?.Invoke(this, u, captor, terms);
            // Released: the hull powers up and leaves the field.
            if (terms != SurrenderTerms.TakeShip) u.Fled = true;
        }

        void StepSurrenders(double dt)
        {
            foreach (var u in Units)
            {
                if (!u.Present) continue;
                if (u.CargoDemandedBy != null)
                {
                    u.CargoDemandTimer += dt;
                    if (u.CargoDemandTimer > SurrenderDecisionSeconds) AnswerCargoDemand(u, false);
                }
                if (!u.Surrendering || u.SurrenderOutcome != null) continue;
                u.SurrenderTimer += dt;
                double wait = CaptorPlayerPresent(u) ? SurrenderDecisionSeconds : SurrenderAiDecisionSeconds;
                if (u.SurrenderTimer < wait) continue;
                var captor = CaptorOf(u.Side);
                if (captor == null) { Settle(u, null, SurrenderTerms.LetGo); continue; }
                bool warship = u.Ship != null && u.Ship.Spec.IsMilitary;
                bool cargo = u.Ship != null && u.Ship.HasCargo;
                Settle(u, captor, Morale.ChooseTerms(captor.IsPirate, warship, cargo, CaptorCanBoard(u), rng.NextDouble()));
            }
        }

        /// <summary>
        /// Pirates with the upper hand hail a player-flown hull that carries cargo: hand it over and go,
        /// or fight. Asked once a battle.
        /// </summary>
        void MaybeDemandCargo(BattleUnit pirate)
        {
            foreach (var v in Units)
            {
                if (!Hostile(pirate, v) || !v.PlayerControlled || v.CargoDemandMade || v.Surrendering) continue;
                if (v.Ship == null || !v.Ship.HasCargo) continue;
                if (!Morale.ShouldDemandCargo(OutgunnedOf(v), DamageTollOf(v))) continue;
                v.CargoDemandMade = true;
                v.CargoDemandedBy = pirate.Faction;
                v.CargoDemandTimer = 0;
                Emit(FxKind.Hail, pirate.Pos, v.Pos, pirate.LengthKm, pirate.Side, pirate, -1);
                CargoDemanded?.Invoke(this, v, pirate.Faction);
            }
        }

        /// <summary>
        /// The player's answer to a pirate cargo demand: comply and the cargo is ransomed and the hull
        /// goes free (it leaves the field); refuse and the fight goes on. False when nothing was asked.
        /// </summary>
        public bool AnswerCargoDemand(BattleUnit victim, bool comply)
        {
            if (victim == null || victim.CargoDemandedBy == null) return false;
            var pirates = victim.CargoDemandedBy;
            victim.CargoDemandedBy = null;
            if (!comply || !victim.Present) return true;
            victim.SurrenderOutcome = SurrenderTerms.RansomCargo;
            SurrenderDecided?.Invoke(this, victim, pirates, SurrenderTerms.RansomCargo);
            victim.Fled = true;
            return true;
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
    }
}
