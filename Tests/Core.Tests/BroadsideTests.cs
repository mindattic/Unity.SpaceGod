using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The capital-ship broadside game (U.SPC-T425): every gun aiming within its own fire cone in 3D (U.SPC-T533), orders
    /// given to the guns that bear where the captain looks, volleys that hold for the guns bearing on the same hull and
    /// telegraph themselves, gunners focusing a weakened shield patch, the brace, damage control and its
    /// emergency repair crews, formations a flagship orders its line into, turning circles, and AI captains using the same
    /// commands and bringing the guns whose cones can take their target to bear.
    /// </summary>
    [TestFixture, NonParallelizable]
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

        /// <summary>The battle-local direction <paramref name="bearingDeg"/> off <paramref name="u"/>'s bow, level, clockwise from above.</summary>
        static DVec3 Dir(BattleUnit u, double bearingDeg) => (Bearing(u, bearingDeg, 1) - u.Pos).Normalized;

        static Subsystem Sponson(BattleUnit u, SubsystemKind kind, DVec3 axis) =>
            u.Combat.Subsystems.First(s => s.Kind == kind && s.Def.ConeAxis.Equals(axis));

        static void Run(Battle b, double seconds, double dt = 0.1)
        {
            for (double t = 0; t < seconds - 1e-9 && !b.Over; t += dt) b.Advance(dt);
        }

        // ------------------------------------------------------------ fire cones and batteries (U.SPC-T533)

        [Test]
        public void AGunBearsOnWhatLiesInsideItsConeAboveAndBelowTheHullAsWellAsToTheSide()
        {
            var (_, me, _) = Helm(90);
            var turret = Sponson(me, SubsystemKind.LaserTurret, FireCones.Starboard);
            me.Basis(out var right, out var up);
            Assert.That(me.BearsAlong(turret.Def, right), Is.True, "straight out to starboard");
            Assert.That(me.BearsAlong(turret.Def, (right + up).Normalized), Is.True, "45 deg above the beam");
            Assert.That(me.BearsAlong(turret.Def, (right - up).Normalized), Is.True, "and 45 deg below it");
            Assert.That(me.BearsAlong(turret.Def, up), Is.False, "but not straight overhead, past its cone");
            Assert.That(me.BearsAlong(turret.Def, me.Forward), Is.False, "nor dead ahead");
            Assert.That(me.BearsAlong(turret.Def, -right), Is.False, "nor across the hull to port");
            var spinal = me.Combat.Subsystems.First(s => s.Def.Spinal);
            Assert.That(me.BearsAlong(spinal.Def, me.Forward), Is.True, "a spinal mount bears down the keel");
            Assert.That(me.BearsAlong(spinal.Def, Dir(me, 20)), Is.False, "and only there: the hull lays it");
        }

        [Test]
        public void ABatteryIsTheGunsWhoseConesTakeInTheLine()
        {
            var (_, me, _) = Helm(90);
            var (_, starboard) = Broadsides.Battery(me, Dir(me, 90));
            var (_, bow) = Broadsides.Battery(me, me.Forward);
            var (_, between) = Broadsides.Battery(me, Dir(me, 50));
            Assert.That(starboard, Is.EqualTo(1), "the starboard laser turret (missiles, point defence and spinal mounts are no battery)");
            Assert.That(bow, Is.EqualTo(2), "the two forward heavy lasers");
            Assert.That(between, Is.EqualTo(3), "at 50 deg the turret's cone and the heavies' overlap: all three bear");
        }

        [Test]
        public void AHeldBatteryDoesNotFireAndFiresAgainAtWill()
        {
            var (b, me, enemy) = Helm(90);
            Assert.That(Broadsides.OrderAlong(me, Dir(me, 90), BatteryOrder.HoldFire), Is.GreaterThan(0));
            double before = Health(enemy);
            Run(b, 30);
            Assert.That(Health(enemy), Is.EqualTo(before).Within(1e-6), "nothing that bears on it fires");
            Assert.That(Broadsides.CycleOrder(me, Dir(me, 90)).order, Is.EqualTo(BatteryOrder.Volley));
            Assert.That(Broadsides.CycleOrder(me, Dir(me, 90)).order, Is.EqualTo(BatteryOrder.FireAtWill));
            Run(b, 30);
            Assert.That(Health(enemy), Is.LessThan(before), "at will, the starboard battery fires");
        }

        [Test]
        public void HoldingTheGunsBearingOneWayLeavesTheOthersFiring()
        {
            var (b, me, enemy) = Helm(0, 12);
            Broadsides.OrderAlong(me, Dir(me, 90), BatteryOrder.HoldFire);
            Broadsides.OrderAlong(me, Dir(me, 270), BatteryOrder.HoldFire);
            Assert.That(Broadsides.OrderAlong(me, me.Forward).order, Is.EqualTo(BatteryOrder.FireAtWill), "no gun bearing on the bow was held");
            double before = Health(enemy);
            Run(b, 30);
            Assert.That(Health(enemy), Is.LessThan(before), "the bow battery is not held");
        }

        [Test]
        public void NoGunBearsWhereNoConeReaches()
        {
            var (_, me, _) = Helm(90);
            me.Basis(out _, out var up);
            var (order, mounts) = Broadsides.CycleOrder(me, -up);
            Assert.That(mounts, Is.EqualTo(0), "nothing on the battleship looks straight down under its keel");
            Assert.That(order, Is.EqualTo(BatteryOrder.FireAtWill));
        }

        [Test]
        public void AVolleyHoldsUntilEveryGunBearingOnTheHullIsLoadedTelegraphsItAndThenFiresTogether()
        {
            // At 50 deg off the bow the enemy lies in the starboard turret's cone and both forward heavy lasers'.
            var (b, me, enemy) = Helm(50);
            Broadsides.OrderAlong(me, Dir(me, 50), BatteryOrder.Volley);
            var turret = Sponson(me, SubsystemKind.LaserTurret, FireCones.Starboard);
            var heavies = me.Combat.Subsystems.Where(s => s.Kind == SubsystemKind.HeavyLaser && s.Def.ConeAxis.Equals(FireCones.Bow)).ToList();
            Assert.That(heavies.Count, Is.EqualTo(2));
            turret.Cooldown = 4;
            double before = Health(enemy);
            Run(b, 2);
            Assert.That(Health(enemy), Is.EqualTo(before).Within(1e-6), "the loaded heavies hold for the turret");
            Assert.That(me.Broadside.Readying, Is.True, "the volley building shows");
            Assert.That(me.Broadside.ReadyingOn, Is.SameAs(enemy));
            Assert.That(me.Broadside.ReadyingLoaded, Is.EqualTo(2.0 / 3).Within(1e-9), "two of the three guns bearing are loaded");
            Assert.That(heavies.All(h => h.Cooldown <= 0), Is.True, "the heavies wait loaded");

            bool fired = false;
            for (int i = 0; i < 60 && !fired; i++)
            {
                double was = turret.Cooldown;
                b.Advance(0.1);
                fired = turret.Cooldown > was;
            }
            Assert.That(fired, Is.True, "the volley goes once every gun bearing is loaded");
            Assert.That(heavies.All(h => h.Cooldown > 0), Is.True, "every loaded gun bearing went with it, in the same step");
        }

        // ------------------------------------------------------------ focus fire (U.SPC-T501)

        [Test]
        public void TheCaptainCanOrderTheGunnersToSpreadTheirFireAndBackToFocus()
        {
            var (_, me, _) = Helm(90);
            Assert.That(Broadsides.FocusesFire(me), Is.True, "a player's crews go for a weakened patch by default");
            Assert.That(Broadsides.ToggleSpreadFire(me), Is.True);
            Assert.That(Broadsides.FocusesFire(me), Is.False, "ordered to spread their fire");
            Assert.That(Broadsides.ToggleSpreadFire(me), Is.False);
            Assert.That(Broadsides.FocusesFire(me), Is.True);
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
        public void DamageControlPatchesMountsInTheFieldButNotAnEngineShotAway()
        {
            var (_, me, _) = Helm(90);
            var turret = me.Combat.Subsystems.First(s => s.Kind == SubsystemKind.LaserTurret);
            var lost = me.Combat.Subsystems.First(s => s.Kind == SubsystemKind.Engine);
            turret.Hp = turret.Def.MaxHp * 0.3;
            lost.Hp = 0;
            for (int i = 0; i < 600; i++) Broadsides.StepDamageControl(me, 1);
            Assert.That(turret.Hp, Is.EqualTo(turret.Def.MaxHp * Broadsides.FieldRepairCap).Within(1e-6), "patched up to what the field allows");
            Assert.That(lost.Destroyed, Is.True, "an engine shot away waits for a yard");
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
        public void StationsStayLevelWhenTheFlagshipRollsForAWeakPatch()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 9);
            var flag = b.AddShip(Hull(ShipClass.Battleship, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            var u = b.AddShip(Hull(ShipClass.Cruiser, Blue, 1), 0, DVec3.Zero, DVec3.UnitX);
            u.Leader = flag;
            u.Slot = new DVec3(4, 0, 0);
            var level = b.StationOf(u) - flag.Pos;
            flag.Roof = new DVec3(0, 0, -1);
            var rolled = b.StationOf(u) - flag.Pos;
            Assert.That((rolled - level).Length, Is.LessThan(1.0), "a roll does not move a line-abreast station");
            Assert.That(Math.Abs(rolled.Z), Is.LessThan(1.0), "still beside the flagship, not above or below");
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
            var turret = Sponson(me, SubsystemKind.LaserTurret, FireCones.Starboard);
            var heavy = Sponson(me, SubsystemKind.HeavyLaser, FireCones.Bow);
            var spinal = me.Combat.Subsystems.First(s => s.Def.Spinal);
            Assert.That(Broadsides.SecondsToBear(me, enemy, turret, 5), Is.EqualTo(0).Within(1e-6), "already in the turret's cone");
            Assert.That(Broadsides.SecondsToBear(me, enemy, heavy, 5), Is.EqualTo(6).Within(1e-6), "30 deg past the heavy laser's 60 deg cone at 5 deg/s");
            Assert.That(Broadsides.SecondsToBear(me, enemy, spinal, 5), Is.EqualTo(16).Within(1e-6), "80 deg to lay a spinal mount's 10 deg cone on it");
        }

        // ------------------------------------------------------------ AI captains

        [Test]
        public void AnAiCaptainFiresTheGunsThatBearTogetherInVolleysAndItsGunnersFocusAWeakenedPatch()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 4);
            var blue = b.AddShip(Hull(ShipClass.Battleship, Blue, 0), 0, new DVec3(-8, 0, 0), DVec3.UnitX);
            var red = b.AddShip(Hull(ShipClass.Battleship, Red, 1), 1, new DVec3(8, 0, 0), -DVec3.UnitX);
            Run(b, 2);
            var cmd = blue.Broadside;
            Assert.That(cmd, Is.Not.Null);
            var starboard = Sponson(blue, SubsystemKind.LaserTurret, FireCones.Starboard);
            var port = Sponson(blue, SubsystemKind.LaserTurret, FireCones.Port);
            var heavy = Sponson(blue, SubsystemKind.HeavyLaser, FireCones.Bow);
            Assert.That(cmd.OrderOf(starboard), Is.EqualTo(BatteryOrder.Volley), "its cone overlaps the bow heavies'");
            Assert.That(cmd.OrderOf(port), Is.EqualTo(BatteryOrder.Volley));
            Assert.That(cmd.OrderOf(heavy), Is.EqualTo(BatteryOrder.Volley));
            Assert.That(cmd.OrderOf(Sponson(blue, SubsystemKind.MissileLauncher, FireCones.Starboard)), Is.EqualTo(BatteryOrder.FireAtWill), "a launcher is no battery");
            Assert.That(blue.Target, Is.SameAs(red));
            Assert.That(Broadsides.FocusesFire(blue), Is.True, "its gunners go for a weakened shield patch");
        }

        [Test]
        public void AnAiCaptainLetsAGunThatBearsAloneFireAtWill()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 4);
            var blue = b.AddShip(Hull(ShipClass.Battleship, Blue, 0), 0, new DVec3(-8, 0, 0), DVec3.UnitX);
            b.AddShip(Hull(ShipClass.Battleship, Red, 1), 1, new DVec3(8, 0, 0), -DVec3.UnitX);
            var aft = Sponson(blue, SubsystemKind.HeavyLaser, FireCones.Stern);
            foreach (var s in blue.Combat.Subsystems)
                if (s != aft && Broadsides.IsBatteryGun(s)) s.Hp = 0;
            Run(b, 0.5);
            Assert.That(blue.Broadside.OrderOf(aft), Is.EqualTo(BatteryOrder.FireAtWill), "no other gun's cone overlaps the stern heavy's now");
        }

        [Test]
        public void AnAiCaptainBringsTheGunsWhoseConesCanTakeItsTargetToBear()
        {
            double OffBowDeg(bool bring)
            {
                bool saved = FireCones.AiBringsGunsToBear.Value;
                try
                {
                    FireCones.AiBringsGunsToBear.Set(bring);
                    var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 4);
                    var blue = b.AddShip(Hull(ShipClass.Battleship, Blue, 0), 0, new DVec3(-8, 0, 0), DVec3.UnitX);
                    var red = b.AddShip(Hull(ShipClass.Battleship, Red, 1), 1, new DVec3(8, 0, 0), -DVec3.UnitX);
                    red.Stunned = 1e6;
                    // Its bow guns and spinal mounts shot away: what it has left bears on the beams.
                    foreach (var s in blue.Combat.Subsystems)
                        if (s.Def.IsWeapon && s.Def.Covers(blue.Combat.Spec, FireCones.Bow)) s.Hp = 0;
                    Run(b, 60, 0.5);
                    var to = (red.Pos - blue.Pos).Normalized;
                    return Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(blue.Forward, to)))) * Astro.Rad2Deg;
                }
                finally
                {
                    FireCones.AiBringsGunsToBear.Set(saved);
                }
            }
            double flank = OffBowDeg(true);
            // It may lay its flank slanted to the line of fire, so hits glance off its armour (U.SPC-T534).
            Assert.That(flank, Is.InRange(30.0, 150.0), "it turns a flank in, its sponsons' cones on the target");
            Assert.That(OffBowDeg(false), Is.LessThan(20), "with the setting off it points its bow at the target");
        }

        [Test]
        public void TurningTheAiVolleySettingOffAndBackOnLeavesTheCaptainFiringAndFocusing()
        {
            bool saved = Broadsides.AiVolleys.Value;
            try
            {
                Broadsides.AiVolleys.Set(false);
                var off = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 4);
                var offBlue = off.AddShip(Hull(ShipClass.Battleship, Blue, 0), 0, new DVec3(-8, 0, 0), DVec3.UnitX);
                off.AddShip(Hull(ShipClass.Battleship, Red, 1), 1, new DVec3(8, 0, 0), -DVec3.UnitX);
                Run(off, 2);
                Assert.That(offBlue.Broadside.OrderOf(Sponson(offBlue, SubsystemKind.LaserTurret, FireCones.Starboard)), Is.EqualTo(BatteryOrder.FireAtWill), "off: every gun fires at will");

                Broadsides.AiVolleys.Set(true);
                var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 4);
                var blue = b.AddShip(Hull(ShipClass.Battleship, Blue, 0), 0, new DVec3(-8, 0, 0), DVec3.UnitX);
                b.AddShip(Hull(ShipClass.Battleship, Red, 1), 1, new DVec3(8, 0, 0), -DVec3.UnitX);
                Run(b, 2);
                Assert.That(blue.Broadside.OrderOf(Sponson(blue, SubsystemKind.LaserTurret, FireCones.Starboard)), Is.EqualTo(BatteryOrder.Volley));
                Assert.That(blue.Broadside.OrderOf(Sponson(blue, SubsystemKind.LaserTurret, FireCones.Port)), Is.EqualTo(BatteryOrder.Volley));
                Assert.That(Broadsides.FocusesFire(blue), Is.True, "its gunners still focus a weakened patch");
            }
            finally
            {
                Broadsides.AiVolleys.Set(saved);
            }
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
        public void TheBowForATargetAboveTheHullPutsItExactlyAtTheAskedBearing()
        {
            double el = 30 * Math.PI / 180;
            var toTarget = new DVec3(Math.Cos(el), 0, Math.Sin(el));
            foreach (int bearing in new[] { 0, 45, 90, 180, 270 })
            {
                var bow = Battle.BowFor(toTarget, bearing).Normalized;
                double off = Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(bow, toTarget.Normalized)))) * 180 / Math.PI;
                Assert.That(off, Is.EqualTo(Math.Min(bearing, 360 - bearing)).Within(1e-6), "bearing " + bearing);
            }
        }

        [Test]
        public void TheBroadsideSettingsAreInTheMenu()
        {
            Assert.That(Broadsides.BraceReduction.Group, Is.EqualTo(SettingGroups.Broadsides));
            Assert.That(SettingsRegistry.Default.Find("broadside.armourAngling"), Is.SameAs(ArmourAngling.Strength), "armour angling on the 3D hull (U.SPC-T534)");
            Assert.That(SettingsRegistry.Default.Find("cone.turretTraverse"), Is.SameAs(FireCones.TurretTraverse));
            Assert.That(SettingsRegistry.Default.Find("cone.fixed"), Is.SameAs(FireCones.FixedCone));
            Assert.That(SettingsRegistry.Default.Find("cone.aiBearing"), Is.SameAs(FireCones.AiBringsGunsToBear));
            Assert.That(FireCones.TurretTraverse.Group, Is.EqualTo(SettingGroups.Weapons));
            Assert.That(SettingsRegistry.Default.Find("broadside.brace"), Is.SameAs(Broadsides.BraceReduction));
            Assert.That(SettingsRegistry.Default.Find("broadside.damageControl"), Is.SameAs(Broadsides.DamageControlRate));
            Assert.That(SettingsRegistry.Default.Find("broadside.aiVolleys"), Is.SameAs(Broadsides.AiVolleys));
            Assert.That(SettingsRegistry.Default.Find("broadside.aiFocusFire"), Is.SameAs(Broadsides.AiFocusFire));
            Assert.That(SettingsRegistry.Default.Find("broadside.aiBrace"), Is.SameAs(Broadsides.AiBraces));
        }

        [Test]
        public void AiTurn_SpinsUpAndCannotReverseTheSwingInOneFrame() // U.SPC-T557
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 3);
            var u = b.AddShip(Hull(ShipClass.Battleship, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            double dt = 1.0 / 30;
            var left = DVec3.UnitY;
            Battle.TurnToward(u, left, u.Combat.Spec.TurnRateDegS, dt);
            double firstFrameDeg = Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(u.Forward, DVec3.UnitX)))) * Astro.Rad2Deg;
            Assert.That(firstFrameDeg, Is.LessThan(u.Combat.Spec.TurnRateDegS * dt * 0.99), "spins up rather than snapping to the full rate");
            for (int i = 0; i < 60; i++) Battle.TurnToward(u, left, u.Combat.Spec.TurnRateDegS, dt);
            double yBefore = u.Forward.Y;
            Assert.That(yBefore, Is.GreaterThan(0.05));
            // The wanted facing flips to the other side: the nose keeps swinging the old way a while before it comes back.
            var before = u.Forward;
            Battle.TurnToward(u, -left, u.Combat.Spec.TurnRateDegS, dt);
            Assert.That(DVec3.Dot(u.Forward, left) > DVec3.Dot(before, left) || u.TurnSpin.Length > 0, Is.True);
            Assert.That(u.Forward.Y, Is.GreaterThanOrEqualTo(yBefore - 1e-9).Or.Not.NaN);
            for (int i = 0; i < 3000; i++) Battle.TurnToward(u, -left, u.Combat.Spec.TurnRateDegS, dt);
            Assert.That(DVec3.Dot(u.Forward, -left), Is.GreaterThan(0.9999));
        }
    }
}
