using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Heavy warheads and the anti-fighter screen (U.SPC-T489): antimatter torpedoes for frigates and capitals, and a
    /// destroyer's rotary flak and interceptor missiles beside its point defence, the umbrella that stops missile swarms and
    /// fighters short of the capitals it escorts.
    /// </summary>
    [TestFixture]
    public class ScreenWeaponsTests
    {
        const double StartJd = 2461324.0;

        static readonly Faction Blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
        static readonly Faction Red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };

        static Ship Hull(ShipClass c, Faction f, int id) => new Ship
        {
            Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c),
        };

        static double Health(BattleUnit u) => u.Combat.Hull + u.Combat.Subsystems.Sum(s => s.Hp + s.Shield);

        static void Knock(BattleUnit u, Func<SubsystemKind, bool> which)
        {
            foreach (var s in u.Combat.Subsystems)
                if (which(s.Kind)) s.Hp = 0;
        }

        static bool IsWeapon(SubsystemKind k) => new SubsystemDef(k, 0, 0, 0).IsWeapon;

        /// <summary>An unarmed, unmoving hostile far off, so a battle runs while rounds are thrown in by hand.</summary>
        static BattleUnit Hulk(Battle b, DVec3 pos)
        {
            var u = b.NewUnit(UnitKind.Platform, 1, Red, new ShipCombat(CombatSpec.Platform), CombatSpec.PlatformLengthKm, pos, DVec3.UnitX);
            Knock(u, IsWeapon);
            return u;
        }

        static Missile Inbound(DVec3 pos, BattleUnit target, double damage = 260, double hp = 12, double speed = 1.4, MissileKind kind = MissileKind.Missile) => new Missile
        {
            Pos = pos, Vel = (target.Pos - pos).Normalized * speed, Target = target, Damage = damage, Speed = speed, Hp = hp, Life = 90, Side = 1,
            Kind = kind, Torpedo = kind != MissileKind.Missile,
        };

        // ------------------------------------------------------------ what they are and where they go

        [Test]
        public void DestroyersCarryFlakAndInterceptorsBesideTheirPointDefenceAndCapitalsCarryAntimatterTorpedoes()
        {
            var destroyer = CombatSpec.Of(ShipClass.Destroyer).Layout;
            Assert.That(destroyer.Count(d => d.Kind == SubsystemKind.PointDefense), Is.GreaterThanOrEqualTo(2), "the destroyer kept its point-defence lasers");
            Assert.That(destroyer.Count(d => d.Kind == SubsystemKind.FlakCannon), Is.EqualTo(2));
            Assert.That(destroyer.Count(d => d.Kind == SubsystemKind.InterceptorMissile), Is.EqualTo(1));
            foreach (var c in new[] { ShipClass.Battleship, ShipClass.Carrier })
                Assert.That(CombatSpec.Of(c).Layout.Count(d => d.Kind == SubsystemKind.AntimatterTorpedo), Is.EqualTo(1), c.ToString());

            var torpedo = WeaponSpec.AntimatterTorpedo;
            Assert.That(torpedo.Missile, Is.True, "a guided warhead");
            Assert.That(torpedo.RangeKm, Is.GreaterThan(WeaponSpec.Laser.RangeKm).And.LessThan(WeaponSpec.HeavyTorpedo.RangeKm), "medium range");
            Assert.That(torpedo.Damage, Is.GreaterThan(WeaponSpec.HeavyTorpedo.Damage), "the hardest single hit in the game");
            Assert.That(WeaponSpec.Flak.AntiSmall && WeaponSpec.Flak.BurstKm > 0 && WeaponSpec.Flak.RangeKm > WeaponSpec.PointDefense.RangeKm, Is.True,
                "flak is an area anti-small gun reaching past point defence");
            Assert.That(WeaponSpec.Interceptor.AntiSmall && WeaponSpec.Interceptor.Missile, Is.True, "an interceptor is a guided anti-small round");
            Assert.That(WeaponSpec.Interceptor.MissileSpeedKmS, Is.GreaterThan(torpedo.MissileSpeedKmS * 2), "it is far quicker than what it hunts");
            Assert.That(ScreenWeapons.RoundHp(SubsystemKind.AntimatterTorpedo), Is.GreaterThan(ScreenWeapons.RoundHp(SubsystemKind.TorpedoTube)),
                "an antimatter torpedo takes more stopping than a tube's torpedo");
        }

        [Test]
        public void TheNewWeaponsAreYardModulesThatFitWhereTheyBelong()
        {
            foreach (var m in new[] { ModuleKind.AntimatterTorpedo, ModuleKind.FlakCannon, ModuleKind.InterceptorMissile })
                Assert.That(ModuleSpec.Of(m).Kind, Is.EqualTo(m), "the catalogue is in step with the module list");
            Assert.That(ModuleSpec.SubsystemOf(ModuleKind.AntimatterTorpedo), Is.EqualTo(SubsystemKind.AntimatterTorpedo));
            Assert.That(ModuleSpec.SubsystemOf(ModuleKind.FlakCannon), Is.EqualTo(SubsystemKind.FlakCannon));
            Assert.That(ModuleSpec.SubsystemOf(ModuleKind.InterceptorMissile), Is.EqualTo(SubsystemKind.InterceptorMissile));

            Assert.That(ModuleSpec.Fits(ModuleKind.AntimatterTorpedo, MountType.Missile, HardpointSize.Medium, true), Is.True, "a spinal missile mount takes an antimatter launcher");
            Assert.That(ModuleSpec.Fits(ModuleKind.AntimatterTorpedo, MountType.Missile, HardpointSize.Large, false), Is.False, "a broadside mount does not");
            Assert.That(ModuleSpec.Fits(ModuleKind.AntimatterTorpedo, MountType.Missile, HardpointSize.Small, true), Is.False, "nor a small one");
            Assert.That(ModuleSpec.Fits(ModuleKind.AntimatterTorpedo, MountType.Energy, HardpointSize.Large, true), Is.False, "nor an energy mount");
            Assert.That(ModuleSpec.Of(ModuleKind.FlakCannon).Mount, Is.EqualTo(MountType.Ballistic), "flak is a ballistic gun");
            Assert.That(ModuleSpec.Of(ModuleKind.InterceptorMissile).Mount, Is.EqualTo(MountType.Missile));
            foreach (var m in new[] { ModuleKind.FlakCannon, ModuleKind.InterceptorMissile })
            {
                var type = ModuleSpec.Of(m).Mount.Value;
                Assert.That(ModuleSpec.Fits(m, type, HardpointSize.Small, false), Is.True, m + " in a small mount of its type");
                Assert.That(ModuleSpec.Fits(m, type, HardpointSize.Large, false), Is.True, m + " in a large one");
                Assert.That(ModuleSpec.Fits(m, MountType.Energy, HardpointSize.Large, false), Is.False, m + " on no energy mount");
            }

            // The stock destroyer's screen mounts are refittable slots carrying their own modules.
            var l = new Loadout(CombatSpec.Of(ShipClass.Destroyer), ShipClass.Destroyer, 0);
            Assert.That(l.Slots.Count(s => s.Stock == ModuleKind.FlakCannon), Is.EqualTo(2));
            Assert.That(l.Slots.Count(s => s.Stock == ModuleKind.InterceptorMissile), Is.EqualTo(1));
            Assert.That(l.Build(), Is.SameAs(CombatSpec.Of(ShipClass.Destroyer)), "a stock loadout is the design as built");
        }

        [Test]
        public void TheNewTunablesAreInTheSettingsMenu()
        {
            SettingsRegistry.DiscoverLoadedDeclarations(); // the menu runs this at startup; All holds only what was declared so far
            var weapons = SettingsRegistry.Default.All.Where(s => s.Group == SettingGroups.Weapons).Select(s => s.Key).ToList();
            foreach (var key in new[] { "weapons.antimatterYield", "weapons.antimatterToughness", "weapons.flakBurst", "weapons.flakDamage", "weapons.interceptorReliability", "weapons.screenStation" })
                Assert.That(weapons, Does.Contain(key));
            Assert.That(SettingGroups.Order, Does.Contain(SettingGroups.Weapons));
        }

        // ------------------------------------------------------------ antimatter torpedoes

        [Test]
        public void ABattleshipLaunchesAnAntimatterTorpedoAtMediumRangeAndItsYieldIsASetting()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 2);
            var me = b.AddShip(Hull(ShipClass.Battleship, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            // Only the antimatter launcher left to fire, at a hull inside its reach.
            Knock(me, k => IsWeapon(k) && k != SubsystemKind.AntimatterTorpedo);
            var them = b.AddShip(Hull(ShipClass.Cruiser, Red, 1), 1, new DVec3(15, 0, 0), -DVec3.UnitX);
            Knock(them, IsWeapon);
            try
            {
                ScreenWeapons.AntimatterYield.Set(2);
                for (int i = 0; i < 10 && !b.Missiles.Any(); i++) b.Advance(1);
                var round = b.Missiles.FirstOrDefault(m => m.Kind == MissileKind.Antimatter);
                Assert.That(round, Is.Not.Null, "no antimatter torpedo was launched at a hull in its reach");
                Assert.That(round.Damage, Is.EqualTo(WeaponSpec.AntimatterTorpedo.Damage * 2).Within(1e-9), "the yield setting scales the warhead");
                Assert.That(round.Hp, Is.EqualTo(ScreenWeapons.AntimatterToughness.Value), "its toughness is the setting's");
                Assert.That(round.Target, Is.SameAs(them));
            }
            finally
            {
                ScreenWeapons.AntimatterYield.Reset();
            }
        }

        [Test]
        public void AnAntimatterTorpedoCanBeShotDownButTakesMoreThanAMissile()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 3);
            b.AddShip(Hull(ShipClass.Destroyer, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            Hulk(b, new DVec3(45, 0, 0));
            var missile = Inbound(new DVec3(0, 20, 0), b.Units[0]);
            var torpedo = Inbound(new DVec3(0, 20.05, 0), b.Units[0], WeaponSpec.AntimatterTorpedo.Damage, ScreenWeapons.RoundHp(SubsystemKind.AntimatterTorpedo), 1.3, MissileKind.Antimatter);
            b.Missiles.Add(missile);
            b.Missiles.Add(torpedo);
            // One flak burst between them: it kills the missile and only scars the torpedo.
            b.FlakBurstAt(new DVec3(0, 20.02, 0), 0, WeaponSpec.Flak.Damage, 0.4);
            Assert.That(missile.Gone, Is.True, "a flak burst kills a missile");
            Assert.That(torpedo.Gone, Is.False, "an antimatter torpedo shrugs off one burst");
            Assert.That(torpedo.Hp, Is.LessThan(ScreenWeapons.RoundHp(SubsystemKind.AntimatterTorpedo)));
            int bursts = 1;
            while (!torpedo.Gone && bursts < 10)
            {
                b.FlakBurstAt(torpedo.Pos, 0, WeaponSpec.Flak.Damage, 0.4);
                bursts++;
            }
            Assert.That(torpedo.Gone, Is.True, "flak never brought the torpedo down");
            Assert.That(bursts, Is.InRange(2, 3), "a couple of bursts on it should bring it down");
        }

        // ------------------------------------------------------------ flak

        [Test]
        public void AFlakBurstKillsAWholeKnotOfASwarmAndHurtsFightersInsideIt()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 4);
            var target = b.AddShip(Hull(ShipClass.Battleship, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            var fighter = b.AddShip(Hull(ShipClass.Fighter, Red, 1), 1, new DVec3(10.1, 0, 0), -DVec3.UnitX);
            fighter.Kind = UnitKind.Fighter;
            var knot = Enumerable.Range(0, 5).Select(i => Inbound(new DVec3(10 + 0.05 * i, 0.05 * (i % 2), 0), target)).ToList();
            var straggler = Inbound(new DVec3(12, 0, 0), target);
            b.Missiles.AddRange(knot);
            b.Missiles.Add(straggler);
            double before = Health(fighter);
            b.FlakBurstAt(new DVec3(10.1, 0, 0), 0, WeaponSpec.Flak.Damage, 0.4);
            Assert.That(knot.All(m => m.Gone), Is.True, "one burst should take every missile inside it");
            Assert.That(straggler.Gone, Is.False, "a missile outside the burst is untouched");
            Assert.That(Health(fighter), Is.LessThan(before), "a fighter inside the burst is hurt");
        }

        [Test]
        public void ADestroyersFlakFiresOnIncomingMissiles()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 5);
            var me = b.AddShip(Hull(ShipClass.Destroyer, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            Knock(me, k => k == SubsystemKind.PointDefense || k == SubsystemKind.InterceptorMissile);
            Hulk(b, new DVec3(45, 0, 0));
            for (int i = 0; i < 6; i++) b.Missiles.Add(Inbound(new DVec3(0, 6 + 0.1 * i, 0), me));
            double before = Health(me);
            for (int i = 0; i < 12; i++) b.Advance(0.5);
            Assert.That(b.Fx.Any(f => f.Kind == FxKind.FlakBurst), Is.True, "the flak never fired");
            Assert.That(Health(me), Is.EqualTo(before).Within(1e-6), "flak let a missile of the six through");
        }

        // ------------------------------------------------------------ interceptors

        [Test]
        public void AnInterceptorRunsDownAMissileLongBeforeItArrives()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 6);
            var me = b.AddShip(Hull(ShipClass.Destroyer, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            Knock(me, k => k == SubsystemKind.PointDefense || k == SubsystemKind.FlakCannon);
            Hulk(b, new DVec3(45, 0, 0));
            var torpedo = Inbound(new DVec3(0, 12, 0), me, WeaponSpec.AntimatterTorpedo.Damage, ScreenWeapons.RoundHp(SubsystemKind.AntimatterTorpedo), 1.3, MissileKind.Antimatter);
            b.Missiles.Add(torpedo);
            var launcher = me.Combat.Subsystems.First(s => s.Kind == SubsystemKind.InterceptorMissile);
            int ammo = launcher.Ammo;
            try
            {
                ScreenWeapons.InterceptorReliability.Set(1);
                for (int i = 0; i < 20 && !torpedo.Gone; i++) b.Advance(0.5);
                Assert.That(launcher.Ammo, Is.LessThan(ammo), "no interceptor was launched");
                Assert.That(torpedo.Gone, Is.True, "the interceptor never killed the torpedo");
                Assert.That(torpedo.Pos.Length, Is.GreaterThan(4), "it should be killed well short of the hull, out past any gun's reach");
            }
            finally
            {
                ScreenWeapons.InterceptorReliability.Reset();
            }
        }

        [Test]
        public void ASalvoOfInterceptorsSpreadsOverASwarmRatherThanPilingOntoOneRound()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 7);
            var me = b.AddShip(Hull(ShipClass.Destroyer, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            Hulk(b, new DVec3(45, 0, 0));
            var a = Inbound(new DVec3(0, 12, 0), me);
            var c = Inbound(new DVec3(0, -12, 0), me);
            b.Missiles.Add(a);
            b.Missiles.Add(c);
            for (int i = 0; i < 16; i++) b.Advance(0.5);
            var hunted = b.Missiles.Where(m => m.Kind == MissileKind.Interceptor).Select(m => m.Prey).ToList();
            Assert.That(a.Gone || hunted.Contains(a), Is.True, "nothing went after the first missile");
            Assert.That(c.Gone || hunted.Contains(c), Is.True, "nothing went after the second missile");
        }

        // ------------------------------------------------------------ the umbrella

        /// <summary>A battleship takes waves of missiles and torpedoes from one quarter; returns what they cost it.</summary>
        static double Barrage(int seed, bool escort, bool screenWeapons)
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed);
            var capital = b.AddShip(Hull(ShipClass.Battleship, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            if (escort)
            {
                var destroyer = b.AddShip(Hull(ShipClass.Destroyer, Blue, 1), 0, new DVec3(-3, 2, 0), DVec3.UnitX);
                if (!screenWeapons) Knock(destroyer, ScreenWeapons.IsScreenMount);
            }
            Hulk(b, new DVec3(45, 0, 0));
            var rng = new Random(seed);
            double before = Health(capital);
            for (int wave = 0; wave < 10; wave++)
            {
                for (int k = 0; k < 6; k++)
                {
                    var from = capital.Pos + new DVec3(rng.NextDouble() * 2 - 1, 14 + rng.NextDouble(), rng.NextDouble() - 0.5);
                    b.Missiles.Add(k == 0
                        ? Inbound(from, capital, WeaponSpec.HeavyTorpedo.Damage, 40, 1.0, MissileKind.Torpedo)
                        : Inbound(from, capital));
                }
                for (int i = 0; i < 8; i++) b.Advance(1);
            }
            for (int i = 0; i < 30; i++) b.Advance(1);
            return before - Health(capital);
        }

        [Test]
        public void ADestroyerScreenMeasurablyProtectsTheCapitalItEscorts()
        {
            double alone = 0, lasersOnly = 0, screened = 0;
            foreach (int seed in new[] { 11, 12, 13 })
            {
                alone += Barrage(seed, escort: false, screenWeapons: false);
                lasersOnly += Barrage(seed, escort: true, screenWeapons: false);
                screened += Barrage(seed, escort: true, screenWeapons: true);
            }
            TestContext.Out.WriteLine($"damage to the battleship: alone {alone:0}, escorted by point defence only {lasersOnly:0}, by the full screen {screened:0}");
            Assert.That(alone, Is.GreaterThan(0), "the barrage never touched an unescorted battleship");
            Assert.That(screened, Is.LessThan(alone * 0.5), "the screen should stop most of what would have hit an unescorted battleship");
            Assert.That(screened, Is.LessThan(lasersOnly * 0.75), "flak and interceptors should add markedly to the destroyer's point defence");
        }

        [Test]
        public void AScreeningDestroyerStandsBetweenItsCapitalAndTheIncomingRounds()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 8);
            var capital = b.AddShip(Hull(ShipClass.Battleship, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            var destroyer = b.AddShip(Hull(ShipClass.Destroyer, Blue, 1), 0, new DVec3(0, -6, 0), DVec3.UnitX);
            Hulk(b, new DVec3(45, 0, 0));
            Assert.That(ScreenWeapons.WardOf(b, destroyer), Is.SameAs(capital));
            Assert.That(ScreenWeapons.WardOf(b, capital), Is.Null, "the biggest hull there screens nobody");
            // A slow, distant stream of torpedoes from +Y keeps the threat on that side while the destroyer moves.
            for (int i = 0; i < 90; i++)
            {
                if (i % 10 == 0) b.Missiles.Add(Inbound(capital.Pos + new DVec3(0, 30, 0), capital, 900, 1e6, 0.05, MissileKind.Torpedo));
                b.Advance(1);
            }
            var toThreat = DVec3.UnitY;
            Assert.That(DVec3.Dot(destroyer.Pos - capital.Pos, toThreat), Is.GreaterThan(0), "the destroyer never crossed to the threatened side of its capital");
        }
    }
}
