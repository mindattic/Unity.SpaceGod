using System;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The construction catalogue (U.SPC-T377): every buildable hull -- each ship class's designs, stations and
    /// satellites -- is a frame whose sockets are its real hardpoints (turrets, missile launchers, torpedo tubes, point
    /// defence, shields, engines, bays), placed where the battle fires from; a construction plan fits parts into them
    /// and gives the hull's spec, bill and price; and capital torpedo tubes are a real weapon that fires torpedoes.
    /// </summary>
    [TestFixture]
    public class ConstructionTests
    {
        static readonly Faction Navy = new Faction("Navy", "NS", new Rgb(0, 0, 1)) { IsMilitary = true };

        [Test]
        public void EveryShipClassAndEveryStationAndSatelliteKindHasAFrame()
        {
            foreach (ShipClass c in Enum.GetValues(typeof(ShipClass)))
            {
                var f = HullFrames.ForClass(c);
                Assert.That(f, Is.Not.Null, c.ToString());
                Assert.That(f.Category, Is.EqualTo(HullFrames.CategoryOf(c)));
                Assert.That(f.BuildPoints, Is.EqualTo(Economy.HullCost(c)));
            }
            foreach (HullCategory cat in Enum.GetValues(typeof(HullCategory)))
                Assert.That(HullFrames.InCategory(cat).Any(), Is.True, cat.ToString());
            Assert.That(HullFrames.InCategory(HullCategory.Station).All(f => f.Static && f.Class == null), Is.True);
            Assert.That(HullFrames.InCategory(HullCategory.Satellite).All(f => f.Static && f.Class == null), Is.True);
        }

        [Test]
        public void AFramesHardpointsAreItsDesignsMountsAtTheirOwnPlacesWithUniqueSockets()
        {
            foreach (var f in HullFrames.Catalogue)
            {
                Assert.That(f.Hardpoints.Select(h => h.Socket).Distinct().Count(), Is.EqualTo(f.Hardpoints.Count), f.Id);
                foreach (var h in f.Hardpoints.Where(h => h.LayoutIndex >= 0))
                {
                    var d = f.Design.Layout[h.LayoutIndex];
                    Assert.That((h.X, h.Y, h.Z), Is.EqualTo((d.X, d.Y, d.Z)), f.Id + " " + h.Socket);
                    Assert.That(h.ArcWidthDeg, Is.EqualTo(d.ArcWidthDeg));
                }
                Assert.That(f.Hardpoints.Count(h => h.Slot == SlotKind.InternalBay), Is.EqualTo(f.InternalBays), f.Id);
            }
            // Every armed hull has weapon sockets, a fighter's small and a battleship's large.
            Assert.That(HullFrames.ForClass(ShipClass.Fighter).Hardpoints.Where(h => h.IsWeaponMount).All(h => h.Size == HardpointSize.Small), Is.True);
            Assert.That(HullFrames.ForClass(ShipClass.Battleship).Hardpoints.Any(h => h.Slot == SlotKind.HeavyMount && h.Size == HardpointSize.Large), Is.True);
            foreach (var f in HullFrames.InCategory(HullCategory.Satellite).Concat(HullFrames.InCategory(HullCategory.Station)))
                Assert.That(f.Hardpoints.Any(h => h.IsWeaponMount), Is.True, f.Id);
        }

        [Test]
        public void CapitalShipsAndTheOrbitalStationCarryTorpedoTubes()
        {
            Assert.That(HullFrames.ForClass(ShipClass.Battleship).Hardpoints.Count(h => h.Slot == SlotKind.TorpedoTube), Is.EqualTo(2));
            Assert.That(HullFrames.ForClass(ShipClass.Cruiser).Hardpoints.Count(h => h.Slot == SlotKind.TorpedoTube), Is.EqualTo(1));
            var station = HullFrames.Of("station/orbital-station");
            Assert.That(station.Hardpoints.Count(h => h.Slot == SlotKind.TorpedoTube), Is.EqualTo(2));
            var tube = station.Hardpoints.First(h => h.Slot == SlotKind.TorpedoTube);
            Assert.That(tube.Stock, Is.EqualTo(ModuleKind.TorpedoTube));
            Assert.That(Hardpoint.ArtKey(tube, tube.Stock), Is.EqualTo("TorpedoTube/Large"));
        }

        [Test]
        public void ATorpedoTubeTakesATubeOrALauncherAndABowMountTakesATube()
        {
            Assert.That(ModuleSpec.Fits(ModuleKind.TorpedoTube, SlotKind.TorpedoTube, false), Is.True);
            Assert.That(ModuleSpec.Fits(ModuleKind.MissileLauncher, SlotKind.TorpedoTube, false), Is.True);
            Assert.That(ModuleSpec.Fits(ModuleKind.HeavyLaser, SlotKind.TorpedoTube, true), Is.False);
            Assert.That(ModuleSpec.Fits(ModuleKind.TorpedoTube, SlotKind.HeavyMount, true), Is.True);
            Assert.That(ModuleSpec.Fits(ModuleKind.TorpedoTube, SlotKind.HeavyMount, false), Is.False, "a tube is fixed: only a mount that bears on the bow");
            Assert.That(ModuleSpec.Fits(ModuleKind.TorpedoTube, SlotKind.UtilityMount, true), Is.False);
        }

        [Test]
        public void APlanBuildsTheSpecBillAndPriceOfWhatIsFitted()
        {
            var plan = new ConstructionPlan(HullFrames.ForClass(ShipClass.Destroyer));
            Assert.That(plan.Bill()[Good.Alloys], Is.EqualTo(Economy.HullCost(ShipClass.Destroyer)));
            Assert.That(plan.BasePrice(), Is.EqualTo(Economy.HullPrice(ShipClass.Destroyer)));
            int bow = plan.Hardpoints.First(h => h.Slot == SlotKind.HeavyMount && h.BearsOnBow && h.Stock == ModuleKind.MissileLauncher).SlotIndex;
            Assert.That(plan.Fit(bow, ModuleKind.TorpedoTube), Is.EqualTo(ConstructionOutcome.Ok));
            int bay = plan.Hardpoints.First(h => h.Slot == SlotKind.InternalBay).SlotIndex;
            Assert.That(plan.Fit(bay, ModuleKind.ArmourPlating), Is.EqualTo(ConstructionOutcome.Ok));
            Assert.That(plan.Fit(bay, ModuleKind.Engine), Is.EqualTo(ConstructionOutcome.WontFit));
            Assert.That(plan.Fit(999, ModuleKind.Brig), Is.EqualTo(ConstructionOutcome.NoSuchSocket));

            var spec = plan.Build();
            Assert.That(spec.Layout.Count(d => d.Kind == SubsystemKind.TorpedoTube), Is.EqualTo(1));
            Assert.That(spec.Layout.First(d => d.Kind == SubsystemKind.TorpedoTube).Weapon, Is.SameAs(WeaponSpec.HeavyTorpedo));
            Assert.That(spec.Armor, Is.GreaterThan(CombatSpec.Of(ShipClass.Destroyer).Armor));

            var bill = plan.Bill();
            Assert.That(bill[Good.Munitions], Is.GreaterThan(0), "the tube's torpedoes, net of the launcher's rounds");
            Assert.That(bill[Good.Alloys], Is.GreaterThan(Economy.HullCost(ShipClass.Destroyer)));
            Assert.That(plan.BasePrice(), Is.GreaterThan(Economy.HullPrice(ShipClass.Destroyer)));
        }

        [Test]
        public void APlanAppliedToANewShipBecomesItsLoadoutAndCombatSpec()
        {
            var plan = new ConstructionPlan(HullFrames.ForClass(ShipClass.Battleship));
            int bay = plan.Hardpoints.First(h => h.Slot == SlotKind.InternalBay).SlotIndex;
            plan.Fit(bay, ModuleKind.Scanner);
            var ship = new Ship { Id = 1, Name = "BB", Spec = ShipClassSpec.Of(ShipClass.Battleship), Faction = Navy };
            Assert.That(plan.Apply(ship), Is.True);
            Assert.That(ship.Loadout.Count(ModuleKind.Scanner), Is.EqualTo(1));
            Assert.That(ship.Combat.Subsystems.Count(s => s.Kind == SubsystemKind.TorpedoTube), Is.EqualTo(2));
            Assert.That(ship.Loadout, Is.Not.SameAs(plan.Loadout), "the ship keeps its own copy");

            var fighter = new Ship { Id = 2, Name = "F", Spec = ShipClassSpec.Of(ShipClass.Fighter), Faction = Navy };
            Assert.That(plan.Apply(fighter), Is.False, "a battleship plan builds no fighter");
            Assert.That(new ConstructionPlan(HullFrames.Of("satellite/weapons")).Apply(ship), Is.False);
        }

        [Test]
        public void ASatelliteOrStationPlanBuildsAStaticCombatState()
        {
            var plan = new ConstructionPlan(HullFrames.Of("satellite/sensor"));
            int bay = plan.Hardpoints.First(h => h.Slot == SlotKind.InternalBay).SlotIndex;
            Assert.That(plan.Fit(bay, ModuleKind.Scanner), Is.EqualTo(ConstructionOutcome.Ok));
            var c = plan.BuildCombat();
            Assert.That(c.Spec.MaxSpeed, Is.EqualTo(0));
            Assert.That(c.Subsystems.Any(s => s.Kind == SubsystemKind.PointDefense), Is.True);

            var station = new ConstructionPlan(HullFrames.Of("station/defence-platform"));
            Assert.That(station.FitFirst(ModuleKind.ArmourPlating), Is.GreaterThanOrEqualTo(0));
            Assert.That(station.BuildCombat().Spec.Armor, Is.GreaterThan(CombatSpec.Platform.Armor));
        }

        [Test]
        public void NewFramesPlugInByRegistration()
        {
            var frame = new HullFrame("test/picket", "Picket", HullCategory.Satellite, null, HullFrames.WeaponsSatellite, 2, false, 500);
            HullFrames.Register(frame);
            Assert.That(HullFrames.Of("test/picket"), Is.SameAs(frame));
            Assert.That(HullFrames.InCategory(HullCategory.Satellite), Does.Contain(frame));
            Assert.That(frame.Hardpoints.Count(h => h.Slot == SlotKind.InternalBay), Is.EqualTo(2));
        }

        [Test]
        public void ABattleshipsTorpedoTubesFireTorpedoesAtAHullAhead()
        {
            var blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
            var red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };
            Ship Hull(ShipClass c, Faction f, int id) => new Ship
            {
                Id = id, Name = f.Prefix + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c),
            };
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), 2461324.0, 1);
            b.AddShip(Hull(ShipClass.Battleship, blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(25, 0, 0), -DVec3.UnitX);
            var tubes = b.Units[0].Combat.Subsystems.Where(s => s.Kind == SubsystemKind.TorpedoTube).ToList();
            Assert.That(tubes.Count, Is.EqualTo(2));
            int full = tubes.Sum(s => s.Ammo);
            bool sawTorpedo = false;
            for (int i = 0; i < 600 && !sawTorpedo; i++)
            {
                b.Advance(0.1);
                sawTorpedo = b.Missiles.Any(m => m.Side == 0 && m.Kind == MissileKind.Torpedo);
            }
            Assert.That(sawTorpedo, Is.True);
            Assert.That(tubes.Sum(s => s.Ammo), Is.LessThan(full));
        }
    }
}
