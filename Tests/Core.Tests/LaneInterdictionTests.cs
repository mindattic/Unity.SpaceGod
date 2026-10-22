using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The player riding a lane through a raider's lurk (U.SPC-T42): the emitter's field picked up ahead with a countdown,
    /// the pulse throwing the hull out stunned into a fight on the lane, surging through it, or dropping out short of it
    /// and getting back in afterwards.
    /// </summary>
    [TestFixture]
    public class LaneInterdictionTests
    {
        const double StartJd = 2461324.0;
        const double Second = 1.0 / Astro.SecondsPerDay;

        sealed class Setup
        {
            public World W;
            public Ephemeris Eph;
            public Ship Pilot, Lurker;
            public Lane Lane;
        }

        /// <summary>
        /// Every other ship out of the picture; a hull of <paramref name="pilotClass"/> at the helm, just in through Earth's
        /// ramp on the Earth-Mars lane; and a raider lying dark beside that lane halfway along, its pulse charged.
        /// </summary>
        static Setup Create(ShipClass pilotClass = ShipClass.Courier)
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            foreach (var s in w.Fleet.Ships) s.Destroyed = true;
            w.War.Battles.Clear();
            var lanes = w.Fleet.Lanes;
            int earth = eph.Catalog["Earth"].Index, mars = eph.Catalog["Mars"].Index;
            var lane = lanes.Lanes.First(l => (l.A == earth && l.B == mars) || (l.A == mars && l.B == earth));
            var ramp = lanes.RampHelio(lane, earth, StartJd);
            var hop = lanes.OnRampAt(ramp, StartJd).Value;

            var lurker = Hull(w, "Shade", ShipClass.Destroyer, w.Pirates);
            w.Fleet.JoinLane(lurker, hop, ramp, StartJd);
            lurker.Phase = ShipPhase.Lurk;
            lurker.LaneHold = 0.5;
            lurker.LaneHoldSide = w.Fleet.OffLane(lurker, StartJd, 0.5, 30000, 1);
            lurker.PhaseStartJd = StartJd;
            lurker.PhaseEndJd = StartJd + 5;

            var pilot = Hull(w, "Swallow", pilotClass, pilotClass == ShipClass.Courier ? w.Independents : w.Sol);
            w.PilotedShip = pilot;
            Fleet.PinAt(pilot, earth, ramp - eph.HelioAt(earth, StartJd), DVec3.UnitX, StartJd);
            w.Fleet.JoinLane(pilot, hop, ramp, StartJd);
            return new Setup { W = w, Eph = eph, Pilot = pilot, Lurker = lurker, Lane = lane };
        }

        static Ship Hull(World w, string name, ShipClass c, Faction f)
        {
            var s = w.Fleet.AddShip(name, c, f, ShipRole.Patrol);
            s.Combat = new ShipCombat(CombatSpec.Of(c), c);
            return s;
        }

        static void Step(Setup t, double jd)
        {
            t.Eph.Update(jd);
            t.W.War.Update(jd);
        }

        /// <summary>Rides on in coarse steps until the scanners pick up the field ahead; the date it first shows, and what it shows.</summary>
        static (double jd, LaneThreat threat) RideToWarning(Setup t)
        {
            for (double jd = StartJd; jd < t.Pilot.PhaseEndJd; jd += 5 * Second)
            {
                Step(t, jd);
                var threat = t.W.War.ThreatAhead(t.Pilot, jd);
                if (threat.HasValue) return (jd, threat.Value);
                Assert.That(t.Pilot.Battle, Is.Null, "caught before any warning");
            }
            Assert.Fail("the field ahead was never picked up");
            return default;
        }

        /// <summary>Rides on second by second until the hull is caught in a fight or <paramref name="untilJd"/> passes.</summary>
        static double RideUntilCaught(Setup t, double fromJd, double untilJd)
        {
            for (double jd = fromJd; jd <= untilJd; jd += Second)
            {
                Step(t, jd);
                if (t.Pilot.Battle != null) return jd;
            }
            return double.NaN;
        }

        [Test]
        public void TheFieldAheadIsPickedUpWithACountdownToThePulse()
        {
            var t = Create();
            Assert.That(t.W.War.ThreatAhead(t.Pilot, StartJd), Is.Null, "half a lane off, the field is beyond the scanners' reach");

            var (seenJd, threat) = RideToWarning(t);
            double reach = SensorSuite.Of(t.Pilot).ReachKm(War.EmitterSignature);
            Assert.That(threat.Lurker, Is.SameAs(t.Lurker));
            Assert.That(threat.DistanceKm, Is.LessThanOrEqualTo(reach).And.GreaterThan(reach * 0.8), "picked up as it comes into reach, not before");
            Assert.That(threat.DistanceKm, Is.GreaterThan(War.PulseRadiusKm), "warned before the pulse can reach");
            Assert.That(threat.SecondsToPulse, Is.GreaterThan(30), "time to answer it");
            Assert.That(threat.Braced, Is.False, "a courier's nav sensors cannot make out the dark hull itself");

            double caughtJd = RideUntilCaught(t, seenJd + Second, seenJd + (threat.SecondsToPulse + 30) * Second);
            Assert.That(double.IsNaN(caughtJd), Is.False, "the pulse went off");
            Assert.That((caughtJd - seenJd) * Astro.SecondsPerDay, Is.EqualTo(threat.SecondsToPulse).Within(2), "when the countdown said");
        }

        [Test]
        public void ARaiderOffRunningThePlayerDownSpringsNoPulseOnTheLaneItLeft()
        {
            var t = Create();
            // The raider has left its lurk to chase someone (U.SPC-T273): its schedule still reads "lurking".
            t.W.Pursuit.Begin(t.Lurker, new DVec3(1e6, 0, 0), DVec3.Zero, StartJd);
            for (double jd = StartJd; jd < t.Pilot.PhaseEndJd; jd += 5 * Second)
            {
                Step(t, jd);
                Assert.That(t.W.War.ThreatAhead(t.Pilot, jd), Is.Null, "a field ahead from a raider that is not there");
                Assert.That(t.Pilot.Battle, Is.Null, "thrown out of the lane by a raider off on a chase");
            }
            Assert.That(t.Lurker.Battle, Is.Null);
        }

        [Test]
        public void ThePulseThrowsTheHullOutStunnedIntoAFightOnTheLaneItFliesItself()
        {
            var t = Create();
            var (seenJd, threat) = RideToWarning(t);
            RideUntilCaught(t, seenJd, seenJd + (threat.SecondsToPulse + 30) * Second);

            var b = t.Pilot.Battle;
            Assert.That(b, Is.Not.Null);
            Assert.That(b.Site, Is.Not.Null, "fought out on the lane, at no body");
            Assert.That(b.Site.Hop.Lane, Is.SameAs(t.Lane));
            Assert.That(t.Pilot.Phase, Is.EqualTo(ShipPhase.Adrift), "thrown out of the lane");
            var unit = b.UnitOf(t.Pilot);
            Assert.That(unit, Is.Not.Null);
            Assert.That(unit.PlayerControlled, Is.True, "the helm flies it");
            Assert.That(unit.Side, Is.Not.EqualTo(b.UnitOf(t.Lurker).Side), "against the raiders");
            Assert.That(unit.Stunned, Is.GreaterThan(0), "drives knocked out");
            Assert.That(unit.Combat.Shield, Is.EqualTo(0), "shields down");
            var notices = t.W.War.TakePilotNotices();
            Assert.That(notices.Any(n => n.StartsWith("INTERDICTED")), Is.True);
            Assert.That(t.W.War.TakePilotNotices(), Is.Empty, "told once");
        }

        [Test]
        public void AWarshipThatMakesOutTheHullIsBracedAndRidesThePulseOut()
        {
            var t = Create(ShipClass.Destroyer);
            var (seenJd, threat) = RideToWarning(t);
            Assert.That(threat.Braced, Is.True, "a warship's suite makes out the raider lying dark at its stand-off");
            RideUntilCaught(t, seenJd, seenJd + (threat.SecondsToPulse + 30) * Second);
            var unit = t.Pilot.Battle?.UnitOf(t.Pilot);
            Assert.That(unit, Is.Not.Null, "still thrown out of the lane into the fight");
            Assert.That(unit.Stunned, Is.EqualTo(0), "but its drives and shields held");
        }

        [Test]
        public void ABracedCarriersFightersRideThePulseOutWithIt()
        {
            var t = Create(ShipClass.Carrier);
            var (seenJd, threat) = RideToWarning(t);
            Assert.That(threat.Braced, Is.True, "a warship's suite makes out the raider lying dark");
            RideUntilCaught(t, seenJd, seenJd + (threat.SecondsToPulse + 30) * Second);
            var b = t.Pilot.Battle;
            Assert.That(b, Is.Not.Null);
            var fighters = b.Units.Where(u => u.Kind == UnitKind.Fighter && u.Fighter.Carrier.Ship == t.Pilot).ToList();
            Assert.That(fighters, Is.Not.Empty, "the carrier launched its wings into the fight");
            Assert.That(b.UnitOf(t.Pilot).Stunned, Is.EqualTo(0), "the carrier itself was braced");
            foreach (var f in fighters)
            {
                Assert.That(f.Stunned, Is.EqualTo(0), "its fighters launched into a pulse the carrier had braced for");
                Assert.That(f.Combat.Shield, Is.GreaterThan(0));
            }
        }

        [Test]
        public void SurgingSpeedsTheRestOfTheRideFromWhereItIsOnceARide()
        {
            var t = Create();
            double jd = StartJd + 0.05;
            var before = t.W.Fleet.StateOf(t.Pilot, jd);
            double left = t.Pilot.PhaseEndJd - jd;
            Assert.That(Fleet.SurgeRide(t.Pilot, jd), Is.True);
            var after = t.W.Fleet.StateOf(t.Pilot, jd);
            Assert.That((after.HelioKm - before.HelioKm).Length, Is.LessThan(1), "no jump along the lane");
            Assert.That(t.Pilot.PhaseEndJd - jd, Is.EqualTo(left / Fleet.SurgeFactor).Within(1e-9), "the rest of the ride goes that much faster");
            Assert.That(after.SpeedKmS, Is.EqualTo(before.SpeedKmS * Fleet.SurgeFactor).Within(before.SpeedKmS * Fleet.SurgeFactor * 0.03), "(as near as the finite difference gives it)");
            Assert.That(Fleet.SurgeRide(t.Pilot, jd + 0.01), Is.False, "once a ride");
        }

        [Test]
        public void SurgingWhileEasingBackInFromBesideTheLaneDoesNotPopTheHullSideways()
        {
            var t = Create();
            var (seenJd, threat) = RideToWarning(t);
            t.W.Fleet.DropOut(t.Pilot, seenJd, DVec3.UnitX);
            double past = seenJd + (threat.SecondsToPulse + 120) * Second;
            var near = t.W.Fleet.RejoinPoint(t.Pilot, past) + new DVec3(0, 80000, 0);
            Assert.That(t.W.Fleet.RejoinLane(t.Pilot, near, past), Is.True);
            double jd = past + 0.03; // about an hour into the ease back in
            var before = t.W.Fleet.StateOf(t.Pilot, jd);
            Assert.That(Fleet.SurgeRide(t.Pilot, jd), Is.True);
            var after = t.W.Fleet.StateOf(t.Pilot, jd);
            Assert.That((after.HelioKm - before.HelioKm).Length, Is.LessThan(1000), "the remaining sideways offset jumped when the ride was sped up");
        }

        [Test]
        public void ASurgingHullCanBeThroughBeforeThePulseClosesOrStillBeCaught()
        {
            var t = Create();
            t.W.War.SurgeSlipChance = 1;
            var (seenJd, _) = RideToWarning(t);
            Fleet.SurgeRide(t.Pilot, seenJd);
            var threat = t.W.War.ThreatAhead(t.Pilot, seenJd).Value;
            double caught = RideUntilCaught(t, seenJd, seenJd + (threat.SecondsToPulse + 60) * Second);
            Assert.That(double.IsNaN(caught), Is.True, "past and gone");
            Assert.That(t.Pilot.Phase, Is.EqualTo(ShipPhase.Lane), "still riding");
            Assert.That(t.W.War.TakePilotNotices().Any(n => n.StartsWith("Through")), Is.True);
            Assert.That(t.W.War.ThreatAhead(t.Pilot, seenJd + (threat.SecondsToPulse + 60) * Second), Is.Null, "the spent emitter is behind");

            var u = Create();
            u.W.War.SurgeSlipChance = 0;
            var (seen2, _) = RideToWarning(u);
            Fleet.SurgeRide(u.Pilot, seen2);
            var threat2 = u.W.War.ThreatAhead(u.Pilot, seen2).Value;
            Assert.That(threat2.SecondsToPulse, Is.LessThan(threat.SecondsToPulse * 1.01), "surging brings the pulse on sooner");
            Assert.That(double.IsNaN(RideUntilCaught(u, seen2, seen2 + (threat2.SecondsToPulse + 60) * Second)), Is.False, "caught anyway");
        }

        [Test]
        public void DroppingOutShortOfTheFieldStaysClearAndTheHullCanGetBackInWhereItLeft()
        {
            var t = Create();
            var (seenJd, threat) = RideToWarning(t);
            var at = t.W.Fleet.StateOf(t.Pilot, seenJd).HelioKm;
            double progress = Fleet.LaneProgress(t.Pilot, seenJd);
            t.W.Fleet.DropOut(t.Pilot, seenJd, DVec3.UnitX);
            Assert.That(t.Pilot.Phase, Is.EqualTo(ShipPhase.Parked), "off the lane, under the pilot's hand");
            Assert.That(t.Pilot.Orbit.Body, Is.EqualTo(t.W.Catalog.Root.Index), "in the system root's frame");
            Assert.That((t.W.Fleet.StateOf(t.Pilot, seenJd).HelioKm - at).Length, Is.LessThan(1), "right where it was");
            Assert.That(t.Pilot.LaneHold, Is.EqualTo(progress).Within(1e-12));

            double past = seenJd + (threat.SecondsToPulse + 120) * Second;
            Assert.That(double.IsNaN(RideUntilCaught(t, seenJd, past)), Is.True, "the pulse never went off");
            Assert.That(t.Lurker.Battle, Is.Null);
            Assert.That(t.Lurker.Phase, Is.EqualTo(ShipPhase.Lurk), "still lying in wait");

            var lanePoint = t.W.Fleet.RejoinPoint(t.Pilot, past);
            var far = lanePoint + new DVec3(Fleet.RejoinKm * 2, 0, 0);
            Assert.That(t.W.Fleet.RejoinLane(t.Pilot, far, past), Is.False, "too far from the lane line");
            var near = lanePoint + new DVec3(0, 20000, 0);
            Assert.That(t.W.Fleet.RejoinLane(t.Pilot, near, past), Is.True);
            Assert.That(t.Pilot.Phase, Is.EqualTo(ShipPhase.Lane));
            Assert.That((t.W.Fleet.StateOf(t.Pilot, past).HelioKm - near).Length, Is.LessThan(1), "eases in from where it is");
            Assert.That(Fleet.LaneProgress(t.Pilot, past), Is.EqualTo(progress).Within(1e-9), "back in at the point it left");
            Assert.That(Fleet.LaneProgress(t.Pilot, t.Pilot.PhaseEndJd), Is.EqualTo(1).Within(1e-12), "and on to the far ramp");
            Assert.That(t.W.War.ThreatAhead(t.Pilot, past), Is.Not.Null, "the raider is still ahead");
        }
    }
}
