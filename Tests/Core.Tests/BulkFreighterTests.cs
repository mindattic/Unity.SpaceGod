using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Bulk freighters as truss-and-pod ships (U.SPC-T482): a bare truss with an engine astern and standard cargo pods and
    /// fuel spheres clamped along it. Its hold is its pods, its tanks its spheres; it carries no armour and no weapons; it
    /// rides the lanes at a bulk toll, is slow to speed up and slow down off them, and never lands on a world.
    /// </summary>
    [TestFixture]
    public class BulkFreighterTests
    {
        const double StartJd = 2461324.0;

        static readonly Faction Navy = new Faction("Navy", "NS", new Rgb(0, 0, 1)) { IsMilitary = true };

        static (Economy economy, Settlement yard) Yard()
        {
            var economy = new Economy(new Diplomacy(), 0);
            var yard = economy.Add("Yard", body: 3, SettlementKind.Station, Navy, 0);
            yard.Facilities.Add(new Facility(Recipe.Of(IndustryKind.Shipyard), 1));
            foreach (var g in new[] { Good.Alloys, Good.Electronics, Good.Machinery, Good.Munitions }) yard.Stock[(int)g] = 1e6;
            yard.RefreshPrices();
            return (economy, yard);
        }

        static Ship Hull(ShipClass c)
        {
            Navy.Treasury = 1e12;
            return new Ship
            {
                Id = 1, Name = c.ToString(), Spec = ShipClassSpec.Of(c), Faction = Navy, Combat = new ShipCombat(CombatSpec.Of(c), c),
                CapacityTonnes = Economy.CapacityOf(c),
            };
        }

        static FacilityKind Pod => FacilityKinds.Of(FacilityKinds.CargoPod);
        static FacilityKind Sphere => FacilityKinds.Of(FacilityKinds.FuelSphere);

        static int IndexOf(Ship ship, string kind) => Outfitting.LoadoutOf(ship).Facilities.FindIndex(f => f.Kind == kind);

        [Test]
        public void AFreighterIsATrussOfPodsAndSpheresWithNoArmourAndNoWeapons()
        {
            var spec = CombatSpec.Of(ShipClass.Freighter);
            Assert.That(spec.Armor, Is.EqualTo(0), "a truss freighter carries no armour");
            Assert.That(spec.Layout.Any(d => d.IsWeapon), Is.False, "a truss freighter carries no weapons, not even point defence or mines");
            var combat = new ShipCombat(spec, ShipClass.Freighter);
            Assert.That(combat.Armed, Is.False);

            // Nowhere to put a gun: its only hardpoints are its shield emitters and its engine.
            var l = new Loadout(spec, ShipClass.Freighter, Economy.CapacityOf(ShipClass.Freighter));
            Assert.That(l.Slots.All(s => s.Kind == SlotKind.ShieldMount || s.Kind == SlotKind.EngineMount), Is.True);

            var layout = FacilityLayouts.For(ShipClass.Freighter);
            Assert.That(layout.Count(f => f.Kind == FacilityKinds.CargoPod), Is.EqualTo(BulkFreighters.StockPods));
            Assert.That(layout.Count(f => f.Kind == FacilityKinds.FuelSphere), Is.EqualTo(BulkFreighters.StockSpheres));
            Assert.That(layout.Any(f => f.Kind == FacilityKinds.Hold && f.Name == "Pod gantry"), Is.True);
            Assert.That(BulkFreighters.IsTruss(ShipClass.Freighter), Is.True);
            Assert.That(BulkFreighters.IsTruss(ShipClass.Miner), Is.False);
        }

        [Test]
        public void ItsHoldIsItsPodsAndItsTanksItsSpheresSoTheEconomyCarriesWhatItDid()
        {
            // The stock design carries what a freighter's holds and tanks did before, so trade runs move as much as ever.
            Assert.That(Economy.CapacityOf(ShipClass.Freighter), Is.EqualTo(BulkFreighters.StockPods * BulkFreighters.PodTonnes));
            Assert.That(Economy.CapacityOf(ShipClass.Freighter), Is.EqualTo(20000));
            Assert.That(CombatSpec.Of(ShipClass.Freighter).PropellantTonnes, Is.EqualTo(BulkFreighters.StockSpheres * BulkFreighters.SphereTonnes));
            Assert.That(CombatSpec.Of(ShipClass.Freighter).PropellantTonnes, Is.EqualTo(6000));

            var ship = Hull(ShipClass.Freighter);
            Assert.That(Outfitting.LoadoutOf(ship).CapacityTonnes, Is.EqualTo(ship.CapacityTonnes));
            Assert.That(BulkFreighters.Pods(ship), Is.EqualTo(BulkFreighters.StockPods));
            Assert.That(BulkFreighters.Spheres(ship), Is.EqualTo(BulkFreighters.StockSpheres));
            // The grid hold: a row for each pod.
            Assert.That(ship.PersonalHold.Height, Is.EqualTo(BulkFreighters.StockPods * BulkFreighters.PodRows));

            // The boarding interior is the cockpit, the gantry the pods open on, the engine room and the security post off the
            // gantry: nobody walks into a pod.
            var interior = ship.Combat.Interior;
            Assert.That(interior.Compartments.Count, Is.EqualTo(4));
            Assert.That(interior.Of(CompartmentKind.Hold), Is.Not.Null);
        }

        [Test]
        public void AYardClampsOnAnotherPodForMoreCargoAndTakesOneOffOnlyWhenWhatIsAboardFits()
        {
            var (economy, yard) = Yard();
            var ship = Hull(ShipClass.Freighter);
            double capacity = ship.CapacityTonnes;
            int rows = ship.PersonalHold.Height;

            Assert.That(Outfitting.FitFacility(economy, ship, yard, Pod, out _), Is.EqualTo(OutfitOutcome.Ok));
            Assert.That(BulkFreighters.Pods(ship), Is.EqualTo(BulkFreighters.StockPods + 1));
            Assert.That(ship.CapacityTonnes, Is.EqualTo(capacity + BulkFreighters.PodTonnes).Within(1e-6));
            Assert.That(ship.PersonalHold.Height, Is.EqualTo(rows + BulkFreighters.PodRows));

            // A full load will not fit in one pod fewer.
            ship.HasCargo = true;
            ship.CargoGood = Good.Alloys;
            ship.CargoTonnes = ship.CapacityTonnes;
            Assert.That(Outfitting.StripFacility(economy, ship, yard, IndexOf(ship, FacilityKinds.CargoPod), out _), Is.EqualTo(OutfitOutcome.HoldTooFull));

            // Half a load does: two pods come off, one of them the design's own.
            ship.CargoTonnes = capacity * 0.5;
            Assert.That(Outfitting.StripFacility(economy, ship, yard, IndexOf(ship, FacilityKinds.CargoPod), out _), Is.EqualTo(OutfitOutcome.Ok));
            Assert.That(Outfitting.StripFacility(economy, ship, yard, IndexOf(ship, FacilityKinds.CargoPod), out _), Is.EqualTo(OutfitOutcome.Ok));
            Assert.That(BulkFreighters.Pods(ship), Is.EqualTo(BulkFreighters.StockPods - 1));
            Assert.That(ship.CapacityTonnes, Is.EqualTo(capacity - BulkFreighters.PodTonnes).Within(1e-6));
            Assert.That(ship.PersonalHold.Height, Is.EqualTo(rows - BulkFreighters.PodRows));
        }

        [Test]
        public void ASphereTradedForAPodTradesRangeForCargo()
        {
            var (economy, yard) = Yard();
            var ship = Hull(ShipClass.Freighter);
            double tanks = ship.PropellantCapacity, capacity = ship.CapacityTonnes;
            Assert.That(Outfitting.StripFacility(economy, ship, yard, IndexOf(ship, FacilityKinds.FuelSphere), out _), Is.EqualTo(OutfitOutcome.Ok));
            Assert.That(ship.PropellantCapacity, Is.EqualTo(tanks - BulkFreighters.SphereTonnes).Within(1e-6));
            Assert.That(Outfitting.FitFacility(economy, ship, yard, Pod, out _), Is.EqualTo(OutfitOutcome.Ok));
            Assert.That(ship.CapacityTonnes, Is.EqualTo(capacity + BulkFreighters.PodTonnes).Within(1e-6));
            Assert.That(Outfitting.FitFacility(economy, ship, yard, Sphere, out _), Is.EqualTo(OutfitOutcome.Ok));
            Assert.That(ship.PropellantCapacity, Is.EqualTo(tanks).Within(1e-6));
        }

        [Test]
        public void NoYardFitsATrussFreighterArmourOrRacksAndNoOtherHullTakesPods()
        {
            var (economy, yard) = Yard();
            var freighter = Hull(ShipClass.Freighter);
            Assert.That(Outfitting.FitFacility(economy, freighter, yard, FacilityKinds.Of(FacilityKinds.ArmourPlating), out _), Is.EqualTo(OutfitOutcome.WontFit),
                "every kilogram of a truss freighter goes to cargo");
            Assert.That(Outfitting.FitFacility(economy, freighter, yard, FacilityKinds.Of(FacilityKinds.CargoRacks), out _), Is.EqualTo(OutfitOutcome.WontFit),
                "its pods are its hold");
            Assert.That(HullFrames.ForClass(ShipClass.Freighter).CanCarry(FacilityKinds.Of(FacilityKinds.ArmourPlating)), Is.False);
            Assert.That(HullFrames.ForClass(ShipClass.Freighter).CanCarry(Pod), Is.True);

            // An ore hauler of the same hull category still takes plate and racks, but nothing without a truss takes a pod.
            var miner = Hull(ShipClass.Miner);
            Assert.That(Outfitting.FitFacility(economy, miner, yard, FacilityKinds.Of(FacilityKinds.CargoRacks), out _), Is.EqualTo(OutfitOutcome.Ok));
            Assert.That(Outfitting.FitFacility(economy, miner, yard, Pod, out _), Is.EqualTo(OutfitOutcome.WontFit));
            Assert.That(Outfitting.FitFacility(economy, Hull(ShipClass.Destroyer), yard, Sphere, out _), Is.EqualTo(OutfitOutcome.WontFit));
        }

        [Test]
        public void ANewFreighterBuiltWithExtraPodsCarriesThemFromTheYard()
        {
            var plan = new ConstructionPlan(HullFrames.ForClass(ShipClass.Freighter));
            Assert.That(plan.FitFacility(Pod), Is.EqualTo(ConstructionOutcome.Ok));
            var ship = new Ship { Id = 2, Name = "New", Spec = ShipClassSpec.Of(ShipClass.Freighter), Faction = Navy };
            Assert.That(plan.Apply(ship), Is.True);
            Assert.That(ship.CapacityTonnes, Is.EqualTo(Economy.CapacityOf(ShipClass.Freighter) + BulkFreighters.PodTonnes).Within(1e-6));
            Assert.That(ship.PersonalHold.Height, Is.EqualTo((BulkFreighters.StockPods + 1) * BulkFreighters.PodRows));
        }

        [Test]
        public void ItsModelShowsAPodClampedForEveryShareOfItsLoad()
        {
            var ship = Hull(ShipClass.Freighter);
            Assert.That(BulkFreighters.LoadedPods(ship), Is.EqualTo(0), "an empty freighter is a bare truss");
            ship.HasCargo = true;
            ship.CargoGood = Good.Food;
            ship.CargoTonnes = 1;
            Assert.That(BulkFreighters.LoadedPods(ship), Is.EqualTo(1), "any load at all fills a pod");
            ship.CargoTonnes = ship.CapacityTonnes / 2;
            Assert.That(BulkFreighters.LoadedPods(ship), Is.EqualTo(BulkFreighters.StockPods / 2));
            ship.CargoTonnes = ship.CapacityTonnes;
            Assert.That(BulkFreighters.LoadedPods(ship), Is.EqualTo(BulkFreighters.StockPods));
        }

        [Test]
        public void ItRidesTheLanesAtABulkTollAndIsSlowToSpeedUpAndSlowDownOffThem()
        {
            double mass = 30000;
            Assert.That(Economy.LaneToll(ShipClass.Freighter, mass), Is.EqualTo(Economy.LaneToll(mass) * BulkFreighters.LaneTollShare.Value).Within(1e-6));
            Assert.That(Economy.LaneToll(ShipClass.Freighter, mass), Is.LessThan(Economy.LaneToll(mass)));
            Assert.That(Economy.LaneToll(ShipClass.Miner, mass), Is.EqualTo(Economy.LaneToll(mass)));
            Assert.That(BulkFreighters.LaneTollShare.Default, Is.LessThan(1));

            // Off the lanes, its weak drive: the slowest of the civilian hulls to build up or shed speed, in a battle or between worlds.
            var freighter = CombatSpec.Of(ShipClass.Freighter);
            foreach (var c in new[] { ShipClass.Miner, ShipClass.Courier, ShipClass.Explorer, ShipClass.Destroyer })
            {
                Assert.That(freighter.Accel, Is.LessThan(CombatSpec.Of(c).Accel), "a freighter picks up speed faster than a " + c);
                Assert.That(freighter.Decel, Is.LessThan(CombatSpec.Of(c).Decel), "a freighter sheds speed faster than a " + c);
            }
            Assert.That(freighter.MaxSpeed / freighter.Accel, Is.GreaterThan(60), "a minute or more to reach full speed");
            Assert.That(ShipClassSpec.Of(ShipClass.Freighter).AccelG, Is.LessThan(Fleet.TorchAccelG));
            Assert.That(ShipClassSpec.Of(ShipClass.Freighter).AccelG, Is.LessThanOrEqualTo(ShipClassSpec.Of(ShipClass.Miner).AccelG));
        }

        [Test]
        public void AFreighterTradesFromOrbitAndNeverGoesDownToAWorld()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            var freighters = w.Fleet.Ships.Where(s => s.Spec.Class == ShipClass.Freighter && w.IsTrader(s)).ToList();
            Assert.That(freighters, Is.Not.Empty);
            Assert.That(freighters.Any(s => w.Fleet.Lands(s)), Is.False, "a truss freighter is built in space and never lands");
            var lander = w.Fleet.Ships.FirstOrDefault(s => w.IsTrader(s) && s.Spec.Class != ShipClass.Freighter);
            Assert.That(lander, Is.Not.Null);
            Assert.That(w.Fleet.Lands(lander), Is.True, "other merchants still go down through the rings");

            int trips = freighters.Sum(s => s.TripsCompleted);
            for (double jd = StartJd; jd < StartJd + 20; jd += 0.25)
            {
                w.Update(jd);
                foreach (var s in freighters)
                    Assert.That(s.Planetside || s.Phase == ShipPhase.Queued, Is.False, s.Name + " went down to a world");
            }
            Assert.That(freighters.Sum(s => s.TripsCompleted), Is.GreaterThan(trips), "the freighters stopped trading");
        }
    }
}
