using System;
using System.Linq;
using SolarSystem.Core;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    /// <summary>
    /// The dogfight arsenal (U.SPC-T271): autocannons and light railguns as fittable guns, EMP rounds that stun
    /// rather than hurt, proximity mines laid astern, and sentry turrets that fight on their own until they run down.
    /// </summary>
    [TestFixture]
    public class ArsenalTests
    {
        const double StartJd = 2461324.0;

        static Ship Hull(ShipClass c, Faction f, int id) => new Ship
        {
            Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c),
        };

        static (Battle b, BattleUnit mine, BattleUnit theirs) Duel(ShipClass mine, ShipClass theirs, DVec3 theirPos, bool player = true)
        {
            var blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
            var red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 1);
            b.AddShip(Hull(mine, blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            b.AddShip(Hull(theirs, red, 1), 1, theirPos, -DVec3.UnitX);
            b.Units[0].PlayerControlled = player;
            return (b, b.Units[0], b.Units[1]);
        }

        static double Health(BattleUnit u) => u.Combat.Hull + u.Combat.Subsystems.Sum(s => s.Hp + s.Shield);

        // ------------------------------------------------------------ fitting

        [Test]
        public void SmallCraftCarryAGunMountAndAUtilityMountThatTakeTheNewArsenal()
        {
            foreach (var c in new[] { ShipClass.Fighter, ShipClass.Courier })
            {
                var l = new Loadout(CombatSpec.Of(c), c, 0);
                int gun = l.Slots.FindIndex(s => s.Kind == SlotKind.GunMount);
                int utility = l.Slots.FindIndex(s => s.Kind == SlotKind.UtilityMount);
                Assert.That(gun, Is.GreaterThanOrEqualTo(0), c + " has no gun mount");
                Assert.That(utility, Is.GreaterThanOrEqualTo(0), c + " has no utility mount");
                Assert.That(Outfitting.CanFit(l, gun, ModuleKind.LightRailgun), Is.True);
                Assert.That(Outfitting.CanFit(l, gun, ModuleKind.HeavyLaser), Is.False, "a heavy laser is no fighter's gun");
                foreach (var m in new[] { ModuleKind.EmpLauncher, ModuleKind.MineLayer, ModuleKind.TurretDeployer, ModuleKind.MissileLauncher })
                    Assert.That(Outfitting.CanFit(l, utility, m), Is.True, c + " utility mount refused " + m);
            }
            var cruiser = new Loadout(CombatSpec.Of(ShipClass.Cruiser), ShipClass.Cruiser, 0);
            int light = cruiser.Slots.FindIndex(s => s.Kind == SlotKind.LightMount);
            Assert.That(Outfitting.CanFit(cruiser, light, ModuleKind.Autocannon), Is.True, "a warship's light mount takes an autocannon");
        }

        [Test]
        public void ALightRailgunFittedToAFighterFiresHarderSlowerSlugs()
        {
            var l = new Loadout(CombatSpec.Of(ShipClass.Fighter), ShipClass.Fighter, 0);
            int gun = l.Slots.FindIndex(s => s.Kind == SlotKind.GunMount);
            l.Fitted[gun] = ModuleKind.LightRailgun;
            var spec = l.Build();
            Assert.That(spec.Layout.Count(d => d.Kind == SubsystemKind.LightRailgun), Is.EqualTo(1));
            Assert.That(WeaponSpec.LightRailgun.Damage, Is.GreaterThan(WeaponSpec.Autocannon.Damage * 4));
            Assert.That(WeaponSpec.LightRailgun.RangeKm, Is.GreaterThan(WeaponSpec.Autocannon.RangeKm * 3));
        }

        [Test]
        public void AFreightersMineLayerDoesNotMakeItAWarship()
        {
            var c = new ShipCombat(CombatSpec.Of(ShipClass.Freighter), ShipClass.Freighter);
            Assert.That(c.Subsystems.Any(s => s.Kind == SubsystemKind.MineLayer), Is.True, "a freighter carries mines for what chases it");
            Assert.That(c.Armed, Is.False, "mines are a way to get away, not to fight");
        }

        // ------------------------------------------------------------ EMP

        [Test]
        public void AnEmpRoundStunsASmallCraftAndHalvesItsShieldsWithoutHurtingIt()
        {
            var (b, me, them) = Duel(ShipClass.Courier, ShipClass.Fighter, new DVec3(2, 0, 0));
            double hull = them.Combat.Hull;
            double shields = them.Combat.Subsystems.Sum(s => s.Shield);
            me.PlayerAim = DVec3.UnitX;
            me.PlayerEmpHeld = true;
            for (int i = 0; i < 60 && them.Stunned <= 0; i++)
            {
                them.Pos = new DVec3(2, 0, 0);
                b.Advance(0.1);
            }
            Assert.That(them.Stunned, Is.GreaterThan(WeaponSpec.Emp.EmpSeconds * 0.8), "the EMP round never stunned the fighter it was fired at");
            Assert.That(them.Combat.Hull, Is.EqualTo(hull), "an EMP round hurt the hull");
            Assert.That(them.Combat.Subsystems.Sum(s => s.Shield), Is.LessThan(shields * 0.6));
            Assert.That(b.Fx.Any(f => f.Kind == FxKind.EmpBurst), Is.True);
        }

        [Test]
        public void ABigHullShrugsAnEmpOffSoonerThanAFighter()
        {
            double fighter = Battle.EmpStunSeconds(8, CombatSpec.Of(ShipClass.Fighter).LengthKm);
            double battleship = Battle.EmpStunSeconds(8, CombatSpec.Of(ShipClass.Battleship).LengthKm);
            Assert.That(fighter, Is.EqualTo(8));
            Assert.That(battleship, Is.LessThan(1));
        }

        [Test]
        public void AStunnedPilotsGunsAndDeployersStaySilent()
        {
            var (b, me, them) = Duel(ShipClass.Breacher, ShipClass.Fighter, new DVec3(1, 0, 0));
            me.Stunned = 5;
            me.PlayerTurretRequested = true;
            b.Advance(0.1);
            Assert.That(b.Units.Any(u => u.Kind == UnitKind.Turret), Is.False, "a turret went out from a hull with its systems knocked out");
        }

        // ------------------------------------------------------------ mines

        [Test]
        public void AMineLaidAsternArmsThenBlowsUnderAHostileThatFliesIntoIt()
        {
            var (b, me, them) = Duel(ShipClass.Freighter, ShipClass.Fighter, new DVec3(-3, 0, 0));
            me.PlayerMineRequested = true;
            b.Advance(0.1);
            Assert.That(b.Mines.Count, Is.EqualTo(1), "no mine was laid on request");
            var mine = b.Mines[0];
            Assert.That(DVec3.Dot(mine.Pos - me.Pos, me.Forward), Is.LessThan(0), "the mine went out ahead, not astern");

            // Straight onto it before it is armed: nothing happens.
            them.Pos = mine.Pos;
            them.Vel = DVec3.Zero;
            b.Advance(0.1);
            Assert.That(b.Mines.Count, Is.EqualTo(1), "a mine blew before it was armed");

            double before = Health(them);
            for (int i = 0; i < 30 && b.Mines.Count > 0; i++)
            {
                them.Pos = mine.Pos;
                b.Advance(0.1);
            }
            Assert.That(b.Mines.Count, Is.EqualTo(0), "an armed mine with a hostile on top of it never went off");
            Assert.That(Health(them), Is.LessThan(before), "the blast hurt nothing");
            Assert.That(b.Fx.Any(f => f.Kind == FxKind.MineBlast), Is.True);
        }

        [Test]
        public void AFreightersCrewLaysMinesForWhatIsOnItsTail()
        {
            // A hostile fighter close astern, the freighter flown by its own crew.
            var (b, me, them) = Duel(ShipClass.Freighter, ShipClass.Fighter, new DVec3(-1.5, 0, 0), player: false);
            for (int i = 0; i < 30 && b.Mines.Count == 0; i++)
            {
                me.Forward = DVec3.UnitX;
                them.Pos = me.Pos + new DVec3(-1.5, 0, 0);
                b.Advance(0.1);
            }
            Assert.That(b.Mines.Count, Is.GreaterThan(0), "a freighter with a fighter on its tail laid no mines");
        }

        // ------------------------------------------------------------ sentry turrets

        [Test]
        public void ASentryTurretFightsOnItsOwnAndRunsDown()
        {
            var (b, me, them) = Duel(ShipClass.Breacher, ShipClass.Fighter, new DVec3(1.0, 0.5, 0));
            me.PlayerTurretRequested = true;
            b.Advance(0.1);
            var turret = b.Units.FirstOrDefault(u => u.Kind == UnitKind.Turret);
            Assert.That(turret, Is.Not.Null, "no turret went out on request");
            Assert.That(turret.Side, Is.EqualTo(me.Side));

            double before = Health(them);
            for (int i = 0; i < 100; i++)
            {
                them.Pos = turret.Pos + new DVec3(0.8, 0, 0);
                them.Vel = DVec3.Zero;
                them.Stunned = 1;
                b.Advance(0.1);
            }
            Assert.That(Health(them), Is.LessThan(before), "the turret never shot at the hostile beside it");

            for (int i = 0; i < 1000 && turret.Present; i++) b.Advance(0.1);
            Assert.That(turret.Present, Is.False, "the turret never ran down");
            Assert.That(b.Seconds, Is.LessThan(CombatSpec.SentryLifeS + 5));
        }

        [Test]
        public void ASentryTurretAloneDoesNotKeepABattleGoing()
        {
            var (b, me, them) = Duel(ShipClass.Breacher, ShipClass.Fighter, new DVec3(1.0, 0.5, 0));
            me.PlayerTurretRequested = true;
            b.Advance(0.1);
            // Its own side gone, the turret on its own is not a side still fighting.
            me.Fled = true;
            for (int i = 0; i < 20 && !b.Over; i++) b.Advance(0.1);
            Assert.That(b.Over, Is.True);
        }

        // ------------------------------------------------------------ missiles at fighters

        [Test]
        public void APlayersMissileLocksOntoAndHitsAFighter()
        {
            var (b, me, them) = Duel(ShipClass.Fighter, ShipClass.Fighter, new DVec3(4, 0, 0));
            me.PlayerLockTarget = them;
            me.PlayerAim = DVec3.UnitX;
            me.PlayerSecondaryHeld = true;
            double before = Health(them);
            for (int i = 0; i < 100; i++)
            {
                me.Forward = DVec3.UnitX;
                them.Pos = new DVec3(4, 0, 0);
                them.Vel = DVec3.Zero;
                b.Advance(0.1);
            }
            Assert.That(Health(them), Is.LessThan(before), "a missile locked onto a fighter never reached it");
        }
    }
}
