using System.Globalization;
using Unity.SpaceGod.Core;
using UnityEngine;

namespace Unity.SpaceGod
{
    /// <summary>
    /// The settings that belong to the engine itself rather than to one of the game's systems (U.SPC-T431): the
    /// frame rate cap, multisample anti-aliasing and the master volume. Each goes into effect the moment it changes
    /// and whenever a profile is loaded. (The resolution, window mode and VSync have their own Display page.)
    /// </summary>
    [SettingsSource]
    public static class EngineSettings
    {
        static readonly int[] FrameRates = { 30, 60, 90, 120, 144, 165, 240, -1 };

        public static readonly ChoiceSetting FrameRateCap = new ChoiceSetting("graphics.frameRateCap", SettingGroups.Graphics,
            "Frame rate cap", new[] { "30 fps", "60 fps", "90 fps", "120 fps", "144 fps", "165 fps", "240 fps", "Unlimited" }, 4,
            "The most frames drawn a second when VSync is off (with VSync on, the screen's refresh rate sets it).",
            "fps performance framerate").Applying(i => Application.targetFrameRate = FrameRates[i]);

        public static readonly ToggleSetting Msaa = new ToggleSetting("render.msaa", SettingGroups.RenderPipeline,
            "Anti-aliasing (MSAA 4x)", true, "Smooths the edges of hulls, rings and orbit lines, at some cost on a slow GPU.",
            "antialiasing jaggies edges multisample quality").Applying(on =>
            {
                foreach (var cam in Camera.allCameras) cam.allowMSAA = on;
            });

        public static readonly RangeSetting MasterVolume = new RangeSetting("audio.masterVolume", SettingGroups.Audio,
            "Master volume", 100, 0, 100, 5, "How loud everything the game plays is.",
            v => v.ToString("0", CultureInfo.InvariantCulture) + "%", "sound mute loudness").Applying(v => AudioListener.volume = (float)(v / 100));
    }
}
