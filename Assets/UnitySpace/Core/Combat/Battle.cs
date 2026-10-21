using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
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

        /// <summary>The gate a fort unit guards (U.SPC-T92); its <see cref="Kind"/> is <see cref="UnitKind.Platform"/>, with no settlement.</summary>
        public JumpGate Fort;
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

        /// <summary>While <see cref="PlayerControlled"/>: the target a forward missile launcher fires at, once
        /// <see cref="LockProgress"/> has made the lock. Direct-fire weapons ignore this and fire down the
        /// crosshair (<see cref="PlayerAim"/>) instead.</summary>
        public BattleUnit PlayerLockTarget;

        /// <summary>While <see cref="PlayerControlled"/>: where the crosshair points (unit, ecliptic axes), the
        /// line the player's guns swing to meet (<see cref="Gunnery"/>, U.SPC-T270). Zero means down the nose.</summary>
        public DVec3 PlayerAim;

        /// <summary>While <see cref="PlayerControlled"/>: the missile trigger held this frame.</summary>
        public bool PlayerSecondaryHeld;

        /// <summary>The player's guns' heat, 0-1: firing builds it, and once it reaches 1 they are
        /// <see cref="Overheated"/> until it has fallen below <see cref="Gunnery.HeatRecoverBelow"/>.</summary>
        public double Heat;
        public bool Overheated;

        /// <summary>The missile lock on <see cref="PlayerLockTarget"/>, 0-1: made at 1 (see <see cref="Gunnery.LockSeconds"/>).</summary>
        public double LockProgress;

        /// <summary>A dogfighting small craft's manoeuvre (see <see cref="Battle.Dogfight"/>, U.SPC-T274), the seconds
        /// it holds it for, and the way it breaks.</summary>
        public DogfightManeuver Maneuver;
        public double ManeuverLeft;
        public int BreakSide = 1;

        /// <summary>The velocity gunners read this hull as having: its real one, eased in over
        /// <see cref="Gunnery.TrackingLagS"/>, so a steady course is tracked and a sudden jink is not (U.SPC-T270).</summary>
        public DVec3 SeenVel;

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

        /// <summary>Seconds left with its systems knocked out by an interdiction pulse or an EMP: drives, guns and
        /// shield regeneration all down until it comes back.</summary>
        public double Stunned;

        public bool Present => !Dead && !Fled && !Docked;
        public double Radius => LengthKm * 0.5;
        public string Name => Ship != null ? Ship.Name : Platform != null ? Platform.Name + " defence platform" : Fort != null ? Fort.Name + " fort" : "Fighter";

        /// <summary>While <see cref="PlayerControlled"/>: the EMP trigger held, and a mine asked for this frame (the
        /// battle clears it once it has tried, U.SPC-T271).</summary>
        public bool PlayerEmpHeld, PlayerMineRequested;

        /// <summary>While <see cref="PlayerControlled"/>: a capital ship at the helm (U.SPC-T272). Its crews work every
        /// mount, favouring the locked hull, and the player calls broadsides: holding the trigger has the battery
        /// bearing on <see cref="PlayerVolleyAim"/> hold its fire and build a volley, and letting go fires it together.</summary>
        public bool PlayerHelm;

        /// <summary>At the helm: where the player is looking (unit, ecliptic axes), the side a volley is called on.</summary>
        public DVec3 PlayerVolleyAim;

        /// <summary>At the helm: the trigger let go this frame, firing the volley (cleared once the battle has fired it).</summary>
        public bool PlayerVolleyRelease;

        /// <summary>At the helm: seconds the volley has been building, up to <see cref="Battle.VolleyBuildSeconds"/>.</summary>
        public double VolleyCharge;

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

    /// <summary>What a dogfighting small craft is doing (U.SPC-T274).</summary>
    public enum DogfightManeuver
    {
        /// <summary>Nose on the lead point, guns working, jinking on the way in.</summary>
        Attack,

        /// <summary>Through the merge and out the other side under boost, to come round for another pass.</summary>
        Extend,

        /// <summary>A hard turn away from a hostile on its tail.</summary>
        Break,
    }

    public enum MissileKind
    {
        Missile,
        Torpedo,
        Pod,

        /// <summary>An EMP round: it bursts beside its target and stuns rather than damages (U.SPC-T271).</summary>
        Emp,
    }

    /// <summary>
    /// A proximity mine (U.SPC-T271): it sits where it was laid, arms after a moment, and blows when a hull hostile to
    /// its layer comes within its trigger, hurting everything hostile within its blast.
    /// </summary>
    public sealed class Mine
    {
        public DVec3 Pos, Vel;
        public int Side;
        public double Damage, BlastKm;

        /// <summary>Seconds until it is armed, and until it fizzles out unexploded.</summary>
        public double ArmIn, Life;
        public bool Gone;

        public bool Armed => ArmIn <= 0;
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

        /// <summary>An EMP round's stun and reach (see <see cref="WeaponSpec.EmpSeconds"/>).</summary>
        public double EmpSeconds, EmpRadiusKm;
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

        /// <summary>An aimed round (U.SPC-T270): no roll at the muzzle and no <see cref="Target"/>; it hits
        /// whatever hostile hull it actually flies through, or nothing (see <see cref="Gunnery"/>).</summary>
        public bool Aimed;
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

        /// <summary>A mine going off (Size: its blast, km).</summary>
        MineBlast,

        /// <summary>An EMP round bursting (Size: its reach, km).</summary>
        EmpBurst,

        /// <summary>A mine laid.</summary>
        Deployed,
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
        public readonly List<Mine> Mines = new List<Mine>();
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

        /// <summary>A gate fort joins the fight for its gate's owner (U.SPC-T92).</summary>
        public BattleUnit AddFort(JumpGate gate, ShipCombat fort, int side, DVec3 pos)
        {
            var u = NewUnit(UnitKind.Platform, side, gate.Owner, fort, CombatSpec.PlatformLengthKm, pos, DVec3.UnitX);
            u.Fort = gate;
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

        /// <summary>A step (seconds) of a battle fought at coarse detail, in a system nobody is looking at (U.SPC-T104).</summary>
        public const double CoarseStep = 15;

        /// <summary>The most coarse steps one advance takes: enough for six hours, the coarse worlds' own step.</summary>
        const int MaxCoarseStepsPerAdvance = 1600;

        /// <summary>
        /// Fights <paramref name="seconds"/> more of the battle: in steps of a second (<see cref="MaxStep"/>), or
        /// at <paramref name="coarse"/> detail (a system nobody is looking at, see <see cref="World.Coarse"/>) in
        /// steps of <see cref="CoarseStep"/>, the same fight resolved fifteen times more cheaply. A battle can go
        /// from one to the other at any step and carries on as it stands.
        /// </summary>
        public void Advance(double seconds, bool coarse = false)
        {
            if (Over || seconds <= 0) return;
            accumulator += seconds;
            double unit = coarse ? CoarseStep : MaxStep;
            int cap = coarse ? MaxCoarseStepsPerAdvance : MaxStepsPerAdvance;
            int steps = (int)Math.Ceiling(accumulator / unit - 1e-9);
            double dt = unit;
            if (steps > cap)
            {
                // Far behind (a year per second): take coarser steps, up to 20 s; drop the rest.
                dt = Math.Min(Math.Max(20, unit), accumulator / cap);
                steps = cap;
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
                if (!u.Present) continue;
                if (u.PlayerControlled) StepPlayerWeapons(u, dt);
                Fire(u, dt);
                // A mine asked for is laid now or not at all, not saved up for when the mount is ready; a volley
                // called is fired now, and the next one builds from nothing.
                if (u.PlayerControlled)
                {
                    u.PlayerMineRequested = false;
                    if (u.PlayerVolleyRelease) u.VolleyCharge = 0;
                    u.PlayerVolleyRelease = false;
                }
            }
            StepMissiles(dt);
            StepRounds(dt);
            StepMines(dt);
            StepBoarding(dt);
            StepSurrenders(dt);
            double track = 1 - Math.Exp(-dt / Gunnery.TrackingLagS);
            foreach (var u in Units)
            {
                if (!u.Present) continue;
                u.SeenVel += (u.Vel - u.SeenVel) * track;
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
            if (!u.Fleeing && Dogfights(u))
            {
                Dogfight(u, dt);
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

        // ---------------------------------------------------------------- dogfighting (U.SPC-T274)

        /// <summary>The share of a hull's own turn rate at the helm (<see cref="PilotTuning.TurnRateDegS"/>) its crew
        /// flies it at in a dogfight: a human's hands are quicker than a crew's reactions.</summary>
        public const double DogfightTurnShare = 0.6;

        /// <summary>How close the merge is, km (inside it the attacker extends), how far it extends before coming
        /// round, and how near a hostile behind must be to count as on its tail.</summary>
        public const double MergeKm = 0.3, ExtendKm = 1.6, TailKm = 2.5;

        /// <summary>How far off a dogfighter goes for the player in preference to whatever else it was after, km.</summary>
        public const double HuntPlayerKm = 8;

        /// <summary>The small craft that dogfight: the light fighters. A heavy fighter (the Breacher) closes to board.</summary>
        static bool Dogfights(BattleUnit u) =>
            u.Kind == UnitKind.Ship && u.Ship != null && (u.Ship.Spec.Class == ShipClass.Fighter || u.Ship.Spec.Class == ShipClass.Courier);

        /// <summary>
        /// A light fighter flown by its crew in a dogfight: on the same airframe the player flies (its helm turn rate,
        /// cut to <see cref="DogfightTurnShare"/>, and its own thrust), going where its nose points. It lays its nose on
        /// the lead point for its guns and jinks on the way in; through the merge it extends under boost and comes round
        /// again; with a hostile on its tail and pointed at it, it breaks hard to one side. It goes for the player first.
        /// </summary>
        void Dogfight(BattleUnit u, double dt)
        {
            var c = u.Combat;
            var cls = u.Ship.Spec.Class;
            var tune = PilotTuning.Of(cls);
            var hull = HullDynamics.Of(c.Spec);
            double engines = 0.4 + 0.6 * c.EngineFactor;
            double speed = tune.MaxSpeed * engines;
            double turn = tune.TurnRateDegS * DogfightTurnShare * engines;
            double accel = Math.Max(hull.MainAccel, 1e-3) * engines;

            // The player first, if near enough to be worth the chase.
            foreach (var o in Units)
                if (o.PlayerControlled && Hostile(u, o) && (o.Pos - u.Pos).Length < HuntPlayerKm)
                {
                    u.Target = o;
                    break;
                }
            var t = u.Target != null && u.Target.Present && Hostile(u, u.Target) ? u.Target : null;

            var threat = OnItsTail(u);
            u.ManeuverLeft -= dt;
            if (threat != null && u.Maneuver != DogfightManeuver.Break)
            {
                u.Maneuver = DogfightManeuver.Break;
                u.ManeuverLeft = 1.6 + rng.NextDouble();
                u.BreakSide = rng.Next(2) == 0 ? -1 : 1;
            }
            else if (u.ManeuverLeft <= 0 && u.Maneuver != DogfightManeuver.Attack)
                u.Maneuver = DogfightManeuver.Attack;

            u.Basis(out var right, out var up);
            DVec3 goal;
            switch (u.Maneuver)
            {
                case DogfightManeuver.Break:
                    // Hard over to one side and a little up, under boost.
                    goal = right * u.BreakSide + up * 0.35 + u.Forward * 0.2;
                    speed *= tune.BoostSpeedMultiplier;
                    break;
                case DogfightManeuver.Extend:
                    goal = u.Forward + (t != null ? (u.Pos - t.Pos).Normalized * 0.4 : DVec3.Zero);
                    speed *= tune.BoostSpeedMultiplier;
                    if (t != null && (t.Pos - u.Pos).Length > ExtendKm) u.ManeuverLeft = 0;
                    break;
                default:
                    if (t == null)
                    {
                        goal = -u.Pos;
                        speed *= 0.5;
                        break;
                    }
                    var rel = t.Pos - u.Pos;
                    double d = rel.Length;
                    double rounds = RoundSpeedOf(c);
                    var aimAt = rounds > 0 ? Gunnery.LeadPoint(u.Pos, u.Vel, t.Pos, t.Vel, rounds) : t.Pos;
                    goal = aimAt - u.Pos;
                    // A weave on the way in, so the target's gunners have a hard time of it too.
                    if (d > 0.8)
                    {
                        double w = Math.Sin(Seconds * 2.3 + u.Wander) * 0.12;
                        goal = goal.Normalized + (right * w + up * w * 0.5);
                    }
                    // On its tail, hold station rather than overrun it.
                    if (d < 1.2) speed = Math.Max(speed * 0.6, Math.Min(speed, t.Vel.Length + 0.05));
                    if (d < MergeKm)
                    {
                        u.Maneuver = DogfightManeuver.Extend;
                        u.ManeuverLeft = 2 + rng.NextDouble();
                    }
                    break;
            }
            // Never out of the fight altogether: past most of the escape radius, it turns back in.
            if (u.Pos.Length > EscapeKm * 0.8) goal = goal.Normalized + (-u.Pos).Normalized * 1.5;
            TurnToward(u, goal, turn, dt);

            var want = u.Forward * speed;
            var dv = want - u.Vel;
            double step = accel * dt;
            u.Vel += dv.Length > step ? dv.Normalized * step : dv;
            u.Pos += u.Vel * dt;
        }

        /// <summary>A hostile close behind <paramref name="u"/> with its nose on it, or null.</summary>
        BattleUnit OnItsTail(BattleUnit u)
        {
            foreach (var o in Units)
            {
                if (!Hostile(u, o) || o.Combat.Disabled || o.Stunned > 0 || !o.Combat.Armed) continue;
                var to = o.Pos - u.Pos;
                double d = to.Length;
                if (d < 1e-6 || d > TailKm) continue;
                var dir = to / d;
                if (DVec3.Dot(u.Forward, dir) < -0.3 && DVec3.Dot(o.Forward, -dir) > 0.94) return o;
            }
            return null;
        }

        /// <summary>The fastest round a hull's forward guns fire, km/s; 0 if they are beams.</summary>
        static double RoundSpeedOf(ShipCombat c)
        {
            double speed = 0;
            foreach (var s in c.Subsystems)
            {
                var w = s.Def.Weapon;
                if (s.Destroyed || w == null || w.Missile || w.AntiSmall || w.Supporting) continue;
                speed = Math.Max(speed, w.MuzzleSpeedKmS);
            }
            return speed;
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
                if (!s.Def.IsWeapon || s.Destroyed || s.Kind == SubsystemKind.PointDefense || s.Kind == SubsystemKind.BoardingPods || s.Def.Weapon.Supporting) continue;
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
            // Knocked out by a pulse or an EMP: no gun on the hull fires until its systems come back.
            if (u.Captured || u.Surrendering || u.Stunned > 0) return;
            double accuracyScale = c.BridgeIntact ? 1 : 0.6;
            foreach (var s in c.Subsystems)
            {
                var w = s.Def.Weapon;
                if (w == null || s.Destroyed) continue;
                // A fleet out of supply reloads slower: short of rounds, of spares for its mounts, of power to spare (U.SPC-T93).
                s.Cooldown -= dt * c.Readiness;
                if (s.Cooldown > 0 || s.Kind == SubsystemKind.Torpedo) continue;
                if (s.Kind == SubsystemKind.BoardingPods)
                {
                    FirePods(u, s);
                    continue;
                }
                if (w.Deploy != DeployKind.None)
                {
                    TryDeploy(u, s, w);
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
                bool helm = u.PlayerControlled && u.PlayerHelm;
                if (helm && HelmVolley(u, s, origin, w, accuracyScale)) continue;
                if (u.PlayerControlled && !helm && IsPlayerAimable(s.Def))
                {
                    // The player's own nose guns: no AI target selection, no autofire. Side/aft
                    // batteries (a broadside hull's real firepower) fall through to the branch below
                    // completely unchanged, so piloting never silences a ship's crewed turrets.
                    if (w.EmpSeconds > 0) target = u.PlayerEmpHeld ? PlayerEmpTarget(u, origin, w) : null;
                    else if (!w.Missile)
                    {
                        // Down the crosshair, hitting only what the shot really meets (U.SPC-T270).
                        if (u.PlayerFireHeld && !u.Overheated) FirePlayerGun(u, s, origin, w);
                        else s.Aim = DVec3.Zero;
                        continue;
                    }
                    else target = u.PlayerSecondaryHeld && u.LockProgress >= 1 ? PlayerLockedTarget(u, w, origin) : null;
                }
                else
                {
                    target = ChooseWeaponTarget(u, origin, w);
                    // At a capital ship's helm the crews go for the hull the player has locked, wherever it is in their arc and reach.
                    if (helm && LockedFor(u, s, origin, w) is BattleUnit locked) target = locked;
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
                            Kind = w.EmpSeconds > 0 ? MissileKind.Emp : s.Kind == SubsystemKind.Torpedo ? MissileKind.Torpedo : MissileKind.Missile,
                            EmpSeconds = w.EmpSeconds, EmpRadiusKm = w.EmpRadiusKm,
                        });
                    }
                    Emit(FxKind.MissileLaunch, origin, origin + s.Aim, 0, u.Side, u, s.Index);
                    continue;
                }

                if (target.PlayerControlled)
                {
                    // At the player's own hull every shot is aimed and real (U.SPC-T270): led, late on a target
                    // crossing the gunner's sights and scattered by his skill, it hits only if it meets the hull.
                    var dir = AimAt(u, origin, target, aimPoint, w, accuracyScale);
                    s.Aim = dir;
                    ResolveShot(u, s, origin, dir, w);
                    continue;
                }

                double dist = (aimPoint - origin).Length;
                double size = Math.Max(0.15, Math.Min(1, target.LengthKm / 0.4));
                if (target.Kind == UnitKind.Fighter) size *= w.AntiSmall ? 1.4 : 0.35;
                double chance = w.Accuracy * accuracyScale * size * (1 - 0.4 * dist / w.RangeKm);
                bool hit = rng.NextDouble() < chance;
                var kind = s.Kind == SubsystemKind.FighterGuns || s.Kind == SubsystemKind.Autocannon || s.Kind == SubsystemKind.AutoTurret ? FxKind.Guns
                    : w.AntiSmall ? FxKind.PointDefense
                    : s.Kind == SubsystemKind.Railgun || s.Kind == SubsystemKind.LightRailgun ? (hit ? FxKind.Railgun : FxKind.RailgunMiss)
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
                Railgun = mount.Kind == SubsystemKind.Railgun || mount.Kind == SubsystemKind.LightRailgun,
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
                if (r.Aimed)
                {
                    StepAimedRound(r, dt);
                    continue;
                }
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
        // Nor is an auto turret (U.SPC-T278): its own fire control works it, whoever flies the hull.
        static bool IsPlayerAimable(SubsystemDef d) =>
            !(d.Weapon != null && d.Weapon.AntiSmall) && d.Kind != SubsystemKind.AutoTurret && Math.Abs(Astro.WrapDegrees360(d.ArcCenterDeg)) < 0.01;

        /// <summary>
        /// One of the player's guns fires down the crosshair (U.SPC-T270): swung off the nose as far as its
        /// gimbal lets it (<see cref="Gunnery.Gimbal"/>), converged on whatever hostile the line of sight meets
        /// (else at the gun's range) so guns spread along the hull all land where the crosshair is, and
        /// building heat. What it hits is <see cref="ResolveShot"/>'s to decide.
        /// </summary>
        void FirePlayerGun(BattleUnit u, Subsystem s, DVec3 origin, WeaponSpec w)
        {
            var aim = u.PlayerAim.LengthSquared > 1e-12 ? u.PlayerAim.Normalized : u.Forward;
            var sight = Gunnery.Gimbal(aim, u.Forward, s.Kind == SubsystemKind.Railgun || s.Kind == SubsystemKind.LightRailgun ? Gunnery.RailgunGimbalDeg : Gunnery.GimbalDeg);
            double converge = FirstHostileAlong(u.Side, u.Pos, sight, w.RangeKm, out _);
            if (converge < 0) converge = w.RangeKm;
            var toConverge = u.Pos + sight * converge - origin;
            var dir = toConverge.LengthSquared > 1e-12 ? toConverge.Normalized : sight;
            double scatter = w.MuzzleSpeedKmS > 0 && s.Kind != SubsystemKind.Railgun && s.Kind != SubsystemKind.LightRailgun ? Gunnery.AutocannonScatterDeg : Gunnery.BeamScatterDeg;
            dir = Gunnery.Scatter(dir, scatter * Astro.Deg2Rad, rng);
            s.Aim = dir;
            // The player's trigger keeps the gun's own rhythm, not a crew's jittered one.
            s.Cooldown = w.Cooldown;
            u.Heat += w.Damage * Gunnery.HeatPerDamage;
            if (u.Heat >= 1)
            {
                u.Heat = 1;
                u.Overheated = true;
            }
            ResolveShot(u, s, origin, dir, w);
        }

        /// <summary>
        /// Where a gunner lays his gun on the player's hull: led for the round's flight (none for a beam), but
        /// trailing a target that crosses his sights by a reaction lag, and scattered, both less the better the
        /// gun (<see cref="WeaponSpec.Accuracy"/>, cut by a lost bridge). A hull flying straight is hit; one
        /// turning, jinking or sliding across the line is missed.
        /// </summary>
        DVec3 AimAt(BattleUnit u, DVec3 origin, BattleUnit target, DVec3 aimPoint, WeaponSpec w, double accuracyScale)
        {
            double skill = Math.Max(0, Math.Min(1, w.Accuracy * accuracyScale));
            // Where he sees it: carried on from a moment ago at the velocity he has read it as having, so a hull that
            // has just changed course is not where he thinks.
            var seen = aimPoint - (target.Vel - target.SeenVel) * (Gunnery.TrackingLagS * (1 - 0.5 * skill));
            var point = w.MuzzleSpeedKmS > 0 ? Gunnery.LeadPoint(origin, u.Vel, seen, target.SeenVel, w.MuzzleSpeedKmS) : seen;
            var to = point - origin;
            var dir = to.LengthSquared > 1e-12 ? to.Normalized : u.Forward;
            return Gunnery.Scatter(dir, Gunnery.MaxScatterDeg * (1 - skill) * Astro.Deg2Rad, rng);
        }

        /// <summary>
        /// An aimed shot from <paramref name="origin"/> down <paramref name="dir"/>: a beam lands at once on the
        /// first hostile hull it meets within range, a round flies off to hit whatever it meets on the way
        /// (<see cref="StepAimedRound"/>). Either hits the hardpoint nearest where it struck.
        /// </summary>
        void ResolveShot(BattleUnit u, Subsystem s, DVec3 origin, DVec3 dir, WeaponSpec w, double damageScale = 1)
        {
            bool beam = w.MuzzleSpeedKmS <= 0;
            double damage = w.Damage * damageScale;
            if (!beam)
            {
                bool slug = s.Kind == SubsystemKind.Railgun || s.Kind == SubsystemKind.LightRailgun;
                Emit(slug ? FxKind.Railgun : FxKind.Guns, origin, origin + dir * w.RangeKm, 0, u.Side, null, -1);
                Rounds.Add(new KineticRound
                {
                    Pos = origin, Vel = u.Vel + dir * w.MuzzleSpeedKmS, Damage = damage, Life = w.RangeKm / w.MuzzleSpeedKmS,
                    Side = u.Side, Railgun = slug, Aimed = true,
                });
                return;
            }
            double t = FirstHostileAlong(u.Side, origin, dir, w.RangeKm, out var hit);
            if (hit == null)
            {
                Emit(w.AntiSmall ? FxKind.PointDefense : FxKind.BeamMiss, origin, origin + dir * w.RangeKm, 0, u.Side, null, -1);
                return;
            }
            var at = origin + dir * t;
            var sub = SubsystemNear(hit, at);
            Emit(w.AntiSmall ? FxKind.PointDefense : FxKind.Beam, origin, at, damage, u.Side, hit, sub?.Index ?? -1);
            ApplyDamage(hit, sub, damage, at);
        }

        /// <summary>An aimed round flies on, and hits the first hostile hull its path crosses this step.</summary>
        void StepAimedRound(KineticRound r, double dt)
        {
            var step = r.Vel * dt;
            double len = step.Length;
            r.Life -= dt;
            if (len > 1e-12)
            {
                double t = FirstHostileAlong(r.Side, r.Pos, step / len, len, out var hit);
                if (hit != null)
                {
                    var at = r.Pos + step / len * t;
                    r.Gone = true;
                    ApplyDamage(hit, SubsystemNear(hit, at), r.Damage, at);
                    return;
                }
            }
            r.Pos += step;
            if (r.Life <= 0) r.Gone = true;
        }

        /// <summary>How far down a line of fire the first hull hostile to <paramref name="side"/> lies, within
        /// <paramref name="maxKm"/>; -1 (and no <paramref name="hit"/>) if the line is clear.</summary>
        double FirstHostileAlong(int side, DVec3 origin, DVec3 dir, double maxKm, out BattleUnit hit)
        {
            hit = null;
            double best = -1;
            foreach (var o in Units)
            {
                if (o.Side == side || !o.Present) continue;
                double t = Gunnery.RaySphere(origin, dir, maxKm, o.Pos, Gunnery.HitRadius(o));
                if (t >= 0 && (best < 0 || t < best))
                {
                    best = t;
                    hit = o;
                }
            }
            return best;
        }

        /// <summary>The hardpoint nearest where a shot struck, if one is close enough to have taken it (within a
        /// sixth of the hull's length); otherwise the shot is to the hull.</summary>
        static Subsystem SubsystemNear(BattleUnit target, DVec3 at)
        {
            if (target.Kind == UnitKind.Fighter) return null;
            Subsystem best = null;
            double bestD = target.LengthKm / 6;
            foreach (var s in target.Combat.Subsystems)
            {
                if (s.Destroyed) continue;
                double d = (target.PointOf(s.Def) - at).Length;
                if (d < bestD)
                {
                    bestD = d;
                    best = s;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- broadsides at the helm (U.SPC-T272)

        /// <summary>Seconds a volley takes to build to full, the extra damage a full one does (as a share of each
        /// gun's own), and how much surer its gunners' aim is than their aim at will.</summary>
        public const double VolleyBuildSeconds = 1.5, VolleyDamageBonus = 0.5, VolleyAccuracyBonus = 0.25;

        /// <summary>How far off the line the player is looking a volley will take a target, deg.</summary>
        public const double VolleyConeDeg = 25;

        /// <summary>
        /// A mount at a capital ship's helm that bears on the side the player is looking: held, its gun waits on the
        /// volley; let go, every such gun that is loaded fires together at the hostile nearest that line (the locked one
        /// if it is near it), harder and surer the longer the volley was built. True if the mount was the volley's.
        /// </summary>
        bool HelmVolley(BattleUnit u, Subsystem s, DVec3 origin, WeaponSpec w, double accuracyScale)
        {
            if (w.Missile || w.AntiSmall || w.Supporting || u.PlayerVolleyAim.LengthSquared < 1e-12) return false;
            var look = u.PlayerVolleyAim.Normalized;
            if (!s.Def.ContainsYaw(u.YawDegOf(u.Pos + look))) return false;
            if (u.PlayerVolleyRelease)
            {
                var target = VolleyTarget(u, look, w.RangeKm);
                if (target == null) return false;
                double built = Math.Min(1, u.VolleyCharge / VolleyBuildSeconds);
                var sub = PickSubsystem(target);
                var aimPoint = sub != null ? target.PointOf(sub.Def) : target.Pos;
                var dir = AimAt(u, origin, target, aimPoint, w, accuracyScale * (1 + VolleyAccuracyBonus * built));
                s.Aim = dir;
                s.Cooldown = w.Cooldown;
                ResolveShot(u, s, origin, dir, w, 1 + VolleyDamageBonus * built);
                return true;
            }
            if (!u.PlayerFireHeld) return false;
            s.Aim = look;
            return true;
        }

        /// <summary>What a volley called along <paramref name="look"/> is fired at: the locked hull if it lies near that
        /// line, else the hostile nearest it within <see cref="VolleyConeDeg"/> and <paramref name="rangeKm"/>.</summary>
        BattleUnit VolleyTarget(BattleUnit u, DVec3 look, double rangeKm)
        {
            double cone = Math.Cos(VolleyConeDeg * Astro.Deg2Rad);
            BattleUnit best = null;
            double bestCos = cone;
            foreach (var o in Units)
            {
                if (!Hostile(u, o) || o.Surrendering || o.Captured) continue;
                var to = o.Pos - u.Pos;
                double d = to.Length;
                if (d < 1e-6 || d - o.Radius > rangeKm) continue;
                double cos = DVec3.Dot(look, to / d);
                if (o == u.PlayerLockTarget && cos > cone) return o;
                if (cos > bestCos)
                {
                    bestCos = cos;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>The player's locked hull, if this mount can reach it and it lies in the mount's arc.</summary>
        BattleUnit LockedFor(BattleUnit u, Subsystem s, DVec3 origin, WeaponSpec w)
        {
            var t = u.PlayerLockTarget;
            if (t == null || !Hostile(u, t) || t.Surrendering || t.Captured) return null;
            if ((t.Pos - origin).Length - t.Radius > w.RangeKm) return null;
            if (w.AntiSmall && t.Kind != UnitKind.Fighter) return null;
            return s.Def.ArcWidthDeg >= 360 || s.Def.ContainsYaw(u.YawDegOf(t.Pos)) ? t : null;
        }

        // ---------------------------------------------------------------- the dogfight arsenal (U.SPC-T271)

        /// <summary>How long a mine takes to arm once laid, and how long it lies waiting, seconds.</summary>
        public const double MineArmSeconds = 1.5, MineLifeSeconds = 240;

        /// <summary>How near a mine a hostile hull must come to set it off, km beyond the hull's own size.</summary>
        public const double MineTriggerKm = 0.12;

        /// <summary>Lays a mine astern when the mount's owner wants one: the player when asked, a crew when something
        /// is on its tail.</summary>
        void TryDeploy(BattleUnit u, Subsystem s, WeaponSpec w)
        {
            bool want = u.PlayerControlled ? u.PlayerMineRequested : SomethingOnItsTail(u);
            if (!want || s.Ammo <= 0 || u.Stunned > 0) return;
            s.Ammo--;
            s.Cooldown = w.Cooldown;
            var at = u.Pos - u.Forward * (u.Radius + 0.04);
            Mines.Add(new Mine { Pos = at, Side = u.Side, Damage = w.Damage, BlastKm = w.RangeKm, ArmIn = MineArmSeconds, Life = MineLifeSeconds });
            Emit(FxKind.Deployed, at, at, 0.02, u.Side, u, s.Index);
        }

        /// <summary>An armed hostile close astern: what a crew lays a mine for.</summary>
        bool SomethingOnItsTail(BattleUnit u)
        {
            foreach (var o in Units)
            {
                if (!Hostile(u, o) || o.Captured || o.Surrendering || !o.Combat.Armed) continue;
                var to = o.Pos - u.Pos;
                double d = to.Length;
                if (d < 2.5 && d > 1e-6 && DVec3.Dot(u.Forward, to / d) < -0.3) return true;
            }
            return false;
        }

        /// <summary>What the player's EMP launcher fires at: the locked hull if it is in reach, else the hostile nearest
        /// the crosshair (within 25 degrees of it) that is not already drifting dead.</summary>
        BattleUnit PlayerEmpTarget(BattleUnit u, DVec3 origin, WeaponSpec w)
        {
            var locked = PlayerLockedTarget(u, w, origin);
            if (locked != null) return locked;
            var aim = u.PlayerAim.LengthSquared > 1e-12 ? u.PlayerAim.Normalized : u.Forward;
            BattleUnit best = null;
            double bestCos = Math.Cos(25 * Astro.Deg2Rad);
            foreach (var o in Units)
            {
                if (!Hostile(u, o) || o.Stunned > 0) continue;
                var to = o.Pos - origin;
                double d = to.Length;
                if (d < 1e-6 || d - o.Radius > w.RangeKm) continue;
                double cos = DVec3.Dot(aim, to / d);
                if (cos > bestCos)
                {
                    bestCos = cos;
                    best = o;
                }
            }
            return best;
        }

        /// <summary>The stun an EMP burst leaves a hull of <paramref name="lengthKm"/>: the full
        /// <paramref name="seconds"/> for a small craft, less for a bigger hull with more to knock out.</summary>
        public static double EmpStunSeconds(double seconds, double lengthKm) => seconds * Math.Min(1, 0.2 / Math.Max(lengthKm, 1e-6));

        /// <summary>An EMP round bursts: every hull hostile to <paramref name="side"/> within its reach has its drives and
        /// guns knocked out (<see cref="BattleUnit.Stunned"/>) and its shields halved.</summary>
        public void EmpBurst(DVec3 at, int side, double seconds, double radiusKm)
        {
            Emit(FxKind.EmpBurst, at, at, radiusKm, side, null, -1);
            foreach (var o in Units)
            {
                if (o.Side == side || !o.Present) continue;
                if ((o.Pos - at).Length > radiusKm + Gunnery.HitRadius(o)) continue;
                o.Stunned = Math.Max(o.Stunned, EmpStunSeconds(seconds, o.LengthKm));
                foreach (var g in o.Combat.Subsystems)
                    if (g.Kind == SubsystemKind.ShieldGenerator) g.Shield *= 0.5;
            }
        }

        /// <summary>Mines arm, wait and run out; an armed one with a hostile hull inside its trigger blows, hurting
        /// everything hostile in its blast, harder the nearer the centre.</summary>
        void StepMines(double dt)
        {
            foreach (var m in Mines)
            {
                if (m.Gone) continue;
                m.Pos += m.Vel * dt;
                m.ArmIn -= dt;
                m.Life -= dt;
                if (m.Life <= 0)
                {
                    m.Gone = true;
                    continue;
                }
                if (!m.Armed) continue;
                bool tripped = false;
                foreach (var o in Units)
                    if (o.Side != m.Side && o.Present && (o.Pos - m.Pos).Length <= MineTriggerKm + Gunnery.HitRadius(o))
                    {
                        tripped = true;
                        break;
                    }
                if (!tripped) continue;
                m.Gone = true;
                Emit(FxKind.MineBlast, m.Pos, m.Pos, m.BlastKm, m.Side, null, -1);
                for (int i = 0; i < Units.Count; i++)
                {
                    var o = Units[i];
                    if (o.Side == m.Side || !o.Present) continue;
                    double d = Math.Max(0, (o.Pos - m.Pos).Length - Gunnery.HitRadius(o));
                    if (d > m.BlastKm) continue;
                    ApplyDamage(o, null, m.Damage * (1 - d / m.BlastKm), m.Pos);
                }
            }
            Mines.RemoveAll(m => m.Gone);
        }

        /// <summary>
        /// The player's guns cool and the missile lock builds or slips (U.SPC-T270): the lock grows while the
        /// crosshair sits on the locked target and, once made, holds while the target stays ahead of the nose.
        /// </summary>
        void StepPlayerWeapons(BattleUnit u, double dt)
        {
            u.Heat = Math.Max(0, u.Heat - Gunnery.HeatCoolPerS * dt);
            if (u.PlayerHelm && u.PlayerFireHeld) u.VolleyCharge = Math.Min(VolleyBuildSeconds, u.VolleyCharge + dt);
            if (u.Overheated && u.Heat < Gunnery.HeatRecoverBelow) u.Overheated = false;

            var t = u.PlayerLockTarget;
            if (t == null || !t.Present || !Hostile(u, t))
            {
                u.LockProgress = 0;
                return;
            }
            var to = t.Pos - u.Pos;
            if (to.LengthSquared < 1e-12) return;
            var dir = to.Normalized;
            var aim = u.PlayerAim.LengthSquared > 1e-12 ? u.PlayerAim.Normalized : u.Forward;
            if (u.LockProgress >= 1)
            {
                if (DVec3.Dot(u.Forward, dir) < Math.Cos(Gunnery.LockHoldDeg * Astro.Deg2Rad)) u.LockProgress = 0;
                return;
            }
            bool onIt = DVec3.Dot(aim, dir) >= Math.Cos(Gunnery.LockConeDeg * Astro.Deg2Rad);
            u.LockProgress = onIt ? Math.Min(1, u.LockProgress + dt / Gunnery.LockSeconds) : Math.Max(0, u.LockProgress - 2 * dt / Gunnery.LockSeconds);
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
            bool emp = w.EmpSeconds > 0;
            if (t != null && Hostile(u, t) && !t.Surrendering && !t.Captured && (t.Pos - origin).Length - t.Radius <= w.RangeKm && !(t.Kind == UnitKind.Fighter && w.Missile && !emp) && !(emp && t.Stunned > 0))
                return t;
            BattleUnit any = null;
            double nearest = w.RangeKm;
            foreach (var o in Units)
            {
                // An EMP round is for a small craft too, but wasted on one already drifting dead (U.SPC-T271).
                // An auto turret's fire control takes fighters as readily as hulls (U.SPC-T278).
                bool takesFighters = emp || u.Kind == UnitKind.Fighter || ReferenceEquals(w, WeaponSpec.AutoTurret);
                if (!Hostile(u, o) || o.Captured || o.Surrendering || (emp && o.Stunned > 0) || (o.Kind == UnitKind.Fighter && (w.Missile && !emp || !takesFighters))) continue;
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
                    case SubsystemKind.Autocannon:
                    case SubsystemKind.LightRailgun:
                    case SubsystemKind.AutoTurret:
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
                    else if (m.Kind == MissileKind.Emp) EmpBurst(m.Pos, m.Side, m.EmpSeconds, m.EmpRadiusKm);
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
            {
                if (o.Leader == u) o.Leader = null;
                // Nobody keeps steering on it as it was: its captors' side re-picks now rather than circling its own prize.
                if (o.Target == u) o.Target = null;
            }
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
                Morale.Resolve(u.Faction, u.Ship != null && u.Ship.Spec.IsMilitary) * (u.Side >= 0 && u.Side < 2 ? Dread[u.Side] : 1) * Morale.SupplyResolve(u.Combat.Readiness),
                u.MoraleThreshold);

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
