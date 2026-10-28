using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// U.SPC-T523: a belt works' raw-material convoys are real freighters. Each convoy is carried in the hold of one of the
    /// works' own hulls, flies with its escort share as real warships, and is taken only by raiders who board the hull,
    /// never by a dice roll on how many raiders are about.
    /// </summary>
    [TestFixture]
    public class WorksFreighterTests
    {
        const double StartJd = 2461324.0;

        static (BeltMining mining, MiningOperation op, MaterialWarehouse depot, Ship freighter) Works(double pressure)
        {
            var mining = new BeltMining(7) { PiratePressure = () => pressure };
            var owner = new Faction("Belt Consortium", "BCS", default);
            var depot = mining.AddWarehouse("Valhalla Depot", owner, 2);
            var op = mining.AddOperation("Ceres Belt Works", owner, 1, depot);
            var freighter = new Ship
            {
                Name = "BCS Ore Wain", Faction = owner, Role = ShipRole.Hauler, Phase = ShipPhase.Parked,
                Orbit = new ParkingOrbit { Body = 1 }, CapacityTonnes = 20000,
            };
            op.Freighters = new List<Ship> { freighter };
            op.ConvoyFreighters = 1;
            return (mining, op, depot, freighter);
        }

        /// <summary>Runs the works a day at a time until its freighter sails, and returns the convoy it carries.</summary>
        static MaterialConvoy Sail(BeltMining mining, Ship freighter, ref double jd)
        {
            for (int d = 0; d < 400; d++)
            {
                mining.Update(jd);
                var c = mining.ConvoyOf(freighter);
                if (c != null) return c;
                jd += 1;
            }
            Assert.Fail("the freighter never sailed");
            return null;
        }

        [Test]
        public void TheLoadSailsInTheFreightersHoldAndIsDeliveredWhenTheHullGetsIn()
        {
            var (mining, op, depot, f) = Works(0);
            double jd = StartJd;
            var c = Sail(mining, f, ref jd);

            Assert.That(c.Freighter, Is.SameAs(f));
            Assert.That(c.Tonnes, Is.GreaterThan(0).And.LessThanOrEqualTo(f.CapacityTonnes + 1e-6), "one hold's worth at most");
            Assert.That(f.HasCargo, Is.True);
            Assert.That(f.CargoTonnes, Is.EqualTo(c.Tonnes).Within(1e-6));
            Assert.That(mining.FreighterNext(f, 1), Is.EqualTo(depot.Body), "laden, it makes for the depot");

            // Long past the timetable's estimate, it is not in until the hull is.
            mining.Update(c.ArriveJd + 5);
            Assert.That(c.State, Is.EqualTo(ConvoyState.EnRoute));
            Assert.That(depot.StoredTonnes, Is.EqualTo(0));

            f.Orbit = new ParkingOrbit { Body = depot.Body };
            Assert.That(mining.FreighterArrived(f, c.ArriveJd + 6), Is.True);
            Assert.That(c.State, Is.EqualTo(ConvoyState.Delivered));
            Assert.That(depot.StoredTonnes, Is.EqualTo(c.Tonnes).Within(1e-6));
            Assert.That(f.HasCargo, Is.False);
            Assert.That(mining.FreighterNext(f, depot.Body), Is.EqualTo(op.RefineryBody), "empty, it goes home");
        }

        [Test]
        public void RaidersAboutNeverTakeAFreighterTheyDoNotBoard()
        {
            var (mining, _, _, f) = Works(1);
            double jd = StartJd;
            var c = Sail(mining, f, ref jd);
            mining.Update(jd + 2000);
            Assert.That(c.State, Is.EqualTo(ConvoyState.EnRoute), "no dice roll on pirate pressure robs a real hull");
            Assert.That(mining.ConvoysStolen, Is.EqualTo(0));
        }

        [Test]
        public void ABoardedFreighterIsTheRaidersAndOneBlownApartIsLostWithItsLoad()
        {
            var raiders = new Faction("Black Banner", "", default);

            var (mining, op, _, f) = Works(0);
            double jd = StartJd;
            var c = Sail(mining, f, ref jd);
            // Looted and left adrift in a fight.
            f.HasCargo = false;
            f.Derelict = true;
            f.DerelictBy = raiders;
            mining.Update(jd + 1);
            Assert.That(c.State, Is.EqualTo(ConvoyState.Stolen));
            Assert.That(c.TakenBy, Is.SameAs(raiders));
            Assert.That(mining.Plundered.Sum(), Is.EqualTo(c.Tonnes).Within(1e-6));
            Assert.That(op.Freighters, Is.Empty, "the works has lost its hull");

            var (mining2, op2, _, f2) = Works(0);
            jd = StartJd;
            var c2 = Sail(mining2, f2, ref jd);
            f2.Destroyed = true;
            mining2.Update(jd + 1);
            Assert.That(c2.State, Is.EqualTo(ConvoyState.Lost));
            Assert.That(mining2.ConvoysLost, Is.EqualTo(1));
            Assert.That(mining2.Plundered.Sum(), Is.EqualTo(0), "nobody the richer");
            Assert.That(op2.FreightersLost, Is.EqualTo(1));
        }

        [Test]
        public void SolsWorksSailsItsConvoysInEscortedFreightersOfItsOwn()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            double jd = StartJd;
            eph.Update(jd);
            var w = World.CreateDefault(eph, jd);
            var op = w.Mining.Operations.Single();
            Assert.That(op.Freighters, Has.Count.EqualTo(2));
            foreach (var f in op.Freighters)
            {
                Assert.That(f.Faction, Is.SameAs(op.Owner));
                Assert.That(f.Role, Is.EqualTo(ShipRole.Hauler));
                Assert.That(f.Spec.Class, Is.EqualTo(ShipClass.Freighter));
                Assert.That(w.Fleet.Ships.Count(s => s.Leader == f && !s.Destroyed), Is.EqualTo(op.EscortsPerFreighter), "its escort share flies with it");
            }

            for (int d = 1; d <= 120; d++)
            {
                eph.Update(jd + d);
                w.Update(jd + d);
            }
            Assert.That(w.Mining.Convoys, Is.Not.Empty);
            Assert.That(w.Mining.Convoys.All(c => c.Freighter != null), "every convoy is carried by a real hull");
        }
    }
}
