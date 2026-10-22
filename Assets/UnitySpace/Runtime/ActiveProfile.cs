using Unity.Shared.Core;
using Unity.SpaceGod.Core;
using Unity.SpaceGod.UI;
using UnityEngine;

namespace Unity.SpaceGod
{
    /// <summary>
    /// The player profile this run uses, from the shared MindAttic profile store (the Unity.Shared package,
    /// <c>Profiles</c> in the player's data folder): current.txt names it, its one settings.json holds the common
    /// settings and the game's own under <c>game</c> (see <see cref="GameSettings"/>), and its Saves folder the
    /// save slots. Profiles from before settings.json are migrated the first time the store is used.
    /// </summary>
    public static class ActiveProfile
    {
        static bool migrated;
        static Profile current;

        public static ProfileStore Store
        {
            get
            {
                if (!Profiles.IsConfigured) Profiles.UseDataPath(Application.persistentDataPath);
                var store = Profiles.Store;
                if (!migrated)
                {
                    migrated = true;
                    foreach (var name in LegacyProfileSettings.MigrateAll(store))
                        Debug.Log("[Unity.SpaceGod] Moved profile " + name + "'s settings into settings.json (the old files are in Settings.old).");
                }
                return store;
            }
        }

        public static Profile Current => current ??= Store.Current;

        /// <summary>Makes <paramref name="name"/> the profile in use (made if it is new, and remembered for the next
        /// run) and puts its display settings, and every other setting it remembers, into effect.</summary>
        public static Profile Switch(string name)
        {
            var store = Store;
            if (!store.Exists(name)) store.Create(name);
            current = store.Select(name);
            GameSettingsHost.Bind(); // the new profile's settings (U.SPC-T431)
            DisplayOptions.ApplySaved();
            return current;
        }
    }
}
