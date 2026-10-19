using System;
using UnitySpace.Core;
using NUnit.Framework;

namespace UnitySpace.Core.Tests
{
    [TestFixture]
    public class DVec3Tests
    {
        [Test]
        public void CrossOfUnitAxesFollowsTheRightHandRule()
        {
            Assert.That(DVec3.Cross(DVec3.UnitX, DVec3.UnitY), Is.EqualTo(DVec3.UnitZ));
            Assert.That(DVec3.Cross(DVec3.UnitY, DVec3.UnitZ), Is.EqualTo(DVec3.UnitX));
            Assert.That(DVec3.Cross(DVec3.UnitZ, DVec3.UnitX), Is.EqualTo(DVec3.UnitY));
        }

        [Test]
        public void CrossIsAntiCommutative()
        {
            var a = new DVec3(1, 2, 3);
            var b = new DVec3(-4, 0, 2);
            Assert.That(DVec3.Cross(b, a), Is.EqualTo(-DVec3.Cross(a, b)));
        }

        [Test]
        public void CrossOfParallelVectorsIsZero()
        {
            var a = new DVec3(2, -3, 5);
            var b = a * 4;
            Assert.That(DVec3.Cross(a, b), Is.EqualTo(DVec3.Zero));
        }

        [Test]
        public void DotOfOrthogonalUnitAxesIsZero()
        {
            Assert.That(DVec3.Dot(DVec3.UnitX, DVec3.UnitY), Is.EqualTo(0));
        }

        [Test]
        public void DotWithSelfEqualsLengthSquared()
        {
            var v = new DVec3(3, -4, 12);
            Assert.That(DVec3.Dot(v, v), Is.EqualTo(v.LengthSquared).Within(1e-9));
        }

        [Test]
        public void DotMatchesItsDefinition()
        {
            var a = new DVec3(1, 2, 3);
            var b = new DVec3(4, -5, 6);
            Assert.That(DVec3.Dot(a, b), Is.EqualTo(1 * 4 + 2 * -5 + 3 * 6));
        }

        [Test]
        public void LerpAtZeroAndOneReturnsTheEndpoints()
        {
            var a = new DVec3(1, 2, 3);
            var b = new DVec3(10, 20, 30);
            Assert.That(DVec3.Lerp(a, b, 0), Is.EqualTo(a));
            Assert.That(DVec3.Lerp(a, b, 1), Is.EqualTo(b));
        }

        [Test]
        public void LerpAtOneHalfReturnsTheMidpoint()
        {
            var a = new DVec3(0, 0, 0);
            var b = new DVec3(10, -20, 30);
            Assert.That(DVec3.Lerp(a, b, 0.5), Is.EqualTo(new DVec3(5, -10, 15)));
        }

        [Test]
        public void SwapYZExchangesTheComponents()
        {
            var v = new DVec3(1, 2, 3);
            Assert.That(v.SwapYZ, Is.EqualTo(new DVec3(1, 3, 2)));
        }

        [Test]
        public void SwapYZIsItsOwnInverse()
        {
            var v = new DVec3(5, -7, 11);
            Assert.That(v.SwapYZ.SwapYZ, Is.EqualTo(v));
        }

        [Test]
        public void DistanceMatchesTheDifferenceVectorsLength()
        {
            var a = new DVec3(1, 2, 3);
            var b = new DVec3(4, 6, 3);
            Assert.That(DVec3.Distance(a, b), Is.EqualTo(5.0).Within(1e-9));
        }

        [Test]
        public void DistanceToSelfIsZero()
        {
            var a = new DVec3(7, -2, 9);
            Assert.That(DVec3.Distance(a, a), Is.EqualTo(0));
        }

        [Test]
        public void NormalizedProducesAUnitLengthVector()
        {
            var v = new DVec3(3, 4, 0);
            var n = v.Normalized;
            Assert.That(n.X, Is.EqualTo(0.6).Within(1e-12));
            Assert.That(n.Y, Is.EqualTo(0.8).Within(1e-12));
            Assert.That(n.Z, Is.EqualTo(0.0).Within(1e-12));
        }

        [Test]
        public void TurningTowardTheExactOppositeDirectionStillTurns()
        {
            foreach (var a in new[] { DVec3.UnitX, DVec3.UnitZ })
            {
                var step = DVec3.TurnStep(a, -a, 0.25);
                Assert.That(step.Length, Is.EqualTo(1.0).Within(1e-12));
                Assert.That(DVec3.Dot(step, a), Is.EqualTo(Math.Cos(Math.PI / 4)).Within(1e-9), "a quarter of a half-turn is 45 degrees");
                Assert.That(DVec3.TurnStep(a, -a, 0.5).Length, Is.EqualTo(1.0).Within(1e-12), "half-way is a right angle, not the zero vector");
            }
            var x = DVec3.TurnStep(DVec3.UnitX, DVec3.UnitY, 0.5);
            Assert.That(x.X, Is.EqualTo(x.Y).Within(1e-12));
        }

        [Test]
        public void ATurnStepTurnsTheFractionOfTheAngleItIsAskedFor()
        {
            foreach (double deg in new[] { 10.0, 90.0, 150.0, 170.0, 179.0, 179.9 })
            {
                double rad = deg * Math.PI / 180;
                var want = new DVec3(Math.Cos(rad), Math.Sin(rad), 0);
                double t = 5.0 / deg; // five degrees of the way round
                var step = DVec3.TurnStep(DVec3.UnitX, want, t);
                double turned = Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(step, DVec3.UnitX)))) * 180 / Math.PI;
                Assert.That(turned, Is.EqualTo(5.0).Within(1e-6), $"facing {deg} degrees away");
                Assert.That(step.Z, Is.EqualTo(0).Within(1e-12), "it turns in the plane toward the target");
            }
        }

        [Test]
        public void NormalizedOfTheZeroVectorIsZero()
        {
            Assert.That(DVec3.Zero.Normalized, Is.EqualTo(DVec3.Zero));
        }

        [Test]
        public void NormalizedOfAVectorThatUnderflowsToZeroLengthIsZero()
        {
            // 1e-200 squared underflows below the smallest subnormal double, so LengthSquared
            // rounds to exactly 0 even though the vector itself is not the zero vector.
            var tiny = new DVec3(1e-200, 0, 0);
            Assert.That(tiny.LengthSquared, Is.EqualTo(0.0));
            Assert.That(tiny.Normalized, Is.EqualTo(DVec3.Zero));
        }

        [Test]
        public void NormalizedOfASmallButRepresentableVectorHasUnitLength()
        {
            var small = new DVec3(1e-150, 1e-150, 1e-150);
            Assert.That(small.Normalized.Length, Is.EqualTo(1.0).Within(1e-9));
        }
    }
}
