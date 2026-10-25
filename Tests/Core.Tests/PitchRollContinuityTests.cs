using System;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>A level hull's roof must not flip as the nose pitches past about 78.5 degrees (U.SPC-T562).</summary>
    [TestFixture]
    public class PitchRollContinuityTests
    {
        static DVec3 Nose(double elevDeg) => new DVec3(Math.Cos(elevDeg * Astro.Deg2Rad), 0, Math.Sin(elevDeg * Astro.Deg2Rad));

        [Test]
        public void ARoofIsContinuousThroughSteepPitch()
        {
            DVec3 prev = default;
            for (double e = 70; e <= 89.9; e += 0.1)
            {
                var f = Nose(e);
                var roof = WeakPatches.LevelRoof(f);
                Assert.That(DVec3.Dot(roof, f), Is.EqualTo(0).Within(1e-9));
                var pilot = PilotState.LevelUp(f);
                Assert.That((pilot - roof).Length, Is.LessThan(1e-9), "pilot and battle agree at " + e);
                if (e > 70) Assert.That((roof - prev).Length, Is.LessThan(0.05), "roof jumped at " + e);
                prev = roof;
            }
        }

        [Test]
        public void AUnitWithoutARoofKeepsItsRightHandSideThroughSteepPitch()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), 2461324.0, 1);
            var s = new Ship { Id = 0, Name = "X", Spec = ShipClassSpec.Of(ShipClass.Battleship), Faction = new Faction("Blue", "BLU", new Rgb(0, 0, 1)), Combat = new ShipCombat(CombatSpec.Of(ShipClass.Battleship), ShipClass.Battleship) };
            var u = b.AddShip(s, 0, DVec3.Zero, Nose(70));
            u.Roof = DVec3.Zero;
            u.Basis(out var r0, out _);
            for (double e = 70; e <= 89.9; e += 0.1)
            {
                u.Forward = Nose(e);
                u.Basis(out var r, out var up);
                Assert.That(DVec3.Dot(r, r0), Is.GreaterThan(0.99), "right flipped at " + e);
                Assert.That(up.Z, Is.GreaterThan(0), "roof toward north at " + e);
            }
        }
    }
}
