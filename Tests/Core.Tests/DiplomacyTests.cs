using SolarSystem.Core;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    /// <summary>
    /// Covers U.SPC-T29: <see cref="Diplomacy"/>'s relation bookkeeping and the two gates
    /// (<see cref="Diplomacy.Hostile"/>, <see cref="Diplomacy.CanDock"/>) that trade, combat and
    /// sieges all read. Today only <see cref="Diplomacy.CanDock"/> is exercised, indirectly, via
    /// MarketTradeTests.
    /// </summary>
    [TestFixture]
    public class DiplomacyTests
    {
        static Faction MakeFaction(string name, bool isPirate = false) =>
            new Faction(name, name.Substring(0, System.Math.Min(3, name.Length)).ToUpperInvariant(), default)
            {
                IsPirate = isPirate,
            };

        [Test]
        public void GetDefaultsToPeaceForFactionsWithNoRecordedRelation()
        {
            var diplomacy = new Diplomacy();
            var a = MakeFaction("Alpha");
            var b = MakeFaction("Beta");

            Assert.That(diplomacy.Get(a, b), Is.EqualTo(Relation.Peace));
        }

        [Test]
        public void GetIsSymmetricRegardlessOfArgumentOrder()
        {
            var diplomacy = new Diplomacy();
            var a = MakeFaction("Alpha");
            var b = MakeFaction("Beta");
            diplomacy.Set(a, b, Relation.War);

            Assert.That(diplomacy.Get(b, a), Is.EqualTo(diplomacy.Get(a, b)));
            Assert.That(diplomacy.Get(b, a), Is.EqualTo(Relation.War));
        }

        [Test]
        public void SetThenGetReturnsTheSameRelationInBothArgumentOrders()
        {
            var diplomacy = new Diplomacy();
            var a = MakeFaction("Alpha");
            var b = MakeFaction("Beta");

            diplomacy.Set(b, a, Relation.Ceasefire);

            Assert.That(diplomacy.Get(a, b), Is.EqualTo(Relation.Ceasefire));
            Assert.That(diplomacy.Get(b, a), Is.EqualTo(Relation.Ceasefire));
        }

        [Test]
        public void AFactionIsAlwaysAtPeaceWithItselfEvenAfterSettingWar()
        {
            var diplomacy = new Diplomacy();
            var a = MakeFaction("Alpha");

            Assert.That(diplomacy.Get(a, a), Is.EqualTo(Relation.Peace));
        }

        [Test]
        public void HostileIsFalseWhenEitherFactionIsNull()
        {
            var diplomacy = new Diplomacy();
            var a = MakeFaction("Alpha");

            Assert.That(diplomacy.Hostile(null, a), Is.False);
            Assert.That(diplomacy.Hostile(a, null), Is.False);
            Assert.That(diplomacy.Hostile(null, null), Is.False);
        }

        [Test]
        public void HostileIsFalseForAFactionAndItself()
        {
            var diplomacy = new Diplomacy();
            var a = MakeFaction("Alpha");

            Assert.That(diplomacy.Hostile(a, a), Is.False);
        }

        [Test]
        public void HostileIsTrueWheneverEitherSideIsAPirateRegardlessOfRecordedRelation()
        {
            var diplomacy = new Diplomacy();
            var pirate = MakeFaction("Raiders", isPirate: true);
            var lawful = MakeFaction("Lawful");
            diplomacy.Set(pirate, lawful, Relation.Peace);

            Assert.That(diplomacy.Hostile(pirate, lawful), Is.True);
            Assert.That(diplomacy.Hostile(lawful, pirate), Is.True);
        }

        [Test]
        public void HostileIsTrueOnlyForWarNotCeasefireOrPeace()
        {
            var diplomacy = new Diplomacy();
            var a = MakeFaction("Alpha");
            var b = MakeFaction("Beta");

            Assert.That(diplomacy.Hostile(a, b), Is.False); // default Peace

            diplomacy.Set(a, b, Relation.Ceasefire);
            Assert.That(diplomacy.Hostile(a, b), Is.False);

            diplomacy.Set(a, b, Relation.War);
            Assert.That(diplomacy.Hostile(a, b), Is.True);
        }

        [Test]
        public void CanDockLetsAPirateShipDockOnlyAtAPirateOwnedPort()
        {
            var diplomacy = new Diplomacy();
            var pirate = MakeFaction("Raiders", isPirate: true);
            var pirateHaven = MakeFaction("Havenport", isPirate: true);
            var lawfulPort = MakeFaction("Lawful");

            Assert.That(diplomacy.CanDock(pirate, pirateHaven), Is.True);
            Assert.That(diplomacy.CanDock(pirate, lawfulPort), Is.False);
        }

        [Test]
        public void CanDockBlocksAPirateShipAtALawfulPortEvenAtPeace()
        {
            var diplomacy = new Diplomacy();
            var pirate = MakeFaction("Raiders", isPirate: true);
            var lawfulPort = MakeFaction("Lawful");

            Assert.That(diplomacy.CanDock(pirate, lawfulPort), Is.False);
        }

        [Test]
        public void CanDockAllowsCeasefireButNotWarAtALawfulPort()
        {
            var diplomacy = new Diplomacy();
            var ship = MakeFaction("Alpha");
            var port = MakeFaction("Beta");

            diplomacy.Set(ship, port, Relation.Ceasefire);
            Assert.That(diplomacy.CanDock(ship, port), Is.True);

            diplomacy.Set(ship, port, Relation.War);
            Assert.That(diplomacy.CanDock(ship, port), Is.False);
        }
    }
}
