using System;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// U.SPC-T522: a belt works' capture tugs are real hulls (<see cref="ShipClass.CaptureTug"/>), not a count and a timer:
    /// each flies out into the field round its refinery, matches a rock of 40-140 m, latches on and pushes it in for the
    /// grinders on the ore tugs' hull machinery (<see cref="Ship.Tow"/>, <see cref="Ship.Homing"/>), and can be seized where
    /// it is or helmed like any other hull.
    /// </summary>
    [TestFixture]
    public class CaptureTugTests
    {
        const double StartJd = 2461324.0;

        static World Sol(out Ephemeris eph)
        {
            eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return World.CreateDefault(eph, StartJd);
        }

        static void Step(World w, Ephemeris eph, double days, Func<bool> until = null, double dt = 1)
        {
            double jd = w.Jd;
            for (double end = jd + days; jd < end;)
            {
                jd += dt;
                eph.Update(jd);
                w.Update(jd);
                if (until != null && until()) return;
            }
        }

        [Test]
        public void TheCaptureTugIsAClassOfItsOwnWithClawsNotAHold()
        {
            var spec = ShipClassSpec.Of(ShipClass.CaptureTug);
            Assert.That(spec.Class, Is.EqualTo(ShipClass.CaptureTug));
            Assert.That(spec.DisplayName, Is.EqualTo("Capture tug"));
            Assert.That(spec.IsMilitary, Is.False);
            Assert.That(spec.AccelG, Is.GreaterThan(ShipClassSpec.Of(ShipClass.OreTug).AccelG), "nimbler than an ore tug");

            var design = CombatSpec.Of(ShipClass.CaptureTug);
            Assert.That(design.DesignName, Is.EqualTo("Capture tug"));
            Assert.That(design.Layout.Any(s => s.IsWeapon), Is.False, "no guns");
            Assert.That(design.DryMassTonnes, Is.LessThan(CombatSpec.Of(ShipClass.OreTug).DryMassTonnes), "a lighter frame than an ore tug's");

            var frame = HullFrames.ForClass(ShipClass.CaptureTug);
            Assert.That(frame, Is.Not.Null);
            Assert.That(frame.Category, Is.EqualTo(HullCategory.Freighter));
            Assert.That(Economy.HullCost(ShipClass.CaptureTug), Is.GreaterThan(0).And.LessThan(Economy.HullCost(ShipClass.OreTug)));
            Assert.That(Economy.CapacityOf(ShipClass.CaptureTug), Is.EqualTo(0), "its rock is in its claws, not a hold");
            Assert.That(FlightModelRegistry.Default.FamilyOf(ShipClass.CaptureTug), Is.EqualTo(FlightFamily.Hauler));
            Assert.That(PilotTuning.ControlOf(ShipClass.CaptureTug), Is.EqualTo(PilotControl.Hauler));
            Assert.That(Interior.For(ShipClass.CaptureTug, design.Crew, 0).Compartments.Any(c => c.Name == "Claw gantry"), Is.True);
        }

        [Test]
        public void EveryCaptureTugIsARealHullInTheFleetThePlayerCanTakeTheHelmOf()
        {
            var w = Sol(out _);
            var op = w.Mining.Operations.Single();
            Assert.That(op.CaptureTugs, Is.EqualTo(2));
            Assert.That(op.CaptureHulls, Has.Count.EqualTo(op.CaptureTugs), "a hull for every capture tug the works counts");
            foreach (var tug in op.CaptureHulls)
            {
                Assert.That(w.Fleet.Ships, Does.Contain(tug));
                Assert.That(w.Fleet.IsPresent(tug), Is.True);
                Assert.That(tug.Spec.Class, Is.EqualTo(ShipClass.CaptureTug));
                Assert.That(tug.Role, Is.EqualTo(ShipRole.Tug));
                Assert.That(tug.Faction, Is.SameAs(op.Owner));
                Assert.That(tug.Orbit.Body, Is.EqualTo(op.RefineryBody), "it lies at its refinery");
                Assert.That(w.Mining.OperationOf(tug), Is.SameAs(op));
                Assert.That(new ShipHelm(w, tug).CanEmbody, Is.True, "its helm can be taken");
            }
            Assert.That(op.CaptureHulls.Intersect(op.Tugs).Any(), Is.False, "its own hulls, not the ore tugs'");
        }

        [Test]
        public void ACaptureTugFliesOutForARockMatchesItAndPushesItInToTheGrinders()
        {
            var w = Sol(out var eph);
            var m = w.Mining;
            var op = m.Operations.Single();
            int captured = op.RocksCaptured;

            bool outEmpty = false, inLaden = false;
            TugHaul catch_ = null;
            Step(w, eph, 300, () =>
            {
                catch_ ??= m.Captures.FirstOrDefault();
                if (catch_ == null) return false;
                var tug = catch_.Tug;
                if (!catch_.Latched && tug.Phase == ShipPhase.Transit)
                {
                    Assert.That(tug.Tow, Is.Null, "out to the field with its claws open");
                    outEmpty = true;
                }
                if (catch_.Latched && tug.Phase == ShipPhase.Transit)
                {
                    Assert.That(tug.Tow, Is.SameAs(catch_.Load), "the rock is in its claws");
                    Assert.That(tug.AccelG, Is.LessThan(tug.Spec.AccelG), "slowed by its rock");
                    inLaden = true;
                }
                return catch_.State != ConvoyState.EnRoute;
            }, dt: 0.05);

            Assert.That(catch_, Is.Not.Null, "the works sent a capture tug out");
            Assert.That(catch_.Capture, Is.True);
            Assert.That(op.CaptureHulls, Does.Contain(catch_.Tug));
            Assert.That(catch_.Load.DiameterM, Is.InRange(op.MinRockM, op.MaxRockM), "a rock of 40-140 m");
            Assert.That(outEmpty, Is.True, "it flew out to the field for its rock");
            Assert.That(inLaden, Is.True, "and pushed it in");
            Assert.That(catch_.State, Is.EqualTo(ConvoyState.Delivered));
            Assert.That(catch_.Tug.Tow, Is.Null, "it let go at the refinery");
            Assert.That(catch_.Tug.Orbit.Body, Is.EqualTo(op.RefineryBody));
            Assert.That(op.RocksCaptured, Is.GreaterThan(captured), "the rock is parked at the refinery for the grinders");
            Assert.That(m.Hauls.Any(h => h.Capture), Is.False, "the ore tugs' hauls are their own");
            Assert.That(w.Events.Any(e => e.Text.Contains(catch_.Tug.Name) && e.Text.Contains("capture tug")), Is.True);
        }

        [Test]
        public void ACaptureTugTakenInAFightTakesItsRockWithIt()
        {
            var w = Sol(out var eph);
            var m = w.Mining;
            var op = m.Operations.Single();
            Step(w, eph, 3);
            var catch_ = m.Captures.First(h => h.State == ConvoyState.EnRoute);
            var tug = catch_.Tug;
            double plundered = m.Plundered.Sum();

            tug.Faction = w.Pirates;
            Step(w, eph, 1);
            Assert.That(catch_.State, Is.EqualTo(ConvoyState.Stolen));
            Assert.That(catch_.TakenBy, Is.SameAs(w.Pirates));
            Assert.That(tug.Tow, Is.Null);
            Assert.That(op.CaptureHulls, Does.Not.Contain(tug));
            Assert.That(op.CaptureTugs, Is.EqualTo(1));
            Assert.That(op.CaptureTugsLost, Is.EqualTo(1));
            Assert.That(op.TugsLost, Is.EqualTo(0), "no ore tug lost");
            Assert.That(m.Plundered.Sum(), Is.GreaterThan(plundered), "its rock is the raiders'");
        }

        [Test]
        public void ACaptureTugSeizedWhereItIsFliesTheRaidersColours()
        {
            var w = Sol(out var eph);
            var m = w.Mining;
            Step(w, eph, 3);
            var catch_ = m.Captures.First(h => h.State == ConvoyState.EnRoute);
            var tug = catch_.Tug;
            var start = w.Fleet.StateOf(tug, w.Jd).HelioKm;
            catch_.TakenBy = w.Pirates;
            m.TugSeized(catch_, w.Jd);
            Assert.That(tug.Faction, Is.SameAs(w.Pirates));
            Assert.That(tug.Role, Is.EqualTo(ShipRole.Free), "no longer the works' to send");
            Assert.That(w.Fleet.IsPresent(tug), Is.True);
            Assert.That((w.Fleet.StateOf(tug, w.Jd).HelioKm - start).Length, Is.LessThan(1e4), "taken where it was");
        }

        [Test]
        public void AWorksRunOnItsOwnStillCapturesByTimer()
        {
            var mining = new BeltMining(3);
            var owner = new Faction("Belt Consortium", "BCS", default);
            var op = mining.AddOperation("Works", owner, 1, null);
            mining.Update(StartJd);
            mining.Update(StartJd + 120);
            Assert.That(op.CaptureHulls, Is.Null);
            Assert.That(op.RocksCaptured, Is.GreaterThan(0));
            Assert.That(mining.Captures, Is.Empty, "no hulls, no flown catches");
        }
    }
}
