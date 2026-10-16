using SolarSystem.Core;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    /// <summary>Unit tests for <see cref="Wreck"/>/<see cref="Wrecks"/> (U.SPC-T39): the lasting hulk and
    /// debris field a destroyed ship leaves behind, independent of the rest of <see cref="War"/>.</summary>
    [TestFixture]
    public class WreckTests
    {
        static Faction MakeFaction() => new Faction("Blue", "BLU", new Rgb(0, 0, 1));

        static BattleUnit MakeUnit(Faction f, Ship ship = null) => new BattleUnit
        {
            Ship = ship,
            Faction = f,
            Pos = new DVec3(3, -2, 1),
            LengthKm = 0.4,
        };

        [Test]
        public void Add_PlacesTheWreckAtTheUnitsBattleLocalPositionOffsetFromTheAnchor()
        {
            var wrecks = new Wrecks();
            var f = MakeFaction();
            var unit = MakeUnit(f);
            var anchor = new DVec3(50000, 0, 0);

            var w = wrecks.Add(unit, body: 3, anchor, jd: 100);

            Assert.That(w.Pos, Is.EqualTo(anchor + unit.Pos));
            Assert.That(w.Body, Is.EqualTo(3));
            Assert.That(w.LengthKm, Is.EqualTo(unit.LengthKm));
            Assert.That(w.Faction, Is.EqualTo(f));
            Assert.That(w.Ship, Is.Null);
            Assert.That(w.CreatedJd, Is.EqualTo(100));
            Assert.That(Expect.Single(wrecks.Items), Is.SameAs(w));
        }

        [Test]
        public void Add_ForAUnitWithAShip_KeepsAReferenceToTheHullItWas()
        {
            var ship = new Ship();
            var wrecks = new Wrecks();
            var w = wrecks.Add(MakeUnit(MakeFaction(), ship), 0, DVec3.Zero, jd: 0);
            Assert.That(w.Ship, Is.SameAs(ship));
        }

        [Test]
        public void Add_WithoutAnExplicitLifetime_UsesTheCollectionsDefault()
        {
            var wrecks = new Wrecks { DefaultLifetimeDays = 12 };
            var w = wrecks.Add(MakeUnit(MakeFaction()), 0, DVec3.Zero, jd: 0);
            Assert.That(w.LifetimeDays, Is.EqualTo(12));
        }

        [Test]
        public void Add_WithAnExplicitLifetime_OverridesTheCollectionsDefault()
        {
            var wrecks = new Wrecks { DefaultLifetimeDays = 12 };
            var w = wrecks.Add(MakeUnit(MakeFaction()), 0, DVec3.Zero, jd: 0, lifetimeDays: 40);
            Assert.That(w.LifetimeDays, Is.EqualTo(40));
        }

        [Test]
        public void Add_CarriesOverTheShipsCargoAsSalvage()
        {
            var ship = new Ship { HasCargo = true, CargoGood = Good.Alloys, CargoTonnes = 40 };
            var wrecks = new Wrecks();

            var w = wrecks.Add(MakeUnit(MakeFaction(), ship), 0, DVec3.Zero, jd: 0);

            var stack = Expect.Single(w.Salvage);
            Assert.That(stack.Good, Is.EqualTo(Good.Alloys));
            Assert.That(stack.Tonnes, Is.EqualTo(40));
        }

        [TestCase(false, Good.Alloys, 40)]
        [TestCase(true, Good.Alloys, 0)]
        public void Add_WithNoCargoAboard_LeavesSalvageEmpty(bool hasCargo, Good good, double tonnes)
        {
            var ship = new Ship { HasCargo = hasCargo, CargoGood = good, CargoTonnes = tonnes };
            var wrecks = new Wrecks();

            var w = wrecks.Add(MakeUnit(MakeFaction(), ship), 0, DVec3.Zero, jd: 0);

            Assert.That(w.Salvage, Is.Empty);
        }

        [Test]
        public void Add_ForAUnitWithNoShip_LeavesSalvageEmpty()
        {
            var wrecks = new Wrecks();
            var w = wrecks.Add(MakeUnit(MakeFaction()), 0, DVec3.Zero, jd: 0);
            Assert.That(w.Salvage, Is.Empty);
        }

        [Test]
        public void AdvanceTo_RemovesAWreckOnceItsLifetimeHasPassed()
        {
            var wrecks = new Wrecks();
            wrecks.Add(MakeUnit(MakeFaction()), 0, DVec3.Zero, jd: 100, lifetimeDays: 10);

            wrecks.AdvanceTo(109);
            Assert.That(wrecks.Items, Has.Exactly(1).Items);

            wrecks.AdvanceTo(110.01);
            Assert.That(wrecks.Items, Is.Empty);
        }

        [Test]
        public void AdvanceTo_OnlyRemovesTheWreckWhoseOwnLifetimeHasRunOut()
        {
            var wrecks = new Wrecks();
            var longLived = wrecks.Add(MakeUnit(MakeFaction()), 0, DVec3.Zero, jd: 0, lifetimeDays: 30);
            wrecks.Add(MakeUnit(MakeFaction()), 0, DVec3.Zero, jd: 0, lifetimeDays: 5);

            wrecks.AdvanceTo(10);

            Assert.That(Expect.Single(wrecks.Items), Is.SameAs(longLived));
        }

        [Test]
        public void TheDebrisFieldSpreadsFromTheHullAndSlowsButNeverPassesItsCap()
        {
            var w = new Wrecks().Add(MakeUnit(MakeFaction()), 0, DVec3.Zero, jd: 100, lifetimeDays: 60);

            double r0 = w.DebrisRadiusKm(100), r1 = w.DebrisRadiusKm(101), r10 = w.DebrisRadiusKm(110), r50 = w.DebrisRadiusKm(150);
            Assert.That(r0, Is.EqualTo(0.5 * w.LengthKm).Within(1e-6));
            Assert.That(r0 < r1 && r1 < r10 && r10 < r50, Is.True, "the cloud keeps spreading");
            Assert.That(r1 - r0 > (r50 - r10) / 40, Is.True, "and spreads fastest at first");
            Assert.That(w.DebrisRadiusKm(10_000) <= Wreck.DebrisMaxLengths * w.LengthKm, Is.True);
        }

        [Test]
        public void EachDebrisPieceKeepsItsHeadingAndStaysInsideTheCloud()
        {
            var w = new Wrecks().Add(MakeUnit(MakeFaction()), 0, DVec3.Zero, jd: 0);

            for (int k = 0; k < w.DebrisPieces; k++)
            {
                var early = w.DebrisOffsetKm(k, 1);
                var late = w.DebrisOffsetKm(k, 5);
                Assert.That(early.Length <= w.DebrisRadiusKm(1) + 1e-9, Is.True);
                Assert.That(late.Length > early.Length, Is.True, "pieces drift outward");
                Assert.That(DVec3.Dot(early.Normalized, late.Normalized), Is.EqualTo(1).Within(1e-6));
                Assert.That(w.DebrisOffsetKm(k, 1), Is.EqualTo(early));
            }
        }

        [Test]
        public void BiggerHullsLeaveMorePiecesWithinBounds()
        {
            var wrecks = new Wrecks();
            var small = wrecks.Add(new BattleUnit { Faction = MakeFaction(), LengthKm = 0.02 }, 0, DVec3.Zero, 0);
            var big = wrecks.Add(new BattleUnit { Faction = MakeFaction(), LengthKm = 0.4 }, 0, DVec3.Zero, 0);
            var huge = wrecks.Add(new BattleUnit { Faction = MakeFaction(), LengthKm = 3 }, 0, DVec3.Zero, 0);

            Assert.That(small.DebrisPieces < big.DebrisPieces, Is.True);
            Assert.That(small.DebrisPieces, Is.InRange(6, 24));
            Assert.That(huge.DebrisPieces, Is.EqualTo(24));
        }

        [Test]
        public void AWreckFadesOnlyOverTheLastFifthOfItsLifetime()
        {
            var w = new Wrecks().Add(MakeUnit(MakeFaction()), 0, DVec3.Zero, jd: 0, lifetimeDays: 20);

            Assert.That(w.Fade(10), Is.EqualTo(1));
            Assert.That(w.Fade(16), Is.EqualTo(1));
            Assert.That(w.Fade(18), Is.EqualTo(0.5).Within(1e-6));
            Assert.That(w.Fade(20), Is.EqualTo(0));
        }

        [Test]
        public void AWreckRemembersItsBattlesAnchorSiteAndLocalSpotAndIsFoundByItsShip()
        {
            var ship = new Ship();
            var site = new LaneSite();
            var wrecks = new Wrecks();
            var unit = MakeUnit(MakeFaction(), ship);
            var anchor = new DVec3(10, 20, 30);

            var w = wrecks.Add(unit, 2, anchor, jd: 0, site: site);

            Assert.That(w.Anchor, Is.EqualTo(anchor));
            Assert.That(w.Local, Is.EqualTo(unit.Pos));
            Assert.That(w.Site, Is.SameAs(site));
            Assert.That(wrecks.Of(ship), Is.SameAs(w));
            Assert.That(wrecks.Of(new Ship()), Is.Null);
        }
    }
}
