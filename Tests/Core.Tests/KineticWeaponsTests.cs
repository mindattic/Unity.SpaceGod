using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// A railgun slug or an autocannon round (<see cref="Battle.KineticRound"/>) is a dumb, non-homing
    /// shot: real travel time across the range, a lead on the target's current velocity decided at the
    /// muzzle, and a miss that keeps flying rather than never having existed. These tests exercise that
    /// model directly through <see cref="Battle.Rounds"/>, independent of how Runtime chooses to draw it.
    /// </summary>
    [TestFixture]
    public class KineticWeaponsTests
    {
        const double StartJd = 2461324.0;


        static Ship Hull(ShipClass c, Faction f, int id) => new Ship
        {
            Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c),
        };

        /// <summary>Stops every weapon on a hull from firing on its own, except (optionally) one mount
        /// held open -- so a test can watch a single hardpoint's shot in isolation.</summary>
        static void Silence(BattleUnit u, Subsystem except)
        {
            foreach (var s in u.Combat.Subsystems)
                if (s.Def.IsWeapon && s != except) s.Cooldown = 9999;
        }

        static (Battle battle, BattleUnit shooter, BattleUnit target, Subsystem rail) RailgunDuel(int seed, DVec3 targetPos, DVec3 targetForward)
        {
            var blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
            var red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed);
            var shooter = b.AddShip(Hull(ShipClass.Destroyer, blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            var target = b.AddShip(Hull(ShipClass.Destroyer, red, 1), 1, targetPos, targetForward);
            var rail = shooter.Combat.Subsystems.First(s => s.Kind == SubsystemKind.Railgun);
            Silence(shooter, rail);
            Silence(target, null);
            return (b, shooter, target, rail);
        }

        [Test]
        public void ARailgunRoundTakesRealTimeToCrossItsRangeBeforeAnyDamageLands()
        {
            var (b, shooter, target, rail) = RailgunDuel(11, new DVec3(25, 0, 0), -DVec3.UnitX);
            target.Vel = DVec3.Zero;
            double initialHull = target.Combat.Hull;

            // Let the round launch (the AI needs its first Think() tick to pick a target).
            for (int i = 0; i < 10 && b.Rounds.Count == 0; i++) b.Advance(0.1);
            Assert.That(b.Rounds.Count == 1, Is.True, "the railgun never launched a round");
            var round = b.Rounds[0];
            Assert.That(round.ArriveIn > 1, Is.True, $"a 25 km shot at 14 km/s should take well over a second to arrive, not {round.ArriveIn:0.##}s");

            bool hullChangedWhileInFlight = false;
            int stepsInFlight = 0;
            for (int i = 0; i < 40 && b.Rounds.Count > 0; i++)
            {
                stepsInFlight++;
                b.Advance(0.1);
                if (b.Rounds.Count > 0 && Math.Abs(target.Combat.Hull - initialHull) > 1e-6) hullChangedWhileInFlight = true;
            }
            Assert.That(stepsInFlight > 1, Is.True, "the round resolved in a single step -- no real travel time");
            Assert.That(hullChangedWhileInFlight, Is.False, "damage landed on the hull before the round's own travel time had elapsed");
        }

        [Test]
        public void AMissedRoundFliesPastItsAimPointInsteadOfJustVanishing()
        {
            bool foundMiss = false;
            for (int seed = 1; seed <= 200 && !foundMiss; seed++)
            {
                var (b, shooter, target, rail) = RailgunDuel(seed, new DVec3(20, 0, 0), -DVec3.UnitX);
                target.Vel = DVec3.Zero;
                b.Advance(1);
                if (b.Rounds.Count != 1 || b.Rounds[0].Hit) continue;
                foundMiss = true;
                var round = b.Rounds[0];
                var posAtLaunch = round.Pos;
                // Fast-forward well past when a hit would have arrived; a miss should still be live,
                // still moving, and further from the muzzle than when it launched.
                for (int i = 0; i < 20 && b.Rounds.Contains(round); i++) b.Advance(0.1);
                TestContext.Out.WriteLine($"seed {seed}: miss round life left {round.Life:0.##}s, pos {round.Pos}");
            }
            Assert.That(foundMiss, Is.True, "never saw a railgun miss across 200 seeds");
        }

        [Test]
        public void AKineticRoundLeadsAMovingTargetInsteadOfAimingWhereItCurrentlyIs()
        {
            bool foundHit = false;
            for (int seed = 1; seed <= 200 && !foundHit; seed++)
            {
                var (b, shooter, target, rail) = RailgunDuel(seed, new DVec3(20, 0, 0), -DVec3.UnitX);
                // An unrealistically fast crossing velocity, perpendicular to the line of fire, so the
                // lead signal is far larger than any noise from which hardpoint happened to be aimed at.
                target.Vel = new DVec3(0, 5, 0);

                b.Advance(1);
                if (b.Rounds.Count != 1 || !b.Rounds[0].Hit) continue;
                foundHit = true;
                var round = b.Rounds[0];

                var straight = (target.Pos - shooter.Pos).Normalized;
                var led = round.Vel.Normalized;
                var alongVel = target.Vel.Normalized;
                double straightAlongVel = DVec3.Dot(straight, alongVel);
                double ledAlongVel = DVec3.Dot(led, alongVel);
                TestContext.Out.WriteLine($"seed {seed}: straight-line component {straightAlongVel:0.####}, led component {ledAlongVel:0.####}");
                Assert.That(ledAlongVel > straightAlongVel + 0.05, Is.True, $"seed {seed}: the round should aim ahead of the target's motion, not straight at where it was standing");
            }
            Assert.That(foundHit, Is.True, "never got a railgun hit to check the lead against, across 200 seeds");
        }

        [Test]
        public void AKineticWeaponMissesASmallEvasiveTargetMoreOftenThanALargeSlowOne()
        {
            int HitsOutOf(double targetLengthKm, int seeds)
            {
                int hits = 0;
                for (int seed = 1; seed <= seeds; seed++)
                {
                    var (b, shooter, target, rail) = RailgunDuel(seed, new DVec3(18, 0, 0), -DVec3.UnitX);
                    target.Vel = DVec3.Zero;
                    target.LengthKm = targetLengthKm;
                    b.Advance(1);
                    if (b.Rounds.Count == 1 && b.Rounds[0].Hit) hits++;
                }
                return hits;
            }

            const int seeds = 150;
            int smallHits = HitsOutOf(0.1, seeds);
            int largeHits = HitsOutOf(0.45, seeds);
            TestContext.Out.WriteLine($"small/evasive hits {smallHits}/{seeds}, large/slow hits {largeHits}/{seeds}");
            Assert.That(smallHits < largeHits, Is.True, "a small, evasive target should be harder for a kinetic round to hit than a large, slow one");
        }

        [Test]
        public void AKineticRoundThatArrivesAfterItsTargetDiedDealsNoDamage()
        {
            var (b, shooter, target, rail) = RailgunDuel(3, new DVec3(25, 0, 0), -DVec3.UnitX);
            target.Vel = DVec3.Zero;

            for (int i = 0; i < 10 && b.Rounds.Count == 0; i++) b.Advance(0.1);
            Assert.That(b.Rounds.Count == 1, Is.True, "the railgun never launched a round");
            var round = b.Rounds[0];
            round.Hit = true; // force a hit roll so a no-op from a dead target is the only reason it's skipped

            target.Dead = true; // the target dies to something else while the round is still in flight
            double hullAtDeath = target.Combat.Hull;
            for (int i = 0; i < 40 && b.Rounds.Contains(round); i++) b.Advance(0.1);
            Assert.That(target.Combat.Hull, Is.EqualTo(hullAtDeath));
        }
    }
}
