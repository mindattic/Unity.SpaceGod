using System;
using System.Collections;
using System.IO;
using System.Linq;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem
{
    /// <summary>
    /// A scripted camera tour that saves screenshots and quits; used to verify a build visually
    /// and to produce the README images. Run the player with <c>-capture &lt;folder&gt;</c>.
    /// </summary>
    public sealed class CaptureRunner : MonoBehaviour
    {
        public string OutputFolder;

        IEnumerator Start()
        {
            var app = GetComponent<SolarSystemApp>();
            Directory.CreateDirectory(OutputFolder);
            yield return Wait(1.5f);
            yield return Shot("01-earth-gameplay");

            app.FocusSystemOverview();
            yield return Wait(2.6f);
            yield return Shot("02-inner-system-gameplay");

            var carrier = app.Fleet.Ships.First(s => s.Spec.Class == ShipClass.Carrier);
            Focus(app, Target.OfShip(carrier), 7);
            yield return Wait(2.6f);
            yield return Shot("03-carrier-group");

            var battleship = app.Fleet.Ships.First(s => s.Spec.Class == ShipClass.Battleship && !s.IsEscort);
            Focus(app, Target.OfShip(battleship), 4);
            yield return Wait(2.6f);
            yield return Shot("04-battleship");

            var freighter = app.Fleet.Ships.First(s => s.Spec.Class == ShipClass.Freighter);
            Focus(app, Target.OfShip(freighter), 3.5);
            yield return Wait(2.6f);
            yield return Shot("05-freighter");

            Focus(app, Target.OfBody(app.Catalog["Saturn"].Index), 3.2);
            yield return Wait(2.6f);
            yield return Shot("06-saturn");

            Focus(app, Target.OfBody(app.Catalog["Earth"].Index), 9);
            yield return Wait(2.6f);
            yield return Shot("07-earth-system-gameplay");

            app.ToggleScale();
            yield return Wait(3.2f);
            yield return Shot("08-earth-system-realistic");

            app.FocusSystemOverview();
            yield return Wait(2.8f);
            yield return Shot("09-inner-system-realistic");

            app.ToggleScale();
            yield return Wait(3.2f);
            Focus(app, Target.OfBody(app.Catalog["Jupiter"].Index), 14);
            yield return Wait(2.8f);
            yield return Shot("10-jupiter-system");

            Debug.Log("[SolarSystem] Capture complete: " + OutputFolder);
            Application.Quit();
        }

        static void Focus(SolarSystemApp app, Target t, double radii)
        {
            app.Select(t);
            app.SpaceCamera.FocusOn(t, radii);
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end) yield return null;
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(OutputFolder, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[SolarSystem] Captured " + path);
            yield return null;
            yield return null;
        }
    }
}
