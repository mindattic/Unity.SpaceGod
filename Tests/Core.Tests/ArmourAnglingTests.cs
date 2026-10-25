using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Armour angling on the real 3D hull (U.SPC-T534): each hit's obliquity is measured against the hull's surface normal
    /// where the shot's line actually strikes it, not against bow, beam and stern faces, so a slanted strike meets thicker
    /// plate (capped) and is partly turned aside; the strength and the cap are settings, and the AI captain and the
    /// weak-patch rolling weigh presenting a slanted hull. Changes settings, so it runs on its own.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class ArmourAnglingTests
    {
        const double StartJd = 2461324.0;

        static readonly Faction Blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
        static readonly Faction Red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };

        static Ship Hull(ShipClass c, Faction f, int id) => new Ship
        {
            Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c),
        };

        /// <summary>A battleship flown by its crew at the origin, bow along +X (starboard is then -Y, its roof +Z), with a
        /// hostile battleship lying silent 10 km off.</summary>
        static (Battle b, BattleUnit me, BattleUnit enemy) Scene(int seed = 3)
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed);
            var me = b.AddShip(Hull(ShipClass.Battleship, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            var enemy = b.AddShip(Hull(ShipClass.Battleship, Red, 1), 1, new DVec3(0, -10, 0), DVec3.UnitY);
            enemy.Stunned = 1e6;
            foreach (var s in enemy.Combat.Subsystems)
                if (s.Def.IsWeapon) s.Hp = 0;
            return (b, me, enemy);
        }

        static void With(GameSetting setting, double value, Action body)
        {
            var range = (RangeSetting)setting;
            double saved = range.Value;
            try
            {
                range.Set(value);
                body();
            }
            finally
            {
                range.Set(saved);
            }
        }

        /// <summary>What a 200-point shot aimed at <paramref name="localAim"/> (ship lengths, hull frame) travelling along
        /// battle-local <paramref name="incoming"/> takes off a fresh, unshielded battleship's hull.</summary>
        static double Loss(DVec3 localAim, DVec3 incoming)
        {
            var (b, me, _) = Scene();
            me.Combat.ZeroAllShields();
            double before = me.Combat.Hull;
            b.ApplyDamage(me, null, 200, me.PointLocal(localAim), null, 1, incoming);
            return before - me.Combat.Hull;
        }

        static double Radius(ShipClass c) => CombatSpec.Of(c).BeamFraction * 0.5;

        // ------------------------------------------------------------ the strike on the hull's surface

        [Test]
        public void ObliquityIsMeasuredAgainstTheHullSurfaceWhereTheShotStrikes()
        {
            var spec = CombatSpec.Of(ShipClass.Battleship);
            double r = Radius(ShipClass.Battleship), core = 0.5 - r;
            var inward = new DVec3(-1, 0, 0); // travelling from starboard to port
            Assert.That(ArmourAngling.StrikeCos(spec, DVec3.Zero, inward), Is.EqualTo(1).Within(1e-9), "square on the beam");
            Assert.That(ArmourAngling.StrikeCos(spec, DVec3.Zero, new DVec3(0, -1, 0)), Is.EqualTo(1).Within(1e-9),
                "square from above too: there are no faces, only the hull's surface");
            Assert.That(ArmourAngling.StrikeCos(spec, DVec3.Zero, new DVec3(-1, 0, -1)), Is.EqualTo(Math.Sqrt(0.5)).Within(1e-6),
                "45 deg off the beam strikes the side at 45 deg");
            // Aimed at the hull's centre line from above but off to one side, the shot meets the curve of the hull.
            var curve = ArmourAngling.StrikeCos(spec, new DVec3(r * 0.9, 0, 0), new DVec3(0, -1, 0));
            Assert.That(curve, Is.EqualTo(Math.Sqrt(1 - 0.81)).Within(1e-6), "the curve of the side turns it");
            Assert.That(ArmourAngling.Entry(spec, new DVec3(r * 0.9, 0, 0), new DVec3(0, -1, 0), out var hit), Is.True);
            Assert.That(hit.Y, Is.GreaterThan(0), "struck on its upper side, where the shot came in");
            // Dead ahead onto the rounded bow: square at its tip, slanted on its shoulder.
            Assert.That(ArmourAngling.StrikeCos(spec, DVec3.Zero, new DVec3(0, 0, -1)), Is.EqualTo(1).Within(1e-9));
            double shoulder = ArmourAngling.StrikeCos(spec, new DVec3(r * Math.Sqrt(0.5), 0, core), new DVec3(0, 0, -1));
            Assert.That(shoulder, Is.EqualTo(Math.Sqrt(0.5)).Within(1e-6));
            Assert.That(ArmourAngling.StrikeCos(spec, new DVec3(r * 0.999, 0, 0), new DVec3(0, 0, -1)), Is.LessThan(0.1),
                "raking the hull lengthways along its side barely grazes it");
        }

        [Test]
        public void ASlantedStrikeMeetsThickerPlateUpToTheCapAndIsPartlyTurnedAside()
        {
            var square = ArmourAngling.Angling(1.0);
            Assert.That(square.armour, Is.EqualTo(1).Within(1e-9));
            Assert.That(square.through, Is.EqualTo(1).Within(1e-9));
            var slanted = ArmourAngling.Angling(0.5);
            double k = ArmourAngling.Strength.Value;
            Assert.That(slanted.armour, Is.EqualTo(1 + k).Within(1e-9), "twice as thick at 60 deg, by the strength");
            Assert.That(slanted.through, Is.EqualTo(1 - k * 0.5).Within(1e-9));
            var grazing = ArmourAngling.Angling(0.0);
            Assert.That(grazing.armour, Is.EqualTo(1 + (ArmourAngling.MaxObliquity.Value - 1) * k).Within(1e-9), "no thicker than the cap");
            Assert.That(grazing.through, Is.EqualTo(1 - k).Within(1e-9));
            With(ArmourAngling.MaxObliquity, 1.5, () =>
                Assert.That(ArmourAngling.Angling(0.1).armour, Is.EqualTo(1 + 0.5 * k).Within(1e-9), "a lower cap thins the slant's plate"));
            With(ArmourAngling.Strength, 0, () =>
            {
                Assert.That(ArmourAngling.Angling(0.1).armour, Is.EqualTo(1));
                Assert.That(ArmourAngling.Angling(0.1).through, Is.EqualTo(1));
            });
        }

        [Test]
        public void TheSameHitDoesLessStrikingTheHullObliquelyAndTheSameWithAnglingOff()
        {
            var fromStarboard = new DVec3(0, 1, 0); // starboard is -Y: the shot travels from it to port
            double beam = Loss(DVec3.Zero, fromStarboard);
            Assert.That(beam, Is.GreaterThan(0));
            Assert.That(Loss(DVec3.Zero, new DVec3(0, 0, -1)), Is.EqualTo(beam).Within(1e-9), "from above, square on the hull's back, the same");
            double angled = Loss(DVec3.Zero, new DVec3(-1, 1, 0).Normalized);
            Assert.That(angled, Is.LessThan(beam * 0.92), "the same shot at 45 deg off the beam glances and meets more plate");
            double r = Radius(ShipClass.Battleship);
            double curve = Loss(new DVec3(r * 0.9, 0, 0), new DVec3(0, 0, -1));
            Assert.That(curve, Is.LessThan(beam * 0.85), "from above onto the curve of the side, where it rounds away");
            With(ArmourAngling.Strength, 0, () =>
            {
                Assert.That(Loss(DVec3.Zero, new DVec3(-1, 1, 0).Normalized), Is.EqualTo(beam).Within(1e-9), "angling off: the slant makes no difference");
                Assert.That(Loss(new DVec3(r * 0.9, 0, 0), new DVec3(0, 0, -1)), Is.EqualTo(beam).Within(1e-9));
            });
        }

        [Test]
        public void AWeakPointStruckFromItsOpenSideHasNoArmourToAngle()
        {
            var (b, me, _) = Scene();
            me.Combat.ZeroAllShields();
            var sub = me.Combat.Subsystems.First(s => s.Def.IsWeapon && !s.Def.Buried);
            var incoming = new DVec3(-1, 1, 0).Normalized;
            double hull = me.Combat.Hull, hp = sub.Hp;
            b.ApplyDamage(me, sub, 100, me.PointOf(sub.Def), null, 2, incoming);
            double open = hull - me.Combat.Hull + hp - sub.Hp;
            Assert.That(open, Is.GreaterThan(0));
            With(ArmourAngling.Strength, 0, () =>
            {
                var (b2, me2, _) = Scene();
                me2.Combat.ZeroAllShields();
                var sub2 = me2.Combat.Subsystems[sub.Index];
                double hull2 = me2.Combat.Hull, hp2 = sub2.Hp;
                b2.ApplyDamage(me2, sub2, 100, me2.PointOf(sub2.Def), null, 2, incoming);
                Assert.That(hull2 - me2.Combat.Hull + hp2 - sub2.Hp, Is.EqualTo(open).Within(1e-9));
            });
        }

        // ------------------------------------------------------------ the captain

        [Test]
        public void TheHarmGettingHomeFallsTheMoreSlantedTheHullLiesToTheFire()
        {
            var spec = CombatSpec.Of(ShipClass.Battleship);
            double beam = ArmourAngling.Harm(spec, new DVec3(1, 0, 0));
            double slanted = ArmourAngling.Harm(spec, new DVec3(1, 0, 1).Normalized);
            Assert.That(beam, Is.LessThan(1), "even beam on, the curve of the hull turns some aside");
            Assert.That(slanted, Is.LessThan(beam - 0.02), "slanted 45 deg to the fire, more glances off");
            With(ArmourAngling.Strength, 0, () => Assert.That(ArmourAngling.Harm(spec, new DVec3(1, 0, 1).Normalized), Is.EqualTo(1)));
        }

        /// <summary>Shoots away every gun of <paramref name="u"/> that bears ahead or astern: what it has left bears on the beams.</summary>
        static void BeamGunsOnly(BattleUnit u)
        {
            foreach (var s in u.Combat.Subsystems)
                if (s.Def.IsWeapon && (s.Def.Covers(u.Combat.Spec, FireCones.Bow) || s.Def.Covers(u.Combat.Spec, FireCones.Stern))) s.Hp = 0;
        }

        static double OffBowDeg(double weight, out bool bears)
        {
            double result = 0;
            bool any = false;
            // Its weak patches left out of it (they would turn it too as its shields go down; the rolling weighs the slant on
            // its own, below): only the captain's choice of heading for its guns.
            bool rolls = WeakPatches.AiRolls.Value;
            WeakPatches.AiRolls.Set(false);
            try
            {
                With(ArmourAngling.AiWeight, weight, () =>
                {
                    var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 4);
                    var blue = b.AddShip(Hull(ShipClass.Battleship, Blue, 0), 0, new DVec3(-8, 0, 0), DVec3.UnitX);
                    // The enemy fights back: there is fire to slant the hull to.
                    var red = b.AddShip(Hull(ShipClass.Battleship, Red, 1), 1, new DVec3(8, 0, 0), -DVec3.UnitX);
                    BeamGunsOnly(blue);
                    for (double t = 0; t < 60 && !b.Over; t += 0.5) b.Advance(0.5);
                    var to = (red.Pos - blue.Pos).Normalized;
                    result = Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(blue.Forward, to)))) * Astro.Rad2Deg;
                    // Whether a beam gun's cone holds the target (the fight may have shot the gun itself away by now).
                    any = blue.Combat.Subsystems.Any(s => s.Def.IsWeapon && !s.Def.Spinal && !s.Def.Weapon.AntiSmall && !s.Def.Covers(blue.Combat.Spec, FireCones.Bow) && !s.Def.Covers(blue.Combat.Spec, FireCones.Stern) && !s.Def.BearsEverywhere && blue.Bears(s.Def, red.Pos));
                });
            }
            finally
            {
                WeakPatches.AiRolls.Set(rolls);
            }
            bears = any;
            return result;
        }

        [Test]
        public void AnAiCaptainKeepsItsFlankInButSlantedToTheFire()
        {
            double square = OffBowDeg(0, out bool squareBears);
            double slanted = OffBowDeg(1, out bool slantedBears);
            TestContext.Out.WriteLine($"off the bow: {square:F1} deg ignoring the slant, {slanted:F1} deg weighing it");
            Assert.That(Math.Abs(square - 90), Is.LessThan(12), "ignoring the slant it lays its beam square to the target");
            Assert.That(squareBears, Is.True);
            Assert.That(Math.Abs(slanted - 90), Is.GreaterThan(18), "weighing it, it angles its hull off the line of fire");
            Assert.That(slanted, Is.InRange(20.0, 160.0), "a flank still in, not its nose or stern");
            Assert.That(slantedBears, Is.True, "while its sponsons' cones still hold the target");
        }

        [Test]
        public void TheWeakPatchRollingWeighsPresentingASlantedHull()
        {
            double Slant(double weight, out bool bears)
            {
                double harm = 0;
                bool any = false;
                With(ArmourAngling.AiWeight, weight, () =>
                {
                    var (b, me, enemy) = Scene();
                    enemy.Stunned = 0;
                    foreach (var s in enemy.Combat.Subsystems) s.Hp = s.Def.MaxHp;
                    BeamGunsOnly(me);
                    me.Target = enemy;
                    var toTarget = (enemy.Pos - me.Pos).Normalized;
                    var bearings = Battle.ConeFirepower(me, toTarget, weight > 0);
                    WeakPatches.Orient(b, me, toTarget, bearings, toTarget, null, 0.1, out var forward, out var roof);
                    me.Forward = forward;
                    me.Roof = roof;
                    harm = ArmourAngling.Harm(me.Combat.Spec, me.DirToLocal(enemy.Pos - me.Pos));
                    any = me.Combat.Subsystems.Any(s => Broadsides.IsBatteryGun(s) && !s.Destroyed && !s.Def.BearsEverywhere && me.Bears(s.Def, enemy.Pos));
                });
                bears = any;
                return harm;
            }
            double square = Slant(0, out bool squareBears), slanted = Slant(1, out bool slantedBears);
            TestContext.Out.WriteLine($"share of the harm getting home: {square:F3} ignoring the slant, {slanted:F3} weighing it");
            Assert.That(squareBears && slantedBears, Is.True, "its guns bear either way");
            Assert.That(slanted, Is.LessThan(square - 0.01), "weighing it, it shows the fire a slanted hull");
        }

        [Test]
        public void TheAnglingFiguresAreSettingsInBroadsides()
        {
            Assert.That(SettingsRegistry.Default.Find("broadside.armourAngling"), Is.SameAs(ArmourAngling.Strength));
            Assert.That(SettingsRegistry.Default.Find("broadside.maxObliquity"), Is.SameAs(ArmourAngling.MaxObliquity));
            Assert.That(SettingsRegistry.Default.Find("broadside.aiAngleHull"), Is.SameAs(ArmourAngling.AiWeight));
            Assert.That(ArmourAngling.Strength.Group, Is.EqualTo(SettingGroups.Broadsides));
            Assert.That(ArmourAngling.MaxObliquity.Group, Is.EqualTo(SettingGroups.Broadsides));
            Assert.That(ArmourAngling.AiWeight.Group, Is.EqualTo(SettingGroups.Broadsides));
        }
    }
}
