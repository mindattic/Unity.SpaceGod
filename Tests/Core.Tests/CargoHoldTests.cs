using UnitySpace.Core;
using NUnit.Framework;

namespace UnitySpace.Core.Tests
{
    [TestFixture]
    public class CargoHoldTests
    {
        [Test]
        public void AnItemThatFitsInsideTheGridIsAccepted()
        {
            var hold = new CargoHold(4, 4);

            var item = hold.Add(Good.Electronics, 0, 0, false);

            Assert.That(item, Is.Not.Null);
            Assert.That(hold.Items, Has.Exactly(1).Items);
            Assert.That(item.Good, Is.EqualTo(Good.Electronics));
        }

        [Test]
        public void FitsReturnsFalseWhenTheFootprintRunsPastTheGridEdge()
        {
            var hold = new CargoHold(3, 3);

            // NickelIron is a 3x3 footprint; it only fits at the single (0, 0) origin of a 3x3 grid.
            Assert.That(hold.Fits(Good.NickelIron, 0, 0, false), Is.True);
            Assert.That(hold.Fits(Good.NickelIron, 1, 0, false), Is.False);
            Assert.That(hold.Fits(Good.NickelIron, 0, 1, false), Is.False);
        }

        [Test]
        public void OutOfBoundsPlacementIsRejectedAndNothingIsAdded()
        {
            var hold = new CargoHold(2, 2);

            var item = hold.Add(Good.NickelIron, 0, 0, false);

            Assert.That(item, Is.Null);
            Assert.That(hold.Items, Is.Empty);
        }

        [Test]
        public void NegativeCoordinatesAreOutOfBounds()
        {
            var hold = new CargoHold(4, 4);

            Assert.That(hold.Fits(Good.Electronics, -1, 0, false), Is.False);
            Assert.That(hold.Fits(Good.Electronics, 0, -1, false), Is.False);
        }

        [Test]
        public void RotatingAFootprintSwapsItsWidthAndHeight()
        {
            var footprint = CargoFootprint.Of(Good.HeliumThree);
            Assert.That(footprint.Width, Is.EqualTo(2));
            Assert.That(footprint.Height, Is.EqualTo(1));

            var hold = new CargoHold(1, 2);

            // Unrotated (2 wide, 1 tall) cannot stand in a 1-wide grid; rotated (1 wide, 2 tall) can.
            Assert.That(hold.Fits(Good.HeliumThree, 0, 0, false), Is.False);
            Assert.That(hold.Fits(Good.HeliumThree, 0, 0, true), Is.True);

            var item = hold.Add(Good.HeliumThree, 0, 0, true);
            Assert.That(item, Is.Not.Null);
            Assert.That(item.Width, Is.EqualTo(1));
            Assert.That(item.Height, Is.EqualTo(2));
        }

        [Test]
        public void TwoItemsCannotOccupyOverlappingCells()
        {
            var hold = new CargoHold(4, 4);
            hold.Add(Good.Food, 0, 0, false); // 2x2 at (0,0)-(1,1)

            Assert.That(hold.Fits(Good.Electronics, 1, 1, false), Is.False);
            var overlapping = hold.Add(Good.Electronics, 1, 1, false);
            Assert.That(overlapping, Is.Null);

            // Just past the first crate, with no shared cells, still fits.
            Assert.That(hold.Fits(Good.Electronics, 2, 0, false), Is.True);
        }

        [Test]
        public void AdjacentNonOverlappingItemsBothFit()
        {
            var hold = new CargoHold(4, 4);
            var a = hold.Add(Good.Food, 0, 0, false); // 2x2
            var b = hold.Add(Good.Food, 2, 0, false); // 2x2, touching a's right edge

            Assert.That(a, Is.Not.Null);
            Assert.That(b, Is.Not.Null);
            Assert.That(hold.Items.Count, Is.EqualTo(2));
        }

        [Test]
        public void RemovingAnItemFreesItsCellsForReuse()
        {
            var hold = new CargoHold(2, 2);
            var item = hold.Add(Good.Food, 0, 0, false);
            Assert.That(item, Is.Not.Null);
            Assert.That(hold.Fits(Good.Food, 0, 0, false), Is.False);

            Assert.That(hold.Remove(item), Is.True);
            Assert.That(hold.Items, Is.Empty);
            Assert.That(hold.Fits(Good.Food, 0, 0, false), Is.True);
        }

        [Test]
        public void RemovingAnItemNotInTheHoldReturnsFalse()
        {
            var hold = new CargoHold(2, 2);
            var neverAdded = new CargoItem(Good.Food, 0, 0, false);

            Assert.That(hold.Remove(neverAdded), Is.False);
        }

        [Test]
        public void AddFirstFitPlacesAtTheFirstOpenCellInRowMajorOrder()
        {
            var hold = new CargoHold(4, 2);
            hold.Add(Good.Electronics, 0, 0, false); // occupies (0,0)

            var placed = hold.AddFirstFit(Good.Electronics); // 1x1, should land at (1,0), not (0,1)

            Assert.That(placed, Is.Not.Null);
            Assert.That(placed.X, Is.EqualTo(1));
            Assert.That(placed.Y, Is.EqualTo(0));
        }

        [Test]
        public void AddFirstFitRotatesWhenOnlyARotatedOrientationFits()
        {
            // A 1-wide, 3-tall hold: a 2x1 crate (HeliumThree) only fits rotated to 1x2.
            var hold = new CargoHold(1, 3);

            var placed = hold.AddFirstFit(Good.HeliumThree);

            Assert.That(placed, Is.Not.Null);
            Assert.That(placed.Rotated, Is.True);
            Assert.That(placed.Width, Is.EqualTo(1));
            Assert.That(placed.Height, Is.EqualTo(2));
        }

