using SolarSystem.Core;
using Xunit;

namespace SolarSystem.Core.Tests
{
    public class CargoHoldTests
    {
        [Fact]
        public void AnItemThatFitsInsideTheGridIsAccepted()
        {
            var hold = new CargoHold(4, 4);

            var item = hold.Add(Good.Electronics, 0, 0, false);

            Assert.NotNull(item);
            Assert.Single(hold.Items);
            Assert.Equal(Good.Electronics, item.Good);
        }

        [Fact]
        public void FitsReturnsFalseWhenTheFootprintRunsPastTheGridEdge()
        {
            var hold = new CargoHold(3, 3);

            // NickelIron is a 3x3 footprint; it only fits at the single (0, 0) origin of a 3x3 grid.
            Assert.True(hold.Fits(Good.NickelIron, 0, 0, false));
            Assert.False(hold.Fits(Good.NickelIron, 1, 0, false));
            Assert.False(hold.Fits(Good.NickelIron, 0, 1, false));
        }

        [Fact]
        public void OutOfBoundsPlacementIsRejectedAndNothingIsAdded()
        {
            var hold = new CargoHold(2, 2);

            var item = hold.Add(Good.NickelIron, 0, 0, false);

            Assert.Null(item);
            Assert.Empty(hold.Items);
        }

        [Fact]
        public void NegativeCoordinatesAreOutOfBounds()
        {
            var hold = new CargoHold(4, 4);

            Assert.False(hold.Fits(Good.Electronics, -1, 0, false));
            Assert.False(hold.Fits(Good.Electronics, 0, -1, false));
        }

        [Fact]
        public void RotatingAFootprintSwapsItsWidthAndHeight()
        {
            var footprint = CargoFootprint.Of(Good.HeliumThree);
            Assert.Equal(2, footprint.Width);
            Assert.Equal(1, footprint.Height);

            var hold = new CargoHold(1, 2);

            // Unrotated (2 wide, 1 tall) cannot stand in a 1-wide grid; rotated (1 wide, 2 tall) can.
            Assert.False(hold.Fits(Good.HeliumThree, 0, 0, false));
            Assert.True(hold.Fits(Good.HeliumThree, 0, 0, true));

            var item = hold.Add(Good.HeliumThree, 0, 0, true);
            Assert.NotNull(item);
            Assert.Equal(1, item.Width);
            Assert.Equal(2, item.Height);
        }

        [Fact]
        public void TwoItemsCannotOccupyOverlappingCells()
        {
            var hold = new CargoHold(4, 4);
            hold.Add(Good.Food, 0, 0, false); // 2x2 at (0,0)-(1,1)

            Assert.False(hold.Fits(Good.Electronics, 1, 1, false));
            var overlapping = hold.Add(Good.Electronics, 1, 1, false);
            Assert.Null(overlapping);

            // Just past the first crate, with no shared cells, still fits.
            Assert.True(hold.Fits(Good.Electronics, 2, 0, false));
        }

        [Fact]
        public void AdjacentNonOverlappingItemsBothFit()
        {
            var hold = new CargoHold(4, 4);
            var a = hold.Add(Good.Food, 0, 0, false); // 2x2
            var b = hold.Add(Good.Food, 2, 0, false); // 2x2, touching a's right edge

            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.Equal(2, hold.Items.Count);
        }

        [Fact]
        public void RemovingAnItemFreesItsCellsForReuse()
        {
            var hold = new CargoHold(2, 2);
            var item = hold.Add(Good.Food, 0, 0, false);
            Assert.NotNull(item);
            Assert.False(hold.Fits(Good.Food, 0, 0, false));

            Assert.True(hold.Remove(item));
            Assert.Empty(hold.Items);
            Assert.True(hold.Fits(Good.Food, 0, 0, false));
        }

        [Fact]
        public void RemovingAnItemNotInTheHoldReturnsFalse()
        {
            var hold = new CargoHold(2, 2);
            var neverAdded = new CargoItem(Good.Food, 0, 0, false);

            Assert.False(hold.Remove(neverAdded));
        }

        [Fact]
        public void AddFirstFitPlacesAtTheFirstOpenCellInRowMajorOrder()
        {
            var hold = new CargoHold(4, 2);
            hold.Add(Good.Electronics, 0, 0, false); // occupies (0,0)

            var placed = hold.AddFirstFit(Good.Electronics); // 1x1, should land at (1,0), not (0,1)

            Assert.NotNull(placed);
            Assert.Equal(1, placed.X);
            Assert.Equal(0, placed.Y);
        }

        [Fact]
        public void AddFirstFitRotatesWhenOnlyARotatedOrientationFits()
        {
            // A 1-wide, 3-tall hold: a 2x1 crate (HeliumThree) only fits rotated to 1x2.
            var hold = new CargoHold(1, 3);

            var placed = hold.AddFirstFit(Good.HeliumThree);

            Assert.NotNull(placed);
            Assert.True(placed.Rotated);
            Assert.Equal(1, placed.Width);
            Assert.Equal(2, placed.Height);
        }

