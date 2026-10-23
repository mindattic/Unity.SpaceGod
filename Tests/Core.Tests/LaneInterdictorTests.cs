using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The lane interdictor (U.SPC-T479): the interdiction pulse comes from a dedicated hull carrying a field projector, not
    /// from any raider lying beside a lane. Raider bands sail with one for their lane ambushes and navies fly one in a lane
    /// picket, and ships its pulse throws out of the lane drop into the battle beside it as before.
    /// </summary>
    [TestFixture]
    public class LaneInterdictorTests
    {
        const double StartJd = 2461324.0;
        const double Second = 1.0 / Astro.SecondsPerDay;

        sealed class Setup
        {
            public World W;
            public Ephemeris Eph;
            public Lane Lane;
            public LaneHop Hop;
            public DVec3 Ramp;
            public int Earth, Mars;
        }

        /// <summary>Default Sol with every ship out of the picture, and Earth's ramp onto the Earth-Mars lane.</summary>
        static Setup Create()
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
            return new Setup { W = w, Eph = eph, Lane = lane, Hop = hop, Ramp = ramp, Earth = earth, Mars = mars };
        }

        static Ship Hull(World w, string name, ShipClass c, Faction f, ShipRole role = ShipRole.Patrol)
        {
            var s = w.Fleet.AddShip(name, c, f, role);
            s.Combat = new ShipCombat(CombatSpec.Of(c), c);
            return s;
        }

        /// <summary>A group led by a hull of <paramref name="leaderClass"/> lying in wait halfway along the lane, with escorts of <paramref name="escorts"/>.</summary>
        static Ship Lurk(Setup t, string name, ShipClass leaderClass, Faction f, params ShipClass[] escorts)
        {
            var leader = Hull(t.W, name, leaderClass, f);
            for (int i = 0; i < escorts.Length; i++)
            {
                var e = Hull(t.W, name + " " + (i + 2), escorts[i], f, ShipRole.Escort);
                t.W.Fleet.AddEscort(leader, e, Fleet.SlotFor(i));
            }
            t.W.Fleet.JoinLane(leader, t.Hop, t.Ramp, StartJd);
            leader.Phase = ShipPhase.Lurk;
            leader.LaneHold = 0.5;
            leader.LaneHoldSide = t.W.Fleet.OffLane(leader, StartJd, 0.5, 30000, 1);
            leader.PhaseStartJd = StartJd;
            leader.PhaseEndJd = StartJd + 5;
            return leader;
        }

        static Ship Ride(Setup t, string name, ShipClass c, Faction f)
        {
            var s = Hull(t.W, name, c, f);
            t.W.Fleet.JoinLane(s, t.Hop, t.Ramp, StartJd);
            return s;
        }

        /// <summary>Steps the war along until the ride is over or <paramref name="caught"/> is in a fight; whether it was caught.</summary>
        static bool RideOut(Setup t, Ship caught, double step = 30 * Second)
        {
            double end = caught.PhaseEndJd;
            for (double jd = StartJd; jd <= end; jd += step)
            {
                t.Eph.Update(jd);
                t.W.War.Update(jd);
                if (caught.Battle != null) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- the class

        [Test]
        public void TheInterdictorIsAWarshipClassWithItsOwnFrameFlightFamilyCrewAndCost()
        {
            var spec = ShipClassSpec.Of(ShipClass.Interdictor);
            Assert.That(spec.Class, Is.EqualTo(ShipClass.Interdictor));
            Assert.That(spec.DisplayName, Is.EqualTo("Lane interdictor"));
            Assert.That(spec.IsMilitary, Is.True);
            Assert.That(CombatSpec.Of(ShipClass.Interdictor).DesignName, Is.EqualTo("Lane interdictor"), "its own design, not the destroyer's fallback");
            var frame = HullFrames.ForClass(ShipClass.Interdictor);
            Assert.That(frame, Is.Not.Null);
            Assert.That(frame.Category, Is.EqualTo(HullCategory.Warship));
            Assert.That(FlightModelRegistry.CreateDefault().FamilyOf(ShipClass.Interdictor), Is.EqualTo(FlightFamily.Capital));
            Assert.That(Crews.Complement(ShipClass.Interdictor), Is.GreaterThan(0).And.LessThan(Crews.Complement(ShipClass.Destroyer)));
            Assert.That(Economy.HullCost(ShipClass.Interdictor), Is.GreaterThan(Economy.HullCost(ShipClass.Destroyer)), "the projector costs more than a destroyer's guns");
            Assert.That(CruiseDrives.Of(ShipClass.Interdictor), Is.SameAs(CruiseDrives.Capital), "it crosses between the stars with its band");
            Assert.That(LaneInterdictors.Emits(ShipClass.Interdictor), Is.True);
            Assert.That(LaneInterdictors.Emits(ShipClass.Destroyer), Is.False);
        }

        [Test]
        public void AnotherClassCanBeFittedWithAProjector()
        {
            try
            {
                LaneInterdictors.RegisterEmitter(ShipClass.Cruiser);
                Assert.That(LaneInterdictors.Emits(ShipClass.Cruiser), Is.True);
            }
            finally
            {
                LaneInterdictors.RegisterEmitter(ShipClass.Cruiser, false);
            }
            Assert.That(LaneInterdictors.Emits(ShipClass.Cruiser), Is.False);
        }

        // ---------------------------------------------------------------- the pulse comes from the interdictor

        [Test]
        public void ARaiderBandWithoutAnInterdictorPullsNobodyOutOfTheLane()
        {
            var t = Create();
            var raider = Lurk(t, "Shade", ShipClass.Destroyer, t.W.Pirates, ShipClass.Destroyer);
            var mark = Ride(t, "Fat Purse", ShipClass.Freighter, t.W.Independents);
            Assert.That(LaneInterdictors.Fielded(raider, t.W.Fleet.Ships), Is.False);
            Assert.That(RideOut(t, mark), Is.False, "thrown out of the lane by a band with no projector");
            Assert.That(raider.Battle, Is.Null);
        }

        [Test]
        public void TheBandsInterdictorPulsesAndItsMarkDropsIntoTheFightBesideTheLane()
        {
            var t = Create();
            var raider = Lurk(t, "Shade", ShipClass.Destroyer, t.W.Pirates, ShipClass.Interdictor);
            var emitter = t.W.Fleet.Ships.Single(s => s.Leader == raider && s.Spec.Class == ShipClass.Interdictor);
            Assert.That(LaneInterdictors.EmitterOf(raider, t.W.Fleet.Ships), Is.SameAs(emitter));
            var mark = Ride(t, "Fat Purse", ShipClass.Freighter, t.W.Independents);
            Assert.That(RideOut(t, mark), Is.True, "the band's interdictor pulled it out");

            var b = mark.Battle;
            Assert.That(b.Site, Is.Not.Null, "fought beside the lane, at no body");
            Assert.That(b.Site.Hop.Lane, Is.SameAs(t.Lane));
            Assert.That(b.EnforcingSide, Is.EqualTo(-1), "a raiders' ambush, not the law");
            Assert.That(mark.Phase, Is.EqualTo(ShipPhase.Adrift));
            Assert.That(b.UnitOf(emitter), Is.Not.Null, "the interdictor fights with its band");
            var pulse = b.Fx.Where(f => f.Kind == FxKind.Pulse).ToList();
            Assert.That(pulse, Is.Not.Empty);
            Assert.That(pulse[0].Unit, Is.SameAs(b.UnitOf(emitter)), "the pulse comes from the interdictor, not the band's leader");
        }

        [Test]
        public void LosingItsInterdictorEndsTheBandsLaneAmbush()
        {
            var t = Create();
            var raider = Lurk(t, "Shade", ShipClass.Destroyer, t.W.Pirates, ShipClass.Interdictor);
            foreach (var s in t.W.Fleet.Ships.Where(s => s.Leader == raider)) s.Destroyed = true;
            var mark = Ride(t, "Fat Purse", ShipClass.Freighter, t.W.Independents);
            Assert.That(RideOut(t, mark), Is.False);
        }

        [Test]
        public void EveryRaiderBandSailsWithAnInterdictor()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            var bands = w.Fleet.Ships.Where(s => !s.Destroyed && !s.IsEscort && s.Faction == w.Pirates && s.Spec.IsMilitary && !s.Runner).ToList();
            Assert.That(bands, Is.Not.Empty);
            foreach (var band in bands)
                Assert.That(LaneInterdictors.Fielded(band, w.Fleet.Ships), Is.True, band.Name + " has no interdictor");
        }

        // ---------------------------------------------------------------- navies field it

        [Test]
        public void ANavysLanePicketPullsOutRaidersAndLetsLawfulTrafficRideOn()
        {
            var t = Create();
            var picket = Lurk(t, "Vigil", ShipClass.Destroyer, t.W.Sol, ShipClass.Interdictor);
            var raider = Ride(t, "Gutter Knife", ShipClass.Breacher, t.W.Pirates);
            var trader = Ride(t, "Honest Margin", ShipClass.Freighter, t.W.Independents);
            Assert.That(t.W.War.Targets(picket, raider), Is.True);
            Assert.That(t.W.War.Targets(picket, trader), Is.False, "lawful traffic is no business of the picket's");

            Assert.That(RideOut(t, raider), Is.True, "the picket pulled the raider out");
            var b = raider.Battle;
            Assert.That(b.Site, Is.Not.Null, "fought beside the lane");
            Assert.That(b.EnforcingSide, Is.EqualTo(b.UnitOf(picket).Side), "the navy enforces the law on the lane");
            Assert.That(b.UnitOf(raider).Side, Is.Not.EqualTo(b.UnitOf(picket).Side));
            Assert.That(trader.Battle, Is.Null, "the trader rode through");
            Assert.That(trader.Phase, Is.EqualTo(ShipPhase.Lane));
        }

        [Test]
        public void ARaidersPulseSparesOtherRaidersAndANavysSparesItsOwn()
        {
            var t = Create();
            var raider = Lurk(t, "Shade", ShipClass.Interdictor, t.W.Pirates);
            var picket = Hull(t.W, "Vigil", ShipClass.Interdictor, t.W.Sol);
            var mate = Hull(t.W, "Brother", ShipClass.Destroyer, t.W.Pirates);
            var own = Hull(t.W, "Sister", ShipClass.Destroyer, t.W.Sol);
            Assert.That(t.W.War.Targets(raider, mate), Is.False);
            Assert.That(t.W.War.Targets(raider, own), Is.True);
            Assert.That(t.W.War.Targets(picket, own), Is.False);
            Assert.That(t.W.War.Targets(picket, mate), Is.True);
        }

        [Test]
        public void ALanePicketWingCarriesAnInterdictorAndLiesInWaitOnALaneOfItsBeat()
        {
            Assert.That(Enumerable.Range(0, 4).Any(i => PatrolWings.Composition(i).Contains(ShipClass.Interdictor)), Is.True, "no lane picket among the wings");
            var t = Create();
            // An older group, so the wing is not taken for the navy's home guard.
            Hull(t.W, "Old Guard", ShipClass.Destroyer, t.W.Sol);
            var wing = Hull(t.W, "Tripwire", ShipClass.Destroyer, t.W.Sol);
            wing.PatrolWing = true;
            wing.Route = new[] { t.Earth, t.Mars };
            var e = Hull(t.W, "Tripwire Two", ShipClass.Interdictor, t.W.Sol, ShipRole.Escort);
            e.PatrolWing = true;
            t.W.Fleet.AddEscort(wing, e, Fleet.SlotFor(0));
            t.W.Fleet.Station(wing, t.Earth, StartJd);
            t.W.Fleet.Lanes.SetDanger(t.Lane, 5);
            double chance = LaneInterdictors.PicketChance.Value;
            try
            {
                LaneInterdictors.PicketChance.Set(1);
                int next = t.W.War.ChooseNext(wing, t.Earth, StartJd);
                var o = t.W.War.OrderOf(wing);
                Assert.That(o.Kind, Is.EqualTo(OrderKind.Ambush), "the picket wing lies in wait on the lane");
                Assert.That(next, Is.EqualTo(t.Mars), "bound along it for the next port of its beat");
                Assert.That(PatrolWings.OnBeat(wing, o.Body), Is.True);
                Assert.That(wing.LurkLane, Is.SameAs(t.Lane));
                Assert.That(wing.LurkAt, Is.InRange(0.3, 0.7));

                // A wing without one keeps to its rounds.
                e.Destroyed = true;
                t.W.War.ChooseNext(wing, t.Earth, StartJd);
                Assert.That(t.W.War.OrderOf(wing).Kind, Is.Not.EqualTo(OrderKind.Ambush));
                Assert.That(wing.LurkLane, Is.Null, "the stale picket is dropped");
            }
            finally
            {
                LaneInterdictors.PicketChance.Set(chance);
            }
        }

        [Test]
        public void ALaneSecurityBuildPlanLaysDownInterdictors()
        {
            Assert.That(BuildPlan.LaneSecurity.Cycle, Does.Contain(ShipClass.Interdictor));
            Assert.That(BuildPlan.All, Does.Contain(BuildPlan.LaneSecurity));
        }
    }
}
