using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The capital-ship broadside game (U.SPC-T425): batteries ordered by the arc they fire into, volleys that hold for the
    /// battery and telegraph themselves, shields angled to a facing, armour that turns oblique hits, the brace, damage
    /// control, formations a flagship orders its line into, turning circles, and AI captains using the same commands.
    /// </summary>
    [TestFixture]
    public class BroadsideTests
    {
        const double StartJd = 2461324.0;

        static double Health(BattleUnit u) => u.Combat.Hull + u.Combat.Subsystems.Sum(s => Math.Max(0, s.Hp) + s.Shield);

        static readonly Faction Blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
        static readonly Faction Red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };

        static Ship Hull(ShipClass c, Faction f, int id) => new Ship
        {
            Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c),
        };

        /// <summary>A point at <paramref name="bearingDeg"/> off a hull at the origin facing +X, <paramref name="km"/> away.</summary>
        static DVec3 Bearing(BattleUnit u, double bearingDeg, double km)
        {
            u.Basis(out var right, out _);
            double r = bearingDeg * Astro.Deg2Rad;
            return u.Pos + (u.Forward * Math.Cos(r) + right * Math.Sin(r)) * km;
        }

        /// <summary>
        /// A battleship at the helm (held by the player, so its crew's doctrine stands aside and its orders are the
        /// player's) against an enemy battleship lying dead in the water at <paramref name="bearingDeg"/>, its guns silent.
        /// </summary>
        static (Battle b, BattleUnit me, BattleUnit enemy) Helm(double bearingDeg, double km = 10, int seed = 3)
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed);
            var me = b.AddShip(Hull(ShipClass.Battleship, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            me.PlayerControlled = true;
            me.PlayerHelm = true;
            var enemy = b.AddShip(Hull(ShipClass.Battleship, Red, 1), 1, Bearing(me, bearingDeg, km), DVec3.UnitY);
            enemy.Stunned = 1e6;
            foreach (var s in enemy.Combat.Subsystems)
                if (s.Def.IsWeapon) s.Hp = 0;
            Broadsides.Of(me);
            return (b, me, enemy);
        }

        static void Run(Battle b, double seconds, double dt = 0.1)
        {
            for (double t = 0; t < seconds - 1e-9 && !b.Over; t += dt) b.Advance(dt);
        }

        // ------------------------------------------------------------ arcs and batteries

        [Test]
        public void ABearingLiesInOneOfFourArcs()
        {
            Assert.That(Facings.Of(0), Is.EqualTo(Facing.Bow));
            Assert.That(Facings.Of(44), Is.EqualTo(Facing.Bow));
            Assert.That(Facings.Of(90), Is.EqualTo(Facing.Starboard));
            Assert.That(Facings.Of(180), Is.EqualTo(Facing.Stern));
            Assert.That(Facings.Of(270), Is.EqualTo(Facing.Port));
            Assert.That(Facings.Of(-30), Is.EqualTo(Facing.Bow));
            Assert.That(Facings.OffDeg(350, Facing.Bow), Is.EqualTo(10).Within(1e-9));
        }

        [Test]
        public void ABatteryIsTheGunsCoveringItsArc()
        {
            var (_, me, _) = Helm(90);
            var (_, starboard) = Broadsides.Battery(me, Facing.Starboard);
            var (_, bow) = Broadsides.Battery(me, Facing.Bow);
            Assert.That(starboard, Is.EqualTo(1), "the starboard laser turret (missiles and point defence are no battery)");
            Assert.That(bow, Is.EqualTo(2), "the two forward heavy lasers");
        }

        [Test]
        public void ABatteryHeldDoesNotFireIntoItsArcAndFiresAgainAtWill()
        {
            var (b, me, enemy) = Helm(90);
            me.Broadside[Facing.Starboard] = BatteryOrder.HoldFire;
            double before = Health(enemy);
            Run(b, 30);
            Assert.That(Health(enemy), Is.EqualTo(before).Within(1e-6), "nothing fires into a held arc");
            Assert.That(Broadsides.CycleOrder(me, Facing.Starboard), Is.EqualTo(BatteryOrder.Volley));
            Assert.That(Broadsides.CycleOrder(me, Facing.Starboard), Is.EqualTo(BatteryOrder.FireAtWill));
            Run(b, 30);
            Assert.That(Health(enemy), Is.LessThan(before), "at will, the starboard battery fires");
        }

        [Test]
        public void HoldingOneArcLeavesTheOthersFiring()
        {
            var (b, me, enemy) = Helm(0, 12);
            me.Broadside[Facing.Starboard] = BatteryOrder.HoldFire;
            me.Broadside[Facing.Port] = BatteryOrder.HoldFire;
            double before = Health(enemy);
            Run(b, 30);
            Assert.That(Health(enemy), Is.LessThan(before), "the bow battery is not held");
        }

        [Test]
        public void AVolleyHoldsUntilItsBatteryIsLoadedTelegraphsItAndThenFiresTogether()
        {
            // At 50 deg off the bow the enemy lies in the starboard arc, under both forward heavy lasers and the starboard turret.
            var (b, me, enemy) = Helm(50);
            me.Broadside[Facing.Starboard] = BatteryOrder.Volley;
            var turret = me.Combat.Subsystems.First(s => s.Kind == SubsystemKind.LaserTurret && Math.Abs(s.Def.ArcCenterDeg - 90) < 1);
            var heavies = me.Combat.Subsystems.Where(s => s.Kind == SubsystemKind.HeavyLaser && s.Def.ArcCenterDeg == 0).ToList();
            turret.Cooldown = 4;
            double before = Health(enemy);
            Run(b, 2);
            Assert.That(Health(enemy), Is.EqualTo(before).Within(1e-6), "the loaded heavies hold for the turret");
            Assert.That(me.Broadside.Readying, Is.EqualTo(Facing.Starboard), "the volley building shows");
            Assert.That(me.Broadside.ReadyingOn, Is.SameAs(enemy));
            Assert.That(me.Broadside.ReadyingLoaded, Is.EqualTo(0).Within(1e-9), "the starboard turret is still loading");
            Assert.That(heavies.All(h => h.Cooldown <= 0), Is.True, "the heavies wait loaded");

            bool fired = false;
            for (int i = 0; i < 60 && !fired; i++)
            {
                double was = turret.Cooldown;
                b.Advance(0.1);
                fired = turret.Cooldown > was;
            }
            Assert.That(fired, Is.True, "the volley goes once the battery is loaded");
            Assert.That(heavies.All(h => h.Cooldown > 0), Is.True, "every loaded gun bearing went with it, in the same step");
        }

        // ------------------------------------------------------------ shields

        [Test]
        public void AngledShieldsDrawChargeToThatFacingWithoutMakingAnyMore()
        {
            var (_, me, _) = Helm(90);
            var c = me.Combat;
            double total = c.Shield;
            var starboard = c.GeneratorFor(90);
            var port = c.GeneratorFor(270);
            Broadsides.TrimShields(me, Facing.Starboard);
            for (int i = 0; i < 120; i++)
            {
                Broadsides.StepShields(me, 1);
                c.RegenShields(1000 + i, 1);
            }
            Assert.That(starboard.Shield, Is.GreaterThan(starboard.MaxShield * 1.3), "the starboard arc holds well over its even share");
            Assert.That(port.Shield, Is.LessThan(port.MaxShield * 0.8), "the far side gives it up");
            Assert.That(c.Shield, Is.EqualTo(total).Within(1e-6), "the whole is what it was");

            Broadsides.TrimShields(me, null);
            for (int i = 0; i < 120; i++)
            {
                Broadsides.StepShields(me, 1);
                c.RegenShields(2000 + i, 1);
            }
            Assert.That(starboard.Shield, Is.EqualTo(starboard.MaxShield).Within(starboard.MaxShield * 0.01), "spread evenly again");
            Assert.That(c.Shield, Is.EqualTo(total).Within(1e-6));
        }

        // ------------------------------------------------------------ armour

        [Test]
        public void AHitStrikingTheBeltObliquelyMeetsThickerArmourAndPartlyGlances()
        {
            var (_, me, _) = Helm(90);
            me.Basis(out var right, out _);
            var square = Broadsides.Angling(me, -right);
            Assert.That(square.armour, Is.EqualTo(1).Within(1e-9), "beam-on: the belt as rated");
            Assert.That(square.through, Is.EqualTo(1).Within(1e-9));
            var angled = Broadsides.Angling(me, (-right - me.Forward).Normalized);
            Assert.That(angled.armour, Is.GreaterThan(1.1));
            Assert.That(angled.through, Is.LessThan(0.95));
            var bow = Broadsides.Angling(me, -me.Forward);
            var stern = Broadsides.Angling(me, me.Forward);
            Assert.That(bow.armour, Is.GreaterThan(1), "the bow is the heaviest face");
            Assert.That(stern.armour, Is.LessThan(1), "the engines are the thinnest");
        }

        [Test]
        public void AngledToTheLineOfFireAHullTakesLessFromTheSameHit()
        {
            double Loss(DVec3 incoming)
            {
                var (b, me, _) = Helm(90);
                me.Combat.ZeroAllShields();
                double before = me.Combat.Hull;
                b.ApplyDamage(me, null, 200, me.Pos, null, 1, incoming);
                return before - me.Combat.Hull;
            }
            var right = new DVec3(0, -1, 0);
            double beamOn = Loss(-right);
            double angled = Loss((-right - DVec3.UnitX).Normalized);
            Assert.That(angled, Is.LessThan(beamOn * 0.92), "the same shot at 45 deg glances and meets more plate");

            double saved = Broadsides.ArmourAngling.Value;
            try
            {
                Broadsides.ArmourAngling.Set(0);
                Assert.That(Loss((-right - DVec3.UnitX).Normalized), Is.EqualTo(beamOn).Within(1e-9), "angling off: the angle makes no difference");
            }
            finally
            {
                Broadsides.ArmourAngling.Set(saved);
            }
        }

        // ------------------------------------------------------------ the brace

        [Test]
        public void ABracedCrewShrugsOffPartOfEveryHitButHoldsItsFire()
        {
            var (b, me, enemy) = Helm(0, 12);
            me.Combat.ZeroAllShields();
            double hull = me.Combat.Hull;
            b.ApplyDamage(me, null, 1000, me.Pos);
            double open = hull - me.Combat.Hull;
            Assert.That(Broadsides.Brace(me), Is.True);
            Assert.That(Broadsides.Brace(me), Is.False, "once at a time");
            hull = me.Combat.Hull;
            b.ApplyDamage(me, null, 1000, me.Pos);
            double braced = hull - me.Combat.Hull;
            Assert.That(braced, Is.LessThan(open * 0.6), "half of it shrugged off");

            double before = Health(enemy);
            Run(b, 2);
            Assert.That(Health(enemy), Is.EqualTo(before).Within(1e-6), "no gun fires while the crew is braced");
            Run(b, 3);
            Assert.That(me.Broadside.Braced, Is.False, "the brace wears off");
            Assert.That(Health(enemy), Is.LessThan(before), "and the guns, loaded meanwhile, fire again");
            Assert.That(Broadsides.Brace(me), Is.False, "the crew is not back at its stations yet");
            Run(b, Broadsides.BraceCooldownSeconds);
            Assert.That(Broadsides.Brace(me), Is.True);
        }

        [Test]
        public void AnAiCaptainBracesForABroadsideItSeesBuilding()
        {
            var (b, me, enemy) = Helm(90);
            enemy.Stunned = 0;
            enemy.Combat.Hull = enemy.Combat.Spec.Hull;
            me.PlayerVolleyAim = (enemy.Pos - me.Pos).Normalized;
            me.PlayerFireHeld = true;
            Assert.That(Broadsides.IncomingBroadside(b, enemy), Is.Null, "nothing building yet");
            Run(b, 1.2);
            Assert.That(Broadsides.IncomingBroadside(b, enemy), Is.SameAs(me), "the player's volley is building on it");
            Assert.That(enemy.Broadside?.Braced ?? false, Is.True, "its captain braced");
        }

        [Test]
        public void AHostileBatteryReadyingOnAHullIsTheTell()
        {
            var (b, me, enemy) = Helm(90);
            var cmd = Broadsides.Of(enemy);
            cmd.Readying = Facing.Port;
            cmd.ReadyingOn = me;
            cmd.ReadyingLoaded = 0.5;
            enemy.Stunned = 0;
            Assert.That(Broadsides.IncomingBroadside(b, me), Is.Null, "half loaded: too early to brace");
            cmd.ReadyingLoaded = 0.8;
            Assert.That(Broadsides.IncomingBroadside(b, me), Is.SameAs(enemy));
        }

        // ------------------------------------------------------------ damage control

        [Test]
        public void DamageControlTeamsComeFromTheCrew()
        {
            var (b, me, _) = Helm(90);
            Assert.That(Broadsides.Teams(me), Is.EqualTo(Broadsides.MaxTeams), "a battleship's company musters every team");
            var destroyer = b.AddShip(Hull(ShipClass.Destroyer, Blue, 7), 0, new DVec3(0, 5, 0), DVec3.UnitX);
            Assert.That(Broadsides.Teams(destroyer), Is.EqualTo(1));
        }

        [Test]
        public void DamageControlPatchesMountsInTheFieldButNotWhatWasShotAway()
        {
            var (_, me, _) = Helm(90);
            var turret = me.Combat.Subsystems.First(s => s.Kind == SubsystemKind.LaserTurret);
            var lost = me.Combat.Subsystems.First(s => s.Kind == SubsystemKind.HeavyLaser);
            turret.Hp = turret.Def.MaxHp * 0.3;
            lost.Hp = 0;
            for (int i = 0; i < 600; i++) Broadsides.StepDamageControl(me, 1);
            Assert.That(turret.Hp, Is.EqualTo(turret.Def.MaxHp * Broadsides.FieldRepairCap).Within(1e-6), "patched up to what the field allows");
            Assert.That(lost.Destroyed, Is.True, "a mount shot away waits for a yard");
            Assert.That(me.Broadside.Repaired, Is.GreaterThan(0));
        }

        [Test]
        public void DamageControlWorksItsFocusFirst()
        {
            var (_, me, _) = Helm(90);
            var turret = me.Combat.Subsystems.First(s => s.Kind == SubsystemKind.LaserTurret);
            var engine = me.Combat.Subsystems.First(s => s.Kind == SubsystemKind.Engine);
            turret.Hp = turret.Def.MaxHp * 0.2;
            engine.Hp = engine.Def.MaxHp * 0.5;
            Assert.That(Broadsides.CycleFocus(me), Is.EqualTo(DamageControlFocus.Weapons));
            Assert.That(Broadsides.CycleFocus(me), Is.EqualTo(DamageControlFocus.Shields));
            Assert.That(Broadsides.CycleFocus(me), Is.EqualTo(DamageControlFocus.Engines));
            double engineBefore = engine.Hp, turretBefore = turret.Hp;
            for (int i = 0; i < 10; i++) Broadsides.StepDamageControl(me, 1);
            Assert.That(engine.Hp, Is.GreaterThan(engineBefore), "the engines first");
            Assert.That(turret.Hp, Is.EqualTo(turretBefore), "the worse-hurt turret waits its turn");
        }

        // ------------------------------------------------------------ formations

        [Test]
        public void FormationsLayTheLineOutInTheFlagshipsFrame()
        {
            for (int i = 0; i < 3; i++)
            {
                var ahead = Broadsides.SlotFor(FleetFormation.LineAhead, i, 3, 4);
                Assert.That(ahead.X, Is.EqualTo(0));
                Assert.That(ahead.Z, Is.EqualTo(-(i + 1) * 4.0), "a column astern");
            }
            Assert.That(Broadsides.SlotFor(FleetFormation.LineAbreast, 0, 2, 4).X, Is.GreaterThan(0));
            Assert.That(Broadsides.SlotFor(FleetFormation.LineAbreast, 1, 2, 4).X, Is.LessThan(0), "alternately to port");
            var echelon = Broadsides.SlotFor(FleetFormation.Echelon, 1, 3, 4);
            Assert.That(echelon.X, Is.GreaterThan(0));
            Assert.That(echelon.Z, Is.LessThan(0));
            Assert.That(Broadsides.SlotFor(FleetFormation.Screen, 0, 4, 4).Length, Is.EqualTo(6).Within(1e-9));
        }

        [Test]
        public void TheLineTakesStationOnItsFlagshipAndBreaksOffToEngageAtWill()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 9);
            var flag = b.AddShip(Hull(ShipClass.Battleship, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            flag.PlayerControlled = true;
            flag.PlayerHelm = true;
            var line = new[]
            {
                b.AddShip(Hull(ShipClass.Cruiser, Blue, 1), 0, new DVec3(3, 6, 0), DVec3.UnitX),
                b.AddShip(Hull(ShipClass.Destroyer, Blue, 2), 0, new DVec3(-2, -7, 0), DVec3.UnitX),
                b.AddShip(Hull(ShipClass.Cruiser, Blue, 3), 0, new DVec3(4, -4, 0), DVec3.UnitX),
            };
            var enemy = b.AddShip(Hull(ShipClass.Battleship, Red, 4), 1, new DVec3(0, 48, 0), DVec3.UnitX);
            enemy.Stunned = 1e6;
            foreach (var s in enemy.Combat.Subsystems)
                if (s.Def.IsWeapon) s.Hp = 0;

            Assert.That(Broadsides.OrderFormation(b, flag, FleetFormation.LineAhead), Is.EqualTo(3));
            Assert.That(line.All(u => u.Leader == flag), Is.True);
            Assert.That(line.Select(u => u.Slot.Z).Distinct().Count(), Is.EqualTo(3), "each its own place in the column");
            Run(b, 400, 1);
            foreach (var u in line)
                Assert.That((u.Pos - b.StationOf(u)).Length, Is.LessThan(1.5), u.Name + " holds its station in the line");

            Broadsides.OrderFormation(b, flag, FleetFormation.EngageAtWill);
            Assert.That(line.All(u => u.Leader == null), Is.True, "cut loose to fight their own fights");
            Assert.That(flag.Broadside.Formation, Is.EqualTo(FleetFormation.EngageAtWill));
        }

        [Test]
        public void OnlyTheFlagshipsOwnWarshipsJoinItsLine()
        {
            var b = new Battle(0, 3, "Test", DVec3.Zero, StartJd, 9);
            var flag = b.AddShip(Hull(ShipClass.Battleship, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            var ally = new Faction("Green", "GRN", new Rgb(0, 1, 0)) { IsMilitary = true };
            b.AddShip(Hull(ShipClass.Cruiser, ally, 1), 0, new DVec3(3, 6, 0), DVec3.UnitX);
            b.AddShip(Hull(ShipClass.Fighter, Blue, 2), 0, new DVec3(3, 3, 0), DVec3.UnitX);
            b.AddShip(Hull(ShipClass.Cruiser, Red, 3), 1, new DVec3(30, 0, 0), DVec3.UnitX);
            var mine = b.AddShip(Hull(ShipClass.Destroyer, Blue, 4), 0, new DVec3(-3, 6, 0), DVec3.UnitX);
            Assert.That(Broadsides.FormationMembers(b, flag), Is.EquivalentTo(new[] { mine }));
        }

        // ------------------------------------------------------------ turning circles

        [Test]
        public void TheTurningCircleWidensWithSpeedAndASlowTurnTakesLongerToComeAbout()
        {
            double turn = PilotTuning.Of(ShipClass.Battleship).TurnRateDegS;
            Assert.That(Broadsides.TurningRadiusKm(0.1, turn), Is.EqualTo(0.1 / (turn * Astro.Deg2Rad)).Within(1e-12));
            Assert.That(Broadsides.TurningRadiusKm(0.2, turn), Is.EqualTo(2 * Broadsides.TurningRadiusKm(0.1, turn)).Within(1e-12));
            Assert.That(Broadsides.ComeAboutSeconds(turn), Is.EqualTo(180 / turn).Within(1e-12));
            Assert.That(Broadsides.ComeAboutSeconds(PilotTuning.Of(ShipClass.Destroyer).TurnRateDegS), Is.LessThan(Broadsides.ComeAboutSeconds(turn)),
                "the destroyer comes about quicker than the battleship");
            var (_, me, enemy) = Helm(90);
            Assert.That(Broadsides.SecondsToBear(me, enemy, Facing.Starboard, 5), Is.EqualTo(0).Within(1e-6), "already on the beam");
            Assert.That(Broadsides.SecondsToBear(me, enemy, Facing.Bow, 5), Is.EqualTo(18).Within(1e-6), "90 deg to swing the bow on at 5 deg/s");
        }

        // ------------------------------------------------------------ AI captains

        [Test]
        public void AnAiCaptainFiresBroadsidesFromItsBeamAndAnglesItsShieldsToTheEnemy()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 4);
            var blue = b.AddShip(Hull(ShipClass.Battleship, Blue, 0), 0, new DVec3(-8, 0, 0), DVec3.UnitX);
            var red = b.AddShip(Hull(ShipClass.Battleship, Red, 1), 1, new DVec3(8, 0, 0), -DVec3.UnitX);
            Run(b, 2);
            var cmd = blue.Broadside;
            Assert.That(cmd, Is.Not.Null);
            Assert.That(cmd[Facing.Starboard], Is.EqualTo(BatteryOrder.Volley));
            Assert.That(cmd[Facing.Port], Is.EqualTo(BatteryOrder.Volley));
            Assert.That(cmd[Facing.Bow], Is.EqualTo(BatteryOrder.FireAtWill));
            Assert.That(blue.Target, Is.SameAs(red));
            Assert.That(cmd.ShieldTrim, Is.EqualTo(Facings.Toward(blue, red.Pos)), "shields angled to the enemy");
        }

        [Test]
        public void SmallCraftHaveNoBroadsideOrders()
        {
            var b = new Battle(0, 3, "Test", DVec3.Zero, StartJd, 1);
            var fighter = b.AddShip(Hull(ShipClass.Fighter, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            Assert.That(Broadsides.Of(fighter), Is.Null);
            Assert.That(Broadsides.Brace(fighter), Is.False);
        }

        [Test]
        public void TheBroadsideSettingsAreInTheMenu()
        {
            Assert.That(Broadsides.ArmourAngling.Group, Is.EqualTo(SettingGroups.Broadsides));
            Assert.That(SettingsRegistry.Default.Find("broadside.armourAngling"), Is.SameAs(Broadsides.ArmourAngling));
            Assert.That(SettingsRegistry.Default.Find("broadside.brace"), Is.SameAs(Broadsides.BraceReduction));
            Assert.That(SettingsRegistry.Default.Find("broadside.damageControl"), Is.SameAs(Broadsides.DamageControlRate));
            Assert.That(SettingsRegistry.Default.Find("broadside.shieldAngling"), Is.SameAs(Broadsides.ShieldAngling));
            Assert.That(SettingsRegistry.Default.Find("broadside.aiVolleys"), Is.SameAs(Broadsides.AiVolleys));
            Assert.That(SettingsRegistry.Default.Find("broadside.aiShields"), Is.SameAs(Broadsides.AiAnglesShields));
            Assert.That(SettingsRegistry.Default.Find("broadside.aiBrace"), Is.SameAs(Broadsides.AiBraces));
        }
    }
}
