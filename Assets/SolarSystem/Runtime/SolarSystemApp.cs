using System;
using System.Collections.Generic;
using SolarSystem.Cameras;
using SolarSystem.Core;
using SolarSystem.Input;
using SolarSystem.UI;
using SolarSystem.Views;
using UnityEngine;

namespace SolarSystem
{
    /// <summary>
    /// Owns the simulation and drives every frame:
    /// input → clock → ephemeris → fleet → scale layout → ships → camera → camera-relative views.
    ///
    /// Floating origin: the Unity camera never moves from the world origin. All positions are kept
    /// in double-precision scene units and every renderer is placed at (scene - camera) each frame,
    /// so a 3 km ship next to the camera renders cleanly while Neptune sits 4.5 million units away.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class SolarSystemApp : MonoBehaviour, ICameraWorld
    {
        public BodyCatalog Catalog { get; private set; }
        public Ephemeris Ephemeris { get; private set; }
        public SimClock Clock { get; private set; }
        public ScaleModel Scale { get; private set; }
        public Fleet Fleet { get; private set; }
        public SpaceCamera SpaceCamera { get; private set; }
        public Camera UnityCamera { get; private set; }

        /// <summary>The real main belt and Jupiter Trojans (JPL SBDB), drawn on the GPU.</summary>
        public AsteroidBelt AsteroidBelt { get; private set; }
        public InputState Input { get; } = new InputState();

        public Target Selected { get; private set; } = Target.None;
        public Target Hovered { get; private set; } = Target.None;

        public bool ShowOrbits { get; set; } = true;
        public bool ShowLabels { get; set; } = true;
        public bool ShowHelp { get; set; } = true;
        public bool ShowHud { get; set; } = true;

        /// <summary>1 = gameplay, 0 = realistic; the value the scale is animating toward.</summary>
        public double ScaleGoal { get; private set; } = 1;
        public bool ScaleAnimating => scaleT < 1f;

        public string Toast { get; private set; }
        public float ToastAge { get; private set; } = 99f;

        /// <summary>GUI-space rects drawn by the HUD last frame; clicks inside them do not reach the world.</summary>
        public readonly List<Rect> UiRects = new List<Rect>();

        DVec3[] bodyScene;
        double[] bodyRadius;
        BodyView[] bodyViews;
        LineStrip[] orbitLines;
        DVec3[] orbitSamples;
        Vector3[] orbitPoints;
        float orbitRefreshTimer;

        ShipView[] shipViews;
        DVec3[] shipScene;
        Quaternion[] shipRotation;
        double[] shipLength;
        ShipState[] shipStates;
        bool[] shipRotationInitialised;
        LineStrip pathLine;
        Vector3[] pathPoints = new Vector3[64];

        Transform worldRoot;
        Hud hud;
        double scaleFrom = 1;
        float scaleT = 1f;
        const float ScaleSeconds = 2.4f;
        int lastNumberKey = -1;
        float lastNumberTime = -10f;

        // ---------------------------------------------------------------- lifecycle

        void Awake()
        {
            Application.targetFrameRate = 144;
            QualitySettings.vSyncCount = 1;

            Catalog = BodyCatalog.CreateSolSystem();
            Ephemeris = new Ephemeris(Catalog);
            Clock = SimClock.StartingNow();
            Scale = new ScaleModel { Blend = 1 };
            Ephemeris.Update(Clock.Jd);
            Fleet = Fleet.CreateDefault(Ephemeris, Clock.Jd);

            SetupCamera();
            worldRoot = new GameObject("World").transform;
            Starfield.Create(worldRoot);

            bodyScene = new DVec3[Catalog.Count];
            bodyRadius = new double[Catalog.Count];
            bodyViews = new BodyView[Catalog.Count];
            orbitLines = new LineStrip[Catalog.Count];
            for (int i = 0; i < Catalog.Count; i++)
            {
                bodyViews[i] = new BodyView(Catalog[i], worldRoot);
                if (Catalog[i].ParentIndex >= 0) orbitLines[i] = new LineStrip("Orbit " + Catalog[i].Name, worldRoot);
            }
            orbitSamples = new DVec3[257];
            orbitPoints = new Vector3[orbitSamples.Length];

            BuildShipViews();
            pathLine = new LineStrip("Flight path", worldRoot);
            pathLine.Visible = false;

            hud = new Hud(this);
            SpaceCamera = new SpaceCamera(this);
            AsteroidBelt = AsteroidBelt.Create(this);

            Scale.Layout(Ephemeris, bodyScene);
            var earth = Catalog["Earth"].Index;
            var start = bodyScene[earth] + new DVec3(-0.6, 0.35, -1).Normalized * (Scale.DisplayRadius(Catalog[earth]) * 9);
            SpaceCamera.PlaceFree(start, 0, 0);
            var look = (bodyScene[earth] - start).Normalized;
            SpaceCamera.PlaceFree(start, (float)(Math.Atan2(look.X, look.Z) * Mathf.Rad2Deg), (float)(-Math.Asin(look.Y) * Mathf.Rad2Deg));
            Select(Target.OfBody(earth));
        }

        void SetupCamera()
        {
            UnityCamera = Camera.main;
            if (UnityCamera == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                UnityCamera = go.AddComponent<Camera>();
            }
            UnityCamera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            UnityCamera.clearFlags = CameraClearFlags.SolidColor;
            UnityCamera.backgroundColor = Color.black;
            UnityCamera.fieldOfView = 60f;
            UnityCamera.nearClipPlane = 0.1f;
            UnityCamera.farClipPlane = 5e8f;
            UnityCamera.allowHDR = false;
            UnityCamera.allowMSAA = true;
            var listener = UnityCamera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = true;
        }

        void BuildShipViews()
        {
            if (shipViews != null)
                foreach (var v in shipViews) Destroy(v.Root);
            var ships = Fleet.Ships;
            shipViews = new ShipView[ships.Count];
            shipScene = new DVec3[ships.Count];
            shipRotation = new Quaternion[ships.Count];
            shipLength = new double[ships.Count];
            shipStates = new ShipState[ships.Count];
            shipRotationInitialised = new bool[ships.Count];
            for (int i = 0; i < ships.Count; i++) shipViews[i] = new ShipView(ships[i], worldRoot);
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            bool overUi = IsPointerOverUi();
            Input.Poll(overUi);
            HandleCommands();

            Clock.Advance(dt);
            Ephemeris.Update(Clock.Jd);
            Fleet.Update(Clock.Jd);

            AnimateScale(dt);
            Scale.Layout(Ephemeris, bodyScene);
            for (int i = 0; i < Catalog.Count; i++) bodyRadius[i] = Scale.DisplayRadius(Catalog[i]);
            UpdateShips(dt);

            SpaceCamera.Update(dt, Input);
            UpdateClipPlanes();
            UpdateViews(dt);
            UpdateHover();
            HandleSelectionClicks();

            ToastAge += dt;
            Cursor.lockState = Input.LookHeld ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !Input.LookHeld;
        }

        void OnGUI()
        {
            hud.Draw();
        }

        bool IsPointerOverUi()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return false;
            var p = mouse.position.ReadValue();
            var gui = new Vector2(p.x, Screen.height - p.y);
            foreach (var r in UiRects)
                if (r.Contains(gui)) return true;
            return false;
        }

