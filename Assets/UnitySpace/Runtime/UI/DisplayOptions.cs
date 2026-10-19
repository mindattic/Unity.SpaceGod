using System.Collections.Generic;
using UnitySpace.Core;
using UnityEngine;

namespace UnitySpace.UI
{
    /// <summary>
    /// Puts <see cref="DisplaySettings"/> on the screen and remembers them between runs (U.SPC-T276): the size,
    /// windowed / borderless windowed / exclusive fullscreen, and VSync. The pause menu's Display page edits a
    /// copy and only comes here on Apply, Keep or Revert.
    /// </summary>
    public static class DisplayOptions
    {
        const string Pref = "DisplaySettings";

        /// <summary>What is on the screen now, as last applied: <see cref="Screen.width"/> and the mode only catch up
        /// with <see cref="Screen.SetResolution(int, int, FullScreenMode)"/> at the end of the frame.</summary>
        public static DisplaySettings Current { get; private set; }

        /// <summary>
        /// At startup: the settings the player last kept, or (none kept yet) the screen as the player opened it with
        /// VSync on. A run whose screen is set on the command line (a capture, the scripted tour, -screen-width...)
        /// keeps that screen and takes only the remembered VSync.
        /// </summary>
        public static void ApplySaved()
        {
            Current = FromScreen().WithVSync(true);
            if (DisplaySettings.TryParse(PlayerPrefs.GetString(Pref, ""), out var saved))
            {
                if (ScreenFixedByCommandLine()) Current = Current.WithVSync(saved.VSync);
                else
                {
                    Apply(saved);
                    return;
                }
            }
            QualitySettings.vSyncCount = Current.VSync ? 1 : 0;
        }

        /// <summary>Puts <paramref name="settings"/> on the screen (exclusive fullscreen at the size's best refresh rate).</summary>
        public static void Apply(DisplaySettings settings)
        {
            QualitySettings.vSyncCount = settings.VSync ? 1 : 0;
            var mode = ToUnity(settings.Mode);
            if (settings.Mode == DisplayMode.Exclusive && TryFindMode(settings.Width, settings.Height, out var best))
                Screen.SetResolution(settings.Width, settings.Height, mode, best.refreshRateRatio);
            else
                Screen.SetResolution(settings.Width, settings.Height, mode);
            Current = settings;
        }

        /// <summary>Remembers <see cref="Current"/> for the next run (the player kept it).</summary>
        public static void Save()
        {
            PlayerPrefs.SetString(Pref, Current.Format());
            PlayerPrefs.Save();
        }

        /// <summary>The sizes the screen offers, once each at its best refresh rate, and the usual widescreen and ultrawide
        /// sizes that fit on it (U.SPC-T284), smallest first; at least the size the game is drawn at now (the editor's
        /// game view lists nothing).</summary>
        public static List<DisplayResolution> Resolutions()
        {
            var modes = new List<DisplayResolution>();
            int maxW = Current.Width, maxH = Current.Height;
            foreach (var r in Screen.resolutions)
            {
                modes.Add(new DisplayResolution(r.width, r.height, r.refreshRateRatio.value));
                maxW = Mathf.Max(maxW, r.width);
                maxH = Mathf.Max(maxH, r.height);
            }
            var native = Screen.currentResolution;
            maxW = Mathf.Max(maxW, native.width);
            maxH = Mathf.Max(maxH, native.height);
            var list = DisplaySettings.WithStandardSizes(DisplaySettings.Distinct(modes), maxW, maxH);
            if (list.Count == 0) list.Add(new DisplayResolution(Current.Width, Current.Height, 0));
            return list;
        }

        static bool TryFindMode(int width, int height, out Resolution best)
        {
            best = default;
            bool found = false;
            foreach (var r in Screen.resolutions)
                if (r.width == width && r.height == height && (!found || r.refreshRateRatio.value > best.refreshRateRatio.value))
                {
                    best = r;
                    found = true;
                }
            return found;
        }

        static DisplaySettings FromScreen() =>
            new DisplaySettings(Screen.width, Screen.height, FromUnity(Screen.fullScreenMode), QualitySettings.vSyncCount > 0);

        static FullScreenMode ToUnity(DisplayMode mode) =>
            mode == DisplayMode.Exclusive ? FullScreenMode.ExclusiveFullScreen
            : mode == DisplayMode.Borderless ? FullScreenMode.FullScreenWindow
            : FullScreenMode.Windowed;

        static DisplayMode FromUnity(FullScreenMode mode) =>
            mode == FullScreenMode.ExclusiveFullScreen ? DisplayMode.Exclusive
            : mode == FullScreenMode.FullScreenWindow ? DisplayMode.Borderless
            : DisplayMode.Windowed; // MaximizedWindow is macOS's own windowed look

        static readonly string[] ScreenFlags =
        {
            "-screen-width", "-screen-height", "-screen-fullscreen", "-window-mode", "-popupwindow", "-capture", "-demo",
        };

        static bool ScreenFixedByCommandLine()
        {
            foreach (var flag in ScreenFlags)
                if (Bootstrap.HasCommandLineFlag(flag)) return true;
            return false;
        }
    }
}
