using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UnitySpace.Core
{
    /// <summary>
    /// The player profiles in the player's data folder: one folder each under <see cref="Root"/>, holding that
    /// player's saves and settings, and a <c>current.txt</c> naming the one in use. Plain files, so they are the
    /// same on every platform and can be read, copied or edited by hand.
    /// </summary>
    public sealed class ProfileStore
    {
        public const string DefaultName = "Default";

        public readonly string Root;

        public ProfileStore(string root) => Root = root;

        string CurrentFile => Path.Combine(Root, "current.txt");

        /// <summary>The profile in use (the one last selected, else <see cref="DefaultName"/>), its folder made if it is new.</summary>
        public PlayerProfile Current()
        {
            string name = null;
            try
            {
                if (File.Exists(CurrentFile)) name = File.ReadAllText(CurrentFile).Trim();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
            return Open(IsValidName(name) ? name : DefaultName);
        }

        /// <summary>Makes <paramref name="name"/> the profile in use, from this run on.</summary>
        public PlayerProfile Select(string name)
        {
            var profile = Open(name);
            Directory.CreateDirectory(Root);
            File.WriteAllText(CurrentFile, profile.Name);
            return profile;
        }

        /// <summary>The profile called <paramref name="name"/>, its folder made if it is new.</summary>
        public PlayerProfile Open(string name)
        {
            if (!IsValidName(name)) throw new ArgumentException("Not a profile name: " + name, nameof(name));
            var profile = new PlayerProfile(name, Path.Combine(Root, name));
            Directory.CreateDirectory(profile.Folder);
            return profile;
        }

        /// <summary>Every profile there is, by name.</summary>
        public List<string> Names() =>
            Directory.Exists(Root)
                ? Directory.GetDirectories(Root).Select(Path.GetFileName).Where(IsValidName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();

        /// <summary>Whether a profile called <paramref name="name"/> is there already (two that differ only in case would share a folder on Windows).</summary>
        public bool Exists(string name) => Names().Contains(name, StringComparer.OrdinalIgnoreCase);

        /// <summary>A name that makes one folder on every platform: not empty, not a path, nothing a file system refuses.</summary>
        public static bool IsValidName(string name) =>
            !string.IsNullOrWhiteSpace(name) && name.Length <= 64 && name == name.Trim() && name != "." && name != ".."
            && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) < 0;
    }

    /// <summary>
    /// One player's folder: <c>Saves</c> for the save slots and <c>Settings</c> with each setting in a file of
    /// its own (<c>display.txt</c>, <c>auto-reorient.txt</c>...), so one that is damaged or deleted costs only itself.
    /// </summary>
    public sealed class PlayerProfile
    {
        public readonly string Name;
        public readonly string Folder;

        public PlayerProfile(string name, string folder)
        {
            Name = name;
            Folder = folder;
        }

        public string SavesFolder => Path.Combine(Folder, "Saves");

        public string SettingsFolder => Path.Combine(Folder, "Settings");

        public string SettingPath(string setting) => Path.Combine(SettingsFolder, setting + ".txt");

        /// <summary>The text of <paramref name="setting"/>, or null when it was never set or cannot be read.</summary>
        public string Setting(string setting)
        {
            string path = SettingPath(setting);
            try
            {
                return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        public void SetSetting(string setting, string value) =>
            SaveGame.WriteFile(SettingPath(setting), System.Text.Encoding.UTF8.GetBytes(value ?? ""));

        /// <summary>An on/off setting, or <paramref name="fallback"/> when it was never set or is not one.</summary>
        public bool Flag(string setting, bool fallback) => bool.TryParse(Setting(setting), out bool on) ? on : fallback;

        public void SetFlag(string setting, bool on) => SetSetting(setting, on ? "true" : "false");
    }
}