        // ---------------------------------------------------------------- commands

        void HandleCommands()
        {
            var i = Input;
            if (i.ToggleScale) ToggleScale();
            if (i.TimeFaster) Clock.Faster();
            if (i.TimeSlower) Clock.Slower();
            if (i.TogglePause) Clock.Paused = !Clock.Paused;
            if (i.ResetTime) ResetToNow();
            if (i.ToggleOrbits) ShowOrbits = !ShowOrbits;
            if (i.ToggleLabels) ShowLabels = !ShowLabels;
            if (i.ToggleHelp) ShowHelp = !ShowHelp;
            if (i.ToggleHud) ShowHud = !ShowHud;
            if (i.Home) FocusSystemOverview();
            if (i.Focus) FocusSelected();
            if (i.Escape)
            {
                if (SpaceCamera.Mode != SpaceCamera.CameraMode.Free) SpaceCamera.ExitFocus();
                else Select(Target.None);
            }
            if (i.CycleShips) CycleShips(i.CycleBackward ? -1 : 1);
            if (i.NumberKey >= 0) NumberHotkey(i.NumberKey);
        }

        static readonly string[] NumberBodies = { "Sun", "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune", "Pluto" };

        void NumberHotkey(int key)
        {
            var target = Target.OfBody(Catalog[NumberBodies[key]].Index);
            bool repeat = key == lastNumberKey && Time.unscaledTime - lastNumberTime < 0.45f;
            Select(target);
            if (repeat || SpaceCamera.Mode != SpaceCamera.CameraMode.Free) SpaceCamera.FocusOn(target);
            lastNumberKey = key;
            lastNumberTime = Time.unscaledTime;
        }

