using System;
using System.Linq;
using SolarSystem.Core;
using Xunit;

namespace SolarSystem.Core.Tests
{
    /// <summary>
    /// Covers USS-T34: <see cref="ShipCombat"/>'s shield bookkeeping, status thresholds, repair and
    /// resupply, plus <see cref="SubsystemDef.ContainsYaw"/> and the <see cref="CombatSpec"/> lookup
    /// fallbacks.
    /// </summary>
    public class CombatStateTests
    {
        static ShipCombat Make(ShipClass c) => new ShipCombat(CombatSpec.Of(c), c);

        // ---------------------------------------------------------------- shields

        [Fact]
        public void ConstructingAShipSplitsItsShieldEvenlyAcrossItsGenerators()
        {
            var combat = Make(ShipClass.Destroyer);
            var spec = combat.Spec;
            var generators = combat.Subsystems.Where(s => s.Kind == SubsystemKind.ShieldGenerator).ToList();
            double share = spec.Shield / generators.Count;

            foreach (var g in generators)
            {
                Assert.Equal(share, g.MaxShield, 9);
                Assert.Equal(share, g.Shield, 9);
            }
            Assert.Equal(spec.Shield, combat.Shield, 6);
        }

        // ---------------------------------------------------------------- ContainsYaw

        [Fact]
        public void ContainsYawRespectsItsWidthAroundAZeroCenter()
        {
            var def = new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0, 0, arcCenterDeg: 0, arcWidthDeg: 90);

            Assert.True(def.ContainsYaw(0));
            Assert.True(def.ContainsYaw(45));
            Assert.False(def.ContainsYaw(46));
            Assert.True(def.ContainsYaw(315)); // -45, the lower edge, wrapped
            Assert.False(def.ContainsYaw(314));
            Assert.False(def.ContainsYaw(180));
        }

        [Fact]
        public void ContainsYawWrapsAcrossTheZeroThreeSixtyBoundary()
        {
            var def = new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0, 0, arcCenterDeg: 350, arcWidthDeg: 60);

