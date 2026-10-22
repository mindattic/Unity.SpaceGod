using System;
using System.IO;
using NUnit.Framework;
using Unity.Shared.Core;
using Unity.Shared.Core.Json;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Unity.SpaceGod's profiles on the shared Unity.Shared store (U.SPC-T325): its own settings live in the
    /// <c>game</c> block of each profile's one settings.json, and profiles from before that, with a file per
    /// setting under <c>Settings/</c>, are moved into it.
    /// </summary>
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
            var profile = new ProfileStore(root).Current;
            Assert.That(profile.Name, Is.EqualTo(ProfileStore.DefaultName));
            Assert.That(Directory.Exists(Path.Combine(root, ProfileStore.DefaultName)), Is.True);
            Assert.That(profile.SavesFolder, Is.EqualTo(Path.Combine(root, ProfileStore.DefaultName, "Saves")));
        }

        [Test]
        public void TheGamesSettingsAreInTheGameBlockOfTheOneSettingsFile()
        {
            var profile = new ProfileStore(root).Current;
            Assert.That(profile.Setting(GameSettings.Display), Is.Null);
            Assert.That(profile.Flag(GameSettings.AutoReorient, true), Is.True);

            profile.SetSetting(GameSettings.Display, "2560x1080 Borderless vsync");
            profile.SetFlag(GameSettings.AutoReorient, false);

            var json = JsonObject.Parse(File.ReadAllText(Path.Combine(root, ProfileStore.DefaultName, "settings.json")));
            Assert.That(json.GetString("format"), Is.EqualTo("mindattic.settings"));
            Assert.That(json.GetObject("game").GetString("display"), Is.EqualTo("2560x1080 Borderless vsync"));
            Assert.That(json.GetObject("game").GetBool("autoReorient", true), Is.False);
            Assert.That(Directory.Exists(Path.Combine(root, ProfileStore.DefaultName, "Settings")), Is.False, "no per-setting files any more");

            var again = new ProfileStore(root).Current;
            Assert.That(again.Setting(GameSettings.Display), Is.EqualTo("2560x1080 Borderless vsync"));
            Assert.That(again.Flag(GameSettings.AutoReorient, true), Is.False);
        }

        [Test]
        public void ProfilesKeepTheirOwnSettingsAndTheSelectedOneIsRemembered()
        {
            var store = new ProfileStore(root);
            store.Current.SetFlag(GameSettings.AutoReorient, false);
            store.Create("Second");
            var second = store.Select("Second");
            Assert.That(second.Flag(GameSettings.AutoReorient, true), Is.True, "a new profile starts from the defaults");

            var reopened = new ProfileStore(root);
            Assert.That(reopened.Current.Name, Is.EqualTo("Second"));
            Assert.That(reopened.Get(ProfileStore.DefaultName).Flag(GameSettings.AutoReorient, true), Is.False);
            Assert.That(reopened.List(), Is.EqualTo(new[] { ProfileStore.DefaultName, "Second" }));
        }

        [Test]
        public void OldPerSettingFilesMoveIntoSettingsJsonAndAreKeptAside()
        {
            // A profile as U.SPC-T327 wrote it: current.txt, Saves/, and Settings/<setting>.txt.
            string folder = Path.Combine(root, "Ryan");
            Directory.CreateDirectory(Path.Combine(folder, "Saves"));
            File.WriteAllText(Path.Combine(folder, "Saves", "slot1.sav"), "save bytes");
            Directory.CreateDirectory(Path.Combine(folder, "Settings"));
            File.WriteAllText(Path.Combine(folder, "Settings", "display.txt"), "1920x1080 Windowed vsync");
            File.WriteAllText(Path.Combine(folder, "Settings", "auto-reorient.txt"), "false");
            File.WriteAllText(Path.Combine(root, "current.txt"), "Ryan");

            var store = new ProfileStore(root);
            Assert.That(LegacyProfileSettings.MigrateAll(store), Is.EqualTo(new[] { "Ryan" }));

            var reopened = new ProfileStore(root);
            var profile = reopened.Current;
            Assert.That(profile.Name, Is.EqualTo("Ryan"), "current.txt is kept");
            Assert.That(profile.Setting(GameSettings.Display), Is.EqualTo("1920x1080 Windowed vsync"));
            Assert.That(profile.Flag(GameSettings.AutoReorient, true), Is.False);
            Assert.That(profile.Settings.Status, Is.EqualTo(SettingsLoadStatus.Loaded));
            Assert.That(profile.Settings.Common.MusicVolume, Is.EqualTo(0.6f), "the common block starts at its defaults");
            Assert.That(File.ReadAllText(Path.Combine(folder, "Saves", "slot1.sav")), Is.EqualTo("save bytes"), "Saves/ is not touched");

            Assert.That(Directory.Exists(Path.Combine(folder, "Settings")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(folder, "Settings.old", "display.txt")), Is.EqualTo("1920x1080 Windowed vsync"), "the old files are kept aside");

            Assert.That(LegacyProfileSettings.MigrateAll(reopened), Is.Empty, "a migrated profile is not migrated again");
        }

        [Test]
        public void AValueAlreadyInSettingsJsonWinsOverAnOldFile()
        {
            var store = new ProfileStore(root);
            var profile = store.Create("Ryan");
            profile.SetFlag(GameSettings.AutoReorient, true);
            Directory.CreateDirectory(Path.Combine(profile.Folder, "Settings"));
            File.WriteAllText(Path.Combine(profile.Folder, "Settings", "auto-reorient.txt"), "false");
            File.WriteAllText(Path.Combine(profile.Folder, "Settings", "display.txt"), "garbled");
            Directory.CreateDirectory(Path.Combine(profile.Folder, "Settings.old"));

            Assert.That(LegacyProfileSettings.Migrate(profile), Is.True);
            Assert.That(profile.Flag(GameSettings.AutoReorient, false), Is.True);
            Assert.That(profile.Setting(GameSettings.Display), Is.EqualTo("garbled"), "text is carried over as it was; DisplaySettings.TryParse refuses it later");
            Assert.That(Directory.GetDirectories(profile.Folder, "Settings.old-*"), Has.Length.EqualTo(1), "an earlier Settings.old is not overwritten");
        }

        [Test]
        public void OldSettingFileNamesBecomeCamelCaseKeys()
        {
            Assert.That(LegacyProfileSettings.KeyFor("auto-reorient"), Is.EqualTo(GameSettings.AutoReorient));
            Assert.That(LegacyProfileSettings.KeyFor("display"), Is.EqualTo(GameSettings.Display));
            Assert.That(LegacyProfileSettings.KeyFor("pan_speed"), Is.EqualTo("panSpeed"));
        }

        [Test]
        public void ANameThatIsNotOneFolderIsRefused()
        {
            var store = new ProfileStore(root);
            foreach (var bad in new[] { "", " ", "..", "a/b", "a\\b", "c:", " padded", "Ryan.", "NUL", "con", "COM1.txt" })
                Assert.That(ProfileStore.IsValidName(bad), Is.False, bad);
            Assert.Throws<ArgumentException>(() => store.Create("../escape"));
            Assert.That(store.Current.Name, Is.EqualTo(ProfileStore.DefaultName));
            File.WriteAllText(store.CurrentFile, "../escape");
            Assert.That(store.Current.Name, Is.EqualTo(ProfileStore.DefaultName));
        }
    }
}
