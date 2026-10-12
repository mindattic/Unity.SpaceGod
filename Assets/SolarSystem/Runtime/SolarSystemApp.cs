using System;
using System.Collections.Generic;
using SolarSystem.Cameras;
using SolarSystem.Core;
using SolarSystem.Input;
using SolarSystem.Rendering;
using SolarSystem.UI;
using SolarSystem.Views;
using UnityEngine;

namespace SolarSystem
{
    /// <summary>
    /// Owns the simulation and drives every frame:
    /// input → clock → every star system (ephemeris, fleet, markets, wars) → scale layout of the
    /// active system → ships → camera → camera-relative views.
    ///
    /// Every star system in the <see cref="Galaxy"/> is simulated on the shared clock all the time;
    /// only the active one is drawn. Switching system tears its views down and builds the other's
    /// (the camera flies between them through the interstellar view, see <see cref="StarTravel"/>).
    ///
    /// Floating origin: the Unity camera never moves from the world origin. All positions are kept
    /// in double-precision scene units and every renderer is placed at (scene - camera) each frame,
    /// so a 3 km ship next to the camera renders cleanly while Neptune sits 4.5 million units away.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class SolarSystemApp : MonoBehaviour, ICameraWorld
    {
        public Galaxy Galaxy { get; private set; }

        /// <summary>The star system being looked at; the properties below are its.</summary>
        public StarSystem ActiveSystem { get; private set; }

        public BodyCatalog Catalog => ActiveSystem.Catalog;
        public Ephemeris Ephemeris => ActiveSystem.Ephemeris;
        public World World => ActiveSystem.World;
        public SimClock Clock { get; private set; }
        public ScaleModel Scale { get; private set; }
        public Fleet Fleet => World.Fleet;
        public Economy Economy => World.Economy;
        public SpaceCamera SpaceCamera { get; private set; }
        public Camera UnityCamera { get; private set; }
        public StarTravel Travel { get; private set; }
        public InterstellarView Interstellar { get; private set; }

        /// <summary>Yards, factories, habitats, spacecraft and docking rings of the active system.</summary>
        public StructureViews Structures { get; private set; }

        /// <summary>The real main belt and Jupiter Trojans (JPL SBDB), drawn on the GPU (Sol only).</summary>
        public AsteroidBelt AsteroidBelt { get; private set; }
        public InputState Input { get; } = new InputState();

        public Target Selected { get; private set; } = Target.None;
        public Target Hovered { get; private set; } = Target.None;

        public bool ShowOrbits { get; set; } = true;
        public bool ShowLabels { get; set; } = true;
        public bool ShowHelp { get; set; } = true;
        public bool ShowHud { get; set; } = true;
        public bool ShowMarket { get; set; }
        public bool ShowLanes { get; set; } = true;

        /// <summary>Satellite constellations (thousands of points round Earth); off by default, K toggles.</summary>
        public bool ShowSatellites { get; set; }

        /// <summary>False while the camera is between the stars (the system's views are hidden).</summary>
        public bool SystemVisible { get; private set; } = true;

        public bool IsSol => ActiveSystem.Catalog.Root.Name == "Sun";

        /// <summary>Every body's scene position this frame.</summary>
        public DVec3[] BodyScenes => bodyScene;

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

        // Moving into and out of battle space is eased over a couple of seconds, from wherever the
        // ship was drawn (kept relative to its body, which keeps moving).
        DVec3[] shipBlendFrom;
        float[] shipBlendT;
        Battle[] shipLastBattle;

        // Wrecks burn out where the ship died, in its battle's frame.
        float[] wreckStart;
        DVec3[] wreckLocal;
        BattleFx battleFx;
        FighterViews fighterViews;
        LaneView laneView;
        EmbeddedPods embeddedPods;
        SatelliteSwarm swarm;
        Faction[] viewFaction = new Faction[0];
        readonly Dictionary<ShipCombat, GameObject> platformViews = new Dictionary<ShipCombat, GameObject>();
        readonly List<(Transform t, Material m)> entryGlows = new List<(Transform, Material)>();
        Battle battleTimeFor;

        /// <summary>Scene units per km inside a battle: the ship scale, so a fight reads at ship size in both views.</summary>
        public double BattleKmScale => Scale.ShipLength(1.0);

        /// <summary>Scene position of a point in a battle (km from its anchor).</summary>
        public DVec3 BattleScene(Battle b, DVec3 localKm) => BattleAnchorScene(b) + localKm.SwapYZ * BattleKmScale;

        /// <summary>Where a battle is drawn: in orbit of its body, or beside the drawn hyperlane for an ambush.</summary>
        DVec3 BattleAnchorScene(Battle b)
        {
            var site = b.Site;
            if (site == null) return bodyScene[b.Body] + Scale.LocalOffset(Catalog[b.Body], b.Anchor);
            var hop = site.Hop;
            var aKm = Ephemeris.Helio(hop.From) + site.FromOffset;
            var cKm = Ephemeris.Helio(hop.To) + site.ToOffset;
            var onLane = Scale.LaneCurveScene(Ephemeris, bodyScene, Fleet.Lanes, hop.Lane, hop.From, aKm, hop.To, cKm, site.Progress, out _);
            return onLane + SideScene(aKm, site.Side);
        }

        /// <summary>A sideways offset (true km) beside a lane, in scene units at that distance from the system's root.</summary>
        DVec3 SideScene(DVec3 nearHelio, DVec3 sideKm) =>
            sideKm.SwapYZ * Scale.LocalDistanceDerivative(Catalog.Root, Math.Max(nearHelio.Length, 1e6));

        /// <summary>A ship lurking beside a lane, or drifting after being pulled out of one.</summary>
        DVec3 LaneHoldScene(Ship ship, out DVec3 along)
        {
            var on = Scale.LaneScene(Ephemeris, bodyScene, Fleet.Lanes, ship, ship.LaneHold, out along);
            double ease = Astro.SmoothStep(0, 0.08, Clock.Jd - ship.PhaseStartJd);
            return on + SideScene(Fleet.LanePoint(ship, Clock.Jd, ship.LaneHold), ship.LaneHoldSide) * ease;
        }

        /// <summary>The model a battle unit is drawn with (a ship's view, or a platform), for parenting things to its hull.</summary>
        public Transform UnitTransform(BattleUnit u)
        {
            if (u.Ship != null) return u.Ship.Id < shipViews.Length ? shipViews[u.Ship.Id].Root.transform : null;
            if (u.Kind == UnitKind.Platform && platformViews.TryGetValue(u.Combat, out var go)) return go.transform;
            return null;
        }
        LineStrip pathLine;
        Vector3[] pathPoints = new Vector3[64];

        Transform worldRoot;
        Transform systemRoot;
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

            Clock = SimClock.StartingNow();
            Scale = new ScaleModel { Blend = 1 };
            Galaxy = Galaxy.CreateDefault(Clock.Jd);

            SetupCamera();
            worldRoot = new GameObject("World").transform;
            Starfield.Create(worldRoot);
            orbitSamples = new DVec3[257];
            orbitPoints = new Vector3[orbitSamples.Length];

            hud = new Hud(this);
            SpaceCamera = new SpaceCamera(this);
            Travel = new StarTravel(this);
            Interstellar = new InterstellarView(Galaxy, worldRoot);
            SubscribeWorlds();

            Activate(Galaxy.Systems[0]);
            AsteroidBelt = AsteroidBelt.Create(this);

            var earth = Catalog["Earth"].Index;
            var start = bodyScene[earth] + new DVec3(-0.6, 0.35, -1).Normalized * (Scale.DisplayRadius(Catalog[earth]) * 9);
            SpaceCamera.PlaceFree(start, 0, 0);
            var look = (bodyScene[earth] - start).Normalized;
            SpaceCamera.PlaceFree(start, (float)(Math.Atan2(look.X, look.Z) * Mathf.Rad2Deg), (float)(-Math.Asin(look.Y) * Mathf.Rad2Deg));
            Select(Target.OfBody(earth));
        }

