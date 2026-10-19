using System.Collections.Generic;
using NUnit.Framework;
using SolarSystem.Core;

namespace SolarSystem.Core.Tests
{
    [TestFixture]
    public class DisplaySettingsTests
    {
        [Test]
        public void ResolutionsAreListedOncePerSizeAtTheirBestRefreshSmallestFirst()
        {
            var modes = new List<DisplayResolution>
            {
                new DisplayResolution(1920, 1080, 60),
                new DisplayResolution(1280, 720, 60),
                new DisplayResolution(1920, 1080, 144),
                new DisplayResolution(1920, 1200, 60),
                new DisplayResolution(1920, 1080, 120),
                new DisplayResolution(640, 480, 60),
                new DisplayResolution(320, 200, 70), // too small to play in
                new DisplayResolution(2560, 1440, 165),
            };
            var list = DisplaySettings.Distinct(modes);
            Assert.That(list, Is.EqualTo(new[]
            {
                new DisplayResolution(640, 480, 60),
                new DisplayResolution(1280, 720, 60),
                new DisplayResolution(1920, 1080, 144),
                new DisplayResolution(1920, 1200, 60),
                new DisplayResolution(2560, 1440, 165),
            }));
        }

        [Test]
        public void TheMenuStartsOnTheCurrentSizeOrTheNearestOne()
        {
            var list = DisplaySettings.Distinct(new[]
            {
                new DisplayResolution(1280, 720, 60),
                new DisplayResolution(1920, 1080, 60),
                new DisplayResolution(2560, 1440, 60),
            });
            Assert.That(DisplaySettings.IndexOfNearest(list, 1920, 1080), Is.EqualTo(1));
            // A dragged window of an odd size: the listed size closest to it.
            Assert.That(DisplaySettings.IndexOfNearest(list, 1366, 768), Is.EqualTo(0));
            Assert.That(DisplaySettings.IndexOfNearest(list, 2400, 1350), Is.EqualTo(2));
            Assert.That(DisplaySettings.IndexOfNearest(new List<DisplayResolution>(), 1920, 1080), Is.EqualTo(-1));
        }

        [Test]
        public void SettingsAreRememberedAndReadBackExactly()
        {
            foreach (var mode in new[] { DisplayMode.Windowed, DisplayMode.Borderless, DisplayMode.Exclusive })
                foreach (bool vSync in new[] { true, false })
                {
                    var s = new DisplaySettings(2560, 1440, mode, vSync);
                    Assert.That(DisplaySettings.TryParse(s.Format(), out var back), Is.True, s.Format());
                    Assert.That(back, Is.EqualTo(s));
                }
            Assert.That(new DisplaySettings(1920, 1080, DisplayMode.Borderless, true).Format(), Is.EqualTo("1920x1080;Borderless;vsync"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("1920x1080;Borderless")]
        [TestCase("1920x1080;Sideways;vsync")]
        [TestCase("1920x1080;7;vsync")]
        [TestCase("1920x;Windowed;vsync")]
        [TestCase("-1920x1080;Windowed;vsync")]
        [TestCase("100x80;Windowed;vsync")]
        [TestCase("1920x1080;Windowed;maybe")]
        public void ADamagedPreferenceIsIgnored(string text)
        {
            Assert.That(DisplaySettings.TryParse(text, out _), Is.False);
        }

        [Test]
        public void EditsMakeNewSettingsAndLeaveTheOldAlone()
        {
            var s = new DisplaySettings(1920, 1080, DisplayMode.Windowed, true);
            var t = s.WithSize(1280, 720).WithMode(DisplayMode.Exclusive).WithVSync(false);
            Assert.That(t, Is.EqualTo(new DisplaySettings(1280, 720, DisplayMode.Exclusive, false)));
            Assert.That(s != t, Is.True);
            Assert.That(s == new DisplaySettings(1920, 1080, DisplayMode.Windowed, true), Is.True);
        }

        [Test]
        public void TheConfirmationRevertsAfterTenRealSecondsUnlessKept()
        {
            var before = new DisplaySettings(1920, 1080, DisplayMode.Borderless, true);
            var after = before.WithMode(DisplayMode.Exclusive);
            var c = new DisplayConfirmation(before, after);
            Assert.That(c.SecondsShown, Is.EqualTo(10));
            Assert.That(c.Previous, Is.EqualTo(before));
            Assert.That(c.Applied, Is.EqualTo(after));

            // A long stall (the screen switching modes) counts too; a zero or negative step changes nothing.
            Assert.That(c.Tick(0.25), Is.False);
            Assert.That(c.SecondsShown, Is.EqualTo(10));
            Assert.That(c.Tick(0), Is.False);
            Assert.That(c.Tick(-5), Is.False);
            Assert.That(c.Tick(3), Is.False);
            Assert.That(c.SecondsShown, Is.EqualTo(7));
            Assert.That(c.Tick(6.7), Is.False);
            Assert.That(c.SecondsShown, Is.EqualTo(1));
            Assert.That(c.Tick(0.1), Is.True);
            Assert.That(c.SecondsShown, Is.EqualTo(0));
        }
    }

    [TestFixture]
    public class EscapeKeyTests
    {
        [Test]
        public void EscBacksOutOneLayerAtATimeThenOpensTheMenu()
        {
            Assert.That(EscapeKey.Route(panelOpen: true, piloting: false, focused: true, selected: true), Is.EqualTo(EscapeAction.ClosePanels));
            Assert.That(EscapeKey.Route(false, false, true, true), Is.EqualTo(EscapeAction.LeaveFocus));
            Assert.That(EscapeKey.Route(false, false, false, true), Is.EqualTo(EscapeAction.Deselect));
            Assert.That(EscapeKey.Route(false, false, false, false), Is.EqualTo(EscapeAction.OpenPauseMenu));
        }

        [Test]
        public void AtTheHelmEscClosesPanelsThenOpensTheMenuAndNeverLetsGoOfTheShip()
        {
            Assert.That(EscapeKey.Route(panelOpen: true, piloting: true, focused: true, selected: true), Is.EqualTo(EscapeAction.ClosePanels));
            // The chase camera and the piloted hull's selection stay: P is the way to let go of the helm.
            Assert.That(EscapeKey.Route(false, true, true, true), Is.EqualTo(EscapeAction.OpenPauseMenu));
        }
    }
}
