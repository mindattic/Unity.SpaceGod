using System;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Hardpoints on the hull, facilities inside it (U.SPC-T470): a hull's internal space is limited and every facility
    /// takes a share of it sized to what it is, so a design is a trade-off and a hull too small for a facility cannot
    /// fit it; facility kinds are registered data (size, mass, bill, the hull categories that may fit them, the
    /// compartment they are, their effect); and the boarding interior is built from the hull's facilities.
    /// </summary>
    [TestFixture]
    public class FacilitiesTests
    {
        static readonly Faction Navy = new Faction("Navy", "NS", new Rgb(0, 0, 1)) { IsMilitary = true };

        static FacilityKind Plate => FacilityKinds.Of(FacilityKinds.ArmourPlating);

        /// <summary>A facility kind registered the way a later ticket adds one: no change to the code that fits it.</summary>
        static readonly FacilityKind Barracks = Register(new FacilityKind("test/barracks", "Test barracks", 4, 300, false,
            new[] { (Good.Alloys, 120.0), (Good.Machinery, 20.0) },
            new[] { HullCategory.Warship, HullCategory.Battleship, HullCategory.Carrier },
            compartment: CompartmentKind.Quarters,
            effect: (spec, loadout, sign) => spec.Marines += sign * 40));

        static readonly FacilityKind CarrierOnly = Register(new FacilityKind("test/carrier-only", "Carrier fitting", 1, 10, false,
            new[] { (Good.Alloys, 5.0) }, new[] { HullCategory.Carrier }));

        static FacilityKind Register(FacilityKind k)
        {
            FacilityKinds.Register(k);
            return k;
        }

        [Test]
        public void EveryHullsInteriorIsTheCompartmentsOfItsFacilities()
        {
            foreach (ShipClass c in Enum.GetValues(typeof(ShipClass)))
            {
                var layout = FacilityLayouts.For(c);
                var interior = Interior.For(c, 100, 10);
                var spaces = layout.Where(s => s.Def.Compartment != null).ToList();
                Assert.That(interior.Compartments.Select(x => x.Name), Is.EqualTo(spaces.Select(s => s.Name)), c.ToString());
                Assert.That(interior.Compartments.Select(x => x.Kind), Is.EqualTo(spaces.Select(s => s.Def.Compartment.Value)), c.ToString());
                Assert.That(interior.Compartments.Select(x => (x.Z, x.Y)), Is.EqualTo(spaces.Select(s => (s.Z, s.Y))), c.ToString());
                Assert.That(interior.Marines, Is.EqualTo(10).Within(1e-9));
            }
            // As the interiors were laid out before facilities: a cruiser's keel from bow to engineering, the bridge off midships.
            var cruiser = Interior.For(ShipClass.Cruiser, 100, 10);
            // Its marine habitat (U.SPC-T465) opens off the quarters.
            Assert.That(cruiser.Compartments.Select(x => x.Name), Is.EqualTo(new[] { "Bow", "Crew quarters", "Midships", "Engineering", "Bridge", "Marine barracks" }));
            Assert.That(cruiser.Of(CompartmentKind.Midships).Links, Is.EqualTo(new[] { 1, 3, 4 }));
            Assert.That(cruiser.Of(CompartmentKind.Bridge).Links, Is.EqualTo(new[] { 2 }));
            Assert.That(cruiser.Of(CompartmentKind.Barracks).Links, Is.EqualTo(new[] { 1 }));
            Assert.That(cruiser.Of(CompartmentKind.Barracks).Marines, Is.EqualTo(10), "marines quartered in their habitat");
            var platform = Interior.ForPlatform(30, 3);
            Assert.That(platform.Compartments.Select(x => x.Name), Is.EqualTo(new[] { "Gun deck", "Control", "Reactor", "Garrison barracks" }));
        }

        [Test]
        public void AHullsInternalSpaceIsLimitedAndEveryFacilityTakesItsShare()
        {
            var plan = new ConstructionPlan(HullFrames.ForClass(ShipClass.Destroyer));
            Assert.That(plan.FreeSpace, Is.EqualTo(3));
            Assert.That(plan.Loadout.InternalSpace, Is.EqualTo(FacilityLayouts.SpaceOf(FacilityLayouts.For(ShipClass.Destroyer)) + 3));
            for (int i = 0; i < 3; i++) Assert.That(plan.FitFacility(Plate), Is.EqualTo(ConstructionOutcome.Ok));
            Assert.That(plan.FitFacility(Plate), Is.EqualTo(ConstructionOutcome.NoRoom), "three sections, three plates");
            Assert.That(plan.FreeSpace, Is.EqualTo(0));
            Assert.That(plan.RemoveFacility(0), Is.EqualTo(ConstructionOutcome.WontFit), "the hull's own structure does not come out");
            int fitted = plan.Facilities.Count - 1;
            Assert.That(plan.RemoveFacility(fitted), Is.EqualTo(ConstructionOutcome.Ok));
            Assert.That(plan.FreeSpace, Is.EqualTo(1));
        }

        [Test]
        public void AHullTooSmallForAFacilityCannotFitItAndABigOneTradesSpaceForIt()
        {
            Assert.That(HullFrames.ForClass(ShipClass.Destroyer).CanCarry(Barracks), Is.False, "four sections will never go in a destroyer's three");
            Assert.That(new ConstructionPlan(HullFrames.ForClass(ShipClass.Destroyer)).FitFacility(Barracks), Is.EqualTo(ConstructionOutcome.NoRoom));
            Assert.That(new ConstructionPlan(HullFrames.ForClass(ShipClass.Freighter)).FitFacility(Barracks), Is.EqualTo(ConstructionOutcome.WontFit),
                "nor in a freighter, which may not carry it at all");
            Assert.That(new ConstructionPlan(HullFrames.ForClass(ShipClass.Battleship)).FitFacility(CarrierOnly), Is.EqualTo(ConstructionOutcome.WontFit));
            Assert.That(new ConstructionPlan(HullFrames.ForClass(ShipClass.Carrier)).FitFacility(CarrierOnly), Is.EqualTo(ConstructionOutcome.Ok));

            // A battleship that takes the barracks gives up four of its six sections of plate, racks and scanners.
            var plan = new ConstructionPlan(HullFrames.ForClass(ShipClass.Battleship));
            Assert.That(plan.FitFacility(Barracks), Is.EqualTo(ConstructionOutcome.Ok));
            Assert.That(plan.FitFacility(Plate), Is.EqualTo(ConstructionOutcome.Ok));
            Assert.That(plan.FitFacility(Plate), Is.EqualTo(ConstructionOutcome.Ok));
            Assert.That(plan.FitFacility(Plate), Is.EqualTo(ConstructionOutcome.NoRoom));
            var full = new ConstructionPlan(HullFrames.ForClass(ShipClass.Battleship));
            for (int i = 0; i < 3; i++) full.FitFacility(Plate);
            Assert.That(full.FitFacility(Barracks), Is.EqualTo(ConstructionOutcome.NoRoom), "plate in the way");
        }

        [Test]
        public void ARegisteredFacilityBringsItsBillMassEffectAndCompartment()
        {
            var plan = new ConstructionPlan(HullFrames.ForClass(ShipClass.Battleship));
            var stock = CombatSpec.Of(ShipClass.Battleship);
            double alloys = plan.Bill()[Good.Alloys];
            Assert.That(plan.FitFacility(Barracks), Is.EqualTo(ConstructionOutcome.Ok));
            Assert.That(plan.Bill()[Good.Alloys], Is.EqualTo(alloys + 120).Within(1e-9));
            Assert.That(plan.Bill()[Good.Machinery], Is.EqualTo(20).Within(1e-9));
            var spec = plan.Build();
            Assert.That(spec.Marines, Is.EqualTo(stock.Marines + 40), "its effect");
            Assert.That(spec.DryMassTonnes, Is.EqualTo(stock.DryMassTonnes + 300).Within(1e-6), "its mass");

            var ship = new Ship { Id = 1, Name = "BB", Spec = ShipClassSpec.Of(ShipClass.Battleship), Faction = Navy };
            Assert.That(plan.Apply(ship), Is.True);
            var interior = ship.Combat.Interior;
            var barracks = interior.Compartments.Single(c => c.Name == "Test barracks");
            var midships = interior.Of(CompartmentKind.Midships);
            Assert.That(barracks.Links, Is.EqualTo(new[] { midships.Index }), "a fitted space opens off midships");
            Assert.That(midships.Links, Does.Contain(barracks.Index));
            Assert.That(interior.Compartments.Count, Is.EqualTo(Interior.For(ShipClass.Battleship, 0, 0).Compartments.Count + 1));
            Assert.That(interior.Marines, Is.EqualTo(stock.Marines + 40).Within(1e-9));
        }

        [Test]
        public void AYardFitsAndStripsFacilitiesIntoTheFreeSpaceAndRebuildsTheInterior()
        {
            var economy = new Economy(new Diplomacy(), 0);
            var yard = economy.Add("Yard", body: 3, SettlementKind.Station, Navy, 0);
            yard.Facilities.Add(new Facility(Recipe.Of(IndustryKind.Shipyard), 1));
            foreach (var g in new[] { Good.Alloys, Good.Electronics, Good.Machinery, Good.Munitions }) yard.Stock[(int)g] = 1e6;
            yard.RefreshPrices();
            Navy.Treasury = 1e12;
            var ship = new Ship { Id = 2, Name = "CA", Spec = ShipClassSpec.Of(ShipClass.Cruiser), Faction = Navy, Combat = new ShipCombat(CombatSpec.Of(ShipClass.Cruiser), ShipClass.Cruiser) };
            int spaces = ship.Combat.Interior.Compartments.Count;
            double crew = ship.Combat.Interior.Crew;

            Assert.That(Outfitting.FitFacility(economy, ship, yard, Barracks, out var paid), Is.EqualTo(OutfitOutcome.Ok));
            Assert.That(paid.Materials, Is.GreaterThan(0));
            Assert.That(ship.Combat.Interior.Compartments.Count, Is.EqualTo(spaces + 1));
            Assert.That(ship.Combat.Interior.Crew, Is.EqualTo(crew).Within(1e-6), "the crew move in, none lost");
            Assert.That(ship.Combat.Spec.Marines, Is.EqualTo(CombatSpec.Of(ShipClass.Cruiser).Marines + 40));
            Assert.That(Outfitting.FitFacility(economy, ship, yard, Barracks, out _), Is.EqualTo(OutfitOutcome.NoRoom), "four sections were all the cruiser had");
            Assert.That(Outfitting.FitFacility(economy, ship, yard, Plate, out _), Is.EqualTo(OutfitOutcome.NoRoom));

            var l = Outfitting.LoadoutOf(ship);
            int index = l.Facilities.FindIndex(f => f.Kind == Barracks.Id);
            Assert.That(Outfitting.QuoteStrip(ship, yard, index).TradeIn, Is.GreaterThan(0));
            Assert.That(Outfitting.StripFacility(economy, ship, yard, index, out _), Is.EqualTo(OutfitOutcome.Ok));
            Assert.That(ship.Combat.Interior.Compartments.Count, Is.EqualTo(spaces));
            Assert.That(l.IsStock, Is.True);
            Assert.That(ship.Combat.Spec, Is.SameAs(CombatSpec.Of(ShipClass.Cruiser)));
            Assert.That(Outfitting.StripFacility(economy, ship, yard, 0, out _), Is.EqualTo(OutfitOutcome.WontFit), "never the hull's own bridge or bow");
        }

        [Test]
        public void ALoadoutWithFacilitiesSurvivesASave()
        {
            var plan = new ConstructionPlan(HullFrames.ForClass(ShipClass.Destroyer));
            plan.FitFacility(Plate);
            var ship = new Ship { Id = 3, Name = "DD", Spec = ShipClassSpec.Of(ShipClass.Destroyer), Faction = Navy };
            plan.Apply(ship);
            var copy = ObjectGraph.Clone(ship);
            Assert.That(copy.Loadout.Count(ModuleKind.ArmourPlating), Is.EqualTo(1));
            Assert.That(copy.Loadout.FreeSpace, Is.EqualTo(2));
            Assert.That(copy.Loadout.IsStock, Is.False);
            Assert.That(copy.Loadout.Build().Armor, Is.EqualTo(ship.Loadout.Build().Armor));
        }
    }
}
