using System;
using System.IO;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    [TestFixture]
    public class ProfileTests
    {
        string root;

        [SetUp]
        public void SetUp() => root = Path.Combine(Path.GetTempPath(), "uss-profiles-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void WithNothingChosenTheDefaultProfileIsUsedAndMadeOnDisk()
        {
            var profile = new ProfileStore(root).Current();
            Assert.That(profile.Name, Is.EqualTo(ProfileStore.DefaultName));
            Assert.That(Directory.Exists(Path.Combine(root, ProfileStore.DefaultName)), Is.True);
            Assert.That(profile.SavesFolder, Is.EqualTo(Path.Combine(root, ProfileStore.DefaultName, "Saves")));
        }

        [Test]
        public void EachSettingIsAFileOfItsOwnInTheProfilesSettingsFolder()
        {
            var profile = new ProfileStore(root).Current();
            Assert.That(profile.Setting("display"), Is.Null);
            Assert.That(profile.Flag("auto-reorient", true), Is.True);

            profile.SetSetting("display", "2560x1080 Borderless vsync");
            profile.SetFlag("auto-reorient", false);

            string settings = Path.Combine(root, ProfileStore.DefaultName, "Settings");
            Assert.That(File.ReadAllText(Path.Combine(settings, "display.txt")), Is.EqualTo("2560x1080 Borderless vsync"));
            Assert.That(File.ReadAllText(Path.Combine(settings, "auto-reorient.txt")), Is.EqualTo("false"));

            var again = new ProfileStore(root).Current();
            Assert.That(again.Setting("display"), Is.EqualTo("2560x1080 Borderless vsync"));
            Assert.That(again.Flag("auto-reorient", true), Is.False);
        }

        [Test]
        public void ADamagedSettingFallsBackWithoutTouchingTheOthers()
        {
            var profile = new ProfileStore(root).Current();
            profile.SetSetting("display", "1920x1080 Windowed vsync");
            profile.SetSetting("auto-reorient", "garbled");
            Assert.That(profile.Flag("auto-reorient", true), Is.True);
            Assert.That(profile.Setting("display"), Is.EqualTo("1920x1080 Windowed vsync"));
        }

        [Test]
        public void ProfilesKeepTheirOwnSettingsAndTheSelectedOneIsRemembered()
        {
            var store = new ProfileStore(root);
            store.Current().SetFlag("auto-reorient", false);
            var second = store.Select("Second");
            Assert.That(second.Flag("auto-reorient", true), Is.True, "a new profile starts from the defaults");

            var reopened = new ProfileStore(root);
            Assert.That(reopened.Current().Name, Is.EqualTo("Second"));
            Assert.That(reopened.Open(ProfileStore.DefaultName).Flag("auto-reorient", true), Is.False);
            Assert.That(reopened.Names(), Is.EqualTo(new[] { ProfileStore.DefaultName, "Second" }));
        }

        [Test]
        public void ANameThatIsNotOneFolderIsRefused()
        {
            var store = new ProfileStore(root);
            foreach (var bad in new[] { "", " ", "..", "a/b", "a\\b", "c:", " padded", "Ryan.", "NUL", "con", "COM1.txt" })
                Assert.That(ProfileStore.IsValidName(bad), Is.False, bad);
            Assert.Throws<ArgumentException>(() => store.Select("../escape"));
            File.WriteAllText(Path.Combine(store.Current().Folder, "..", "current.txt"), "../escape");
            Assert.That(store.Current().Name, Is.EqualTo(ProfileStore.DefaultName));
        }
    }
}
