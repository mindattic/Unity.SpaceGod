using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Shared.Core;
using Unity.SpaceGod.Core;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>The settings registry (U.SPC-T431): declared tunables, grouped, searchable, reset and kept per profile.</summary>
    [TestFixture]
    public class SettingsRegistryTests
    {
        [Test]
        public void DeclaredSettingsAreGroupedInTheMenusOrderWithUnknownGroupsAfterByName()
        {
            var reg = new SettingsRegistry();
            new ToggleSetting("a.x", "Mods", "X", true, registry: reg);
            new RangeSetting("a.y", SettingGroups.Controls, "Y", 1, 0, 2, registry: reg);
            new ToggleSetting("a.z", SettingGroups.Graphics, "Z", false, registry: reg);
            new ToggleSetting("a.w", "Accessibility", "W", false, registry: reg);
            new ToggleSetting("a.v", SettingGroups.Graphics, "V", false, registry: reg);

            Assert.That(reg.Groups(), Is.EqualTo(new[] { SettingGroups.Graphics, SettingGroups.Controls, "Accessibility", "Mods" }));
            Assert.That(reg.InGroup(SettingGroups.Graphics).Select(s => s.Key), Is.EqualTo(new[] { "a.z", "a.v" }), "declaration order within a group");
            Assert.That(reg.Find("a.y"), Is.SameAs(reg.InGroup(SettingGroups.Controls)[0]));
            Assert.That(reg.Find("nope"), Is.Null);
        }

        [Test]
        public void TwoSettingsCannotShareAKeyAndAKeyMustBeAFileName()
        {
            var reg = new SettingsRegistry();
            new ToggleSetting("same", "G", "One", true, registry: reg);
            Assert.Throws<InvalidOperationException>(() => new ToggleSetting("same", "G", "Two", true, registry: reg));
            foreach (var bad in new[] { "", "a/b", "a b", "..", ".hidden", "trailing.", "x:y", "quote\"" })
                Assert.Throws<ArgumentException>(() => new ToggleSetting(bad, "G", "Bad", true, registry: reg), bad);
        }

        [Test]
        public void RangesClampAndSnapToTheirStepsAndChoicesStayInTheirList()
        {
            var reg = new SettingsRegistry();
            var r = new RangeSetting("r", "G", "R", 4000, 500, 8000, 500, registry: reg);
            r.Set(4260);
            Assert.That(r.Value, Is.EqualTo(4500));
            r.Set(-3);
            Assert.That(r.Value, Is.EqualTo(500));
            r.Set(1e9);
            Assert.That(r.Value, Is.EqualTo(8000));
            r.SetFraction(0.5);
            Assert.That(r.Value, Is.EqualTo(4500), "halfway along 500-8000 is 4250, on the nearest step");
            r.Set(double.NaN);
            Assert.That(r.Value, Is.EqualTo(4000), "nonsense falls back to the default");

            var fine = new RangeSetting("f", "G", "F", 1, 0.25, 4, 0.05, registry: reg);
            fine.Set(1.234);
            Assert.That(fine.Value, Is.EqualTo(1.25).Within(1e-12));

            var c = new ChoiceSetting("c", "G", "C", new[] { "Low", "Mid", "High" }, 1, registry: reg);
            c.Set(7);
            Assert.That(c.Selected, Is.EqualTo("High"));
            c.Cycle(1);
            Assert.That(c.Selected, Is.EqualTo("Low"), "cycling wraps round");
            c.Cycle(-1);
            Assert.That(c.Selected, Is.EqualTo("High"));
        }

        [Test]
        public void AProfilesValuesAreLoadedAndAChangeIsRememberedTheMomentItIsMade()
        {
            var reg = new SettingsRegistry();
            var vsyncish = new ToggleSetting("g.toggle", "G", "Toggle", true, registry: reg);
            var volume = new RangeSetting("g.volume", "G", "Volume", 100, 0, 100, 5, registry: reg);
            var quality = new ChoiceSetting("g.quality", "G", "Quality", new[] { "Low", "Mid", "High" }, 2, registry: reg);

            var store = new MemorySettingStore();
            store.Values["g.toggle"] = "false";
            store.Values["g.volume"] = "35";
            store.Values["g.quality"] = "Mid";
            reg.Bind(store);
            Assert.That(vsyncish.Value, Is.False);
            Assert.That(volume.Value, Is.EqualTo(35));
            Assert.That(quality.Selected, Is.EqualTo("Mid"));

            volume.Set(80);
            quality.Set(0);
            Assert.That(store.Values["g.volume"], Is.EqualTo("80"));
            Assert.That(store.Values["g.quality"], Is.EqualTo("Low"), "a choice is kept by name, so reordered options keep it");

            // Back at its default it is forgotten, so a later change of default reaches it.
            vsyncish.Set(true);
            Assert.That(store.Values.ContainsKey("g.toggle"), Is.False);

            // Another profile: what it has, and defaults for the rest (nothing carried over from the first).
            var other = new MemorySettingStore();
            other.Values["g.volume"] = "10";
            reg.Bind(other);
            Assert.That(volume.Value, Is.EqualTo(10));
            Assert.That(quality.Selected, Is.EqualTo("High"));
            Assert.That(store.Values["g.volume"], Is.EqualTo("80"), "the first profile keeps its own");
        }

        [TestCase("")]
        [TestCase("loud")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        public void ADamagedValueIsIgnoredAndTheDefaultStands(string text)
        {
            var reg = new SettingsRegistry();
            var volume = new RangeSetting("v", "G", "Volume", 70, 0, 100, registry: reg);
            var toggle = new ToggleSetting("t", "G", "T", true, registry: reg);
            var choice = new ChoiceSetting("c", "G", "C", new[] { "A", "B" }, 1, registry: reg);
            var store = new MemorySettingStore();
            store.Values["v"] = store.Values["t"] = store.Values["c"] = text;
            reg.Bind(store);
            Assert.That(volume.Value, Is.EqualTo(70));
            Assert.That(toggle.Value, Is.True);
            Assert.That(choice.Selected, Is.EqualTo("B"));
        }

        [Test]
        public void AnOutOfRangeValueIsBroughtBackIntoRange()
        {
            var reg = new SettingsRegistry();
            var volume = new RangeSetting("v", "G", "Volume", 70, 0, 100, 5, registry: reg);
            reg.Bind(new MemorySettingStore { Values = { ["v"] = "250" } });
            Assert.That(volume.Value, Is.EqualTo(100));
        }

        [Test]
        public void ResetPutsASettingAGroupOrEverythingBackAndForgetsIt()
        {
            var reg = new SettingsRegistry();
            var a = new RangeSetting("a", "One", "A", 1, 0, 10, 1, registry: reg);
            var b = new ToggleSetting("b", "One", "B", false, registry: reg);
            var c = new ChoiceSetting("c", "Two", "C", new[] { "x", "y" }, 0, registry: reg);
            var store = new MemorySettingStore();
            reg.Bind(store);
            a.Set(7);
            b.Set(true);
            c.Set(1);
            Assert.That(store.Values.Count, Is.EqualTo(3));

            a.Reset();
            Assert.That(a.Value, Is.EqualTo(1));
            Assert.That(a.IsDefault, Is.True);
            Assert.That(store.Values.ContainsKey("a"), Is.False);

            a.Set(3);
            reg.ResetGroup("One");
            Assert.That(a.IsDefault && b.IsDefault, Is.True);
            Assert.That(c.IsDefault, Is.False, "another group is left alone");

            reg.ResetAll();
            Assert.That(reg.All.All(s => s.IsDefault), Is.True);
            Assert.That(store.Values, Is.Empty);
        }

        [Test]
        public void WhatASystemPutsIntoEffectFollowsEveryChangeResetAndProfile()
        {
            var reg = new SettingsRegistry();
            double applied = -1;
            int calls = 0;
            var cap = new RangeSetting("cap", "G", "Cap", 144, 30, 240, 1, registry: reg).Applying(v =>
            {
                applied = v;
                calls++;
            });
            Assert.That(applied, Is.EqualTo(144), "applied the moment it is declared");

            cap.Set(60);
            Assert.That(applied, Is.EqualTo(60));
            cap.Set(60);
            Assert.That(calls, Is.EqualTo(2), "setting the same value again changes nothing");
            cap.Reset();
            Assert.That(applied, Is.EqualTo(144));
            reg.Bind(new MemorySettingStore { Values = { ["cap"] = "90" } });
            Assert.That(applied, Is.EqualTo(90));
        }

        [Test]
        public void ASettingDeclaredAfterAProfileWasLoadedTakesThatProfilesValue()
        {
            var reg = new SettingsRegistry();
            reg.Bind(new MemorySettingStore { Values = { ["late"] = "true" } });
            var late = new ToggleSetting("late", "G", "Late", false, registry: reg);
            Assert.That(late.Value, Is.True);
        }

        [Test]
        public void SearchFindsSettingsByEveryWordAcrossLabelGroupDescriptionAndKeywords()
        {
            var reg = new SettingsRegistry();
            var sens = new RangeSetting("controls.look", SettingGroups.Controls, "Mouse look sensitivity", 1, 0.25, 4,
                description: "How far the view turns.", keywords: "aim camera", registry: reg);
            var invert = new ToggleSetting("controls.invert", SettingGroups.Controls, "Invert vertical look", false, registry: reg);
            var fps = new ChoiceSetting("graphics.fps", SettingGroups.Graphics, "Frame rate cap", new[] { "60", "144" }, 1,
                keywords: "performance", registry: reg);
            var volume = new RangeSetting("audio.volume", SettingGroups.Audio, "Master volume", 100, 0, 100, registry: reg);

            Assert.That(reg.Search("LOOK"), Is.EqualTo(new GameSetting[] { sens, invert }), "case does not matter");
            Assert.That(reg.Search("look aim"), Is.EqualTo(new GameSetting[] { sens }), "every word must be found");
            Assert.That(reg.Search("performance"), Is.EqualTo(new GameSetting[] { fps }), "keywords count");
            Assert.That(reg.Search("audio"), Is.EqualTo(new GameSetting[] { volume }), "the group's name counts");
            Assert.That(reg.Search("turns"), Is.EqualTo(new GameSetting[] { sens }), "the description counts");
            Assert.That(reg.Search("warp drive"), Is.Empty);
            Assert.That(reg.Search("  "), Is.EqualTo(new GameSetting[] { fps, sens, invert, volume }), "everything, in the menu's group order");
        }

        [Test]
        public void TheGamesOwnDeclarationsAreFoundWithoutBeingTouchedFirst()
        {
            int sources = SettingsRegistry.DiscoverDeclarations(new[] { typeof(SettingsRegistry).Assembly });
            Assert.That(sources, Is.GreaterThanOrEqualTo(2));
            var reg = SettingsRegistry.Default;
            Assert.That(reg.Find("simulation.startingWarp"), Is.SameAs(SimClock.StartingWarp));
            Assert.That(reg.Find("economy.tradersPerRoute"), Is.SameAs(InterstellarTraffic.TradersPerRoute));
            Assert.That(reg.Groups(), Does.Contain(SettingGroups.Simulation).And.Contain(SettingGroups.Economy));

            // Their defaults are what the game ran on before they were settings.
            Assert.That(SimClock.StartingWarp.Default, Is.EqualTo(SimClock.DefaultWarpIndex));
            Assert.That(SimClock.StartingWarp.Options.Count, Is.EqualTo(SimClock.Levels.Length));
            Assert.That(InterstellarTraffic.TradersPerRoute.Default, Is.EqualTo(4));
            Assert.That(InterstellarTraffic.MaxTradersPerRoute, Is.EqualTo(4));
        }

        [Test]
        public void AProfileKeepsItsSettingsInTheGameBlockOfItsSettingsFile()
        {
            string root = Path.Combine(Path.GetTempPath(), "spacegod-settings-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new ProfileStore(root);
                var reg = new SettingsRegistry();
                var volume = new RangeSetting("audio.masterVolume", SettingGroups.Audio, "Master volume", 100, 0, 100, 5, registry: reg);
                var look = new RangeSetting("controls.lookSensitivity", SettingGroups.Controls, "Look", 1, 0.25, 4, 0.05, registry: reg);
                var fps = new ChoiceSetting("graphics.frameRateCap", SettingGroups.Graphics, "Cap", new[] { "60 fps", "144 fps" }, 1, registry: reg);
                var ryan = store.Create("Ryan");
                reg.Bind(new ProfileSettingStore(ryan));
                volume.Set(45);
                look.Set(1.5);
                fps.Set(0);
                var game = ryan.ReloadSettings().Game;
                Assert.That(game.GetDouble("audio.masterVolume"), Is.EqualTo(45), "a number is a JSON number");
                Assert.That(game.GetDouble("controls.lookSensitivity"), Is.EqualTo(1.5));
                Assert.That(game.GetString("graphics.frameRateCap"), Is.EqualTo("60 fps"));

                // The next run, or a switch back to the profile, finds them.
                var reg2 = new SettingsRegistry();
                var volume2 = new RangeSetting("audio.masterVolume", SettingGroups.Audio, "Master volume", 100, 0, 100, 5, registry: reg2);
                var fps2 = new ChoiceSetting("graphics.frameRateCap", SettingGroups.Graphics, "Cap", new[] { "60 fps", "144 fps" }, 1, registry: reg2);
                reg2.Bind(new ProfileSettingStore(new ProfileStore(root).Get("Ryan")));
                Assert.That(volume2.Value, Is.EqualTo(45));
                Assert.That(fps2.Selected, Is.EqualTo("60 fps"));
                reg2.Bind(new ProfileSettingStore(store.Create("Guest")));
                Assert.That(volume2.Value, Is.EqualTo(100), "another profile has its own");

                volume.Reset();
                Assert.That(ryan.ReloadSettings().Game.ContainsKey("audio.masterVolume"), Is.False, "a reset forgets it");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void TheAutoReorientFlagKeepsTheValueItWasAlwaysSavedAs()
        {
            // UnitySpaceApp's auto-reorient moved onto the registry under the key it was always kept by, as a JSON
            // bool like the profile's other flags, so a value kept before the move still reads back and vice versa.
            var reg = new SettingsRegistry();
            var flag = new ToggleSetting(GameSettings.AutoReorient, SettingGroups.Controls, "Auto-reorient view", true, registry: reg);
            string root = Path.Combine(Path.GetTempPath(), "spacegod-settings-" + Guid.NewGuid().ToString("N"));
            try
            {
                var profile = new ProfileStore(root).Create("Old");
                profile.SetFlag(GameSettings.AutoReorient, false);
                reg.Bind(new ProfileSettingStore(profile));
                Assert.That(flag.Value, Is.False);
                flag.Set(true); // back at its default: removed, and the old reader's fallback (true) stands
                Assert.That(profile.Flag(GameSettings.AutoReorient, true), Is.True);
                Assert.That(profile.Settings.Game.ContainsKey(GameSettings.AutoReorient), Is.False);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
