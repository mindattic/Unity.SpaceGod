using SolarSystem.Core;
using Xunit;

namespace SolarSystem.Core.Tests
{
    /// <summary>Unit tests for <see cref="Wreck"/>/<see cref="Wrecks"/> (U.SPC-T39): the lasting hulk and
    /// debris field a destroyed ship leaves behind, independent of the rest of <see cref="War"/>.</summary>
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

        [Fact]
        public void Add_PlacesTheWreckAtTheUnitsBattleLocalPositionOffsetFromTheAnchor()
        {
            var wrecks = new Wrecks();
            var f = MakeFaction();
            var unit = MakeUnit(f);
            var anchor = new DVec3(50000, 0, 0);

            var w = wrecks.Add(unit, body: 3, anchor, jd: 100);

            Assert.Equal(anchor + unit.Pos, w.Pos);
            Assert.Equal(3, w.Body);
            Assert.Equal(unit.LengthKm, w.LengthKm);
            Assert.Equal(f, w.Faction);
            Assert.Null(w.Ship);
            Assert.Equal(100, w.CreatedJd);
            Assert.Same(w, Assert.Single(wrecks.Items));
        }

        [Fact]
        public void Add_ForAUnitWithAShip_KeepsAReferenceToTheHullItWas()
        {
            var ship = new Ship();
            var wrecks = new Wrecks();
            var w = wrecks.Add(MakeUnit(MakeFaction(), ship), 0, DVec3.Zero, jd: 0);
            Assert.Same(ship, w.Ship);
        }

        [Fact]
        public void Add_WithoutAnExplicitLifetime_UsesTheCollectionsDefault()
        {
            var wrecks = new Wrecks { DefaultLifetimeDays = 12 };
            var w = wrecks.Add(MakeUnit(MakeFaction()), 0, DVec3.Zero, jd: 0);
            Assert.Equal(12, w.LifetimeDays);
        }

        [Fact]
        public void Add_WithAnExplicitLifetime_OverridesTheCollectionsDefault()
        {
            var wrecks = new Wrecks { DefaultLifetimeDays = 12 };
            var w = wrecks.Add(MakeUnit(MakeFaction()), 0, DVec3.Zero, jd: 0, lifetimeDays: 40);
            Assert.Equal(40, w.LifetimeDays);
        }

        [Fact]
        public void Add_CarriesOverTheShipsCargoAsSalvage()
        {
            var ship = new Ship { HasCargo = true, CargoGood = Good.Alloys, CargoTonnes = 40 };
            var wrecks = new Wrecks();

            var w = wrecks.Add(MakeUnit(MakeFaction(), ship), 0, DVec3.Zero, jd: 0);

            var stack = Assert.Single(w.Salvage);
            Assert.Equal(Good.Alloys, stack.Good);
            Assert.Equal(40, stack.Tonnes);
        }

        [Theory]
        [InlineData(false, Good.Alloys, 40)]
        [InlineData(true, Good.Alloys, 0)]
        public void Add_WithNoCargoAboard_LeavesSalvageEmpty(bool hasCargo, Good good, double tonnes)
        {
            var ship = new Ship { HasCargo = hasCargo, CargoGood = good, CargoTonnes = tonnes };
            var wrecks = new Wrecks();

            var w = wrecks.Add(MakeUnit(MakeFaction(), ship), 0, DVec3.Zero, jd: 0);

            Assert.Empty(w.Salvage);
        }

        [Fact]
        public void Add_ForAUnitWithNoShip_LeavesSalvageEmpty()
        {
            var wrecks = new Wrecks();
            var w = wrecks.Add(MakeUnit(MakeFaction()), 0, DVec3.Zero, jd: 0);
            Assert.Empty(w.Salvage);
        }

        [Fact]
        public void AdvanceTo_RemovesAWreckOnceItsLifetimeHasPassed()
        {
            var wrecks = new Wrecks();
            wrecks.Add(MakeUnit(MakeFaction()), 0, DVec3.Zero, jd: 100, lifetimeDays: 10);

            wrecks.AdvanceTo(109);
            Assert.Single(wrecks.Items);

            wrecks.AdvanceTo(110.01);
            Assert.Empty(wrecks.Items);
        }

