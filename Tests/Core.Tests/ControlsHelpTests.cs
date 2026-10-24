using System.Linq;
using NUnit.Framework;
using Unity.SpaceGod.Core;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>The Help screen's contents (U.SPC-T494): every key the old help card listed, in clear groups.</summary>
    [TestFixture]
    public class ControlsHelpTests
    {
        [Test]
        public void TheHelpScreenIsLaidOutInTheTicketsGroupsInOrder()
        {
            Assert.That(ControlsHelp.Sections.Select(s => s.Title), Is.EqualTo(new[]
            {
                "CAMERA AND GOD VIEW",
                "HELM AND FLIGHT",
                "COMBAT",
                "TRADE",
                "DIPLOMACY AND THE LAW",
                "FLEET AND COMMAND",
                "SCREENS AND PANELS",
            }));
            foreach (var s in ControlsHelp.Sections)
            {
                Assert.That(s.Lines, Is.Not.Empty, s.Title);
                Assert.That(s.Lines[0].Keys, Is.Not.Empty, s.Title + " opens on a key, not a carried-on line");
                Assert.That(s.Lines.All(l => l.Text.Trim().Length > 0), s.Title + " has a line with nothing to say");
            }
        }

        /// <summary>Every key and control the in-game help card printed before it became a screen of its own.</summary>
        static readonly string[] CardKeys =
        {
            "W A S D", "Q / E", "C / ]", "Space", "Shift", "Ctrl", "Wheel", "Right-drag", "Left-click", "F", "Esc", "Home",
            "G", "`", "1-9, 0", "Tab", "M", "B", "N", "K", "R", "Navigator", "[", "F11", "F12", "P", ",  .", "T", "O", "L",
            "H", "F1", "U", "'", ";", "/", "Mouse", "W / S", "A / D", "Z / X", "Left mouse", "Right mouse", "Middle mouse",
            "Insert", "2", "3", "4", "5", "6", "7", "8", "9", "0", "2-9", "Ring gate", "Y", "Enter", "PgDn / PgUp", "Del",
            "End", "I", "F2", "F3", "F7", "F10", "\\", "F4", "X", "V", "F5", "F8", "F6",
        };

        [Test]
        public void EveryKeyTheOldHelpCardListedIsOnTheHelpScreen()
        {
            var keys = ControlsHelp.AllLines().Select(l => l.Keys).Where(k => k.Length > 0).ToList();
            foreach (var key in CardKeys)
                Assert.That(keys.Any(k => k.Contains(key)), Is.True, "no help line for " + key);
        }

        [Test]
        public void TheOldCardsTopicsAreAllStillExplained()
        {
            string all = string.Join("\n", ControlsHelp.AllLines().Select(l => l.Text));
            foreach (var topic in new[]
                     {
                         "pause menu", "galaxy map", "DIPLOMACY", "broadside", "brace", "damage control", "line ahead", "EMP",
                         "mine", "phaser", "plasma cannon", "off-ramp", "surge", "spool", "FTL autopilot", "cargo hold",
                         "outfitting", "silent running", "contracts board", "your fleet", "trade desk", "bounty board",
                         "latch on", "market", "news feed", "a crime if seen", "pay a fine", "spawn & pilot", "quicksave",
                         "quickload", "save slots", "autosaves", "STAR SYSTEMS", "GAMEPLAY / REALISTIC", "auto-reorient",
                         "satellites", "hyperlanes", "torpedoes", "flight assist", "this help screen", "clear the screen",
                     })
                Assert.That(all, Does.Contain(topic), topic);
        }

        [Test]
        public void NoKeyIsListedTwiceInOneGroupForTheSameThing()
        {
            foreach (var s in ControlsHelp.Sections)
            {
                var dupes = s.Lines.GroupBy(l => l.ToString()).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                Assert.That(dupes, Is.Empty, s.Title);
            }
        }
    }
}
