using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    [TestFixture]
    public class GunneryTests
    {
        const double StartJd = 2461324.0;

        static double AngleDeg(DVec3 a, DVec3 b) => Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(a.Normalized, b.Normalized)))) * Astro.Rad2Deg;

        static Ship Hull(ShipClass c, Faction f, int id) => new Ship
        {
            Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c),
        };

        /// <summary>A player-flown hull at the origin, nose on +X, and one enemy hull where asked.</summary>
        static (Battle b, BattleUnit player, BattleUnit enemy) Duel(ShipClass mine, ShipClass theirs, DVec3 enemyPos, int seed = 1)
        {
            var blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
            var red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed);
            b.AddShip(Hull(mine, blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            b.AddShip(Hull(theirs, red, 1), 1, enemyPos, -DVec3.UnitX);
            var player = b.Units[0];
            player.PlayerControlled = true;
            return (b, player, b.Units[1]);
        }

        // ------------------------------------------------------------ geometry

        [Test]
        public void ARayMeetsASphereWhereItEntersAndMissesOneBesideIt()
        {
            Assert.That(Gunnery.RaySphere(DVec3.Zero, DVec3.UnitX, 100, new DVec3(10, 0, 0), 1), Is.EqualTo(9).Within(1e-9));
            Assert.That(Gunnery.RaySphere(DVec3.Zero, DVec3.UnitX, 100, new DVec3(10, 2, 0), 1), Is.EqualTo(-1));
            Assert.That(Gunnery.RaySphere(DVec3.Zero, DVec3.UnitX, 5, new DVec3(10, 0, 0), 1), Is.EqualTo(-1), "out of reach");
            Assert.That(Gunnery.RaySphere(DVec3.Zero, DVec3.UnitX, 100, new DVec3(-10, 0, 0), 1), Is.EqualTo(-1), "behind the muzzle");
        }

        [Test]
        public void TheLeadPointIsWhereARoundAndItsTargetMeet()
        {
            var target = new DVec3(10, 0, 0);
            var targetVel = new DVec3(0, 0.5, 0);
            const double speed = 3.5;
            var lead = Gunnery.LeadPoint(DVec3.Zero, DVec3.Zero, target, targetVel, speed);
            double t = (lead - target).Length / targetVel.Length;
            Assert.That(lead.Length, Is.EqualTo(speed * t).Within(1e-9), "the round reaches the lead point exactly when the target does");
            Assert.That(lead.Y > 0, Is.True);

            Assert.That(Gunnery.LeadPoint(DVec3.Zero, DVec3.Zero, target, new DVec3(10, 0, 0), speed).Equals(target), Is.True, "a target outrunning the round is aimed at where it is");
        }

        [Test]
        public void AGunSwingsToTheCrosshairOnlyAsFarAsItsGimbal()
        {
            var aim = PilotFlight.RotateAbout(DVec3.UnitX, DVec3.UnitZ, 30 * Astro.Deg2Rad);
            Assert.That(AngleDeg(Gunnery.Gimbal(aim, DVec3.UnitX, Gunnery.GimbalDeg), DVec3.UnitX), Is.EqualTo(Gunnery.GimbalDeg).Within(1e-9));
            var near = PilotFlight.RotateAbout(DVec3.UnitX, DVec3.UnitZ, 5 * Astro.Deg2Rad);
            Assert.That(Gunnery.Gimbal(near, DVec3.UnitX, Gunnery.GimbalDeg).Equals(near), Is.True);
        }

        // ------------------------------------------------------------ the player's guns

        [Test]
        public void ThePlayersBeamHitsWhatTheCrosshairIsOnEvenOffTheNose()
        {
            // 8 degrees off the nose: inside the gimbal, so the crosshair laid on the hull hits it.
            var enemyPos = PilotFlight.RotateAbout(DVec3.UnitX, DVec3.UnitZ, 8 * Astro.Deg2Rad) * 8;
            var (b, player, enemy) = Duel(ShipClass.Battleship, ShipClass.Battleship, enemyPos);
            player.PlayerAim = enemyPos.Normalized;
            player.PlayerFireHeld = true;
            double before = enemy.Combat.Hull + enemy.Combat.Subsystems.Sum(s => s.Hp + s.Shield);
            for (int i = 0; i < 60; i++) b.Advance(0.1);
            double after = enemy.Combat.Hull + enemy.Combat.Subsystems.Sum(s => s.Hp + s.Shield);
            Assert.That(after, Is.LessThan(before), "the crosshair was on the enemy and nothing landed");
            Assert.That(b.Fx.Any(f => f.Kind == FxKind.Beam && f.Unit == enemy), Is.True);
        }

        [Test]
        public void ThePlayersBeamMissesAHullTheCrosshairIsNotOnEvenDeadAhead()
        {
            // The enemy dead ahead, the crosshair laid well beside it: no aim assist pulls the shot on.
            var (b, player, enemy) = Duel(ShipClass.Fighter, ShipClass.Fighter, new DVec3(1.2, 0, 0));
            player.PlayerAim = PilotFlight.RotateAbout(DVec3.UnitX, DVec3.UnitZ, 6 * Astro.Deg2Rad);
            player.PlayerFireHeld = true;
            for (int i = 0; i < 30; i++)
            {
                enemy.Pos = new DVec3(1.2, 0, 0);
                b.Advance(0.1);
            }
            Assert.That(b.Fx.Any(f => (f.Kind == FxKind.Beam || f.Kind == FxKind.Impact) && f.Unit == enemy), Is.False, "a shot landed where the crosshair was not");
        }

        [Test]
        public void AnAutocannonRoundHitsOnlyWhatItFliesThrough()
        {
            var (b, player, enemy) = Duel(ShipClass.Fighter, ShipClass.Fighter, new DVec3(1.0, 0, 0), seed: 3);
            player.PlayerAim = DVec3.UnitX;
            player.PlayerFireHeld = true;
            double hull = enemy.Combat.Hull;
            for (int i = 0; i < 20; i++)
            {
                enemy.Pos = new DVec3(1.0, 0, 0);
                enemy.Vel = DVec3.Zero;
                b.Advance(0.05);
            }
            player.PlayerFireHeld = false;
            for (int i = 0; i < 20; i++)
            {
                enemy.Pos = new DVec3(1.0, 0, 0);
                b.Advance(0.05);
            }
            Assert.That(enemy.Combat.Hull + enemy.Combat.Subsystems.Sum(s => s.Shield), Is.LessThan(hull + CombatSpec.Of(ShipClass.Fighter).Shield), "rounds aimed straight at a still target never landed");

            // The same burst at a hull that slid out of the line after the trigger was let go: the rounds in flight miss.
            var (b2, p2, e2) = Duel(ShipClass.Fighter, ShipClass.Fighter, new DVec3(1.0, 0, 0), seed: 3);
            p2.PlayerAim = DVec3.UnitX;
            p2.PlayerFireHeld = true;
            b2.Advance(0.05);
            p2.PlayerFireHeld = false;
            double full = e2.Combat.Hull;
            double shields = e2.Combat.Subsystems.Sum(s => s.Shield);
            e2.Pos = new DVec3(1.0, 0.3, 0);
            for (int i = 0; i < 40; i++)
            {
                e2.Pos = new DVec3(1.0, 0.3, 0);
                b2.Advance(0.05);
            }
            Assert.That(e2.Combat.Hull, Is.EqualTo(full));
            Assert.That(e2.Combat.Subsystems.Sum(s => s.Shield), Is.EqualTo(shields).Within(1e-9));
        }

        [Test]
        public void SustainedFireOverheatsTheGunsUntilTheyCool()
        {
            var (b, player, _) = Duel(ShipClass.Fighter, ShipClass.Fighter, new DVec3(0, 1.5, 0));
            player.PlayerAim = DVec3.UnitX;
            player.PlayerFireHeld = true;
            double t = 0;
            while (!player.Overheated && t < 60)
            {
                b.Advance(0.05);
                t += 0.05;
            }
            Assert.That(player.Overheated, Is.True, "a minute of held fire never overheated the guns");
            Assert.That(t, Is.InRange(2, 20), "overheated far too soon or too late");

            var gun = player.Combat.Subsystems.First(s => s.Kind == SubsystemKind.Autocannon);
            double cooldown = gun.Cooldown;
            b.Advance(0.5);
            Assert.That(gun.Cooldown, Is.LessThanOrEqualTo(cooldown), "an overheated gun fired");

            player.PlayerFireHeld = false;
            for (int i = 0; i < 200 && player.Overheated; i++) b.Advance(0.05);
            Assert.That(player.Overheated, Is.False, "the guns never cooled");
        }

        // ------------------------------------------------------------ missiles

        [Test]
        public void AMissileFliesOnlyOnceTheLockIsMade()
        {
            var (b, player, enemy) = Duel(ShipClass.Destroyer, ShipClass.Destroyer, new DVec3(20, 0, 0));
            player.PlayerLockTarget = enemy;
            player.PlayerSecondaryHeld = true;
            // The crosshair off the target: the lock never builds and nothing launches.
            player.PlayerAim = PilotFlight.RotateAbout(DVec3.UnitX, DVec3.UnitZ, 20 * Astro.Deg2Rad);
            for (int i = 0; i < 40; i++)
            {
                player.Forward = DVec3.UnitX;
                b.Advance(0.1);
            }
            Assert.That(player.LockProgress, Is.EqualTo(0));
            Assert.That(b.Missiles.Any(m => m.Side == player.Side), Is.False, "a missile flew with no lock");

            player.PlayerAim = DVec3.UnitX;
            for (int i = 0; i < 25; i++)
            {
                player.Forward = DVec3.UnitX;
                b.Advance(0.1);
            }
            Assert.That(player.LockProgress, Is.EqualTo(1).Within(1e-9), "holding the crosshair on the target made no lock");
            Assert.That(b.Missiles.Any(m => m.Side == player.Side), Is.True, "the lock was made and the trigger held, yet nothing launched");
        }

        // ------------------------------------------------------------ fire at the player

        [Test]
        public void GunnersHitAHullFlyingStraightFarMoreOftenThanOneJinkingAcrossTheirSights()
        {
            double Damage(bool jink, int seed)
            {
                // A fighter crossing a destroyer's bow at full speed, steadily or reversing every second or so.
                var (b, player, enemy) = Duel(ShipClass.Fighter, ShipClass.Destroyer, new DVec3(4, 0, 0), seed);
                enemy.Forward = -DVec3.UnitX;
                // Guns only: a homing missile is not dodged by jinking.
                foreach (var s in enemy.Combat.Subsystems) s.Ammo = 0;
                var c = player.Combat;
                double Health() => c.Hull + c.Subsystems.Sum(s => s.Hp + s.Shield);
                double taken = 0;
                double t = 0;
                for (int i = 0; i < 150; i++)
                {
                    double vy = 0.4 * (jink ? Math.Sign(Math.Sin(t * 2.6)) : 1);
                    player.Vel = new DVec3(0, vy, 0);
                    player.Pos += player.Vel * 0.1;
                    player.Forward = DVec3.UnitX;
                    enemy.Pos = new DVec3(4, 0, 0);
                    enemy.Vel = DVec3.Zero;
                    // Full health each step, so the tally is every hit landed, not just those before the fighter dies.
                    c.Hull = c.Spec.Hull;
                    double before = Health();
                    b.Advance(0.1);
                    taken += before - Health();
                    t += 0.1;
                    if (b.Over || !player.Present) break;
                }
                return taken;
            }
            double straight = 0, jinking = 0;
            for (int seed = 1; seed <= 12; seed++)
            {
                straight += Damage(false, seed);
                jinking += Damage(true, seed);
            }
            TestContext.Out.WriteLine($"damage taken: straight {straight:0}, jinking {jinking:0}");
            Assert.That(straight, Is.GreaterThan(0), "a hull flying straight was never hit");
            Assert.That(jinking, Is.LessThan(straight * 0.75), "jinking across the gunners' sights made no difference");
        }
    }
}
