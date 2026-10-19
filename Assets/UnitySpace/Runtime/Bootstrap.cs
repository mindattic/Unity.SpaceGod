using System;
using UnityEngine;

namespace UnitySpace
{
    /// <summary>
    /// Makes any scene playable: if no <see cref="UnitySpaceApp"/> exists after load, one is
    /// created. With no flags that is all that happens. <c>-demo</c> runs the scripted tour once and
    /// quits (<see cref="CaptureRunner"/>) — the same flag name used for it in every MindAttic Unity
    /// project. Pair it with <c>-capture</c> (parsed separately by MindAttic.Capture) to record the
    /// session.
    /// </summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureApp()
        {
            var app = UnityEngine.Object.FindAnyObjectByType<UnitySpaceApp>();
            if (app == null) app = new GameObject("Solar System").AddComponent<UnitySpaceApp>();

            if (HasCommandLineFlag("-demo") && app.GetComponent<CaptureRunner>() == null)
            {
                // -capture's own timer (if requested) keeps running unmodified: -demo only adds
                // the tour, it does not change what -capture does.
                app.gameObject.AddComponent<CaptureRunner>();
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
