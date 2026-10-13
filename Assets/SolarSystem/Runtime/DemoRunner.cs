using System.Collections;
using UnityEngine;

namespace SolarSystem
{
    /// <summary>
    /// Plays the <see cref="SceneTour"/> live, on loop, as an unattended showcase: no screenshots,
    /// no quitting. Any player input (swim, look, scroll, click or a hotkey) hands control straight
    /// back to free swim and stops the tour. Run the player with <c>-demo</c>.
    /// </summary>
    public sealed class DemoRunner : MonoBehaviour
    {
        SolarSystemApp app;

        bool rebuilt;

        void Start()
        {
            app = GetComponent<SolarSystemApp>();
            app.GalaxyRebuilt += OnGalaxyRebuilt;
            StartCoroutine(Loop());
        }

        void OnDestroy() => app.GalaxyRebuilt -= OnGalaxyRebuilt;

        void OnGalaxyRebuilt() => rebuilt = true;

        IEnumerator Loop()
        {
            while (true)
            {
                yield return SceneTour.Run(app, NoShot);
                if (TookOver()) yield break;
            }
        }

        void Update()
        {
            if (rebuilt || TookOver())
            {
                // A pause pressed to take over is kept (the clock is easing to a stop); otherwise run on.
                if (!app.Input.TogglePause) app.Clock.Paused = false;
                StopAllCoroutines();
                Destroy(this);
            }
        }

        bool TookOver()
        {
            var i = app.Input;
            return i.Move.sqrMagnitude > 0.0001f || i.LookHeld || i.Scroll != 0 || i.SelectClicked
                || i.Focus || i.Escape || i.Home || i.CycleShips || i.NumberKey >= 0
                || i.TogglePause || i.LevelView || i.ToggleScale || i.WatchBattle;
        }

        static IEnumerator NoShot(string name)
        {
            yield break;
        }
    }
}
