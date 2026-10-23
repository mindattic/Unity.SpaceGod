using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using Unity.SpaceGod.Core;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The render budget at universe scale (U.SPC-T415): every render class degrades by apparent size, full model,
    /// simplified model, icon or impostor, folded marks, and nothing below a pixel.
    /// </summary>
    [TestFixture]
    public class RenderLodTests
    {
        [Test]
        public void AShipGoesFromFullModelToSimplifiedToIconAndIsNeverCulled()
        {
            // Model below 5 px, simplified below 48, cull below 1.
            LodTier T(double px) => RenderLod.Tier(px, 5, LodFar.Icon, true, 48, 1);
            Assert.That(T(200), Is.EqualTo(LodTier.Full));
            Assert.That(T(48), Is.EqualTo(LodTier.Full), "exactly at the simplify point it keeps its detail");
            Assert.That(T(47.9), Is.EqualTo(LodTier.Simplified));
            Assert.That(T(5), Is.EqualTo(LodTier.Simplified), "exactly at the swap point it is still a model");
            Assert.That(T(4.9), Is.EqualTo(LodTier.Icon));
            Assert.That(T(0.001), Is.EqualTo(LodTier.Icon), "a ship keeps its icon however small: it folds into a mark instead");
            Assert.That(T(0), Is.EqualTo(LodTier.Icon));
        }

        [Test]
        public void SmallCraftShowAnImpostorThenNothingBelowAPixel()
        {
            LodTier T(double px) => RenderLod.Tier(px, 3, LodFar.Impostor, false, 48, 1);
            Assert.That(T(10), Is.EqualTo(LodTier.Full), "no simpler model: straight from full to its glow");
            Assert.That(T(2), Is.EqualTo(LodTier.Icon));
            Assert.That(T(1), Is.EqualTo(LodTier.Icon));
            Assert.That(T(0.99), Is.EqualTo(LodTier.Culled));
        }

        [Test]
        public void AThingWithNoIconIsCulledBelowItsModelAndNeverDrawnBelowAPixel()
        {
            LodTier T(double px, double modelBelow, double cull) => RenderLod.Tier(px, modelBelow, LodFar.None, false, 48, cull);
            Assert.That(T(4, 4, 1), Is.EqualTo(LodTier.Full));
            Assert.That(T(3.9, 4, 1), Is.EqualTo(LodTier.Culled));
            Assert.That(T(1, 0, 1), Is.EqualTo(LodTier.Full), "a wreck is drawn down to a pixel");
            Assert.That(T(0.5, 0, 1), Is.EqualTo(LodTier.Culled), "and not below one");
            Assert.That(T(0.5, 0, 0), Is.EqualTo(LodTier.Full), "a cull size of 0 culls nothing");
            Assert.That(T(3, 2, 4), Is.EqualTo(LodTier.Culled), "the cull size wins over a lower model swap point");
        }

        [Test]
        public void EveryBuiltInClassIsRegisteredAndShipsAgreeWithShipLod()
        {
            var keys = RenderLod.Classes.Select(c => c.Key).ToList();
            Assert.That(keys, Is.SupersetOf(new[] { "ship", "body", "gate", "structure", "ring", "platform", "fighter", "pod", "wreck" }));
            Assert.That(keys, Is.Unique);
            Assert.That(RenderLod.Find("ship"), Is.SameAs(RenderLod.Ship));
            Assert.That(RenderLod.Find("nothing"), Is.Null);
            Assert.That(RenderLod.Ship.Far, Is.EqualTo(LodFar.Icon));
            Assert.That(RenderLod.Ship.Simplifies, Is.True);
            Assert.That(RenderLod.Ship.ModelBelowPx, Is.EqualTo(ShipLod.IconBelowPx.Value), "the ship's own swap point setting");
            Assert.That(RenderLod.Body.Far, Is.EqualTo(LodFar.Icon));
            Assert.That(RenderLod.Fighter.Far, Is.EqualTo(LodFar.Impostor));
            Assert.That(RenderLod.Wreck.Far, Is.EqualTo(LodFar.None));

            double swap = ShipLod.IconBelowPx.Value;
            Assert.That(ShipLod.DrawsModel(swap + 1), Is.True);
            Assert.That(ShipLod.DrawsModel(swap - 0.5), Is.False);
            Assert.That(ShipLod.DrawsFullModel(RenderLod.SimplifyBelowPx.Value + 1), Is.True);
            Assert.That(ShipLod.DrawsFullModel(RenderLod.SimplifyBelowPx.Value - 1), Is.False, "a model under the simplify point is the hull alone");

            Assert.Throws<System.ArgumentException>(() => RenderLod.Register(new LodClass("ship", "Another ship", () => 1, LodFar.Icon, false)), "a key is registered once");
        }

        [Test]
        public void ANewRenderClassJoinsTheSchemeByRegistering()
        {
            var probe = RenderLod.Find("test.probe") ?? RenderLod.Register(new LodClass("test.probe", "Probe", () => 6, LodFar.Impostor, false));
            Assert.That(RenderLod.Classes, Does.Contain(probe));
            Assert.That(RenderLod.Tier(probe, 10), Is.EqualTo(LodTier.Full));
            Assert.That(RenderLod.Tier(probe, 5), Is.EqualTo(LodTier.Icon));
            Assert.That(RenderLod.Tier(probe, 0.01), Is.EqualTo(LodTier.Culled));
        }

        [Test]
        public void TheBudgetIsSettingsInTheRenderBudgetGroup()
        {
            foreach (var (s, key) in new[] { (RenderLod.SimplifyBelowPx, "lod.simplifyPx"), (RenderLod.CullBelowPx, "lod.cullPx"), (RenderLod.AggregatePx, "lod.aggregatePx") })
            {
                Assert.That(s.Group, Is.EqualTo(SettingGroups.RenderBudget));
                Assert.That(SettingsRegistry.Default.Find(key), Is.SameAs(s));
            }
            Assert.That(RenderLod.CullBelowPx.Default, Is.EqualTo(1), "nothing below a pixel");
            Assert.That(RenderLod.AggregatePx.Min, Is.EqualTo(0), "folding can be turned off");
            Assert.That(RenderLod.SimplifyBelowPx.Default, Is.GreaterThan(ShipLod.IconBelowPx.Default));
        }

        [Test]
        public void IconsOfOneSideThatCrowdFoldIntoOneMarkLedByTheBiggestHull()
        {
            var marks = new List<LodMark>
            {
                new LodMark(100, 100, 1, 1),   // 0 escort
                new LodMark(104, 102, 1, 5),   // 1 flagship
                new LodMark(98, 97, 1, 1),     // 2 escort
                new LodMark(102, 100, 2, 3),   // 3 another side, right on top: kept apart
                new LodMark(300, 300, 1, 2),   // 4 far off: its own mark
            };
            var folded = RenderLod.Aggregate(marks, 14);
            Assert.That(folded, Has.Count.EqualTo(3));
            var fleet = folded.Single(c => c.Members.Contains(1));
            Assert.That(fleet.Representative, Is.EqualTo(1), "the biggest hull stands for the fleet");
            Assert.That(fleet.Members, Is.EquivalentTo(new[] { 1, 0, 2 }));
            Assert.That(fleet.Members[0], Is.EqualTo(1), "its representative first");
            Assert.That(fleet.Tier, Is.EqualTo(LodTier.Aggregate));
            Assert.That((fleet.X, fleet.Y), Is.EqualTo((104.0, 102.0)), "the mark sits where its representative is");
            Assert.That(folded.Single(c => c.Members.Contains(3)).Count, Is.EqualTo(1));
            Assert.That(folded.Single(c => c.Members.Contains(4)).Tier, Is.EqualTo(LodTier.Icon));
            Assert.That(RenderLod.CountLabel(3), Is.EqualTo("×3"));
            Assert.That(RenderLod.CountLabel(1), Is.Empty);
        }

        [Test]
        public void APinnedIconIsNeverFoldedAndARadiusOfZeroFoldsNothing()
        {
            var marks = new List<LodMark>
            {
                new LodMark(100, 100, 1, 1, pinned: true),
                new LodMark(101, 100, 1, 9),
                new LodMark(102, 100, 1, 1),
            };
            var folded = RenderLod.Aggregate(marks, 14);
            Assert.That(folded.Single(c => c.Members.Contains(0)).Count, Is.EqualTo(1), "the selected ship keeps its own icon");
            Assert.That(folded.Single(c => c.Members.Contains(1)).Members, Is.EquivalentTo(new[] { 1, 2 }), "the rest still fold");

            Assert.That(RenderLod.Aggregate(marks, 0), Has.Count.EqualTo(3));
            Assert.That(RenderLod.Aggregate(new List<LodMark>(), 14), Is.Empty);
            Assert.That(RenderLod.Aggregate(null, 14), Is.Empty);
        }

        [Test]
        public void ThousandsOfIconsFoldQuicklyIntoAHandfulOfMarks()
        {
            // Five thousand ships of four sides, crowded round twenty ports on a 1920x1080 screen.
            var marks = new List<LodMark>();
            var rng = new System.Random(7);
            for (int i = 0; i < 5000; i++)
            {
                int port = rng.Next(20);
                double x = 100 + port % 5 * 400 + rng.NextDouble() * 30, y = 100 + port / 5 * 250 + rng.NextDouble() * 30;
                marks.Add(new LodMark(x, y, rng.Next(4), rng.NextDouble()));
            }
            RenderLod.Aggregate(marks, 14);
            var sw = Stopwatch.StartNew();
            var folded = RenderLod.Aggregate(marks, 14);
            sw.Stop();
            Assert.That(folded.Sum(c => c.Count), Is.EqualTo(5000), "every icon is in exactly one mark");
            Assert.That(folded.SelectMany(c => c.Members).Distinct().Count(), Is.EqualTo(5000));
            Assert.That(folded.Count, Is.LessThan(1000), "a crowd is drawn as a few marks, not thousands of icons");
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(100), "folding fits well inside a frame");
        }

        [Test]
        public void AnotherSystemIsOneMarkSayingHowManyShipsItHolds()
        {
            Assert.That(RenderLod.SystemShipsLabel(0), Is.Empty);
            Assert.That(RenderLod.SystemShipsLabel(1), Is.EqualTo("1 ship"));
            Assert.That(RenderLod.SystemShipsLabel(214), Is.EqualTo("214 ships"));
            Assert.That(RenderLod.LiveShips(null), Is.EqualTo(0));

            var g = Galaxy.CreateDefault(2461324.0);
            var sol = g.Find("Sol System");
            int live = sol.World.Fleet.Ships.Count(s => !s.Destroyed);
            Assert.That(live, Is.GreaterThan(0));
            Assert.That(RenderLod.LiveShips(sol.World.Fleet), Is.EqualTo(live));
        }
    }
}
