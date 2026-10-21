using System.IO;
using Unity.SpaceGod.Core;
using Unity.SpaceGod.UI;
using UnityEngine;

namespace Unity.SpaceGod
{
    /// <summary>
    /// The player profile this run uses, from <c>Profiles</c> in the player's data folder: its saves and each of
    /// its settings are files in its own folder there (see <see cref="PlayerProfile"/>), on every platform alike.
    /// </summary>
    public static class ActiveProfile
    {
        static ProfileStore store;
        static PlayerProfile current;

        public static ProfileStore Store => store ??= new ProfileStore(Path.Combine(Application.persistentDataPath, "Profiles"));

        public static PlayerProfile Current => current ??= Store.Current();

        /// <summary>Makes <paramref name="name"/> the profile in use (made if it is new, and remembered for the next
        /// run) and puts its display settings on the screen.</summary>
        public static PlayerProfile Switch(string name)
        {
            current = Store.Select(name);
            DisplayOptions.ApplySaved();
            return current;
        }
    }
}
