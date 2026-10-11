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

        void Start()
        {
            app = GetComponent<SolarSystemApp>();
            StartCoroutine(Loop());
        }

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
            if (TookOver())
            {
                app.Clock.Paused = false;
                StopAllCoroutines();
                Destroy(this);
            }
        }

        bool TookOver()
        {
            var i = app.Input;
            return i.Move.sqrMagnitude > 0.0001f || i.LookHeld || i.Scroll != 0 || i.SelectClicked
                || i.Focus || i.Escape || i.Home || i.CycleShips || i.NumberKey >= 0;
        }

        static IEnumerator NoShot(string name)
        {
            yield break;
        }
    }
}
