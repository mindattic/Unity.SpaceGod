using Unity.Shared.Core;
using UnityEngine;

namespace Unity.SpaceGod
{
    /// <summary>
    /// The CLI mute (U.SPC-T536): launched with <c>-muted</c> or <c>MINDATTIC_CLI_MUTED=1</c>, Unity.Shared's bootstrap
    /// turns on <see cref="Profiles.SessionMuted"/> before the first scene loads. Everything this game plays goes through
    /// the one <see cref="AudioListener"/>, whose volume the profile's master volume setting
    /// (<see cref="EngineSettings.MasterVolume"/>) would otherwise put back whenever a profile is loaded, so that setting
    /// asks <see cref="ListenerVolume"/> here. The mute lasts this run only: AudioListener.volume is never saved and the
    /// stored master volume is left as the player set it.
    /// </summary>
    public static class SessionAudio
    {
        /// <summary>Whether this run is muted by the CLI mute.</summary>
        public static bool Muted => Profiles.SessionMuted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void OnLoad()
        {
            if (!Muted) return;
            AudioListener.volume = 0f;
            Debug.Log("[Unity.SpaceGod] Audio muted for this session (-muted / MINDATTIC_CLI_MUTED=1).");
        }

        /// <summary>The listener volume, 0-1, for a master volume of <paramref name="percent"/>: 0 while the session is muted.</summary>
        public static float ListenerVolume(double percent) => Muted ? 0f : Mathf.Clamp01((float)(percent / 100));
    }
}
