using System;
using MindAttic.Capture.Unity;
using UnityEngine;

namespace SolarSystem
{
    /// <summary>
    /// Makes any scene playable: if no <see cref="SolarSystemApp"/> exists after load, one is
    /// created. In a capture-enabled player (or the editor), <c>-capture screen</c> attaches the
    /// scripted screenshot tour (<see cref="CaptureRunner"/>); <c>-demo</c> attaches the live,
    /// looping showcase instead. <c>-capture</c> is parsed by MindAttic.Capture.
    /// </summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureApp()
        {
            var app = UnityEngine.Object.FindAnyObjectByType<SolarSystemApp>();
            if (app == null) app = new GameObject("Solar System").AddComponent<SolarSystemApp>();

            if (WantsTour() && app.GetComponent<CaptureRunner>() == null)
            {
                // The tour decides when to shoot, so turn off MindAttic.Capture's own every-N-seconds timer.
                // It starts on the first frame, after this runs.
                CaptureUnity.Options.ScreenshotInterval = null;
                app.gameObject.AddComponent<CaptureRunner>();
            }
            else if (HasCommandLineFlag("-demo") && app.GetComponent<DemoRunner>() == null)
            {
                app.gameObject.AddComponent<DemoRunner>();
            }
        }

        /// <summary>
        /// "-capture screen" with no interval. "-capture screen 5" keeps MindAttic.Capture's plain
        /// timed screenshots instead (with or without -demo).
        /// </summary>
        static bool WantsTour()
        {
            var args = CaptureUnity.Args;
            return args != null && CaptureUnity.Options != null && args.ScreenInterval != null && !args.ScreenIntervalSpecified;
        }

        public static bool HasCommandLineFlag(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            foreach (var a in args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
