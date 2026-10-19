using System;
using System.Linq;
using SolarSystem.Core;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    /// <summary>
    /// Fighter escorts for the merchant fleet (U.SPC-T275): a freighter in port takes on a wing sized by its cargo and
    /// the danger of its waters, usually a heavy fighter and two light ones on a rich run into trouble, paid for by its
    /// owners, and only one they can afford.
    /// </summary>
    [TestFixture]
    public class EscortTests
    {
        const double StartJd = 2461324.0;

        [Test]
        public void TheWingGrowsWithTheCargoAndTheDanger()
        {
            Assert.That(MerchantEscorts.Wanted(5e4, 0), Is.EqualTo(0), "a cheap run through quiet space took on escorts");
            Assert.That(MerchantEscorts.Wanted(3e6, 0.5), Is.EqualTo(3), "a rich cargo into a war zone went without a full wing");
            int last = 0;
            foreach (var value in new[] { 1e5, 1e6, 1e7 })
                foreach (var danger in new[] { 0.0, 0.1, 0.3 })
                {
                    int n = MerchantEscorts.Wanted(value, danger);
                    Assert.That(n, Is.InRange(0, 3));
                    Assert.That(MerchantEscorts.Wanted(value * 10, danger), Is.GreaterThanOrEqualTo(n), "a richer cargo wanted fewer escorts");
                    Assert.That(MerchantEscorts.Wanted(value, danger + 0.2), Is.GreaterThanOrEqualTo(n), "more danger wanted fewer escorts");
                    last = n;
                }
            Assert.That(last, Is.EqualTo(3));
        }

        [Test]
        public void AFullWingIsAHeavyFighterLeadingTwoLightOnes()
        {
            var rng = new Random(1);
            for (int i = 0; i < 20; i++)
            {
                var wing = MerchantEscorts.Wing(3, rng);
                Assert.That(wing.Length, Is.EqualTo(3));
                Assert.That(wing[0], Is.EqualTo(ShipClass.Breacher));
                Assert.That(wing.Skip(1).All(c => c == ShipClass.Fighter || c == ShipClass.Courier), Is.True);
            }
            Assert.That(MerchantEscorts.Wing(1, rng).Single(), Is.Not.EqualTo(ShipClass.Breacher));
        }

        /// <summary>A world with a freighter carrying a fortune parked at Earth. (Building the world has already run its
        /// first update, so the escorts next look over the merchants a little later: the tests look a day on.)</summary>
        static (World w, Ship hauler) RichHauler(double treasury)
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            var flag = new Faction("Hauliers Guild", "HG", new Rgb(0.5f, 0.5f, 0.5f)) { Treasury = treasury };
            var hauler = w.Fleet.AddShip("Long Haul", ShipClass.Freighter, flag, ShipRole.Trader);
            w.Fleet.Station(hauler, w.Catalog["Earth"].Index, StartJd + 30);
            hauler.HasCargo = true;
            hauler.CargoGood = Good.Electronics;
            hauler.CargoTonnes = 1e6 / Math.Max(1, GoodInfo.Of(Good.Electronics).BasePrice) * 100;
            return (w, hauler);
        }

        [Test]
        public void ARichFreighterInPortTakesOnAWingThatFliesWithItAndIsPaidFor()
        {
            var (w, hauler) = RichHauler(1e9);
            double before = hauler.Faction.Treasury;
            w.Escorts.Update(StartJd + 1);
            var wing = w.Fleet.Ships.Where(s => s.Leader == hauler && !s.Destroyed).ToList();
            Assert.That(wing.Count, Is.GreaterThanOrEqualTo(2), "a freighter carrying a fortune took on no wing");
            Assert.That(wing.All(s => s.IsEscort && s.Faction == hauler.Faction && s.Role == ShipRole.Escort), Is.True);
            Assert.That(wing.All(s => s.Spec.Class == ShipClass.Fighter || s.Spec.Class == ShipClass.Courier || s.Spec.Class == ShipClass.Breacher), Is.True);
            Assert.That(hauler.Faction.Treasury, Is.LessThan(before), "the wing came free");

            // Once it has its wing it takes on no more, and goes on paying the wing's wages.
            int count = wing.Count;
            double paid = hauler.Faction.Treasury;
            w.Escorts.Update(StartJd + 2);
            Assert.That(w.Fleet.Ships.Count(s => s.Leader == hauler && !s.Destroyed), Is.EqualTo(count));
            Assert.That(hauler.Faction.Treasury, Is.LessThan(paid), "the wing flew without wages");
        }

        [Test]
        public void OwnersWhoCannotAffordAWingGoWithout()
        {
            var (w, hauler) = RichHauler(1000);
            w.Escorts.Update(StartJd + 1);
            Assert.That(w.Fleet.Ships.Any(s => s.Leader == hauler), Is.False, "a freighter whose owners are broke took on a wing");
        }
    }
}