        [Test]
        public void AddFirstFitReturnsNullWhenTheHoldHasNoRoomLeft()
        {
            var hold = new CargoHold(1, 1);
            var first = hold.AddFirstFit(Good.Electronics);
            Assert.That(first, Is.Not.Null);

            var second = hold.AddFirstFit(Good.Electronics);

            Assert.That(second, Is.Null);
            Assert.That(hold.Items, Has.Exactly(1).Items);
        }

        [Test]
        public void HoldCapacityIsBoundedByItsGridNotAnUnlimitedList()
        {
            var hold = new CargoHold(2, 2);
            Assert.That(hold.AddFirstFit(Good.Electronics), Is.Not.Null);
            Assert.That(hold.AddFirstFit(Good.Electronics), Is.Not.Null);
            Assert.That(hold.AddFirstFit(Good.Electronics), Is.Not.Null);
            Assert.That(hold.AddFirstFit(Good.Electronics), Is.Not.Null);

            // All four 1x1 cells are now full.
            Assert.That(hold.AddFirstFit(Good.Electronics), Is.Null);
            Assert.That(hold.Add(Good.Electronics, 0, 0, false), Is.Null);
            Assert.That(hold.Items.Count, Is.EqualTo(4));
        }

        [Test]
        public void MoveRelocatesAnItemToAnOpenPositionAndRotation()
        {
            var hold = new CargoHold(4, 4);
            var item = hold.Add(Good.HeliumThree, 0, 0, false); // 2x1
            Assert.That(item, Is.Not.Null);

            bool moved = hold.Move(item, 2, 2, true);

            Assert.That(moved, Is.True);
            Assert.That(item.X, Is.EqualTo(2));
            Assert.That(item.Y, Is.EqualTo(2));
            Assert.That(item.Rotated, Is.True);
            // The old cells are free again.
            Assert.That(hold.Fits(Good.HeliumThree, 0, 0, false), Is.True);
        }

        [Test]
        public void MoveRejectsOverlapWithAnotherItemAndLeavesTheItemWhereItWas()
        {
            var hold = new CargoHold(4, 4);
            var a = hold.Add(Good.Food, 0, 0, false); // 2x2
            var b = hold.Add(Good.Food, 2, 2, false); // 2x2

            bool moved = hold.Move(b, 0, 0, false);

            Assert.That(moved, Is.False);
            Assert.That(b.X, Is.EqualTo(2));
            Assert.That(b.Y, Is.EqualTo(2));
        }

        [Test]
        public void MoveRejectsGoingOutOfBoundsAndLeavesTheItemWhereItWas()
        {
            var hold = new CargoHold(3, 3);
            var item = hold.Add(Good.MedicalSupplies, 0, 0, false); // 1x1

            bool moved = hold.Move(item, 5, 5, false);

            Assert.That(moved, Is.False);
            Assert.That(item.X, Is.EqualTo(0));
            Assert.That(item.Y, Is.EqualTo(0));
        }

        [Test]
        public void MoveOntoAnItemsOwnCurrentCellsSucceeds()
        {
            var hold = new CargoHold(3, 3);
            var item = hold.Add(Good.NickelIron, 0, 0, false); // 3x3, fills the whole grid

            // Rotating in place would overlap "itself" under a naive check; Move must exclude it.
            bool moved = hold.Move(item, 0, 0, true);

            Assert.That(moved, Is.True);
            Assert.That(item.Rotated, Is.True);
        }

        [Test]
        public void CargoHoldSpecSizesAreSensibleAndClassSpecific()
        {
            var fighter = CargoHoldSpec.Of(ShipClass.Courier);
            var hauler = CargoHoldSpec.Of(ShipClass.Freighter);

            Assert.That(fighter.Width > 0 && fighter.Height > 0, Is.True);
            Assert.That(hauler.Width * hauler.Height > fighter.Width * fighter.Height, Is.True);
        }

        [Test]
        public void ShipsPersonalHoldIsSizedOffItsOwnClass()
        {
            var courier = new Ship { Spec = ShipClassSpec.Of(ShipClass.Courier) };
            var freighter = new Ship { Spec = ShipClassSpec.Of(ShipClass.Freighter) };

            Assert.That(courier.PersonalHold.Width, Is.EqualTo(CargoHoldSpec.Of(ShipClass.Courier).Width));
            Assert.That(freighter.PersonalHold.Width, Is.EqualTo(CargoHoldSpec.Of(ShipClass.Freighter).Width));

            // Repeated access returns the same hold, not a fresh one each time (so placed cargo sticks).
            var first = courier.PersonalHold;
            Assert.That(courier.PersonalHold, Is.SameAs(first));
        }

        [Test]
        public void TheAiEconomysSingleSlotCargoFieldsAreUntouchedByThePersonalHold()
        {
            var ship = new Ship { Spec = ShipClassSpec.Of(ShipClass.Freighter) };
            ship.HasCargo = true;
            ship.CargoGood = Good.Titanium;
            ship.CargoTonnes = 42;

            ship.PersonalHold.Add(Good.Electronics, 0, 0, false);

            Assert.That(ship.HasCargo, Is.True);
            Assert.That(ship.CargoGood, Is.EqualTo(Good.Titanium));
            Assert.That(ship.CargoTonnes, Is.EqualTo(42));
        }
    }
}
