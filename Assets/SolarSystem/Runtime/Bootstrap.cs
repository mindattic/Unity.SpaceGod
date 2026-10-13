using System;
using UnityEngine;

namespace SolarSystem
{
    /// <summary>
    /// Makes any scene playable: if no <see cref="SolarSystemApp"/> exists after load, one is
    /// created. With no flags that is all that happens. <c>-autoplay</c> runs the scripted tour once
    /// and quits (<see cref="CaptureRunner"/>); <c>-demo</c> plays the same tour live, on loop, as an
    /// interactive showcase (<see cref="DemoRunner"/>). Pair either with <c>-capture</c> (parsed
    /// separately by MindAttic.Capture) to record the session.
    /// </summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureApp()
        {
            var app = UnityEngine.Object.FindAnyObjectByType<SolarSystemApp>();
            if (app == null) app = new GameObject("Solar System").AddComponent<SolarSystemApp>();

            if (HasCommandLineFlag("-autoplay") && app.GetComponent<CaptureRunner>() == null)
            {
                // -capture's own timer (if requested) keeps running unmodified: -autoplay only adds
                // the tour, it does not change what -capture does.
                app.gameObject.AddComponent<CaptureRunner>();
            }
            else if (HasCommandLineFlag("-demo") && app.GetComponent<DemoRunner>() == null)
            {
                app.gameObject.AddComponent<DemoRunner>();
            }
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
