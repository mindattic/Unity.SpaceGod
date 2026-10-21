using System.Collections;
using MindAttic.Capture.Unity;
using UnityEngine;

namespace Unity.SpaceGod
{
    /// <summary>
    /// Started by <c>-demo</c> (see <see cref="Bootstrap"/>): runs the <see cref="SceneTour"/>
    /// once, then quits. Used to verify a build visually and to produce the README images. Add
    /// <c>-capture screen</c> (or <c>video</c>, or both) to save a screenshot at each stop through
    /// MindAttic.Capture — shots go to R:\MindAttic\Capture\Unity.SpaceGod\Unity.SpaceGod_{timestamp}.png,
    /// each sidecar tagged with its stop. Without <c>-capture</c> the tour still plays, just with no screenshots.
    /// </summary>
    public sealed class CaptureRunner : MonoBehaviour
    {
        IEnumerator Start()
        {
            var app = GetComponent<UnitySpaceApp>();
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
                if (!capturing) Debug.LogError("[Unity.SpaceGod] MindAttic.Capture did not start; the tour will not be captured.");
            }

            // A time reset rebuilds the galaxy under the tour (its ships, bodies and battles are gone):
            // stop there rather than run on, framing and waiting on objects that no longer exist.
            bool done = false, rebuilt = false;
            void OnRebuilt() => rebuilt = true;
            app.GalaxyRebuilt += OnRebuilt;
            var tour = StartCoroutine(RunTour(app, capturing, () => done = true));
            while (!done && !rebuilt) yield return null;
            app.GalaxyRebuilt -= OnRebuilt;
            if (!done)
            {
                StopCoroutine(tour);
                // A stop cut off mid-way never lets go of the camera itself (a dogfight's chase lock on a hull nobody flies).
                if (!app.IsPiloting) app.SpaceCamera.ExitFocus();
                Debug.LogWarning("[Unity.SpaceGod] The galaxy was rebuilt mid-tour; the tour stops here.");
            }

            if (capturing) Debug.Log("[Unity.SpaceGod] Tour captured to " + CaptureUnity.Engine.SessionFolder);
            Application.Quit();
        }

        static IEnumerator RunTour(UnitySpaceApp app, bool capturing, System.Action finished)
        {
            yield return SceneTour.Run(app, capturing ? Shot : NoShot);
            finished();
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
            if (result.Succeeded) Debug.Log("[Unity.SpaceGod] Captured " + result.PrimaryFile);
            else Debug.LogError("[Unity.SpaceGod] Screenshot " + name + " failed: " + result.Error);
        }
    }
}
