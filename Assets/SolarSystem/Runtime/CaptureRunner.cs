using System.Collections;
using MindAttic.Capture.Unity;
using UnityEngine;

namespace SolarSystem
{
    /// <summary>
    /// Runs the <see cref="SceneTour"/> once, saving a screenshot at each stop through MindAttic.Capture,
    /// then quits. Used to verify a build visually and to produce the README images. Run a
    /// capture-enabled player with <c>-capture screen</c> (add <c>video</c> to film the tour too);
    /// shots go to R:\MindAttic\Capture\UnitySolarSystem\{timestamp}.png, each sidecar tagged with its stop.
    /// </summary>
    public sealed class CaptureRunner : MonoBehaviour
    {
        IEnumerator Start()
        {
            var app = GetComponent<SolarSystemApp>();
            var wait = 0f;
            while (!CaptureUnity.Engine.Enabled && wait < 15f)
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!CaptureUnity.Engine.Enabled)
            {
                Debug.LogError("[SolarSystem] MindAttic.Capture did not start; the tour was not captured.");
                yield break;
            }

            yield return SceneTour.Run(app, Shot);

            Debug.Log("[SolarSystem] Tour captured to " + CaptureUnity.Engine.SessionFolder);
            Application.Quit();
        }

        static IEnumerator Shot(string name)
        {
            var shot = CaptureUnity.Engine.ScreenshotAsync(name);
            while (!shot.IsCompleted) yield return null;
            var result = shot.Result;
            if (result.Succeeded) Debug.Log("[SolarSystem] Captured " + result.PrimaryFile);
            else Debug.LogError("[SolarSystem] Screenshot " + name + " failed: " + result.Error);
        }
    }
}
