using SolarSystem.Core;
using Xunit;

namespace SolarSystem.Core.Tests
{
    /// <summary>
    /// Covers USS-T29: <see cref="Diplomacy"/>'s relation bookkeeping and the two gates
    /// (<see cref="Diplomacy.Hostile"/>, <see cref="Diplomacy.CanDock"/>) that trade, combat and
    /// sieges all read. Today only <see cref="Diplomacy.CanDock"/> is exercised, indirectly, via
    /// MarketTradeTests.
    /// </summary>
    public class DiplomacyTests
    {
        static Faction MakeFaction(string name, bool isPirate = false) =>
            new Faction(name, name.Substring(0, System.Math.Min(3, name.Length)).ToUpperInvariant(), default)
            {
                IsPirate = isPirate,
            };

        [Fact]
        public void GetDefaultsToPeaceForFactionsWithNoRecordedRelation()
        {
            var diplomacy = new Diplomacy();
            var a = MakeFaction("Alpha");
            var b = MakeFaction("Beta");

            Assert.Equal(Relation.Peace, diplomacy.Get(a, b));
        }

        [Fact]
        public void GetIsSymmetricRegardlessOfArgumentOrder()
        {
            var diplomacy = new Diplomacy();
            var a = MakeFaction("Alpha");
            var b = MakeFaction("Beta");
            diplomacy.Set(a, b, Relation.War);

            Assert.Equal(diplomacy.Get(a, b), diplomacy.Get(b, a));
            Assert.Equal(Relation.War, diplomacy.Get(b, a));
        }

        [Fact]
        public void SetThenGetReturnsTheSameRelationInBothArgumentOrders()
        {
            var diplomacy = new Diplomacy();
            var a = MakeFaction("Alpha");
            var b = MakeFaction("Beta");

            diplomacy.Set(b, a, Relation.Ceasefire);

            Assert.Equal(Relation.Ceasefire, diplomacy.Get(a, b));
            Assert.Equal(Relation.Ceasefire, diplomacy.Get(b, a));
        }

        [Fact]
        public void AFactionIsAlwaysAtPeaceWithItselfEvenAfterSettingWar()
        {
            var diplomacy = new Diplomacy();
            var a = MakeFaction("Alpha");

            Assert.Equal(Relation.Peace, diplomacy.Get(a, a));
        }

        [Fact]
        public void HostileIsFalseWhenEitherFactionIsNull()
        {
            var diplomacy = new Diplomacy();
            var a = MakeFaction("Alpha");

            Assert.False(diplomacy.Hostile(null, a));
            Assert.False(diplomacy.Hostile(a, null));
            Assert.False(diplomacy.Hostile(null, null));
        }

        [Fact]
        public void HostileIsFalseForAFactionAndItself()
        {
            var diplomacy = new Diplomacy();
            var a = MakeFaction("Alpha");

            Assert.False(diplomacy.Hostile(a, a));
        }

        [Fact]
        public void HostileIsTrueWheneverEitherSideIsAPirateRegardlessOfRecordedRelation()
        {
            var diplomacy = new Diplomacy();
            var pirate = MakeFaction("Raiders", isPirate: true);
            var lawful = MakeFaction("Lawful");
            diplomacy.Set(pirate, lawful, Relation.Peace);

            Assert.True(diplomacy.Hostile(pirate, lawful));
            Assert.True(diplomacy.Hostile(lawful, pirate));
        }

        [Fact]
        public void HostileIsTrueOnlyForWarNotCeasefireOrPeace()
        {
            var diplomacy = new Diplomacy();
            var a = MakeFaction("Alpha");
            var b = MakeFaction("Beta");

            Assert.False(diplomacy.Hostile(a, b)); // default Peace

            diplomacy.Set(a, b, Relation.Ceasefire);
            Assert.False(diplomacy.Hostile(a, b));

            diplomacy.Set(a, b, Relation.War);
            Assert.True(diplomacy.Hostile(a, b));
        }

        [Fact]
        public void CanDockLetsAPirateShipDockOnlyAtAPirateOwnedPort()
        {
            var diplomacy = new Diplomacy();
            var pirate = MakeFaction("Raiders", isPirate: true);
            var pirateHaven = MakeFaction("Havenport", isPirate: true);
            var lawfulPort = MakeFaction("Lawful");

            Assert.True(diplomacy.CanDock(pirate, pirateHaven));
            Assert.False(diplomacy.CanDock(pirate, lawfulPort));
        }

        [Fact]
        public void CanDockBlocksAPirateShipAtALawfulPortEvenAtPeace()
        {
            var diplomacy = new Diplomacy();
            var pirate = MakeFaction("Raiders", isPirate: true);
            var lawfulPort = MakeFaction("Lawful");

            Assert.False(diplomacy.CanDock(pirate, lawfulPort));
        }

        [Fact]
        public void CanDockAllowsCeasefireButNotWarAtALawfulPort()
        {
            var diplomacy = new Diplomacy();
            var ship = MakeFaction("Alpha");
            var port = MakeFaction("Beta");

            diplomacy.Set(ship, port, Relation.Ceasefire);
            Assert.True(diplomacy.CanDock(ship, port));

            diplomacy.Set(ship, port, Relation.War);
            Assert.False(diplomacy.CanDock(ship, port));
        }
    }
}