        [Fact]
        public void AddFirstFitReturnsNullWhenTheHoldHasNoRoomLeft()
        {
            var hold = new CargoHold(1, 1);
            var first = hold.AddFirstFit(Good.Electronics);
            Assert.NotNull(first);

            var second = hold.AddFirstFit(Good.Electronics);

            Assert.Null(second);
            Assert.Single(hold.Items);
        }

        [Fact]
        public void HoldCapacityIsBoundedByItsGridNotAnUnlimitedList()
        {
            var hold = new CargoHold(2, 2);
            Assert.NotNull(hold.AddFirstFit(Good.Electronics));
            Assert.NotNull(hold.AddFirstFit(Good.Electronics));
            Assert.NotNull(hold.AddFirstFit(Good.Electronics));
            Assert.NotNull(hold.AddFirstFit(Good.Electronics));

            // All four 1x1 cells are now full.
            Assert.Null(hold.AddFirstFit(Good.Electronics));
            Assert.Null(hold.Add(Good.Electronics, 0, 0, false));
            Assert.Equal(4, hold.Items.Count);
        }

        [Fact]
        public void MoveRelocatesAnItemToAnOpenPositionAndRotation()
        {
            var hold = new CargoHold(4, 4);
            var item = hold.Add(Good.HeliumThree, 0, 0, false); // 2x1
            Assert.NotNull(item);

            bool moved = hold.Move(item, 2, 2, true);

            Assert.True(moved);
            Assert.Equal(2, item.X);
            Assert.Equal(2, item.Y);
            Assert.True(item.Rotated);
            // The old cells are free again.
            Assert.True(hold.Fits(Good.HeliumThree, 0, 0, false));
        }

        [Fact]
        public void MoveRejectsOverlapWithAnotherItemAndLeavesTheItemWhereItWas()
        {
            var hold = new CargoHold(4, 4);
            var a = hold.Add(Good.Food, 0, 0, false); // 2x2
            var b = hold.Add(Good.Food, 2, 2, false); // 2x2

            bool moved = hold.Move(b, 0, 0, false);

            Assert.False(moved);
            Assert.Equal(2, b.X);
            Assert.Equal(2, b.Y);
        }

        [Fact]
        public void MoveRejectsGoingOutOfBoundsAndLeavesTheItemWhereItWas()
        {
            var hold = new CargoHold(3, 3);
            var item = hold.Add(Good.MedicalSupplies, 0, 0, false); // 1x1

            bool moved = hold.Move(item, 5, 5, false);

            Assert.False(moved);
            Assert.Equal(0, item.X);
            Assert.Equal(0, item.Y);
        }

        [Fact]
        public void MoveOntoAnItemsOwnCurrentCellsSucceeds()
        {
            var hold = new CargoHold(3, 3);
            var item = hold.Add(Good.NickelIron, 0, 0, false); // 3x3, fills the whole grid

            // Rotating in place would overlap "itself" under a naive check; Move must exclude it.
            bool moved = hold.Move(item, 0, 0, true);

            Assert.True(moved);
            Assert.True(item.Rotated);
        }

        [Fact]
        public void CargoHoldSpecSizesAreSensibleAndClassSpecific()
        {
            var fighter = CargoHoldSpec.Of(ShipClass.Courier);
            var hauler = CargoHoldSpec.Of(ShipClass.Freighter);

            Assert.True(fighter.Width > 0 && fighter.Height > 0);
            Assert.True(hauler.Width * hauler.Height > fighter.Width * fighter.Height);
        }

        [Fact]
        public void ShipsPersonalHoldIsSizedOffItsOwnClass()
        {
            var courier = new Ship { Spec = ShipClassSpec.Of(ShipClass.Courier) };
            var freighter = new Ship { Spec = ShipClassSpec.Of(ShipClass.Freighter) };

            Assert.Equal(CargoHoldSpec.Of(ShipClass.Courier).Width, courier.PersonalHold.Width);
            Assert.Equal(CargoHoldSpec.Of(ShipClass.Freighter).Width, freighter.PersonalHold.Width);

            // Repeated access returns the same hold, not a fresh one each time (so placed cargo sticks).
            Assert.Same(courier.PersonalHold, courier.PersonalHold);
        }

        [Fact]
        public void TheAiEconomysSingleSlotCargoFieldsAreUntouchedByThePersonalHold()
        {
            var ship = new Ship { Spec = ShipClassSpec.Of(ShipClass.Freighter) };
            ship.HasCargo = true;
            ship.CargoGood = Good.Titanium;
            ship.CargoTonnes = 42;

            ship.PersonalHold.Add(Good.Electronics, 0, 0, false);

            Assert.True(ship.HasCargo);
            Assert.Equal(Good.Titanium, ship.CargoGood);
            Assert.Equal(42, ship.CargoTonnes);
        }
    }
}
