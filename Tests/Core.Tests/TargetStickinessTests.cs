using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>U.SPC-T560: a hull keeps its current mark unless a rival is clearly better, so its heading settles.</summary>
    [TestFixture]
    public class TargetStickinessTests
    {
        const double StartJd = 2461324.0;

        static readonly Faction Blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
        static readonly Faction Red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };

        static Ship Hull(ShipClass c, Faction f, int id) => new Ship
        {
            Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c),
        };

        [Test]
        public void ACruiserBetweenTwoEquidistantEnemiesDoesNotFlipFlop()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed);
                var me = b.AddShip(Hull(ShipClass.Cruiser, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
                b.AddShip(Hull(ShipClass.Cruiser, Red, 1), 1, new DVec3(20, 0, 0), -DVec3.UnitX);
                b.AddShip(Hull(ShipClass.Cruiser, Red, 2), 1, new DVec3(0, 20, 0), -DVec3.UnitX);
                me.Target = b.PickTarget(me);
                int switches = 0;
                for (int i = 0; i < 40; i++)
                {
                    var t = b.PickTarget(me);
                    if (t != me.Target) switches++;
                    me.Target = t;
                }
                Assert.That(switches, Is.LessThanOrEqualTo(2), $"seed {seed}");
            }
        }

        [Test]
        public void ItStillSwitchesToAClearlyBetterTarget()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 1);
            var me = b.AddShip(Hull(ShipClass.Cruiser, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            var far = b.AddShip(Hull(ShipClass.Cruiser, Red, 1), 1, new DVec3(200, 0, 0), -DVec3.UnitX);
            var near = b.AddShip(Hull(ShipClass.Cruiser, Red, 2), 1, new DVec3(0, 10, 0), -DVec3.UnitX);
            me.Target = far;
            Assert.That(b.PickTarget(me), Is.SameAs(near));
        }
    }
}