        void CycleShips(int direction)
        {
            var candidates = new List<Ship>();
            foreach (var s in Fleet.Ships)
                if (!s.IsEscort) candidates.Add(s);
            if (candidates.Count == 0) return;
            int current = Selected.IsShip ? candidates.IndexOf(Selected.Ship.Controller) : -1;
            int next = ((current + direction) % candidates.Count + candidates.Count) % candidates.Count;
            Select(Target.OfShip(candidates[next]));
            if (SpaceCamera.Mode != SpaceCamera.CameraMode.Free) SpaceCamera.FocusOn(Selected);
        }

        public void Select(Target target)
        {
            Selected = target;
        }

        public void FocusSelected()
        {
            if (Selected.IsNone)
            {
                if (SpaceCamera.Mode != SpaceCamera.CameraMode.Free) SpaceCamera.ExitFocus();
                return;
            }
            bool refocus = SpaceCamera.Mode == SpaceCamera.CameraMode.Focus && SpaceCamera.FocusTarget == Selected;
            SpaceCamera.FocusOn(Selected, refocus ? SpaceCamera.DefaultFraming(Selected) : double.NaN);
        }

        public void FocusSystemOverview()
        {
            var sun = Target.OfBody(0);
            Select(sun);
            double span = Scale.LocalDistance(Catalog.Sun, 2.2 * Astro.AuKm);
            SpaceCamera.FocusOn(sun, span * 1.9 / bodyRadius[0], 52f);
        }

        public void ToggleScale()
        {
            scaleFrom = Scale.Blend;
            ScaleGoal = ScaleGoal > 0.5 ? 0 : 1;
            scaleT = 0;
            ShowToast(ScaleGoal > 0.5
                ? "GAMEPLAY SCALE: sizes and distances compressed to be navigable"
                : "REALISTIC SCALE: true proportions, 1 unit = 1,000 km");
        }

        public void SetScaleImmediate(double blend)
        {
            ScaleGoal = blend;
            scaleFrom = blend;
            Scale.Blend = blend;
            scaleT = 1;
        }

        void AnimateScale(float dt)
        {
            if (scaleT >= 1f) return;
            scaleT = Mathf.Min(1f, scaleT + dt / ScaleSeconds);
            float e = scaleT * scaleT * scaleT * (scaleT * (scaleT * 6 - 15) + 10);
            Scale.Blend = scaleFrom + (ScaleGoal - scaleFrom) * e;
        }

        public void ShowToast(string message)
        {
            Toast = message;
            ToastAge = 0;
        }

        public void ResetToNow()
        {
            double now = Astro.JulianDate(DateTime.UtcNow);
            bool backwards = now < Clock.Jd;
            Clock.SetJd(now);
            Clock.SetWarp(SimClock.DefaultWarpIndex);
            if (backwards)
            {
                // Schedules only run forward; rebuild the fleet at the new date.
                Ephemeris.Update(now);
                Fleet = Fleet.CreateDefault(Ephemeris, now);
                if (Selected.IsShip) Select(Target.None);
                if (SpaceCamera.FocusTarget.IsShip) SpaceCamera.ExitFocus();
                BuildShipViews();
            }
            ShowToast("Time reset to now");
        }

        // ---------------------------------------------------------------- ships

