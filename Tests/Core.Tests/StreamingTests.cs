using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>Seamless streaming (U.SPC-T414): what is loaded, in what order, within what budget, and how a gate covers the rest.</summary>
    [TestFixture]
    public class StreamingTests
    {
        const double Ppu = 1000, ModelPx = 5;

        static StreamView View(DVec3 at, DVec3 velocity, DVec3 heading, double lead = 5) =>
            new StreamView(at, velocity, heading, Ppu, ModelPx, lead);

        [Test]
        public void SomethingBigOnScreenIsWantedAndSomethingFarOffIsNot()
        {
            var view = View(DVec3.Zero, DVec3.Zero, DVec3.UnitX);
            // 1 unit long, 100 away: 10 px, twice the model size.
            Assert.That(Streaming.Urgency(view, new DVec3(0, 100, 0), 1), Is.EqualTo(2).Within(1e-9));
            Assert.That(Streaming.WantsLoaded(Streaming.Urgency(view, new DVec3(0, 100, 0), 1), false), Is.True);
            // 10,000 away: a tenth of a pixel.
            Assert.That(Streaming.WantsLoaded(Streaming.Urgency(view, new DVec3(0, 10000, 0), 1), false), Is.False);
        }

        [Test]
        public void WhatLiesAheadOfTheViewCountsBiggerThanWhatLiesBehind()
        {
            Assert.That(Streaming.HeadingWeight(DVec3.UnitX, new DVec3(5, 0, 0)), Is.EqualTo(Streaming.AheadBoost).Within(1e-12));
            Assert.That(Streaming.HeadingWeight(DVec3.UnitX, new DVec3(-5, 0, 0)), Is.EqualTo(1));
            Assert.That(Streaming.HeadingWeight(DVec3.UnitX, new DVec3(0, 5, 0)), Is.EqualTo(1).Within(1e-12));
            var view = View(DVec3.Zero, DVec3.Zero, DVec3.UnitX);
            double ahead = Streaming.Urgency(view, new DVec3(1000, 0, 0), 1);
            double behind = Streaming.Urgency(view, new DVec3(-1000, 0, 0), 1);
            Assert.That(ahead, Is.GreaterThan(behind * 2));
        }

        [Test]
        public void WhatTheViewIsFlyingTowardIsAskedForBeforeItIsReached()
        {
            // At 1000 units/s, something 4000 ahead is reached within the 5 s lead: wanted now, though tiny today.
            var still = View(DVec3.Zero, DVec3.Zero, DVec3.UnitX);
            var flying = View(DVec3.Zero, new DVec3(1000, 0, 0), DVec3.UnitX);
            var item = new DVec3(4000, 50, 0);
            Assert.That(Streaming.WantsLoaded(Streaming.Urgency(still, item, 1), false), Is.False);
            Assert.That(Streaming.WantsLoaded(Streaming.Urgency(flying, item, 1), false), Is.True);
            // Flying away from it, it is not.
            var leaving = View(DVec3.Zero, new DVec3(-1000, 0, 0), DVec3.UnitX);
            Assert.That(Streaming.WantsLoaded(Streaming.Urgency(leaving, item, 1), false), Is.False);
        }

        [Test]
        public void TheGapBetweenLoadingAndLettingGoKeepsTheEdgeFromFlickering()
        {
            double between = (Streaming.LoadShare + Streaming.UnloadShare) / 2;
            Assert.That(Streaming.WantsLoaded(between, false), Is.False, "not yet asked for");
            Assert.That(Streaming.WantsLoaded(between, true), Is.True, "but kept once in");
        }

        [Test]
        public void ThePlanLoadsTheMostUrgentFirstTheAlwaysWantedAheadOfAllAndLetsGoOfTheUnwanted()
        {
            var view = View(DVec3.Zero, DVec3.Zero, DVec3.UnitX);
            var items = new List<StreamItem>
            {
                new StreamItem(0, new DVec3(0, 150, 0), 1, false),   // 6.7 px
                new StreamItem(1, new DVec3(0, 50, 0), 1, false),    // 20 px
                new StreamItem(2, new DVec3(0, 1e6, 0), 1, false, always: true), // the ship at the helm, far or not
                new StreamItem(3, new DVec3(0, 1e6, 0), 1, true),    // dwindled away
                new StreamItem(4, new DVec3(0, 1e6, 0), 1, false),   // too far to bother with
                new StreamItem(5, new DVec3(0, 60, 0), 1, true),     // already in, still wanted
            };
            var load = new List<int>();
            var unload = new List<int>();
            Streaming.Plan(items, view, load, unload);
            Assert.That(load, Is.EqualTo(new[] { 2, 1, 0 }));
            Assert.That(unload, Is.EqualTo(new[] { 3 }));
        }

        [Test]
        public void AFramesWorkStopsOnceItsBudgetIsSpentButAlwaysDoesSomething()
        {
            double now = 0;
            var order = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 };
            var done = new List<int>();
            // Each piece of work takes 1 ms; the budget is 3.
            int n = Streaming.Spend(order, i => { now += 1; done.Add(i); return true; }, 3, () => now);
            Assert.That(n, Is.EqualTo(3));
            Assert.That(done, Is.EqualTo(new[] { 0, 1, 2 }));

            // One piece far over budget still gets through, alone.
            now = 0;
            done.Clear();
            n = Streaming.Spend(order, i => { now += 50; done.Add(i); return true; }, 3, () => now);
            Assert.That(n, Is.EqualTo(1));

            // Work not ready yet (its asset still on its way) is skipped, not counted, and the next is tried.
            now = 0;
            done.Clear();
            n = Streaming.Spend(order, i => { now += 0.5; if (i % 2 == 0) return false; done.Add(i); return true; }, 3, () => now);
            Assert.That(done, Is.EqualTo(new[] { 1, 3, 5 }));
            Assert.That(n, Is.EqualTo(3));
        }

        /// <summary>
        /// Arriving by plain flight never waits: flying straight into a crowd of ships at speed, a frame's budget building
        /// only a couple of models, every ship is built before it is ever big enough on screen to be drawn as one.
        /// </summary>
        [Test]
        public void FlyingIntoACrowdEveryModelIsInBeforeItIsBigEnoughToShow()
        {
            const double frame = 1 / 60.0, speed = 2000;
            const int perFrame = 2;
            var rng = new Random(7);
            var positions = new List<DVec3>();
            for (int i = 0; i < 120; i++)
                positions.Add(new DVec3(20000 + rng.NextDouble() * 20000, (rng.NextDouble() - 0.5) * 600, (rng.NextDouble() - 0.5) * 600));
            var loaded = new bool[positions.Count];
            var at = DVec3.Zero;
            var velocity = new DVec3(speed, 0, 0);
            var items = new List<StreamItem>();
            var load = new List<int>();
            var unload = new List<int>();
            for (int step = 0; step < 60 * 25; step++)
            {
                var view = View(at, velocity, DVec3.UnitX, Streaming.LeadSeconds.Default);
                items.Clear();
                for (int i = 0; i < positions.Count; i++) items.Add(new StreamItem(i, positions[i], 1, loaded[i]));
                Streaming.Plan(items, view, load, unload);
                int built = 0;
                foreach (int i in load)
                {
                    if (built++ >= perFrame) break;
                    loaded[i] = true;
                }
                foreach (int i in unload) loaded[i] = false;
                at += velocity * frame;
                for (int i = 0; i < positions.Count; i++)
                {
                    double px = ShipLod.ApparentPx(1, (positions[i] - at).Length, Ppu);
                    if (ShipLod.DrawsModel(px, ModelPx))
                        Assert.That(loaded[i], Is.True, "ship " + i + " shown before its model was in, at step " + step);
                }
            }
        }

        [Test]
        public void TheGatesFarSystemIsWarmedWhenTheViewIsNearOrClosingOnIt()
        {
            var gate = new DVec3(1e6, 0, 0);
            Assert.That(Streaming.ShouldWarm(new DVec3(1e6 - 10, 0, 0), DVec3.Zero, gate, 100), Is.True, "beside it");
            Assert.That(Streaming.ShouldWarm(DVec3.Zero, DVec3.Zero, gate, 100), Is.False, "far and still");
            Assert.That(Streaming.ShouldWarm(DVec3.Zero, new DVec3(1e5, 0, 0), gate, 100), Is.True, "there in ten seconds");
            Assert.That(Streaming.ShouldWarm(DVec3.Zero, new DVec3(-1e5, 0, 0), gate, 100), Is.False, "flying away");
        }

        [Test]
        public void AGateTunnelPlaysItsOwnLengthWhenTheFarSideIsAlreadyIn()
        {
            var cover = new GateCover();
            cover.Begin(GateJump.TunnelSeconds, GateJump.HoldAt, 8);
            double played = 0;
            while (cover.Active && played < 100)
            {
                cover.Advance(0.05, false);
                played += 0.05;
            }
            Assert.That(played, Is.EqualTo(GateJump.TunnelSeconds).Within(0.051));
            Assert.That(cover.Held, Is.EqualTo(0));
            Assert.That(cover.Done, Is.True);
        }

        [Test]
        public void AGateTunnelIsHeldAtItsHeightUntilTheFarSideHasLoaded()
        {
            var cover = new GateCover();
            cover.Begin(GateJump.TunnelSeconds, GateJump.HoldAt, 8);
            double elapsed = 0;
            // The far side takes 3 s to load.
            while (cover.Active && elapsed < 100)
            {
                bool loading = elapsed < 3;
                cover.Advance(0.02, loading);
                elapsed += 0.02;
                if (loading && elapsed > GateJump.TunnelSeconds * GateJump.HoldAt + 0.05)
                {
                    Assert.That(cover.T, Is.EqualTo(GateJump.HoldAt).Within(1e-9), "held at its height");
                    Assert.That(cover.Holding, Is.True);
                }
            }
            // The rest of the tunnel plays out once loading is done: never cut short, never ending before it.
            Assert.That(elapsed, Is.GreaterThanOrEqualTo(3 + GateJump.TunnelSeconds * (1 - GateJump.HoldAt) - 0.03));
            Assert.That(elapsed, Is.LessThan(3 + GateJump.TunnelSeconds * (1 - GateJump.HoldAt) + 0.1));
            // While held, the effect stands at its height: warp at full, no flash.
            Assert.That(GateJump.Warp(GateJump.Phase.Tunnel, GateJump.HoldAt), Is.EqualTo(1).Within(1e-9));
            Assert.That(GateJump.Flash(GateJump.Phase.Tunnel, GateJump.HoldAt), Is.EqualTo(0));
        }

        [Test]
        public void AGateTunnelIsNeverHeldPastItsCap()
        {
            var cover = new GateCover();
            cover.Begin(GateJump.TunnelSeconds, GateJump.HoldAt, 2);
            double elapsed = 0;
            while (cover.Active && elapsed < 100)
            {
                cover.Advance(0.1, true);
                elapsed += 0.1;
            }
            Assert.That(cover.Held, Is.EqualTo(2).Within(1e-9));
            Assert.That(elapsed, Is.EqualTo(GateJump.TunnelSeconds + 2).Within(0.11));
        }

        [Test]
        public void LoadingThatStartsPastTheHeightDoesNotFreezeTheTunnelsEnding()
        {
            var cover = new GateCover();
            cover.Begin(1, 0.5, 8);
            cover.Advance(0.8, false);
            cover.Advance(0.1, true);
            Assert.That(cover.Held, Is.EqualTo(0));
            Assert.That(cover.T, Is.EqualTo(0.9).Within(1e-9));
        }

        [Test]
        public void TheTunnelFoldsShutJustBeforeTheFarGateAndOpensWithItsFlare()
        {
            Assert.That(GateJump.Fold(GateJump.Phase.Tunnel, 0.5), Is.EqualTo(0));
            Assert.That(GateJump.Fold(GateJump.Phase.Tunnel, 0.92), Is.GreaterThan(0.9));
            Assert.That(GateJump.Fold(GateJump.Phase.Tunnel, 1), Is.EqualTo(0).Within(1e-9));
            Assert.That(GateJump.Fold(GateJump.Phase.Emerge, 0), Is.EqualTo(0));
            // The view narrows in the fold, past its usual width.
            Assert.That(GateJump.FovKick(GateJump.Phase.Tunnel, 0.92), Is.LessThan(0));
        }

        [Test]
        public void TheHelmsFlightThroughARingGlowsAndFoldsThenClears()
        {
            Assert.That(GateCover.HelmGlow(0), Is.EqualTo(0));
            Assert.That(GateCover.HelmGlow(GateCover.HelmHoldAt), Is.EqualTo(1).Within(1e-9));
            Assert.That(GateCover.HelmGlow(1), Is.EqualTo(0).Within(1e-9));
            Assert.That(GateCover.HelmFold(0), Is.EqualTo(0));
            Assert.That(GateCover.HelmFold(0.5), Is.EqualTo(1).Within(1e-9));
            Assert.That(GateCover.HelmFold(1), Is.EqualTo(0).Within(1e-9));
        }

        [Test]
        public void TheStreamingSettingsAreInTheMenu()
        {
            Assert.That(Streaming.FrameBudgetMs.Group, Is.EqualTo(SettingGroups.Streaming));
            Assert.That(Streaming.LeadSeconds.Group, Is.EqualTo(SettingGroups.Streaming));
            Assert.That(Streaming.GateCoverMaxSeconds.Group, Is.EqualTo(SettingGroups.Streaming));
            Assert.That(SettingsRegistry.Default.Find("stream.leadSeconds"), Is.SameAs(Streaming.LeadSeconds));
        }
    }
}
