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

            yield return SurfaceTour(app);

            Debug.Log("[SolarSystem] Capture complete: " + OutputFolder);
            Application.Quit();
        }

        // ---------------------------------------------------------------- surfaces, weather, asteroids

        IEnumerator SurfaceTour(SolarSystemApp app)
        {
            int moon = app.Catalog["Moon"].Index;
            int earth = app.Catalog["Earth"].Index;
            // Between the Earth-facing and sunlit directions: the near side, partly lit.
            View(app, moon, Blend(Dir(app, moon, earth), Dir(app, moon, 0), 0.5), 3.2);
            yield return Wait(3f);
            yield return Shot("11-moon-near-side");
            // Low sun across the surface: LOLA relief along the terminator.
            View(app, moon, Blend(Dir(app, moon, 0), Side(Dir(app, moon, 0)), 0.75), 2.4);
            yield return Wait(3f);
            yield return Shot("12-moon-terminator");

            foreach (var name in new[] { "Io", "Europa", "Ganymede" })
            {
                int i = app.Catalog[name].Index;
                View(app, i, Blend(Dir(app, i, 0), Side(Dir(app, i, 0)), 0.3), 3.0);
                yield return Wait(3f);
                yield return Shot("13-" + name.ToLowerInvariant());
            }

            // Jupiter with the Great Red Spot turned toward the Sun, seen from just off the subsolar point.
            int jupiter = app.Catalog["Jupiter"].Index;
            app.Clock.Paused = true; // Jupiter turns 110 degrees in 3 s at the default warp
            app.Clock.SetJd(NextGrsNoon(app, jupiter));
            yield return null;
            var grs = GrsDirection(app, jupiter);
            View(app, jupiter, Blend(grs, Dir(app, jupiter, 0), 0.2), 2.1);
            yield return Wait(3f);
            yield return Shot("14-jupiter-great-red-spot");
            View(app, jupiter, GrsDirection(app, jupiter), 1.35);
            yield return Wait(3f);
            yield return Shot("15-jupiter-grs-close");
            // One System III rotation (9 h 55.5 m) later the spot is back at noon: the bands have moved
            // with their winds and the oval has turned counter-clockwise.
            app.Clock.SetJd(app.Clock.Jd + 360.0 / 870.536);
            yield return null;
            View(app, jupiter, GrsDirection(app, jupiter), 1.35);
            yield return Wait(3f);
            yield return Shot("15b-jupiter-grs-close-one-rotation-later");
            app.Clock.Paused = false;

            foreach (var name in new[] { "Mars", "Titan", "Pluto", "Venus" })
            {
                int i = app.Catalog[name].Index;
                View(app, i, Blend(Dir(app, i, 0), Side(Dir(app, i, 0)), 0.35), name == "Titan" ? 3.0 : 2.6);
                yield return Wait(3.2f);
                yield return Shot("16-" + name.ToLowerInvariant());
            }

            // The belt from above at gameplay scale, then at true scale.
            var sun = Target.OfBody(0);
            app.Select(sun);
            double span = app.Scale.LocalDistance(app.Catalog.Sun, 4.0 * Astro.AuKm);
            app.SpaceCamera.FocusOn(sun, span * 1.9 / app.BodyDisplayRadius(0), 62f);
            yield return Wait(3f);
            yield return Shot("17-asteroid-belt-gameplay");
            // Frame rate with the whole belt on screen (vsync caps it at the display rate).
            int frames = 0;
            float start = Time.unscaledTime;
            while (Time.unscaledTime - start < 3f)
            {
                frames++;
                yield return null;
            }
            Debug.Log($"[SolarSystem] Belt overview: {frames / (Time.unscaledTime - start):F1} fps");
            app.ToggleScale();
            yield return Wait(3.2f);
            app.SpaceCamera.FocusOn(sun, app.Scale.LocalDistance(app.Catalog.Sun, 4.0 * Astro.AuKm) * 1.9 / app.BodyDisplayRadius(0), 62f);
            yield return Wait(3f);
            yield return Shot("18-asteroid-belt-realistic");
            app.ToggleScale();
            yield return Wait(3.2f);

            // Inside the belt near Vesta, looking along the orbit.
            var belt = app.AsteroidBelt;
            if (belt != null)
            {
                int vesta = belt.Field.IndexOfNumber(4);
                var v = belt.SceneOf(vesta, app.Clock.Jd);
                var outward = v.Normalized;
                var along = DVec3.Cross(new DVec3(0, 1, 0), outward).Normalized;
                // Just above the belt plane behind Vesta, looking ahead along its orbit and slightly down.
                var eye = v - along * 1500 + new DVec3(0, 1200, 0);
                var look = (v + along * 9000 - outward * 1500 - eye).Normalized;
                app.Select(Target.None);
                app.SpaceCamera.PlaceFree(eye, (float)(Math.Atan2(look.X, look.Z) * Mathf.Rad2Deg), (float)(-Math.Asin(look.Y) * Mathf.Rad2Deg));
                yield return Wait(1.5f);
                yield return Shot("19-inside-the-belt");
            }
        }

        /// <summary>Frames body <paramref name="body"/> from scene direction <paramref name="dir"/> (unit, from the body).</summary>
        static void View(SolarSystemApp app, int body, DVec3 dir, double radii)
        {
            var t = Target.OfBody(body);
            app.Select(t);
            double r = app.BodyDisplayRadius(body);
            var eye = app.BodyScene(body) + dir * (r * radii * 1.3);
            var look = -dir;
            app.SpaceCamera.PlaceFree(eye, (float)(Math.Atan2(look.X, look.Z) * Mathf.Rad2Deg), (float)(-Math.Asin(look.Y) * Mathf.Rad2Deg));
            app.SpaceCamera.FocusOn(t, radii, (float)(Math.Asin(Math.Max(-1, Math.Min(1, dir.Y))) * Mathf.Rad2Deg));
        }

        static DVec3 Dir(SolarSystemApp app, int from, int to) => (app.BodyScene(to) - app.BodyScene(from)).Normalized;
        static DVec3 Blend(DVec3 a, DVec3 b, double t) => DVec3.Lerp(a, b, t).Normalized;
        static DVec3 Side(DVec3 d) => DVec3.Cross(new DVec3(0, 1, 0), d).Normalized;

        /// <summary>Scene direction (Unity axes) from Jupiter's centre to the Great Red Spot.</summary>
        static DVec3 GrsDirection(SolarSystemApp app, int jupiter)
        {
            var body = app.Catalog[jupiter];
            double jd = app.Clock.Jd;
            BodyOrientation.Frame(body, jd, DVec3.Zero, out var north, out var meridian);
            var east = DVec3.Cross(north, meridian);
            double lon = GasGiantWeather.GreatRedSpot.EastLongitudeDeg(jd) * Astro.Deg2Rad;
            double lat = GasGiantWeather.GreatRedSpot.LatitudeDeg * Astro.Deg2Rad;
            var d = (meridian * Math.Cos(lon) + east * Math.Sin(lon)) * Math.Cos(lat) + north * Math.Sin(lat);
            return d.SwapYZ.Normalized;
        }

        /// <summary>The next time within one Jovian day that the spot is closest to local noon.</summary>
        static double NextGrsNoon(SolarSystemApp app, int jupiter)
        {
            var body = app.Catalog[jupiter];
            double jd0 = app.Clock.Jd, best = jd0, bestDot = -2;
            var toSun = (-app.Ephemeris.Helio(jupiter)).Normalized;
            for (int k = 0; k < 400; k++)
            {
                double jd = jd0 + k * (10.0 / 24.0) / 400;
                BodyOrientation.Frame(body, jd, DVec3.Zero, out var north, out var meridian);
                var east = DVec3.Cross(north, meridian);
                double lon = GasGiantWeather.GreatRedSpot.EastLongitudeDeg(jd) * Astro.Deg2Rad;
                double lat = GasGiantWeather.GreatRedSpot.LatitudeDeg * Astro.Deg2Rad;
                var d = (meridian * Math.Cos(lon) + east * Math.Sin(lon)) * Math.Cos(lat) + north * Math.Sin(lat);
                double dot = DVec3.Dot(d, toSun);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = jd;
                }
            }
            return best;
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
