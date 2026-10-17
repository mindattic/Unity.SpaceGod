using System;
using System.Collections.Generic;
using System.Globalization;
using SolarSystem.Cameras;
using SolarSystem.Core;
using SolarSystem.Rendering;
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

        /// <summary>The crate currently picked up by a source click in the cargo panel, awaiting a
        /// destination click; null means nothing is picked up.</summary>
        CargoItem cargoSelected;
        CargoHold cargoSelectedHold; // the hold the picked-up crate is in: a crate never carries over to another ship's hold

        /// <summary>Crates per manual Buy/Sell click in the market board, shared by every good's row.</summary>
        int tradeCrates = 1;

        /// <summary>Dev/sandbox spawn panel: which faction the next spawned ship joins.</summary>
        bool spawnHostile;

        /// <summary>Collapse state for panels that can sit over the 3D view and get in the way --
        /// same "[-]/[+] TITLE" bar <see cref="DrawNavigator"/> already uses, so Market/Cargo/Help can
        /// be shrunk to just their title bar without closing them outright.</summary>
        bool marketOpen = true, cargoOpen = true, helpOpen = true;

        /// <summary>The outfitting panel's chosen slot (-1: none) and how far its slot list is scrolled.</summary>
        int outfitSlot = -1;
        Vector2 outfitScroll;

        static readonly Color Accent = new Color(0.45f, 0.85f, 1f);
        static readonly Color Dim = new Color(0.62f, 0.7f, 0.78f);

        /// <summary>An unidentified contact: no faction colour until the scanners know whose it is.</summary>
        static readonly Color ContactGrey = new Color(0.78f, 0.8f, 0.82f);

        /// <summary>The piloted ship's own sensor picture, or null when only watching (then everything is shown).</summary>
        ContactBook Picture => app.IsPiloting && app.Piloted.Ship != null ? app.World.Sensors?.Player : null;

        public Hud(SolarSystemApp app) => this.app = app;

        public void Draw()
        {
            EnsureStyles();
            if (Event.current.type == EventType.Layout) app.UiRects.Clear();

            // Spawn & Pilot is its own full screen, not an overlay panel -- everything else waits.
            if (app.ShowSpawn) { DrawSpawn(); return; }

            if (app.Travel.Active) DrawTravel();
            else DrawWorldOverlay();
            if (!app.ShowHud)
            {
                if (app.ShowSaves) DrawSaves();
                return;
            }
            DrawFlightMarkers();
            DrawTopBar();
            if (app.ShowNews) DrawNews();
            else DrawTicker();
            DrawNavigator();
            DrawSelection();
            DrawStatus();
            DrawSalvage();
            DrawHail();
            if (!app.ShowMarket) DrawOpportunityBoard();
            if (app.ShowMarket) DrawMarket();
            if (app.ShowCargo) DrawCargo();
            if (app.ShowOutfit) DrawOutfit();
            if (app.ShowBounties) DrawBounties();
            if (app.ShowHelp) DrawHelp();
            if (app.ShowSaves) DrawSaves();
            DrawToast();
        }

        // ---------------------------------------------------------------- save and load (U.SPC-T67)

        SaveHeader[] saveHeaders;
        float savesReadAt = -10f;

        /// <summary>
        /// The save slots (F6): the quicksave, three slots to save into and the rotating autosaves, each with
        /// where and when it was saved, who was flying and their purse. Save and Load act at the next frame.
        /// </summary>
        void DrawSaves()
        {
            var slots = app.Saves;
            if (slots == null) return;
            var names = new System.Collections.Generic.List<string>(SaveSlots.AllSlots());
            if (saveHeaders == null || saveHeaders.Length != names.Count || Time.unscaledTime - savesReadAt > 1f)
            {
                saveHeaders = new SaveHeader[names.Count];
                for (int i = 0; i < names.Count; i++) saveHeaders[i] = slots.Header(names[i]);
                savesReadAt = Time.unscaledTime;
            }
            const float w = 720, rowH = 24;
            float h = 82 + names.Count * rowH;
            var r = Panel(new Rect((Screen.width - w) * 0.5f, 170, w, h));
            GUI.Label(new Rect(r.x + 12, r.y + 6, w - 60, 22), "SAVE & LOAD", title);
            if (GUI.Button(new Rect(r.xMax - 34, r.y + 6, 24, 22), "X", button)) app.ShowSaves = false;
            var old = GUI.contentColor;
            GUI.contentColor = Dim;
            string blocked = app.SaveBlocker();
            GUI.Label(new Rect(r.x + 12, r.y + 30, w - 24, 18), blocked ??
                "F5 quicksave   F8 quickload   F6 close   ·   autosaves every " + (SolarSystemApp.AutosaveIntervalS / 60f).ToString("0", CultureInfo.InvariantCulture) + " minutes and on quit", small);
            GUI.contentColor = old;
            for (int i = 0; i < names.Count; i++)
            {
                string slot = names[i];
                var hdr = saveHeaders[i];
                var row0 = new Rect(r.x + 8, r.y + 54 + i * rowH, w - 16, rowH - 2);
                GUI.Box(row0, GUIContent.none, row);
                GUI.Label(new Rect(row0.x + 6, row0.y + 2, 100, 18), SolarSystemApp.SlotName(slot), text);
                GUI.contentColor = hdr == null ? Dim : Color.white;
                string line = hdr == null ? "empty" : SolarSystemApp.DescribeSave(hdr) + "   ·   saved " + hdr.SavedUtc.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.InvariantCulture);
                GUI.Label(new Rect(row0.x + 108, row0.y + 3, w - 290, 18), line, small);
                GUI.contentColor = old;
                bool autosave = slot.StartsWith("autosave-", StringComparison.Ordinal);
                if (!autosave && blocked == null && GUI.Button(new Rect(row0.xMax - 150, row0.y + 1, 70, 20), "Save", button))
                {
                    app.RequestSave(slot);
                    savesReadAt = -10f;
                }
                if (hdr != null && blocked == null && GUI.Button(new Rect(row0.xMax - 74, row0.y + 1, 70, 20), "Load", button))
                    app.RequestLoad(slot);
            }
        }

        // ---------------------------------------------------------------- world overlay

        /// <summary>Between the stars: each star named, the two systems highlighted, and what the camera is doing.</summary>
        void DrawTravel()
        {
            var travel = app.Travel;
            if (Event.current.type == EventType.Repaint && app.Interstellar.Visible && app.ShowLabels)
            {
                foreach (var s in app.Interstellar.Stars)
                {
                    if (!app.RelativeToGui(s.Relative, out var p)) continue;
                    var c = Color.Lerp(s.Color, Color.white, 0.4f);
                    if (s.Label == null) continue;
                    bool system = s.System;
                    string name = s.Label;
                    double ly = s.PositionLy.Length;
                    DrawLabel(system ? name.ToUpperInvariant() : name + (ly > 0.01 ? "  " + ly.ToString("0.0", CultureInfo.InvariantCulture) + " ly" : ""),
                        p + new Vector2(10, -8), system ? Accent : new Color(c.r, c.g, c.b, 0.75f), !system);
                }
            }
            if (string.IsNullOrEmpty(travel.Banner)) return;
            var content = new GUIContent(travel.Banner);
            float w = Mathf.Max(toast.CalcSize(content).x + 60, 420);
            var r = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.18f, w, 52);
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(r.x + 30, r.y + 8, w - 60, 22), content, toast);
            var bar = new Rect(r.x + 30, r.y + 36, w - 60, 4);
            GUI.DrawTexture(bar, buttonTexture);
            var old = GUI.color;
            GUI.color = Accent;
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * travel.Overall, bar.height), Icons.White);
            GUI.color = old;
        }

        void DrawWorldOverlay()
        {
            if (Event.current.type != EventType.Repaint) return;
            var catalog = app.Catalog;
            double ppu = app.PixelsPerUnitAtUnitDistance();
            DrawSkyMarkers();
            DrawStructureLabels(ppu);

            for (int i = 0; i < catalog.Count; i++)
            {
                var body = catalog[i];
                if (!body.IsVisible) continue;
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
                    DrawLabel(body.Name + StatusMark(body) + HavenMark(body), p + new Vector2(Mathf.Max(pxRadius, iconSize * 0.5f) + 5, -8), tint, body.Kind == BodyKind.Moon);
            }

            var ships = app.Fleet.Ships;
            // At the helm the player sees only what the ship's own scanners hold (U.SPC-T66): an unknown blip, a hull
            // class, or -- identified -- whose it is; a track gone cold is drawn faded where it was last seen.
            var picture = Picture;
            for (int i = 0; i < ships.Count; i++)
            {
                var ship = ships[i];
                if (ship.Destroyed || ship.Controller.Phase == ShipPhase.Landed) continue;
                var contact = picture != null && ship != app.Piloted.Ship ? picture.Of(ship) : null;
                if (picture != null && ship != app.Piloted.Ship && contact == null) continue;
                var scene = app.ShipScene(ship);
                if (!app.ToGui(scene, out var p, out var depth)) continue;
                float px = (float)(app.ShipDisplayLength(ship) / depth * ppu);
                if (px >= 5) continue;
                if (app.Occluded(scene)) continue;
                if (ship.IsEscort && app.ToGui(app.ShipScene(ship.Leader), out var lp, out _) && (lp - p).magnitude < 7f) continue;
                var c = ship.Faction.Color;
                if (contact == null)
                {
                    DrawIcon(Icons.ForShip(ship.Spec.Class), p, Icons.PixelSize(ship.Spec.Class), new Color(c.R, c.G, c.B, 0.95f));
                    continue;
                }
                float alpha = contact.Cold ? 0.4f : 0.95f;
                if (contact.Level == ContactLevel.Blip) DrawIcon(Icons.Blip, p, 12, new Color(ContactGrey.r, ContactGrey.g, ContactGrey.b, alpha));
                else
                {
                    var tint = contact.Level == ContactLevel.Identified ? new Color(c.R, c.G, c.B, alpha) : new Color(ContactGrey.r, ContactGrey.g, ContactGrey.b, alpha);
                    DrawIcon(Icons.ForShip(ship.Spec.Class), p, Icons.PixelSize(ship.Spec.Class), tint);
                }
            }

            DrawTargetMarker(app.Hovered, false);
            DrawTargetMarker(app.Selected, true);
            DrawOpportunityMarkers();

            if (app.ShowLabels)
            {
                foreach (var ship in ships)
                {
                    if (ship.Destroyed) continue;
                    var contact = picture != null && ship != app.Piloted.Ship ? picture.Of(ship) : null;
                    if (picture != null && ship != app.Piloted.Ship && contact == null) continue;
                    var t = Target.OfShip(ship);
                    bool named = t == app.Hovered || t == app.Selected || app.SpaceCamera.FocusTarget == t;
                    // A flagship is named on sight only once the scanners have identified it.
                    if (!named && !ship.IsEscort && ship.Spec.IsMilitary && (contact == null || contact.Level == ContactLevel.Identified))
                    {
                        // Name fleet flagships once they are big enough on screen to be a thing, not a dot.
                        if (app.ToGui(app.ShipScene(ship), out _, out var d))
                            named = app.ShipDisplayLength(ship) / d * ppu > 12;
                    }
                    if (!named) continue;
                    if (!app.ToGui(app.ShipScene(ship), out var p, out var depth) || app.Occluded(app.ShipScene(ship))) continue;
                    float px = (float)(app.ShipDisplayLength(ship) / depth * ppu);
                    var c = ship.Faction.Color;
                    if (contact == null)
                    {
                        DrawLabel(ship.Name, p + new Vector2(Mathf.Max(px * 0.5f, 10) + 4, -8), new Color(c.R, c.G, c.B), false);
                        continue;
                    }
                    // A contact is named by what the scanners make of it: unknown, its class, or its name.
                    var tint = contact.Level == ContactLevel.Identified ? new Color(c.R, c.G, c.B) : ContactGrey;
                    if (contact.Cold) tint.a = 0.5f;
                    DrawLabel(contact.Label + (contact.Cold ? "  (contact lost)" : ""), p + new Vector2(Mathf.Max(px * 0.5f, 10) + 4, -8), tint, contact.Level != ContactLevel.Identified);
                }
            }
        }

        /// <summary>Unconfirmed worlds are marked as such wherever they are named.</summary>
        static string StatusMark(BodyDefinition b) =>
            b.Status == BodyStatus.Candidate ? "  (candidate)" : b.Status == BodyStatus.Disputed ? "  (disputed)" : "";

        /// <summary>The other simulated systems shine in this one's sky: Alpha Centauri from Sol, the Sun in Cassiopeia from Alpha Centauri.</summary>
        void DrawSkyMarkers()
        {
            if (!app.ShowLabels) return;
            foreach (var other in app.Galaxy.Systems)
            {
                if (other == app.ActiveSystem) continue;
                var dir = app.SkyDirection(other) * 1e6f;
                if (!app.RelativeToGui(dir, out var p)) continue;
                if (p.x < 0 || p.y < 0 || p.x > Screen.width || p.y > Screen.height) continue;
                var c = new Color(1f, 0.95f, 0.8f, 0.9f);
                DrawIcon(Icons.ForBody(other.Catalog.Root.IsStar ? other.Catalog.Root : other.Catalog[1]), p, 14, c);
                string name = other.Catalog.Root.Name == "Sun" ? "Sun  (Sol System)" : other.Name;
                double ly = Galaxy.DistanceLy(app.ActiveSystem, other);
                DrawLabel(name + "  " + ly.ToString("0.00", CultureInfo.InvariantCulture) + " ly", p + new Vector2(10, -8), c, true);
            }
        }

        /// <summary>Yards, factories, spacecraft and docking rings are named once they are drawn big enough to see.</summary>
        void DrawStructureLabels(double ppu)
        {
            var views = app.Structures;
            if (views == null || !app.ShowLabels) return;
            foreach (var kv in views.Scenes)
            {
                if (!app.ToGui(kv.Value, out var p, out var depth)) continue;
                float px = (float)(views.Sizes[kv.Key] / depth * ppu);
                var t = Target.OfStructure(kv.Key);
                if (px < 6 && t != app.Selected && t != app.Hovered) continue;
                if (app.Occluded(kv.Value)) continue;
                var c = kv.Key.Owner != null ? new Color(kv.Key.Owner.Color.R, kv.Key.Owner.Color.G, kv.Key.Owner.Color.B) : new Color(0.85f, 0.88f, 0.92f);
                DrawLabel(kv.Key.Name, p + new Vector2(Mathf.Max(px * 0.5f, 6) + 4, -8), c, true);
            }
            foreach (var kv in views.RingScenes)
            {
                if (!app.ToGui(kv.Value, out var p, out var depth)) continue;
                float px = (float)(views.Sizes[kv.Key] / depth * ppu);
                var t = Target.OfRing(kv.Key);
                if (px < 2.5f && t != app.Selected && t != app.Hovered) continue;
                if (app.Occluded(kv.Value)) continue;
                int queued = 0;
                foreach (var s in app.Fleet.Ships)
                    if (!s.Destroyed && s.Phase == ShipPhase.Queued && s.Ring == kv.Key) queued++;
                DrawLabel(kv.Key.Name + (queued > 0 ? "  · " + queued + " queued" : ""), p + new Vector2(Mathf.Max(px * 0.5f, 6) + 4, -8), new Color(0.6f, 0.9f, 1f), true);
            }
        }

        float ParentSeparation(BodyDefinition moon, Vector2 p)
        {
            return app.ToGui(app.BodyScene(moon.ParentIndex), out var pp, out _) ? (pp - p).magnitude : 0;
        }

        // ---------------------------------------------------------------- flight markers (U.SPC-T59)

        static readonly Color ProgradeColor = new Color(0.55f, 1f, 0.6f, 0.95f);
        static readonly Color RetrogradeColor = new Color(1f, 0.62f, 0.35f, 0.95f);

        /// <summary>
        /// While piloting, the flight instruments a Newtonian hull needs: a boresight where the nose
        /// points, the prograde marker where the hull is actually going (its velocity relative to the
        /// body it flies near) and the retrograde marker opposite it, joined to the boresight by the
        /// velocity vector so a slide (flight assist off, or a hull still killing its drift) reads at a
        /// glance, and a readout of flight assist, speed and how far the track is off the nose.
        /// </summary>
        void DrawFlightMarkers()
        {
            if (!app.IsPiloting || app.Travel.Active || Event.current.type != EventType.Repaint) return;
            var vel = app.PilotVelocityKmS;
            double speed = vel.Length;
            bool noseOn = DirectionToGui(app.PilotForward, out var nose);
            if (noseOn) DrawBoresight(nose, new Color(1, 1, 1, 0.8f));

            double driftDeg = 0;
            if (speed > 1e-6)
            {
                var dir = vel / speed;
                driftDeg = Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(dir, app.PilotForward)))) * 180 / Math.PI;
                if (DirectionToGui(dir, out var pro))
                {
                    if (noseOn && (pro - nose).sqrMagnitude > 400) DrawLine(nose, pro, new Color(ProgradeColor.r, ProgradeColor.g, ProgradeColor.b, 0.45f), 1.5f);
                    DrawIcon(Icons.Prograde, pro, 28, ProgradeColor);
                    DrawLabel(SpeedText(speed), pro + new Vector2(17, -8), ProgradeColor, true);
                }
                if (DirectionToGui(-dir, out var retro)) DrawIcon(Icons.Retrograde, retro, 28, RetrogradeColor);
            }

            bool assist = app.PilotFlightAssist;
            string readout = (assist ? "FLIGHT ASSIST ON" : "FLIGHT ASSIST OFF") + "   " + SpeedText(speed) +
                             (speed > 1e-6 ? "   track " + driftDeg.ToString("0", CultureInfo.InvariantCulture) + "° off the nose" : "") + "   (F)";
            var content = new GUIContent(readout);
            var size = small.CalcSize(content);
            var p = new Vector2((Screen.width - size.x) * 0.5f, Screen.height - 66);
            DrawLabel(readout, p, assist ? Dim : RetrogradeColor, true);
        }

        /// <summary>Where a direction (ecliptic, unit) vanishes on screen, if it is in front of the camera and on screen.</summary>
        bool DirectionToGui(DVec3 direction, out Vector2 p)
        {
            var d = direction.SwapYZ;
            if (!app.RelativeToGui(new Vector3((float)d.X, (float)d.Y, (float)d.Z) * 1e6f, out p)) return false;
            return p.x >= 0 && p.y >= 0 && p.x <= Screen.width && p.y <= Screen.height;
        }

        void DrawBoresight(Vector2 p, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            var w = Icons.White;
            GUI.DrawTexture(new Rect(p.x - 12, p.y - 0.75f, 8, 1.5f), w);
            GUI.DrawTexture(new Rect(p.x + 4, p.y - 0.75f, 8, 1.5f), w);
            GUI.DrawTexture(new Rect(p.x - 0.75f, p.y + 4, 1.5f, 6), w);
            GUI.DrawTexture(new Rect(p.x - 1, p.y - 1, 2, 2), w);
            GUI.color = old;
        }

        void DrawLine(Vector2 a, Vector2 b, Color color, float thickness)
        {
            var d = b - a;
            float length = d.magnitude;
            if (length < 1) return;
            var matrix = GUI.matrix;
            var old = GUI.color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.color = color;
            GUI.DrawTexture(new Rect(a.x, a.y - thickness * 0.5f, length, thickness), Icons.White);
            GUI.color = old;
            GUI.matrix = matrix;
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

        // ---------------------------------------------------------------- opportunities (U.SPC-T44)

        static Color OpportunityColor(Opportunity op, float alpha = 1)
        {
            var c = op.MarkerColor;
            return new Color(c.R, c.G, c.B, alpha);
        }

        /// <summary>Every open adventure is bracketed where it is, in its kind's colour, with its tag and how far it is.</summary>
        void DrawOpportunityMarkers()
        {
            var director = app.World.Adventures;
            if (director == null || director.Active.Count == 0) return;
            var player = app.PlayerHelioKm();
            foreach (var op in director.Active)
            {
                if (!app.OpportunityScene(op, out var scene) || !app.ToGui(scene, out var p, out _)) continue;
                if (p.x < 0 || p.x > Screen.width || p.y < 0 || p.y > Screen.height) continue;
                var color = OpportunityColor(op, 0.95f);
                DrawBrackets(new Rect(p.x - 11, p.y - 11, 22, 22), color, 1.5f);
                DrawLabel(op.MarkerLabel + "  " + Km((op.HelioKm - player).Length), p + new Vector2(14, 6), color, true);
            }
        }

        /// <summary>The open adventures near the player, most urgent first: tag, title, distance, time left and the hook.</summary>
        void DrawOpportunityBoard()
        {
            var director = app.World.Adventures;
            if (director == null || director.Active.Count == 0) return;
            var list = new List<Opportunity>(director.Active);
            double jd = app.Clock.Jd;
            list.Sort((a, b) => a.ExpiresJd.CompareTo(b.ExpiresJd));
            int n = Mathf.Min(5, list.Count);
            float w = 380, rowH = 40;
            var r = Panel(new Rect(Screen.width - w - 10, 72, w, 30 + n * rowH));
            GUI.Label(new Rect(r.x + 12, r.y + 6, w - 24, 18), "OPPORTUNITIES" + (app.IsPiloting ? "" : "   (pilot a ship to answer)"), small);
            var player = app.PlayerHelioKm();
            var old = GUI.contentColor;
            opportunityHook ??= new GUIStyle(small) { wordWrap = true, clipping = TextClipping.Clip };
            for (int i = 0; i < n; i++)
            {
                var op = list[i];
                float y = r.y + 26 + i * rowH;
                GUI.contentColor = OpportunityColor(op);
                GUI.Label(new Rect(r.x + 12, y, w - 24, 16), op.MarkerLabel + "   " + op.Title + "   " + Km((op.HelioKm - player).Length) + "   " +
                    Duration(op.DaysLeft(jd)) + " left" + (op.Engaged ? "   · on scene" : ""), small);
                GUI.contentColor = Dim;
                GUI.Label(new Rect(r.x + 12, y + 15, w - 24, rowH - 16), op.Hook, opportunityHook);
            }
            GUI.contentColor = old;
        }

        GUIStyle opportunityHook;

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
            string rate = clock.Paused ? "PAUSED"
                : clock.Pausing ? "PAUSING  x" + clock.Speed.ToString("0.00", CultureInfo.InvariantCulture)
                : clock.Resuming ? "RESUMING  x" + clock.Speed.ToString("0.00", CultureInfo.InvariantCulture) + "  of " + clock.Warp.Label
                : clock.Warp.Label;
            GUI.Label(new Rect(r.x + 14, r.y + 28, 220, 18), rate, small);

            float bx = r.x + 200;
            if (GUI.Button(new Rect(bx, r.y + 12, 34, 28), "<<", button)) clock.Slower();
            if (GUI.Button(new Rect(bx + 38, r.y + 12, 44, 28), clock.PauseHeld ? ">" : "||", clock.PauseHeld ? buttonActive : button)) app.TogglePause();
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

            // Star systems: one button each; the active one is where the body tree below belongs.
            float sy = r.y + 34;
            var oldColor = GUI.contentColor;
            GUI.contentColor = Accent;
            GUI.Label(new Rect(r.x + 12, sy, w - 24, 16), "STAR SYSTEMS", small);
            GUI.contentColor = oldColor;
            sy += 17;
            var network = app.Galaxy.Network;
            double jd = app.Clock.Jd;
            foreach (var system in app.Galaxy.Systems)
            {
                bool here = system == app.ActiveSystem;
                bool going = app.Travel.Active && app.Travel.To == system;
                // How many gates away it is, by the quickest open way (a gate network, not all-pairs).
                var route = here ? null : network.Route(app.ActiveSystem, system, null, jd);
                string hops = route == null ? "" : "  " + route.Count + (route.Count == 1 ? " gate" : " gates");
                string label = system.Name + (here ? "   ◉ here" : going ? "   travelling…" : "   " + Galaxy.DistanceLy(app.ActiveSystem, system).ToString("0.00", CultureInfo.InvariantCulture) + " ly" + hops + "  ›");
                // Rows tighten as the list of systems grows, so the body tree below keeps its room.
                float rowHeight = app.Galaxy.Systems.Count > 4 ? 18 : 22;
                if (GUI.Button(new Rect(r.x + 10, sy, w - 20, rowHeight), label, here ? buttonActive : button) && !here) app.TravelTo(system);
                sy += rowHeight + (app.Galaxy.Systems.Count > 4 ? 2 : 3);
                if (!here) continue;
                // This system's own gates: where each leads, how long the crossing, and whether it is shut.
                foreach (var gate in system.Gates)
                {
                    bool shut = gate.Blockaded(jd) || gate.Partner.Blockaded(jd);
                    string line = "   " + gate.Name + "  " + gate.Link.Ly.ToString("0.00", CultureInfo.InvariantCulture) + " ly, " +
                                  (gate.Link.TransitDays * 24).ToString("0", CultureInfo.InvariantCulture) + " h" + (shut ? "   SHUT" : "");
                    var old = GUI.contentColor;
                    GUI.contentColor = shut ? new Color(1f, 0.5f, 0.42f) : new Color(0.7f, 0.82f, 0.95f);
                    GUI.Label(new Rect(r.x + 12, sy, w - 24, 15), line, small);
                    GUI.contentColor = old;
                    sy += 15;
                }
            }
            int crossing = app.Galaxy.Traffic.InGates.Count;
            if (crossing > 0)
            {
                GUI.Label(new Rect(r.x + 12, sy, w - 24, 15), "   " + crossing + (crossing == 1 ? " ship" : " ships") + " in the gates", small);
                sy += 15;
            }
            float treeTop = sy + 4 - r.y;

            var catalog = app.Catalog;
            var rows = new List<(string text, Target target, int indent, Color color)>();
            var war = app.World.War;
            if (war.Battles.Count > 0)
            {
                rows.Add(("BATTLES", Target.None, 0, new Color(1f, 0.5f, 0.4f)));
                foreach (var b in war.Battles)
                {
                    var flag = Flagship(b);
                    rows.Add((b.Name + "  " + BattleScore(b), flag != null ? Target.OfShip(flag) : Target.OfBody(b.Body), 1, new Color(1f, 0.72f, 0.6f)));
                }
                rows.Add(("", Target.None, 0, Color.clear));
            }
            AddBody(rows, 0, 0);
            foreach (var faction in app.Fleet.Factions)
            {
                rows.Add(("", Target.None, 0, Color.clear));
                var fc = new Color(faction.Color.R, faction.Color.G, faction.Color.B);
                rows.Add((faction.Name.ToUpperInvariant() + "   " + Credits(faction.Treasury), Target.None, 0, fc));
                foreach (var ship in app.Fleet.Ships)
                {
                    if (ship.Faction != faction || ship.IsEscort || ship.Destroyed) continue;
                    rows.Add((ship.Name + "  " + ShortStatus(ship), Target.OfShip(ship), 1, fc));
                    foreach (var escort in app.Fleet.Ships)
                        if (escort.Leader == ship && !escort.Destroyed) rows.Add((escort.Name + "  · " + escort.Spec.DisplayName, Target.OfShip(escort), 2, Color.Lerp(fc, Color.gray, 0.35f)));
                }
            }
            // The trading companies that own the merchant fleet, in their liveries.
            var companies = app.World.Companies;
            if (companies != null && companies.Enabled)
            {
                rows.Add(("", Target.None, 0, Color.clear));
                rows.Add(("TRADING COMPANIES", Target.None, 0, new Color(0.9f, 0.86f, 0.7f)));
                foreach (var co in companies.All)
                {
                    var lc = new Color(co.Livery.R, co.Livery.G, co.Livery.B);
                    if (!co.Trading) lc = Color.Lerp(lc, Color.gray, 0.6f);
                    rows.Add((CompanyText(co), Target.None, 1, lc));
                }
            }
            // Ships from other systems, come through the gates (their factions belong to their own worlds).
            bool visitorsHeader = false;
            var local = new HashSet<Faction>(app.Fleet.Factions);
            foreach (var ship in app.Fleet.Ships)
            {
                if (ship.IsEscort || ship.Destroyed || local.Contains(ship.Faction)) continue;
                if (!visitorsHeader)
                {
                    rows.Add(("", Target.None, 0, Color.clear));
                    rows.Add(("FROM BEYOND THE GATES", Target.None, 0, new Color(0.7f, 0.82f, 0.95f)));
                    visitorsHeader = true;
                }
                var fc = new Color(ship.Faction.Color.R, ship.Faction.Color.G, ship.Faction.Color.B);
                string from = ship.Voyage != null ? "  · " + ship.Voyage.Home.Name : "";
                rows.Add((ship.Name + from + "  " + ShortStatus(ship), Target.OfShip(ship), 1, fc));
            }

            // Docking rings and what is in orbit, after the bodies.
            var infra = app.World.Infrastructure;
            if (infra != null && infra.Rings.Count > 0)
            {
                rows.Add(("", Target.None, 0, Color.clear));
                rows.Add(("DOCKING RINGS", Target.None, 0, new Color(0.6f, 0.9f, 1f)));
                foreach (var ring in infra.Rings)
                {
                    int queued = 0;
                    foreach (var s in app.Fleet.Ships)
                        if (!s.Destroyed && s.Phase == ShipPhase.Queued && s.Ring == ring) queued++;
                    rows.Add((ring.Name + "  · " + catalog[ring.Body].Name + (queued > 0 ? "  · " + queued + " queued" : ""), Target.OfRing(ring), 1, new Color(0.6f, 0.9f, 1f)));
                }
            }
            if (infra != null && infra.Structures.Count > 0)
            {
                rows.Add(("", Target.None, 0, Color.clear));
                rows.Add(("IN ORBIT", Target.None, 0, new Color(0.85f, 0.88f, 0.92f)));
                foreach (var s in infra.Structures)
                {
                    var c = s.Owner != null ? new Color(s.Owner.Color.R, s.Owner.Color.G, s.Owner.Color.B) : new Color(0.85f, 0.88f, 0.92f);
                    rows.Add((s.Name + "  · " + catalog[s.Body].Name, Target.OfStructure(s), 1, c));
                }
            }

            const float rowH = 19;
            var view = new Rect(r.x + 4, r.y + treeTop, w - 8, h - treeTop - 6);
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
                    if (doubleClick && !app.IsPiloting) app.SpaceCamera.FocusOn(target);
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
            if (!b.IsVisible)
            {
                // A barycentre heads the tree as a title, not a selectable body.
                rows.Add((b.Name.ToUpperInvariant() + "  (barycentre)", Target.None, 0, Dim));
                foreach (int child in app.Catalog.ChildrenOf(index)) AddBody(rows, child, indent);
                return;
            }
            rows.Add((b.Name + StatusMark(b), Target.OfBody(index), indent, b.Kind == BodyKind.Moon ? Color.Lerp(tint, Color.gray, 0.3f) : tint));
            foreach (int child in app.Catalog.ChildrenOf(index)) AddBody(rows, child, Math.Min(indent + 1, 2));
        }

        static Ship Flagship(Battle b)
        {
            Ship best = null;
            foreach (var u in b.Units)
                if (u.Ship != null && u.Present && (best == null || u.Ship.Combat.Spec.Hull > best.Combat.Spec.Hull)) best = u.Ship;
            return best;
        }

        static string BattleScore(Battle b)
        {
            int a = 0, d = 0;
            foreach (var u in b.Units)
            {
                if (!u.Present || u.Kind == UnitKind.Fighter) continue;
                if (u.Side == 0) a++;
                else d++;
            }
            return a + " v " + d;
        }

        string ShortStatus(Ship ship)
        {
            if (ship.Battle != null) return "IN BATTLE  " + ship.Battle.Name;
            var c = ship.Controller;
            if (ship.Derelict) return "DERELICT";
            if (c.Phase == ShipPhase.Lurk) return "lurking on " + c.LaneLeg.Lane.Name;
            if (c.Phase == ShipPhase.Adrift) return "adrift by " + c.LaneLeg.Lane.Name;
            if (c.GateKm is DVec3 gateKm)
            {
                // Bound for a jump gate: name it, and the system beyond.
                string gate = "a jump gate";
                foreach (var g in app.ActiveSystem.Gates)
                    if ((g.PositionKm - gateKm).Length < 1) gate = "the " + g.Name;
                if (c.Voyage != null && c.Voyage.AtGate) return "at " + gate;
                if (c.Phase == ShipPhase.Transit) return "-> " + gate;
            }
            if (c.Phase == ShipPhase.Parked) return (c.Role == ShipRole.Free ? "drifting @ " : "@ ") + app.Catalog[c.Orbit.Body].Name;
            if (c.Phase == ShipPhase.Transit && c.Wander) return "free flight near " + app.Catalog[c.Orbit.Body].Name;
            if (c.Phase == ShipPhase.Queued) return "queued @ " + c.Ring.Name;
            if (c.Phase == ShipPhase.Descent) return "descending to " + c.Ring.City;
            if (c.Phase == ShipPhase.Landed) return "landed @ " + c.Ring.City;
            if (c.Phase == ShipPhase.Ascent) return "climbing to " + c.Ring.Name;
            int dest = c.Journey != null ? c.FinalBody : c.Orbit.Body;
            return (c.Phase == ShipPhase.Lane ? "=> " : "-> ") + app.Catalog[dest].Name;
        }

        void DrawSelection()
        {
            var t = app.Selected;
            if (t.IsNone) return;
            var lines = new List<(string key, string value)>();
            string heading, sub;
            Color headingColor = Accent;

            if (t.IsStructure) StructureLines(t.Structure, lines, out heading, out sub, out headingColor);
            else if (t.IsRing) RingLines(t.Ring, lines, out heading, out sub, out headingColor);
            else if (t.IsBody)
            {
                var b = app.Catalog[t.Body];
                heading = b.Name;
                sub = KindName(b) + (b.Status == BodyStatus.Candidate ? "  ·  CANDIDATE, unconfirmed" : b.Status == BodyStatus.Disputed ? "  ·  DISPUTED" : b.Status == BodyStatus.Fictional ? "  ·  fiction (game layer)" : "");
                var eph = app.Ephemeris;
                if (b.IsStar) lines.Add(("Photosphere", b.TeffK.ToString("N0", CultureInfo.InvariantCulture) + " K,  " + b.LuminositySolar.ToString(b.LuminositySolar < 0.01 ? "0.00000" : "0.000", CultureInfo.InvariantCulture) + " x the Sun's light"));
                lines.Add(("Radius", Km(b.RadiusKm)));
                if (b.DiscoveryYear > 0)
                {
                    double earths = b.Gm / 398600.435;
                    lines.Add(("Mass", (b.MassIsMinimum ? "at least " : "") + earths.ToString(earths < 10 ? "0.00" : "N0", CultureInfo.InvariantCulture) + " Earth masses"));
                    lines.Add(("Habitable zone", b.HabitableZone == HabitableZone.Conservative ? "inside"
                        : b.HabitableZone == HabitableZone.Optimistic ? "inside the optimistic zone only" : "outside"));
                    lines.Add(("Found", b.DiscoveryYear.ToString(CultureInfo.InvariantCulture)));
                }
                if (b.ParentIndex >= 0)
                {
                    var parent = app.Catalog[b.ParentIndex];
                    var k = b.Elements.At(app.Clock.Jd);
                    double dist = eph.Relative(t.Body).Length;
                    if (parent.UsesStellarMapping) lines.Add(("From " + (parent.IsVisible ? parent.Name : "barycentre"), (dist > 1000 * Astro.AuKm ? (dist / Astro.AuKm).ToString("N0", CultureInfo.InvariantCulture) + " AU" : Au(dist)) + "  (" + LightTime(dist) + " light)"));
                    else lines.Add(("From " + parent.Name, Km(dist)));
                    lines.Add(("Orbital speed", eph.OrbitalSpeedKmS(t.Body).ToString("0.00", CultureInfo.InvariantCulture) + " km/s"));
                    lines.Add(("Period", Duration(k.PeriodDays)));
                    lines.Add(("Eccentricity", k.E.ToString("0.0000", CultureInfo.InvariantCulture)));
                    lines.Add(("Semi-major axis", parent.UsesStellarMapping ? (k.A > 1000 * Astro.AuKm ? (k.A / Astro.AuKm).ToString("N0", CultureInfo.InvariantCulture) + " AU" : Au(k.A)) : Km(k.A)));
                }
                if (b.RotationHours != 0)
                    lines.Add(("Day (sidereal)", (Math.Abs(b.RotationHours) < 48 ? Math.Abs(b.RotationHours).ToString("0.00", CultureInfo.InvariantCulture) + " h" : Duration(Math.Abs(b.RotationHours) / 24)) + (b.RotationHours < 0 ? " retro" : "")));
                if (b.Kind != BodyKind.Station) lines.Add(("Surface gravity", (b.Gm / (b.RadiusKm * b.RadiusKm) * 1000 / 9.80665).ToString("0.000", CultureInfo.InvariantCulture) + " g"));
                var ringsHere = new List<DockingRing>(app.World.Infrastructure?.RingsAt(t.Body) ?? new DockingRing[0]);
                if (ringsHere.Count > 0) lines.Add(("Docking rings", ringsHere.Count + ": " + string.Join(", ", ringsHere.ConvertAll(x => x.City))));
                if (!string.IsNullOrEmpty(b.Summary)) lines.Add(("", b.Summary));
                foreach (var s in app.Economy.At(t.Body))
                    if (s.Discovered) AddSettlementLines(lines, s);
                var battleHere = app.World.War.BattleAt(t.Body);
                if (battleHere != null) lines.Add(("Battle", battleHere.Name + ", " + BattleScore(battleHere) + ", " + Duration(battleHere.Seconds / Astro.SecondsPerDay)));
            }
            else
            {
                var ship = t.Ship;
                var c = ship.Controller;
                var st = app.ShipStateOf(ship);
                heading = ship.Name;
                var company = c.Company;
                sub = company != null
                    ? company.Name + " " + ship.Spec.DisplayName.ToLowerInvariant() + ", under the " + ship.Faction.Name + " flag"
                    : ship.Faction.Name + " " + ship.Spec.DisplayName.ToLowerInvariant();
                var livery = company?.Livery ?? ship.Faction.Color;
                headingColor = new Color(livery.R, livery.G, livery.B);
                lines.Add(("Length", Km(ship.LengthKm)));
                lines.Add(("Drive", ship.Spec.AccelG.ToString("0.##", CultureInfo.InvariantCulture) + " g, flip-and-burn; hyperlanes between systems"));
                lines.Add(("Propellant", PropellantText(ship)));
                if (ship.IsEscort) lines.Add(("Escorting", ship.Leader.Name));
                if (app.IsPiloting && app.Piloted.Ship == ship && ship.Battle == null)
                {
                    // Under the player's own hand: where it really is, not the spot its schedule was pinned at.
                    var near = app.Catalog[app.PilotFrameBody];
                    lines.Add(("Status", "Under your helm near " + near.Name));
                    lines.Add(("Altitude", Km(app.PilotOffsetKm.Length - near.RadiusKm)));
                    lines.Add(("Speed", SpeedText(app.PilotSpeedKmS)));
                }
                else if (c.Phase == ShipPhase.Lurk)
                {
                    lines.Add(("Status", "Lying in wait beside the " + c.LaneLeg.Lane.Name + " hyperlane"));
                    lines.Add(("Until", Duration(Fleet.RemainingDays(c, app.Clock.Jd)) + " more, unless prey comes by"));
                }
                else if (c.Phase == ShipPhase.Adrift)
                {
                    lines.Add(("Status", "Pulled out of the " + c.LaneLeg.Lane.Name + " hyperlane"));
                }
                else if (c.Phase == ShipPhase.Lane)
                {
                    lines.Add(("Status", "In the " + c.LaneLeg.Lane.Name + " hyperlane, bound for " + app.Catalog[c.FinalBody].Name));
                    lines.Add(("Speed", SpeedText(st.SpeedKmS)));
                    lines.Add(("Remaining", Km(c.LaneLengthKm * (1 - st.Progress))));
                    lines.Add(("Lane exit", Duration(Fleet.RemainingDays(c, app.Clock.Jd))));
                }
                else if (c.Phase == ShipPhase.Queued)
                {
                    lines.Add(("Status", "Waiting off the " + c.Ring.Name + " to be cleared down to " + c.Ring.City));
                    lines.Add(("Cleared in", Duration(Fleet.RemainingDays(c, app.Clock.Jd)) + "  (" + QueuePosition(c) + " in line)"));
                }
                else if (c.Phase == ShipPhase.Descent || c.Phase == ShipPhase.Ascent)
                {
                    double alt = (st.HelioKm - app.Ephemeris.Helio(c.Ring.Body)).Length - c.Ring.SurfaceKm;
                    lines.Add(("Status", c.Phase == ShipPhase.Descent ? "Descending from the " + c.Ring.Name + " to " + c.Ring.City : "Climbing from " + c.Ring.City + " to the " + c.Ring.Name));
                    lines.Add(("Altitude", Km(Math.Max(0, alt)) + (alt < c.Ring.EntryInterfaceKm ? "  ·  IN THE ATMOSPHERE" : "")));
                    lines.Add(("Vertical speed", SpeedText(st.SpeedKmS)));
                    lines.Add(("Done in", Duration(Fleet.RemainingDays(c, app.Clock.Jd))));
                }
                else if (c.Phase == ShipPhase.Landed)
                {
                    lines.Add(("Status", "Landed at " + c.Ring.City + ", " + app.Catalog[c.Ring.Body].Name));
                    lines.Add(("Next", app.Catalog[c.DestinationBody].Name + " in " + Duration(Fleet.RemainingDays(c, app.Clock.Jd)) + ", by the " + c.Ring.Name));
                }
                else if (c.Phase == ShipPhase.Transit)
                {
                    string where = c.Ring != null && c.Orbit == c.HoldPoint ? c.Ring.Name + ", " + app.Catalog[c.Orbit.Body].Name
                        : c.Orbit.Fixed ? app.Catalog[c.Orbit.Body].Name + " hyperlane ramp" : app.Catalog[c.Orbit.Body].Name;
                    lines.Add(("Status", (st.Progress < 0.5 ? "Burning toward " : "Braking for ") + where));
                    if (c.Journey != null) lines.Add(("Bound for", app.Catalog[c.FinalBody].Name + " by " + JourneyText(c)));
                    lines.Add(("From", app.Catalog[c.OriginBody].Name));
                    lines.Add(("Speed", st.SpeedKmS.ToString("N0", CultureInfo.InvariantCulture) + " km/s"));
                    lines.Add(("Remaining", Km(c.PathLength * (1 - st.Progress))));
                    lines.Add(("ETA", Duration(Fleet.RemainingDays(c, app.Clock.Jd))));
                }
                else
                {
                    var body = app.Catalog[c.Orbit.Body];
                    lines.Add(("Status", ship.Battle != null ? "Fighting in the " + ship.Battle.Name
                        : c.OutOfPropellant ? "Drifting off " + body.Name + " with dry tanks; a tanker is " + Duration(Fleet.RemainingDays(c, app.Clock.Jd)) + " out"
                        : (c.Orbit.Fixed ? "Holding position at " : "Holding orbit at ") + body.Name));
                    // From where it actually is: a held position (a released or spawned hull) is a fixed point
                    // with no orbit radius, and a ring's holding point sits off the body's centre.
                    lines.Add(("Altitude", Km(c.Orbit.OffsetAt(app.Clock.Jd).Length - body.RadiusKm)));
                    lines.Add(("Orbital speed", c.Orbit.SpeedKmS.ToString("0.00", CultureInfo.InvariantCulture) + " km/s"));
                    // A freshly spawned or not-yet-scheduled ship has no destination yet (-1).
                    if (c.DestinationBody >= 0)
                        lines.Add(("Next", app.Catalog[c.DestinationBody].Name + " in " + Duration(Fleet.RemainingDays(c, app.Clock.Jd))));
                }
                var giving = app.Fleet.EvadingFrom(ship);
                var off = app.Fleet.EvasionOf(ship);
                if (giving != null || off.Length > 0.5)
                    lines.Add(("Manoeuvring", (giving != null ? "giving way to " + giving.Name + ", " : "easing back onto course, ") + Km(off.Length) + " off its path"));
                if (c.Role == ShipRole.Free) lines.Add(("Pilot", "Independent: goes where it pleases" + (c.Wander ? " (a hop out to open space)" : "")));
                var combat = ship.Combat;
                lines.Add(("Hull", Percent(combat.HullFraction) + "   Shields " + Percent(combat.Spec.Shield > 0 ? combat.Shield / combat.Spec.Shield : 0)));
                int lost = 0;
                foreach (var hp in combat.Subsystems) if (hp.Destroyed) lost++;
                if (lost > 0) lines.Add(("Damage", lost + " of " + combat.Subsystems.Count + " hardpoints destroyed"));
                if (combat.Spec.Squadrons > 0) lines.Add(("Fighters", combat.Fighters + " of " + combat.Spec.Squadrons * 6 + " aboard"));
                var inside = combat.Interior;
                lines.Add(("Aboard", inside.Crew.ToString("0", CultureInfo.InvariantCulture) + " crew, " + inside.Marines.ToString("0", CultureInfo.InvariantCulture) + " marines" +
                    (combat.Disabled ? "  ·  DISABLED" : "")));
                if (inside.UnderBoarding)
                {
                    lines.Add(("BOARDED", inside.Boarder.Name + ": " + inside.Boarders.ToString("0", CultureInfo.InvariantCulture) + " boarders hold " + inside.TakenCount + " of " + inside.Compartments.Count + " spaces"));
                    foreach (var space in inside.Compartments) lines.Add(("  " + space.Name, SpaceText(space)));
                    if (inside.Log.Count > 0) lines.Add(("", inside.Log[inside.Log.Count - 1].Text));
                }
                if (ship.Derelict) lines.Add(("DERELICT", "Boarded, stripped and abandoned"));
                if (ship.Battle != null) lines.Add(("In battle", ship.Battle.Name + ", " + BattleScore(ship.Battle)));
                if (ship.Spec.IsMilitary || ship.Faction.IsPirate)
                {
                    var order = app.World.War.OrderOf(c);
                    var outlaws = app.World.Outlaws;
                    var hunter = outlaws?.HunterOf(c);
                    var outlaw = outlaws?.Of(c);
                    if (hunter != null)
                    {
                        var target = hunter.Contract?.Target;
                        string after = target == null ? "looking for a contract" : "hunting " + (outlaws.Of(target)?.Title ?? (target.IsPlayer ? "YOU" : target.Name));
                        lines.Add(("Bounty hunter", after + (hunter.Takes > 0 ? ",  " + hunter.Takes + " taken for " + Credits(hunter.Collected) : "")));
                    }
                    else lines.Add(("Orders", order != null ? OrderText(order) : "Patrol " + RouteText(c)));
                    if (outlaw != null)
                        lines.Add(("Outlaw", outlaw.Title + ",  " + outlaw.Deeds() + ",  " + outlaw.DaysAtLarge(app.Clock.Jd).ToString("0", CultureInfo.InvariantCulture) + " days at large" +
                            (outlaw.Haven != null && outlaw.Haven.Discovered ? ",  sails from " + outlaw.Haven.Name : "")));
                }
                else
                {
                    lines.Add(("Cargo", string.IsNullOrEmpty(c.Cargo) ? "Empty" : c.Cargo));
                    lines.Add(("Hold", Tonnes(c.CapacityTonnes)));
                    if (c.HasCargo) lines.Add(("Paid", Credits(c.CargoCost) + (c.CargoBonus > 0 ? "  (+" + Credits(c.CargoBonus) + " contract)" : "")));
                    lines.Add(("Trade profit", Credits(c.Credits)));
                    if (c.Company != null) lines.Add(("Owner", CompanyText(c.Company)));
                }
                lines.Add(("Trips", c.TripsCompleted.ToString(CultureInfo.InvariantCulture)));
                // The law's file on whoever commands it: the player's own record for the ship they fly.
                var record = app.World.Law?.FindRecord(ship);
                if (record != null && (record.Wanted || record.Crimes.Count > 0))
                    lines.Add((record.IsPlayer ? "Your record" : "Captain", (record.IsPlayer ? "" : record.Name + ", ") + RecordText(record)));
            }

            float w = 410;
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

        /// <summary>The tanks: how full, how many tonnes, and the delta-v that leaves at the ship's present mass.</summary>
        static string PropellantText(Ship ship)
        {
            if (ship.OutOfPropellant || ship.Propellant <= 1e-6) return "DRY  ·  0 of " + Tonnes(ship.PropellantCapacity);
            return Percent(ship.PropellantFraction) + "  ·  " + Tonnes(ship.Propellant) + " of " + Tonnes(ship.PropellantCapacity) +
                   "  ·  Δv " + ship.DeltaVBudgetKmS.ToString("N0", CultureInfo.InvariantCulture) + " km/s";
        }

        string QueuePosition(Ship ship)
        {
            int ahead = 1;
            foreach (var s in app.Fleet.Ships)
                if (!s.Destroyed && s.Phase == ShipPhase.Queued && s.Ring == ship.Ring && s.PhaseEndJd < ship.PhaseEndJd) ahead++;
            return ahead == 1 ? "1st" : ahead == 2 ? "2nd" : ahead == 3 ? "3rd" : ahead + "th";
        }

        void StructureLines(OrbitalStructure s, List<(string key, string value)> lines, out string heading, out string sub, out Color headingColor)
        {
            heading = s.Name;
            string kind = s.Kind == StructureKind.Shipyard ? "Orbital shipyard" : s.Kind == StructureKind.Factory ? "Factory station"
                : s.Kind == StructureKind.Habitat ? "Orbital habitat" : s.Kind == StructureKind.CrewedStation ? "Crewed space station"
                : s.Kind == StructureKind.Telescope ? "Space telescope" : "Robotic orbiter";
            sub = kind + (s.Real ? "  ·  real spacecraft" : "  ·  " + (s.Owner?.Name ?? ""));
            headingColor = s.Owner != null ? new Color(s.Owner.Color.R, s.Owner.Color.G, s.Owner.Color.B) : Accent;
            var b = app.Catalog[s.Body];
            if (s.AtL2) lines.Add(("Where", "Sun-" + b.Name + " L2, 1.5 million km beyond " + b.Name));
            else
            {
                lines.Add(("Orbiting", b.Name));
                lines.Add(("Altitude", Km(s.Orbit.RadiusKm - b.RadiusKm)));
                lines.Add(("Period", Duration(Astro.TwoPi / Math.Max(s.Orbit.RateRadPerDay, 1e-12))));
                lines.Add(("Speed", s.Orbit.SpeedKmS.ToString("0.00", CultureInfo.InvariantCulture) + " km/s"));
            }
            lines.Add(("Size", Km(s.LengthKm)));
            if (s.Kind == StructureKind.Shipyard && s.Settlement != null)
            {
                var slip = s.Settlement.OnSlip;
                lines.Add(("On the slip", slip != null ? ShipClassSpec.Of(slip.Value).DisplayName + ", " + Percent(s.SlipProgress) + " built" : "Empty"));
                lines.Add(("Build points", s.Settlement.BuildPoints.ToString("N0", CultureInfo.InvariantCulture)));
            }
            if (s.Kind == StructureKind.Factory && s.Settlement != null)
                foreach (var f in s.Settlement.Facilities)
                    if (f.Recipe.Kind == s.Industry)
                        lines.Add(("Output", f.Recipe.Name + ", level " + f.Level.ToString("0.0", CultureInfo.InvariantCulture) + ", running " + Percent(f.Utilisation)));
            if (s.Settlement != null) lines.Add(("Serves", s.Settlement.Name + ", pop " + People(s.Settlement.Population)));
            if (!string.IsNullOrEmpty(s.Note)) lines.Add(("", s.Note));
        }

        void RingLines(DockingRing r, List<(string key, string value)> lines, out string heading, out string sub, out Color headingColor)
        {
            heading = r.Name;
            sub = "Docking ring over " + r.City + ", " + app.Catalog[r.Body].Name + "  ·  fiction: held on station by thrust";
            headingColor = new Color(0.6f, 0.9f, 1f);
            lines.Add(("Over", r.City + "  " + Math.Abs(r.LatDeg).ToString("0.00", CultureInfo.InvariantCulture) + (r.LatDeg >= 0 ? "° N  " : "° S  ")
                + Math.Abs(r.LonDeg).ToString("0.00", CultureInfo.InvariantCulture) + (r.LonDeg >= 0 ? "° E" : "° W")));
            lines.Add(("Altitude", Km(r.RadiusKm - r.SurfaceKm) + (Math.Abs(r.RadiusKm - Infrastructure.RingRadiusKm(app.Catalog[r.Body])) < 1 && r.RadiusKm > r.SurfaceKm * 3.5 ? "  (stationary-orbit height)" : "")));
            int queued = 0, down = 0, landed = 0, up = 0;
            foreach (var s in app.Fleet.Ships)
            {
                if (s.Destroyed || s.Ring != r) continue;
                if (s.Phase == ShipPhase.Queued) queued++;
                else if (s.Phase == ShipPhase.Descent) down++;
                else if (s.Phase == ShipPhase.Landed) landed++;
                else if (s.Phase == ShipPhase.Ascent) up++;
            }
            lines.Add(("Traffic", queued + " queued, " + down + " descending, " + landed + " landed, " + up + " climbing"));
            lines.Add(("Clearance", "one ship every " + (r.EntryIntervalDays * 1440).ToString("0", CultureInfo.InvariantCulture) + " min" +
                (r.NextEntryJd > app.Clock.Jd ? ", next in " + Duration(r.NextEntryJd - app.Clock.Jd) : ", clear now")));
            lines.Add(("Descent", Duration(r.DescentDays) + " to the ground; entry interface " + Km(r.EntryInterfaceKm) + " up"));
            if (r.Settlement != null) lines.Add(("Serves", r.Settlement.Name + " (" + r.Owner?.Name + ")"));
            lines.Add(("", "Merchants and couriers land through the rings; warships and pirates hold orbit."));
        }

        /// <summary>A trading company in a line: name, home port, hulls, cash, income, standing with its flag, or its fate.</summary>
        string CompanyText(Company co)
        {
            if (co.Fate == CompanyFate.Merged) return co.Name + "  · merged into " + co.MergedInto?.Name;
            if (co.Fate == CompanyFate.Bankrupt) return co.Name + "  · bankrupt";
            string order = co.OnOrder != null ? ",  " + co.OnOrder.Value.ToString().ToLowerInvariant() + " on order at " + co.OrderYard?.Name : "";
            return co.Name + " (" + co.HomePort?.Name + ")  " + co.Hulls + (co.Hulls == 1 ? " hull" : " hulls") + ",  " + Credits(co.Cash) +
                   ",  " + Credits(co.IncomePerDay) + "/day" + order;
        }

        void AddSettlementLines(List<(string key, string value)> lines, Settlement s)
        {
            lines.Add(("", ""));
            lines.Add((s.Name, s.Owner.Name + " " + KindName(s.Kind) + ", pop " + People(s.Population)));
            var houses = app.World.Companies;
            if (houses != null)
                foreach (var co in houses.All)
                    if (co.Trading && co.HomePort == s) lines.Add(("Company", CompanyText(co)));
            if (s.Kind != SettlementKind.Haven && !s.Fallen)
            {
                double moving = s.NetMigration;
                string migration = Math.Abs(moving) < 1 ? "no migration" :
                    (moving > 0 ? "+" : "-") + Math.Abs(moving).ToString("N0", CultureInfo.InvariantCulture) + "/day " + (moving > 0 ? "moving in" : "leaving");
                lines.Add(("People", migration + "  ·  jobs " + (s.Staffing * 100).ToString("0", CultureInfo.InvariantCulture) + "% filled  ·  wage tax " + Credits(s.WageTax) + "/day"));
            }
            var exports = new List<(double value, string name)>();
            var shortages = new List<(double ratio, string name)>();
            for (int g = 0; g < GoodInfo.Count; g++)
            {
                var info = GoodInfo.Of((Good)g);
                double net = s.Supply[g] - s.Demand[g];
                if (net > 0) exports.Add((net * info.BasePrice, info.Name));
                double ratio = s.Price[g] / info.BasePrice;
                if (s.Demand[g] > 0 && ratio > 1.4) shortages.Add((ratio, info.Name + " x" + ratio.ToString("0.0", CultureInfo.InvariantCulture)));
            }
            exports.Sort((a, b) => b.value.CompareTo(a.value));
            shortages.Sort((a, b) => b.ratio.CompareTo(a.ratio));
            if (exports.Count > 0) lines.Add(("Exports", string.Join(", ", exports.GetRange(0, Math.Min(3, exports.Count)).ConvertAll(e => e.name))));
            lines.Add(("Short of", shortages.Count > 0 ? string.Join(", ", shortages.GetRange(0, Math.Min(3, shortages.Count)).ConvertAll(e => e.name)) : "nothing"));
            double siege = app.World.War.SiegeDays(s);
            lines.Add(("Defences", s.Platforms.Count + " of " + s.MaxPlatforms + " platforms" +
                (siege > 0 ? "  ·  under siege " + siege.ToString("0", CultureInfo.InvariantCulture) + " of " + War.SiegeNeeded(s).ToString("0", CultureInfo.InvariantCulture) + " days" : "")));
        }

        /// <summary>A found pirate haven is marked on its world's label (a hidden one is not, until found).</summary>
        string HavenMark(BodyDefinition body)
        {
            foreach (var s in app.Economy.At(body.Index))
                if (s.Kind == SettlementKind.Haven && s.Discovered) return "  · " + s.Name + (s.Fallen ? " (fallen)" : " (pirate haven)");
            return "";
        }

        static string KindName(SettlementKind k)
        {
            switch (k)
            {
                case SettlementKind.Station: return "station";
                case SettlementKind.MiningOutpost: return "mining outpost";
                case SettlementKind.Haven: return "pirate haven";
                default: return "colony";
            }
        }

        // ---------------------------------------------------------------- bounty board

        /// <summary>
        /// The bounty board (F4) of the port in view -- the selected world's biggest, else the one the piloted ship is
        /// alongside: everyone its owner has a bounty on (outlaws, and the player when wanted), the price, the record,
        /// where each was last seen and who is hunting them (see <see cref="Outlaws.BoardAt"/>).
        /// </summary>
        void DrawBounties()
        {
            var outlaws = app.World.Outlaws;
            if (outlaws == null) return;
            Settlement port = null;
            var t = app.Selected;
            if (t.IsBody)
                foreach (var s in app.Economy.At(t.Body))
                    if (s.Discovered && (port == null || s.Population > port.Population)) port = s;
            if (port == null && app.IsPiloting)
                foreach (var s in app.Economy.Settlements)
                    if (s.Discovered && app.NearPort(s)) { port = s; break; }
            const float w = 760, rowH = 18;
            var board = port != null ? outlaws.BoardAt(port) : new List<BountyPosting>();
            int rows = Mathf.Min(board.Count, 14);
            // Captains in the player's brig, and kills waiting on a scan of the wreck (U.SPC-T63).
            var ship = app.IsPiloting ? app.World.PilotedShip : null;
            var held = outlaws.HeldAboard(ship);
            var claims = ship != null ? outlaws.Claims : new List<KillClaim>();
            int extra = held.Count + claims.Count;
            float h = 54 + Mathf.Max(1, rows) * rowH + (extra > 0 ? 8 + extra * 22 : 0);
            var r = Panel(new Rect((Screen.width - w) * 0.5f, 120, w, h));
            GUI.Label(new Rect(r.x + 12, r.y + 6, w - 24, 22), "BOUNTY BOARD  ·  " + (port != null ? port.Name + "  ·  " + port.Owner.Name : "no port in view"), title);
            var old = GUI.contentColor;
            GUI.contentColor = Dim;
            string note = port == null ? "Select a world with a port, or fly alongside one.   F4 to close"
                : port.Owner.IsPirate ? "Nobody here answers to the law: a haven keeps no board.   F4 to close"
                : board.Count == 0 ? "Nobody is wanted here.   F4 to close"
                : "Bounties posted by " + port.Owner.Name + ", biggest first; the price pays in full alive, half dead on a scanned wreck.   F4 to close";
            GUI.Label(new Rect(r.x + 12, r.y + 28, w - 24, 18), note, small);
            for (int i = 0; i < rows; i++)
            {
                var p = board[i];
                GUI.contentColor = p.IsPlayer ? new Color(1f, 0.55f, 0.45f) : p.Outlaw != null && p.Outlaw.Feared ? new Color(1f, 0.78f, 0.35f) : Color.white;
                GUI.Label(new Rect(r.x + 12, r.y + 48 + i * rowH, w - 24, rowH), Outlaws.Describe(p, app.Clock.Jd), small);
            }
            float y = r.y + 56 + Mathf.Max(1, rows) * rowH;
            bool alongside = port != null && app.NearPort(port);
            foreach (var o in held)
            {
                GUI.contentColor = new Color(0.6f, 0.9f, 1f);
                GUI.Label(new Rect(r.x + 12, y + 2, w - 200, rowH), "IN YOUR BRIG  ·  " + o.Title + "  ·  " + Credits(o.Record.TotalBounty) + " alive  ·  ransom " + Credits(Outlaws.RansomFor(o)), small);
                GUI.contentColor = Color.white;
                if (alongside && !port.Owner.IsPirate && (!o.Record.Wanted || o.Record.WantedBy(port.Owner)) &&
                    GUI.Button(new Rect(r.xMax - 180, y, 168, 20), "Hand over  +" + Credits(o.Record.TotalBounty), button))
                {
                    double paid = outlaws.Deliver(o, port, app.Clock.Jd);
                    app.ShowToast(o.Fate == OutlawFate.Captured ? o.Name + " handed over at " + port.Name + (paid > 0 ? " for " + Credits(paid) : "") : "Not welcome here");
                }
                else if (alongside && port.Owner.IsPirate && GUI.Button(new Rect(r.xMax - 180, y, 168, 20), "Ransom  +" + Credits(Outlaws.RansomFor(o)), button))
                {
                    double paid = outlaws.Ransom(o, port, app.Clock.Jd);
                    app.ShowToast(paid > 0 ? o.Name + " ransomed back for " + Credits(paid) : "The " + port.Owner.Name + " will not pay");
                }
                y += 22;
            }
            foreach (var c in claims)
            {
                GUI.contentColor = Dim;
                GUI.Label(new Rect(r.x + 12, y + 2, w - 24, rowH), "KILL UNPROVEN  ·  " + c.Outlaw.Title + "  ·  scan the wreck of the " + (c.Wreck != null ? c.Wreck.Name : "flagship") +
                    " for " + Credits(Outlaws.DeadRate(c.Outlaw.Record)) + ", the dead rate", small);
                y += 22;
            }
            GUI.contentColor = old;
        }

        // ---------------------------------------------------------------- market board

        void DrawMarket()
        {
            Settlement market = null;
            var t = app.Selected;
            if (t.IsBody) foreach (var s in app.Economy.At(t.Body)) if (s.Discovered) { market = s; break; }
            if (t.IsShip && t.Ship.Controller.CargoDestination != null) market = t.Ship.Controller.CargoDestination;

            // Buy/Sell only make sense for a market the player's own ship could actually hand goods
            // over at right now -- not one merely being looked at from across the system.
            bool canTrade = market != null && app.CanTradeNow(market);
            var law = app.World.Law;
            var pilotRecord = app.IsPiloting && law != null ? law.Player : null;
            bool lawful = market != null && market.Owner != null && !market.Owner.IsPirate;
            // A row for the law at this port: the bounty to pay off here, or why it will not dock the ship.
            bool atPort = market != null && pilotRecord != null && lawful && app.NearPort(market);
            bool wantedHere = atPort && pilotRecord.WantedBy(market.Owner);
            bool refused = atPort && !canTrade && app.Economy.LawRefuses(app.World.PilotedShip, market);
            float lawRow = wantedHere || refused ? 22 : 0;

            // What is shaking this market right now (a strike, a blight, a blockade, rationing), from the sim's own events.
            string shocks = null;
            if (market != null)
                foreach (var e in app.World.Shocks.At(market))
                    shocks = shocks == null ? e.Label : shocks + "  ·  " + e.Label;
            float extra = shocks != null ? 18 : 0;

            const float rowH = 17;
            float w = canTrade ? 600 : 430;
            float h = marketOpen ? 62 + GoodInfo.Count * rowH + (canTrade ? 20 : 0) + extra + lawRow : 34;
            var r = Panel(new Rect(Screen.width - w - 10, 72, w, h));
            string heading = market != null ? "MARKET  ·  " + market.Name : "MARKET  ·  system average";
            if (GUI.Button(new Rect(r.x + 8, r.y + 6, w - 16, 22), (marketOpen ? "[-]  " : "[+]  ") + heading, title)) marketOpen = !marketOpen;
            if (!marketOpen) return;
            var old = GUI.contentColor;
            GUI.contentColor = Dim;
            GUI.Label(new Rect(r.x + 12, r.y + 30, w - 24, 18), market != null
                ? market.Owner.Name + (pilotRecord != null && lawful ? StandingText(pilotRecord, market.Owner) : "   (select a body or a trader to change, M to close)")
                : "Select a port to see its market.   M to close", small);
            // At a yard the ship can refit: open the outfitting panel (F2) from here too.
            if (canTrade && Outfitting.IsYard(market) && GUI.Button(new Rect(r.xMax - 110, r.y + 28, 98, 18), app.ShowOutfit ? "Outfit  [-]" : "Outfit", button))
                app.ShowOutfit = !app.ShowOutfit;
            if (shocks != null)
            {
                GUI.contentColor = new Color(1f, 0.72f, 0.4f);
                GUI.Label(new Rect(r.x + 12, r.y + 48, w - 24, 18), "NOW:  " + shocks, small);
            }
            if (lawRow > 0)
            {
                GUI.contentColor = new Color(1f, 0.55f, 0.45f);
                GUI.Label(new Rect(r.x + 12, r.y + 50 + extra, w - 230, 18), refused ? "DOCKING REFUSED: " + market.Owner.Name + " will not let you in"
                    : "WANTED by " + market.Owner.Name, small);
                // Fines are paid at the port's office even when it will not let the ship dock to trade.
                if (wantedHere && GUI.Button(new Rect(r.xMax - 210, r.y + 48 + extra, 196, 20), "Pay bounty  " + Credits(pilotRecord.BountyBy(market.Owner)), button))
                    app.ShowToast(law.PayFine(app.World.PilotedShip, market.Owner, app.Clock.Jd, out double paid)
                        ? "Paid " + Credits(paid) + " to " + market.Owner.Name + ": the bounty is lifted"
                        : "Not enough credits to pay off the bounty");
                GUI.contentColor = Dim;
            }
            float top = r.y + 50 + extra + lawRow;
            if (canTrade)
            {
                GUI.contentColor = Color.white;
                var pilotShip = app.World.PilotedShip;
                GUI.Label(new Rect(r.x + 12, top, 200, 18), "Your credits: " + Credits(pilotShip.Faction.Treasury), small);
                // Fill the tanks here: water or hydrocarbons and their helium-3, at this market's price.
                GUI.Label(new Rect(r.x + 215, top, 110, 18), "Tanks " + Percent(pilotShip.PropellantFraction), small);
                string refuel = "Refuel  ~" + Credits(Economy.PropellantPrice(market) * (pilotShip.PropellantCapacity - pilotShip.Propellant));
                if (GUI.Button(new Rect(r.x + 300, top - 2, 140, 20), refuel, button)) ClickRefuel(market);
                GUI.Label(new Rect(r.xMax - 150, top, 40, 18), "Qty", small);
                if (GUI.Button(new Rect(r.xMax - 95, top - 2, 22, 20), "-", button)) tradeCrates = Math.Max(1, tradeCrates - 1);
                GUI.Label(new Rect(r.xMax - 70, top, 30, 18), tradeCrates.ToString(CultureInfo.InvariantCulture), small);
                if (GUI.Button(new Rect(r.xMax - 40, top - 2, 22, 20), "+", button)) tradeCrates++;
            }
            float y = top + (canTrade ? 20 : 0);
            GUI.contentColor = Dim;
            GUI.Label(new Rect(r.x + 12, y, 150, rowH), "Good", small);
            GUI.Label(new Rect(r.x + 170, y, 80, rowH), market != null ? "Stock" : "", small);
            GUI.Label(new Rect(r.x + 250, y, 80, rowH), "Price", small);
            GUI.Label(new Rect(r.x + 340, y, 80, rowH), market != null ? "Flow/day" : "vs base", small);
            y += rowH;
            for (int g = 0; g < GoodInfo.Count; g++)
            {
                var good = (Good)g;
                var info = GoodInfo.Of(good);
                double ratio = market != null ? market.Price[g] / info.BasePrice : app.Economy.PriceIndex(good);
                GUI.contentColor = Color.white;
                GUI.Label(new Rect(r.x + 12, y, 160, rowH), info.Name, small);
                if (market != null)
                {
                    GUI.Label(new Rect(r.x + 170, y, 80, rowH), Tonnes(market.Stock[g]), small);
                    double net = market.Supply[g] - market.Demand[g];
                    GUI.contentColor = net > 0 ? new Color(0.55f, 0.9f, 0.6f) : net < 0 ? new Color(1f, 0.7f, 0.45f) : Dim;
                    GUI.Label(new Rect(r.x + 340, y, 90, rowH), (net > 0 ? "+" : "") + Tonnes(net), small);
                }
                GUI.contentColor = ratio > 1.5 ? new Color(1f, 0.55f, 0.45f) : ratio < 0.6 ? new Color(0.55f, 0.85f, 1f) : Color.white;
                string price = (market != null ? market.Price[g] : info.BasePrice * ratio).ToString("N0", CultureInfo.InvariantCulture);
                GUI.Label(new Rect(r.x + 250, y, 90, rowH), price, small);
                if (market == null) GUI.Label(new Rect(r.x + 340, y, 90, rowH), "x" + ratio.ToString("0.00", CultureInfo.InvariantCulture), small);
                if (canTrade)
                {
                    if (GUI.Button(new Rect(r.x + 440, y - 1, 68, rowH), "Buy", button)) ClickBuy(market, good);
                    if (GUI.Button(new Rect(r.x + 515, y - 1, 68, rowH), "Sell", button)) ClickSell(market, good);
                }
                y += rowH;
            }
            GUI.contentColor = old;
        }

        /// <summary>The player's standing with a port's owner, and what it does to the prices there.</summary>
        static string StandingText(LawRecord r, Faction owner)
        {
            double s = r.StandingWith(owner);
            return "   ·   your standing " + (s > 0 ? "+" : "") + s.ToString("0", CultureInfo.InvariantCulture) +
                   ", prices x" + Law.PriceFactor(r, owner).ToString("0.00", CultureInfo.InvariantCulture);
        }

        void ClickBuy(Settlement market, Good good)
        {
            var ship = app.World.PilotedShip;
            var outcome = app.Economy.BuyIntoHold(ship, market, good, tradeCrates, out int bought);
            if (bought > 0)
                app.ShowToast("Bought " + bought + " crate" + (bought == 1 ? "" : "s") + " of " + GoodInfo.Of(good).Name +
                    (bought < tradeCrates ? "  (cargo hold is full)" : ""));
            else
                app.ShowToast(outcome switch
                {
                    Economy.TradeOutcome.NoStock => market.Name + " has none to sell",
                    Economy.TradeOutcome.CantAfford => "Not enough credits",
                    Economy.TradeOutcome.NoRoom => "Cargo hold is full",
                    Economy.TradeOutcome.CannotDock => "Not welcome to trade here",
                    Economy.TradeOutcome.Refused => "Docking refused: your standing with " + market.Owner.Name + " is too low",
                    Economy.TradeOutcome.Contraband => "No lawful market will take stolen goods",
                    _ => "Can't buy that right now",
                });
        }

        void ClickRefuel(Settlement market)
        {
            var ship = app.World.PilotedShip;
            var outcome = app.Economy.RefuelAt(ship, market, out double tonnes, out double cost);
            app.ShowToast(outcome switch
            {
                Economy.TradeOutcome.Ok => "Took on " + Tonnes(tonnes) + " of propellant for " + Credits(cost) +
                    (ship.PropellantFraction < 0.99 ? "  (" + (market.Available(Good.Water) + market.Available(Good.Hydrocarbons) < 1 ? "the port ran out" : "credits ran out") + ")" : ""),
                Economy.TradeOutcome.NoRoom => "Tanks are already full",
                Economy.TradeOutcome.NoStock => market.Name + " has no reaction mass to spare",
                Economy.TradeOutcome.CantAfford => "Not enough credits",
                Economy.TradeOutcome.CannotDock => "Not welcome to trade here",
                Economy.TradeOutcome.Refused => "Docking refused: your standing with " + market.Owner.Name + " is too low",
                _ => "Can't refuel right now",
            });
        }

        void ClickSell(Settlement market, Good good)
        {
            var ship = app.World.PilotedShip;
            var outcome = app.Economy.SellFromHold(ship, market, good, tradeCrates, out int sold);
            if (sold > 0)
                app.ShowToast("Sold " + sold + " crate" + (sold == 1 ? "" : "s") + " of " + GoodInfo.Of(good).Name);
            else
                app.ShowToast(outcome switch
                {
                    Economy.TradeOutcome.NothingToSell => "None of that in the hold",
                    Economy.TradeOutcome.CannotDock => "Not welcome to trade here",
                    Economy.TradeOutcome.Refused => "Docking refused: your standing with " + market.Owner.Name + " is too low",
                    Economy.TradeOutcome.Contraband => "No lawful market will take stolen goods",
                    _ => "Can't sell that right now",
                });
        }

        // ---------------------------------------------------------------- outfitting (U.SPC-T65)

        /// <summary>
        /// The yard's outfitting panel: the piloted hull's slots and what is in each, and for the chosen slot every
        /// module that fits with its price at this yard's market (materials at its prices, labour, less what the
        /// old part fetches). Prisoners in the brig are handed over here too, at any port the ship may dock at.
        /// </summary>
        void DrawOutfit()
        {
            var ship = app.World.PilotedShip;
            const float w = 600;
            if (ship == null || ship.Combat == null)
            {
                var r0 = Panel(new Rect((Screen.width - w) * 0.5f, 190, w, 52));
                GUI.Label(new Rect(r0.x + 12, r0.y + 6, w - 24, 22), "OUTFITTING", title);
                GUI.Label(new Rect(r0.x + 12, r0.y + 28, w - 24, 18), "Pilot a ship (P) and bring it to a shipyard to refit it.   F2 to close", small);
                return;
            }
            Settlement yard = null, port = null;
            foreach (var st in app.Economy.Settlements)
            {
                if (!st.Discovered || !app.CanTradeNow(st)) continue;
                port ??= st;
                if (yard == null && Outfitting.IsYard(st)) yard = st;
            }
            var loadout = Outfitting.LoadoutOf(ship);
            var spec = ship.Combat.Spec;
            const float rowH = 18;
            int options = 0;
            if (yard != null && outfitSlot >= 0 && outfitSlot < loadout.Slots.Count)
            {
                foreach (var m in ModuleSpec.Catalogue) if (Outfitting.CanFit(loadout, outfitSlot, m.Kind)) options++;
                if (Outfitting.CanFit(loadout, outfitSlot, null)) options++;
            }
            else outfitSlot = -1;
            float listH = Mathf.Min(loadout.Slots.Count * rowH, 230);
            float h = 112 + listH + (options > 0 ? 26 + options * rowH : 0);
            var r = Panel(new Rect((Screen.width - w) * 0.5f, 190, w, h));
            GUI.Label(new Rect(r.x + 12, r.y + 6, w - 24, 22), "OUTFITTING  ·  " + ship.Name + "  ·  " + (yard != null ? yard.Name : port != null ? port.Name + " (no yard)" : "not alongside a port"), title);
            var old = GUI.contentColor;
            GUI.contentColor = Dim;
            GUI.Label(new Rect(r.x + 12, r.y + 28, w - 24, 18),
                "Hull " + spec.Hull.ToString("N0", CultureInfo.InvariantCulture) + "  ·  shield " + spec.Shield.ToString("N0", CultureInfo.InvariantCulture) +
                "  ·  armour " + spec.Armor.ToString("0", CultureInfo.InvariantCulture) + "  ·  dry mass " + Tonnes(spec.DryMassTonnes) +
                "  ·  delta-v " + ship.DeltaVBudgetKmS.ToString("N0", CultureInfo.InvariantCulture) + " km/s  ·  scan " + (Outfitting.ScanRangeKm(ship) / 1e6).ToString("0.#", CultureInfo.InvariantCulture) + " M km", small);
            GUI.contentColor = Color.white;
            GUI.Label(new Rect(r.x + 12, r.y + 46, 220, 18), "Credits: " + Credits(ship.Faction.Treasury), small);
            double brig = Outfitting.BrigCapacity(ship);
            if (brig > 0 || ship.Prisoners > 0)
            {
                GUI.Label(new Rect(r.x + 230, r.y + 46, 200, 18), "Brig " + ship.Prisoners.ToString("0", CultureInfo.InvariantCulture) + " / " + brig.ToString("0", CultureInfo.InvariantCulture) + " prisoners", small);
                if (ship.Prisoners > 0 && port != null && GUI.Button(new Rect(r.xMax - 170, r.y + 44, 158, 20), "Hand over  +" + Credits(ship.PrisonerBounty), button))
                {
                    double paid = Outfitting.HandOverPrisoners(app.Economy, ship, port);
                    app.ShowToast(paid > 0 ? "Prisoners handed over at " + port.Name + " for " + Credits(paid) : "Not welcome here");
                }
            }
            GUI.contentColor = Dim;
            GUI.Label(new Rect(r.x + 12, r.y + 64, w - 24, 18), yard != null
                ? "Pick a slot, then a module to fit.   Prices are this yard's market.   F2 to close"
                : "Bring the ship alongside a shipyard (or a haven) to refit.   F2 to close", small);

            // The slots and what is in them.
            var view = new Rect(r.x + 8, r.y + 84, w - 16, listH);
            var content = new Rect(0, 0, w - 36, loadout.Slots.Count * rowH);
            outfitScroll = GUI.BeginScrollView(view, outfitScroll, content);
            for (int i = 0; i < loadout.Slots.Count; i++)
            {
                var slot = loadout.Slots[i];
                string fitted = loadout.Fitted[i] is ModuleKind m ? ModuleSpec.Of(m).Name : "(empty)";
                if (slot.LayoutIndex >= 0 && slot.LayoutIndex < ship.Combat.Subsystems.Count)
                {
                    var sub = ship.Combat.Subsystems[slot.LayoutIndex];
                    fitted += sub.Destroyed ? "  (shot away)" : sub.Health < 0.99 ? "  (" + Percent(sub.Health) + ")" : "";
                }
                if (slot.Stock != null && loadout.Fitted[i] != slot.Stock) fitted += "   was " + ModuleSpec.Of(slot.Stock.Value).Name.ToLowerInvariant();
                var rr = new Rect(0, i * rowH, content.width, rowH - 1);
                string where = slot.BearsOnBow && slot.Kind == SlotKind.HeavyMount ? " (bow)" : "";
                if (GUI.Button(rr, "  " + slot.Name + where + "   " + fitted, i == outfitSlot ? rowSelected : row))
                    outfitSlot = i == outfitSlot ? -1 : i;
            }
            GUI.EndScrollView();

            if (options == 0) { GUI.contentColor = old; return; }
            float y = r.y + 92 + listH;
            GUI.contentColor = Accent;
            GUI.Label(new Rect(r.x + 12, y, w - 24, 18), "Fit into " + loadout.Slots[outfitSlot].Name.ToLowerInvariant() + ":", small);
            y += 22;
            foreach (var m in ModuleSpec.Catalogue)
                if (Outfitting.CanFit(loadout, outfitSlot, m.Kind))
                {
                    OutfitOption(ship, yard, loadout, m.Kind, m.Name, r.x, y, w, rowH);
                    y += rowH;
                }
            if (Outfitting.CanFit(loadout, outfitSlot, null)) OutfitOption(ship, yard, loadout, null, "Empty the bay", r.x, y, w, rowH);
            GUI.contentColor = old;
        }

        void OutfitOption(Ship ship, Settlement yard, Loadout loadout, ModuleKind? module, string name, float x, float y, float w, float rowH)
        {
            bool current = loadout.Fitted[outfitSlot] == module;
            var q = Outfitting.Quote(ship, yard, outfitSlot, module);
            GUI.contentColor = current ? Accent : Color.white;
            GUI.Label(new Rect(x + 20, y, 200, rowH), name, small);
            GUI.contentColor = Dim;
            string price = current ? "fitted"
                : !q.InStock ? "yard is short of " + GoodInfo.Of(q.Short).Name.ToLowerInvariant()
                : q.Net >= 0 ? Credits(q.Net) : "+" + Credits(-q.Net) + " back";
            GUI.Label(new Rect(x + 220, y, 250, rowH), price, small);
            GUI.contentColor = Color.white;
            if (current || !q.InStock) return;
            if (!GUI.Button(new Rect(x + w - 100, y - 1, 84, rowH - 1), module == null ? "Strip" : "Fit", button)) return;
            var outcome = Outfitting.Fit(app.Economy, ship, yard, outfitSlot, module, out var paid);
            app.ShowToast(outcome switch
            {
                OutfitOutcome.Ok => (module == null ? "Bay emptied" : name + " fitted") + (paid.Net >= 0 ? " for " + Credits(paid.Net) : ", " + Credits(-paid.Net) + " back"),
                OutfitOutcome.CantAfford => "Not enough credits",
                OutfitOutcome.NoStock => yard.Name + " is short of the materials",
                OutfitOutcome.HoldTooFull => "Empty the hold before taking the racks out",
                OutfitOutcome.BrigOccupied => "Hand over the prisoners before taking the brig out",
                OutfitOutcome.Busy => "Not while fighting or boarded",
                OutfitOutcome.CannotDock => "Not welcome to refit here",
                _ => "Can't fit that here",
            });
        }

        // ---------------------------------------------------------------- spawn (dev/sandbox)

        static readonly ShipClass[] SpawnClasses = (ShipClass[])Enum.GetValues(typeof(ShipClass));

        /// <summary>
        /// U.SPC-T9 dev/sandbox tool, redesigned as its own full screen rather than a small corner panel:
        /// every <see cref="ShipClass"/> laid out as a grid of real rendered hull previews
        /// (<see cref="Rendering.ShipThumbnails"/>, the same model a live ship uses) -- click one to
        /// spawn a ship of it (a random design, if the class has more than one) at the camera's current
        /// position and immediately pilot it. See <see cref="SolarSystemApp.SpawnShip"/>.
        /// </summary>
        void DrawSpawn()
        {
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none, panel);
            RegisterUi(new Rect(0, 0, Screen.width, Screen.height));

            GUI.Label(new Rect(40, 24, 500, 30), "SPAWN & PILOT", title);
            GUI.contentColor = Dim;
            GUI.Label(new Rect(40, 56, 700, 20), "Click a hull to spawn and pilot it at the camera   ·   V to close", small);
            GUI.contentColor = Color.white;

            if (GUI.Button(new Rect(Screen.width - 210, 28, 160, 26), spawnHostile ? "FACTION: HOSTILE" : "FACTION: HOME", spawnHostile ? buttonActive : button))
                spawnHostile = !spawnHostile;
            if (GUI.Button(new Rect(Screen.width - 40, 24, 30, 30), "X", button))
                app.ShowSpawn = false;

            var faction = spawnHostile ? app.World.Pirates : app.World.Sol;
            const float cell = 176f, gap = 20f, top = 96f, labelH = 30f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((Screen.width - 80 + gap) / (cell + gap)));
            float gridW = perRow * (cell + gap) - gap;
            float startX = (Screen.width - gridW) * 0.5f;

            for (int i = 0; i < SpawnClasses.Length; i++)
            {
                var cls = SpawnClasses[i];
                int col = i % perRow, line = i / perRow;
                var r = new Rect(startX + col * (cell + gap), top + line * (cell + labelH + gap), cell, cell + labelH);
                bool hover = r.Contains(Event.current.mousePosition);
                GUI.Box(r, GUIContent.none, hover ? rowSelected : row);
                var tex = ShipThumbnails.For(cls, faction);
                if (tex != null) GUI.DrawTexture(new Rect(r.x + 6, r.y + 6, cell - 12, cell - 12), tex, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(r.x, r.yMax - labelH, cell, labelH), ShipClassSpec.Of(cls).DisplayName, title);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    app.SpawnShip(cls, spawnHostile);
                    app.ShowSpawn = false;
                }
            }
        }

        // ---------------------------------------------------------------- cargo hold

        /// <summary>
        /// The player-piloted ship's grid cargo hold (<see cref="CargoHold"/>): click an occupied cell
        /// to pick up its crate, then click an empty cell to drop it there; clicking the picked-up
        /// crate again cancels. Separate from, and drawn independently of, the market board above.
        /// </summary>
        void DrawCargo()
        {
            var ship = app.World.PilotedShip;
            if (ship == null)
            {
                cargoSelected = null;
                return;
            }
            var hold = ship.PersonalHold;
            if (cargoSelectedHold != hold) cargoSelected = null;
            cargoSelectedHold = hold;
            const float cell = 28f;
            float w = Math.Max(260, hold.Width * cell + 24);
            float h = cargoOpen ? 74 + hold.Height * cell + (cargoSelected != null ? 28 : 0) : 34;
            var r = Panel(new Rect(10, Screen.height - h - 56, w, h));

            if (GUI.Button(new Rect(r.x + 8, r.y + 6, w - 16, 22), (cargoOpen ? "[-]  " : "[+]  ") + "CARGO HOLD  ·  " + ship.Name, title)) cargoOpen = !cargoOpen;
            if (!cargoOpen) return;
            GUI.contentColor = Dim;
            GUI.Label(new Rect(r.x + 12, r.y + 28, w - 24, 32),
                cargoSelected != null ? "Click a cell to move the crate there  (click it again to cancel)" : "Click a crate to pick it up   [I to close]", small);
            GUI.contentColor = Color.white;

            var atCell = new CargoItem[hold.Width, hold.Height];
            foreach (var item in hold.Items)
                for (int dx = 0; dx < item.Width; dx++)
                    for (int dy = 0; dy < item.Height; dy++)
                        atCell[item.X + dx, item.Y + dy] = item;

            float gx = r.x + 12, gy = r.y + 48;
            for (int y = 0; y < hold.Height; y++)
            {
                for (int x = 0; x < hold.Width; x++)
                {
                    var occupant = atCell[x, y];
                    var cellRect = new Rect(gx + x * cell, gy + y * cell, cell - 2, cell - 2);
                    string label = occupant != null ? GoodAbbreviation(occupant.Good) : "";
                    bool isSelected = occupant != null && occupant == cargoSelected;
                    if (GUI.Button(cellRect, label, isSelected ? buttonActive : button))
                        ClickCargoCell(hold, x, y, occupant);
                }
            }

            if (cargoSelected != null)
            {
                float by = gy + hold.Height * cell + 6;
                if (GUI.Button(new Rect(r.x + 12, by, 90, 22), "Rotate", button))
                    hold.Move(cargoSelected, cargoSelected.X, cargoSelected.Y, !cargoSelected.Rotated);
            }
        }

        void ClickCargoCell(CargoHold hold, int x, int y, CargoItem occupant)
        {
            if (cargoSelected == null)
            {
                if (occupant != null) cargoSelected = occupant;
                return;
            }
            if (occupant == cargoSelected)
            {
                cargoSelected = null;
                return;
            }
            if (hold.Move(cargoSelected, x, y, cargoSelected.Rotated)) cargoSelected = null;
            else app.ShowToast("Doesn't fit there");
        }

        static string GoodAbbreviation(Good g)
        {
            string name = GoodInfo.Of(g).Name;
            return name.Length <= 3 ? name : name.Substring(0, 3);
        }

        static string Tonnes(double t)
        {
            double a = Math.Abs(t);
            if (a >= 1e6) return (t / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + " Mt";
            if (a >= 1e4) return (t / 1e3).ToString("0", CultureInfo.InvariantCulture) + " kt";
            return t.ToString("N0", CultureInfo.InvariantCulture) + " t";
        }

        static string Credits(double c)
        {
            double a = Math.Abs(c);
            if (a >= 1e9) return (c / 1e9).ToString("0.00", CultureInfo.InvariantCulture) + " B cr";
            if (a >= 1e6) return (c / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + " M cr";
            if (a >= 1e3) return (c / 1e3).ToString("0", CultureInfo.InvariantCulture) + " k cr";
            return c.ToString("0", CultureInfo.InvariantCulture) + " cr";
        }

        static string People(double p) =>
            p >= 1e6 ? (p / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + " M" : (p / 1e3).ToString("0", CultureInfo.InvariantCulture) + " k";

        static string SpaceText(Compartment c)
        {
            string who = c.Taken ? "TAKEN, " + c.Boarders.ToString("0", CultureInfo.InvariantCulture) + " boarders"
                : c.Contested ? "FIGHTING: " + c.Boarders.ToString("0", CultureInfo.InvariantCulture) + " boarders v " + (c.Marines + c.Crew).ToString("0", CultureInfo.InvariantCulture)
                : (c.Crew + c.Marines).ToString("0", CultureInfo.InvariantCulture) + " aboard";
            return who + (c.Vented ? (c.SealIn > 0 ? "  ·  VENTING" : "  ·  sealed, in vacuum") : "");
        }

        string OrderText(GroupOrder o)
        {
            string where = o.Body >= 0 ? app.Catalog[o.Body].Name : "";
            switch (o.Kind)
            {
                case OrderKind.Ambush: return "Lie in wait on the hyperlanes (toward " + where + ")";
                case OrderKind.Attack: return "Attack " + where;
                case OrderKind.Defend: return "Defend " + where;
                case OrderKind.Siege: return "Besiege " + where;
                case OrderKind.Repair: return "Refit at " + where;
                case OrderKind.Raid: return "Raid shipping at " + where;
                case OrderKind.Hunt: return "Hunt pirates at " + where;
                default: return "Patrol, next " + where;
            }
        }

        GUIStyle tickerStyle;

        static string Percent(double f) => (Math.Max(0, f) * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

        void DrawTicker()
        {
            var events = app.World.Events;
            int n = Math.Min(4, events.Count);
            if (n == 0) return;
            float w = 640, rowH = 17;
            var r = Panel(new Rect((Screen.width - w) * 0.5f, 70, w, 10 + n * rowH));
            var old = GUI.contentColor;
            for (int i = 0; i < n; i++)
            {
                var e = events[events.Count - n + i];
                GUI.contentColor = KindColor(e.Kind);
                tickerStyle ??= new GUIStyle(small) { wordWrap = false, clipping = TextClipping.Clip };
                GUI.Label(new Rect(r.x + 10, r.y + 5 + i * rowH, w - 20, rowH), NewsLine(e), tickerStyle);
            }
            GUI.contentColor = old;
        }

        static Color KindColor(WorldEventKind k) => k switch
        {
            WorldEventKind.Battle => new Color(1f, 0.7f, 0.6f),
            WorldEventKind.Capture => new Color(1f, 0.85f, 0.4f),
            WorldEventKind.Opportunity => new Color(0.65f, 1f, 0.75f),
            WorldEventKind.Diplomacy => new Color(0.7f, 0.9f, 1f),
            WorldEventKind.Economy => new Color(1f, 0.72f, 0.4f),
            WorldEventKind.Market => new Color(0.95f, 0.95f, 0.55f),
            _ => Dim,
        };

        string NewsLine(WorldEvent e) => app.Clock.Utc.Year == Astro.ToDateTime(e.Jd).Year
            ? Astro.ToDateTime(e.Jd).ToString("MMM dd", CultureInfo.InvariantCulture) + "   " + e.Text
            : Astro.ToDateTime(e.Jd).ToString("yyyy MMM dd", CultureInfo.InvariantCulture) + "   " + e.Text;

        // ---------------------------------------------------------------- news feed

        static readonly string[] NewsFilters = { "ALL", "MARKETS", "WAR", "SHIPS & OTHER" };
        int newsFilter;
        Vector2 newsScroll;
        GUIStyle newsRow;
        readonly List<WorldEvent> newsShown = new List<WorldEvent>();

        /// <summary>Which filter tabs a report shows under: diplomacy (ceasefires, embargoes) is both markets and war news.</summary>
        static bool InFilter(WorldEventKind k, int filter) => filter switch
        {
            1 => k == WorldEventKind.Economy || k == WorldEventKind.Market || k == WorldEventKind.Diplomacy,
            2 => k == WorldEventKind.Battle || k == WorldEventKind.Capture || k == WorldEventKind.Loss || k == WorldEventKind.Diplomacy,
            3 => k == WorldEventKind.Info || k == WorldEventKind.Launch || k == WorldEventKind.Opportunity,
            _ => true,
        };

        /// <summary>
        /// The news feed (X), in place of the ticker: everything the sim has reported, newest first, filtered
        /// to markets (shocks, price spikes, embargoes), war (battles, captures, surrenders) or the rest. Every
        /// report is what actually happened; clicking one selects where it happened (and opens the market for
        /// market news) so the player can act on it.
        /// </summary>
        void DrawNews()
        {
            const float w = 760;
            float h = Mathf.Min(Screen.height * 0.5f, 420);
            var r = Panel(new Rect((Screen.width - w) * 0.5f, 70, w, h));
            var events = app.World.Events;
            if (GUI.Button(new Rect(r.x + 8, r.y + 6, 120, 22), "[-]  NEWS", title)) app.ShowNews = false;
            var old = GUI.contentColor;
            GUI.contentColor = Dim;
            GUI.Label(new Rect(r.x + 130, r.y + 9, w - 140, 18), "as it happens  ·  click a report to select where it happened  ·  X to close", small);
            GUI.contentColor = old;
            for (int i = 0; i < NewsFilters.Length; i++)
                if (GUI.Button(new Rect(r.x + 10 + i * 128, r.y + 32, 122, 20), NewsFilters[i], newsFilter == i ? buttonActive : button))
                {
                    newsFilter = i;
                    newsScroll = Vector2.zero;
                }

            newsRow ??= new GUIStyle(row) { fontSize = 11, wordWrap = true, padding = new RectOffset(6, 4, 2, 2) };
            newsShown.Clear();
            for (int i = events.Count - 1; i >= 0 && newsShown.Count < 200; i--)
                if (InFilter(events[i].Kind, newsFilter)) newsShown.Add(events[i]);

            var view = new Rect(r.x + 4, r.y + 58, w - 8, h - 62);
            float width = view.width - 20, total = 0;
            var heights = new float[newsShown.Count];
            for (int i = 0; i < newsShown.Count; i++) total += heights[i] = newsRow.CalcHeight(new GUIContent(NewsLine(newsShown[i])), width);
            newsScroll = GUI.BeginScrollView(view, newsScroll, new Rect(0, 0, width, Mathf.Max(total, 1)));
            float y = 0;
            for (int i = 0; i < newsShown.Count; i++)
            {
                var e = newsShown[i];
                var rr = new Rect(0, y, width, heights[i]);
                y += heights[i];
                GUI.contentColor = KindColor(e.Kind);
                if (e.Body < 0 || e.Body >= app.Catalog.Count)
                {
                    GUI.Label(rr, NewsLine(e), newsRow);
                    continue;
                }
                if (GUI.Button(rr, NewsLine(e), newsRow))
                {
                    app.Select(Target.OfBody(e.Body));
                    if (e.Kind == WorldEventKind.Economy || e.Kind == WorldEventKind.Market) app.ShowMarket = true;
                }
            }
            GUI.contentColor = old;
            if (newsShown.Count == 0) GUI.Label(new Rect(8, 4, width, 18), "Nothing to report yet.", small);
            GUI.EndScrollView();
        }

        static string JourneyText(Ship s)
        {
            var names = new List<string>();
            foreach (var hop in s.Journey) names.Add(hop.Lane.Name);
            return string.Join(", ", names);
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
                case SpaceCamera.CameraMode.Focus when cam.PilotLocked:
                    s = "PILOTING  ·  " + TargetName(cam.FocusTarget) + (app.Piloted.Ship != null ? "  ·  PROPELLANT " + (app.Piloted.Ship.Propellant <= 1e-6 ? "DRY" : Percent(app.Piloted.Ship.PropellantFraction)) : "") +
                        (app.World.Law != null && app.World.Law.Player.Wanted ? "  ·  WANTED " + Credits(app.World.Law.Player.TotalBounty) : "") +
                        SensorText() +
                        "   (WASD throttle/strafe, mouse to steer, Z/X roll, F flight assist, Shift boost, F3 silent running, P to release)";
                    break;
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

            // Tanks running low under the player's own hand: an emergency tanker can be called out.
            var flown = app.IsPiloting ? app.Piloted.Ship : null;
            if (flown != null && flown.Battle == null && (flown.PropellantFraction < 0.15 || app.TankerDueS >= 0))
            {
                var call = Panel(new Rect(r.x - 178, r.y, 170, 30));
                string label = app.TankerDueS >= 0 ? "TANKER IN " + Mathf.CeilToInt(app.TankerDueS) + " s" : "CALL A TANKER";
                if (GUI.Button(new Rect(call.x + 4, call.y + 4, call.width - 8, 22), label, app.TankerDueS >= 0 ? buttonActive : button)) app.CallTanker();
            }

            // Level the view to the system's plane (a smooth swing, not a snap).
            var level = Panel(new Rect(r.xMax + 8, r.y, 120, 30));
            if (GUI.Button(new Rect(level.x + 4, level.y + 4, level.width - 8, 22), cam.Levelling ? "LEVELLING…" : "LEVEL  [R]", cam.Levelling ? buttonActive : button))
                app.LevelView();
        }

        /// <summary>The helm's sensor readout: running silent or not, the ship's own signature (lit up by a burn or a
        /// fight), and the contacts its scanners hold -- identified, by class only, and unknown blips.</summary>
        string SensorText()
        {
            var sensors = app.World.Sensors;
            var ship = app.Piloted.Ship;
            if (sensors == null || ship == null) return "";
            var book = sensors.Player;
            int id = book.CountLive(ContactLevel.Identified), cls = book.CountLive(ContactLevel.Class), blips = book.CountLive(ContactLevel.Blip);
            return (ship.SilentRunning ? "  ·  SILENT RUNNING" : "") +
                "  ·  SIG " + sensors.PilotSignature.ToString(sensors.PilotSignature < 10 ? "0.00" : "0", CultureInfo.InvariantCulture) +
                "  ·  CONTACTS " + id + " id / " + cls + " class / " + blips + " unknown";
        }

        /// <summary>A patrol's hail of the piloted ship, above the salvage readout: hold still for a scan, or pay the fine (1) or be fired on.</summary>
        void DrawHail()
        {
            var hail = app.IsPiloting ? app.World.Law?.Hail : null;
            if (hail == null) return;
            int seconds = Mathf.CeilToInt((float)hail.SecondsLeft(app.Clock.Jd));
            string s = "HAILED  ·  " + hail.By.Name.ToUpperInvariant() + " PATROL " + hail.Patrol.Name.ToUpperInvariant() + "  ·  " +
                (hail.Interdiction ? "HEAVE TO:  1 pay the " + Credits(hail.Fine) + " fine, or be fired on in " + seconds + " s"
                    : "CARGO SCAN: hold position, " + seconds + " s  (running is a crime)");
            var line = new GUIContent(s);
            float lw = text.CalcSize(line).x + 28;
            var r = Panel(new Rect((Screen.width - lw) * 0.5f, Screen.height - 112, lw, 30));
            GUI.contentColor = hail.Interdiction ? new Color(1f, 0.55f, 0.45f) : new Color(1f, 0.85f, 0.4f);
            GUI.Label(new Rect(r.x + 14, r.y + 6, lw, 20), line, text);
            GUI.contentColor = Color.white;
        }

        /// <summary>A record in a line: the bounties posted, notoriety, and how many crimes are on file.</summary>
        static string RecordText(LawRecord r)
        {
            var parts = new List<string>();
            if (r.Wanted)
            {
                var by = new List<string>();
                foreach (var kv in r.Bounty) by.Add(kv.Key.Name + " " + Credits(kv.Value));
                parts.Add("WANTED: " + string.Join(", ", by));
            }
            parts.Add("notoriety " + r.Notoriety.ToString("0", CultureInfo.InvariantCulture));
            parts.Add(r.Crimes.Count + " crime" + (r.Crimes.Count == 1 ? "" : "s") + " on file");
            return string.Join(",  ", parts);
        }

        /// <summary>
        /// Above the status bar while piloting: the approach readout for the nearest derelict or wreck (range,
        /// closing speed, what is still needed to latch), or -- latched on -- the hulk's compartments with who is
        /// in each, what is still aboard, the part most worth stripping and the number-key choices.
        /// </summary>
        void DrawSalvage()
        {
            var s = app.Salvage;
            if (s == null)
            {
                if (string.IsNullOrEmpty(app.SalvageApproach)) return;
                var line = new GUIContent(app.SalvageApproach);
                float lw = text.CalcSize(line).x + 28;
                var lr = Panel(new Rect((Screen.width - lw) * 0.5f, Screen.height - 76, lw, 30));
                GUI.contentColor = Accent;
                GUI.Label(new Rect(lr.x + 14, lr.y + 6, lw, 20), line, text);
                GUI.contentColor = Color.white;
                return;
            }

            var rows = new List<string>();
            foreach (var c in s.Interior.Compartments)
            {
                var who = new List<string>();
                if (c.Marines > 0.5) who.Add(c.Marines.ToString("0", CultureInfo.InvariantCulture) + " raiders");
                if (c.Boarders > 0.5) who.Add(c.Boarders.ToString("0", CultureInfo.InvariantCulture) + " our marines");
                if (c.Survivors > 0.5) who.Add(c.Survivors.ToString("0", CultureInfo.InvariantCulture) + " survivors");
                if (c.Vented) who.Add("open to space");
                rows.Add(c.Name + ":  " + (who.Count == 0 ? "empty" : string.Join(", ", who)));
            }
            var aboard = new List<string>();
            foreach (var stack in s.Manifest)
                aboard.Add(GoodInfo.Of(stack.Good).Name + " " + stack.Tonnes.ToString("0", CultureInfo.InvariantCulture) + " t");
            rows.Add("Aboard:  " + (aboard.Count == 0 ? "nothing left" : string.Join(", ", aboard)));
            var part = s.BestPart();
            if (part != null)
                rows.Add("Best part:  " + SalvageSession.KindName(part.Kind) + " (" + part.Health.ToString("P0", CultureInfo.InvariantCulture) + ")" +
                         (s.FitFor(part) != null ? ", fits our own shot-away mount" : ", scrap for " + GoodInfo.Of(SalvageSession.ScrapOf(part.Kind)).Name.ToLowerInvariant()));
            if (s.SurvivorsRescued > 0.5) rows.Add("Rescued:  " + s.SurvivorsRescued.ToString("0", CultureInfo.InvariantCulture) + " survivors");

            const float rowH = 16;
            float w = 520, h = 62 + rows.Count * rowH;
            var r = Panel(new Rect((Screen.width - w) * 0.5f, Screen.height - h - 48, w, h));
            GUI.Label(new Rect(r.x + 12, r.y + 6, w - 24, 20), "LATCHED  ·  " + s.Name.ToUpperInvariant() + (s.Hostile ? "  ·  HOSTILES ABOARD" : ""), title);
            for (int i = 0; i < rows.Count; i++)
                GUI.Label(new Rect(r.x + 12, r.y + 30 + i * rowH, w - 24, rowH), rows[i], small);
            GUI.contentColor = Accent;
            GUI.Label(new Rect(r.x + 12, r.yMax - 26, w - 24, 20), app.SalvageChoices(), text);
            GUI.contentColor = Color.white;
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
                "W A S D   swim          Q / E  or  C / ]   down / up          Space   pause (eases to a stop and back)",
                "Shift   boost           Ctrl   fine             Wheel   swim speed / zoom",
                "Right-drag   look / orbit                Left-click   select    (double: focus)",
                "F   focus selection     Esc   leave focus / deselect     Home   system view",
                "G  or  `   toggle GAMEPLAY / REALISTIC scale",
                "1-9, 0   planets (twice to focus)    Tab   cycle ships    M   market    B   watch a battle    N   hyperlanes",
                "K   satellites    R   level the view to the system plane",
                "Navigator  STAR SYSTEMS   fly to another star (Esc to skip the trip)",
                "P   pilot the selected ship     ,  .   time warp     T   now     O  orbits   L  labels   H  help   U  HUD",
                "Piloting   mouse steer   W/S thrust   A/D Q/E strafe   Z/X roll   F flight assist (off: drift, turn and burn)",
                "I   cargo hold  (while piloting)   click a crate, then a cell, to move it",
                "F2  outfitting at a shipyard (or Outfit on the market): fit guns, shields, drives, plate, racks, scanners, a brig",
                "F3  silent running (piloting): transponder off, heat damped -- only what your scanners hold is shown: blips, then class, then name",
                "F4  bounty board of the port in view: outlaws wanted there, their records, where last seen, who hunts them",
                "Derelicts and wrecks  (while piloting)   match velocity alongside, 1 latch on, then 1-4 as the panel shows",
                "M   market  Buy/Sell and Refuel appear while piloting near the shown settlement's body",
                "X   news feed: shocks, price spikes, embargoes, battles and captures as they happen",
                "Law   T lock a ship, hold fire on it to attack (a crime if seen)   patrols scan: hold still   1 pay a fine when hailed",
                "V   spawn & pilot: pick a hull from the grid to spawn and pilot it at the camera",
                "F5  quicksave   F8  quickload   F6  save slots   (autosaves every 5 minutes and on quit; -continue resumes the latest)",
            };
            float w = 610, h = helpOpen ? 42 + help.Length * 17 : 34;
            var r = Panel(new Rect(10, Screen.height - h - 10, w, h));
            if (GUI.Button(new Rect(r.x + 8, r.y + 6, w - 16, 22), (helpOpen ? "[-]  " : "[+]  ") + "CONTROLS", title)) helpOpen = !helpOpen;
            if (!helpOpen) return;
            for (int i = 0; i < help.Length; i++)
                GUI.Label(new Rect(r.x + 12, r.y + 34 + i * 17, w - 20, 17), help[i], small);
        }

        void DrawToast()
        {
            if (string.IsNullOrEmpty(app.Toast) || app.ToastAge > 3.5f) return;
            float a = Mathf.Clamp01(3.5f - app.ToastAge);
            var content = new GUIContent(app.Toast);
            float w = toast.CalcSize(content).x + 40;
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, a);
            var r = new Rect((Screen.width - w) * 0.5f, 150, w, 34);
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(r.x + 20, r.y + 7, w, 22), content, toast);
            GUI.color = old;
        }

        // ---------------------------------------------------------------- formatting

        string TargetName(Target t) => t.IsBody ? app.Catalog[t.Body].Name : t.IsShip ? t.Ship.Name : t.IsStructure ? t.Structure.Name : t.IsRing ? t.Ring.Name : "";

        static string KindName(BodyDefinition b)
        {
            switch (b.Kind)
            {
                case BodyKind.Star:
                    string kind = b.TeffK < 2400 ? "Brown dwarf" : b.RadiusKm < 0.05 * 695700 ? "White dwarf"
                        : b.TeffK < 3900 ? "Red dwarf star" : b.TeffK < 5300 ? "Orange dwarf star" : b.TeffK < 7500 ? "Yellow dwarf star" : "White main-sequence star";
                    return string.IsNullOrEmpty(b.SpectralType) ? kind : kind + "  (" + b.SpectralType + ")";
                case BodyKind.DwarfPlanet: return "Dwarf planet";
                case BodyKind.Asteroid: return "Asteroid";
                case BodyKind.Moon: return "Moon";
                case BodyKind.Station: return "Free-flying station";
                case BodyKind.Barycentre: return "Barycentre";
                default:
                    if (b.GasGiant || b.Name == "Jupiter" || b.Name == "Saturn") return "Gas giant";
                    if (b.Name == "Uranus" || b.Name == "Neptune") return "Ice giant";
                    double earths = b.Gm / 398600.435;
                    return earths >= 8 ? "Neptune-like planet" : earths >= 2 ? "Super-Earth" : "Terrestrial planet";
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
            return s < 120 ? s.ToString("0", CultureInfo.InvariantCulture) + " s" : s < 7200 ? (s / 60).ToString("0.0", CultureInfo.InvariantCulture) + " min"
                : s < 172800 ? (s / 3600).ToString("0.0", CultureInfo.InvariantCulture) + " h" : (s / 86400).ToString("0.0", CultureInfo.InvariantCulture) + " days";
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