        /// <summary>Battle toasts from whichever system is being watched.</summary>
        void SubscribeWorlds()
        {
            foreach (var system in Galaxy.Systems)
            {
                var s = system;
                s.World.War.BattleStarted += b =>
                {
                    if (s == ActiveSystem) ShowToast(b.Name.ToUpperInvariant() + " has begun");
                };
            }
        }

        /// <summary>
        /// Makes <paramref name="system"/> the one drawn: its bodies, orbits, ships, lanes, battles,
        /// structures and rings get views (the previous system's are torn down). The camera keeps its
        /// scene position; callers place it.
        /// </summary>
        public void Activate(StarSystem system)
        {
            if (systemRoot != null) Destroy(systemRoot.gameObject);
            ActiveSystem = system;
            systemRoot = new GameObject(system.Name).transform;
            systemRoot.SetParent(worldRoot, false);
            SystemVisible = true;

            int n = Catalog.Count;
            bodyScene = new DVec3[n];
            bodyRadius = new double[n];
            bodyViews = new BodyView[n];
            orbitLines = new LineStrip[n];
            for (int i = 0; i < n; i++)
            {
                bodyViews[i] = new BodyView(Catalog[i], systemRoot);
                if (Catalog[i].ParentIndex >= 0) orbitLines[i] = new LineStrip("Orbit " + Catalog[i].Name, systemRoot);
            }
            shipViews = null;
            BuildShipViews();
            pathLine = new LineStrip("Flight path", systemRoot) { Visible = false };
            battleFx = new BattleFx(this, systemRoot);
            fighterViews = new FighterViews(this, systemRoot);
            laneView = new LaneView(this, systemRoot);
            embeddedPods = new EmbeddedPods(this);
            platformViews.Clear();
            entryGlows.Clear();
            Structures = new StructureViews(this, systemRoot);
            swarm = new SatelliteSwarm(this, systemRoot);
            if (AsteroidBelt != null) AsteroidBelt.enabled = IsSol;

            viewFaction = new Faction[0];
            lastBattleScene = new DVec3[0];
            battleTimeFor = null;
            Selected = Target.None;
            Hovered = Target.None;
            Scale.Layout(Ephemeris, bodyScene);
            UpdateRadii();
            Structures.Layout();
            // A new system starts level with the ecliptic again (R levels to its own plane).
            SpaceCamera.SetUpFrame(Quaternion.identity);
            SpaceCamera.SetPose(SpaceCamera.ScenePosition, SpaceCamera.Rotation);
        }

