using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Shields come from emitters, not facing arcs (U.SPC-T501): each shield emitter on the hull projects a field over the
    /// patch of hull around it, a shot drains the field of the patch it strikes in 3D, a patch shot away is holed while the
    /// rest stays covered, each patch regrows on its own clock, gunners focus a weakened patch, and emergency repair crews
    /// bring back shot-away emitters and gun mounts given time. Changes settings, so it runs on its own.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class ShieldEmitterTests
    {
        const double StartJd = 2461324.0;

        static readonly Faction Blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
        static readonly Faction Red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };

        static ShipCombat Make(ShipClass c) => new ShipCombat(CombatSpec.Of(c), c);

        static Ship Hull(ShipClass c, Faction f, int id) => new Ship
        {
            Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c),
        };

        static DVec3 At(Subsystem s) => new DVec3(s.Def.X, s.Def.Y, s.Def.Z);

        static Subsystem[] Emitters(ShipCombat c) => c.Subsystems.Where(s => s.Kind == SubsystemKind.ShieldGenerator).ToArray();

        static Subsystem Bow(ShipCombat c) => Emitters(c).OrderByDescending(s => s.Def.Z).First();

        /// <summary>A destroyer's hull with one emitter on its back and one under its keel, and nothing else to hit.</summary>
        static ShipCombat TopAndBottom()
        {
            var spec = CombatSpec.Of(ShipClass.Destroyer).Clone();
            spec.Layout = new[]
            {
                new SubsystemDef(SubsystemKind.ShieldGenerator, 0, 0.05, 0),
                new SubsystemDef(SubsystemKind.ShieldGenerator, 0, -0.05, 0),
                new SubsystemDef(SubsystemKind.Reactor, 0, 0, -0.3),
            };
            return new ShipCombat(spec, ShipClass.Destroyer);
        }

        [Test]
        public void NoShieldEmitterHasAFacingArc()
        {
            foreach (ShipClass cls in Enum.GetValues(typeof(ShipClass)))
            foreach (var spec in CombatSpec.DesignsOf(cls))
            foreach (var d in spec.Layout.Where(d => d.Kind == SubsystemKind.ShieldGenerator))
                Assert.That(d.BearsEverywhere, Is.True, spec.DesignName + "'s emitters cover their patch, not an arc or a cone");
        }

        [Test]
        public void EveryEmitterCoversThePatchOfHullAroundIt()
        {
            foreach (ShipClass cls in Enum.GetValues(typeof(ShipClass)))
            foreach (var spec in CombatSpec.DesignsOf(cls))
            {
                var c = new ShipCombat(spec, cls);
                foreach (var e in Emitters(c))
                    Assert.That(c.PatchAt(At(e)), Is.SameAs(e), spec.DesignName + ": an emitter's own spot is in its own patch");
            }
        }

        [Test]
        public void AHitDrainsOnlyTheFieldOfThePatchItStrikes()
        {
            var c = Make(ShipClass.Destroyer);
            var bow = Bow(c);
            double charge = bow.Shield;
            Assert.That(Collisions.ApplyDamage(c, new DVec3(0, 0, 0.5), charge * 0.5), Is.EqualTo(0));
            Assert.That(bow.Shield, Is.EqualTo(charge * 0.5).Within(1e-9));
            foreach (var e in Emitters(c).Where(e => e != bow))
                Assert.That(e.Shield, Is.EqualTo(e.MaxShield), "the rest of the hull is untouched");
        }

        [Test]
        public void APatchShotAwayIsHoledWhileTheRestOfTheHullStaysCovered()
        {
            var c = Make(ShipClass.Destroyer);
            var bow = Bow(c);
            bow.Hp = 0;
            bow.Shield = 0;
            Assert.That(c.EmitterFor(new DVec3(0, 0, 0.5)), Is.Null, "the bow is holed");
            Assert.That(ShieldEmitters.PatchStrength(c, new DVec3(0, 0, 0.5)), Is.EqualTo(0));
            Assert.That(ShieldEmitters.PatchStrength(c, new DVec3(0, 0, -0.5)), Is.EqualTo(1));
            Assert.That(Collisions.ApplyDamage(c, new DVec3(0, 0, 0.5), 200), Is.GreaterThan(0), "a hit on the bow goes through");
            double hull = c.Hull;
            Assert.That(Collisions.ApplyDamage(c, new DVec3(0, 0, -0.5), 200), Is.EqualTo(0), "a hit on the stern is still soaked");
            Assert.That(c.Hull, Is.EqualTo(hull));
        }

        [Test]
        public void ShotsHitTheShieldOfThePatchTheyStrikeInThreeDimensions()
        {
            var c = TopAndBottom();
            var top = c.Subsystems[0];
            var bottom = c.Subsystems[1];
            Collisions.ApplyDamage(c, new DVec3(0, 0.1, 0), 100);
            Assert.That(top.Shield, Is.LessThan(top.MaxShield), "struck from above, the dorsal field takes it");
            Assert.That(bottom.Shield, Is.EqualTo(bottom.MaxShield));

            // A shot at the hull as a whole lands on the side it came in from: one rising from below meets the keel's field.
            var strike = ShieldEmitters.StrikePoint(c.Spec, DVec3.Zero, new DVec3(0, 1, 0));
            Assert.That(c.PatchAt(strike), Is.SameAs(bottom));
            strike = ShieldEmitters.StrikePoint(c.Spec, DVec3.Zero, new DVec3(0, -1, 0));
            Assert.That(c.PatchAt(strike), Is.SameAs(top));
        }

        [Test]
        public void AShotInBattleDrainsTheFieldOverWhereItLands()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 3);
            var target = b.AddShip(Hull(ShipClass.Destroyer, Red, 1), 1, DVec3.Zero, DVec3.UnitX);
            var bow = Bow(target.Combat);
            var stern = Emitters(target.Combat).OrderBy(s => s.Def.Z).First();
            double bowBefore = bow.Shield, sternBefore = stern.Shield;
            // Fired from astern at the stern of a hull facing +X.
            var at = target.PointLocal(new DVec3(0, 0, -0.5));
            b.ApplyDamage(target, null, 50, at, null, 1, DVec3.UnitX);
            Assert.That(stern.Shield, Is.LessThan(sternBefore));
            Assert.That(bow.Shield, Is.EqualTo(bowBefore));
        }

        [Test]
        public void EachPatchRegrowsOnItsOwnClock()
        {
            var c = Make(ShipClass.Destroyer);
            c.ZeroAllShields();
            var bow = Bow(c);
            bow.LastHitSeconds = 100;
            c.RegenShields(100 + ShieldEmitters.RegenDelay.Value * 0.5, 1);
            Assert.That(bow.Shield, Is.EqualTo(0), "the patch under fire stays down");
            foreach (var e in Emitters(c).Where(e => e != bow))
                Assert.That(e.Shield, Is.GreaterThan(0), "the rest recover");
        }

        [Test]
        public void ARegrowthRateOfZeroLeavesAFieldShotAwayDown()
        {
            double saved = ShieldEmitters.RegenRate.Value;
            try
            {
                ShieldEmitters.RegenRate.Set(0);
                var c = Make(ShipClass.Destroyer);
                c.ZeroAllShields();
                c.RegenShields(1000, 10);
                Assert.That(c.Shield, Is.EqualTo(0));
            }
            finally
            {
                ShieldEmitters.RegenRate.Set(saved);
            }
        }

        [Test]
        public void GunnersFavourAMountInAWeakenedPatch()
        {
            var c = Make(ShipClass.Destroyer);
            var bow = Bow(c);
            var inBow = c.Subsystems.First(s => s.Kind != SubsystemKind.ShieldGenerator && !s.Def.Buried && c.PatchAt(At(s)) == bow);
            var elsewhere = c.Subsystems.First(s => s.Kind != SubsystemKind.ShieldGenerator && !s.Def.Buried && c.PatchAt(At(s)) != bow);
            Assert.That(ShieldEmitters.FocusWeight(c, inBow), Is.EqualTo(1).Within(1e-9), "under a full field, no pull");
            bow.Shield = 0;
            Assert.That(ShieldEmitters.FocusWeight(c, inBow), Is.EqualTo(1 + ShieldEmitters.FocusFire.Value).Within(1e-9), "holed: the full pull");
            Assert.That(ShieldEmitters.FocusWeight(c, elsewhere), Is.EqualTo(1).Within(1e-9));

            double saved = ShieldEmitters.FocusFire.Value;
            try
            {
                ShieldEmitters.FocusFire.Set(0);
                Assert.That(ShieldEmitters.FocusWeight(c, inBow), Is.EqualTo(1), "at 0 the fields are ignored");
            }
            finally
            {
                ShieldEmitters.FocusFire.Set(saved);
            }
        }

        [Test]
        public void AiGunnersPickTheHardpointsInAHoledPatchMoreOften()
        {
            int Picks(bool focus)
            {
                bool saved = Broadsides.AiFocusFire.Value;
                try
                {
                    Broadsides.AiFocusFire.Set(focus);
                    var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 11);
                    var shooter = b.AddShip(Hull(ShipClass.Cruiser, Blue, 0), 0, new DVec3(-8, 0, 0), DVec3.UnitX);
                    var target = b.AddShip(Hull(ShipClass.Destroyer, Red, 1), 1, DVec3.Zero, DVec3.UnitX);
                    var bow = Bow(target.Combat);
                    bow.Shield = 0;
                    int n = 0;
                    for (int i = 0; i < 4000; i++)
                    {
                        var s = b.PickSubsystem(target, shooter);
                        if (s != null && s != bow && target.Combat.PatchAt(At(s)) == bow) n++;
                    }
                    return n;
                }
                finally
                {
                    Broadsides.AiFocusFire.Set(saved);
                }
            }
            int spread = Picks(false), focused = Picks(true);
            Assert.That(focused, Is.GreaterThan(spread * 1.3), "focused " + focused + " vs spread " + spread);
        }

        [Test]
        public void EmergencyRepairCrewsBringBackAShotAwayEmitterAndGunGivenTime()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 3);
            var me = b.AddShip(Hull(ShipClass.Battleship, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            me.PlayerControlled = true;
            Broadsides.Of(me);
            var emitter = Bow(me.Combat);
            var gun = me.Combat.Subsystems.First(s => s.Kind == SubsystemKind.HeavyLaser);
            var engine = me.Combat.Subsystems.First(s => s.Kind == SubsystemKind.Engine);
            emitter.Hp = 0;
            emitter.Shield = 0;
            gun.Hp = 0;
            engine.Hp = 0;

            Broadsides.StepDamageControl(me, 1);
            Assert.That(emitter.Destroyed && gun.Destroyed, Is.True, "not in a second");
            for (int i = 0; i < 600; i++) Broadsides.StepDamageControl(me, 1);
            Assert.That(emitter.Destroyed, Is.False, "the emitter is back");
            Assert.That(gun.Destroyed, Is.False, "and the gun");
            Assert.That(engine.Destroyed, Is.True, "an engine shot away waits for a yard");
            Assert.That(me.Broadside.Restored, Is.EqualTo(2));
            Assert.That(me.Combat.EmitterFor(At(emitter)), Is.SameAs(emitter), "its patch is covered again");
        }

        [Test]
        public void AnEmergencyRepairRateOfZeroLeavesWhatWasShotAwayLost()
        {
            double saved = ShieldEmitters.EmergencyRepairRate.Value;
            try
            {
                ShieldEmitters.EmergencyRepairRate.Set(0);
                var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 3);
                var me = b.AddShip(Hull(ShipClass.Battleship, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
                me.PlayerControlled = true;
                Broadsides.Of(me);
                var emitter = Bow(me.Combat);
                emitter.Hp = 0;
                for (int i = 0; i < 600; i++) Broadsides.StepDamageControl(me, 1);
                Assert.That(emitter.Destroyed, Is.True);
            }
            finally
            {
                ShieldEmitters.EmergencyRepairRate.Set(saved);
            }
        }

        [Test]
        public void TheShieldEmitterSettingsAreInTheMenu()
        {
            Assert.That(SettingsRegistry.Default.Find("shield.regenDelay"), Is.SameAs(ShieldEmitters.RegenDelay));
            Assert.That(SettingsRegistry.Default.Find("shield.regenRate"), Is.SameAs(ShieldEmitters.RegenRate));
            Assert.That(SettingsRegistry.Default.Find("shield.focusFire"), Is.SameAs(ShieldEmitters.FocusFire));
            Assert.That(SettingsRegistry.Default.Find("broadside.emergencyRepair"), Is.SameAs(ShieldEmitters.EmergencyRepairRate));
            Assert.That(SettingsRegistry.Default.Find("broadside.emergencyRepairWork"), Is.SameAs(ShieldEmitters.EmergencyRepairWork));
            Assert.That(SettingsRegistry.Default.Find("broadside.restoredHealth"), Is.SameAs(ShieldEmitters.RestoredHealth));
            Assert.That(ShieldEmitters.RegenDelay.Group, Is.EqualTo(SettingGroups.Broadsides));
        }
    }
}