        [Fact]
        public void AdvanceTo_OnlyRemovesTheWreckWhoseOwnLifetimeHasRunOut()
        {
            var wrecks = new Wrecks();
            var longLived = wrecks.Add(MakeUnit(MakeFaction()), 0, DVec3.Zero, jd: 0, lifetimeDays: 30);
            wrecks.Add(MakeUnit(MakeFaction()), 0, DVec3.Zero, jd: 0, lifetimeDays: 5);

            wrecks.AdvanceTo(10);

            Assert.Same(longLived, Assert.Single(wrecks.Items));
        }

        [Fact]
        public void TheDebrisFieldSpreadsFromTheHullAndSlowsButNeverPassesItsCap()
        {
            var w = new Wrecks().Add(MakeUnit(MakeFaction()), 0, DVec3.Zero, jd: 100, lifetimeDays: 60);

            double r0 = w.DebrisRadiusKm(100), r1 = w.DebrisRadiusKm(101), r10 = w.DebrisRadiusKm(110), r50 = w.DebrisRadiusKm(150);
            Assert.Equal(0.5 * w.LengthKm, r0, 6);
            Assert.True(r0 < r1 && r1 < r10 && r10 < r50, "the cloud keeps spreading");
            Assert.True(r1 - r0 > (r50 - r10) / 40, "and spreads fastest at first");
            Assert.True(w.DebrisRadiusKm(10_000) <= Wreck.DebrisMaxLengths * w.LengthKm);
        }

        [Fact]
        public void EachDebrisPieceKeepsItsHeadingAndStaysInsideTheCloud()
        {
            var w = new Wrecks().Add(MakeUnit(MakeFaction()), 0, DVec3.Zero, jd: 0);

            for (int k = 0; k < w.DebrisPieces; k++)
            {
                var early = w.DebrisOffsetKm(k, 1);
                var late = w.DebrisOffsetKm(k, 5);
                Assert.True(early.Length <= w.DebrisRadiusKm(1) + 1e-9);
                Assert.True(late.Length > early.Length, "pieces drift outward");
                Assert.Equal(1, DVec3.Dot(early.Normalized, late.Normalized), 6);
                Assert.Equal(early, w.DebrisOffsetKm(k, 1));
            }
        }

        [Fact]
        public void BiggerHullsLeaveMorePiecesWithinBounds()
        {
            var wrecks = new Wrecks();
            var small = wrecks.Add(new BattleUnit { Faction = MakeFaction(), LengthKm = 0.02 }, 0, DVec3.Zero, 0);
            var big = wrecks.Add(new BattleUnit { Faction = MakeFaction(), LengthKm = 0.4 }, 0, DVec3.Zero, 0);
            var huge = wrecks.Add(new BattleUnit { Faction = MakeFaction(), LengthKm = 3 }, 0, DVec3.Zero, 0);

            Assert.True(small.DebrisPieces < big.DebrisPieces);
            Assert.InRange(small.DebrisPieces, 6, 24);
            Assert.Equal(24, huge.DebrisPieces);
        }

        [Fact]
        public void AWreckFadesOnlyOverTheLastFifthOfItsLifetime()
        {
            var w = new Wrecks().Add(MakeUnit(MakeFaction()), 0, DVec3.Zero, jd: 0, lifetimeDays: 20);

            Assert.Equal(1, w.Fade(10));
            Assert.Equal(1, w.Fade(16));
            Assert.Equal(0.5, w.Fade(18), 6);
            Assert.Equal(0, w.Fade(20));
        }

        [Fact]
        public void AWreckRemembersItsBattlesAnchorSiteAndLocalSpotAndIsFoundByItsShip()
        {
            var ship = new Ship();
            var site = new LaneSite();
            var wrecks = new Wrecks();
            var unit = MakeUnit(MakeFaction(), ship);
            var anchor = new DVec3(10, 20, 30);

            var w = wrecks.Add(unit, 2, anchor, jd: 0, site: site);

            Assert.Equal(anchor, w.Anchor);
            Assert.Equal(unit.Pos, w.Local);
            Assert.Same(site, w.Site);
            Assert.Same(w, wrecks.Of(ship));
            Assert.Null(wrecks.Of(new Ship()));
        }
    }
}
