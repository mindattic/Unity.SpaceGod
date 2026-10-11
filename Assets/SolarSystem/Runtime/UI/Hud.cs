using System;
using System.Collections.Generic;
using System.Globalization;
using SolarSystem.Cameras;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.UI
{
    /// <summary>
    /// Immediate-mode HUD. World overlay: billboard icons for anything below a few pixels, labels,
    /// selection brackets and an off-screen pointer. Panels: time and scale bar, navigator tree,
    /// selection details, camera status and the controls card.
    /// </summary>
    public sealed class Hud
    {
        readonly SolarSystemApp app;
        bool stylesReady;
        GUIStyle panel, title, text, small, minorLabel, label, labelShadow, button, buttonActive, row, rowSelected, toast;
        Texture2D panelTexture, rowHoverTexture, rowSelectedTexture, buttonTexture, buttonActiveTexture;

        Vector2 navigatorScroll;
        bool navigatorOpen = true;
        float lastRowClick = -10f;
        Target lastRowTarget = Target.None;

        static readonly Color Accent = new Color(0.45f, 0.85f, 1f);
        static readonly Color Dim = new Color(0.62f, 0.7f, 0.78f);

        public Hud(SolarSystemApp app) => this.app = app;

        public void Draw()
        {
            EnsureStyles();
            if (Event.current.type == EventType.Layout) app.UiRects.Clear();

            DrawWorldOverlay();
            if (!app.ShowHud) return;
            DrawTopBar();
            DrawNavigator();
            DrawSelection();
            DrawStatus();
            if (app.ShowHelp) DrawHelp();
            DrawToast();
        }

        // ---------------------------------------------------------------- world overlay

        void DrawWorldOverlay()
        {
            if (Event.current.type != EventType.Repaint) return;
            var catalog = app.Catalog;
            double ppu = app.PixelsPerUnitAtUnitDistance();

            for (int i = 0; i < catalog.Count; i++)
            {
                var body = catalog[i];
                var scene = app.BodyScene(i);
                if (!app.ToGui(scene, out var p, out var depth)) continue;
                float pxRadius = (float)(app.BodyDisplayRadius(i) / depth * ppu);

                if (body.Kind == BodyKind.Moon)
                {
                    // Moons hug their planet on screen until you are close; skip them until they separate.
                    if (app.ToGui(app.BodyScene(body.ParentIndex), out var pp, out _) && (pp - p).magnitude < 14f) continue;
                }
                bool occluded = pxRadius < 3 && app.Occluded(scene, i);
                if (occluded) continue;

                var tint = BodyTint(body);
                float iconSize = Icons.PixelSize(body);
                if (pxRadius < iconSize * 0.3f)
                    DrawIcon(Icons.ForBody(body), p, iconSize, tint);

                if (app.ShowLabels && (body.Kind != BodyKind.Moon || pxRadius > 1.5f || ParentSeparation(body, p) > 50))
                    DrawLabel(body.Name, p + new Vector2(Mathf.Max(pxRadius, iconSize * 0.5f) + 5, -8), tint, body.Kind == BodyKind.Moon);
            }

            var ships = app.Fleet.Ships;
            for (int i = 0; i < ships.Count; i++)
            {
                var ship = ships[i];
                var scene = app.ShipScene(ship);
                if (!app.ToGui(scene, out var p, out var depth)) continue;
                float px = (float)(app.ShipDisplayLength(ship) / depth * ppu);
                if (px >= 5) continue;
                if (app.Occluded(scene)) continue;
                if (ship.IsEscort && app.ToGui(app.ShipScene(ship.Leader), out var lp, out _) && (lp - p).magnitude < 7f) continue;
                var c = ship.Faction.Color;
                DrawIcon(Icons.ForShip(ship.Spec.Class), p, Icons.PixelSize(ship.Spec.Class), new Color(c.R, c.G, c.B, 0.95f));
            }

            DrawTargetMarker(app.Hovered, false);
            DrawTargetMarker(app.Selected, true);

            if (app.ShowLabels)
            {
                foreach (var ship in ships)
                {
                    var t = Target.OfShip(ship);
                    bool named = t == app.Hovered || t == app.Selected || app.SpaceCamera.FocusTarget == t;
                    if (!named && !ship.IsEscort && ship.Spec.IsMilitary)
                    {
                        // Name fleet flagships once they are big enough on screen to be a thing, not a dot.
                        if (app.ToGui(app.ShipScene(ship), out _, out var d))
                            named = app.ShipDisplayLength(ship) / d * ppu > 12;
                    }
                    if (!named) continue;
                    if (!app.ToGui(app.ShipScene(ship), out var p, out var depth) || app.Occluded(app.ShipScene(ship))) continue;
                    float px = (float)(app.ShipDisplayLength(ship) / depth * ppu);
                    var c = ship.Faction.Color;
                    DrawLabel(ship.Name, p + new Vector2(Mathf.Max(px * 0.5f, 10) + 4, -8), new Color(c.R, c.G, c.B), false);
                }
            }
        }

        float ParentSeparation(BodyDefinition moon, Vector2 p)
        {
            return app.ToGui(app.BodyScene(moon.ParentIndex), out var pp, out _) ? (pp - p).magnitude : 0;
        }

        void DrawTargetMarker(Target t, bool selected)
        {
            if (t.IsNone) return;
            var scene = app.TargetScene(t);
            double radius = app.TargetRadius(t);
            bool onScreen = app.ToGui(scene, out var p, out var depth) &&
                            p.x >= 0 && p.x <= Screen.width && p.y >= 0 && p.y <= Screen.height;
            if (!onScreen)
            {
                if (selected) DrawOffscreenPointer(scene, TargetName(t));
                return;
            }
            float px = (float)(radius / depth * app.PixelsPerUnitAtUnitDistance());
            float half = Mathf.Max(px * 1.18f, 13f);
            var color = selected ? Accent : new Color(1, 1, 1, 0.55f);
            DrawBrackets(new Rect(p.x - half, p.y - half, half * 2, half * 2), color, selected ? 2f : 1f);
        }

        void DrawBrackets(Rect r, Color color, float thickness)
        {
            float len = Mathf.Clamp(r.width * 0.28f, 5f, 18f);
            var old = GUI.color;
            GUI.color = color;
            var w = Icons.White;
            GUI.DrawTexture(new Rect(r.xMin, r.yMin, len, thickness), w);
            GUI.DrawTexture(new Rect(r.xMin, r.yMin, thickness, len), w);
            GUI.DrawTexture(new Rect(r.xMax - len, r.yMin, len, thickness), w);
            GUI.DrawTexture(new Rect(r.xMax - thickness, r.yMin, thickness, len), w);
            GUI.DrawTexture(new Rect(r.xMin, r.yMax - thickness, len, thickness), w);
            GUI.DrawTexture(new Rect(r.xMin, r.yMax - len, thickness, len), w);
            GUI.DrawTexture(new Rect(r.xMax - len, r.yMax - thickness, len, thickness), w);
            GUI.DrawTexture(new Rect(r.xMax - thickness, r.yMax - len, thickness, len), w);
            GUI.color = old;
        }

        void DrawOffscreenPointer(DVec3 scene, string name)
        {
            var rel = app.Relative(scene);
            var local = Quaternion.Inverse(app.SpaceCamera.Rotation) * rel;
            var dir = new Vector2(local.x, -local.y);
            if (dir.sqrMagnitude < 1e-12f) return;
            dir.Normalize();
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float margin = 34f;
            float sx = (center.x - margin) / Mathf.Max(Mathf.Abs(dir.x), 1e-4f);
            float sy = (center.y - margin) / Mathf.Max(Mathf.Abs(dir.y), 1e-4f);
            var p = center + dir * Mathf.Min(sx, sy);

            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 90f;
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, p);
            var old = GUI.color;
            GUI.color = Accent;
            GUI.DrawTexture(new Rect(p.x - 9, p.y - 9, 18, 18), Icons.ForShip(ShipClass.Cruiser));
            GUI.color = old;
            GUI.matrix = matrix;
            DrawLabel(name, p + new Vector2(dir.x < 0 ? 14 : -14 - label.CalcSize(new GUIContent(name)).x, dir.y < 0 ? 10 : -26), Accent, false);
        }

        void DrawIcon(Texture2D icon, Vector2 p, float size, Color tint)
        {
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, tint.a * 0.6f);
            GUI.DrawTexture(new Rect(p.x - size * 0.5f + 1, p.y - size * 0.5f + 1, size, size), icon);
            GUI.color = tint;
            GUI.DrawTexture(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), icon);
            GUI.color = old;
        }

        void DrawLabel(string s, Vector2 p, Color color, bool minor)
        {
            var style = minor ? minorLabel : label;
            var content = new GUIContent(s);
            var size = style.CalcSize(content);
            var r = new Rect(p.x, p.y, size.x, size.y);
            labelShadow.fontSize = style.fontSize;
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), content, labelShadow);
            var old = GUI.contentColor;
            GUI.contentColor = minor ? Color.Lerp(color, Color.gray, 0.3f) : color;
            GUI.Label(r, content, style);
            GUI.contentColor = old;
        }

        static Color BodyTint(BodyDefinition b)
        {
            var c = new Color(b.Color.R, b.Color.G, b.Color.B);
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            c = max > 0 ? c / max : Color.white;
            return Color.Lerp(c, Color.white, 0.35f);
        }

        // ---------------------------------------------------------------- panels

        Rect Panel(Rect r)
        {
            GUI.Box(r, GUIContent.none, panel);
            if (Event.current.type == EventType.Layout || Event.current.type == EventType.Repaint) RegisterUi(r);
            return r;
        }

        void RegisterUi(Rect r)
        {
            if (!app.UiRects.Contains(r)) app.UiRects.Add(r);
        }

        void DrawTopBar()
        {
            float w = 640, h = 52;
            var r = Panel(new Rect((Screen.width - w) * 0.5f, 10, w, h));
            var clock = app.Clock;

            GUI.Label(new Rect(r.x + 14, r.y + 7, 220, 20), clock.Utc.ToString("yyyy-MM-dd  HH:mm", CultureInfo.InvariantCulture) + " UTC", title);
            GUI.Label(new Rect(r.x + 14, r.y + 28, 220, 18), clock.Paused ? "PAUSED" : clock.Warp.Label, small);

            float bx = r.x + 200;
            if (GUI.Button(new Rect(bx, r.y + 12, 34, 28), "<<", button)) clock.Slower();
            if (GUI.Button(new Rect(bx + 38, r.y + 12, 44, 28), clock.Paused ? ">" : "||", clock.Paused ? buttonActive : button)) clock.Paused = !clock.Paused;
            if (GUI.Button(new Rect(bx + 86, r.y + 12, 34, 28), ">>", button)) clock.Faster();
            if (GUI.Button(new Rect(bx + 124, r.y + 12, 44, 28), "Now", button)) app.ResetToNow();

            float sx = r.x + 390;
            bool gameplay = app.ScaleGoal > 0.5;
            if (GUI.Button(new Rect(sx, r.y + 9, 236, 22), gameplay ? "GAMEPLAY SCALE   [G]" : "REALISTIC SCALE   [G]", buttonActive))
                app.ToggleScale();
            var bar = new Rect(sx, r.y + 36, 236, 6);
            GUI.DrawTexture(bar, buttonTexture);
            var old = GUI.color;
            GUI.color = Accent;
            float blend = (float)app.Scale.Blend;
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * blend, bar.height), Icons.White);
            GUI.color = old;
            GUI.Label(new Rect(sx - 2, r.y + 40, 120, 14), "real", small);
            var g = new GUIContent("gameplay");
            GUI.Label(new Rect(sx + 238 - small.CalcSize(g).x, r.y + 40, 120, 14), g, small);
        }

        void DrawNavigator()
        {
            float w = 268;
            float h = navigatorOpen ? Mathf.Min(Screen.height - 24, 640) : 34;
            var r = Panel(new Rect(10, 10, w, h));
            if (GUI.Button(new Rect(r.x + 8, r.y + 6, w - 16, 22), (navigatorOpen ? "[-]  " : "[+]  ") + "NAVIGATOR", title)) navigatorOpen = !navigatorOpen;
            if (!navigatorOpen) return;

            var catalog = app.Catalog;
            var rows = new List<(string text, Target target, int indent, Color color)>();
            AddBody(rows, 0, 0);
            foreach (var faction in app.Fleet.Factions)
            {
                rows.Add(("", Target.None, 0, Color.clear));
                var fc = new Color(faction.Color.R, faction.Color.G, faction.Color.B);
                rows.Add((faction.Name.ToUpperInvariant(), Target.None, 0, fc));
                foreach (var ship in app.Fleet.Ships)
                {
                    if (ship.Faction != faction || ship.IsEscort) continue;
                    rows.Add((ship.Name + "  " + ShortStatus(ship), Target.OfShip(ship), 1, fc));
                    foreach (var escort in app.Fleet.Ships)
                        if (escort.Leader == ship) rows.Add((escort.Name + "  · " + escort.Spec.DisplayName, Target.OfShip(escort), 2, Color.Lerp(fc, Color.gray, 0.35f)));
                }
            }

            const float rowH = 19;
            var view = new Rect(r.x + 4, r.y + 34, w - 8, h - 40);
            var content = new Rect(0, 0, w - 26, rows.Count * rowH);
            navigatorScroll = GUI.BeginScrollView(view, navigatorScroll, content);
            for (int i = 0; i < rows.Count; i++)
            {
                var (s, target, indent, color) = rows[i];
                var rr = new Rect(0, i * rowH, content.width, rowH);
                if (target.IsNone)
                {
                    if (s.Length == 0) continue;
                    var oldC = GUI.contentColor;
                    GUI.contentColor = color;
                    GUI.Label(new Rect(rr.x + 6, rr.y + 2, rr.width, rr.height), s, small);
                    GUI.contentColor = oldC;
                    continue;
                }
                bool selected = app.Selected == target;
                var old = GUI.contentColor;
                GUI.contentColor = selected ? Color.white : color;
                if (GUI.Button(new Rect(rr.x + 4 + indent * 14, rr.y, rr.width - indent * 14 - 4, rr.height), s, selected ? rowSelected : row))
                {
                    bool doubleClick = lastRowTarget == target && Time.unscaledTime - lastRowClick < 0.4f;
                    app.Select(target);
                    if (doubleClick) app.SpaceCamera.FocusOn(target);
                    lastRowClick = Time.unscaledTime;
                    lastRowTarget = target;
                }
                GUI.contentColor = old;
            }
            GUI.EndScrollView();
        }

        void AddBody(List<(string, Target, int, Color)> rows, int index, int indent)
        {
            var b = app.Catalog[index];
            var tint = BodyTint(b);
            rows.Add((b.Name, Target.OfBody(index), indent, b.Kind == BodyKind.Moon ? Color.Lerp(tint, Color.gray, 0.3f) : tint));
            foreach (int child in app.Catalog.ChildrenOf(index)) AddBody(rows, child, Math.Min(indent + 1, 2));
        }

        string ShortStatus(Ship ship)
        {
            var c = ship.Controller;
            return c.Phase == ShipPhase.Transit ? "-> " + app.Catalog[c.Orbit.Body].Name : "@ " + app.Catalog[c.Orbit.Body].Name;
        }

        void DrawSelection()
        {
            var t = app.Selected;
            if (t.IsNone) return;
            var lines = new List<(string key, string value)>();
            string heading, sub;
            Color headingColor = Accent;

            if (t.IsBody)
            {
                var b = app.Catalog[t.Body];
                heading = b.Name;
                sub = KindName(b);
                var eph = app.Ephemeris;
                lines.Add(("Radius", Km(b.RadiusKm)));
                if (b.ParentIndex >= 0)
                {
                    var parent = app.Catalog[b.ParentIndex];
                    var k = b.Elements.At(app.Clock.Jd);
                    double dist = eph.Relative(t.Body).Length;
                    if (b.ParentIndex == 0) lines.Add(("From Sun", Au(dist) + "  (" + LightTime(dist) + " light)"));
                    else lines.Add(("From " + parent.Name, Km(dist)));
                    lines.Add(("Orbital speed", eph.OrbitalSpeedKmS(t.Body).ToString("0.00", CultureInfo.InvariantCulture) + " km/s"));
                    lines.Add(("Period", Duration(k.PeriodDays)));
                    lines.Add(("Eccentricity", k.E.ToString("0.0000", CultureInfo.InvariantCulture)));
                    lines.Add(("Semi-major axis", b.ParentIndex == 0 ? Au(k.A) : Km(k.A)));
                }
                if (b.RotationHours != 0)
                    lines.Add(("Day (sidereal)", (Math.Abs(b.RotationHours) < 48 ? Math.Abs(b.RotationHours).ToString("0.00", CultureInfo.InvariantCulture) + " h" : Duration(Math.Abs(b.RotationHours) / 24)) + (b.RotationHours < 0 ? " retro" : "")));
                lines.Add(("Surface gravity", (b.Gm / (b.RadiusKm * b.RadiusKm) * 1000 / 9.80665).ToString("0.000", CultureInfo.InvariantCulture) + " g"));
                if (!string.IsNullOrEmpty(b.Summary)) lines.Add(("", b.Summary));
            }
            else
            {
                var ship = t.Ship;
                var c = ship.Controller;
                var st = app.ShipStateOf(ship);
                heading = ship.Name;
                sub = ship.Faction.Name + " " + ship.Spec.DisplayName.ToLowerInvariant();
                headingColor = new Color(ship.Faction.Color.R, ship.Faction.Color.G, ship.Faction.Color.B);
                lines.Add(("Length", Km(ship.Spec.LengthKm)));
                lines.Add(("Drive", ship.Spec.AccelG.ToString("0.##", CultureInfo.InvariantCulture) + " g brachistochrone"));
                if (ship.IsEscort) lines.Add(("Escorting", ship.Leader.Name));
                if (c.Phase == ShipPhase.Transit)
                {
                    lines.Add(("Status", (st.Progress < 0.5 ? "Burning toward " : "Braking for ") + app.Catalog[c.Orbit.Body].Name));
                    lines.Add(("From", app.Catalog[c.OriginBody].Name));
                    lines.Add(("Speed", st.SpeedKmS.ToString("N0", CultureInfo.InvariantCulture) + " km/s"));
                    lines.Add(("Remaining", Km(c.PathLength * (1 - st.Progress))));
                    lines.Add(("ETA", Duration(Fleet.RemainingDays(c, app.Clock.Jd))));
                }
                else
                {
                    var body = app.Catalog[c.Orbit.Body];
                    lines.Add(("Status", "Holding orbit at " + body.Name));
                    lines.Add(("Altitude", Km(c.Orbit.RadiusKm - body.RadiusKm)));
                    lines.Add(("Orbital speed", c.Orbit.SpeedKmS.ToString("0.00", CultureInfo.InvariantCulture) + " km/s"));
                    lines.Add(("Next", app.Catalog[c.DestinationBody].Name + " in " + Duration(Fleet.RemainingDays(c, app.Clock.Jd))));
                }
                if (!string.IsNullOrEmpty(c.Cargo)) lines.Add(("Cargo", c.Cargo));
                else if (ship.Spec.IsMilitary) lines.Add(("Orders", "Patrol " + RouteText(c)));
                lines.Add(("Trips", c.TripsCompleted.ToString(CultureInfo.InvariantCulture)));
            }

            float w = 340;
            float h = 64 + lines.Count * 19 + 30;
            var r = Panel(new Rect(Screen.width - w - 10, Screen.height - h - 10, w, h));
            var old = GUI.contentColor;
            GUI.contentColor = headingColor;
            GUI.Label(new Rect(r.x + 14, r.y + 8, w - 28, 24), heading, title);
            GUI.contentColor = Dim;
            GUI.Label(new Rect(r.x + 14, r.y + 30, w - 28, 18), sub, small);
            GUI.contentColor = old;

            float y = r.y + 54;
            foreach (var (key, value) in lines)
            {
                if (key.Length == 0)
                {
                    GUI.Label(new Rect(r.x + 14, y, w - 28, 36), value, small);
                    y += 19;
                    continue;
                }
                GUI.contentColor = Dim;
                GUI.Label(new Rect(r.x + 14, y, 120, 19), key, small);
                GUI.contentColor = Color.white;
                GUI.Label(new Rect(r.x + 132, y, w - 146, 19), value, text);
                y += 19;
            }
            GUI.contentColor = old;

            bool focused = app.SpaceCamera.FocusTarget == t && app.SpaceCamera.Mode != SpaceCamera.CameraMode.Free;
            if (GUI.Button(new Rect(r.x + 14, r.yMax - 32, 150, 24), focused ? "Re-frame  [F]" : "Focus  [F]", buttonActive)) app.FocusSelected();
            if (GUI.Button(new Rect(r.x + 172, r.yMax - 32, 150, 24), "Deselect  [Esc]", button)) app.Select(Target.None);
        }

        string RouteText(Ship s)
        {
            if (s.Route == null) return "";
            var names = new string[s.Route.Length];
            for (int i = 0; i < names.Length; i++) names[i] = app.Catalog[s.Route[i]].Name;
            return string.Join(" › ", names);
        }

        void DrawStatus()
        {
            var cam = app.SpaceCamera;
            string s;
            switch (cam.Mode)
            {
                case SpaceCamera.CameraMode.Focus:
                    s = "FOCUS  ·  " + TargetName(cam.FocusTarget) + "   (WASD to swim free, wheel to zoom, right-drag to orbit)";
                    break;
                case SpaceCamera.CameraMode.Travel:
                    s = "TRAVELLING  ·  " + TargetName(cam.FocusTarget);
                    break;
                default:
                    double kmPerSec = SceneSpeedToKmS(cam.CurrentSpeed);
                    s = "FREE  ·  swim x" + cam.SpeedMultiplier.ToString("0.##", CultureInfo.InvariantCulture) + "  ·  " + SpeedText(kmPerSec) +
                        "  ·  near " + app.Catalog[cam.FrameBody].Name;
                    break;
            }
            var content = new GUIContent(s);
            float w = text.CalcSize(content).x + 28;
            var r = Panel(new Rect((Screen.width - w) * 0.5f, Screen.height - 40, w, 30));
            GUI.Label(new Rect(r.x + 14, r.y + 6, w, 20), content, text);
        }

        /// <summary>Converts a scene speed near the camera into an equivalent true speed, using the local scale.</summary>
        double SceneSpeedToKmS(double sceneSpeed)
        {
            var cam = app.SpaceCamera;
            var body = app.Catalog[cam.FrameBody];
            double sceneDist = (cam.ScenePosition - app.BodyScene(cam.FrameBody)).Length;
            double trueDist = app.Scale.InverseLocalDistance(body, sceneDist);
            double perUnit = sceneDist > 0 ? trueDist / sceneDist : ScaleModel.KmPerUnit;
            return sceneSpeed * perUnit;
        }

        void DrawHelp()
        {
            string[] help =
            {
                "W A S D   swim          Q / E  or  C / Space   down / up",
                "Shift   boost           Ctrl   fine             Wheel   swim speed / zoom",
                "Right-drag   look / orbit                Left-click   select    (double: focus)",
                "F   focus selection     Esc   leave focus / deselect     Home   system view",
                "G  or  `   toggle GAMEPLAY / REALISTIC scale",
                "1-9, 0   planets (twice to focus)    Tab   cycle ships",
                ",  .   time warp     P   pause     T   now     O  orbits   L  labels   H  help   U  HUD",
            };
            float w = 470, h = 22 + help.Length * 17;
            var r = Panel(new Rect(10, Screen.height - h - 10, w, h));
            for (int i = 0; i < help.Length; i++)
                GUI.Label(new Rect(r.x + 12, r.y + 10 + i * 17, w - 20, 17), help[i], small);
        }

        void DrawToast()
        {
            if (string.IsNullOrEmpty(app.Toast) || app.ToastAge > 3.5f) return;
            float a = Mathf.Clamp01(3.5f - app.ToastAge);
            var content = new GUIContent(app.Toast);
            float w = toast.CalcSize(content).x + 40;
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, a);
            var r = new Rect((Screen.width - w) * 0.5f, 72, w, 34);
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(r.x + 20, r.y + 7, w, 22), content, toast);
            GUI.color = old;
        }

        // ---------------------------------------------------------------- formatting

        string TargetName(Target t) => t.IsBody ? app.Catalog[t.Body].Name : t.IsShip ? t.Ship.Name : "";

        static string KindName(BodyDefinition b)
        {
            switch (b.Kind)
            {
                case BodyKind.Star: return "Star";
                case BodyKind.DwarfPlanet: return "Dwarf planet";
                case BodyKind.Moon: return "Moon";
                default: return b.Name == "Jupiter" || b.Name == "Saturn" ? "Gas giant" : b.Name == "Uranus" || b.Name == "Neptune" ? "Ice giant" : "Terrestrial planet";
            }
        }

        static string Km(double km)
        {
            if (km < 1) return (km * 1000).ToString("N0", CultureInfo.InvariantCulture) + " m";
            if (km < 1e7) return km.ToString("N0", CultureInfo.InvariantCulture) + " km";
            return Au(km);
        }

        static string Au(double km) => (km / Astro.AuKm).ToString(km / Astro.AuKm < 10 ? "0.000" : "0.00", CultureInfo.InvariantCulture) + " AU";

        static string LightTime(double km)
        {
            double s = km / Astro.LightSpeedKmPerSec;
            return s < 120 ? s.ToString("0", CultureInfo.InvariantCulture) + " s" : s < 7200 ? (s / 60).ToString("0.0", CultureInfo.InvariantCulture) + " min" : (s / 3600).ToString("0.0", CultureInfo.InvariantCulture) + " h";
        }

        static string Duration(double days)
        {
            if (days < 1.0 / 24) return (days * 1440).ToString("0", CultureInfo.InvariantCulture) + " min";
            if (days < 2) return (days * 24).ToString("0.0", CultureInfo.InvariantCulture) + " h";
            if (days < 400) return days.ToString("0.0", CultureInfo.InvariantCulture) + " days";
            return (days / 365.25).ToString("0.00", CultureInfo.InvariantCulture) + " years";
        }

        static string SpeedText(double kmPerSec)
        {
            if (kmPerSec > Astro.LightSpeedKmPerSec * 0.5) return (kmPerSec / Astro.LightSpeedKmPerSec).ToString("0.0", CultureInfo.InvariantCulture) + " c";
            if (kmPerSec >= 1) return kmPerSec.ToString("N0", CultureInfo.InvariantCulture) + " km/s";
            return (kmPerSec * 1000).ToString("N0", CultureInfo.InvariantCulture) + " m/s";
        }

        // ---------------------------------------------------------------- styles

        void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;
            panelTexture = Solid(new Color(0.03f, 0.05f, 0.08f, 0.78f), new Color(0.3f, 0.6f, 0.75f, 0.45f));
            rowHoverTexture = Solid(new Color(0.2f, 0.45f, 0.6f, 0.3f), Color.clear);
            rowSelectedTexture = Solid(new Color(0.2f, 0.55f, 0.75f, 0.45f), Color.clear);
            buttonTexture = Solid(new Color(0.12f, 0.18f, 0.24f, 0.95f), new Color(0.3f, 0.5f, 0.6f, 0.6f));
            buttonActiveTexture = Solid(new Color(0.1f, 0.3f, 0.42f, 0.95f), new Color(0.45f, 0.85f, 1f, 0.8f));

            panel = new GUIStyle { normal = { background = panelTexture }, border = new RectOffset(2, 2, 2, 2) };
            title = new GUIStyle { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            text = new GUIStyle { fontSize = 13, normal = { textColor = Color.white }, clipping = TextClipping.Clip };
            small = new GUIStyle { fontSize = 11, normal = { textColor = Dim }, wordWrap = true };
            label = new GUIStyle { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            minorLabel = new GUIStyle { fontSize = 11, normal = { textColor = Color.white } };
            labelShadow = new GUIStyle(label) { normal = { textColor = new Color(0, 0, 0, 0.85f) } };
            button = new GUIStyle
            {
                fontSize = 12, alignment = TextAnchor.MiddleCenter, border = new RectOffset(2, 2, 2, 2),
                normal = { background = buttonTexture, textColor = Color.white },
                hover = { background = buttonActiveTexture, textColor = Color.white },
                active = { background = buttonActiveTexture, textColor = Accent },
            };
            buttonActive = new GUIStyle(button) { normal = { background = buttonActiveTexture, textColor = Color.white }, fontStyle = FontStyle.Bold };
            row = new GUIStyle
            {
                fontSize = 12, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(6, 4, 0, 0), clipping = TextClipping.Clip,
                normal = { textColor = Color.white }, hover = { background = rowHoverTexture, textColor = Color.white },
            };
            rowSelected = new GUIStyle(row) { normal = { background = rowSelectedTexture, textColor = Color.white }, fontStyle = FontStyle.Bold };
            toast = new GUIStyle { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Accent } };
        }

        static Texture2D Solid(Color fill, Color edge)
        {
            const int n = 8;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, filterMode = FilterMode.Point };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    t.SetPixel(x, y, x == 0 || y == 0 || x == n - 1 || y == n - 1 ? (edge.a > 0 ? edge : fill) : fill);
            t.Apply();
            return t;
        }
    }
}
