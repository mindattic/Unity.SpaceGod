using SolarSystem.Core;
using Xunit;

namespace SolarSystem.Core.Tests
{
    public class DVec3Tests
    {
        [Fact]
        public void CrossOfUnitAxesFollowsTheRightHandRule()
        {
            Assert.Equal(DVec3.UnitZ, DVec3.Cross(DVec3.UnitX, DVec3.UnitY));
            Assert.Equal(DVec3.UnitX, DVec3.Cross(DVec3.UnitY, DVec3.UnitZ));
            Assert.Equal(DVec3.UnitY, DVec3.Cross(DVec3.UnitZ, DVec3.UnitX));
        }

        [Fact]
        public void CrossIsAntiCommutative()
        {
            var a = new DVec3(1, 2, 3);
            var b = new DVec3(-4, 0, 2);
            Assert.Equal(-DVec3.Cross(a, b), DVec3.Cross(b, a));
        }

        [Fact]
        public void CrossOfParallelVectorsIsZero()
        {
            var a = new DVec3(2, -3, 5);
            var b = a * 4;
            Assert.Equal(DVec3.Zero, DVec3.Cross(a, b));
        }

        [Fact]
        public void DotOfOrthogonalUnitAxesIsZero()
        {
            Assert.Equal(0, DVec3.Dot(DVec3.UnitX, DVec3.UnitY));
        }

        [Fact]
        public void DotWithSelfEqualsLengthSquared()
        {
            var v = new DVec3(3, -4, 12);
            Assert.Equal(v.LengthSquared, DVec3.Dot(v, v), 9);
        }

        [Fact]
        public void DotMatchesItsDefinition()
        {
            var a = new DVec3(1, 2, 3);
            var b = new DVec3(4, -5, 6);
            Assert.Equal(1 * 4 + 2 * -5 + 3 * 6, DVec3.Dot(a, b));
        }

        [Fact]
        public void LerpAtZeroAndOneReturnsTheEndpoints()
        {
            var a = new DVec3(1, 2, 3);
            var b = new DVec3(10, 20, 30);
            Assert.Equal(a, DVec3.Lerp(a, b, 0));
            Assert.Equal(b, DVec3.Lerp(a, b, 1));
        }

        [Fact]
        public void LerpAtOneHalfReturnsTheMidpoint()
        {
            var a = new DVec3(0, 0, 0);
            var b = new DVec3(10, -20, 30);
            Assert.Equal(new DVec3(5, -10, 15), DVec3.Lerp(a, b, 0.5));
        }

        [Fact]
        public void SwapYZExchangesTheComponents()
        {
            var v = new DVec3(1, 2, 3);
            Assert.Equal(new DVec3(1, 3, 2), v.SwapYZ);
        }

        [Fact]
        public void SwapYZIsItsOwnInverse()
        {
            var v = new DVec3(5, -7, 11);
            Assert.Equal(v, v.SwapYZ.SwapYZ);
        }

        [Fact]
        public void DistanceMatchesTheDifferenceVectorsLength()
        {
            var a = new DVec3(1, 2, 3);
            var b = new DVec3(4, 6, 3);
            Assert.Equal(5.0, DVec3.Distance(a, b), 9);
        }

        [Fact]
        public void DistanceToSelfIsZero()
        {
            var a = new DVec3(7, -2, 9);
            Assert.Equal(0, DVec3.Distance(a, a));
        }

        [Fact]
        public void NormalizedProducesAUnitLengthVector()
        {
            var v = new DVec3(3, 4, 0);
            var n = v.Normalized;
            Assert.Equal(0.6, n.X, 12);
            Assert.Equal(0.8, n.Y, 12);
            Assert.Equal(0.0, n.Z, 12);
        }

        [Fact]
        public void NormalizedOfTheZeroVectorIsZero()
        {
            Assert.Equal(DVec3.Zero, DVec3.Zero.Normalized);
        }

        [Fact]
        public void NormalizedOfAVectorThatUnderflowsToZeroLengthIsZero()
        {
            // 1e-200 squared underflows below the smallest subnormal double, so LengthSquared
            // rounds to exactly 0 even though the vector itself is not the zero vector.
            var tiny = new DVec3(1e-200, 0, 0);
            Assert.Equal(0.0, tiny.LengthSquared);
            Assert.Equal(DVec3.Zero, tiny.Normalized);
        }

        [Fact]
        public void NormalizedOfASmallButRepresentableVectorHasUnitLength()
        {
            var small = new DVec3(1e-150, 1e-150, 1e-150);
            Assert.Equal(1.0, small.Normalized.Length, 9);
        }
    }
}
