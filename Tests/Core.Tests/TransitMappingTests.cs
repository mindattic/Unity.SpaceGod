using System;
using System.Collections.Generic;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    [TestFixture]
    public class TransitMappingTests
    {
        const double StartJd = 2461324.0;

        /// <summary>Every transit leg flown by the default fleet over a year, captured at departure.</summary>
        static (Ephemeris eph, List<Ship> legs) Legs()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var fleet = Fleet.CreateDefault(eph, StartJd);
            var legs = new List<Ship>();
            var seen = new HashSet<(int, double)>();
            for (double jd = StartJd; jd < StartJd + 365; jd += 0.25)
            {
                eph.Update(jd);
                fleet.Update(jd);
                foreach (var ship in fleet.Ships)
                {
                    if (ship.IsEscort || ship.Phase != ShipPhase.Transit || !seen.Add((ship.Id, ship.PhaseStartJd))) continue;
                    legs.Add(Snapshot(ship));
                }
            }
            return (eph, legs);
        }

        static Ship Snapshot(Ship s) => new Ship
        {
            Id = s.Id, Name = s.Name, Spec = s.Spec, Phase = s.Phase,
            FrameBody = s.FrameBody, Orbit = s.Orbit, DepartureOrbit = s.DepartureOrbit, OriginBody = s.OriginBody, DestinationBody = s.DestinationBody,
            PhaseStartJd = s.PhaseStartJd, PhaseEndJd = s.PhaseEndJd,
            Path = s.Path, PathCumulative = s.PathCumulative, PathLength = s.PathLength, AccelKmPerDay2 = s.AccelKmPerDay2,
            DepartureVelocity = s.DepartureVelocity, ArrivalVelocity = s.ArrivalVelocity,
        };

        static DVec3[] LayoutAt(Ephemeris eph, ScaleModel scale, double jd)
        {
            eph.Update(jd);
            var scene = new DVec3[eph.Catalog.Count];
            scale.Layout(eph, scene);
            return scene;
        }

        [Test]
        public void TransitsLeaveAndJoinTheParkingSlotsExactly()
        {
            var (eph, legs) = Legs();
            Assert.That(legs.Count > 50, Is.True);
            var scale = new ScaleModel { Blend = 1 };
            foreach (var leg in legs)
            {
                var scene = LayoutAt(eph, scale, leg.PhaseStartJd);
                var origin = eph.Catalog[leg.OriginBody];
                var parked = scene[origin.Index] + scale.LocalOffset(origin, leg.DepartureOrbit.OffsetAt(leg.PhaseStartJd));
                var start = scale.TransitScene(eph, scene, leg, 0, out _);
                Assert.That((start - parked).Length < 1e-3, Is.True, $"{leg.Name} leaves {(start - parked).Length:G3} units off its slot");

                scene = LayoutAt(eph, scale, leg.PhaseEndJd);
                var destination = eph.Catalog[leg.Orbit.Body];
                parked = scene[destination.Index] + scale.LocalOffset(destination, leg.Orbit.OffsetAt(leg.PhaseEndJd));
                var end = scale.TransitScene(eph, scene, leg, 1, out _);
                Assert.That((end - parked).Length < 1e-3, Is.True, $"{leg.Name} arrives {(end - parked).Length:G3} units off its slot");
            }
        }

        // Moons are drawn inflated, so in a crowded system a neighbouring moon's sphere can overlap
        // the far end's neighbourhood (a parked ship shows the same). What the mapping promises is
        // clearance from the origin while leaving, the destination while arriving, and the planets
        // they orbit throughout.
        [Test]
        public void TransitsNeverEnterTheirEndpointBodiesOrJump()
        {
            var (eph, legs) = Legs();
            var scale = new ScaleModel { Blend = 1 };
            foreach (var leg in legs)
            {
                var parents = new[] { leg.OriginBody, leg.Orbit.Body }
                    .Select(b => eph.Catalog[b].ParentIndex)
                    .Where(b => b > 0).Distinct().ToArray();
                DVec3 previous = default;
                double travelled = 0, biggestStep = 0;
                const int samples = 400;
                for (int k = 0; k <= samples; k++)
                {
                    double u = (double)k / samples;
                    // Time and progress advance together, as they do in flight.
                    double tau = u * (leg.PhaseEndJd - leg.PhaseStartJd);
                    double half = (leg.PhaseEndJd - leg.PhaseStartJd) / 2;
                    double progress = tau < half ? 2 * u * u : 1 - 2 * (1 - u) * (1 - u);
                    var scene = LayoutAt(eph, scale, leg.PhaseStartJd + tau);
                    var p = scale.TransitScene(eph, scene, leg, progress, out var dir);
                    Assert.That(double.IsNaN(p.X + p.Y + p.Z + dir.X), Is.False, leg.Name);
                    foreach (int b in parents.Append(progress < 0.5 ? leg.OriginBody : leg.Orbit.Body))
                    {
                        double clearance = (p - scene[b]).Length / scale.DisplayRadius(eph.Catalog[b]);
                        Assert.That(clearance > 1.0, Is.True, $"{leg.Name} passes inside {eph.Catalog[b].Name} at progress {progress:0.000}");
                    }
                    if (k > 0)
                    {
                        double step = (p - previous).Length;
                        travelled += step;
                        biggestStep = Math.Max(biggestStep, step);
                    }
                    previous = p;
                }
                Assert.That(biggestStep < 0.08 * travelled, Is.True, $"{leg.Name} jumps {biggestStep / travelled:P0} of its drawn path in one step");
            }
        }

        [Test]
        public void BetweenPlanetsShipsFlyTheTrueLineAsDrawn()
        {
            var (eph, legs) = Legs();
            var scale = new ScaleModel { Blend = 1 };
            int checkedLegs = 0, onLine = 0;
            foreach (var leg in legs)
            {
                int SystemOf(int b) => eph.Catalog[b].ParentIndex == 0 ? b : eph.Catalog[b].ParentIndex;
                if (SystemOf(leg.OriginBody) == SystemOf(leg.Orbit.Body)) continue;
                double mid = (leg.PhaseStartJd + leg.PhaseEndJd) / 2;
                var scene = LayoutAt(eph, scale, mid);
                var helio = eph.Helio(leg.FrameBody) + Fleet.TransitOffset(leg, 0.5);
                var drawn = scale.LocalOffset(eph.Catalog.Sun, helio);
                var p = scale.TransitScene(eph, scene, leg, 0.5, out _);
                checkedLegs++;
                if ((p - drawn).Length < 1e-6 * drawn.Length) onLine++;
            }
            Assert.That(checkedLegs > 20, Is.True);
            Assert.That(onLine >= checkedLegs * 0.9, Is.True, $"{onLine} of {checkedLegs} interplanetary legs are on the heliocentric line at midpoint");
        }

        [Test]
        public void RealisticScaleIsTheTrueInertialLine()
        {
            var (eph, legs) = Legs();
            var scale = new ScaleModel { Blend = 0 };
            foreach (var leg in legs.Take(40))
            {
                foreach (double u in new[] { 0.0, 0.01, 0.3, 0.5, 0.97, 1.0 })
                {
                    var scene = LayoutAt(eph, scale, leg.PhaseStartJd + u * (leg.PhaseEndJd - leg.PhaseStartJd));
                    var helio = eph.Helio(leg.FrameBody) + Fleet.TransitOffset(leg, u);
                    var p = scale.TransitScene(eph, scene, leg, u, out _);
                    var truth = helio.SwapYZ / ScaleModel.KmPerUnit;
                    Assert.That((p - truth).Length < 1e-6 * truth.Length + 1e-6, Is.True, $"{leg.Name} at {u}");
                }
            }
        }
    }
}