            Assert.True(def.ContainsYaw(350));
            Assert.True(def.ContainsYaw(0));
            Assert.True(def.ContainsYaw(15));
            Assert.True(def.ContainsYaw(320)); // lower edge
            Assert.True(def.ContainsYaw(20));  // upper edge, wrapped past 360
            Assert.False(def.ContainsYaw(319));
            Assert.False(def.ContainsYaw(21));
            Assert.False(def.ContainsYaw(200));
        }

        // ---------------------------------------------------------------- GeneratorFor

        [Fact]
        public void GeneratorForReturnsTheLiveGeneratorCoveringAYawAndSkipsDestroyedOnes()
        {
            var combat = Make(ShipClass.Destroyer);
            var bow = combat.Subsystems.Single(s => s.Kind == SubsystemKind.ShieldGenerator && s.Def.ArcCenterDeg == 0);

            Assert.Same(bow, combat.GeneratorFor(0));
            Assert.Same(bow, combat.GeneratorFor(40));

            bow.Hp = 0;
            Assert.Null(combat.GeneratorFor(0));
        }

        // ---------------------------------------------------------------- RegenShields

        [Fact]
        public void RegenShieldsDoesNotRegenerateAGeneratorHitWithinTheLast8Seconds()
        {
            var combat = Make(ShipClass.Destroyer);
            var gen = combat.Subsystems.First(s => s.Kind == SubsystemKind.ShieldGenerator);
            combat.ZeroAllShields();

            gen.LastHitSeconds = 92; // nowSeconds - LastHitSeconds == 8, not > 8
            combat.RegenShields(100, 1);
            Assert.Equal(0, gen.Shield);

            gen.LastHitSeconds = 91.9; // just over 8 seconds since the hit
            combat.RegenShields(100, 1);
            Assert.True(gen.Shield > 0, "a generator unhit for over 8 seconds should have regenerated");
        }

        [Fact]
        public void RegenShieldsSharesTheHullsRegenRateEvenlyAcrossItsGenerators()
        {
            var combat = Make(ShipClass.Destroyer);
            var generators = combat.Subsystems.Where(s => s.Kind == SubsystemKind.ShieldGenerator).ToList();
            combat.ZeroAllShields();

            combat.RegenShields(100, 1);

            double share = combat.Spec.ShieldRegen / generators.Count;
            foreach (var g in generators) Assert.Equal(share, g.Shield, 9);
        }

        [Fact]
        public void RegenShieldsCapsAtMaxShieldAndNeverRegeneratesADestroyedGenerator()
        {
            var combat = Make(ShipClass.Destroyer);
            var generators = combat.Subsystems.Where(s => s.Kind == SubsystemKind.ShieldGenerator).ToList();
            combat.ZeroAllShields();
            generators[0].Hp = 0; // destroyed: should stay at 0 regardless of elapsed time

            combat.RegenShields(10000, 10000); // a huge dt that would massively overshoot every cap

            Assert.Equal(0, generators[0].Shield);
            foreach (var g in generators.Skip(1)) Assert.Equal(g.MaxShield, g.Shield, 9);
        }

        // ---------------------------------------------------------------- Disabled / EngineFactor / BridgeIntact

        [Fact]
        public void EngineFactorIsTheShareOfEnginesStillWorking()
        {
            var combat = Make(ShipClass.Cruiser);
            var engines = combat.Subsystems.Where(s => s.Kind == SubsystemKind.Engine).ToList();
            Assert.Equal(1, combat.EngineFactor);

            engines[0].Hp = 0;
            Assert.Equal((engines.Count - 1.0) / engines.Count, combat.EngineFactor, 9);

            foreach (var e in engines) e.Hp = 0;
            Assert.Equal(0, combat.EngineFactor);
        }

        [Fact]
        public void EngineFactorIsOneWhenTheHullHasNoEngines()
        {
            var combat = new ShipCombat(CombatSpec.Platform);
            Assert.DoesNotContain(combat.Subsystems, s => s.Kind == SubsystemKind.Engine);
            Assert.Equal(1, combat.EngineFactor);
        }

        [Fact]
        public void BridgeIntactIsTrueWhenTheHullHasNoBridgeSubsystem()
        {
            var combat = new ShipCombat(CombatSpec.Platform);
            Assert.DoesNotContain(combat.Subsystems, s => s.Kind == SubsystemKind.Bridge);
            Assert.True(combat.BridgeIntact);
        }

        [Fact]
        public void DisabledWhenNoEnginesAreStillWorking()
        {
            var combat = Make(ShipClass.Cruiser);
            foreach (var e in combat.Subsystems.Where(s => s.Kind == SubsystemKind.Engine)) e.Hp = 0;
            Assert.True(combat.Disabled);
        }

        [Fact]
        public void DisabledWhenHullDropsBelowTwelvePercent()
        {
            var combat = Make(ShipClass.Destroyer);

            combat.Hull = combat.Spec.Hull * 0.12;
            Assert.False(combat.Disabled, "exactly 12% hull should not yet disable the ship");

            combat.Hull = combat.Spec.Hull * 0.11;
            Assert.True(combat.Disabled);
        }

        [Fact]
        public void DisabledWhenTheBridgeIsLostAndHullIsBelowFortyPercentButNotAboveIt()
        {
            var combat = Make(ShipClass.Destroyer);
            combat.Subsystems.First(s => s.Kind == SubsystemKind.Bridge).Hp = 0;

            combat.Hull = combat.Spec.Hull * 0.5;
            Assert.False(combat.Disabled, "a lost bridge alone shouldn't disable a mostly-healthy hull");

            combat.Hull = combat.Spec.Hull * 0.3;
            Assert.True(combat.Disabled);
        }

        [Fact]
        public void DisabledIsFalseForAShipThatHasAlreadyBeenDestroyed()
        {
            var combat = Make(ShipClass.Destroyer);
            combat.Hull = 0;
            Assert.False(combat.Alive);
            Assert.False(combat.Disabled);
        }

        // ---------------------------------------------------------------- Armed

        [Fact]
        public void ArmedIsFalseWhenTheOnlyWeaponsArePointDefense()
        {
            var combat = Make(ShipClass.Freighter);
            Assert.Contains(combat.Subsystems, s => s.Kind == SubsystemKind.PointDefense);
            Assert.DoesNotContain(combat.Subsystems, s => s.Def.IsWeapon && s.Kind != SubsystemKind.PointDefense);
            Assert.False(combat.Armed);
        }

        [Fact]
        public void ArmedIsFalseWhenTheOnlyLiveWeaponIsAnEmptyMissileLauncher()
        {
            var combat = Make(ShipClass.Destroyer);
            foreach (var s in combat.Subsystems.Where(s => s.Def.IsWeapon && s.Kind != SubsystemKind.MissileLauncher))
                s.Hp = 0;
            var launcher = combat.Subsystems.Single(s => s.Kind == SubsystemKind.MissileLauncher);

            launcher.Ammo = 0;
            Assert.False(combat.Armed);

            launcher.Ammo = 1;
            Assert.True(combat.Armed);
        }

        // ---------------------------------------------------------------- Damage / Strength

        [Fact]
        public void DamageIsZeroForAPristineShip()
        {
            var combat = Make(ShipClass.Cruiser);
            Assert.Equal(0, combat.Damage);
        }

        [Fact]
        public void DamageScalesWithBothHullLossAndSubsystemWear()
        {
            var combat = Make(ShipClass.Cruiser);
            combat.Hull = combat.Spec.Hull * 0.5;
            var sub = combat.Subsystems[0];
            sub.Hp = sub.Def.MaxHp * 0.5;

            double missing = 0.5 + (1 - sub.Health) * 0.1;
            double expected = missing / (1 + combat.Subsystems.Count * 0.1);
            Assert.Equal(expected, combat.Damage, 9);
        }

        [Fact]
        public void StrengthIsZeroForAShipThatHasBeenDestroyed()
        {
            var combat = Make(ShipClass.Cruiser);
            combat.Hull = 0;
            Assert.Equal(0, combat.Strength);
        }

        [Fact]
        public void StrengthScalesWithHullFraction()
        {
            var combat = Make(ShipClass.Cruiser);
            double full = combat.Strength;

            combat.Hull = combat.Spec.Hull * 0.5;
            double half = combat.Strength;

            Assert.Equal(full * 0.65, half, 6); // 0.3 + 0.7 * 0.5
        }

        [Fact]
        public void StrengthDropsWhenAWeaponIsDestroyed()
        {
            var combat = Make(ShipClass.Cruiser);
            double before = combat.Strength;
            combat.Subsystems.First(s => s.Def.IsWeapon).Hp = 0;
            double after = combat.Strength;

            Assert.True(after < before, "destroying a weapon should reduce fighting strength");
        }

        // ---------------------------------------------------------------- Repair

        [Fact]
        public void RepairRestoresHullAndHardpointsByTheGivenShare()
        {
            var combat = Make(ShipClass.Destroyer);
            var spec = combat.Spec;
            combat.Hull = spec.Hull * 0.2;
            var sub = combat.Subsystems[0];
            sub.Hp = sub.Def.MaxHp * 0.1;

            combat.Repair(0.3);

            Assert.Equal(Math.Min(spec.Hull, spec.Hull * 0.2 + spec.Hull * 0.3), combat.Hull, 6);
            Assert.Equal(Math.Min(sub.Def.MaxHp, sub.Def.MaxHp * 0.1 + sub.Def.MaxHp * 0.3 * 1.5), sub.Hp, 6);
        }

        [Fact]
        public void RepairCapsHullAndHardpointsAtTheirMaximum()
        {
            var combat = Make(ShipClass.Destroyer);
            combat.Repair(1.0); // already pristine; a full repair must not overshoot

            Assert.Equal(combat.Spec.Hull, combat.Hull);
            foreach (var s in combat.Subsystems) Assert.Equal(s.Def.MaxHp, s.Hp);
        }

        [Fact]
        public void RepairRefillsShieldsButDoesNotRestockAmmo()
        {
            var combat = Make(ShipClass.Destroyer);
            combat.ZeroAllShields();
            var launcher = combat.Subsystems.Single(s => s.Kind == SubsystemKind.MissileLauncher);
            launcher.Ammo = 0;

            combat.Repair(0.1);

            foreach (var g in combat.Subsystems.Where(s => s.Kind == SubsystemKind.ShieldGenerator))
                Assert.Equal(g.MaxShield, g.Shield);
            Assert.Equal(0, launcher.Ammo);
        }

        // ---------------------------------------------------------------- FreeResupply / RestoreFighters

        [Fact]
        public void FreeResupplyRestocksAmmoAndMustersCrewAndMarinesBackToSpec()
        {
            var combat = Make(ShipClass.Destroyer);
            foreach (var s in combat.Subsystems.Where(s => s.Def.Weapon != null)) s.Ammo = 0;
            combat.Interior.Compartments[0].Crew = 0;

            combat.FreeResupply();

            foreach (var s in combat.Subsystems.Where(s => s.Def.Weapon != null))
                Assert.Equal(s.Def.Weapon.Ammo, s.Ammo);
            Assert.Equal(combat.Spec.Crew, combat.Interior.Crew, 6);
            Assert.Equal(combat.Spec.Marines, combat.Interior.Marines, 6);
        }

        [Fact]
        public void RestoreFightersCapsAtSixPerSquadron()
        {
            var combat = Make(ShipClass.Carrier);
            int max = combat.Spec.Squadrons * 6;
            combat.Fighters = 0;

            combat.RestoreFighters(max - 2);
            Assert.Equal(max - 2, combat.Fighters);

            combat.RestoreFighters(1000);
            Assert.Equal(max, combat.Fighters);
        }

        // ---------------------------------------------------------------- CombatSpec fallbacks

        [Fact]
        public void OfFallsBackToDestroyerForAnUnknownShipClass()
        {
            var fallback = CombatSpec.Of((ShipClass)999);
            Assert.Same(CombatSpec.Of(ShipClass.Destroyer), fallback);
            Assert.Equal("Destroyer", fallback.DesignName);
        }

        [Fact]
        public void RandomOfFallsBackToADestroyerDesignForAnUnknownShipClass()
        {
            var spec = CombatSpec.RandomOf((ShipClass)999, new Random(1));
            Assert.Equal("Destroyer", spec.DesignName);
        }
    }
}
