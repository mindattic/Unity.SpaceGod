using System;
using System.Linq;
using SolarSystem.Core;
using Xunit;
using Xunit.Abstractions;

namespace SolarSystem.Core.Tests
{
    public class CombatTests
    {
        const double StartJd = 2461324.0;
        readonly ITestOutputHelper output;

        public CombatTests(ITestOutputHelper output) => this.output = output;

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

        [Fact]
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
                Assert.True(b.Over, "battle never ended");
                if (b.Winner == 0) blueWins++;
                var subsHit = b.Units.SelectMany(u => u.Combat.Subsystems).Count(s => s.Hp < s.Def.MaxHp);
                Assert.True(subsHit > 0, "no hardpoint was ever damaged");
                // A single fast-resolving seed can end (e.g. via an early boarding) before slower, longer-
                // cooldown beam weapons ever get a turn; only require beam fire somewhere across the sample.
                anyBeam |= b.Fx.Any(f => f.Kind == FxKind.Beam);
                anyMissileOrImpact |= b.Fx.Any(f => f.Kind == FxKind.MissileLaunch || f.Kind == FxKind.Impact);
                output.WriteLine($"seed {seed}: winner {b.Winner} after {b.Seconds / 60:0} min, dead {b.Units.Count(u => u.Dead)}, fled {b.Units.Count(u => u.Fled)}");
            }
            Assert.True(blueWins >= 5, $"the battleship group won only {blueWins} of 6");
            Assert.True(anyBeam, "no beam weapon ever fired across any seed");
            Assert.True(anyMissileOrImpact, "no missile or impact ever happened across any seed");
        }

        [Fact]
        public void ABattleshipWithItsForwardBatteryKnockedOutTurnsBroadsideInstead()
        {
            var blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
            var red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 7);
            var battleship = Hull(ShipClass.Battleship, blue, 0);
            // Knock out the forward/aft spinal HeavyLasers so a broadside battery becomes the strongest arc.
            foreach (var s in battleship.Combat.Subsystems)
                if (s.Kind == SubsystemKind.HeavyLaser) s.Hp = 0;
            var target = Hull(ShipClass.Destroyer, red, 1);
            var bu = b.AddShip(battleship, 0, DVec3.Zero, DVec3.UnitX);
            var tu = b.AddShip(target, 1, new DVec3(20, 0, 0), -DVec3.UnitX);

            for (int i = 0; i < 200; i++) b.Advance(1);

            double angleToTarget = Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(bu.Forward, (tu.Pos - bu.Pos).Normalized)))) * Astro.Rad2Deg;
            output.WriteLine($"battleship forward={bu.Forward}, angle to target={angleToTarget:0.#} deg");
            Assert.True(angleToTarget > 30, "the battleship still pointed its bow at the target instead of presenting a broadside");
        }

        [Fact]
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
            output.WriteLine($"battleship forward={bu.Forward}, angle to target={angleToTarget:0.#} deg");
            Assert.True(angleToTarget < 20, "a fully healthy battleship should still point its strongest (spinal) battery at the target");
        }

        [Fact]
        public void LosingTheReactorDestroysTheShipAndLosingAGeneratorDropsItsOwnShields()
        {
            var (b, _, _) = Skirmish(3, new[] { (ShipClass.Cruiser, 0), (ShipClass.Cruiser, 1) });
            var target = b.Units[1];
            var gen = target.Combat.Subsystems.First(s => s.Kind == SubsystemKind.ShieldGenerator);
            gen.Shield = 0;
            b.ApplyDamage(target, gen, 5000, target.Pos);
            Assert.True(gen.Destroyed);
            Assert.Equal(0, gen.Shield);

            var reactor = target.Combat.Subsystems.First(s => s.Kind == SubsystemKind.Reactor);
            target.Combat.Hull = target.Combat.Spec.Hull;
            b.ApplyDamage(target, reactor, 5000, target.Pos);
            Assert.True(target.Dead);
            Assert.Contains(b.Fx, f => f.Kind == FxKind.UnitDestroyed);
        }

        [Fact]
        public void HardpointsSitOnTheHull()
        {
            var u = new BattleUnit { Pos = new DVec3(1, 2, 3), Forward = new DVec3(0, 1, 0), LengthKm = 2 };
            var nose = new SubsystemDef(SubsystemKind.Engine, 0, 0, 0.5);
            var top = new SubsystemDef(SubsystemKind.Engine, 0, 0.5, 0);
            Assert.True((u.PointOf(nose) - new DVec3(1, 3, 3)).Length < 1e-9);
            // "Up" on a level hull is ecliptic north.
            Assert.True((u.PointOf(top) - new DVec3(1, 2, 4)).Length < 1e-9);
        }

        [Fact]
        public void CoarseStepsAtHighWarpStillFinishTheBattle()
        {
            var (b, _, _) = Skirmish(9, new[] { (ShipClass.Battleship, 0), (ShipClass.Destroyer, 1), (ShipClass.Destroyer, 1) });
            b.Advance(3 * 86400);
            Assert.True(b.Over);
        }

        [Fact]
        public void TwoYearsOfWarHaveBattlesLossesAndAStillWorkingEconomy()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
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
                    Assert.False(double.IsNaN(st.HelioKm.X), s.Name);
                    if (s.Battle != null) Assert.False(s.Battle.Over, $"{s.Name} still held by a finished battle");
                }
            }
            foreach (var e in w.Events.Where(e => e.Kind != WorldEventKind.Launch).TakeLast(40))
                output.WriteLine($"{Astro.ToDateTime(e.Jd):yyyy-MM-dd}  {e.Text}");
            foreach (var f in w.Fleet.Factions)
                output.WriteLine($"{f.Name,-22} losses {w.War.Losses(f),3}  ships {w.Fleet.Ships.Count(s => s.Faction == f && !s.Destroyed),3}  treasury {f.Treasury / 1e9:0.00} B");
            output.WriteLine($"launches {w.Events.Count(e => e.Kind == WorldEventKind.Launch)}, battles {w.Events.Count(e => e.Kind == WorldEventKind.Battle) / 2}, peak concurrent {maxBattles}");
            foreach (var s in w.Economy.Settlements) output.WriteLine($"{s.Name,-20} {s.Owner.Prefix,-4} pop {s.Population / 1e3,7:N0}k platforms {s.Platforms.Count}/{s.MaxPlatforms}");

            Assert.True(w.Events.Count(e => e.Kind == WorldEventKind.Battle) >= 4, "hardly any fighting in two years");
            Assert.True(w.Fleet.Ships.Count(s => s.Destroyed) > 0, "nobody ever died");
            Assert.True(w.Fleet.Ships.Count(s => !s.Destroyed && w.IsTrader(s)) > 20, "the merchant fleet was wiped out");
            Assert.True(w.Fleet.Ships.Count(s => !s.Destroyed && s.Spec.IsMilitary && s.Faction == w.Sol) > 0);
            Assert.True(w.Fleet.Ships.Count(s => !s.Destroyed && s.Spec.IsMilitary && s.Faction == w.Reach) > 0);
            foreach (var s in w.Economy.Settlements)
                Assert.True(s.Population >= s.InitialPopulation * 0.5, $"{s.Name} collapsed to {s.Population:N0}");
        }

        [Fact]
        public void WrecksAppearWhereShipsDieAndClearAwayAfterTheirLifetime()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            int maxWrecks = 0;
            double lastJd = StartJd;
            for (double jd = StartJd; jd <= StartJd + 200; jd += 0.25)
            {
                eph.Update(jd);
                w.Update(jd);
                maxWrecks = Math.Max(maxWrecks, w.War.Wrecks.Items.Count);
                lastJd = jd;
            }

            Assert.True(w.Fleet.Ships.Count(s => s.Destroyed) > 0, "nobody ever died, so this proves nothing");
            Assert.True(maxWrecks > 0, "a ship died but no wreck was ever left behind");
            foreach (var wreck in w.War.Wrecks.Items)
                Assert.True(lastJd - wreck.CreatedJd <= wreck.LifetimeDays, $"{wreck.Name}'s wreck outlived its own lifetime and should have been cleared away");
        }

        [Fact]
        public void TheDestroyersRailgunIsAFixedSpinalGunThatHitsHarderThanAnyTurret()
        {
            var layout = CombatSpec.Of(ShipClass.Destroyer).Layout;
            var rail = Assert.Single(layout, s => s.Kind == SubsystemKind.Railgun);
            Assert.True(rail.ArcWidthDeg < 360, "a railgun should be a fixed spinal mount, not omnidirectional");

            var w = rail.Weapon;
            Assert.NotNull(w);
            Assert.True(w.RangeKm > WeaponSpec.Heavy.RangeKm, "a railgun should outrange even a heavy laser");
            Assert.True(w.Damage > WeaponSpec.Heavy.Damage && w.Damage < WeaponSpec.Torpedo.Damage,
                "a railgun should hit harder than a heavy laser but softer than a torpedo");
            Assert.False(w.Missile, "a railgun is a direct-fire kinetic weapon, not a missile");
        }

        [Fact]
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
            Assert.True(sawRailgunFx, "the Destroyer's railgun never fired a visible shot");
        }

        [Fact]
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
            Assert.False(forwardFiredWithoutTheTrigger, "a player-controlled forward gun fired without the trigger held");
            Assert.True(sideFiredAutonomously, "side/aft batteries should keep fighting on their own while piloting");
        }

        [Fact]
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
            Assert.True(fired, "the player's forward gun never fired despite holding the trigger aimed at a hostile");
        }

        [Fact]
        public void APlayerControlledUnitKeepsTheHullExactlyWhereThePilotLoopPutIt()
        {
            var (b, blue, red) = Skirmish(7, new[] { (ShipClass.Battleship, 0), (ShipClass.Battleship, 1) });
            var player = b.Units[0];
            player.PlayerControlled = true;
            var pinned = new DVec3(42, -7, 3);
            player.Pos = pinned;
            player.Vel = DVec3.Zero;
            for (int i = 0; i < 50 && !b.Over; i++) b.Advance(1);
            Assert.Equal(pinned.X, player.Pos.X, 6);
            Assert.Equal(pinned.Y, player.Pos.Y, 6);
            Assert.Equal(pinned.Z, player.Pos.Z, 6);
        }
    }
}
