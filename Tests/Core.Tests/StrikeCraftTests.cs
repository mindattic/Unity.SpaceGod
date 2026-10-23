using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The strike craft as hulls of their own (U.SPC-T484, docs/SHIP_ROSTER.md): the interceptor, the space superiority
    /// fighter (today's <see cref="ShipClass.Fighter"/>) and the bomber, each a <see cref="ShipClass"/> with its own frame,
    /// crew, cost and flight model; carriers launch them as squadrons of those hulls; and a bomber's torpedoes do their
    /// work on a capital whose shields are down.
    /// </summary>
    [TestFixture]
    public class StrikeCraftTests
    {
        const double StartJd = 2461324.0;

        static readonly ShipClass[] StrikeCraft = { ShipClass.Interceptor, ShipClass.Fighter, ShipClass.Bomber };

        static Ship Hull(ShipClass c, Faction f, int id, int fighters = -1)
        {
            var ship = new Ship { Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c) };
            if (fighters >= 0) ship.Combat.Fighters = fighters;
            return ship;
        }

        static (Battle b, Faction blue, Faction red) NewBattle(int seed = 5)
        {
            var blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
            var red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed) { Launch = FighterAI.Launch, FighterStep = FighterAI.Step };
            return (b, blue, red);
        }

        static BattleUnit LeadOf(Battle b, FighterRole role, int side = 0) =>
            b.Units.First(u => u.Kind == UnitKind.Fighter && u.Side == side && u.Fighter.Role == role && u.Fighter.Lead == null);

        // ------------------------------------------------------------ the hulls

        [Test]
        public void EachStrikeCraftIsAHullOfItsOwnWithAFrameCrewCostAndCarriage()
        {
            foreach (var c in StrikeCraft)
            {
                var design = CombatSpec.Of(c);
                Assert.That(design.DesignName, Is.EqualTo(c.ToString()), c + " has a design of its own");
                Assert.That(ShipClassSpec.Of(c).IsMilitary, Is.True, c + " is a warship's craft");
                Assert.That(design.Crew, Is.GreaterThan(0), c + " is piloted");
                Assert.That(design.MassKt, Is.GreaterThan(0), c + " has a real hull to fly");
                var frame = HullFrames.ForClass(c);
                Assert.That(frame, Is.Not.Null, c + " can be laid down at a yard");
                Assert.That(frame.Category, Is.EqualTo(HullCategory.Fighter));
                Assert.That(Economy.HullCost(c), Is.GreaterThan(0));
                Assert.That(Loadout.IsSmallCraft(c), Is.True);
                Assert.That(FtlCarriage.SizeOf(c), Is.GreaterThan(0), c + " rides in a carrier's hangar");
                Assert.That(CruiseDrives.Of(c), Is.Null, c + " has no FTL of its own");
            }
            Assert.That(ShipClassSpec.Of(ShipClass.Interceptor).DisplayName, Is.EqualTo("Interceptor"));
            Assert.That(ShipClassSpec.Of(ShipClass.Bomber).DisplayName, Is.EqualTo("Bomber"));

            var interceptor = CombatSpec.Of(ShipClass.Interceptor);
            var fighter = CombatSpec.Of(ShipClass.Fighter);
            var bomber = CombatSpec.Of(ShipClass.Bomber);
            Assert.That(PilotTuning.Of(ShipClass.Interceptor).MaxSpeed, Is.GreaterThan(PilotTuning.Of(ShipClass.Fighter).MaxSpeed), "the interceptor is the fastest");
            Assert.That(PilotTuning.Of(ShipClass.Interceptor).TurnRateDegS, Is.GreaterThan(PilotTuning.Of(ShipClass.Fighter).TurnRateDegS), "and the most agile");
            Assert.That(PilotTuning.Of(ShipClass.Bomber).MaxSpeed, Is.LessThan(PilotTuning.Of(ShipClass.Fighter).MaxSpeed), "the bomber is slow");
            Assert.That(interceptor.Hull, Is.LessThan(fighter.Hull), "the interceptor is lightly built");
            Assert.That(bomber.Hull, Is.GreaterThan(fighter.Hull), "the bomber is tough");
            Assert.That(bomber.Armor, Is.GreaterThan(fighter.Armor));
            Assert.That(bomber.Layout.Count(d => d.Kind == SubsystemKind.TorpedoTube), Is.EqualTo(2), "the bomber carries torpedoes");
            Assert.That(interceptor.Layout.Any(d => d.Kind == SubsystemKind.TorpedoTube), Is.False);
            Assert.That(Economy.HullCost(ShipClass.Bomber), Is.GreaterThan(Economy.HullCost(ShipClass.Interceptor)));
            Assert.That(PilotTuning.ControlOf(ShipClass.Interceptor), Is.EqualTo(PilotControl.FreeAim));
            Assert.That(PilotTuning.ControlOf(ShipClass.Bomber), Is.EqualTo(PilotControl.DirectNose));
        }

        // ------------------------------------------------------------ flight models

        [Test]
        public void EachStrikeCraftFliesOnItsOwnFlightModel()
        {
            var reg = FlightModelRegistry.Default;
            Assert.That(reg.FamilyOf(ShipClass.Interceptor), Is.EqualTo(FlightFamily.Interceptor));
            Assert.That(reg.FamilyOf(ShipClass.Fighter), Is.EqualTo(FlightFamily.LightFighter));
            Assert.That(reg.FamilyOf(ShipClass.Bomber), Is.EqualTo(FlightFamily.Bomber));
            var models = StrikeCraft.Select(c => reg.For(c)).ToList();
            Assert.That(models.Distinct().Count(), Is.EqualTo(3), "three classes, three models");
            Assert.That(reg.For(ShipClass.Interceptor).Name, Is.EqualTo("Interceptor"));
            Assert.That(reg.For(ShipClass.Bomber), Is.InstanceOf<BomberFlightModel>());

            var light = LightFighterHandling.Default;
            var hunter = ((LightFighterFlightModel)reg.For(ShipClass.Interceptor)).Handling;
            Assert.That(hunter.Grip, Is.GreaterThan(light.Grip));
            Assert.That(hunter.StrafeSpeedFraction, Is.GreaterThan(light.StrafeSpeedFraction));
            Assert.That(hunter.CornerSpeedFraction, Is.GreaterThan(light.CornerSpeedFraction), "it holds its turn to a higher speed");
        }

        /// <summary>The share of its top speed a hull of <paramref name="c"/> has gathered from rest after
        /// <paramref name="seconds"/> at full throttle, and how fast it strafes after a second of full strafe.</summary>
        static (double gathered, double strafe) Handling(ShipClass c, double seconds)
        {
            var spec = CombatSpec.Of(c);
            var hull = HullDynamics.Of(spec);
            var tuning = PilotTuning.Of(c);
            var model = FlightModelRegistry.Default.For(c);
            var combat = new ShipCombat(spec, c);
            var s = PilotState.At(DVec3.Zero, DVec3.Zero, DVec3.UnitX);
            for (double t = 0; t < seconds; t += 0.05)
                model.Step(ref s, combat, hull, tuning, new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1 }, DVec3.Zero, 0.05);
            double gathered = s.Vel.Length / tuning.MaxSpeed;
            var side = PilotState.At(DVec3.Zero, DVec3.Zero, DVec3.UnitX);
            for (double t = 0; t < 1; t += 0.05)
                model.Step(ref side, combat, hull, tuning, new PilotInput { DesiredForward = DVec3.UnitX, StrafeRight = 1 }, DVec3.Zero, 0.05);
            return (gathered, side.Vel.Length);
        }

        [Test]
        public void ABomberIsSlowToGatherWayAndBarelySidesteps()
        {
            // The same airframe on the light fighter's model and the bomber's: only the rules differ.
            var spec = CombatSpec.Of(ShipClass.Bomber);
            var hull = HullDynamics.Of(spec);
            var tuning = PilotTuning.Of(ShipClass.Bomber);
            var combat = new ShipCombat(spec, ShipClass.Bomber);
            double Speed(IFlightModel model, PilotInput input)
            {
                var s = PilotState.At(DVec3.Zero, DVec3.Zero, DVec3.UnitX);
                for (double t = 0; t < 2; t += 0.05) model.Step(ref s, combat, hull, tuning, input, DVec3.Zero, 0.05);
                return s.Vel.Length;
            }
            var throttle = new PilotInput { DesiredForward = DVec3.UnitX, Throttle = 1 };
            var strafe = new PilotInput { DesiredForward = DVec3.UnitX, StrafeRight = 1 };
            var bomber = new BomberFlightModel();
            var light = new LightFighterFlightModel();
            Assert.That(Speed(bomber, throttle), Is.LessThan(Speed(light, throttle) * 0.8), "the throttle has heft");
            Assert.That(Speed(bomber, strafe), Is.LessThan(Speed(light, strafe) * 0.8), "the strafe is weak");
            Assert.That(Speed(bomber, throttle), Is.GreaterThan(0), "but it does get going");

            var (interceptorWay, interceptorStrafe) = Handling(ShipClass.Interceptor, 2);
            var (bomberWay, bomberStrafe) = Handling(ShipClass.Bomber, 2);
            TestContext.Out.WriteLine($"2 s from rest: interceptor {interceptorWay:P0}, bomber {bomberWay:P0}; strafe {interceptorStrafe:0.000} vs {bomberStrafe:0.000} km/s");
            Assert.That(interceptorWay, Is.GreaterThan(bomberWay), "the interceptor is the quicker off the mark");
            Assert.That(interceptorStrafe, Is.GreaterThan(bomberStrafe * 2), "and the far harder sidestep");
        }

        [Test]
        public void WithAssistOffABomberDriftsOnTheNewtonianHull()
        {
            var spec = CombatSpec.Of(ShipClass.Bomber);
            var hull = HullDynamics.Of(spec);
            var tuning = PilotTuning.Of(ShipClass.Bomber);
            var input = new PilotInput { DesiredForward = DVec3.UnitY, Throttle = 1, FlightAssistOff = true };
            var a = PilotState.At(DVec3.Zero, new DVec3(0.1, 0, 0), DVec3.UnitX);
            var b = a;
            new BomberFlightModel().Step(ref a, new ShipCombat(spec, ShipClass.Bomber), hull, tuning, input, DVec3.Zero, 0.1);
            PilotFlight.Step(ref b, new ShipCombat(spec, ShipClass.Bomber), hull, tuning, input, DVec3.Zero, 0.1);
            Assert.That((a.Vel - b.Vel).Length, Is.LessThan(1e-12));
            Assert.That((a.Forward - b.Forward).Length, Is.LessThan(1e-12));
        }

        // ------------------------------------------------------------ carriers and their squadrons

        [Test]
        public void ACarrierLaunchesItsSquadronsAsHullsOfTheirOwnClasses()
        {
            var (b, blue, red) = NewBattle();
            b.AddShip(Hull(ShipClass.Carrier, blue, 0, fighters: 24), 0, new DVec3(14, 0, 0), -DVec3.UnitX);
            b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(-14, 0, 0), DVec3.UnitX);
            var aloft = b.Units.Where(u => u.Kind == UnitKind.Fighter).ToList();
            Assert.That(aloft.Count, Is.EqualTo(24));
            var roles = new[] { FighterRole.Interceptor, FighterRole.SpaceSuperiority, FighterRole.Bomber, FighterRole.HeavyFighter };
            for (int q = 0; q < roles.Length; q++)
                Assert.That(aloft.Where(u => u.Fighter.Squadron == q).All(u => u.Fighter.Role == roles[q]), Is.True, "squadron " + q + " flies " + roles[q]);
            Assert.That(FighterCraft.Of(FighterRole.Interceptor).Class, Is.EqualTo(ShipClass.Interceptor));
            Assert.That(FighterCraft.Of(FighterRole.SpaceSuperiority).Class, Is.EqualTo(ShipClass.Fighter));
            Assert.That(FighterCraft.Of(FighterRole.Bomber).Class, Is.EqualTo(ShipClass.Bomber));
            Assert.That(FighterCraft.OfClass(ShipClass.Bomber), Is.SameAs(FighterCraft.Bomber));
            foreach (var u in aloft)
            {
                var craft = FighterCraft.Of(u.Fighter.Role);
                Assert.That(craft.Model, Is.SameAs(FlightModelRegistry.Default.For(craft.Class)), "each flies on its own class's model");
            }
            Assert.That(aloft.Where(u => u.Fighter.Bomber).All(u => u.Fighter.Role == FighterRole.Bomber), Is.True);
        }

        [Test]
        public void TheSquadronsComeHomeAndLandWhenTheFightIsOver()
        {
            var (b, blue, red) = NewBattle();
            var carrier = b.AddShip(Hull(ShipClass.Carrier, blue, 0), 0, new DVec3(14, 0, 0), -DVec3.UnitX);
            var enemy = b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(-14, 0, 0), DVec3.UnitX);
            Assert.That(carrier.Combat.Fighters, Is.EqualTo(0), "all aloft");
            b.Advance(5);
            // The enemy strikes: the fighting is over and the carrier calls its squadrons home.
            enemy.Surrendering = true;
            for (int i = 0; i < 600 && b.Units.Any(u => u.Kind == UnitKind.Fighter && u.Present); i++) b.Advance(1);
            Assert.That(b.Units.Where(u => u.Kind == UnitKind.Fighter).All(u => !u.Present), Is.True, "every craft is back aboard");
            Assert.That(carrier.Combat.Fighters, Is.EqualTo(18), "and counted back into the hangar");
        }

        // ------------------------------------------------------------ pilots pick their fights

        [Test]
        public void ABomberGoesForTheCapitalWhoseShieldsAreDown()
        {
            var (b, blue, red) = NewBattle();
            b.AddShip(Hull(ShipClass.Carrier, blue, 0), 0, new DVec3(30, 0, 0), -DVec3.UnitX);
            var up = b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(-14, 6, 0), DVec3.UnitX);
            var down = b.AddShip(Hull(ShipClass.Battleship, red, 2), 1, new DVec3(-14, -6, 0), DVec3.UnitX);
            down.Combat.ZeroAllShields();
            var bomber = LeadOf(b, FighterRole.Bomber);
            bomber.Pos = new DVec3(0, 0, 0);
            for (int i = 0; i < 20; i++)
                Assert.That(FighterAI.ChooseTarget(b, bomber), Is.SameAs(down), "the one its torpedoes can finish");
            Assert.That(StrikeTorpedoes.ShieldDownToward(down, bomber.Pos), Is.True);
            Assert.That(StrikeTorpedoes.ShieldDownToward(up, bomber.Pos), Is.False);
        }

        [Test]
        public void AnInterceptorScreensItsBombers()
        {
            var (b, blue, red) = NewBattle();
            b.AddShip(Hull(ShipClass.Carrier, blue, 0), 0, new DVec3(30, 0, 0), -DVec3.UnitX);
            b.AddShip(Hull(ShipClass.Carrier, red, 1), 1, new DVec3(-30, 0, 0), DVec3.UnitX);
            var interceptor = LeadOf(b, FighterRole.Interceptor, 0);
            var fighter = LeadOf(b, FighterRole.SpaceSuperiority, 0);
            var ourBomber = LeadOf(b, FighterRole.Bomber, 0);
            // Every hostile craft out of the way but two: one far off on its own, one closing on our bomber.
            var hostiles = b.Units.Where(u => u.Kind == UnitKind.Fighter && u.Side == 1).ToList();
            foreach (var h in hostiles) h.Docked = true;
            var loose = hostiles[0];
            var onTheBomber = hostiles[1];
            loose.Docked = onTheBomber.Docked = false;
            interceptor.Pos = fighter.Pos = DVec3.Zero;
            ourBomber.Pos = new DVec3(0, 12, 0);
            loose.Pos = new DVec3(0, -9, 0);
            onTheBomber.Pos = ourBomber.Pos + new DVec3(1.5, 0, 0);
            int screens = 0, hunts = 0;
            for (int i = 0; i < 40; i++)
            {
                var t = FighterAI.ChooseTarget(b, interceptor);
                if (t == onTheBomber) screens++;
                if (t != null && t.Kind == UnitKind.Fighter) hunts++;
            }
            Assert.That(hunts, Is.EqualTo(40), "an interceptor goes for craft before hulls");
            Assert.That(screens, Is.GreaterThan(30), "and first for the one on its bomber, though it is the further off");
            Assert.That(FighterCraft.Interceptor.ScreensBombers, Is.True);
            Assert.That(FighterCraft.SpaceSuperiority.ScreensBombers, Is.False);
            Assert.That(FighterCraft.Interceptor.HuntsFightersKm, Is.GreaterThan(FighterCraft.SpaceSuperiority.HuntsFightersKm));
        }

        // ------------------------------------------------------------ torpedoes on a bare hull

        /// <summary>The hull a battleship loses to one hit of <paramref name="damage"/> from astern with its shields up or
        /// down, as a bomber's torpedo or not.</summary>
        static double HullLost(bool shieldsDown, bool strike, double damage = 420)
        {
            var (b, blue, red) = NewBattle();
            var battleship = b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(-14, 0, 0), DVec3.UnitX);
            if (shieldsDown) battleship.Combat.ZeroAllShields();
            double before = battleship.Combat.Hull + battleship.Combat.Subsystems.Sum(s => Math.Max(0, s.Hp));
            var at = battleship.Pos - battleship.Forward * battleship.Radius;
            b.ApplyDamage(battleship, null, damage, at, at, 1, battleship.Forward, strikeTorpedo: strike);
            return before - (battleship.Combat.Hull + battleship.Combat.Subsystems.Sum(s => Math.Max(0, s.Hp)));
        }

        [Test]
        public void ABombersTorpedoHitsABareCapitalHullHarder()
        {
            double plain = HullLost(shieldsDown: true, strike: false);
            double torpedo = HullLost(shieldsDown: true, strike: true);
            TestContext.Out.WriteLine($"bare hull: a shot {plain:0} hp, a bomber's torpedo {torpedo:0} hp");
            Assert.That(plain, Is.GreaterThan(0));
            Assert.That(torpedo, Is.GreaterThan(plain * 2), "the warhead does its work once the shield is down");
            Assert.That(HullLost(shieldsDown: false, strike: true), Is.EqualTo(0).Within(1e-9), "while the shield holds, it soaks the torpedo like anything else");

            double saved = StrikeTorpedoes.BareHullDamage.Value;
            try
            {
                StrikeTorpedoes.BareHullDamage.Set(1);
                Assert.That(HullLost(shieldsDown: true, strike: true), Is.EqualTo(plain).Within(1e-6), "the setting can take the bonus away");
            }
            finally
            {
                StrikeTorpedoes.BareHullDamage.Set(saved);
            }
            Assert.That(SettingsRegistry.Default.Find("dogfight.torpedoBareHull"), Is.SameAs(StrikeTorpedoes.BareHullDamage));
        }

        [Test]
        public void ABomberRunOnAShieldsDownCapitalDoesFarMoreThanOnAShieldedOne()
        {
            double Run(bool shieldsDown)
            {
                var (b, blue, red) = NewBattle(9);
                b.AddShip(Hull(ShipClass.Carrier, blue, 0), 0, new DVec3(14, 0, 0), -DVec3.UnitX);
                var battleship = b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(-14, 0, 0), DVec3.UnitX);
                // Only the bombers fly, and the battleship's guns and shields are out of the fight.
                foreach (var u in b.Units)
                {
                    if (u.Kind == UnitKind.Fighter && !u.Fighter.Bomber) u.Docked = true;
                    if (u.Kind != UnitKind.Fighter)
                        foreach (var s in u.Combat.Subsystems)
                            if (s.Def.IsWeapon) s.Hp = 0;
                }
                battleship.Stunned = 1e6;
                if (shieldsDown) battleship.Combat.ZeroAllShields();
                double start = battleship.Combat.Hull;
                for (int i = 0; i < 240 && !b.Over && battleship.Present; i++)
                {
                    b.Advance(1);
                    // Hold its shields up all through the shielded run.
                    if (!shieldsDown)
                        foreach (var s in battleship.Combat.Subsystems)
                            if (s.Kind == SubsystemKind.ShieldGenerator && !s.Destroyed) s.Shield = s.MaxShield;
                }
                return start - battleship.Combat.Hull;
            }
            double shielded = Run(false), bare = Run(true);
            TestContext.Out.WriteLine($"six bombers' torpedoes: shields up {shielded:0} hull, shields down {bare:0} hull");
            Assert.That(bare, Is.GreaterThan(shielded + 1000), "bombers knock the shields down first, then torpedo");
        }
    }
}
