using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Radiators and heat on big ships (U.SPC-T491): capitals carry radiator panels that shed what their reactor, guns and
    /// drive make; whole, they shed it all, so heat is no worry; the panels are a weak point gunners go for; a hull that has
    /// lost them builds heat and its crew throttles the guns and drive to hold it short of the limit, and one that runs hot
    /// past the limit loses hands and burns out its systems.
    /// </summary>
    [TestFixture]
    public class RadiatorTests
    {
        const double StartJd = 2461324.0;

        static readonly Faction Blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
        static readonly Faction Red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };

        static Ship Hull(ShipClass c, Faction f, int id) => new Ship
        {
            Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c),
        };

        static (Battle b, BattleUnit u) One(ShipClass c = ShipClass.Battleship, int seed = 3)
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed);
            var u = b.AddShip(Hull(c, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            return (b, u);
        }

        static void LoseRadiators(BattleUnit u)
        {
            foreach (var s in u.Combat.Subsystems)
                if (s.Kind == SubsystemKind.Radiator) s.Hp = 0;
        }

        /// <summary>Fights flat out for <paramref name="seconds"/>: every gun reloading and the drive at full battle speed, as
        /// far as the heat's throttle lets it go (the drive's speed held down by the throttle as the battle's own move does).</summary>
        static void FlatOut(BattleUnit u, double seconds, double dt = 0.5)
        {
            for (double t = 0; t < seconds; t += dt)
            {
                foreach (var s in u.Combat.Subsystems)
                    if (s.Def.Weapon != null && !s.Destroyed) s.Cooldown = 1;
                u.Vel = DVec3.UnitX * u.Combat.Spec.MaxSpeed * (Radiators.RunsHot(u) ? 1 : Radiators.Throttle(u));
                Radiators.Step(u, dt);
            }
        }

        [Test]
        public void BigShipsCarryRadiatorPanelsAndSmallerOnesShedThroughTheirSkin()
        {
            foreach (var c in new[] { ShipClass.Carrier, ShipClass.Battleship, ShipClass.CommandCruiser, ShipClass.Cruiser })
            {
                var spec = CombatSpec.Of(c);
                Assert.That(spec.Layout.Count(d => d.Kind == SubsystemKind.Radiator), Is.EqualTo(4), c.ToString());
                Assert.That(Radiators.Carries(new ShipCombat(spec, c)), Is.True, c.ToString());
                // On after the design's own hardpoints, so theirs keep their places.
                int first = Array.FindIndex(spec.Layout, d => d.Kind == SubsystemKind.Radiator);
                Assert.That(spec.Layout.Skip(first).All(d => d.Kind == SubsystemKind.Radiator), Is.True, c.ToString());
            }
            foreach (var c in new[] { ShipClass.Destroyer, ShipClass.Frigate, ShipClass.Fighter, ShipClass.Freighter })
                Assert.That(Radiators.Carries(new ShipCombat(CombatSpec.Of(c), c)), Is.False, c.ToString());
            Assert.That(Radiators.Carries(new ShipCombat(CombatSpec.Platform)), Is.False, "a station is not a ship");
        }

        [Test]
        public void ARadiatorPanelIsAWeakPoint()
        {
            var spec = CombatSpec.Of(ShipClass.Battleship);
            var panel = spec.Layout.First(d => d.Kind == SubsystemKind.Radiator);
            var turret = spec.Layout.First(d => d.Kind == SubsystemKind.LaserTurret);
            var engine = spec.Layout.First(d => d.Kind == SubsystemKind.Engine);
            Assert.That(panel.MaxHp, Is.LessThan(turret.MaxHp / 3), "a thin sheet, not a mount");
            Assert.That(panel.MaxHp, Is.LessThan(engine.MaxHp / 3));
            Assert.That(panel.MaxHp, Is.LessThan(WeaponSpec.Heavy.Damage), "a heavy laser's hit through the shields takes one away");
            Assert.That(panel.IsWeapon, Is.False);
            Assert.That(SubsystemDef.Name(SubsystemKind.Radiator), Is.EqualTo("Radiator"));
        }

        [Test]
        public void WholeRadiatorsShedEverythingAShipMakesFightingFlatOut()
        {
            var (_, u) = One();
            FlatOut(u, 900);
            Assert.That(u.HullHeat, Is.EqualTo(0), "no heat to manage");
            Assert.That(Radiators.Throttle(u), Is.EqualTo(1));
            Assert.That(Radiators.Made(u, 1), Is.LessThan(Radiators.Shed(u.Combat)));
        }

        [Test]
        public void LosingHalfTheRadiatorsOnlyTellsInAFightFlatOut()
        {
            var (_, u) = One();
            foreach (var s in u.Combat.Subsystems.Where(s => s.Kind == SubsystemKind.Radiator).Take(2)) s.Hp = 0;
            Assert.That(Radiators.Intact(u.Combat), Is.EqualTo(0.5).Within(1e-9));
            // Cruising, guns quiet: it sheds all it makes.
            u.Vel = DVec3.UnitX * u.Combat.Spec.MaxSpeed * 0.5;
            for (int i = 0; i < 600; i++) Radiators.Step(u, 0.5);
            Assert.That(u.HullHeat, Is.EqualTo(0));
            // Every gun and full burn: it heats, slowly.
            FlatOut(u, 60);
            Assert.That(u.HullHeat, Is.GreaterThan(0));
            Assert.That(u.HullHeat, Is.LessThan(Radiators.ThrottleAt.Value), "minutes, not seconds, before it tells");
        }

        [Test]
        public void AHullThatCannotShedItsHeatThrottlesItsGunsAndDriveToHoldIt()
        {
            var (_, u) = One();
            LoseRadiators(u);
            double crew = u.Combat.Interior.Crew;
            FlatOut(u, 1800);
            Assert.That(u.HullHeat, Is.GreaterThan(Radiators.ThrottleAt.Value), "it built heat");
            Assert.That(u.HullHeat, Is.LessThan(1), "the throttle holds it short of the limit");
            Assert.That(Radiators.Throttle(u), Is.LessThan(1));
            Assert.That(Radiators.Throttle(u), Is.GreaterThanOrEqualTo(Radiators.MinThrottle.Value));
            Assert.That(u.Combat.Interior.Crew, Is.EqualTo(crew), "nobody hurt");
            Assert.That(u.Combat.Subsystems.Where(s => s.Kind != SubsystemKind.Radiator).All(s => s.Hp == s.Def.MaxHp), Is.True, "nothing burned");
        }

        [Test]
        public void RunningHotCooksTheCrewAndTheSystemsButNeverTheReactor()
        {
            var (_, u) = One();
            LoseRadiators(u);
            Broadsides.Of(u).RunHot = true;
            double crew = u.Combat.Interior.Crew;
            FlatOut(u, 900);
            Assert.That(Radiators.Throttle(u), Is.EqualTo(1), "guns and drive kept at full");
            Assert.That(u.HullHeat, Is.GreaterThan(1));
            Assert.That(u.Combat.Interior.Crew, Is.LessThan(crew), "hands lost");
            Assert.That(u.Combat.Interior.CrewLost, Is.GreaterThan(0));
            Assert.That(u.Combat.Subsystems.Any(s => s.Kind == SubsystemKind.Engine && s.Hp < s.Def.MaxHp), Is.True, "the drives burn");
            Assert.That(u.Combat.Subsystems.Any(s => s.Def.IsWeapon && s.Hp < s.Def.MaxHp), Is.True, "the guns burn");
            Assert.That(u.Combat.Subsystems.Where(s => s.Kind == SubsystemKind.Reactor || s.Kind == SubsystemKind.Bridge).All(s => s.Hp == s.Def.MaxHp), Is.True);
        }

        [Test]
        public void AnAiCaptainRunsHotOnlyToEscape()
        {
            var (_, u) = One();
            u.HullHeat = 1;
            Assert.That(Radiators.RunsHot(u), Is.False);
            Assert.That(Radiators.Throttle(u), Is.EqualTo(Radiators.MinThrottle.Value).Within(1e-9));
            u.Fleeing = true;
            Assert.That(Radiators.RunsHot(u), Is.True);
            Assert.That(Radiators.Throttle(u), Is.EqualTo(1));
            u.PlayerControlled = true;
            Assert.That(Radiators.RunsHot(u), Is.False, "the player decides for a hull they fly");
        }

        [Test]
        public void HeatDrainsOnceTheFightingStops()
        {
            var (_, u) = One();
            u.HullHeat = 0.9;
            u.Vel = DVec3.Zero;
            for (int i = 0; i < 300; i++) Radiators.Step(u, 0.5);
            Assert.That(u.HullHeat, Is.EqualTo(0));
        }

        [Test]
        public void ADestroyerHasNoHeatToManage()
        {
            var (_, u) = One(ShipClass.Destroyer);
            FlatOut(u, 900);
            Assert.That(u.HullHeat, Is.EqualTo(0));
            Assert.That(Radiators.Throttle(u), Is.EqualTo(1));
        }

        [Test]
        public void GunnersGoForTheRadiatorsOfAHullRunningHot()
        {
            var (b, u) = One();
            var shooter = b.AddShip(Hull(ShipClass.Battleship, Red, 1), 1, new DVec3(20, 0, 0), -DVec3.UnitX);
            int Picks()
            {
                int n = 0;
                for (int i = 0; i < 4000; i++) if (b.PickSubsystem(u, shooter)?.Kind == SubsystemKind.Radiator) n++;
                return n;
            }
            u.HullHeat = 0;
            int cold = Picks();
            u.HullHeat = 1;
            int hot = Picks();
            Assert.That(cold, Is.GreaterThan(0), "a weak point worth a shot");
            Assert.That(hot, Is.GreaterThan(cold * 2), "and far more so on a hull that shows it is running hot");
        }

        [Test]
        public void InBattleAHotHullReloadsSlowerAndItsHeatIsStepped()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 5);
            var hot = b.AddShip(Hull(ShipClass.Battleship, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            var cool = b.AddShip(Hull(ShipClass.Battleship, Blue, 1), 0, new DVec3(0, 5, 0), DVec3.UnitX);
            // A foe far out of reach, so nobody fires and every gun just reloads.
            var foe = b.AddShip(Hull(ShipClass.Battleship, Red, 2), 1, new DVec3(50, 0, 0), -DVec3.UnitX);
            foe.Stunned = 1e6;
            LoseRadiators(hot);
            hot.HullHeat = 1;
            foreach (var u in new[] { hot, cool })
                foreach (var s in u.Combat.Subsystems)
                    if (s.Def.Weapon != null) s.Cooldown = 50;
            for (int i = 0; i < 10; i++) b.Advance(0.1);
            var hotGun = hot.Combat.Subsystems.First(s => s.Kind == SubsystemKind.HeavyLaser);
            var coolGun = cool.Combat.Subsystems.First(s => s.Kind == SubsystemKind.HeavyLaser);
            double hotReloaded = 50 - hotGun.Cooldown, coolReloaded = 50 - coolGun.Cooldown;
            Assert.That(coolReloaded, Is.GreaterThan(0.9));
            Assert.That(hotReloaded, Is.LessThan(coolReloaded * 0.3), "the throttled crew reloads at a fraction of the rate");
            Assert.That(hot.HullHeat, Is.LessThan(1), "and the heat is coming down");
            Assert.That(cool.HullHeat, Is.EqualTo(0));
        }
    }
}
