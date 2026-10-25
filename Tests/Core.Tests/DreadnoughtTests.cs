using System;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Dreadnought spinal mounts and the rear-line doctrine (U.SPC-T490): a battleship is built around two ship-length spinal
    /// mounts (a railgun and a coilgun as designed; a yard may put a tachyon lance or a plasma spinal cannon in either) fed by
    /// spinal power plants inside the hull, and it holds the rear and snipes from the extreme reach of those guns instead of
    /// closing.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class DreadnoughtTests
    {
        static readonly Faction Navy = new Faction("Navy", "NS", new Rgb(0, 0, 1)) { IsMilitary = true };
        static readonly Faction Foe = new Faction("Foe", "FOE", new Rgb(1, 0, 0)) { IsMilitary = true };

        const double StartJd = 2461324.0;

        static Ship Hull(ShipClass c, Faction f, int id = 1)
        {
            f.Treasury = 1e12;
            return new Ship { Id = id, Name = c.ToString() + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c) };
        }

        static (Economy economy, Settlement yard) Yard(Faction owner, double level = 2)
        {
            var economy = new Economy(new Diplomacy(), 0);
            var yard = economy.Add("Yard", body: 3, SettlementKind.Station, owner, 0);
            yard.Facilities.Add(new Facility(Recipe.Of(IndustryKind.Shipyard), level));
            foreach (var g in new[] { Good.Alloys, Good.Electronics, Good.Machinery, Good.Munitions, Good.Platinum, Good.RareEarths, Good.HeliumThree })
                yard.Stock[(int)g] = 1e6;
            yard.RefreshPrices();
            return (economy, yard);
        }

        static int[] SpinalSlots(Loadout l) => Enumerable.Range(0, l.Slots.Count).Where(i => l.Slots[i].Type == MountType.Spinal).ToArray();

        [Test]
        public void TheBattleshipIsBuiltAroundTwoShipLengthSpinalMountsOnLargePowerPlants()
        {
            var ship = Hull(ShipClass.Battleship, Navy);
            var spinal = ship.Combat.Subsystems.Where(s => s.Def.Spinal).ToList();
            Assert.That(spinal.Select(s => s.Kind), Is.EquivalentTo(new[] { SubsystemKind.Railgun, SubsystemKind.Coilgun }));
            foreach (var s in spinal)
            {
                Assert.That(s.Def.Z, Is.GreaterThan(0.45), "its muzzle is at the bow");
                Assert.That(s.Def.HalfDeg, Is.LessThanOrEqualTo(SpinalMounts.ConeHalfDeg), "the hull lays it: a narrow cone along the keel");
                Assert.That(s.Def.Covers(ship.Combat.Spec, FireCones.Bow), Is.True);
                Assert.That(s.Def.Covers(ship.Combat.Spec, new DVec3(0.5, 0, 0.866)), Is.False, "30 deg off the bow");
                Assert.That(s.Def.Covers(ship.Combat.Spec, new DVec3(0, 0.5, 0.866)), Is.False, "nor 30 deg above it");
                // Further than any other gun or launcher the hull carries.
                foreach (var o in ship.Combat.Subsystems.Where(o => o.Def.IsWeapon && !o.Def.Spinal && !o.Def.Weapon.Lance))
                    Assert.That(s.Def.Weapon.RangeKm, Is.GreaterThan(o.Def.Weapon.RangeKm), s.Kind + " outranges the " + o.Kind);
            }
            var rail = spinal.Single(s => s.Kind == SubsystemKind.Railgun).Def.Weapon;
            var coil = spinal.Single(s => s.Kind == SubsystemKind.Coilgun).Def.Weapon;
            Assert.That(rail, Is.SameAs(SpinalMounts.Railgun));
            Assert.That(rail.RangeKm, Is.GreaterThan(WeaponSpec.Railgun.RangeKm), "a spinal railgun outranges a destroyer's turret railgun");
            Assert.That(rail.Damage, Is.GreaterThan(WeaponSpec.Railgun.Damage * 2));
            Assert.That(coil.Cooldown, Is.LessThan(rail.Cooldown), "the coilgun cycles faster");
            Assert.That(coil.MuzzleSpeedKmS, Is.LessThan(rail.MuzzleSpeedKmS), "with a slower slug");

            var l = Outfitting.LoadoutOf(ship);
            Assert.That(SpinalSlots(l).Length, Is.EqualTo(2));
            Assert.That(l.CountFacility(FacilityKinds.SpinalPlant), Is.EqualTo(2), "a power plant for each mount");
            Assert.That(ship.Combat.Spec.SpinalPlants, Is.EqualTo(2));
            Assert.That(FacilityKinds.Of(FacilityKinds.SpinalPlant).Size, Is.GreaterThanOrEqualTo(3), "a large power plant");
            foreach (var s in spinal) Assert.That(SpinalMounts.ChargeRate(ship.Combat, s), Is.EqualTo(1));

            // No other class is built around one.
            foreach (var c in new[] { ShipClass.Cruiser, ShipClass.Destroyer, ShipClass.Carrier, ShipClass.Frigate })
            {
                var other = Hull(c, Navy, 2);
                Assert.That(other.Combat.Subsystems.Any(s => s.Def.Spinal), Is.False, c.ToString());
                Assert.That(Outfitting.LoadoutOf(other).CanAdd(FacilityKinds.Of(FacilityKinds.SpinalPlant)), Is.EqualTo(OutfitOutcome.WontFit), c + " fits no spinal plant");
            }
        }

        [Test]
        public void ASpinalMountTakesARailgunCoilgunTachyonLanceOrPlasmaSpinalCannonAndNothingElse()
        {
            var ship = Hull(ShipClass.Battleship, Navy);
            var l = Outfitting.LoadoutOf(ship);
            int slot = SpinalSlots(l)[0];
            foreach (var m in new[] { ModuleKind.Railgun, ModuleKind.Coilgun, ModuleKind.TachyonLance, ModuleKind.PlasmaCannon })
                Assert.That(Outfitting.CanFit(l, slot, m), Is.True, m + " goes in a spinal mount");
            foreach (var m in new[] { ModuleKind.HeavyLaser, ModuleKind.MissileLauncher, ModuleKind.TorpedoTube, ModuleKind.PointDefense })
                Assert.That(Outfitting.CanFit(l, slot, m), Is.False, m + " is no spinal gun");
            // A coilgun is only ever spinal: no turret mount on any hull takes one.
            foreach (var c in new[] { ShipClass.Battleship, ShipClass.Cruiser, ShipClass.Destroyer })
            {
                var lc = Outfitting.LoadoutOf(Hull(c, Navy, 3));
                for (int i = 0; i < lc.Slots.Count; i++)
                    if (lc.Slots[i].Type != MountType.Spinal) Assert.That(Outfitting.CanFit(lc, i, ModuleKind.Coilgun), Is.False, c + " slot " + i);
            }

            // Built to the hull's length: dearer than the turret of the same name.
            var (economy, yard) = Yard(Navy);
            int heavy = Enumerable.Range(0, l.Slots.Count).First(i => l.Slots[i].Type == MountType.Energy && l.Slots[i].Size == HardpointSize.Large && l.Slots[i].BearsOnBow);
            Assert.That(Outfitting.Quote(ship, yard, slot, ModuleKind.PlasmaCannon).Materials,
                Is.GreaterThan(Outfitting.Quote(ship, yard, heavy, ModuleKind.PlasmaCannon).Materials * 3), "a plasma spinal cannon is no turret");

            // A plasma spinal cannon in place of the coilgun: still spinal, its own bolt, bleeding off over distance.
            int coil = SpinalSlots(l).First(i => l.Fitted[i] == ModuleKind.Coilgun);
            Assert.That(Outfitting.Fit(economy, ship, yard, coil, ModuleKind.PlasmaCannon, out _), Is.EqualTo(OutfitOutcome.Ok));
            var plasma = ship.Combat.Subsystems.Single(s => s.Def.Spinal && s.Kind == SubsystemKind.PlasmaCannon);
            Assert.That(plasma.Def.Weapon, Is.SameAs(SpinalMounts.Plasma));
            Assert.That(plasma.Def.Weapon.RangeKm, Is.GreaterThan(EnergyWeapons.PlasmaCannon.RangeKm));
            Assert.That(plasma.Def.Weapon.DamageAt(plasma.Def.Weapon.RangeKm), Is.LessThan(plasma.Def.Weapon.Damage), "it bleeds off");
            Assert.That(plasma.Def.HalfDeg, Is.LessThanOrEqualTo(SpinalMounts.ConeHalfDeg));

            // And a tachyon lance in place of the railgun, at a yard great enough to build one.
            int railSlot = SpinalSlots(l).First(i => l.Fitted[i] == ModuleKind.Railgun);
            Assert.That(Outfitting.Fit(economy, ship, yard, railSlot, ModuleKind.TachyonLance, out _), Is.EqualTo(OutfitOutcome.Ok));
            var lance = ship.Combat.Subsystems.Single(s => s.Kind == SubsystemKind.TachyonLance);
            Assert.That(lance.Def.Spinal, Is.True);
            Assert.That(lance.Def.Weapon.Lance, Is.True);
            Assert.That(SpinalSlots(Outfitting.LoadoutOf(ship)).Length, Is.EqualTo(2), "still two spinal mounts");
        }

        [Test]
        public void WithFewerPowerPlantsThanMountsTheSpinalGunsRechargeSlowly()
        {
            var ship = Hull(ShipClass.Battleship, Navy);
            var (economy, yard) = Yard(Navy);
            var spinal = ship.Combat.Subsystems.First(s => s.Def.Spinal);
            Assert.That(SpinalMounts.ChargeRate(ship.Combat, spinal), Is.EqualTo(1));

            int Plant() => Outfitting.LoadoutOf(ship).Facilities.FindIndex(f => f.Kind == FacilityKinds.SpinalPlant);
            Assert.That(Outfitting.StripFacility(economy, ship, yard, Plant(), out _), Is.EqualTo(OutfitOutcome.Ok));
            spinal = ship.Combat.Subsystems.First(s => s.Def.Spinal);
            Assert.That(ship.Combat.Spec.SpinalPlants, Is.EqualTo(1));
            Assert.That(SpinalMounts.ChargeRate(ship.Combat, spinal), Is.EqualTo(0.5).Within(1e-9), "one plant for two mounts");

            Assert.That(Outfitting.StripFacility(economy, ship, yard, Plant(), out _), Is.EqualTo(OutfitOutcome.Ok));
            spinal = ship.Combat.Subsystems.First(s => s.Def.Spinal);
            Assert.That(ship.Combat.Spec.SpinalPlants, Is.EqualTo(0));
            Assert.That(SpinalMounts.ChargeRate(ship.Combat, spinal), Is.EqualTo(SpinalMounts.Unpowered.Value).Within(1e-9));
            var laser = ship.Combat.Subsystems.First(s => s.Kind == SubsystemKind.HeavyLaser);
            Assert.That(SpinalMounts.ChargeRate(ship.Combat, laser), Is.EqualTo(1), "a turret draws nothing from them");

            // A plant fitted back in brings the rate back up.
            Assert.That(Outfitting.FitFacility(economy, ship, yard, FacilityKinds.Of(FacilityKinds.SpinalPlant), out _), Is.EqualTo(OutfitOutcome.Ok));
            Assert.That(ship.Combat.Spec.SpinalPlants, Is.EqualTo(1));

            // In battle the drain shows: with no plants a fired spinal gun is still charging long after a fed one is ready.
            double Recharged(Ship s)
            {
                var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 1);
                b.AddShip(s, 0, DVec3.Zero, DVec3.UnitX);
                // Something to fight, beyond every gun's reach and knocked out, so the battle runs.
                b.AddShip(Hull(ShipClass.Cruiser, Foe, 9), 1, new DVec3(54, 0, 0), -DVec3.UnitX).Stunned = 1e6;
                var gun = s.Combat.Subsystems.First(x => x.Def.Spinal && x.Kind == SubsystemKind.Railgun);
                gun.Cooldown = 20;
                for (int i = 0; i < 20; i++) b.Advance(1);
                return gun.Cooldown;
            }
            var fed = Hull(ShipClass.Battleship, Navy, 5);
            var starved = Hull(ShipClass.Battleship, Navy, 6);
            Outfitting.StripFacility(economy, starved, yard, Outfitting.LoadoutOf(starved).Facilities.FindIndex(f => f.Kind == FacilityKinds.SpinalPlant), out _);
            Outfitting.StripFacility(economy, starved, yard, Outfitting.LoadoutOf(starved).Facilities.FindIndex(f => f.Kind == FacilityKinds.SpinalPlant), out _);
            Assert.That(Recharged(fed), Is.LessThanOrEqualTo(0.5), "fed: charged again in its time");
            Assert.That(Recharged(starved), Is.GreaterThan(10), "starved: still charging");
        }

        [Test]
        public void ADreadnoughtHoldsTheRearAndSnipesFromItsSpinalGunsExtremeReach()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 4);
            // A home guard that does not board, so the knocked-out mark stays on the field to be sniped at.
            var guard = new Faction("Guard", "GRD", new Rgb(0, 1, 0));
            var dread = b.AddShip(Hull(ShipClass.Battleship, guard, 0), 0, new DVec3(-6, 0, 0), DVec3.UnitX);
            var escort = b.AddShip(Hull(ShipClass.Destroyer, guard, 1), 0, new DVec3(-4, 3, 0), DVec3.UnitX);
            var enemy = b.AddShip(Hull(ShipClass.Battleship, Foe, 2), 1, new DVec3(8, 0, 0), -DVec3.UnitX);
            // A mark that cannot fire or run (knocked out, held in place) and is patched up each second so it never sinks: what
            // matters is where the dreadnought chooses to fight from.
            enemy.Stunned = 1e6;
            Assert.That(RearLine.Flies(dread), Is.True);
            Assert.That(RearLine.Flies(escort), Is.False, "a destroyer closes as it always has");

            double start = (dread.Pos - enemy.Pos).Length;
            bool spinalFired = false;
            int seen = b.FxSeq;
            for (int i = 0; i < 900 && !b.Over; i++)
            {
                b.Advance(1);
                enemy.Combat.Hull = enemy.Combat.Spec.Hull;
                enemy.Pos = new DVec3(8, 0, 0);
                enemy.Vel = DVec3.Zero;
                foreach (var s in enemy.Combat.Subsystems) s.Hp = s.Def.MaxHp;
                foreach (var f in b.Fx)
                    if (f.Seq > seen && (f.Kind == FxKind.Railgun || f.Kind == FxKind.RailgunMiss) && (f.A - dread.Pos).Length < dread.LengthKm * 2) spinalFired = true;
                seen = b.FxSeq;
            }
            Assert.That(b.Over, Is.False, $"t={b.Seconds} winner={b.Winner} enemy dead={enemy.Dead} fled={enemy.Fled} capt={enemy.Captured} surr={enemy.Surrendering} dis={enemy.Combat.Disabled} armed={enemy.Combat.Armed}; dread fled={dread.Fled} dead={dread.Dead} armed={dread.Combat.Armed} pos={dread.Pos}");
            double range = (dread.Pos - enemy.Pos).Length - enemy.Radius;
            double standoff = RearLine.StandoffKm(dread);
            TestContext.Out.WriteLine($"start {start:0.0} km, now {range:0.0} km off its mark (standoff {standoff:0.0}), escort at {(escort.Pos - enemy.Pos).Length:0.0} km");
            Assert.That(range, Is.GreaterThan(start), "it opened the range instead of closing");
            Assert.That(range, Is.EqualTo(standoff).Within(6), "it holds at its spinal guns' reach");
            Assert.That(range, Is.GreaterThan(WeaponSpec.Heavy.RangeKm), "beyond its own turrets' reach, never mind the enemy's");
            Assert.That(range, Is.LessThanOrEqualTo(SpinalMounts.ReachKm(dread.Combat)), "every spinal gun still bears");
            Assert.That((escort.Pos - enemy.Pos).Length, Is.LessThan(range), "its escort fights in front of it");
            double off = Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(dread.Forward, (enemy.Pos - dread.Pos).Normalized)))) * Astro.Rad2Deg;
            Assert.That(off, Is.LessThan(SpinalMounts.ConeHalfDeg), "its bow laid on the mark");
            Assert.That(spinalFired, Is.True, "it sniped with its spinal guns");
        }

        [Test]
        public void AHostileClosingInsideItsGuardIsBackedAwayFromAndShotAwayMountsEndTheDoctrine()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 2);
            var dread = b.AddShip(Hull(ShipClass.Battleship, Navy, 0), 0, DVec3.Zero, DVec3.UnitX);
            var enemy = b.AddShip(Hull(ShipClass.Destroyer, Foe, 1), 1, new DVec3(4, 0, 0), -DVec3.UnitX);
            dread.Target = enemy;
            RearLine.Steer(b, dread, dread.Combat.Spec.MaxSpeed, out var desired, out var face);
            Assert.That(DVec3.Dot(desired, enemy.Pos - dread.Pos), Is.LessThan(0), "it backs away from a hull inside its guard");
            Assert.That(DVec3.Dot(face.Normalized, (enemy.Pos - dread.Pos).Normalized), Is.GreaterThan(0.99), "bow still on the mark");

            // A capital is the mark it prefers.
            var capital = b.AddShip(Hull(ShipClass.Cruiser, Foe, 2), 1, new DVec3(20, 0, 0), -DVec3.UnitX);
            Assert.That(RearLine.TargetBias(dread, capital), Is.EqualTo(RearLine.CapitalBias.Value));
            Assert.That(RearLine.TargetBias(dread, enemy), Is.EqualTo(0));

            foreach (var s in dread.Combat.Subsystems) if (s.Def.Spinal) s.Hp = 0;
            Assert.That(RearLine.Flies(dread), Is.False, "with its spinal guns gone it fights as any warship");
            Assert.That(RearLine.TargetBias(dread, capital), Is.EqualTo(0));
        }

        [Test]
        public void TheSpinalAndDoctrineFiguresAreSettings()
        {
            foreach (var key in new[]
                     {
                         "spinal.railgunRange", "spinal.railgunDamage", "spinal.railgunRecharge", "spinal.coilgunRange", "spinal.coilgunDamage",
                         "spinal.coilgunRecharge", "spinal.plasmaRange", "spinal.plasmaDamage", "spinal.plasmaRecharge", "spinal.unpowered",
                         "dreadnought.standoff", "dreadnought.guardKm", "dreadnought.rearBias", "dreadnought.capitalBias",
                     })
                Assert.That(SettingsRegistry.Default.Find(key), Is.Not.Null, key);
            double saved = SpinalMounts.RailgunRange.Value;
            try
            {
                SpinalMounts.RailgunRange.Set(40);
                Assert.That(Hull(ShipClass.Battleship, Navy).Combat.Subsystems.First(s => s.Def.Spinal && s.Kind == SubsystemKind.Railgun).Def.Weapon.RangeKm, Is.EqualTo(40));
            }
            finally
            {
                SpinalMounts.RailgunRange.Set(saved);
            }
            Assert.That(ModuleSpec.Of(ModuleKind.Coilgun).Kind, Is.EqualTo(ModuleKind.Coilgun), "the coilgun sits at its own place in the catalogue");
        }
    }
}
