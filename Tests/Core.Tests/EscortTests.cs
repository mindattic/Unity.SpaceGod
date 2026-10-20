using System;
using System.Linq;
using UnitySpace.Core;
using NUnit.Framework;

namespace UnitySpace.Core.Tests
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
    
        [Test]
        public void AWingThatHasOutlivedItsMerchantIsPaidOffAtItsNextPortAndDrawsNoMoreWages()
        {
            var (w, hauler) = RichHauler(1e9);
            w.Escorts.Update(StartJd + 1);
            var wing = w.Fleet.Ships.Where(s => s.Leader == hauler && !s.Destroyed).ToList();
            Assert.That(wing.Count, Is.GreaterThanOrEqualTo(2));

            // The merchant is lost: its wing goes on under its strongest, as any group's does (U.SPC-T285).
            hauler.Destroyed = true;
            w.War.HandOverCommand(hauler);
            foreach (var s in wing) s.Phase = ShipPhase.Parked;
            w.Escorts.Update(StartJd + 2);
            Assert.That(wing.All(s => s.Destroyed && s.GateDeparted), Is.True, "a wing with no merchant to guard was not paid off in port");

            double treasury = hauler.Faction.Treasury;
            w.Escorts.Update(StartJd + 3);
            Assert.That(hauler.Faction.Treasury, Is.EqualTo(treasury), "a paid-off wing still drew wages");
        }

        [Test]
        public void AWingStillWithItsMerchantIsNotPaidOff()
        {
            var (w, hauler) = RichHauler(1e9);
            w.Escorts.Update(StartJd + 1);
            w.Escorts.Update(StartJd + 2);
            Assert.That(w.Fleet.Ships.Count(s => s.Leader == hauler && !s.Destroyed), Is.GreaterThanOrEqualTo(2));
        }

        /// <summary>Runs the whole galaxy forward in half-day steps until <paramref name="done"/> or <paramref name="days"/> have gone by.</summary>
        static double RunUntil(Galaxy g, double fromJd, double days, Func<bool> done)
        {
            double jd = fromJd;
            while (jd < fromJd + days && !done())
            {
                jd += 0.5;
                g.Update(jd);
            }
            return jd;
        }

        /// <summary>A hired wing that crosses a gate with its merchant is still watched for its merchant going away at
        /// the far end, instead of flying on untracked for good once the gate that carried it leaves its hiring system
        /// behind (U.SPC-T301, see <see cref="MerchantEscorts.MarkHired"/>).</summary>
        [Test]
        public void AHiredWingThatCrossesAGateWithItsMerchantIsStillPaidOffWhenItIsGone()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var ac = g.Find("Alpha Centauri");
            var w = sol.World;
            var flag = new Faction("Hauliers Guild", "HG", new Rgb(0.5f, 0.5f, 0.5f)) { Treasury = 1e9 };
            var hauler = w.Fleet.AddShip("Long Haul", ShipClass.Freighter, flag, ShipRole.Trader);
            w.Fleet.Station(hauler, w.Catalog["Earth"].Index, StartJd);
            hauler.HasCargo = true;
            hauler.CargoGood = Good.Electronics;
            hauler.CargoTonnes = 1e6 / Math.Max(1, GoodInfo.Of(Good.Electronics).BasePrice) * 100;

            w.Escorts.Update(StartJd + 1);
            var wing = w.Fleet.Ships.Where(s => s.Leader == hauler && !s.Destroyed).ToList();
            Assert.That(wing.Count, Is.GreaterThanOrEqualTo(2), "a rich hauler took on no wing");
            Assert.That(wing.All(s => w.Escorts.IsHired(s)), Is.True, "the wing was not tracked as hired before crossing");
            var wingNames = wing.Select(s => s.Name).OrderBy(n => n).ToArray();

            var v = g.Traffic.Send(hauler, sol, ac, VoyagePurpose.Trade, StartJd + 2);
            Assert.That(v, Is.Not.Null, "the hauler could not set out for Alpha Centauri");

            double jd = RunUntil(g, StartJd + 2, 120, () => ac.World.Fleet.Ships.Any(s => s.Name == hauler.Name && !s.Destroyed));
            var arrived = ac.World.Fleet.Ships.FirstOrDefault(s => s.Name == hauler.Name && !s.Destroyed);
            Assert.That(arrived, Is.Not.Null, "the hauler never reached Alpha Centauri");
            var arrivedWing = ac.World.Fleet.Ships.Where(s => s.Leader == arrived && !s.Destroyed).ToList();
            Assert.That(arrivedWing.Select(s => s.Name).OrderBy(n => n), Is.EquivalentTo(wingNames), "the wing did not follow its merchant through the gate");
            Assert.That(arrivedWing.All(s => ac.World.Escorts.IsHired(s)), Is.True, "the wing lost its hired tracking crossing the gate");
            Assert.That(w.Escorts.IsHired(wing[0]), Is.False, "the wing was still tracked hired in the system it left");

            // The merchant is lost at the far end: its wing's tracking there still pays it off once it has nobody to guard.
            arrived.Destroyed = true;
            ac.World.War.HandOverCommand(arrived);
            foreach (var s in arrivedWing) s.Phase = ShipPhase.Parked;
            ac.World.Escorts.Update(jd + 1);
            Assert.That(arrivedWing.All(s => s.Destroyed && s.GateDeparted), Is.True, "a wing with no merchant to guard was not paid off after crossing a gate");
        }
    }
}
