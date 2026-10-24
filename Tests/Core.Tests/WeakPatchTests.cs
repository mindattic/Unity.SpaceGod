using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Large ships roll to protect their weak spots (U.SPC-T502): a capital rolls and turns its hull to keep damaged,
    /// unshielded or holed patches away from the heaviest incoming fire, trading that against keeping its batteries
    /// bearing; the AI weighs where the fire comes from, and the player gets a readout of the weak patches and an order to
    /// roll them away. Changes settings, so it runs on its own.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class WeakPatchTests
    {
        const double StartJd = 2461324.0;

        static readonly Faction Blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
        static readonly Faction Red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };

        static Ship Hull(ShipClass c, Faction f, int id) => new Ship
        {
            Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c),
        };

        /// <summary>Shoots away every shield emitter on the starboard side (the hull frame's +X) of <paramref name="u"/>.</summary>
        static void HoleStarboard(BattleUnit u)
        {
            foreach (var s in u.Combat.Subsystems)
                if (s.Kind == SubsystemKind.ShieldGenerator && s.Def.X > 0.05)
                {
                    s.Hp = 0;
                    s.Shield = 0;
                }
        }

        /// <summary>A battleship flown by its crew at the origin, bow along +X (starboard is then -Y), with a hostile battleship
        /// dead ahead and another off its starboard beam, both armed.</summary>
        static (Battle b, BattleUnit me, BattleUnit ahead, BattleUnit abeam) Scene(int seed = 5)
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed);
            var me = b.AddShip(Hull(ShipClass.Battleship, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            var ahead = b.AddShip(Hull(ShipClass.Battleship, Red, 1), 1, new DVec3(15, 0, 0), -DVec3.UnitX);
            var abeam = b.AddShip(Hull(ShipClass.Battleship, Red, 2), 1, new DVec3(0, -15, 0), DVec3.UnitY);
            me.Target = ahead;
            return (b, me, ahead, abeam);
        }

        static double LevelExposure(BattleUnit u, System.Collections.Generic.IReadOnlyList<IncomingFire> fire)
        {
            var roof = u.Roof;
            u.Roof = DVec3.Zero;
            double e = WeakPatches.Exposure(u, fire);
            u.Roof = roof;
            return e;
        }

        static void WithProtection(double value, Action body)
        {
            double saved = WeakPatches.Protection.Value;
            try
            {
                WeakPatches.Protection.Set(value);
                body();
            }
            finally
            {
                WeakPatches.Protection.Set(saved);
            }
        }

        // ------------------------------------------------------------ the patches

        [Test]
        public void TheHelmNamesEachPatchAndCallsOutTheWeakOnesAndTheOneUnderFire()
        {
            var (b, me, ahead, abeam) = Scene();
            b.Units.Remove(ahead);
            HoleStarboard(me);
            var patches = WeakPatches.Readout(b, me);
            Assert.That(patches.Count, Is.EqualTo(me.Combat.Subsystems.Count(s => s.Kind == SubsystemKind.ShieldGenerator)));
            Assert.That(patches.Select(p => p.Name), Is.Unique);
            Assert.That(patches.Select(p => p.Name), Does.Contain("dorsal bow").And.Contain("dorsal stern").And.Contain("starboard beam").And.Contain("port quarter"));
            foreach (var p in patches)
            {
                bool starboard = p.Emitter.Def.X > 0.05;
                Assert.That(p.Holed, Is.EqualTo(starboard), p.Name);
                Assert.That(p.Weak, Is.EqualTo(starboard), p.Name);
            }
            var under = patches.OrderByDescending(p => p.Fire).First();
            Assert.That(under.Name, Does.StartWith("starboard"), "the hostile off the starboard beam fires into the starboard side");
            Assert.That(patches.Sum(p => p.Fire), Is.EqualTo(1).Within(1e-9));

            // A field shot down is weak too, though its emitter stands.
            var bow = patches.First(p => p.Name == "dorsal bow").Emitter;
            bow.Shield = bow.MaxShield * 0.2;
            Assert.That(WeakPatches.Readout(b, me).First(p => p.Name == "dorsal bow").Weak, Is.True);
            Assert.That(WeakPatches.Weakness(bow), Is.EqualTo(0.8).Within(1e-9));
        }

        [Test]
        public void TheWeakShareOfTheFaceShownTheFireFallsWhenTheHullRollsItsHolesAway()
        {
            var (b, me, ahead, _) = Scene();
            b.Units.Remove(ahead);
            var fire = WeakPatches.IncomingFireOn(b, me);
            Assert.That(fire.Count, Is.EqualTo(1));
            Assert.That(WeakPatches.Exposure(me, fire), Is.EqualTo(0).Within(1e-9), "whole, there is nothing to hide");
            HoleStarboard(me);
            double level = WeakPatches.Exposure(me, fire);
            Assert.That(level, Is.GreaterThan(0.5), "starboard to the fire, the holes face it");
            me.Roof = -DVec3.UnitZ; // rolled over: the port side faces the fire now
            Assert.That(WeakPatches.Exposure(me, fire), Is.LessThan(level * 0.3));
        }

        // ------------------------------------------------------------ the AI captain

        [Test]
        public void AFreshHullFightsAsItAlwaysHas()
        {
            // Not weighing the slant of its hull to the fire (U.SPC-T534, see ArmourAnglingTests), which may roll it.
            double saved = ArmourAngling.AiWeight.Value;
            try
            {
                ArmourAngling.AiWeight.Set(0);
                var (b, me, ahead, _) = Scene();
                var bearings = Battle.ConeFirepower(me, (ahead.Pos - me.Pos).Normalized);
                var toTarget = (ahead.Pos - me.Pos).Normalized;
                WeakPatches.Orient(b, me, DVec3.UnitX, bearings, toTarget, null, 0.1, out var forward, out var roof);
                int best = bearings.OrderByDescending(kv => kv.Value).First().Key;
                Assert.That((forward - Battle.BowFor(toTarget, best).Normalized).Length, Is.LessThan(1e-9), "its strongest battery turned to the target");
                Assert.That((roof - WeakPatches.LevelRoof(forward)).Length, Is.LessThan(1e-9), "and held level");
            }
            finally
            {
                ArmourAngling.AiWeight.Set(saved);
            }
        }

        [Test]
        public void ACaptainRollsAboutTheLineToItsTargetToHideItsHolesFromAnotherShip()
        {
            var (b, me, ahead, _) = Scene();
            HoleStarboard(me);
            var bearings = Battle.ConeFirepower(me, (ahead.Pos - me.Pos).Normalized);
            var toTarget = (ahead.Pos - me.Pos).Normalized;
            WeakPatches.Orient(b, me, DVec3.UnitX, bearings, toTarget, null, 0.1, out var forward, out var roof);
            var fire = WeakPatches.IncomingFireOn(b, me);
            double before = WeakPatches.Exposure(me, fire);
            me.Forward = forward;
            me.Roof = roof;
            Assert.That(WeakPatches.Exposure(me, fire), Is.LessThan(before * 0.5), "the holes turned away from the fire");
            Assert.That(DVec3.Dot(roof, DVec3.UnitZ), Is.LessThan(0.9), "rolled, not held level");
            Assert.That(me.Combat.Subsystems.Any(s => Broadsides.IsBatteryGun(s) && !s.Def.BearsEverywhere && me.Bears(s.Def, ahead.Pos)), Is.True,
                "a gun's fire cone still holds its target");
        }

        [Test]
        public void ProtectionTradesTheBatteriesBearingAgainstTheHoles()
        {
            var (b, me, ahead, abeam) = Scene();
            b.Units.Remove(abeam);
            // Every patch round the bow shot away, and the enemy dead ahead: only by turning a weaker battery to it can the
            // captain show it fresh shields.
            foreach (var s in me.Combat.Subsystems)
                if (s.Kind == SubsystemKind.ShieldGenerator && s.Def.Z > 0.1)
                {
                    s.Hp = 0;
                    s.Shield = 0;
                }
            var bearings = Battle.ConeFirepower(me, (ahead.Pos - me.Pos).Normalized);
            int strongest = bearings.OrderByDescending(kv => kv.Value).First().Key;
            var toTarget = (ahead.Pos - me.Pos).Normalized;
            WithProtection(0, () =>
            {
                me.Broadside = null;
                WeakPatches.Orient(b, me, DVec3.UnitX, bearings, toTarget, null, 0.1, out _, out _);
                Assert.That(me.Broadside.RollBearing, Is.EqualTo(strongest), "with protection off it lays its strongest battery");
            });
            WithProtection(3, () =>
            {
                me.Broadside = null;
                WeakPatches.Orient(b, me, DVec3.UnitX, bearings, toTarget, null, 0.1, out var forward, out var roof);
                Assert.That(me.Broadside.RollBearing, Is.Not.EqualTo(strongest), "valuing its shields most, it turns a fresh side to the enemy");
                me.Forward = forward;
                me.Roof = roof;
                Assert.That(me.Combat.Subsystems.Any(s => Broadsides.IsBatteryGun(s) && !s.Def.BearsEverywhere && me.Bears(s.Def, ahead.Pos)), Is.True,
                    "and fights it with that one");
            });
        }

        [Test]
        public void InBattleAnAiCaptainRollsItsHolesOutOfTheFireAtItsOwnTurnRate()
        {
            var (b, me, _, _) = Scene();
            HoleStarboard(me);
            Assert.That(WeakPatches.Steers(me), Is.True);
            double dt = 0.1;
            b.Advance(dt);
            double turn = me.Combat.Spec.TurnRateDegS * dt * 1.01;
            Assert.That(Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(me.Roof.Normalized, WeakPatches.LevelRoof(me.Forward))))) * Astro.Rad2Deg,
                Is.LessThan(turn + 1e-6), "a roll no faster than the hull turns");
            bool rolled = false;
            for (int i = 0; i < 600 && !b.Over; i++)
            {
                b.Advance(dt);
                rolled |= DVec3.Dot(me.Roof.Normalized, WeakPatches.LevelRoof(me.Forward)) < 0.9;
            }
            Assert.That(rolled, Is.True, "in the fight it rolls off level");
            // Wherever the fight has left its bow, the attitude its captain has rolled to there shows the fire no more of its
            // holes than lying level would (once the fight has drained its shields all round, level may be the best there is).
            var fire = WeakPatches.IncomingFireOn(b, me);
            Assert.That(fire.Count, Is.GreaterThan(0));
            Assert.That(WeakPatches.Exposure(me, fire), Is.LessThan(LevelExposure(me, fire)), "it lies with its holes turned away");

            // With the doctrine off it rolls back to level.
            WeakPatches.AiRolls.Set(false);
            try
            {
                for (int i = 0; i < 600 && !b.Over; i++) b.Advance(dt);
                Assert.That(DVec3.Dot(me.Roof.Normalized, WeakPatches.LevelRoof(me.Forward)), Is.GreaterThan(0.999));
            }
            finally
            {
                WeakPatches.AiRolls.Set(true);
            }
        }

        [Test]
        public void TheBattleFlownHullsFrameRollsWithItsCaptainsRoof()
        {
            var u = new BattleUnit { LengthKm = 1, Forward = DVec3.UnitX };
            u.Roof = DVec3.UnitY;
            u.Basis(out var right, out var up);
            Assert.That((up - DVec3.UnitY).Length, Is.LessThan(1e-9));
            Assert.That((right - DVec3.UnitZ).Length, Is.LessThan(1e-9));
            // At the helm the player's roof rules, not the captain's.
            u.PlayerControlled = true;
            u.Up = DVec3.UnitZ;
            u.Basis(out _, out up);
            Assert.That((up - DVec3.UnitZ).Length, Is.LessThan(1e-9));
        }

        // ------------------------------------------------------------ the helm

        [Test]
        public void OrderedToTheHelmAssistRollsTheWeakPatchesAwayAndThenHolds()
        {
            var (b, me, ahead, _) = Scene();
            b.Units.Remove(ahead);
            me.PlayerControlled = true;
            me.PlayerHelm = true;
            me.Up = DVec3.UnitZ;
            HoleStarboard(me);
            Assert.That(WeakPatches.ToggleOrder(me), Is.True);
            Assert.That(me.Broadside.RollWeakPatches, Is.True);
            var fire = WeakPatches.IncomingFireOn(b, me);
            double before = WeakPatches.Exposure(me, fire);
            const double rate = 6, alpha = 0.5, dt = 0.1;
            double first = WeakPatches.HelmRoll(b, me, me.Forward, me.Up, rate, alpha, dt);
            Assert.That(Math.Abs(first), Is.GreaterThan(0.1), "it puts on a roll");
            for (int i = 0; i < 2000; i++)
            {
                double input = WeakPatches.HelmRoll(b, me, me.Forward, me.Up, rate, alpha, dt);
                me.Up = PilotFlight.RotateAbout(me.Up, me.Forward, input * rate * Astro.Deg2Rad * dt).Normalized;
            }
            Assert.That(WeakPatches.Exposure(me, fire), Is.LessThan(before * 0.5), "the holes rolled out of the fire");
            Assert.That(WeakPatches.HelmRoll(b, me, me.Forward, me.Up, rate, alpha, dt), Is.EqualTo(0), "and there it holds");
            Assert.That(WeakPatches.ToggleOrder(me), Is.False, "the order taken back");
        }

        [Test]
        public void EveryFigureIsASettingInBroadsides()
        {
            Assert.That(SettingsRegistry.Default.Find("broadside.aiRollPatches"), Is.SameAs(WeakPatches.AiRolls));
            Assert.That(SettingsRegistry.Default.Find("broadside.patchProtection"), Is.SameAs(WeakPatches.Protection));
            Assert.That(WeakPatches.AiRolls.Group, Is.EqualTo(SettingGroups.Broadsides));
            Assert.That(WeakPatches.Protection.Group, Is.EqualTo(SettingGroups.Broadsides));
        }
    }
}