        void UpdateShips(float dt)
        {
            var ships = Fleet.Ships;
            double jd = Clock.Jd;
            float response = 1f - Mathf.Exp(-dt * 3f);

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < ships.Count; i++)
                {
                    var ship = ships[i];
                    if (ship.IsEscort != (pass == 1)) continue;
                    shipLength[i] = Scale.ShipLength(ship.Spec.LengthKm);
                    var st = Fleet.StateOf(ship, jd);
                    shipStates[i] = st;

                    if (ship.IsEscort)
                    {
                        int leader = ship.Leader.Id;
                        var slot = ship.FormationSlot;
                        var offset = shipRotation[leader] * new Vector3((float)slot.X, (float)slot.Y, (float)slot.Z) * (float)shipLength[leader];
                        shipScene[i] = shipScene[leader] + new DVec3(offset.x, offset.y, offset.z);
                        shipRotation[i] = shipRotation[leader];
                        shipRotationInitialised[i] = true;
                        continue;
                    }

                    DVec3 facing;
                    if (ship.Phase == ShipPhase.Parked)
                    {
                        var orbit = ship.Orbit;
                        shipScene[i] = bodyScene[orbit.Body] + Scale.LocalOffset(Catalog[orbit.Body], orbit.OffsetAt(jd));
                        facing = st.Facing.SwapYZ;
                    }
                    else
                    {
                        shipScene[i] = TransitScene(ship, st.Progress, out var travel);
                        facing = st.Progress < 0.5 ? travel : -travel;
                    }

                    var f = new Vector3((float)facing.X, (float)facing.Y, (float)facing.Z);
                    if (f.sqrMagnitude < 1e-12f) f = Vector3.forward;
                    var up = Mathf.Abs(Vector3.Dot(f.normalized, Vector3.up)) > 0.98f ? Vector3.right : Vector3.up;
                    var goal = Quaternion.LookRotation(f, up);
                    shipRotation[i] = shipRotationInitialised[i] ? Quaternion.Slerp(shipRotation[i], goal, response) : goal;
                    shipRotationInitialised[i] = true;
                }
            }
        }

        /// <summary>Scene position of a transiting ship at a fraction of its path (see <see cref="ScaleModel.TransitScene"/>).</summary>
        public DVec3 TransitScene(Ship ship, double progress, out DVec3 travelDirection) =>
            Scale.TransitScene(Ephemeris, bodyScene, ship, progress, out travelDirection);

        public DVec3 ShipScene(Ship ship) => shipScene[ship.Id];
        public double ShipDisplayLength(Ship ship) => shipLength[ship.Id];
        public ShipState ShipStateOf(Ship ship) => shipStates[ship.Id];
        public Quaternion ShipRotation(Ship ship) => shipRotation[ship.Id];

        // ---------------------------------------------------------------- ICameraWorld

        public DVec3 BodyScene(int body) => bodyScene[body];
        public double BodyDisplayRadius(int body) => bodyRadius[body];

        public DVec3 TargetScene(Target target)
        {
            if (target.IsBody) return bodyScene[target.Body];
            if (target.IsShip) return shipScene[target.Ship.Id];
            return DVec3.Zero;
        }

        public double TargetRadius(Target target)
        {
            if (target.IsBody) return bodyRadius[target.Body];
            if (target.IsShip) return shipLength[target.Ship.Id] * 0.5;
            return 1;
        }

        public double NearestSurface(DVec3 p)
        {
            double best = double.MaxValue;
            for (int i = 0; i < bodyScene.Length; i++)
                best = Math.Min(best, (p - bodyScene[i]).Length - bodyRadius[i]);
            for (int i = 0; i < shipScene.Length; i++)
                best = Math.Min(best, (p - shipScene[i]).Length - shipLength[i] * 0.5);
            return Math.Max(best, 1e-7);
        }

        // ---------------------------------------------------------------- views

        void UpdateClipPlanes()
        {
            double nearest = NearestSurface(SpaceCamera.ScenePosition);
            UnityCamera.nearClipPlane = (float)Math.Max(1e-6, Math.Min(nearest * 0.25, 20.0));
            UnityCamera.farClipPlane = 5e8f;
            UnityCamera.transform.SetPositionAndRotation(Vector3.zero, SpaceCamera.Rotation);
        }

        public Vector3 Relative(DVec3 scene)
        {
            var d = scene - SpaceCamera.ScenePosition;
            return new Vector3((float)d.X, (float)d.Y, (float)d.Z);
        }

        void UpdateViews(float dt)
        {
            Shader.SetGlobalVector("_SunPositionWS", Relative(bodyScene[0]));
            var forward = SpaceCamera.Rotation * Vector3.forward;
            double pixelsPerUnit = PixelsPerUnitAtUnitDistance();
            double warp = Clock.Paused ? 0 : Clock.Warp.SimSecondsPerSecond;

            for (int i = 0; i < Catalog.Count; i++)
            {
                // Body-fixed frame: IAU prime meridians, or 0° toward the parent for synchronous moons.
                var rel = Ephemeris.Relative(i);
                var orbitNormal = DVec3.Cross(rel, Ephemeris.RelativeVelocityKmS(i));
                BodyOrientation.Frame(Catalog[i], Clock.Jd, -rel, orbitNormal, out var north, out var meridian);
                var target = Target.OfBody(i);
                bodyViews[i].Apply(Relative(bodyScene[i]), (float)bodyRadius[i], north, meridian,
                    target == Selected || target == Hovered, forward);
                bodyViews[i].UpdateWeather(Clock.Jd, warp);
            }
            AsteroidBelt?.Render(Clock.Jd, Scale, Relative(bodyScene[0]));

            for (int i = 0; i < shipViews.Length; i++)
            {
                var rel = Relative(shipScene[i]);
                double px = shipLength[i] / Math.Max(rel.magnitude, 1e-9) * pixelsPerUnit;
                // Too small to read: hide the model, the HUD draws a billboard icon instead.
                bool visible = px >= 5;
                shipViews[i].SetVisible(visible);
                if (!visible) continue;
                var target = Target.OfShip(Fleet.Ships[i]);
                shipViews[i].Apply(rel, shipRotation[i], (float)shipLength[i], shipStates[i].Burning,
                    target == Selected || target == Hovered);
            }

            UpdateOrbitLines(dt);
            UpdatePathLine();
        }

        void UpdateOrbitLines(float dt)
        {
            orbitRefreshTimer -= dt;
            bool refresh = orbitRefreshTimer <= 0 || ScaleAnimating;
            if (refresh) orbitRefreshTimer = 0.2f;

            for (int i = 0; i < Catalog.Count; i++)
            {
                var line = orbitLines[i];
                if (line == null) continue;
                var body = Catalog[i];
                var parent = Catalog[body.ParentIndex];
                line.Visible = ShowOrbits;
                if (!ShowOrbits) continue;

                double orbitScene = Scale.LocalDistance(parent, body.Elements.AKm);
                double cameraDistance = (SpaceCamera.ScenePosition - bodyScene[parent.Index]).Length;
                float alpha;
                Color color;
                bool selected = Selected == Target.OfBody(i);
                if (body.Kind == BodyKind.Moon)
                {
                    alpha = Mathf.Clamp01(1.6f - (float)(cameraDistance / (orbitScene * 14.0)));
                    color = new Color(0.55f, 0.65f, 0.78f, 0.45f);
                }
                else
                {
                    alpha = 1f;
                    color = body.Kind == BodyKind.DwarfPlanet ? new Color(0.7f, 0.62f, 0.5f, 0.32f) : new Color(0.45f, 0.62f, 0.9f, 0.42f);
                }
                if (selected) color = new Color(0.55f, 0.9f, 1f, 0.85f);
                line.Visible = alpha > 0.01f;
                if (!line.Visible) continue;

                if (refresh || line.Capacity == 0)
                {
                    Ephemeris.SampleOrbitTrail(i, Clock.Jd, orbitSamples);
                    for (int k = 0; k < orbitSamples.Length; k++)
                    {
                        var o = Scale.LocalOffset(parent, orbitSamples[k]);
                        orbitPoints[k] = new Vector3((float)o.X, (float)o.Y, (float)o.Z);
                    }
                    line.SetPoints(orbitPoints, orbitPoints.Length, color, 1f, 0.12f);
                }
                line.SetTint(new Color(1, 1, 1, alpha));
                line.Root.transform.localPosition = Relative(bodyScene[parent.Index]);
                line.Root.transform.localRotation = Quaternion.identity;
            }
        }

        void UpdatePathLine()
        {
            var t = Selected.IsShip ? Selected : Hovered.IsShip ? Hovered : Target.None;
            var ship = t.IsShip ? t.Ship.Controller : null;
            if (ship == null || ship.Phase != ShipPhase.Transit)
            {
                pathLine.Visible = false;
                return;
            }
            double progress = shipStates[ship.Id].Progress;
            for (int k = 0; k < pathPoints.Length; k++)
            {
                double u = progress + (1 - progress) * k / (pathPoints.Length - 1);
                pathPoints[k] = Relative(TransitScene(ship, u, out _));
            }
            var c = ship.Faction.Color;
            pathLine.SetPoints(pathPoints, pathPoints.Length, new Color(c.R, c.G, c.B, 0.9f), 1f, 0.35f);
            pathLine.SetTint(Color.white);
            pathLine.Root.transform.localPosition = Vector3.zero;
            pathLine.Visible = true;
        }

        public double PixelsPerUnitAtUnitDistance() =>
            Screen.height / (2.0 * Math.Tan(UnityCamera.fieldOfView * 0.5 * Mathf.Deg2Rad));

        // ---------------------------------------------------------------- picking

        /// <summary>Projects a scene point to GUI coordinates (origin top-left). False if behind the camera.</summary>
        public bool ToGui(DVec3 scene, out Vector2 gui, out float depth)
        {
            var rel = Relative(scene);
            var sp = UnityCamera.WorldToScreenPoint(rel);
            depth = sp.z;
            gui = new Vector2(sp.x, Screen.height - sp.y);
            return sp.z > 0;
        }

        /// <summary>True if a sphere of radius <paramref name="ignoreRadius"/> at scene point p is hidden behind any body.</summary>
        public bool Occluded(DVec3 p, int ignoreBody = -1)
        {
            var dir = p - SpaceCamera.ScenePosition;
            double len = dir.Length;
            if (len <= 0) return false;
            dir /= len;
            for (int i = 0; i < bodyScene.Length; i++)
            {
                if (i == ignoreBody) continue;
                var oc = SpaceCamera.ScenePosition - bodyScene[i];
                double b = DVec3.Dot(oc, dir);
                double c = oc.LengthSquared - bodyRadius[i] * bodyRadius[i];
                if (c < 0) continue;
                double disc = b * b - c;
                if (disc <= 0) continue;
                double hit = -b - Math.Sqrt(disc);
                if (hit > 0 && hit < len - bodyRadius[i] * 1e-3) return true;
            }
            return false;
        }

        void UpdateHover()
        {
            if (Input.LookHeld || IsPointerOverUi())
            {
                Hovered = Target.None;
                return;
            }
            var mouse = new Vector2(Input.MousePosition.x, Screen.height - Input.MousePosition.y);
            Hovered = Pick(mouse);
        }

        void HandleSelectionClicks()
        {
            if (!Input.SelectClicked) return;
            Select(Hovered);
            if (Input.SelectDoubleClicked && !Hovered.IsNone) SpaceCamera.FocusOn(Hovered);
        }

        public Target Pick(Vector2 gui)
        {
            double ppu = PixelsPerUnitAtUnitDistance();
            var best = Target.None;
            double bestScore = double.MaxValue;

            for (int i = 0; i < Catalog.Count; i++)
            {
                if (!ToGui(bodyScene[i], out var p, out var depth)) continue;
                double pxRadius = bodyRadius[i] / depth * ppu;
                double pickRadius = Math.Max(pxRadius, Icons.PixelSize(Catalog[i]) * 0.5 + 5);
                double d = (p - gui).magnitude;
                if (d > pickRadius) continue;
                if (pxRadius < 3 && Occluded(bodyScene[i], i)) continue;
                double score = d / pickRadius + 0.5;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = Target.OfBody(i);
                }
            }

            var ships = Fleet.Ships;
            for (int i = 0; i < ships.Count; i++)
            {
                if (!ToGui(shipScene[i], out var p, out var depth)) continue;
                double px = shipLength[i] / depth * ppu;
                double pickRadius = Math.Max(px * 0.5, Icons.PixelSize(ships[i].Spec.Class) * 0.5 + 4);
                double d = (p - gui).magnitude;
                if (d > pickRadius) continue;
                if (Occluded(shipScene[i])) continue;
                // Ships win ties against the planet they orbit.
                double score = d / pickRadius;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = Target.OfShip(ships[i]);
                }
            }
            return best;
        }
    }
}
