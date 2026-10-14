using System.Collections;
using MindAttic.Capture.Unity;
using UnityEngine;

namespace SolarSystem
{
    /// <summary>
    /// Started by <c>-demo</c> (see <see cref="Bootstrap"/>): runs the <see cref="SceneTour"/>
    /// once, then quits. Used to verify a build visually and to produce the README images. Add
    /// <c>-capture screen</c> (or <c>video</c>, or both) to save a screenshot at each stop through
    /// MindAttic.Capture — shots go to R:\MindAttic\Capture\UnitySolarSystem\UnitySolarSystem_{timestamp}.png,
    /// each sidecar tagged with its stop. Without <c>-capture</c> the tour still plays, just with no screenshots.
    /// </summary>
    public sealed class CaptureRunner : MonoBehaviour
    {
        IEnumerator Start()
        {
            var app = GetComponent<SolarSystemApp>();
            var capturing = CaptureUnity.Args != null && CaptureUnity.Args.Any;
            if (capturing)
            {
                var wait = 0f;
                while (!CaptureUnity.Engine.Enabled && wait < 15f)
                {
                    wait += Time.unscaledDeltaTime;
                    yield return null;
                }
                capturing = CaptureUnity.Engine.Enabled;
                if (!capturing) Debug.LogError("[SolarSystem] MindAttic.Capture did not start; the tour will not be captured.");
            }

            yield return SceneTour.Run(app, capturing ? Shot : NoShot);

            if (capturing) Debug.Log("[SolarSystem] Tour captured to " + CaptureUnity.Engine.SessionFolder);
            Application.Quit();
        }

        static IEnumerator NoShot(string name)
        {
            yield break;
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
