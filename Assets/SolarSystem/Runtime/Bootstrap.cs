using System;
using UnityEngine;

namespace SolarSystem
{
    /// <summary>
    /// Makes any scene playable: if no <see cref="SolarSystemApp"/> exists after load, one is
    /// created. Passing <c>-capture &lt;folder&gt;</c> on the player command line also attaches the
    /// scripted screenshot tour.
    /// </summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureApp()
        {
            var app = UnityEngine.Object.FindAnyObjectByType<SolarSystemApp>();
            if (app == null) app = new GameObject("Solar System").AddComponent<SolarSystemApp>();

            string capture = CommandLineValue("-capture");
            if (capture != null && app.GetComponent<CaptureRunner>() == null)
                app.gameObject.AddComponent<CaptureRunner>().OutputFolder = capture;
        }

        public static string CommandLineValue(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
