using System;
using System.Collections;
using System.Linq;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem
{
    /// <summary>
    /// The scripted camera tour through the system: inner planets, a carrier group, a battleship,
    /// a freighter mid-burn, Saturn, Earth at both scales, Jupiter, then real surfaces, weather and
    /// the asteroid belt. Run by <see cref="CaptureRunner"/>, which stops at each shot to save a
    /// screenshot when <c>-capture</c> is also active.
    /// </summary>
    public static class SceneTour
    {
        /// <summary>Runs one pass of the tour, calling <paramref name="stop"/> by name at each framed shot.</summary>
        public static IEnumerator Run(SolarSystemApp app, Func<string, IEnumerator> stop)
        {
            yield return Wait(1.5f);
            yield return stop("01-earth-gameplay");

            app.FocusSystemOverview();
            yield return Wait(2.6f);
            yield return stop("02-inner-system-gameplay");

            var carrier = app.Fleet.Ships.FirstOrDefault(s => !s.Destroyed && s.Controller.Phase != ShipPhase.Landed && s.Spec.Class == ShipClass.Carrier);
            Focus(app, Target.OfShip(carrier), 7);
            yield return Wait(2.6f);
            yield return stop("03-carrier-group");

            var battleship = app.Fleet.Ships.FirstOrDefault(s => !s.Destroyed && s.Controller.Phase != ShipPhase.Landed && s.Spec.Class == ShipClass.Battleship && !s.IsEscort);
            Focus(app, Target.OfShip(battleship), 4);
            yield return Wait(2.6f);
            yield return stop("04-battleship");

            var freighter = app.Fleet.Ships.FirstOrDefault(s => !s.Destroyed && s.Controller.Phase != ShipPhase.Landed && s.Spec.Class == ShipClass.Freighter);
            Focus(app, Target.OfShip(freighter), 3.5);
            yield return Wait(2.6f);
            yield return stop("05-freighter");

            var miner = app.Fleet.Ships.FirstOrDefault(s => !s.Destroyed && s.Controller.Phase != ShipPhase.Landed && s.Spec.Class == ShipClass.Miner);
            Focus(app, Target.OfShip(miner), 3.5);
            yield return Wait(2.6f);
            yield return stop("05b-miner");

            var courier = app.Fleet.Ships.FirstOrDefault(s => !s.Destroyed && s.Controller.Phase != ShipPhase.Landed && s.Spec.Class == ShipClass.Courier);
            Focus(app, Target.OfShip(courier), 3);
            yield return Wait(2.6f);
            yield return stop("05c-courier");

            Focus(app, Target.OfBody(app.Catalog["Saturn"].Index), 3.2);
            yield return Wait(2.6f);
            yield return stop("06-saturn");

            Focus(app, Target.OfBody(app.Catalog["Earth"].Index), 9);
            yield return Wait(2.6f);
            yield return stop("07-earth-system-gameplay");

            app.ToggleScale();
            yield return Wait(3.2f);
            yield return stop("08-earth-system-realistic");

            app.FocusSystemOverview();
            yield return Wait(2.8f);
            yield return stop("09-inner-system-realistic");

            app.ToggleScale();
            yield return Wait(3.2f);
            Focus(app, Target.OfBody(app.Catalog["Jupiter"].Index), 14);
            yield return Wait(2.8f);
            yield return stop("10-jupiter-system");

            yield return InfrastructureTour(app, stop);
            yield return CentauriTour(app, stop);
            yield return SurfaceTour(app, stop);
            yield return BattleTour(app, stop);
            yield return CombatShowcaseTour(app, stop);
        }

        // ---------------------------------------------------------------- what is in orbit

        /// <summary>
        /// Earth's docking rings and satellite clouds, a ring up close, a merchant coming down through
        /// the atmosphere under it, a yard with a hull on its slip, and the ISS.
        /// </summary>
        static IEnumerator InfrastructureTour(SolarSystemApp app, Func<string, IEnumerator> stop)
        {
            var infra = app.World.Infrastructure;
            if (infra == null) yield break;
            int earth = app.Catalog["Earth"].Index;
            View(app, earth, Blend(Dir(app, earth, 0), Side(Dir(app, earth, 0)), 0.45), 7.5);
            yield return Wait(3f);
            yield return stop("11a-earth-rings-and-satellites");

            var ny = infra.Rings.First(r => r.City == "New York");
            var rt = Target.OfRing(ny);
            app.Select(rt);
            app.SpaceCamera.FocusOn(rt, 9, 20f);
            yield return Wait(3f);
            yield return stop("11b-new-york-ring");

            // A merchant coming down: hurry the clock until one starts down to Earth, then watch it.
            bool Fresh(Ship s) => !s.Destroyed && s.Phase == ShipPhase.Descent && s.Ring != null && s.Ring.Body == earth
                                  && app.Clock.Jd - s.PhaseStartJd < s.Ring.DescentDays * 0.25;
            app.Clock.Paused = false;
            app.Clock.SetWarp(5);
            Ship lander = null;
            float deadline = Time.unscaledTime + 30f;
            while (lander == null && Time.unscaledTime < deadline)
            {
                lander = app.Fleet.Ships.FirstOrDefault(Fresh);
                yield return null;
            }
            if (lander != null)
            {
                app.Clock.SetWarp(3);
                var lt = Target.OfShip(lander);
                // From the side and a little above, so the ground it is heading for fills the background.
                var up = (app.ShipScene(lander) - app.BodyScene(earth)).Normalized;
                ViewShip(app, lander, Blend(Side(up), up, 0.3), 16);
                yield return Wait(3f);
                yield return stop("11c-descent-from-the-ring");
                // Slow down for the last of it, through the entry interface.
                deadline = Time.unscaledTime + 40f;
                while (lander.Phase == ShipPhase.Descent && Time.unscaledTime < deadline && (app.Clock.Jd - lander.PhaseStartJd) / lander.Ring.DescentDays < 0.9)
                    yield return null;
                app.Clock.SetWarp(2);
                var low = (app.ShipScene(lander) - app.BodyScene(earth)).Normalized;
                ViewShip(app, lander, Blend(Side(low), low, 0.2), 10);
                deadline = Time.unscaledTime + 25f;
                while (lander.Phase == ShipPhase.Descent && app.ShipStateOf(lander).Heat < 0.5f && Time.unscaledTime < deadline) yield return null;
                if (lander.Phase == ShipPhase.Descent)
                {
                    yield return Wait(1.2f);
                    yield return stop("11d-entry-plasma");
                }
            }
            app.Clock.SetWarp(SimClock.DefaultWarpIndex);

            var queued = app.Fleet.Ships.FirstOrDefault(s => !s.Destroyed && s.Phase == ShipPhase.Queued);
            if (queued != null)
            {
                var qt = Target.OfShip(queued);
                app.Select(qt);
                app.SpaceCamera.FocusOn(qt, 10, 18f);
                yield return Wait(3f);
                yield return stop("11e-queued-at-" + queued.Ring.Name.ToLowerInvariant().Replace(' ', '-'));
            }

            var yard = infra.Structures.FirstOrDefault(s => s.Kind == StructureKind.Shipyard && s.SlipProgress > 0.3)
                       ?? infra.Structures.First(s => s.Kind == StructureKind.Shipyard);
            var yt = Target.OfStructure(yard);
            app.Select(yt);
            app.SpaceCamera.FocusOn(yt, 3.2, 24f);
            yield return Wait(3.2f);
            yield return stop("11f-orbital-shipyard");

            var iss = infra.Structures.FirstOrDefault(s => s.Name == "ISS");
            if (iss != null)
            {
                var it = Target.OfStructure(iss);
                app.Select(it);
                app.SpaceCamera.FocusOn(it, 4, 15f);
                yield return Wait(3f);
                yield return stop("11g-iss");
            }
        }

        // ---------------------------------------------------------------- Alpha Centauri

        /// <summary>Out to Alpha Centauri through the interstellar view, round its stars, worlds and gates, and home.</summary>
        static IEnumerator CentauriTour(SolarSystemApp app, Func<string, IEnumerator> stop)
        {
            var sol = app.ActiveSystem;
            var ac = app.Galaxy.Find("Alpha Centauri");
            if (ac == null) yield break;
            app.Clock.Paused = false;
            app.Clock.SetWarp(SimClock.DefaultWarpIndex);
            app.TravelTo(ac);
            while (app.Travel.Active && app.Travel.Stage != Cameras.StarTravel.Leg.Cross) yield return null;
            yield return Wait(1.9f);
            yield return stop("30-between-the-stars");
            while (app.Travel.Active) yield return null;
            yield return Wait(2.2f);
            yield return stop("31-alpha-centauri");
            // The A-B orbit is tilted 70-odd degrees to the ecliptic: level the view to it.
            app.Reorient();
            yield return Wait(2.2f);
            yield return stop("31b-alpha-centauri-levelled");

            int a = app.Catalog["Alpha Centauri A"].Index;
            int b = app.Catalog["Alpha Centauri B"].Index;
            View(app, a, Blend(Dir(app, a, b), new DVec3(0, 1, 0), 0.35), 5);
            yield return Wait(3f);
            yield return stop("32-alpha-centauri-a-and-b");

            Focus(app, Target.OfBody(app.Catalog["Rigil Gate"].Index), 7);
            yield return Wait(3f);
            yield return stop("33-rigil-gate");

            int giant = app.Catalog["Alpha Centauri Ab"].Index;
            View(app, giant, Blend(Dir(app, giant, a), Side(Dir(app, giant, a)), 0.4), 3);
            yield return Wait(3f);
            yield return stop("34-candidate-giant-s1");

            int proxima = app.Catalog["Proxima Centauri"].Index;
            int pb = app.Catalog["Proxima b"].Index;
            var pt = Target.OfBody(proxima);
            app.Select(pt);
            app.SpaceCamera.FocusOn(pt, app.Scale.LocalDistance(app.Catalog[proxima], 0.06 * Astro.AuKm) * 1.8 / app.BodyDisplayRadius(proxima), 40f);
            yield return Wait(3.4f);
            yield return stop("35-proxima-system");

            View(app, pb, Blend(Dir(app, pb, proxima), Side(Dir(app, pb, proxima)), 0.8), 3.2);
            yield return Wait(3f);
            yield return stop("36-proxima-b");

            var gate = app.World.Infrastructure.Rings.FirstOrDefault();
            if (gate != null)
            {
                var gt = Target.OfRing(gate);
                app.Select(gt);
                app.SpaceCamera.FocusOn(gt, 10, 20f);
                yield return Wait(3f);
                yield return stop("37-" + gate.Name.ToLowerInvariant().Replace(' ', '-'));
            }

            app.TravelTo(sol);
            while (app.Travel.Active) yield return null;
            yield return Wait(1.5f);
            yield return stop("38-home-again");
        }

        // ---------------------------------------------------------------- the war

        /// <summary>
        /// Runs the clock fast until a real battle breaks out somewhere (nothing is staged), then
        /// slows to battle time and frames it: the whole engagement, then a capital ship up close.
        /// </summary>
        static IEnumerator BattleTour(SolarSystemApp app, Func<string, IEnumerator> stop)
        {
            app.Clock.Paused = false;
            app.Clock.SetWarp(6);
            Battle battle = null;
            var war = app.World.War;
            // Catch it the moment it starts (it does not run in its first frame) and drop to battle time.
            void Started(Battle b)
            {
                // The tour was stopped: stop watching.
                if (app.GetComponent<CaptureRunner>() == null)
                {
                    war.BattleStarted -= Started;
                    return;
                }
                // A real fleet action: at least two armed warships or platforms on each side.
                bool Armed(BattleUnit u) => u.Kind == UnitKind.Platform || (u.Ship != null && (u.Ship.Spec.IsMilitary || u.Ship.Faction.IsPirate));
                bool fleetAction = b.Units.Count(u => u.Side == 0 && Armed(u)) >= 2 && b.Units.Count(u => u.Side == 1 && Armed(u)) >= 2;
                // A lane ambush is worth watching too: the pulse, the stunned ships, the boarders going in.
                if (battle != null || !(fleetAction || b.Site != null)) return;
                battle = b;
                app.Clock.SetWarp(0);
            }
            war.BattleStarted += Started;
            app.FocusSystemOverview();
            float deadline = Time.unscaledTime + 240f;
            while (battle == null && Time.unscaledTime < deadline) yield return null;
            war.BattleStarted -= Started;
            if (battle == null)
            {
                app.Clock.SetWarp(SimClock.DefaultWarpIndex);
                yield break;
            }
            app.EnterBattleTime(battle);
            var flagship = battle.Units.Where(u => u.Ship != null && u.Present).OrderByDescending(u => u.Combat.Spec.Hull).First().Ship;
            var t = Target.OfShip(flagship);
            app.Select(t);
            app.SpaceCamera.FocusOn(t, 30, 25f);
            yield return Wait(6f);
            yield return stop("20-battle");
            if (!flagship.Destroyed && flagship.Battle != null)
            {
                app.SpaceCamera.FocusOn(t, 5, 12f);
                yield return Wait(4f);
                yield return stop("21-battle-capital-ship");
            }
            // Ride it out, cutting between the two flagships and any carrier with fighters out.
            for (int k = 0; k < 6 && !battle.Over; k++)
            {
                var subject = Subject(battle, k);
                if (subject != null)
                {
                    var st = Target.OfShip(subject);
                    app.Select(st);
                    app.SpaceCamera.FocusOn(st, k % 3 == 2 ? 9 : 16, k % 2 == 0 ? 30f : 12f);
                }
                yield return Wait(8f);
                yield return stop("22-battle-" + (k + 1));
            }
            app.Clock.SetWarp(SimClock.DefaultWarpIndex);
        }

        // ---------------------------------------------------------------- combat showcase (U.SPC-T10)

        /// <summary>
        /// Three staged skirmishes, each built to put one distinct combat feel on camera reliably
        /// rather than hoping the live war happens to produce it in time for a capture: a capital-ship
        /// broadside line (<see cref="Battle.BroadsideFacing"/>), then a Breacher "heavy fighter"
        /// dogfight and a Courier "light fighter" one (their forward guns, see
        /// <see cref="CombatSpec"/>), each watched close since nobody is actually piloting them.
        /// </summary>
        static IEnumerator CombatShowcaseTour(SolarSystemApp app, Func<string, IEnumerator> stop)
        {
            yield return BroadsideShowcase(app, stop);
            yield return DogfightShowcase(app, stop, "Neptune", app.World.Sol, app.World.Reach, ShipClass.Breacher, "41-breacher-dogfight");
            yield return DogfightShowcase(app, stop, "Pluto", app.World.Pirates, app.World.Sol, ShipClass.Courier, "42-courier-dogfight");
        }

        /// <summary>A Sol/Reach battleship-and-cruiser line off Uranus: the whole engagement, then
        /// whichever capital ship has taken the most damage, to catch a flank turned in once its bow
        /// guns are out rather than its nose held on target.</summary>
        static IEnumerator BroadsideShowcase(SolarSystemApp app, Func<string, IEnumerator> stop)
        {
            int uranus = app.Catalog["Uranus"].Index;
            var battle = TriggerSkirmish(app, uranus, app.World.Sol, app.World.Reach,
                new[] { ShipClass.Battleship, ShipClass.Battleship, ShipClass.Cruiser });
            if (battle == null) yield break;
            app.Clock.Paused = false;
            app.WatchBattle(battle);
            yield return Wait(8f);
            yield return stop("40-capital-broadside");
            yield return Wait(14f);
            var hurt = battle.Units.Where(u => u.Ship != null && u.Present).OrderByDescending(u => u.Combat.Damage).Select(u => u.Ship).FirstOrDefault();
            if (hurt != null)
            {
                var t = Target.OfShip(hurt);
                app.Select(t);
                app.SpaceCamera.FocusOn(t, 5, 12f);
            }
            yield return Wait(4f);
            yield return stop("40b-capital-broadside-flank");
            app.Clock.SetWarp(SimClock.DefaultWarpIndex);
        }

        /// <summary>A small, fast skirmish between <paramref name="cls"/> hulls, watched in close chase
        /// from behind one of them (<see cref="Cameras.SpaceCamera.EnterPilotChase"/>, otherwise only
        /// used for the player's own piloted ship) so its forward-mounted gun -- a Breacher's
        /// HeavyLaser, a Courier's FighterGuns -- reads clearly as it fights.</summary>
        static IEnumerator DogfightShowcase(SolarSystemApp app, Func<string, IEnumerator> stop, string bodyName, Faction aggressor, Faction defender, ShipClass cls, string stopId)
        {
            int body = app.Catalog[bodyName].Index;
            var battle = TriggerSkirmish(app, body, aggressor, defender, new[] { cls, cls, cls });
            if (battle == null) yield break;
            app.Clock.Paused = false;
            app.EnterBattleTime(battle);
            for (int k = 0; k < 2 && !battle.Over; k++)
            {
                var subject = battle.Units.FirstOrDefault(u => u.Ship != null && u.Present && u.Side == 0)?.Ship
                              ?? battle.Units.FirstOrDefault(u => u.Ship != null && u.Present)?.Ship;
                if (subject == null) break;
                var t = Target.OfShip(subject);
                app.Select(t);
                app.SpaceCamera.EnterPilotChase(t, cls == ShipClass.Breacher ? 5 : 4);
                yield return Wait(8f);
                yield return stop(k == 0 ? stopId : stopId + "b");
            }
            app.SpaceCamera.ExitFocus();
            app.Clock.SetWarp(SimClock.DefaultWarpIndex);
        }

        /// <summary>Spawns <paramref name="classes"/> for each side parked near <paramref name="body"/>,
        /// already hostile, and starts the fight at once -- a deterministic stand-in for waiting on the
        /// live war to produce the matchup a demo capture wants to show (an empty, unsettled body such
        /// as Uranus, Neptune or Pluto keeps the live war's own traffic out of it).</summary>
        static Battle TriggerSkirmish(SolarSystemApp app, int body, Faction aggressor, Faction defender, ShipClass[] classes)
        {
            double jd = app.Clock.Jd;
            var dir = new DVec3(1, 0.15, 0.4).Normalized;
            var side = Side(dir);
            for (int i = 0; i < classes.Length; i++)
                app.Fleet.SpawnAt("Showcase " + aggressor.Name + " " + (i + 1), classes[i], aggressor, body, dir * (2000 + i * 400), -dir, jd);
            for (int i = 0; i < classes.Length; i++)
                app.Fleet.SpawnAt("Showcase " + defender.Name + " " + (i + 1), classes[i], defender, body, dir * (2000 + i * 400) + side * 3000, dir, jd);
            return app.World.War.TryStartBattle(body, aggressor, jd);
        }

        // ---------------------------------------------------------------- surfaces, weather, asteroids

        static IEnumerator SurfaceTour(SolarSystemApp app, Func<string, IEnumerator> stop)
        {
            int moon = app.Catalog["Moon"].Index;
            int earth = app.Catalog["Earth"].Index;
            // Between the Earth-facing and sunlit directions: the near side, partly lit.
            View(app, moon, Blend(Dir(app, moon, earth), Dir(app, moon, 0), 0.5), 3.2);
            yield return Wait(3f);
            yield return stop("11-moon-near-side");
            // Low sun across the surface: LOLA relief along the terminator.
            View(app, moon, Blend(Dir(app, moon, 0), Side(Dir(app, moon, 0)), 0.75), 2.4);
            yield return Wait(3f);
            yield return stop("12-moon-terminator");

            foreach (var name in new[] { "Io", "Europa", "Ganymede" })
            {
                int i = app.Catalog[name].Index;
                View(app, i, Blend(Dir(app, i, 0), Side(Dir(app, i, 0)), 0.3), 3.0);
                yield return Wait(3f);
                yield return stop("13-" + name.ToLowerInvariant());
            }

            // Jupiter with the Great Red Spot turned toward the Sun, seen from just off the subsolar point.
            int jupiter = app.Catalog["Jupiter"].Index;
            app.Clock.Paused = true; // Jupiter turns 110 degrees in 3 s at the default warp
            app.Clock.SetJd(NextGrsNoon(app, jupiter));
            yield return null;
            var grs = GrsDirection(app, jupiter);
            View(app, jupiter, Blend(grs, Dir(app, jupiter, 0), 0.2), 2.1);
            yield return Wait(3f);
            yield return stop("14-jupiter-great-red-spot");
            View(app, jupiter, GrsDirection(app, jupiter), 1.35);
            yield return Wait(3f);
            yield return stop("15-jupiter-grs-close");
            // One System III rotation (9 h 55.5 m) later the spot is back at noon: the bands have moved
            // with their winds and the oval has turned counter-clockwise.
            app.Clock.SetJd(app.Clock.Jd + 360.0 / 870.536);
            yield return null;
            View(app, jupiter, GrsDirection(app, jupiter), 1.35);
            yield return Wait(3f);
            yield return stop("15b-jupiter-grs-close-one-rotation-later");
            app.Clock.Paused = false;

            foreach (var name in new[] { "Mars", "Titan", "Pluto", "Venus" })
            {
                int i = app.Catalog[name].Index;
                View(app, i, Blend(Dir(app, i, 0), Side(Dir(app, i, 0)), 0.35), name == "Titan" ? 3.0 : 2.6);
                yield return Wait(3.2f);
                yield return stop("16-" + name.ToLowerInvariant());
            }

            // The belt from above at gameplay scale, then at true scale.
            var sun = Target.OfBody(0);
            app.Select(sun);
            double span = app.Scale.LocalDistance(app.Catalog.Root, 4.0 * Astro.AuKm);
            app.SpaceCamera.FocusOn(sun, span * 1.9 / app.BodyDisplayRadius(0), 62f);
            yield return Wait(3f);
            yield return stop("17-asteroid-belt-gameplay");
            yield return Wait(3f);
            app.ToggleScale();
            yield return Wait(3.2f);
            app.SpaceCamera.FocusOn(sun, app.Scale.LocalDistance(app.Catalog.Root, 4.0 * Astro.AuKm) * 1.9 / app.BodyDisplayRadius(0), 62f);
            yield return Wait(3f);
            yield return stop("18-asteroid-belt-realistic");
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
                yield return stop("19-inside-the-belt");
            }
        }

        /// <summary>Who to watch next: a carrier with fighters out, else alternately each side's biggest ship.</summary>
        static Ship Subject(Battle battle, int shot)
        {
            Ship Biggest(int side) => battle.Units.Where(u => u.Ship != null && u.Present && u.Side == side)
                .OrderByDescending(u => u.Combat.Spec.Hull).Select(u => u.Ship).FirstOrDefault();
            if (shot % 3 == 2)
            {
                var carrier = battle.Units.FirstOrDefault(u => u.Ship != null && u.Present && u.Combat.Spec.Squadrons > 0);
                if (carrier != null && battle.Units.Any(u => u.Kind == UnitKind.Fighter && u.Present)) return carrier.Ship;
            }
            return Biggest(shot % 2) ?? Biggest(1 - shot % 2);
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
            app.SpaceCamera.FocusOn(t, radii, LevelPitchDeg(app, dir));
        }

        /// <summary>Frames a ship from scene direction <paramref name="dir"/> (unit, from the ship).</summary>
        static void ViewShip(SolarSystemApp app, Ship ship, DVec3 dir, double radii)
        {
            var t = Target.OfShip(ship);
            app.Select(t);
            var eye = app.ShipScene(ship) + dir * (app.ShipDisplayLength(ship) * 0.5 * radii * 1.3);
            var look = -dir;
            app.SpaceCamera.PlaceFree(eye, (float)(Math.Atan2(look.X, look.Z) * Mathf.Rad2Deg), (float)(-Math.Asin(look.Y) * Mathf.Rad2Deg));
            app.SpaceCamera.FocusOn(t, radii, LevelPitchDeg(app, dir));
        }

        /// <summary>FocusOn's pitch override is an elevation in the (possibly tilted) level frame, not world axes.</summary>
        static float LevelPitchDeg(SolarSystemApp app, DVec3 dir)
        {
            var world = new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);
            var local = Quaternion.Inverse(app.SpaceCamera.UpFrame) * world;
            return Mathf.Asin(Mathf.Clamp(local.y, -1, 1)) * Mathf.Rad2Deg;
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

        public static IEnumerator Wait(float seconds)
        {
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end) yield return null;
        }
    }
}
