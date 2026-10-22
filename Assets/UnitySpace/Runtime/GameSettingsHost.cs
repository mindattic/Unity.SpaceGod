using Unity.SpaceGod.Core;
using UnityEngine;

namespace Unity.SpaceGod
{
    /// <summary>
    /// Puts the settings registry (U.SPC-T431) to work in the game: finds every setting the game's systems declare
    /// (classes marked <see cref="SettingsSourceAttribute"/>, in every assembly that uses Core) once, and binds them
    /// to the active profile, so each takes the value that profile remembers and each change is written there.
    /// </summary>
    public static class GameSettingsHost
    {
        static bool discovered;

        public static SettingsRegistry Registry => SettingsRegistry.Default;

        /// <summary>Discovers the declarations (the first time) and loads the active profile's values into them.</summary>
        public static void Bind()
        {
            if (!discovered)
            {
                discovered = true;
                SettingsRegistry.DiscoverLoadedDeclarations();
                Registry.StoreFailed += (s, e) => Debug.LogError("[Unity.SpaceGod] Saving the setting '" + s.Key + "' failed: " + e);
            }
            Registry.Bind(new ProfileSettingStore(ActiveProfile.Current));
        }
    }
}
