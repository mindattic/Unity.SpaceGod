using System;
using System.Linq;
using SolarSystem.Core;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    /// <summary>
    /// A capital ship at the helm (U.SPC-T272): its crews work every mount on their own, going for the hull the player
    /// has locked, and the player calls broadsides, the battery bearing where they look holding fire while the trigger
    /// is held and firing together, harder, when it is let go.
    /// </summary>
    [TestFixture]
    public class HelmTests
    {
        const double StartJd = 2461324.0;

        static Ship Hull(ShipClass c, Faction f, int id) => new Ship
        {
            Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c),
        };

        static (Battle b, BattleUnit me, BattleUnit[] them) Fight(params DVec3[] enemies)
        {
            var blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
            var red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 1);
            b.AddShip(Hull(ShipClass.Battleship, blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            for (int i = 0; i < enemies.Length; i++) b.AddShip(Hull(ShipClass.Cruiser, red, i + 1), 1, enemies[i], -DVec3.UnitX);
            var me = b.Units[0];
            me.PlayerControlled = true;
            me.PlayerHelm = true;
            return (b, me, b.Units.Skip(1).ToArray());
        }

        static void Hold(Battle b, BattleUnit me, BattleUnit[] them, DVec3[] at, double seconds)
        {
            for (double t = 0; t < seconds; t += 0.1)
            {
                me.Pos = DVec3.Zero;
                me.Vel = DVec3.Zero;
                me.Forward = DVec3.UnitX;
                for (int i = 0; i < them.Length; i++)
                {
                    them[i].Pos = at[i];
                    them[i].Vel = DVec3.Zero;
                }
                b.Advance(0.1);
            }
        }

        [Test]
        public void AtTheHelmEveryMountFightsOnItsOwnWithNoTriggerHeld()
        {
            var at = new[] { new DVec3(12, 0, 0) };
            var (b, me, them) = Fight(at);
            var bow = me.Combat.Subsystems.Where(s => s.Def.Weapon != null && !s.Def.Weapon.Missile && !s.Def.Weapon.AntiSmall && s.Def.ContainsYaw(0)).ToList();
            Assert.That(bow, Is.Not.Empty);
            bool fired = false;
            for (int i = 0; i < 100 && !fired; i++)
            {
                var before = bow.Select(s => s.Cooldown).ToArray();
                Hold(b, me, them, at, 0.1);
                fired = bow.Where((s, k) => s.Cooldown > before[k] + 1e-9).Any();
            }
            Assert.That(fired, Is.True, "a capital ship's bow guns waited on a trigger at the helm");
        }

        [Test]
        public void HoldingTheTriggerHoldsTheBatteryAndLettingGoFiresItTogetherHarder()
        {
            var at = new[] { new DVec3(0, -10, 0) };   // dead abeam to starboard
            var (b, me, them) = Fight(at);
            me.PlayerVolleyAim = -DVec3.UnitY;
            var starboard = me.Combat.Subsystems.Where(s => s.Def.Weapon != null && !s.Def.Weapon.Missile && !s.Def.Weapon.AntiSmall && !s.Def.Weapon.Supporting && s.Def.ContainsYaw(90)).ToList();
            Assert.That(starboard, Is.Not.Empty, "the battleship has no starboard battery to fire");

            me.PlayerFireHeld = true;
            Hold(b, me, them, at, Battle.VolleyBuildSeconds + 0.5);
            Assert.That(starboard.All(s => s.Cooldown <= 0), Is.True, "a gun held for the volley fired anyway");
            Assert.That(me.VolleyCharge, Is.EqualTo(Battle.VolleyBuildSeconds).Within(1e-9));

            me.PlayerFireHeld = false;
            me.PlayerVolleyRelease = true;
            int before = b.Fx.Count;
            Hold(b, me, them, at, 0.1);
            var shots = b.Fx.Skip(before).Where(f => (f.Kind == FxKind.Beam || f.Kind == FxKind.BeamMiss || f.Kind == FxKind.Railgun || f.Kind == FxKind.Guns) && f.Side == me.Side).ToList();
            int fired = starboard.Count(s => s.Cooldown > 0);
            Assert.That(fired, Is.EqualTo(starboard.Count), "not every loaded gun of the battery fired with the volley");
            var hits = shots.Where(f => f.Kind == FxKind.Beam).ToList();
            Assert.That(hits, Is.Not.Empty, "the volley landed nothing on a cruiser dead abeam");
            Assert.That(hits.Max(f => f.Size), Is.GreaterThan(WeaponSpec.Laser.Damage * (1 + Battle.VolleyDamageBonus) - 1e-6), "a full volley hit no harder than a gun at will");
            Assert.That(me.VolleyCharge, Is.EqualTo(0), "the next volley does not build from nothing");
        }

        [Test]
        public void TheCrewsGoForTheLockedHullOverANearerOne()
        {
            var at = new[] { new DVec3(8, 0, 0), new DVec3(16, 0.5, 0) };
            var (b, me, them) = Fight(at);
            me.PlayerLockTarget = them[1];
            Hold(b, me, them, at, 30);
            int atLocked = b.Fx.Count(f => (f.Kind == FxKind.Beam || f.Kind == FxKind.Impact || f.Kind == FxKind.ShieldHit) && f.Unit == them[1]);
            int atNearer = b.Fx.Count(f => (f.Kind == FxKind.Beam || f.Kind == FxKind.Impact || f.Kind == FxKind.ShieldHit) && f.Unit == them[0]);
            TestContext.Out.WriteLine($"locked {atLocked}, nearer {atNearer}");
            Assert.That(atLocked, Is.GreaterThan(atNearer), "the crews kept shooting the nearer hull with another locked");
        }
    }
}
