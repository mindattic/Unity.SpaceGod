using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    [TestFixture]
    public class CombatTests
    {
        const double StartJd = 2461324.0;


        static Ship Hull(ShipClass c, Faction f, int id) => new Ship
        {
            Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c),
        };

        static (Battle battle, Faction blue, Faction red) Skirmish(int seed, (ShipClass, int side)[] ships)
        {
            var blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
            var red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed);
            int id = 0;
            foreach (var (c, side) in ships)
            {
                var ship = Hull(c, side == 0 ? blue : red, id++);
                var pos = new DVec3(side == 0 ? 13 : -13, (id % 3 - 1) * 3.0, 0);
                b.AddShip(ship, side, pos, side == 0 ? -DVec3.UnitX : DVec3.UnitX);
            }
            return (b, blue, red);
        }

        [Test]
        public void TheStrongerGroupWinsAndHardpointsTakeTheDamage()
        {
            int blueWins = 0;
            bool anyBeam = false, anyMissileOrImpact = false;
            for (int seed = 1; seed <= 6; seed++)
            {
                var (b, _, _) = Skirmish(seed, new[]
                {
                    (ShipClass.Battleship, 0), (ShipClass.Cruiser, 0), (ShipClass.Destroyer, 0),
                    (ShipClass.Destroyer, 1), (ShipClass.Destroyer, 1),
                });
                for (int i = 0; i < 20000 && !b.Over; i++) b.Advance(1);
                Assert.That(b.Over, Is.True, "battle never ended");
                if (b.Winner == 0) blueWins++;
                var subsHit = b.Units.SelectMany(u => u.Combat.Subsystems).Count(s => s.Hp < s.Def.MaxHp);
                Assert.That(subsHit > 0, Is.True, "no hardpoint was ever damaged");
                // A single fast-resolving seed can end (e.g. via an early boarding) before slower, longer-
                // cooldown beam weapons ever get a turn; only require beam fire somewhere across the sample.
                anyBeam |= b.Fx.Any(f => f.Kind == FxKind.Beam);
                anyMissileOrImpact |= b.Fx.Any(f => f.Kind == FxKind.MissileLaunch || f.Kind == FxKind.Impact);
                TestContext.Out.WriteLine($"seed {seed}: winner {b.Winner} after {b.Seconds / 60:0} min, dead {b.Units.Count(u => u.Dead)}, fled {b.Units.Count(u => u.Fled)}");
            }
            Assert.That(blueWins >= 5, Is.True, $"the battleship group won only {blueWins} of 6");
            Assert.That(anyBeam, Is.True, "no beam weapon ever fired across any seed");
            Assert.That(anyMissileOrImpact, Is.True, "no missile or impact ever happened across any seed");
        }

        [Test]
        public void ABattleshipWithItsForwardBatteryKnockedOutTurnsBroadsideInstead()
        {
            var blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
            var red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 7);
            var battleship = Hull(ShipClass.Battleship, blue, 0);
            // Knock out the forward/aft spinal HeavyLasers and the bow torpedo tubes (U.SPC-T377) so a broadside battery
            // becomes the strongest arc.
            foreach (var s in battleship.Combat.Subsystems)
                if (s.Kind == SubsystemKind.HeavyLaser || s.Kind == SubsystemKind.TorpedoTube) s.Hp = 0;
            var target = Hull(ShipClass.Destroyer, red, 1);
            var bu = b.AddShip(battleship, 0, DVec3.Zero, DVec3.UnitX);
            var tu = b.AddShip(target, 1, new DVec3(20, 0, 0), -DVec3.UnitX);

            // Mid-fight, while the destroyer is within the broadside's reach (it later opens the range,
            // and a hull with nothing in reach noses in to close it again, turning at its full rate).
            for (int i = 0; i < 120; i++) b.Advance(1);

            double angleToTarget = Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(bu.Forward, (tu.Pos - bu.Pos).Normalized)))) * Astro.Rad2Deg;
            TestContext.Out.WriteLine($"battleship forward={bu.Forward}, angle to target={angleToTarget:0.#} deg");
            Assert.That(angleToTarget > 30, Is.True, "the battleship still pointed its bow at the target instead of presenting a broadside");
        }

        [Test]
        public void AHealthyBattleshipStillNosesInBecauseItsSpinalBatteryIsStrongest()
        {
            var blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
            var red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 7);
            var battleship = Hull(ShipClass.Battleship, blue, 0);
            var target = Hull(ShipClass.Destroyer, red, 1);
            var bu = b.AddShip(battleship, 0, DVec3.Zero, new DVec3(0, 1, 0));
            var tu = b.AddShip(target, 1, new DVec3(20, 0, 0), new DVec3(0, -1, 0));

            for (int i = 0; i < 200; i++) b.Advance(1);

            double angleToTarget = Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(bu.Forward, (tu.Pos - bu.Pos).Normalized)))) * Astro.Rad2Deg;
            TestContext.Out.WriteLine($"battleship forward={bu.Forward}, angle to target={angleToTarget:0.#} deg");
            Assert.That(angleToTarget < 20, Is.True, "a fully healthy battleship should still point its strongest (spinal) battery at the target");
        }

        [Test]
        public void AShieldHitNamesTheGeneratorWhoseArcTookItAndACollapsedArcRaisesNoBubble()
        {
            // U.SPC-T40: the bubble a shield hit raises is drawn on the struck arc, in that generator's colour at
            // its charge, so the hit has to say which generator it was; a collapsed arc lets hits through to the hull.
            var (b, _, _) = Skirmish(3, new[] { (ShipClass.Cruiser, 0), (ShipClass.Cruiser, 1) });
            var target = b.Units[1];
            var bowPoint = target.PointLocal(new DVec3(0, 0, 0.5));
            var gen = target.Combat.GeneratorFor(target.YawDegOf(bowPoint));
            Assert.That(gen, Is.Not.Null, "the cruiser's bow arc has no generator");
            Assert.That(gen.ShieldFraction, Is.EqualTo(1).Within(1e-9));

            b.ApplyDamage(target, null, gen.MaxShield * 0.25, bowPoint);
            var hit = b.Fx.Last(f => f.Kind == FxKind.ShieldHit);
            Assert.That(hit.Sub, Is.EqualTo(gen.Index), "the shield hit did not name the generator covering the bow");
            Assert.That(hit.Unit, Is.SameAs(target));
            Assert.That(gen.ShieldFraction, Is.EqualTo(0.75).Within(1e-9), "the bubble's charge is not the arc's own");

            // A hull's collision through the same arc names it too.
            int seq = b.FxSeq;
            b.ApplyImpact(target, bowPoint, gen.MaxShield * 0.1);
            Assert.That(b.Fx.Where(f => f.Seq > seq && f.Kind == FxKind.ShieldHit).Select(f => f.Sub), Is.EqualTo(new[] { gen.Index }));

            gen.Shield = 0;
            Assert.That(gen.ShieldFraction, Is.EqualTo(0), "a collapsed arc still has charge to show");
            seq = b.FxSeq;
            double hull = target.Combat.Hull;
            b.ApplyDamage(target, null, 50, bowPoint);
            Assert.That(b.Fx.Any(f => f.Seq > seq && f.Kind == FxKind.ShieldHit), Is.False, "a collapsed arc raised a shield bubble");
            Assert.That(b.Fx.Any(f => f.Seq > seq && f.Kind == FxKind.Impact), Is.True, "a hit on a collapsed arc did not go to the hull");
            Assert.That(target.Combat.Hull, Is.LessThan(hull));
        }

        [Test]
        public void LosingTheReactorDestroysTheShipAndLosingAGeneratorDropsItsOwnShields()
        {
            var (b, _, _) = Skirmish(3, new[] { (ShipClass.Cruiser, 0), (ShipClass.Cruiser, 1) });
            var target = b.Units[1];
            var gen = target.Combat.Subsystems.First(s => s.Kind == SubsystemKind.ShieldGenerator);
            gen.Shield = 0;
            b.ApplyDamage(target, gen, 5000, target.Pos);
            Assert.That(gen.Destroyed, Is.True);
            Assert.That(gen.Shield, Is.EqualTo(0));

            var reactor = target.Combat.Subsystems.First(s => s.Kind == SubsystemKind.Reactor);
            target.Combat.Hull = target.Combat.Spec.Hull;
            b.ApplyDamage(target, reactor, 5000, target.Pos);
            Assert.That(target.Dead, Is.True);
            Assert.That((b.Fx).Any(f => f.Kind == FxKind.UnitDestroyed), Is.True);
        }

        [Test]
        public void HardpointsSitOnTheHull()
        {
            var u = new BattleUnit { Pos = new DVec3(1, 2, 3), Forward = new DVec3(0, 1, 0), LengthKm = 2 };
            var nose = new SubsystemDef(SubsystemKind.Engine, 0, 0, 0.5);
            var top = new SubsystemDef(SubsystemKind.Engine, 0, 0.5, 0);
            Assert.That((u.PointOf(nose) - new DVec3(1, 3, 3)).Length < 1e-9, Is.True);
            // "Up" on a level hull is ecliptic north.
            Assert.That((u.PointOf(top) - new DVec3(1, 2, 4)).Length < 1e-9, Is.True);
        }

        [Test]
        public void CoarseStepsAtHighWarpStillFinishTheBattle()
        {
            var (b, _, _) = Skirmish(9, new[] { (ShipClass.Battleship, 0), (ShipClass.Destroyer, 1), (ShipClass.Destroyer, 1) });
            b.Advance(3 * 86400);
            Assert.That(b.Over, Is.True);
        }

        [Parallelizable(ParallelScope.Children)]
        [TestCase(1977), TestCase(1978), TestCase(1979), TestCase(1980), TestCase(1981), TestCase(1982)]
        public void TwoYearsOfWarHaveBattlesLossesAndAStillWorkingEconomy(int seed)
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd, seed);
            int maxBattles = 0;
            for (double jd = StartJd; jd <= StartJd + 730; jd += 0.25)
            {
                eph.Update(jd);
                w.Update(jd);
                maxBattles = Math.Max(maxBattles, w.War.Battles.Count);
                foreach (var s in w.Fleet.Ships)
                {
                    if (s.Destroyed) continue;
                    var st = w.Fleet.StateOf(s, jd);
                    Assert.That(double.IsNaN(st.HelioKm.X), Is.False, s.Name);
                    if (s.Battle != null) Assert.That(s.Battle.Over, Is.False, $"{s.Name} still held by a finished battle");
                }
            }
            foreach (var e in w.Events.Where(e => e.Kind != WorldEventKind.Launch).TakeLast(40))
                TestContext.Out.WriteLine($"{Astro.ToDateTime(e.Jd):yyyy-MM-dd}  {e.Text}");
            foreach (var f in w.Fleet.Factions)
                TestContext.Out.WriteLine($"{f.Name,-22} losses {w.War.Losses(f),3}  ships {w.Fleet.Ships.Count(s => s.Faction == f && !s.Destroyed),3}  treasury {f.Treasury / 1e9:0.00} B");
            TestContext.Out.WriteLine($"launches {w.Events.Count(e => e.Kind == WorldEventKind.Launch)}, battles {w.Events.Count(e => e.Kind == WorldEventKind.Battle) / 2}, peak concurrent {maxBattles}");
            foreach (var s in w.Economy.Settlements) TestContext.Out.WriteLine($"{s.Name,-20} {s.Owner.Prefix,-4} pop {s.Population / 1e3,7:N0}k platforms {s.Platforms.Count}/{s.MaxPlatforms}");

            Assert.That(w.Events.Count(e => e.Kind == WorldEventKind.Battle) >= 4, Is.True, "hardly any fighting in two years");
            Assert.That(w.Fleet.Ships.Count(s => s.Destroyed) > 0, Is.True, "nobody ever died");
            Assert.That(w.Fleet.Ships.Count(s => !s.Destroyed && w.IsTrader(s)) > 20, Is.True, "the merchant fleet was wiped out");
            // Neither power is finished, whatever the dice: each still has warships or a yard of its own to build them
            // again, because a navy beaten down to a handful of hulls (or to its last, besieged yard) sues for peace.
            foreach (var power in new[] { w.Sol, w.Reach })
                Assert.That(w.Fleet.Ships.Any(s => !s.Destroyed && s.Spec.IsMilitary && s.Faction == power) ||
                            w.Economy.Settlements.Any(s => s.Owner == power && s.HasShipyard), Is.True, $"the {power.Name} is out of the war");
            // Nobody starved: a pirate haven a navy has stormed and levelled (empty until it is rebuilt) is not a famine.
            foreach (var s in w.Economy.Settlements.Where(s => !(s.Kind == SettlementKind.Haven && s.Fallen)))
                Assert.That(s.Population >= s.InitialPopulation * 0.5, Is.True, $"{s.Name} collapsed to {s.Population:N0}");
        }

        [Test]
        public void WrecksAppearWhereShipsDieAndClearAwayAfterTheirLifetime()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            int maxWrecks = 0;
            double lastJd = StartJd, firstWreckJd = double.NaN;
            // Most fights end in surrender or flight, so the first hull to die in one can take most of a year: run until
            // one has, then on past that wreck's lifetime (the world starts with older wrecks from its seeded history, which do not count).
            for (double jd = StartJd; jd <= StartJd + 400 + w.War.Wrecks.DefaultLifetimeDays + 2; jd += 0.25)
            {
                eph.Update(jd);
                w.Update(jd);
                maxWrecks = Math.Max(maxWrecks, w.War.Wrecks.Items.Count);
                if (double.IsNaN(firstWreckJd) && w.War.Wrecks.Items.Any(x => x.CreatedJd >= StartJd)) firstWreckJd = w.War.Wrecks.Items.First(x => x.CreatedJd >= StartJd).CreatedJd;
                lastJd = jd;
                if (jd > firstWreckJd + w.War.Wrecks.DefaultLifetimeDays + 1) break;
            }
            Assert.That(w.War.Wrecks.Items.Any(x => x.CreatedJd == firstWreckJd), Is.False, "the first wreck should have been cleared away once its lifetime ran out");

            Assert.That(w.Fleet.Ships.Count(s => s.Destroyed) > 0, Is.True, "nobody ever died, so this proves nothing");
            Assert.That(maxWrecks > 0, Is.True, "a ship died but no wreck was ever left behind");
            foreach (var wreck in w.War.Wrecks.Items)
                Assert.That(lastJd - wreck.CreatedJd <= wreck.LifetimeDays, Is.True, $"{wreck.Name}'s wreck outlived its own lifetime and should have been cleared away");
        }

        [Test]
        public void TheDestroyersRailgunIsAFixedSpinalGunThatHitsHarderThanAnyTurret()
        {
            var layout = CombatSpec.Of(ShipClass.Destroyer).Layout;
            var rail = Expect.Single(layout, s => s.Kind == SubsystemKind.Railgun);
            Assert.That(rail.ArcWidthDeg < 360, Is.True, "a railgun should be a fixed spinal mount, not omnidirectional");

            var w = rail.Weapon;
            Assert.That(w, Is.Not.Null);
            Assert.That(w.RangeKm > WeaponSpec.Heavy.RangeKm, Is.True, "a railgun should outrange even a heavy laser");
            Assert.That(w.Damage > WeaponSpec.Heavy.Damage && w.Damage < WeaponSpec.Torpedo.Damage, Is.True, "a railgun should hit harder than a heavy laser but softer than a torpedo");
            Assert.That(w.Missile, Is.False, "a railgun is a direct-fire kinetic weapon, not a missile");
        }

        [Test]
        public void ARailgunHitIsDrawnDifferentlyFromALaserBeam()
        {
            var (b, blue, red) = Skirmish(1, new[] { (ShipClass.Destroyer, 0), (ShipClass.Destroyer, 1) });
            b.Units[0].Combat.Subsystems.First(s => s.Kind == SubsystemKind.Railgun).Cooldown = 0;
            bool sawRailgunFx = false;
            for (int i = 0; i < 4000 && !sawRailgunFx && !b.Over; i++)
            {
                b.Advance(1);
                sawRailgunFx = b.Fx.Any(f => f.Kind == FxKind.Railgun || f.Kind == FxKind.RailgunMiss);
            }
            Assert.That(sawRailgunFx, Is.True, "the Destroyer's railgun never fired a visible shot");
        }

        [Test]
        public void APlayerControlledHullsForwardGunsOnlyFireOnCommandWhileSideBatteriesStayAutonomous()
        {
            var (b, blue, red) = Skirmish(5, new[] { (ShipClass.Battleship, 0), (ShipClass.Battleship, 1) });
            var player = b.Units[0];
            var enemy = b.Units[1];
            player.PlayerControlled = true;
            player.PlayerFireHeld = false;
            player.Pos = DVec3.Zero;
            player.Forward = DVec3.UnitX;
            // Dead abeam to starboard (yaw 90): outside the forward guns' +-60 degree arc but square
            // in the starboard turret's and missile battery's 150-degree arc.
            enemy.Pos = new DVec3(0, -15, 0);

            var forwardGun = player.Combat.Subsystems.First(s => s.Kind == SubsystemKind.HeavyLaser);
            var sideGun = player.Combat.Subsystems.First(s => s.Kind == SubsystemKind.LaserTurret);
            bool forwardFiredWithoutTheTrigger = false, sideFiredAutonomously = false;
            double forwardCooldown = forwardGun.Cooldown, sideCooldown = sideGun.Cooldown;
            for (int i = 0; i < 400 && !b.Over; i++)
            {
                b.Advance(1);
                if (forwardGun.Cooldown > forwardCooldown + 1e-9) forwardFiredWithoutTheTrigger = true;
                if (sideGun.Cooldown > sideCooldown + 1e-9) sideFiredAutonomously = true;
                forwardCooldown = forwardGun.Cooldown;
                sideCooldown = sideGun.Cooldown;
            }
            Assert.That(forwardFiredWithoutTheTrigger, Is.False, "a player-controlled forward gun fired without the trigger held");
            Assert.That(sideFiredAutonomously, Is.True, "side/aft batteries should keep fighting on their own while piloting");
        }

        [Test]
        public void HoldingFireAimedAtAHostileFiresThePlayerControlledForwardGun()
        {
            var (b, blue, red) = Skirmish(6, new[] { (ShipClass.Battleship, 0), (ShipClass.Battleship, 1) });
            var player = b.Units[0];
            var enemy = b.Units[1];
            player.PlayerControlled = true;
            player.PlayerFireHeld = true;
            player.Pos = DVec3.Zero;
            enemy.Pos = new DVec3(10, 0, 0);
            player.Forward = (enemy.Pos - player.Pos).Normalized;

            var forwardGun = player.Combat.Subsystems.First(s => s.Kind == SubsystemKind.HeavyLaser);
            bool fired = false;
            double cooldown = forwardGun.Cooldown;
            for (int i = 0; i < 30 && !fired && !b.Over; i++)
            {
                // The AI isn't steering this hull anymore (Battle.Move skips a player-controlled
                // unit entirely) -- hold the nose on target exactly as a player's own mouse would.
                player.Forward = (enemy.Pos - player.Pos).Normalized;
                b.Advance(1);
                if (forwardGun.Cooldown > cooldown + 1e-9) fired = true;
                cooldown = forwardGun.Cooldown;
            }
            Assert.That(fired, Is.True, "the player's forward gun never fired despite holding the trigger aimed at a hostile");
        }

        [Test]
        public void APlayerControlledUnitKeepsTheHullExactlyWhereThePilotLoopPutIt()
        {
            var (b, blue, red) = Skirmish(7, new[] { (ShipClass.Battleship, 0), (ShipClass.Battleship, 1) });
            var player = b.Units[0];
            player.PlayerControlled = true;
            var pinned = new DVec3(42, -7, 3);
            player.Pos = pinned;
            player.Vel = DVec3.Zero;
            for (int i = 0; i < 50 && !b.Over; i++) b.Advance(1);
            Assert.That(player.Pos.X, Is.EqualTo(pinned.X).Within(1e-6));
            Assert.That(player.Pos.Y, Is.EqualTo(pinned.Y).Within(1e-6));
            Assert.That(player.Pos.Z, Is.EqualTo(pinned.Z).Within(1e-6));
        }
    }
}