        /// <summary>Hides the active system's views (between the stars) or shows them again.</summary>
        public void SetSystemVisible(bool visible)
        {
            SystemVisible = visible;
            if (systemRoot != null) systemRoot.gameObject.SetActive(visible);
        }

        /// <summary>Flies the camera to another star system.</summary>
        public void TravelTo(StarSystem system)
        {
            if (system == null || system == ActiveSystem || Travel.Active) return;
            Select(Target.None);
            Travel.Begin(system);
        }

        void UpdateRadii()
        {
            // The barycentre is drawn as nothing but still needs a size to frame the system around it.
            for (int i = 0; i < Catalog.Count; i++) bodyRadius[i] = Catalog[i].IsVisible ? Scale.DisplayRadius(Catalog[i]) : 1.0;
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

        /// <summary>Adds views (and per-ship arrays) for hulls a yard has launched since the last frame.</summary>
        void SyncShipViews()
        {
            var ships = Fleet.Ships;
            int have = shipViews.Length;
            if (ships.Count == have) return;
            Array.Resize(ref shipViews, ships.Count);
            Array.Resize(ref shipScene, ships.Count);
            Array.Resize(ref shipRotation, ships.Count);
            Array.Resize(ref shipLength, ships.Count);
            Array.Resize(ref shipStates, ships.Count);
            Array.Resize(ref shipRotationInitialised, ships.Count);
            Array.Resize(ref shipBlendFrom, ships.Count);
            Array.Resize(ref shipBlendT, ships.Count);
            Array.Resize(ref shipLastBattle, ships.Count);
            Array.Resize(ref wreckStart, ships.Count);
            Array.Resize(ref wreckLocal, ships.Count);
            for (int i = have; i < ships.Count; i++)
            {
                shipViews[i] = new ShipView(ships[i], systemRoot);
                shipBlendT[i] = 1;
                wreckStart[i] = -1;
            }
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
            shipBlendFrom = new DVec3[ships.Count];
            shipBlendT = new float[ships.Count];
            shipLastBattle = new Battle[ships.Count];
            wreckStart = new float[ships.Count];
            wreckLocal = new DVec3[ships.Count];
            for (int i = 0; i < ships.Count; i++)
            {
                shipViews[i] = new ShipView(ships[i], systemRoot);
                shipBlendT[i] = 1;
                wreckStart[i] = ships[i].Destroyed ? float.MaxValue : -1;
            }
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            bool overUi = IsPointerOverUi();
            Input.Poll(overUi);
            HandleCommands();

            Clock.Advance(dt);
            Galaxy.Update(Clock.Jd);
            SyncShipViews();

            AnimateScale(dt);
            Scale.Layout(Ephemeris, bodyScene);
            UpdateRadii();
            UpdateShips(dt);
            Structures.Layout();

            DropLostTargets();
            BattleTime();
            if (Travel.Active) Travel.Update(dt);
            else SpaceCamera.Update(dt, Input);
            UpdateClipPlanes();
            UpdateViews(dt);
            UpdateHover();
            HandleSelectionClicks();

            ToastAge += dt;
            Cursor.lockState = Input.LookHeld ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !Input.LookHeld;
        }

        /// <summary>A ship destroyed while selected or followed: let go of it.</summary>
        void DropLostTargets()
        {
            if (Selected.IsShip && Selected.Ship.Destroyed) Select(Target.None);
            if (Hovered.IsShip && Hovered.Ship.Destroyed) Hovered = Target.None;
            if (SpaceCamera.FocusTarget.IsShip && SpaceCamera.FocusTarget.Ship.Destroyed) SpaceCamera.ExitFocus();
        }

        /// <summary>Following a ship into battle slows the clock to battle time once, so the fight can be watched.</summary>
        void BattleTime()
        {
            var t = SpaceCamera.FocusTarget;
            var battle = t.IsShip ? t.Ship.Battle : null;
            if (battle == null || battle == battleTimeFor) return;
            EnterBattleTime(battle);
        }

        /// <summary>Real time, so a capital ship engagement plays out at the pace it is fought.</summary>
        public void EnterBattleTime(Battle battle)
        {
            battleTimeFor = battle;
            if (Clock.WarpIndex > 0)
            {
                Clock.SetWarp(0);
                ShowToast("BATTLE TIME: " + battle.Name + ", " + Clock.Warp.Label + "  (. to speed up)");
            }
        }

        /// <summary>The largest battle under way, or null.</summary>
        public Battle BiggestBattle()
        {
            Battle best = null;
            int bestCount = 0;
            foreach (var b in World.War.Battles)
            {
                int n = 0;
                foreach (var u in b.Units)
                    if (u.Present) n++;
                if (n > bestCount)
                {
                    bestCount = n;
                    best = b;
                }
            }
            return best;
        }

        /// <summary>Flies the camera to a battle's flagship and drops to battle time.</summary>
        public void WatchBattle(Battle battle)
        {
            Ship flag = null;
            foreach (var u in battle.Units)
                if (u.Ship != null && u.Present && (flag == null || u.Combat.Spec.Hull > flag.Combat.Spec.Hull)) flag = u.Ship;
            if (flag == null) return;
            var t = Target.OfShip(flag);
            Select(t);
            SpaceCamera.FocusOn(t, 30, 25f);
            EnterBattleTime(battle);
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
            if (Travel.Active)
            {
                // Between the stars only time and the escape hatch work.
                if (i.Escape) Travel.Finish();
                if (i.TimeFaster) Clock.Faster();
                if (i.TimeSlower) Clock.Slower();
                if (i.TogglePause) TogglePause();
                return;
            }
            if (i.ToggleScale) ToggleScale();
            if (i.TimeFaster) Clock.Faster();
            if (i.TimeSlower) Clock.Slower();
            if (i.TogglePause) TogglePause();
            if (i.ResetTime) ResetToNow();
            if (i.ToggleOrbits) ShowOrbits = !ShowOrbits;
            if (i.ToggleLabels) ShowLabels = !ShowLabels;
            if (i.ToggleHelp) ShowHelp = !ShowHelp;
            if (i.ToggleHud) ShowHud = !ShowHud;
            if (i.ToggleMarket) ShowMarket = !ShowMarket;
            if (i.ToggleLanes) ShowLanes = !ShowLanes;
            if (i.LevelView) LevelView();
            if (i.ToggleSatellites)
            {
                ShowSatellites = !ShowSatellites;
                ShowToast(ShowSatellites ? "SATELLITES ON: constellations drawn" : "SATELLITES OFF  (K to show)");
            }
            if (i.WatchBattle)
            {
                var b = BiggestBattle();
                if (b != null) WatchBattle(b);
                else ShowToast("No battles under way");
            }
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

        static readonly string[] SolNumberBodies = { "Sun", "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune", "Pluto" };

        static readonly string[] CentauriNumberBodies =
        {
            "Alpha Centauri A", "Alpha Centauri B", "Proxima Centauri", "Proxima b", "Proxima d", "Proxima c",
            "Alpha Centauri Ab", "Rigil Gate", "Toliman Forge", "Blackhollow",
        };

        void NumberHotkey(int key)
        {
            var names = IsSol ? SolNumberBodies : CentauriNumberBodies;
            if (key >= names.Length || !Catalog.TryGet(names[key], out var body)) return;
            var target = Target.OfBody(body.Index);
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
                if (!s.IsEscort && !s.Destroyed && s.Phase != ShipPhase.Landed) candidates.Add(s);
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

        /// <summary>Camera elevation of the system overview, degrees.</summary>
        public const float OverviewPitch = 52f;

        /// <summary>How much of the system the overview frames: the inner planets in Sol, the A-B pair in Alpha Centauri.</summary>
        double OverviewSpanKm => Catalog.Root.Kind == BodyKind.Barycentre ? 22 * Astro.AuKm : 2.2 * Astro.AuKm;

        /// <summary>The overview's camera distance from the system's root, scene units.</summary>
        public double OverviewDistance() => Scale.LocalDistance(Catalog.Root, OverviewSpanKm) * 1.9;

        public void FocusSystemOverview()
        {
            var root = Target.OfBody(0);
            Select(Catalog.Root.IsVisible ? root : Target.None);
            SpaceCamera.FocusOn(root, OverviewDistance() / Math.Max(bodyRadius[0], 1e-9), OverviewPitch);
        }

        /// <summary>
        /// The plane a system is laid out in, as its north-side normal (Unity axes): the ecliptic for
        /// a single star, the orbit of the first star round the barycentre for a binary (Alpha
        /// Centauri's A-B plane, steeply tilted to the ecliptic).
        /// </summary>
        public Vector3 SystemPlaneNormal()
        {
            if (Catalog.Root.IsStar || Catalog.Count < 2) return Vector3.up;
            var n = DVec3.Cross(Ephemeris.Relative(1), Ephemeris.RelativeVelocityKmS(1)).Normalized.SwapYZ;
            return n.LengthSquared > 0 ? new Vector3((float)n.X, (float)n.Y, (float)n.Z) : Vector3.up;
        }

        public string SystemPlaneName => Catalog.Root.IsStar ? "the ecliptic" : "the A–B orbital plane";

        /// <summary>Swings the view smoothly round until the system's plane is level.</summary>
        public void LevelView()
        {
            if (Travel.Active) return;
            SpaceCamera.Level(SystemPlaneNormal());
            ShowToast("LEVEL: " + SystemPlaneName.ToUpperInvariant() + " is now horizontal");
        }

        /// <summary>Space: the whole simulation (every system) eases to a stop, or back up to speed.</summary>
        public void TogglePause()
        {
            Clock.ToggleEasedPause();
            ShowToast(Clock.PauseHeld ? "PAUSING: every system easing to a stop  (Space to resume)" : "RESUMING: time picking back up to " + Clock.Warp.Label);
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
                // Schedules only run forward; rebuild every system at the new date.
                int index = Galaxy.Systems.IndexOf(ActiveSystem);
                var cameraAt = SpaceCamera.ScenePosition;
                var cameraRotation = SpaceCamera.Rotation;
                Galaxy = Galaxy.CreateDefault(now);
                Destroy(Interstellar.Root);
                Interstellar = new InterstellarView(Galaxy, worldRoot);
                SubscribeWorlds();
                Activate(Galaxy.Systems[Math.Max(0, index)]);
                SpaceCamera.SetPose(cameraAt, cameraRotation);
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
                        // Human pilots: each escort wanders slowly about its slot instead of sitting welded to it.
                        float t = Time.unscaledTime * 0.25f + ship.Id * 1.7f;
                        var slot = ship.FormationSlot + new DVec3(Mathf.Sin(t * 1.3f) * 0.12f, Mathf.Sin(t * 0.9f + 1.1f) * 0.08f, Mathf.Sin(t * 0.7f + 2.3f) * 0.15f);
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
                    else if (ship.Phase == ShipPhase.Queued || ship.Planetside)
                    {
                        // At a docking ring: in the ringed world's own (turning) neighbourhood.
                        int body = ship.Ring.Body;
                        shipScene[i] = bodyScene[body] + Scale.LocalOffset(Catalog[body], st.HelioKm - Ephemeris.Helio(body));
                        facing = st.Facing.SwapYZ;
                    }
                    else if (ship.Phase == ShipPhase.Lane)
                    {
                        shipScene[i] = Scale.LaneScene(Ephemeris, bodyScene, Fleet.Lanes, ship, st.Progress, out var travel);
                        facing = travel;
                    }
                    else if (ship.Phase == ShipPhase.Lurk || ship.Phase == ShipPhase.Adrift)
                    {
                        shipScene[i] = LaneHoldScene(ship, out var along);
                        facing = along;
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
            PlaceInBattles(dt);
        }

        /// <summary>
        /// Ships fighting are drawn where the battle has them; ships entering or leaving a fight ease
        /// between that and their orbit over two seconds.
        /// </summary>
        void PlaceInBattles(float dt)
        {
            var ships = Fleet.Ships;
            if (lastBattleScene.Length != ships.Count) Array.Resize(ref lastBattleScene, ships.Count);
            for (int i = 0; i < ships.Count; i++)
            {
                var ship = ships[i];
                if (ship.Destroyed) continue;
                var battle = ship.Battle;
                int body = battle != null ? battle.Body : ship.Controller.Orbit.Body;
                if (battle != shipLastBattle[i])
                {
                    var current = shipBlendT[i] < 1 || shipLastBattle[i] != null ? DrawnScene(i) : shipScene[i];
                    shipBlendFrom[i] = current - bodyScene[body];
                    shipBlendT[i] = 0;
                    shipLastBattle[i] = battle;
                }
                if (battle != null)
                {
                    var u = battle.UnitOf(ship);
                    if (u != null)
                    {
                        shipScene[i] = BattleScene(battle, u.Pos);
                        var f = u.Forward.SwapYZ;
                        var fv = new Vector3((float)f.X, (float)f.Y, (float)f.Z);
                        if (fv.sqrMagnitude > 1e-12f) shipRotation[i] = Quaternion.LookRotation(fv, Mathf.Abs(fv.normalized.y) > 0.98f ? Vector3.right : Vector3.up);
                        lastBattleScene[i] = shipScene[i];
                    }
                }
                if (shipBlendT[i] < 1)
                {
                    shipBlendT[i] = Mathf.Min(1, shipBlendT[i] + dt / 2f);
                    float e = shipBlendT[i] * shipBlendT[i] * (3 - 2 * shipBlendT[i]);
                    shipScene[i] = DVec3.Lerp(bodyScene[body] + shipBlendFrom[i], shipScene[i], e);
                }
            }
        }

        DVec3[] lastBattleScene = new DVec3[0];

        DVec3 DrawnScene(int i) => lastBattleScene.Length > i && shipLastBattle[i] != null ? lastBattleScene[i] : shipScene[i];

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
            if (target.IsStructure && Structures.Scenes.TryGetValue(target.Structure, out var s)) return s;
            if (target.IsRing && Structures.RingScenes.TryGetValue(target.Ring, out var r)) return r;
            return DVec3.Zero;
        }

        public double TargetRadius(Target target)
        {
            if (target.IsBody) return bodyRadius[target.Body];
            if (target.IsShip) return shipLength[target.Ship.Id] * 0.5;
            if (target.IsStructure && Structures.Sizes.TryGetValue(target.Structure, out var s)) return s * 0.5;
            if (target.IsRing && Structures.Sizes.TryGetValue(target.Ring, out var r)) return r * 0.5;
            return 1;
        }

        public double NearestSurface(DVec3 p)
        {
            double best = double.MaxValue;
            for (int i = 0; i < bodyScene.Length; i++)
                best = Math.Min(best, (p - bodyScene[i]).Length - bodyRadius[i]);
            for (int i = 0; i < shipScene.Length; i++)
                best = Math.Min(best, (p - shipScene[i]).Length - shipLength[i] * 0.5);
            foreach (var kv in Structures.Scenes)
                best = Math.Min(best, (p - kv.Value).Length - Structures.Sizes[kv.Key] * 0.5);
            foreach (var kv in Structures.RingScenes)
                best = Math.Min(best, (p - kv.Value).Length - Structures.Sizes[kv.Key] * 0.5);
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

        /// <summary>
        /// Lights the scene with the two stars that shine brightest where the camera is (luminosity
        /// over distance squared): the Sun alone in Sol; A with B beside it in Alpha Centauri, or
        /// Proxima out by Proxima. Light colours follow each star's temperature, the Sun's being white.
        /// </summary>
        void UpdateLights()
        {
            int first = -1, second = -1;
            double best = 0, next = 0;
            for (int i = 0; i < Catalog.Count; i++)
            {
                var b = Catalog[i];
                if (!b.IsStar) continue;
                double d = Math.Max((SpaceCamera.ScenePosition - bodyScene[i]).Length, bodyRadius[i]);
                double flux = b.LuminositySolar / (d * d);
                if (flux > best)
                {
                    second = first;
                    next = best;
                    first = i;
                    best = flux;
                }
                else if (flux > next)
                {
                    second = i;
                    next = flux;
                }
            }
            if (first < 0) first = 0;
            Shader.SetGlobalVector("_SunPositionWS", Relative(bodyScene[first]));
            Shader.SetGlobalVector("_SunColor", LightColor(Catalog[first], 1));
            if (second >= 0)
            {
                // The square root keeps a companion's light a visible fill; the true ratio would hide it.
                float strength = Mathf.Clamp01((float)Math.Sqrt(next / best)) * 0.9f;
                Shader.SetGlobalVector("_Sun2PositionWS", Relative(bodyScene[second]));
                Shader.SetGlobalVector("_Sun2Color", LightColor(Catalog[second], strength));
            }
            else
            {
                Shader.SetGlobalVector("_Sun2PositionWS", Relative(bodyScene[first]));
                Shader.SetGlobalVector("_Sun2Color", Vector4.zero);
            }
        }

        static readonly Rgb SunLight = AlphaCentauri.StarColor(5772);

        /// <summary>A star's light relative to sunlight (so the Sun lights white), scaled to its brightest channel.</summary>
        public static Vector4 LightColor(BodyDefinition star, float strength)
        {
            var c = AlphaCentauri.StarColor(star.TeffK);
            var v = new Vector3(c.R / Mathf.Max(SunLight.R, 1e-3f), c.G / Mathf.Max(SunLight.G, 1e-3f), c.B / Mathf.Max(SunLight.B, 1e-3f));
            float max = Mathf.Max(v.x, Mathf.Max(v.y, v.z));
            v /= Mathf.Max(max, 1e-3f);
            return new Vector4(v.x * strength, v.y * strength, v.z * strength, strength);
        }

        void UpdateViews(float dt)
        {
            if (!SystemVisible) return;
            UpdateLights();
            var forward = SpaceCamera.Rotation * Vector3.forward;
            double pixelsPerUnit = PixelsPerUnitAtUnitDistance();
            double warp = Clock.Paused ? 0 : Clock.Warp.SimSecondsPerSecond * Clock.Speed;

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
            if (IsSol) AsteroidBelt?.Render(Clock.Jd, Scale, Relative(bodyScene[0]));

            int glowIndex = 0;
            for (int i = 0; i < shipViews.Length; i++)
            {
                var ship = Fleet.Ships[i];
                if (ship.Destroyed)
                {
                    DrawWreck(i, ship);
                    continue;
                }
                if (viewFaction.Length != shipViews.Length) Array.Resize(ref viewFaction, shipViews.Length);
                if (viewFaction[i] == null) viewFaction[i] = ship.Faction;
                if (viewFaction[i] != ship.Faction)
                {
                    // Taken as a prize: repaint in the captor's livery.
                    Destroy(shipViews[i].Root);
                    shipViews[i] = new ShipView(ship, systemRoot);
                    viewFaction[i] = ship.Faction;
                }
                var rel = Relative(shipScene[i]);
                var controller = ship.Controller;
                if (controller.Phase == ShipPhase.Landed)
                {
                    shipViews[i].SetVisible(false);
                    continue;
                }
                float heat = shipStates[i].Heat;
                if (heat > 0.01f) PlaceEntryGlow(glowIndex++, i, rel, heat, pixelsPerUnit);
                double px = shipLength[i] / Math.Max(rel.magnitude, 1e-9) * pixelsPerUnit;
                // Too small to read: hide the model, the HUD draws a billboard icon instead.
                bool visible = px >= 5;
                shipViews[i].SetVisible(visible);
                if (!visible) continue;
                if (ship.Derelict)
                {
                    shipViews[i].Derelict(rel, shipRotation[i], (float)shipLength[i], Time.unscaledTime + ship.Id);
                    continue;
                }
                var target = Target.OfShip(ship);
                var unit = ship.Battle?.UnitOf(ship);
                bool thrusting = shipStates[i].Burning || (unit != null && unit.Vel.LengthSquared > 1e-4);
                shipViews[i].Apply(rel, shipRotation[i], (float)shipLength[i], shipStates[i].Burning,
                    target == Selected || target == Hovered);
                shipViews[i].ApplyCombat(ship.Combat, ship.Battle != null, thrusting, Time.unscaledTime, dt);
            }
            for (int g = glowIndex; g < entryGlows.Count; g++) entryGlows[g].t.gameObject.SetActive(false);

            UpdatePlatforms();
            laneView.Update(ShowLanes);
            fighterViews.Update();
            embeddedPods.Update();
            battleFx.Update();
            Structures.Update(true);
            swarm.Update(ShowSatellites);
            UpdateOrbitLines(dt);
            UpdatePathLine();
        }

        static Mesh glowQuad;

        /// <summary>The plasma sheath round a ship coming down through an atmosphere: a hot glow at its nose.</summary>
        void PlaceEntryGlow(int index, int ship, Vector3 rel, float heat, double pixelsPerUnit)
        {
            if (index >= entryGlows.Count)
            {
                glowQuad ??= MeshFactory.Quad();
                var go = new GameObject("Entry glow");
                go.transform.SetParent(systemRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = glowQuad;
                var mr = go.AddComponent<MeshRenderer>();
                var m = Materials.Create(Materials.Glow, "Entry glow");
                m.SetFloat("_Falloff", 2.0f);
                mr.sharedMaterial = m;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                entryGlows.Add((go.transform, m));
            }
            var (t, material) = entryGlows[index];
            t.gameObject.SetActive(true);
            float length = (float)shipLength[ship];
            // Ships come down tail-first: the sheath forms on the face meeting the air, below the drive.
            var nose = shipRotation[ship] * Vector3.forward;
            var p = rel - nose * length * 0.55f;
            float dist = Mathf.Max(p.magnitude, 1e-6f);
            // At least a few pixels, so a descent shows as a spark from orbit.
            float size = Mathf.Max(length * (0.45f + 0.75f * heat), dist * 5f / (float)pixelsPerUnit * heat);
            t.localPosition = p;
            t.localRotation = Quaternion.LookRotation(p / dist, Vector3.up);
            t.localScale = Vector3.one * size;
            material.SetColor("_Color", Color.Lerp(new Color(1f, 0.55f, 0.25f), new Color(1f, 0.4f, 0.75f), heat));
            material.SetFloat("_Intensity", 0.35f + 0.9f * heat);
        }

        /// <summary>A ship lost in battle breaks up and burns out where it died.</summary>
        void DrawWreck(int i, Ship ship)
        {
            var battle = shipLastBattle[i];
            if (wreckStart[i] < 0)
            {
                var unit = battle?.UnitOf(ship);
                if (unit == null)
                {
                    wreckStart[i] = float.MaxValue;
                    shipViews[i].SetVisible(false);
                    return;
                }
                wreckStart[i] = Time.unscaledTime;
                wreckLocal[i] = unit.Pos;
            }
            if (wreckStart[i] == float.MaxValue || battle == null)
            {
                shipViews[i].SetVisible(false);
                return;
            }
            var scene = BattleScene(battle, wreckLocal[i]);
            if (!shipViews[i].Wreck(Relative(scene), shipRotation[i], (float)Scale.ShipLength(ship.Spec.LengthKm), Time.unscaledTime - wreckStart[i], Time.unscaledTime))
                wreckStart[i] = float.MaxValue;
        }

        /// <summary>Defence platforms are drawn while they fight.</summary>
        void UpdatePlatforms()
        {
            var live = new HashSet<ShipCombat>();
            foreach (var b in World.War.Battles)
            {
                foreach (var u in b.Units)
                {
                    if (u.Kind != UnitKind.Platform || !u.Present) continue;
                    live.Add(u.Combat);
                    if (!platformViews.TryGetValue(u.Combat, out var go))
                    {
                        go = new GameObject(u.Name);
                        go.transform.SetParent(systemRoot, false);
                        go.AddComponent<MeshFilter>().sharedMesh = ShipMeshFactory.GetPlatform(u.Faction);
                        var mr = go.AddComponent<MeshRenderer>();
                        mr.sharedMaterial = ShipModels.ProceduralMaterial;
                        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        platformViews[u.Combat] = go;
                    }
                    go.transform.localPosition = Relative(BattleScene(b, u.Pos));
                    go.transform.localRotation = Quaternion.Euler(20, (float)(b.Seconds * 0.5 % 360), 0);
                    go.transform.localScale = Vector3.one * (float)Scale.ShipLength(CombatSpec.PlatformLengthKm);
                }
            }
            var gone = new List<ShipCombat>();
            foreach (var kv in platformViews)
                if (!live.Contains(kv.Key))
                {
                    Destroy(kv.Value);
                    gone.Add(kv.Key);
                }
            foreach (var k in gone) platformViews.Remove(k);
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
                    color = body.Kind == BodyKind.DwarfPlanet ? new Color(0.7f, 0.62f, 0.5f, 0.32f)
                        : body.Kind == BodyKind.Station ? new Color(0.9f, 0.75f, 0.45f, 0.3f)
                        : body.IsStar ? new Color(1f, 0.85f, 0.55f, 0.35f)
                        : body.Status != BodyStatus.Confirmed ? new Color(0.6f, 0.6f, 0.7f, 0.25f)
                        : new Color(0.45f, 0.62f, 0.9f, 0.42f);
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
            if (ship == null || ship.Destroyed || ship.Derelict || ship.Battle != null || (ship.Phase != ShipPhase.Transit && ship.Phase != ShipPhase.Lane))
            {
                pathLine.Visible = false;
                return;
            }
            double progress = shipStates[ship.Id].Progress;
            for (int k = 0; k < pathPoints.Length; k++)
            {
                double u = progress + (1 - progress) * k / (pathPoints.Length - 1);
                pathPoints[k] = Relative(ship.Phase == ShipPhase.Lane ? Scale.LaneScene(Ephemeris, bodyScene, Fleet.Lanes, ship, u, out _) : TransitScene(ship, u, out _));
            }
            var c = ship.Faction.Color;
            pathLine.SetPoints(pathPoints, pathPoints.Length, new Color(c.R, c.G, c.B, 0.9f), 1f, 0.35f);
            pathLine.SetTint(Color.white);
            pathLine.Root.transform.localPosition = Vector3.zero;
            pathLine.Visible = true;
        }

        public double PixelsPerUnitAtUnitDistance() =>
            Screen.height / (2.0 * Math.Tan(UnityCamera.fieldOfView * 0.5 * Mathf.Deg2Rad));

        /// <summary>Where another star system shines in this one's sky (a Unity-axes unit vector).</summary>
        public Vector3 SkyDirection(StarSystem other)
        {
            var d = (other.PositionLy - ActiveSystem.PositionLy).Normalized.SwapYZ;
            return new Vector3((float)d.X, (float)d.Y, (float)d.Z);
        }

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

        /// <summary>Projects a camera-relative point (or direction far away) to GUI coordinates.</summary>
        public bool RelativeToGui(Vector3 rel, out Vector2 gui)
        {
            var sp = UnityCamera.WorldToScreenPoint(rel);
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
                if (i == ignoreBody || !Catalog[i].IsVisible) continue;
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
            if (Input.LookHeld || IsPointerOverUi() || Travel.Active)
            {
                Hovered = Target.None;
                return;
            }
            var mouse = new Vector2(Input.MousePosition.x, Screen.height - Input.MousePosition.y);
            Hovered = Pick(mouse);
        }

        void HandleSelectionClicks()
        {
            if (!Input.SelectClicked || Travel.Active) return;
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
                if (!Catalog[i].IsVisible) continue;
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
                if (ships[i].Destroyed || ships[i].Controller.Phase == ShipPhase.Landed) continue;
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

            // Structures and rings, only once they are drawn large enough to be things.
            void Consider(object key, DVec3 scene, Target t)
            {
                if (!ToGui(scene, out var p, out var depth)) return;
                double px = Structures.Sizes[key] / depth * ppu;
                if (px < 4) return;
                double pickRadius = Math.Max(px * 0.5, 8);
                double d = (p - gui).magnitude;
                if (d > pickRadius || Occluded(scene)) return;
                double score = d / pickRadius + 0.2;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = t;
                }
            }
            foreach (var kv in Structures.Scenes) Consider(kv.Key, kv.Value, Target.OfStructure(kv.Key));
            foreach (var kv in Structures.RingScenes) Consider(kv.Key, kv.Value, Target.OfRing(kv.Key));
            return best;
        }
    }
}
