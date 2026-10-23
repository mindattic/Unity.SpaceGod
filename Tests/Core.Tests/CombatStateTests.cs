using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Covers U.SPC-T34: <see cref="ShipCombat"/>'s shield bookkeeping, status thresholds, repair and
    /// resupply, plus <see cref="SubsystemDef.ContainsYaw"/> and the <see cref="CombatSpec"/> lookup
    /// fallbacks.
    /// </summary>
    [TestFixture]
    public class CombatStateTests
    {
        static ShipCombat Make(ShipClass c) => new ShipCombat(CombatSpec.Of(c), c);

        // ---------------------------------------------------------------- shields

        [Test]
        public void ConstructingAShipSplitsItsShieldEvenlyAcrossItsGenerators()
        {
            var combat = Make(ShipClass.Destroyer);
            var spec = combat.Spec;
            var generators = combat.Subsystems.Where(s => s.Kind == SubsystemKind.ShieldGenerator).ToList();
            double share = spec.Shield / generators.Count;

            foreach (var g in generators)
            {
                Assert.That(g.MaxShield, Is.EqualTo(share).Within(1e-9));
                Assert.That(g.Shield, Is.EqualTo(share).Within(1e-9));
            }
            Assert.That(combat.Shield, Is.EqualTo(spec.Shield).Within(1e-6));
        }

        // ---------------------------------------------------------------- ContainsYaw

        [Test]
        public void ContainsYawRespectsItsWidthAroundAZeroCenter()
        {
            var def = new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0, 0, arcCenterDeg: 0, arcWidthDeg: 90);

            Assert.That(def.ContainsYaw(0), Is.True);
            Assert.That(def.ContainsYaw(45), Is.True);
            Assert.That(def.ContainsYaw(46), Is.False);
            Assert.That(def.ContainsYaw(315), Is.True); // -45, the lower edge, wrapped
            Assert.That(def.ContainsYaw(314), Is.False);
            Assert.That(def.ContainsYaw(180), Is.False);
        }

        [Test]
        public void ContainsYawWrapsAcrossTheZeroThreeSixtyBoundary()
        {
            var def = new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0, 0, arcCenterDeg: 350, arcWidthDeg: 60);

            Assert.That(def.ContainsYaw(350), Is.True);
            Assert.That(def.ContainsYaw(0), Is.True);
            Assert.That(def.ContainsYaw(15), Is.True);
            Assert.That(def.ContainsYaw(320), Is.True); // lower edge
            Assert.That(def.ContainsYaw(20), Is.True);  // upper edge, wrapped past 360
            Assert.That(def.ContainsYaw(319), Is.False);
            Assert.That(def.ContainsYaw(21), Is.False);
            Assert.That(def.ContainsYaw(200), Is.False);
        }

        // ---------------------------------------------------------------- GeneratorFor

        [Test]
        public void GeneratorForReturnsTheLiveGeneratorCoveringAYawAndSkipsDestroyedOnes()
        {
            var combat = Make(ShipClass.Destroyer);
            var bow = combat.Subsystems.Single(s => s.Kind == SubsystemKind.ShieldGenerator && s.Def.ArcCenterDeg == 0);

            Assert.That(combat.GeneratorFor(0), Is.SameAs(bow));
            Assert.That(combat.GeneratorFor(40), Is.SameAs(bow));

            bow.Hp = 0;
            Assert.That(combat.GeneratorFor(0), Is.Null);
        }

        // ---------------------------------------------------------------- RegenShields

        [Test]
        public void RegenShieldsDoesNotRegenerateAGeneratorHitWithinTheLast8Seconds()
        {
            var combat = Make(ShipClass.Destroyer);
            var gen = combat.Subsystems.First(s => s.Kind == SubsystemKind.ShieldGenerator);
            combat.ZeroAllShields();

            gen.LastHitSeconds = 92; // nowSeconds - LastHitSeconds == 8, not > 8
            combat.RegenShields(100, 1);
            Assert.That(gen.Shield, Is.EqualTo(0));

            gen.LastHitSeconds = 91.9; // just over 8 seconds since the hit
            combat.RegenShields(100, 1);
            Assert.That(gen.Shield > 0, Is.True, "a generator unhit for over 8 seconds should have regenerated");
        }

        [Test]
        public void RegenShieldsSharesTheHullsRegenRateEvenlyAcrossItsGenerators()
        {
            var combat = Make(ShipClass.Destroyer);
            var generators = combat.Subsystems.Where(s => s.Kind == SubsystemKind.ShieldGenerator).ToList();
            combat.ZeroAllShields();

            combat.RegenShields(100, 1);

            double share = combat.Spec.ShieldRegen / generators.Count;
            foreach (var g in generators) Assert.That(g.Shield, Is.EqualTo(share).Within(1e-9));
        }

        [Test]
        public void RegenShieldsCapsAtMaxShieldAndNeverRegeneratesADestroyedGenerator()
        {
            var combat = Make(ShipClass.Destroyer);
            var generators = combat.Subsystems.Where(s => s.Kind == SubsystemKind.ShieldGenerator).ToList();
            combat.ZeroAllShields();
            generators[0].Hp = 0; // destroyed: should stay at 0 regardless of elapsed time

            combat.RegenShields(10000, 10000); // a huge dt that would massively overshoot every cap

            Assert.That(generators[0].Shield, Is.EqualTo(0));
            foreach (var g in generators.Skip(1)) Assert.That(g.Shield, Is.EqualTo(g.MaxShield).Within(1e-9));
        }

        // ---------------------------------------------------------------- Disabled / EngineFactor / BridgeIntact

        [Test]
        public void EngineFactorIsTheShareOfEnginesStillWorking()
        {
            var combat = Make(ShipClass.Cruiser);
            var engines = combat.Subsystems.Where(s => s.Kind == SubsystemKind.Engine).ToList();
            Assert.That(combat.EngineFactor, Is.EqualTo(1));

            engines[0].Hp = 0;
            Assert.That(combat.EngineFactor, Is.EqualTo((engines.Count - 1.0) / engines.Count).Within(1e-9));

            foreach (var e in engines) e.Hp = 0;
            Assert.That(combat.EngineFactor, Is.EqualTo(0));
        }

        [Test]
        public void EngineFactorIsOneWhenTheHullHasNoEngines()
        {
            var combat = new ShipCombat(CombatSpec.Platform);
            Assert.That((combat.Subsystems).Any(s => s.Kind == SubsystemKind.Engine), Is.False);
            Assert.That(combat.EngineFactor, Is.EqualTo(1));
        }

        [Test]
        public void BridgeIntactIsTrueWhenTheHullHasNoBridgeSubsystem()
        {
            var combat = new ShipCombat(CombatSpec.Platform);
            Assert.That((combat.Subsystems).Any(s => s.Kind == SubsystemKind.Bridge), Is.False);
            Assert.That(combat.BridgeIntact, Is.True);
        }

        [Test]
        public void DisabledWhenNoEnginesAreStillWorking()
        {
            var combat = Make(ShipClass.Cruiser);
            foreach (var e in combat.Subsystems.Where(s => s.Kind == SubsystemKind.Engine)) e.Hp = 0;
            Assert.That(combat.Disabled, Is.True);
        }

        [Test]
        public void DisabledWhenHullDropsBelowTwelvePercent()
        {
            var combat = Make(ShipClass.Destroyer);

            combat.Hull = combat.Spec.Hull * 0.12;
            Assert.That(combat.Disabled, Is.False, "exactly 12% hull should not yet disable the ship");

            combat.Hull = combat.Spec.Hull * 0.11;
            Assert.That(combat.Disabled, Is.True);
        }

        [Test]
        public void DisabledWhenTheBridgeIsLostAndHullIsBelowFortyPercentButNotAboveIt()
        {
            var combat = Make(ShipClass.Destroyer);
            combat.Subsystems.First(s => s.Kind == SubsystemKind.Bridge).Hp = 0;

            combat.Hull = combat.Spec.Hull * 0.5;
            Assert.That(combat.Disabled, Is.False, "a lost bridge alone shouldn't disable a mostly-healthy hull");

            combat.Hull = combat.Spec.Hull * 0.3;
            Assert.That(combat.Disabled, Is.True);
        }

        [Test]
        public void DisabledIsFalseForAShipThatHasAlreadyBeenDestroyed()
        {
            var combat = Make(ShipClass.Destroyer);
            combat.Hull = 0;
            Assert.That(combat.Alive, Is.False);
            Assert.That(combat.Disabled, Is.False);
        }

        // ---------------------------------------------------------------- Armed

        [Test]
        public void ArmedIsFalseWhenTheOnlyWeaponsArePointDefense()
        {
            // A truss freighter carries no weapons (U.SPC-T482); one given point defence and a mine layer (U.SPC-T271) is still
            // not armed: the one only shoots down what comes at it, the other supports, and neither fights.
            var spec = CombatSpec.Of(ShipClass.Freighter).Clone();
            spec.Layout = spec.Layout.Concat(new[]
            {
                new SubsystemDef(SubsystemKind.PointDefense, 0, 0.07, 0),
                new SubsystemDef(SubsystemKind.MineLayer, 0, -0.04, -0.45, facesAft: true, arcCenterDeg: 180),
            }).ToArray();
            var combat = new ShipCombat(spec, ShipClass.Freighter);
            Assert.That((combat.Subsystems).Any(s => s.Kind == SubsystemKind.PointDefense), Is.True);
            Assert.That((combat.Subsystems).Any(s => s.Def.IsWeapon && s.Kind != SubsystemKind.PointDefense && !s.Def.Weapon.Supporting), Is.False);
            Assert.That(combat.Armed, Is.False);
        }

        [Test]
        public void ArmedIsFalseWhenTheOnlyLiveWeaponIsAnEmptyMissileLauncher()
        {
            var combat = Make(ShipClass.Destroyer);
            foreach (var s in combat.Subsystems.Where(s => s.Def.IsWeapon && s.Kind != SubsystemKind.MissileLauncher))
                s.Hp = 0;
            var launcher = combat.Subsystems.Single(s => s.Kind == SubsystemKind.MissileLauncher);

            launcher.Ammo = 0;
            Assert.That(combat.Armed, Is.False);

            launcher.Ammo = 1;
            Assert.That(combat.Armed, Is.True);
        }

        // ---------------------------------------------------------------- Damage / Strength

        [Test]
        public void DamageIsZeroForAPristineShip()
        {
            var combat = Make(ShipClass.Cruiser);
            Assert.That(combat.Damage, Is.EqualTo(0));
        }

        [Test]
        public void DamageScalesWithBothHullLossAndSubsystemWear()
        {
            var combat = Make(ShipClass.Cruiser);
            combat.Hull = combat.Spec.Hull * 0.5;
            var sub = combat.Subsystems[0];
            sub.Hp = sub.Def.MaxHp * 0.5;

            double missing = 0.5 + (1 - sub.Health) * 0.1;
            double expected = missing / (1 + combat.Subsystems.Count * 0.1);
            Assert.That(combat.Damage, Is.EqualTo(expected).Within(1e-9));
        }

        [Test]
        public void StrengthIsZeroForAShipThatHasBeenDestroyed()
        {
            var combat = Make(ShipClass.Cruiser);
            combat.Hull = 0;
            Assert.That(combat.Strength, Is.EqualTo(0));
        }

        [Test]
        public void StrengthScalesWithHullFraction()
        {
            var combat = Make(ShipClass.Cruiser);
            double full = combat.Strength;

            combat.Hull = combat.Spec.Hull * 0.5;
            double half = combat.Strength;

            Assert.That(half, Is.EqualTo(full * 0.65).Within(1e-6)); // 0.3 + 0.7 * 0.5
        }

        [Test]
        public void StrengthDropsWhenAWeaponIsDestroyed()
        {
            var combat = Make(ShipClass.Cruiser);
            double before = combat.Strength;
            combat.Subsystems.First(s => s.Def.IsWeapon).Hp = 0;
            double after = combat.Strength;

            Assert.That(after < before, Is.True, "destroying a weapon should reduce fighting strength");
        }

        // ---------------------------------------------------------------- Repair

        [Test]
        public void RepairRestoresHullAndHardpointsByTheGivenShare()
        {
            var combat = Make(ShipClass.Destroyer);
            var spec = combat.Spec;
            combat.Hull = spec.Hull * 0.2;
            var sub = combat.Subsystems[0];
            sub.Hp = sub.Def.MaxHp * 0.1;

            combat.Repair(0.3);

            Assert.That(combat.Hull, Is.EqualTo(Math.Min(spec.Hull, spec.Hull * 0.2 + spec.Hull * 0.3)).Within(1e-6));
            Assert.That(sub.Hp, Is.EqualTo(Math.Min(sub.Def.MaxHp, sub.Def.MaxHp * 0.1 + sub.Def.MaxHp * 0.3 * 1.5)).Within(1e-6));
        }

        [Test]
        public void RepairCapsHullAndHardpointsAtTheirMaximum()
        {
            var combat = Make(ShipClass.Destroyer);
            combat.Repair(1.0); // already pristine; a full repair must not overshoot

            Assert.That(combat.Hull, Is.EqualTo(combat.Spec.Hull));
            foreach (var s in combat.Subsystems) Assert.That(s.Hp, Is.EqualTo(s.Def.MaxHp));
        }

        [Test]
        public void RepairRefillsShieldsButDoesNotRestockAmmo()
        {
            var combat = Make(ShipClass.Destroyer);
            combat.ZeroAllShields();
            var launcher = combat.Subsystems.Single(s => s.Kind == SubsystemKind.MissileLauncher);
            launcher.Ammo = 0;

            combat.Repair(0.1);

            foreach (var g in combat.Subsystems.Where(s => s.Kind == SubsystemKind.ShieldGenerator))
                Assert.That(g.Shield, Is.EqualTo(g.MaxShield));
            Assert.That(launcher.Ammo, Is.EqualTo(0));
        }

        // ---------------------------------------------------------------- FreeResupply / RestoreFighters

        [Test]
        public void FreeResupplyRestocksAmmoAndMustersCrewAndMarinesBackToSpec()
        {
            var combat = Make(ShipClass.Destroyer);
            foreach (var s in combat.Subsystems.Where(s => s.Def.Weapon != null)) s.Ammo = 0;
            combat.Interior.Compartments[0].Crew = 0;

            combat.FreeResupply();

            foreach (var s in combat.Subsystems.Where(s => s.Def.Weapon != null))
                Assert.That(s.Ammo, Is.EqualTo(s.Def.Weapon.Ammo));
            Assert.That(combat.Interior.Crew, Is.EqualTo(combat.Spec.Crew).Within(1e-6));
            Assert.That(combat.Interior.Marines, Is.EqualTo(combat.Spec.Marines).Within(1e-6));
        }

        [Test]
        public void RestoreFightersCapsAtSixPerSquadron()
        {
            var combat = Make(ShipClass.Carrier);
            int max = combat.Spec.Squadrons * 6;
            combat.Fighters = 0;

            combat.RestoreFighters(max - 2);
            Assert.That(combat.Fighters, Is.EqualTo(max - 2));

            combat.RestoreFighters(1000);
            Assert.That(combat.Fighters, Is.EqualTo(max));
        }

        // ---------------------------------------------------------------- CombatSpec fallbacks

        [Test]
        public void OfFallsBackToDestroyerForAnUnknownShipClass()
        {
            var fallback = CombatSpec.Of((ShipClass)999);
            Assert.That(fallback, Is.SameAs(CombatSpec.Of(ShipClass.Destroyer)));
            Assert.That(fallback.DesignName, Is.EqualTo("Destroyer"));
        }

        [Test]
        public void RandomOfFallsBackToADestroyerDesignForAnUnknownShipClass()
        {
            var spec = CombatSpec.RandomOf((ShipClass)999, new Random(1));
            Assert.That(spec.DesignName, Is.EqualTo("Destroyer"));
        }
    }
}
