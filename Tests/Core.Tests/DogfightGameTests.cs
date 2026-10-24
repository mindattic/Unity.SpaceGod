using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The dogfighting game (U.SPC-T424): fighters, heavy fighters and bombers as classes of their own on their own flight
    /// models, weapon groups, lock-on missiles, countermeasures, power shared between weapons, shields and engines, orders
    /// to wingmen, and attack runs on capital-ship weak points that let a lone small craft bring down a battleship.
    /// </summary>
    [TestFixture]
    public class DogfightGameTests
    {
        const double StartJd = 2461324.0;

        static double AngleDeg(DVec3 a, DVec3 b) => Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(a.Normalized, b.Normalized)))) * Astro.Rad2Deg;

        static double Health(BattleUnit u) => u.Combat.Hull + u.Combat.Subsystems.Sum(s => Math.Max(0, s.Hp) + s.Shield);

        static Ship Hull(ShipClass c, Faction f, int id) => new Ship
        {
            Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c),
        };

        /// <summary>A carrier and its three squadrons against a battleship (and, with <paramref name="enemyCarrier"/>, a carrier of its own).</summary>
        static (Battle b, BattleUnit carrier, BattleUnit enemy) Fight(int seed = 5, bool enemyCarrier = false)
        {
            var blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
            var red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed) { Launch = FighterAI.Launch, FighterStep = FighterAI.Step };
            var carrier = b.AddShip(Hull(ShipClass.Carrier, blue, 0), 0, new DVec3(14, 0, 0), -DVec3.UnitX);
            var enemy = b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(-14, 0, 0), DVec3.UnitX);
            if (enemyCarrier) b.AddShip(Hull(ShipClass.Carrier, red, 2), 1, new DVec3(-14, 6, 0), DVec3.UnitX);
            return (b, carrier, enemy);
        }

        static BattleUnit LeadOf(Battle b, int squadron, int side = 0) =>
            b.Units.First(u => u.Kind == UnitKind.Fighter && u.Side == side && u.Fighter.Squadron == squadron && u.Fighter.Lead == null);

        /// <summary>Silences every gun in the fight and puts every craft back aboard but <paramref name="keep"/>.</summary>
        static void Duel(Battle b, params BattleUnit[] keep)
        {
            foreach (var u in b.Units)
            {
                if (u.Kind == UnitKind.Fighter)
                {
                    if (!keep.Contains(u)) u.Docked = true;
                    continue;
                }
                foreach (var s in u.Combat.Subsystems)
                    if (s.Def.IsWeapon) s.Hp = 0;
            }
        }

        static (FighterSeat seat, EmbodimentSession session) Take(Battle b, BattleUnit craft)
        {
            var seat = Embodiables.FighterOf(b, craft);
            var session = new EmbodimentSession();
            Assert.That(session.Switch(seat), Is.True);
            return (seat, session);
        }

        static void Fly(Battle b, EmbodimentSession session, Func<DVec3> aim, double seconds, bool guns = false, bool secondary = false, double throttle = 0, bool boost = false)
        {
            for (double t = 0; t < seconds && !b.Over; t += 0.1)
            {
                session.Step(new EmbodimentInput { Aim = aim(), Throttle = throttle, Primary = guns, Secondary = secondary, Boost = boost }, 0.1);
                b.Advance(0.1);
            }
        }

        // ------------------------------------------------------------ craft classes and their flight models

        [Test]
        public void ACarrierLaunchesInterceptorsFightersAndBombersEachItsOwnClass()
        {
            var (b, _, _) = Fight();
            var aloft = b.Units.Where(u => u.Kind == UnitKind.Fighter).ToList();
            Assert.That(aloft.Count, Is.EqualTo(20), "three squadrons and the recon pair (U.SPC-T485)");
            Assert.That(aloft.Where(u => u.Fighter.Squadron == 0).All(u => u.Fighter.Role == FighterRole.Interceptor), Is.True);
            Assert.That(aloft.Where(u => u.Fighter.Squadron == 1).All(u => u.Fighter.Role == FighterRole.SpaceSuperiority), Is.True);
            Assert.That(aloft.Where(u => u.Fighter.Squadron == 2).All(u => u.Fighter.Role == FighterRole.Bomber && u.Fighter.Bomber), Is.True);
            foreach (var u in aloft)
            {
                var craft = FighterCraft.Of(u.Fighter.Role);
                Assert.That(u.Combat.Spec, Is.SameAs(craft.Spec));
                Assert.That(u.LengthKm, Is.EqualTo(craft.LengthKm));
                Assert.That(u.Power, Is.Not.Null, "every craft shares its reactor");
                // A real hull: its main drive pushes it at its design acceleration.
                Assert.That(HullDynamics.Of(craft.Spec).MainAccel, Is.EqualTo(craft.Spec.Accel).Within(1e-9));
            }
            Assert.That(FighterCraft.Interceptor.Model, Is.InstanceOf<LightFighterFlightModel>(), "an interceptor flies Star Conflict style");
            Assert.That(FighterCraft.Interceptor.Family, Is.EqualTo(FlightFamily.Interceptor));
            Assert.That(FighterCraft.SpaceSuperiority.Family, Is.EqualTo(FlightFamily.LightFighter));
            Assert.That(FighterCraft.HeavyFighter.Family, Is.EqualTo(FlightFamily.HeavyFighter));
            Assert.That(FighterCraft.Bomber.Family, Is.EqualTo(FlightFamily.Bomber));
            Assert.That(FighterCraft.Bomber.Model, Is.InstanceOf<BomberFlightModel>());
            Assert.That(FighterCraft.HeavyFighter.Model, Is.SameAs(FlightModelRegistry.Default.ModelOf(FlightFamily.HeavyFighter)));
        }

        [Test]
        public void AtTheStickAnInterceptorOutTurnsAFighterAndCarriesItsRollAndStrafe()
        {
            double Turned(int squadron)
            {
                var (b, _, _) = Fight();
                var craft = LeadOf(b, squadron);
                Duel(b, craft);
                var (_, session) = Take(b, craft);
                var start = craft.Forward;
                var goal = PilotFlight.RotateAbout(start, DVec3.UnitZ, 120 * Astro.Deg2Rad);
                Fly(b, session, () => goal, 0.5, throttle: 0);
                return AngleDeg(craft.Forward, start);
            }
            double light = Turned(0), heavy = Turned(1);
            TestContext.Out.WriteLine($"half a second: interceptor {light:0.0} deg, fighter {heavy:0.0} deg");
            Assert.That(light, Is.GreaterThan(heavy + 5), "the interceptor turns the tighter");

            var (fb, _, _) = Fight();
            var fighter = LeadOf(fb, 0);
            Duel(fb, fighter);
            var (seat, s) = Take(fb, fighter);
            var heading = fighter.Forward;
            for (int i = 0; i < 20; i++)
            {
                s.Step(new EmbodimentInput { Aim = heading, StrafeRight = 1 }, 0.1);
                fb.Advance(0.1);
            }
            var right = DVec3.Cross(fighter.Forward, seat.PoseAt(StartJd).Up).Normalized;
            Assert.That(DVec3.Dot(fighter.Vel, right), Is.GreaterThan(0.02), "flight assist strafes it sideways");
            var upBefore = seat.PoseAt(StartJd).Up;
            for (int i = 0; i < 5; i++)
            {
                s.Step(new EmbodimentInput { Aim = heading, Roll = 1 }, 0.1);
                fb.Advance(0.1);
            }
            Assert.That(AngleDeg(seat.PoseAt(StartJd).Up, upBefore), Is.GreaterThan(30), "it rolled");
        }

        [Test]
        public void PowerToTheEnginesIsSpeedAndItCanOnlyGoSoFar()
        {
            double TopSpeed(bool engines)
            {
                var (b, _, _) = Fight();
                var craft = LeadOf(b, 0);
                Duel(b, craft);
                var (seat, session) = Take(b, craft);
                if (engines)
                {
                    Assert.That(seat.ShiftPower(PowerSystem.Engines), Is.True);
                    Assert.That(seat.ShiftPower(PowerSystem.Engines), Is.True);
                    Assert.That(seat.ShiftPower(PowerSystem.Engines), Is.False, "four pips is all one system takes");
                    Assert.That(craft.Power[PowerSystem.Engines], Is.EqualTo(PowerDistribution.MaxPips));
                    Assert.That(craft.Power[PowerSystem.Weapons] + craft.Power[PowerSystem.Shields], Is.EqualTo(2));
                }
                var heading = craft.Forward;
                Fly(b, session, () => heading, 20, throttle: 1);
                return craft.Vel.Length;
            }
            double even = TopSpeed(false), fast = TopSpeed(true);
            Assert.That(even, Is.EqualTo(FighterAI.Interceptor.MaxSpeed).Within(1e-6));
            Assert.That(fast, Is.EqualTo(FighterAI.Interceptor.MaxSpeed * 1.5).Within(1e-6));

            var p = new PowerDistribution();
            Assert.That(p.Factor(PowerSystem.Weapons), Is.EqualTo(1));
            Assert.That(p.Set(4, 2, 0), Is.True);
            Assert.That(p.Factor(PowerSystem.Weapons), Is.EqualTo(1.5));
            Assert.That(p.Factor(PowerSystem.Engines), Is.EqualTo(0.5));
            Assert.That(p.Set(5, 1, 0), Is.False);
            p.Balance();
            Assert.That(p.IsEven, Is.True);
        }

        [Test]
        public void PowerToTheShieldsRechargesThemFaster()
        {
            double Recharged(bool shields)
            {
                var (b, _, _) = Fight();
                var craft = LeadOf(b, 0);
                Duel(b, craft);
                var (seat, session) = Take(b, craft);
                if (shields) seat.ShiftPower(PowerSystem.Shields);
                craft.Combat.ZeroAllShields();
                Fly(b, session, () => craft.Forward, 3);
                return craft.Combat.Shield;
            }
            Assert.That(Recharged(true), Is.EqualTo(Recharged(false) * 1.25).Within(1e-6));
        }

        // ------------------------------------------------------------ weapon groups, lock-on and missiles

        [Test]
        public void EachClassCarriesItsOwnWeaponGroupsOnTheSecondTrigger()
        {
            var (b, _, _) = Fight();
            Assert.That(WeaponGroups.Secondaries(LeadOf(b, 0)), Is.EqualTo(new[] { WeaponGroup.Missiles }));
            Assert.That(WeaponGroups.Secondaries(LeadOf(b, 1)), Is.EqualTo(new[] { WeaponGroup.Missiles }));
            Assert.That(WeaponGroups.Secondaries(LeadOf(b, 2)), Is.EqualTo(new[] { WeaponGroup.Torpedoes }));
            Assert.That(WeaponGroups.Ammo(LeadOf(b, 1), WeaponGroup.Missiles), Is.EqualTo(2 * WeaponSpec.DogfightMissile.Ammo));
            var (seat, _) = Take(b, LeadOf(b, 2));
            Assert.That(seat.SecondaryGroup, Is.EqualTo(WeaponGroup.Torpedoes), "a bomber's second trigger starts on its torpedoes");
            Assert.That(seat.CycleSecondaryGroup(), Is.EqualTo(WeaponGroup.Torpedoes), "it has no other group");
        }

        /// <summary>Blue's interceptor lead and a red fighter drifting dead (its systems knocked out) a little way ahead of it.</summary>
        static (Battle b, BattleUnit craft, BattleUnit bandit) Bandit(double aheadKm = 1.8)
        {
            var (b, _, _) = Fight(enemyCarrier: true);
            var craft = LeadOf(b, 0);
            var bandit = LeadOf(b, 0, side: 1);
            Duel(b, craft, bandit);
            craft.Vel = DVec3.Zero;
            bandit.Pos = craft.Pos + craft.Forward * aheadKm;
            bandit.Vel = DVec3.Zero;
            bandit.Stunned = 1e6;
            return (b, craft, bandit);
        }

        [Test]
        public void ALockTakenOnTheBanditNearestTheCrosshairBuildsAndTheMissilesRunItDown()
        {
            var (b, craft, bandit) = Bandit();
            var (seat, session) = Take(b, craft);
            Assert.That(seat.SecondaryGroup, Is.EqualTo(WeaponGroup.Missiles));
            var aim = (bandit.Pos - craft.Pos).Normalized;
            Assert.That(seat.CycleLock(aim), Is.EqualTo(bandit), "the hostile nearest the crosshair");
            Assert.That(craft.LockProgress, Is.EqualTo(0));

            int ammo = WeaponGroups.Ammo(craft, WeaponGroup.Missiles);
            // The trigger held before the lock is made fires nothing.
            Fly(b, session, () => (bandit.Pos - craft.Pos).Normalized, 0.5, secondary: true);
            Assert.That(WeaponGroups.Ammo(craft, WeaponGroup.Missiles), Is.EqualTo(ammo), "no missile before the lock");
            Fly(b, session, () => (bandit.Pos - craft.Pos).Normalized, Gunnery.LockSeconds + 0.5);
            Assert.That(craft.LockProgress, Is.EqualTo(1), "held on the crosshair, the lock is made");
            Assert.That(seat.Readout(), Does.Contain("LOCKED"));

            double before = Health(bandit);
            Fly(b, session, () => (bandit.Pos - craft.Pos).Normalized, 0.3, secondary: true);
            Assert.That(WeaponGroups.Ammo(craft, WeaponGroup.Missiles), Is.LessThan(ammo), "the second trigger looses a missile");
            var missile = b.Missiles.First(m => m.Target == bandit);
            Assert.That(missile.SmallCraft, Is.True);
            Fly(b, session, () => (bandit.Pos - craft.Pos).Normalized, 8);
            Assert.That(bandit.Dead || Health(bandit) < before, Is.True, "the missile ran the fighter down");
        }

        [Test]
        public void AFlareDrawsTheMissileOffAndItGoesOffHarmlessly()
        {
            double saved = Countermeasures.Effectiveness.Value;
            try
            {
                Countermeasures.Effectiveness.Set(1);
                var (b, craft, bandit) = Bandit(aheadKm: 2.5);
                var (seat, session) = Take(b, craft);
                int flares = seat.Countermeasures;
                var missile = new Missile
                {
                    Pos = bandit.Pos, Vel = DVec3.Zero, Target = craft, Damage = 70, Speed = 1.1, Hp = 12, Life = 30, Side = bandit.Side,
                    Kind = MissileKind.Missile, SmallCraft = true,
                };
                b.Missiles.Add(missile);
                Assert.That(seat.MissileWarning, Is.True);
                Assert.That(seat.Readout(), Does.Contain("MISSILE"));
                double before = Health(craft);
                seat.DropCountermeasures();
                Fly(b, session, () => craft.Forward, 0.1);
                Assert.That(seat.Countermeasures, Is.EqualTo(flares - 1), "a cloud thrown");
                Assert.That(missile.Decoy, Is.Not.Null, "the missile is drawn off");
                Assert.That(b.Decoys, Is.Not.Empty);
                Fly(b, session, () => craft.Forward, 6);
                Assert.That(missile.Gone, Is.True);
                Assert.That(Health(craft), Is.EqualTo(before).Within(1e-6), "it went off in the cloud, not on the craft");
                Assert.That(b.Decoys, Is.Empty, "the cloud burnt out");
            }
            finally
            {
                Countermeasures.Effectiveness.Set(saved);
            }
        }

        [Test]
        public void APilotThrowsFlaresOnItsOwnAtAMissileClosingOnIt()
        {
            var (b, _, _) = Fight(enemyCarrier: true);
            var pilot = LeadOf(b, 0, side: 1);
            int flares = pilot.Combat.Subsystems.Where(s => s.Kind == SubsystemKind.Countermeasures).Sum(s => s.Ammo);
            b.Missiles.Add(new Missile
            {
                Pos = pilot.Pos + new DVec3(1.0, 0, 0), Vel = DVec3.Zero, Target = pilot, Damage = 70, Speed = 1.1, Hp = 1000, Life = 30, Side = 0,
                // Too tough for its own carrier's point defence to stop before the pilot reacts: what is tested is the pilot.
                Kind = MissileKind.Missile,
            });
            b.Advance(1);
            Assert.That(pilot.Combat.Subsystems.Where(s => s.Kind == SubsystemKind.Countermeasures).Sum(s => s.Ammo), Is.LessThan(flares));
            Assert.That(pilot.Power[PowerSystem.Shields], Is.EqualTo(PowerDistribution.MaxPips), "and puts its power into its shields");
        }

        [Test]
        public void AiFightersFireTheirMissilesAtEnemyFighters()
        {
            var (b, _, _) = Fight(seed: 7, enemyCarrier: true);
            bool atFighter = false;
            for (int i = 0; i < 600 && !atFighter && !b.Over; i++)
            {
                b.Advance(1);
                atFighter = b.Missiles.Any(m => m.SmallCraft && m.Target != null && m.Target.Kind == UnitKind.Fighter);
            }
            Assert.That(atFighter, Is.True, "a dogfight missile's seeker takes a fighter");
        }

        // ------------------------------------------------------------ wingmen

        [Test]
        public void WingmenTakeTheirLeadsOrders()
        {
            var (b, _, enemy) = Fight();
            // The player takes a wingman's seat: the first order makes it the squadron's lead.
            var craft = b.Units.First(u => u.Kind == UnitKind.Fighter && u.Fighter.Squadron == 0 && u.Fighter.Slot == 2);
            var (seat, session) = Take(b, craft);
            seat.Lock(enemy);
            Assert.That(seat.OrderWing(WingOrder.AttackMyTarget), Is.EqualTo(5));
            Assert.That(craft.Fighter.Lead, Is.Null);
            var wing = Wing.Members(b, craft);
            Assert.That(wing.All(w => w.Fighter.Lead == craft && w.Fighter.Order == WingOrder.AttackMyTarget), Is.True);
            Fly(b, session, () => craft.Forward, 30);
            Assert.That(wing.Where(w => w.Present).All(w => w.Target == enemy), Is.True, "they go after the lead's target");
            Assert.That(wing.Where(w => w.Present).Any(w => w.Fighter.Phase == FighterPhase.AttackRun || w.Fighter.Phase == FighterPhase.BreakOff || (w.Pos - craft.Pos).Length > 3),
                Is.True, "flying runs of their own, not holding the vee");

            seat.OrderWing(WingOrder.FormUp);
            Fly(b, session, () => craft.Forward, 1);
            Assert.That(wing.Where(w => w.Present).All(w => w.Target == enemy), Is.True, "formed up, they fire on the lead's target");
            Assert.That(seat.Readout(), Does.Contain("form up"));
        }

        [Test]
        public void CoveringWingmenGoAfterWhatIsOnTheirLeadsTail()
        {
            var (b, _, _) = Fight(enemyCarrier: true);
            var craft = LeadOf(b, 0);
            var bandit = LeadOf(b, 0, side: 1);
            var (seat, session) = Take(b, craft);
            bandit.Pos = craft.Pos - craft.Forward * 1.0;
            Assert.That(Wing.Threat(b, craft), Is.EqualTo(bandit), "the fighter on its tail");
            seat.OrderWing(WingOrder.CoverMe);
            Fly(b, session, () => craft.Forward, 0.5);
            var wing = Wing.Members(b, craft).Where(w => w.Present).ToList();
            Assert.That(wing, Is.Not.Empty);
            var threat = Wing.Threat(b, craft);
            Assert.That(wing.All(w => w.Target == threat), Is.True, "every wingman turns on it");
        }

        // ------------------------------------------------------------ weak points

        [Test]
        public void ACapitalShipsWeakPointsLieOpenOnlyFromTheirOwnSide()
        {
            var (b, _, battleship) = Fight();
            var reactor = battleship.Combat.Subsystems.First(s => s.Kind == SubsystemKind.Reactor);
            var engine = battleship.Combat.Subsystems.First(s => s.Kind == SubsystemKind.Engine);
            var at = battleship.PointOf(reactor.Def);
            // The battleship faces +X with its roof on +Z: its reactor vent opens down and astern.
            var belowAstern = at + new DVec3(-0.8, 0, -0.6) * 3;
            var ahead = at + new DVec3(5, 0, 0);
            Assert.That(WeakPoints.ExposedFrom(battleship, reactor, belowAstern), Is.True);
            Assert.That(WeakPoints.ExposedFrom(battleship, reactor, ahead), Is.False);
            Assert.That(WeakPoints.Factor(battleship, reactor, at - belowAstern), Is.EqualTo(4));
            Assert.That(WeakPoints.Factor(battleship, reactor, at - ahead), Is.EqualTo(1));
            Assert.That(WeakPoints.ExposedFrom(battleship, engine, battleship.PointOf(engine.Def) - new DVec3(4, 0, 0)), Is.True, "engines from astern");
            Assert.That(WeakPoints.BestFrom(battleship, belowAstern), Is.SameAs(reactor), "the reactor first, where it is open");
            Assert.That(WeakPoints.BestFrom(battleship, battleship.Pos + new DVec3(0, 0, 6)).Kind, Is.EqualTo(SubsystemKind.ShieldGenerator), "the emitters from above");
            Assert.That(WeakPoints.Preferred(battleship), Is.SameAs(reactor));
            Assert.That(WeakPoints.On(battleship).Count, Is.EqualTo(1 + 8 + 3));
            Assert.That(WeakPoints.On(LeadOf(b, 0)), Is.Empty, "a fighter has none worth a run");
            Assert.That(WeakPoints.Cycle(battleship, reactor, 1).Kind, Is.EqualTo(SubsystemKind.ShieldGenerator));
            Assert.That(WeakPoints.Cycle(battleship, reactor, -1).Kind, Is.EqualTo(SubsystemKind.Engine));

            double saved = WeakPoints.Damage.Value;
            try
            {
                WeakPoints.Damage.Set(0);
                Assert.That(WeakPoints.Factor(battleship, reactor, at - belowAstern), Is.EqualTo(1), "the setting can take the bonus away");
            }
            finally
            {
                WeakPoints.Damage.Set(saved);
            }
        }

        /// <summary>A lone bomber under a battleship's stern, shields down on its side, its lock on the reactor.</summary>
        static bool LoneBomberRun(double weakPointDamage)
        {
            double saved = WeakPoints.Damage.Value, bare = StrikeTorpedoes.BareHullDamage.Value;
            try
            {
                WeakPoints.Damage.Set(weakPointDamage);
                // The weak point alone (the bare-hull warhead, U.SPC-T484, is StrikeCraftTests').
                StrikeTorpedoes.BareHullDamage.Set(1);
                var (b, _, battleship) = Fight();
                var bomber = LeadOf(b, 2);
                Duel(b, bomber);
                // Its systems out (no point defence, no shields coming back) and its shields shot away.
                battleship.Stunned = 1e6;
                battleship.Combat.ZeroAllShields();
                var reactor = battleship.Combat.Subsystems.First(s => s.Kind == SubsystemKind.Reactor);
                var vent = battleship.PointOf(reactor.Def);
                bomber.Pos = vent + new DVec3(-0.8, 0, -0.6) * 3;
                bomber.Vel = DVec3.Zero;
                bomber.Forward = (battleship.Pos - bomber.Pos).Normalized;
                var (seat, session) = Take(b, bomber);
                seat.Lock(battleship);
                Assert.That(bomber.PlayerSubTarget, Is.SameAs(reactor), "the reactor is the weak point picked");
                Assert.That(seat.Readout(), Does.Contain("reactor vent (OPEN)"));
                seat.ShiftPower(PowerSystem.Weapons);
                seat.ShiftPower(PowerSystem.Weapons);
                Fly(b, session, () => (battleship.Pos - bomber.Pos).Normalized, 45, secondary: true);
                Assert.That(bomber.Combat.Subsystems.First(s => s.Kind == SubsystemKind.Torpedo).Ammo, Is.EqualTo(0), "both torpedoes away");
                return battleship.Dead;
            }
            finally
            {
                WeakPoints.Damage.Set(saved);
                StrikeTorpedoes.BareHullDamage.Set(bare);
            }
        }

        [Test]
        public void ALoneBomberThroughAnOpenReactorVentBringsDownABattleship()
        {
            Assert.That(LoneBomberRun(1), Is.True, "two torpedoes into the open vent take the reactor and the ship with it");
            Assert.That(LoneBomberRun(0), Is.False, "the same two anywhere else barely scratch it");
        }

        [Test]
        public void AiBombersLineTheirRunsUpOnTheWeakPoints()
        {
            var (b, _, battleship) = Fight();
            var bomber = LeadOf(b, 2);
            var reactor = battleship.Combat.Subsystems.First(s => s.Kind == SubsystemKind.Reactor);
            bomber.Pos = battleship.PointOf(reactor.Def) + new DVec3(-0.8, 0, -0.6) * 3;
            Assert.That(FighterAI.TorpedoAimPoint(b, bomber, battleship), Is.SameAs(reactor), "its torpedo goes for the open reactor");
            bool lined = false;
            for (int i = 0; i < 300 && !lined && !b.Over; i++)
            {
                b.Advance(1);
                var lead = b.Units.FirstOrDefault(u => u.Present && u.Kind == UnitKind.Fighter && u.Fighter.Bomber && u.Fighter.Lead == null);
                if (lead == null) break;
                lined = lead.Fighter.Phase == FighterPhase.AttackRun && lead.Target == battleship
                        && WeakPoints.On(battleship).Any(s => WeakPoints.ExposedFrom(battleship, s, lead.Pos));
            }
            Assert.That(lined, Is.True, "a bomber's run comes in from where a weak point lies open");
        }

        [Test]
        public void TheDogfightingSettingsAreInTheMenu()
        {
            Assert.That(WeakPoints.Damage.Group, Is.EqualTo(SettingGroups.Dogfighting));
            Assert.That(SettingsRegistry.Default.Find("dogfight.weakPointDamage"), Is.SameAs(WeakPoints.Damage));
            Assert.That(SettingsRegistry.Default.Find("dogfight.countermeasures"), Is.SameAs(Countermeasures.Effectiveness));
            Assert.That(SettingsRegistry.Default.Find("dogfight.aiWeakPoints"), Is.SameAs(WeakPoints.AiGoesForThem));
        }
    }
}
