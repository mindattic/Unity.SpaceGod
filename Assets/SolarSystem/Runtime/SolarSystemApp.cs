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
        /// <summary>Raised when the galaxy is rebuilt from scratch (a backwards time reset): anything holding
        /// references into the old one (a running tour, a capture) must stop rather than run on stale data.</summary>
        public event Action GalaxyRebuilt;

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

        /// <summary>The ship under direct player control, if any (ship-only, or <see cref="Target.None"/>).</summary>
        public Target Piloted { get; private set; } = Target.None;
        public bool IsPiloting => !Piloted.IsNone;

        /// <summary>Where the piloted ship really is (its body frame and true km offset) and how fast it is
        /// flying: its schedule shows only the spot it was pinned at when the helm was taken.</summary>
        public int PilotFrameBody => pilotFrameBody;
        public DVec3 PilotOffsetKm => pilotOffsetKm;
        public double PilotSpeedKmS => pilotVelKmS.Length;

        /// <summary>The piloted hull's velocity in its body's frame (ecliptic axes, km/s), nose and roof:
        /// what the HUD's velocity vector and prograde/retrograde markers are drawn from.</summary>
        public DVec3 PilotVelocityKmS => pilotVelKmS;
        public DVec3 PilotForward => pilot.Forward;
        public DVec3 PilotUp => pilot.Up;

        /// <summary>Whether the flight computer is flying the hull to the stick (on each time the helm is
        /// taken; F toggles it) or the hull is drifting on raw thrust. See <see cref="PilotFlight"/>.</summary>
        public bool PilotFlightAssist => !pilotFlightAssistOff;
        bool pilotFlightAssistOff;

        int pilotFrameBody;

        /// <summary>The piloted hull's whole motion (position, velocity, attitude, spin, cruise ramp),
        /// integrated by <see cref="PilotFlight.Step"/> every frame; the properties below are its parts.</summary>
        PilotState pilot;

        DVec3 pilotOffsetKm { get => pilot.Pos; set => pilot.Pos = value; }
        DVec3 pilotVelKmS { get => pilot.Vel; set => pilot.Vel = value; }
        DVec3 pilotForward => pilot.Forward;

        /// <summary>Where the stick points the nose (ecliptic, unit): mouse movement swings it about the
        /// hull's own roof and starboard axes, so steering stays screen-relative however the hull has
        /// rolled, and it can point anywhere -- straight up, down or behind -- with no gimbal clamp.</summary>
        DVec3 pilotAim = DVec3.UnitX;
        const float PilotLookSensitivity = 0.12f;

        /// <summary>What the pilot camera leans toward tracking, independent of the ship's own facing.</summary>
        Target pilotLockTarget = Target.None;

        /// <summary>Base field of view everywhere outside an active boost (matches <see cref="SetupCamera"/>'s
        /// initial value) and the widened view boost eases toward -- a classic sense-of-speed cue
        /// (Freelancer, Elite, Star Citizen all do this) that costs nothing to add since the ship's
        /// actual boosted velocity is already real.</summary>
        const float BaseFov = 60f, BoostFov = 74f, CruiseFov = 86f, FovResponseHz = 6f;
        float cameraFov = BaseFov;
        bool pilotBoosting;

        /// <summary>The cruise drive's own ramp state (see <see cref="PilotFlight.Step"/>) -- persists
        /// across frames like <see cref="pilotVelKmS"/> does, and only while piloting.</summary>
        PilotCruiseState pilotCruise { get => pilot.Cruise; set => pilot.Cruise = value; }

        /// <summary>Real seconds until the emergency tanker the pilot called reaches the ship, or below 0 for none on its way.</summary>
        public float TankerDueS { get; private set; } = -1;

        /// <summary>The propellant warning last shown (0 none, 1 low, 2 dry), so each is toasted once as it is crossed.</summary>
        int propellantWarning;

        /// <summary>Real seconds a tanker the pilot calls takes to come alongside.</summary>
        public const float TankerCallS = 20f;

        /// <summary>Seconds until the next check for a hostile worth proactively starting a fight over
        /// (see <see cref="War.TryStartPlayerBattle"/>) -- a few times a second is plenty responsive
        /// and cheap enough not to bother running every single frame.</summary>
        float pilotBattleCheckTimer;
        const float PilotBattleCheckIntervalS = 0.5f;

        /// <summary>The surrendered hull (or pirate cargo demand) the player was last prompted about, so the
        /// choice toast shows once per hail rather than every frame. See <see cref="UpdateSurrenderPrompts"/>.</summary>
        BattleUnit termsPrompted;
        bool demandPrompted;

        /// <summary>The jump gate the piloted ship is close enough to activate, or null
        /// (see <see cref="UpdateGateActivation"/>); drives that gate's glow and the HUD prompt.</summary>
        JumpGate nearGate;

        /// <summary>The hulk the piloted ship is latched on to (see <see cref="UpdateSalvage"/>), or null.</summary>
        public SalvageSession Salvage { get; private set; }

        /// <summary>The nearest derelict or wreck within <see cref="SalvageApproachKm"/> while piloting and not
        /// latched: a line for the HUD saying how far off it is and how fast the gap is closing.</summary>
        public string SalvageApproach { get; private set; }

        /// <summary>How near a hulk must be for its approach readout to show, km.</summary>
        const double SalvageApproachKm = 60;

        /// <summary>The hulk last offered "1 latch on", and how far into the session's log the toasts have reached.</summary>
        object salvagePrompted;
        int salvageLogShown;

        /// <summary>The piloted ship's offset from the hulk it is latched to, held fixed while the clamps are on.</summary>
        DVec3 salvageLatchOffsetKm;

        public bool ShowOrbits { get; set; } = true;
        public bool ShowLabels { get; set; } = true;
        public bool ShowHelp { get; set; } = true;
        public bool ShowHud { get; set; } = true;
        public bool ShowMarket { get; set; }
        public bool ShowCargo { get; set; }

        /// <summary>The news feed panel in place of the four-line ticker (X).</summary>
        public bool ShowNews { get; set; }
        public bool ShowSpawn { get; set; }

        /// <summary>A screen the player clicks through is open (market, cargo hold, spawn grid): the cursor is
        /// freed for it, so the mouse stops steering (and firing) the piloted hull meanwhile.</summary>
        bool ClickPanelOpen => ShowSpawn || (ShowHud && (ShowMarket || ShowCargo || ShowNews)); // market, cargo and news are HUD panels, hidden with it
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
        BattleFx battleFx;
        DebrisFields debrisFields;
        DiscView discView;
        FighterViews fighterViews;
        LaneView laneView;
        GateView gateView;
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
        DVec3 BattleAnchorScene(Battle b) => AnchorScene(b.Site, b.Body, b.Anchor);

        /// <summary>Scene position of a lasting wreck: where its battle was drawn, at the spot its unit died.</summary>
        public DVec3 WreckScene(Wreck w) => AnchorScene(w.Site, w.Body, w.Anchor) + w.Local.SwapYZ * BattleKmScale;

        DVec3 AnchorScene(LaneSite site, int body, DVec3 anchor)
        {
            if (site == null) return bodyScene[body] + Scale.LocalOffset(Catalog[body], anchor);
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
            // Let go of anything in the old system first: its ships, structures and indices are about to go.
            SpaceCamera.ExitFocus();
            if (systemRoot != null)
            {
                Materials.DestroyOwned(systemRoot);
                Destroy(systemRoot.gameObject);
            }
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
            debrisFields = new DebrisFields(this, systemRoot);
            discView = new DiscView(this, systemRoot);
            fighterViews = new FighterViews(this, systemRoot);
            laneView = new LaneView(this, systemRoot);
            gateView = new GateView(this, systemRoot);
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
            UpdateShips(0);
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
            // The pilot session belongs to this system's ship and world: the gate is the way across while flying.
            if (IsPiloting) { ShowToast("Release the helm (P) before travelling to another system, or fly through the gate"); return; }
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

        /// <summary>Adds views (and per-ship arrays) for hulls a yard has launched since the last frame.
        /// Returns whether a resize actually happened, so a caller reached off the normal per-frame
        /// path (<see cref="TargetScene"/>) knows whether it must also force a fresh <see cref="UpdateShips"/>
        /// before trusting the newcomer's <see cref="shipScene"/> entry.</summary>
        bool SyncShipViews()
        {
            var ships = Fleet.Ships;
            int have = shipViews.Length;
            if (ships.Count == have) return false;
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
            for (int i = have; i < ships.Count; i++)
            {
                shipViews[i] = new ShipView(ships[i], systemRoot);
                shipBlendT[i] = 1;
                wreckStart[i] = -1;
            }
            return true;
        }

        void BuildShipViews()
        {
            if (shipViews != null)
                foreach (var v in shipViews)
                {
                    Materials.DestroyOwned(v.Root.transform);
                    Destroy(v.Root);
                }
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
            Input.Poll(overUi, ModelViewer.CapturesInput);
            HandleCommands();

            Clock.Advance(dt);
            Galaxy.Update(Clock.Jd);
            SyncShipViews();

            AnimateScale(dt);
            Scale.Layout(Ephemeris, bodyScene);
            UpdateRadii();
            UpdatePiloting(dt);
            ShowLawNotices();
            UpdateShips(dt);
            Structures.Layout();
            if (!Travel.Active) World.UpdateAdventures(Clock.Jd, PlayerHelioKm());

            DropLostTargets();
            BattleTime();
            if (Travel.Active) Travel.Update(dt);
            else SpaceCamera.Update(dt, Input);
            UpdateClipPlanes(dt);
            UpdateViews(dt);
            UpdateHover();
            HandleSelectionClicks();

            ToastAge += dt;
            bool lockCursor = (Input.LookHeld || (IsPiloting && !ClickPanelOpen)) && !ModelViewer.CapturesInput;
            Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockCursor;
        }

        /// <summary>A ship destroyed while selected or followed: let go of it.</summary>
        void DropLostTargets()
        {
            if (Selected.IsShip && Selected.Ship.Destroyed) Select(Target.None);
            if (Hovered.IsShip && Hovered.Ship.Destroyed) Hovered = Target.None;
            if (IsPiloting && Piloted.Ship.Destroyed) ExitPiloting();
            else if (IsPiloting && World.PilotedShip != Piloted.Ship) LoseHelm();
            else if (SpaceCamera.FocusTarget.IsShip && SpaceCamera.FocusTarget.Ship.Destroyed) SpaceCamera.ExitFocus();
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
            // The model viewer has the screen: the game's HUD would paint over the model and its buttons
            // (travel, Now, Focus...) would still act on the hidden game.
            if (ModelViewer.CapturesInput) return;
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
            if (i.ResetTime && !IsPiloting) ResetToNow();
            if (i.ToggleOrbits) ShowOrbits = !ShowOrbits;
            if (i.ToggleLabels) ShowLabels = !ShowLabels;
            if (i.ToggleHelp) ShowHelp = !ShowHelp;
            if (i.ToggleHud) ShowHud = !ShowHud;
            if (i.ToggleMarket) ShowMarket = !ShowMarket;
            if (i.ToggleCargo) ShowCargo = !ShowCargo;
            if (i.ToggleNews) ShowNews = !ShowNews;
            if (i.ToggleSpawn) ShowSpawn = !ShowSpawn;
            if (i.ToggleLanes) ShowLanes = !ShowLanes;
            if (i.LevelView) LevelView();
            if (i.ToggleSatellites)
            {
                ShowSatellites = !ShowSatellites;
                ShowToast(ShowSatellites ? "SATELLITES ON: constellations drawn" : "SATELLITES OFF  (K to show)");
            }
            // The chase camera belongs to the pilot: nothing re-aims it at something else mid-flight.
            if (i.WatchBattle && !IsPiloting)
            {
                var b = BiggestBattle();
                if (b != null) WatchBattle(b);
                else ShowToast("No battles under way");
            }
            if (i.Home && !IsPiloting) FocusSystemOverview();
            if (i.Focus && !IsPiloting) FocusSelected();
            if (i.Escape)
            {
                if (IsPiloting) ExitPiloting();
                else if (SpaceCamera.Mode != SpaceCamera.CameraMode.Free) SpaceCamera.ExitFocus();
                else Select(Target.None);
            }
            if (i.CycleShips && !IsPiloting) CycleShips(i.CycleBackward ? -1 : 1);
            if (i.NumberKey >= 0 && !IsPiloting) NumberHotkey(i.NumberKey);
            if (i.Pilot) TogglePiloting();
        }

        static readonly string[] SolNumberBodies = { "Sun", "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune", "Pluto" };

        static readonly string[] CentauriNumberBodies =
        {
            "Alpha Centauri A", "Alpha Centauri B", "Proxima Centauri", "Proxima b", "Proxima d", "Proxima c",
            "Alpha Centauri Ab", "Rigil Gate", "Toliman Forge", "Blackhollow",
        };

        /// <summary>The bodies the number keys pick: Sol's and Alpha Centauri's chosen lists, elsewhere the visible bodies in catalogue order.</summary>
        string[] NumberBodies()
        {
            if (IsSol) return SolNumberBodies;
            if (ActiveSystem.Name == "Alpha Centauri") return CentauriNumberBodies;
            var names = new List<string>();
            foreach (var b in Catalog.Bodies)
                if (b.IsVisible && names.Count < 10) names.Add(b.Name);
            return names.ToArray();
        }

        void NumberHotkey(int key)
        {
            var names = NumberBodies();
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
            // The chase camera belongs to the pilot (the HUD's Focus button lands here too).
            if (IsPiloting) return;
            if (Selected.IsNone)
            {
                if (SpaceCamera.Mode != SpaceCamera.CameraMode.Free) SpaceCamera.ExitFocus();
                return;
            }
            bool refocus = SpaceCamera.Mode == SpaceCamera.CameraMode.Focus && SpaceCamera.FocusTarget == Selected;
            SpaceCamera.FocusOn(Selected, refocus ? SpaceCamera.DefaultFraming(Selected) : double.NaN);
        }

        // ---------------------------------------------------------------- piloting

        public void TogglePiloting()
        {
            if (IsPiloting)
            {
                ExitPiloting();
                return;
            }
            var t = Selected.IsShip ? Selected : Hovered.IsShip ? Hovered : Target.None;
            if (!t.IsShip)
            {
                ShowToast("Select a ship to pilot it");
                return;
            }
            var ship = t.Ship;
            // Nor a prize already taken in a fight still on: it is its captors' to dispose of when it ends.
            bool taken = ship.Battle?.UnitOf(ship)?.Captured ?? false;
            if (ship.Destroyed || ship.Derelict || taken)
            {
                ShowToast("Can't take the helm of that ship right now");
                return;
            }
            EnterPiloting(t);
        }

        /// <summary>
        /// Makes <paramref name="ship"/> a freshly Parked hull fixed at the pilot's spot and facing.
        /// Taken control may start mid-lane, mid-burn, queued, landed or in formation (see
        /// <see cref="EnterPiloting"/>), so the old phase's bookkeeping (path, journey, ring queue) is
        /// stale and dropped: done on taking the helm, so nothing treats a flown hull as landed (hidden,
        /// unpickable) or reads an escort's missing orbit, and again on release.
        /// </summary>
        void PinWherePiloted(Ship ship)
        {
            var v = pilotForward;
            var u = DVec3.Cross(Math.Abs(v.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX, v).Normalized;
            ship.Orbit = new ParkingOrbit
            {
                Body = pilotFrameBody, Center = pilotOffsetKm, RadiusKm = 0,
                U = u, V = v, EpochJd = Clock.Jd, PhaseAtEpoch = 0, RateRadPerDay = 0, Fixed = true,
            };
            ship.Phase = ShipPhase.Parked;
            ship.Path = null;
            ship.Journey = null;
            ship.JourneyIndex = 0;
            ship.Ring = null;
            ship.HoldPoint = null;
            ship.LaneHoldSide = DVec3.Zero;
        }

        /// <summary>The body whose frame best describes a ship's current position, and its offset
        /// there in true km -- the same per-phase placement <see cref="UpdateShips"/> computes for
        /// drawing, used instead to seed piloting from wherever a ship actually is (mid-lane,
        /// mid-burn, queued, landed...), not only a parked orbit.</summary>
        (int body, DVec3 offsetKm) CurrentFrame(Ship ship, double jd)
        {
            // An escort has no schedule of its own (no orbit, no phase): it is wherever its leader is.
            var c = ship.Controller;
            var st = Fleet.StateOf(c, jd);
            if (c.Phase == ShipPhase.Parked)
                return (c.Orbit.Body, c.Orbit.OffsetAt(jd));
            if ((c.Phase == ShipPhase.Queued || c.Planetside) && c.Ring != null)
                return (c.Ring.Body, st.HelioKm - Ephemeris.Helio(c.Ring.Body));
            return (c.FrameBody, st.HelioKm - Ephemeris.Helio(c.FrameBody));
        }

        /// <summary>
        /// Takes direct control of any flyable hull, wherever it is -- parked, mid-transit, riding or
        /// lurking off a hyperlane, queued or landed at a ring, even mid-battle (an escort is detached
        /// from its formation; a ship already fighting has its existing <see cref="BattleUnit"/> handed
        /// to the player instead of starting a new fight) -- via <see cref="TogglePiloting"/>, or a
        /// freshly spawned one via <see cref="SpawnShip"/>. Seeds every per-frame pilot field and snaps
        /// the camera into chase.
        /// </summary>
        void EnterPiloting(Target t)
        {
            var ship = t.Ship;
            Piloted = t;
            World.PilotedShip = ship;
            // Where it is (and which way it faces) before an escort is cut loose from the leader it shares them with.
            var (frame, offsetKm) = CurrentFrame(ship, Clock.Jd);
            var facing = Fleet.StateOf(ship, Clock.Jd).Facing;
            var velocity = DVec3.Zero;
            if (ship.IsEscort) ship.Leader = null;
            else World.War.HandOverCommand(ship);
            if (ship.Battle != null)
            {
                var unit = ship.Battle.UnitOf(ship);
                if (unit != null)
                {
                    unit.PlayerControlled = true;
                    unit.PlayerFireHeld = false;
                    unit.PlayerLockTarget = null;
                    // Mid-fight, the hull is where the battle has it, not where its schedule would put it:
                    // the pilot loop writes this pose back every frame (pilotOffsetKm - Anchor), so seed it from there.
                    frame = ship.Battle.Body;
                    offsetKm = unit.Pos + ship.Battle.Anchor;
                    facing = unit.Forward;
                    velocity = unit.Vel;
                }
            }
            pilotFrameBody = frame;
            SeedPilot(offsetKm, velocity, facing);
            pilotFlightAssistOff = false;
            pilotBattleCheckTimer = 0;
            demandPrompted = false;
            termsPrompted = null;
            havenPrompted = null;
            // Out of a battle, the hull is the pilot's from here: no longer landed, descending or in formation.
            // In one, the battle owns its pose, but an escort cut loose still needs an orbit of its own.
            if (ship.Battle == null || ship.Orbit == null) PinWherePiloted(ship);

            Select(t);
            SpaceCamera.EnterPilotChase(t, 6);
            Clock.SetWarp(0);
            ShowToast("Piloting " + ship.Name + ", " + Clock.Warp.Label + "  (P to release, . to speed up, Left-click to fire, T to lock a target, Z/X to roll, F flight assist, hold J to cruise)");
        }

        /// <summary>
        /// Dev/sandbox tool (U.SPC-T9): builds a ship of <paramref name="cls"/> (a random design if the
        /// class has more than one, see <see cref="CombatSpec.RandomOf"/>) via <see cref="Fleet.SpawnAt"/>,
        /// parked exactly where the camera is looking from, and immediately pilots it -- for testing
        /// flight feel and combat without hunting down an existing hull in the live simulation. Releases
        /// whatever was already being piloted first, same as pressing Enter would.
        /// </summary>
        public void SpawnShip(ShipClass cls, bool hostile)
        {
            if (IsPiloting) ExitPiloting();

            // Where the camera is now: its free-flight frame goes stale while it is focused on something far off.
            int frame = SpaceCamera.FrameAt(SpaceCamera.ScenePosition);
            var offsetKm = Scale.InverseLocalOffset(Catalog[frame], SpaceCamera.ScenePosition - BodyScene(frame));
            var forwardUnity = SpaceCamera.Rotation * Vector3.forward;
            var forward = new DVec3(forwardUnity.x, forwardUnity.y, forwardUnity.z).SwapYZ;

            var faction = hostile ? World.Pirates : World.Sol;
            string name = "Sandbox " + cls + " " + (++spawnedCount);
            var ship = Fleet.SpawnAt(name, cls, faction, frame, offsetKm, forward, Clock.Jd);
            ship.Ports = World.FreeShipPorts(); // not a hidden haven's rock, as for every free ship

            EnterPiloting(Target.OfShip(ship));
        }

        int spawnedCount;

        /// <summary>Where the player is, true heliocentric km: the piloted ship, else the camera (an observer
        /// still has adventures offered around them, but only a piloted ship can answer one).</summary>
        public DVec3 PlayerHelioKm()
        {
            if (IsPiloting) return Ephemeris.Helio(pilotFrameBody) + pilotOffsetKm;
            // The camera's free-flight frame goes stale while it is focused on something far off.
            int frame = SpaceCamera.FrameAt(SpaceCamera.ScenePosition);
            return Ephemeris.Helio(frame) + Scale.InverseLocalOffset(Catalog[frame], SpaceCamera.ScenePosition - BodyScene(frame));
        }

        /// <summary>Scene position of an opportunity's HUD marker: on its ship or wreck, else its point in its body's frame.</summary>
        public bool OpportunityScene(Opportunity op, out DVec3 scene)
        {
            scene = default;
            if (op.Ship != null && !op.Ship.Destroyed && op.Ship.Id < shipScene.Length) scene = shipScene[op.Ship.Id];
            else if (op.Wreck != null) scene = WreckScene(op.Wreck);
            else if (op.Body >= 0 && op.Body < bodyScene.Length) scene = bodyScene[op.Body] + Scale.LocalOffset(Catalog[op.Body], op.OffsetKm);
            else return false;
            return true;
        }

        /// <summary>Starts the hull's motion at rest in attitude, nose on a facing (ecliptic, unit) and
        /// level with the camera's up frame unless that facing is itself near-vertical, with the stick
        /// pointing straight ahead -- shared by <see cref="TogglePiloting"/> and a jump gate handing the
        /// same pilot session to a new hull in the destination system.</summary>
        void SeedPilot(DVec3 offsetKm, DVec3 velocityKmS, DVec3 facing)
        {
            pilot = PilotState.At(offsetKm, velocityKmS, facing);
            var upUnity = SpaceCamera.UpFrame * Vector3.up;
            var upReference = new DVec3(upUnity.x, upUnity.y, upUnity.z).SwapYZ;
            var level = upReference - pilot.Forward * DVec3.Dot(upReference, pilot.Forward);
            if (level.LengthSquared > 1e-6) pilot.Up = level.Normalized;
            pilotAim = pilot.Forward;
        }

        /// <summary>The piloted ship was taken from the player (boarded and captured): the session ends with
        /// nothing handed back, since the hull is its captor's now and the war disposes of it.</summary>
        void LoseHelm()
        {
            EndSalvage(null);
            SalvageApproach = null;
            var ship = Piloted.Ship;
            // Where it was taken: the captor's schedule (or the drifting hulk) picks up from here. If the
            // capture ended the fight in the same tick, the war has already planned the prize's next trip from
            // its old spot: plan it again from here, or the pin would hold it until that trip's departure time.
            PinWherePiloted(ship);
            if (ship.Battle == null && !ship.Destroyed && !ship.Derelict && !ship.IsEscort) Fleet.ScheduleNext(ship, Clock.Jd);
            Piloted = Target.None;
            pilotLockTarget = Target.None;
            pilotBoosting = false;
            pilotCruise = default;
            SpaceCamera.PilotLockTarget = Target.None;
            SpaceCamera.ExitFocus();
            ShowToast("The " + ship.Name + " has been boarded and taken");
        }

        void ExitPiloting()
        {
            EndSalvage(null);
            SalvageApproach = null;
            TankerDueS = -1;
            propellantWarning = 0;
            var ship = Piloted.Ship;
            Piloted = Target.None;
            World.PilotedShip = null;
            pilotLockTarget = Target.None;
            pilotBoosting = false;
            pilotCruise = default;
            SpaceCamera.PilotLockTarget = Target.None;
            if (ship != null && !ship.Destroyed)
            {
                if (ship.Battle != null)
                {
                    // Hand the fight back to the crew rather than leaving the hull idle mid-battle:
                    // its own autonomous AI (broadsides, point defence, missiles) picks up right where
                    // manual control left off, same facing and velocity.
                    var unit = ship.Battle.UnitOf(ship);
                    if (unit != null)
                    {
                        unit.PlayerControlled = false;
                        unit.PlayerFireHeld = false;
                        unit.PlayerLockTarget = null;
                    }
                    // And its schedule picks up from here when the fight is over, not from wherever
                    // the helm was taken (the battle's own pose doesn't move the ship's orbit).
                    PinWherePiloted(ship);
                }
                else
                {
                    // Pin the hull exactly where the player left it (facing included) before the AI
                    // schedule resumes -- otherwise it picks up from the stale pre-piloting orbit and
                    // the ship snaps back to wherever that orbit would have drifted to on its own.
                    PinWherePiloted(ship);
                    Fleet.ScheduleNext(ship, Clock.Jd);
                }
            }
            SpaceCamera.ExitFocus();
            ShowToast("Released manual control");
        }

        /// <summary>Whether the piloted ship can trade at <paramref name="s"/> right now: piloting is a local
        /// flight within one body's frame (<see cref="pilotFrameBody"/> never changes mid-flight, see
        /// <see cref="TogglePiloting"/>), so reach is that frame matching the market's body and the ship still
        /// being within its neighbourhood -- the same "near a world" radius <see cref="Fleet.WanderOrbit"/>
        /// and the hyperlane ramps use -- on top of the usual diplomatic can-dock check.</summary>
        public bool CanTradeNow(Settlement s) => NearPort(s) && Economy.CanTrade(World.PilotedShip, s);

        /// <summary>Whether the piloted ship is close enough to <paramref name="s"/> to deal with its port (trade, pay fines), welcome or not.</summary>
        public bool NearPort(Settlement s) =>
            IsPiloting && World.PilotedShip != null && pilotFrameBody == s.Body && pilotOffsetKm.Length <= ScaleModel.NeighbourhoodKm(Catalog[s.Body]);

        /// <summary>Shows what the law has to tell the player (their crimes seen, hails, scans, bounties collected) as toasts.</summary>
        void ShowLawNotices()
        {
            var notices = World.Law?.TakeNotices();
            if (notices != null && notices.Count > 0) ShowToast(string.Join("   ·   ", notices));
        }

        /// <summary>
        /// A patrol's order to heave to (see <see cref="Law.UpdatePilot"/>): 1 pays the fine it asks from the
        /// player's purse. Returns true while that hail has the number keys.
        /// </summary>
        bool UpdateLawHail(Ship ship)
        {
            var hail = World.Law?.Hail;
            if (hail == null || !hail.Interdiction) return false;
            if (Input.NumberKey == 1)
            {
                if (World.Law.PayFine(ship, hail.By, Clock.Jd, out double paid))
                    ShowToast("Paid " + Num(paid, "N0") + " cr in fines to " + hail.By.Name + ": you are free to go");
                else ShowToast("Not enough credits to pay the fine of " + Num(hail.Fine, "N0") + " cr");
            }
            return true;
        }

        /// <summary>Turns mouse delta + throttle/strafe keys into a flight command and steps the hull's
        /// position every frame piloting is active, before <see cref="UpdateShips"/> reads it.</summary>
        void UpdatePiloting(float dt)
        {
            if (!IsPiloting) { pilotBoosting = false; pilotCruise = default; nearGate = null; return; }
            var ship = Piloted.Ship;
            // Taken this very frame (the war let go of it on capture) -- even if its captors then scuttled it
            // at once: the session ends, as a loss, before anything here hands the hull back to the player.
            if (ship != null && World.PilotedShip != ship)
            {
                LoseHelm();
                return;
            }
            if (ship == null || ship.Destroyed)
            {
                ExitPiloting();
                return;
            }

            // Free look: the stick swings about the hull's own roof (yaw) and starboard (pitch) axes,
            // so there's no gimbal clamp and "mouse right" stays screen-right however the hull has
            // rolled -- the nose can be pointed anywhere, including straight up/down or looping past behind.
            var look = ClickPanelOpen ? Vector2.zero : Input.MouseDelta;
            if (look.sqrMagnitude > 0)
            {
                pilotAim = PilotFlight.RotateAbout(pilotAim, pilot.Up, -look.x * PilotLookSensitivity * Mathf.Deg2Rad);
                pilotAim = PilotFlight.RotateAbout(pilotAim, pilot.Right, look.y * PilotLookSensitivity * Mathf.Deg2Rad).Normalized;
            }
            var desiredForward = pilotAim;

            if (Input.ToggleFlightAssist)
            {
                pilotFlightAssistOff = !pilotFlightAssistOff;
                ShowToast(pilotFlightAssistOff ? "FLIGHT ASSIST OFF: the hull drifts on its momentum  (F to turn it back on)" : "FLIGHT ASSIST ON");
            }

            if (Input.LockTarget) ToggleLockTarget();
            if (!pilotLockTarget.IsNone && (!pilotLockTarget.IsShip || pilotLockTarget.Ship.Destroyed)) pilotLockTarget = Target.None;
            SpaceCamera.PilotLockTarget = pilotLockTarget;

            var tuning = PilotTuning.Of(ship.Spec.Class);
            var input = new PilotInput
            {
                DesiredForward = desiredForward,
                Throttle = Mathf.Clamp(Input.Move.z, -1f, 1f),
                StrafeRight = Mathf.Clamp(Input.Move.x, -1f, 1f),
                StrafeUp = Mathf.Clamp(Input.Move.y, -1f, 1f),
                Roll = Mathf.Clamp(Input.Roll, -1f, 1f),
                Boost = Input.Boost,
                Cruise = Input.Cruise,
                InCombat = ship.Battle != null,
                FlightAssistOff = pilotFlightAssistOff,
            };
            // Mirrors PilotFlight.Step's own "boosting" condition, read before Step drains the
            // charge further this frame, purely to drive the camera's FOV kick below.
            pilotBoosting = input.Boost && tuning.BoostDurationS > 0 && ship.Combat.BoostFraction > 0;
            var gravity = PilotGravity();
            var posBefore = pilot.Pos;
            var velBefore = pilot.Vel;
            PilotFlight.Step(ref pilot, ship.Combat, HullDynamics.Of(ship.Combat.Spec), tuning, input, gravity, dt);
            // The drive runs on the ship's own tanks: every change of velocity its thrusters make (not gravity's
            // pull, which is free) is paid for in reaction mass by the rocket equation, and a dry hull only coasts.
            var pos = pilot.Pos;
            var vel = pilot.Vel;
            Propulsion.ApplyManoeuvre(ship, ref pos, ref vel, posBefore, velBefore + gravity * dt, dt);
            pilot.Pos = pos;
            pilot.Vel = vel;
            KeepPilotAboveSurfaces(ship);
            UpdatePropellant(ship, dt);

            UpdatePilotBattle(ship, dt);
            // The law around the ship: trespass, and patrols that scan it or, if it is wanted, hail it to pay up.
            World.Law.UpdatePilot(ship, pilotFrameBody, pilotOffsetKm, pilotForward, Clock.Jd);
            UpdateGateActivation(ship);
        }

        /// <summary>The pull on the piloted hull of every body it is close to (see <see cref="PilotFlight.Gravity"/>),
        /// km/s² -- its own frame's body above all, but a moon or a neighbour too when it flies near one.</summary>
        DVec3 PilotGravity()
        {
            var helio = Ephemeris.Helio(pilotFrameBody) + pilotOffsetKm;
            var g = DVec3.Zero;
            for (int b = 0; b < Catalog.Count; b++)
            {
                var body = Catalog[b];
                if (body.Gm > 0) g += PilotFlight.Gravity(helio - Ephemeris.Helio(b), body.Gm, body.RadiusKm);
            }
            return g;
        }

        /// <summary>A hull its thrusters cannot hold up sinks onto the surface and rests there rather than
        /// falling through it -- any body it is close enough to feel, not only its own frame's.</summary>
        void KeepPilotAboveSurfaces(Ship ship)
        {
            var frameHelio = Ephemeris.Helio(pilotFrameBody);
            for (int b = 0; b < Catalog.Count; b++)
            {
                var body = Catalog[b];
                if (body.Gm <= 0) continue;
                var centre = Ephemeris.Helio(b) - frameHelio;
                if ((pilotOffsetKm - centre).Length > body.RadiusKm * PilotFlight.GravityReachRadii) continue;
                PilotFlight.KeepAboveSurface(ref pilot, centre, body.RadiusKm + Math.Max(0.5, ship.LengthKm));
            }
        }

        /// <summary>Warns as the piloted ship's tanks run low and dry, and brings a called tanker alongside when it is due.</summary>
        void UpdatePropellant(Ship ship, float dt)
        {
            if (TankerDueS >= 0)
            {
                TankerDueS -= dt;
                if (TankerDueS < 0)
                {
                    double tonnes = Propulsion.TankerLoad(ship);
                    double fee = Propulsion.TankerFee(tonnes);
                    ship.Propellant += tonnes;
                    ship.Faction.Treasury -= fee;
                    ShowToast("A tanker comes alongside: " + tonnes.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " t of reaction mass for " +
                        fee.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " cr");
                }
            }
            int level = ship.Propellant <= 1e-6 ? 2 : ship.PropellantFraction < 0.15 ? 1 : 0;
            if (level > propellantWarning)
                ShowToast(level == 2 ? "PROPELLANT DRY: drifting. Refuel at a port's market, or call a tanker" : "Propellant low: " + Mathf.RoundToInt((float)(ship.PropellantFraction * 100)) + "% left");
            propellantWarning = level;
        }

        /// <summary>Calls an emergency tanker out to the piloted ship (dearer than a port: a call-out fee and three times the going rate).</summary>
        public void CallTanker()
        {
            if (!IsPiloting || TankerDueS >= 0) return;
            var ship = Piloted.Ship;
            if (ship == null || Propulsion.TankerLoad(ship) < 1) { ShowToast("Tanks are full"); return; }
            TankerDueS = TankerCallS;
            ShowToast("Tanker called: about " + Mathf.RoundToInt(TankerCallS) + " s out, " +
                Propulsion.TankerFee(Propulsion.TankerLoad(ship)).ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " cr to fill the tanks");
        }

        /// <summary>
        /// While piloting, checks (and on the player's command, performs) a transit through whichever
        /// of this system's jump gates the ship is in range of (one that is shut will not open): a flyable, in-world alternative to the menu-driven STAR SYSTEMS jump (see
        /// <see cref="StarTravel"/>, untouched -- the camera still uses it on its own). The eligibility
        /// and position-handoff math is Core's (<see cref="GateTransit"/>); this just wires the
        /// activation key and re-seeds the pilot session onto the arriving hull in its new system,
        /// exactly as <see cref="TogglePiloting"/> seeds one at the start.
        /// </summary>
        void UpdateGateActivation(Ship ship)
        {
            if (ActiveSystem.Gates.Count == 0 || ship.Battle != null) { nearGate = null; return; }
            var shipHelioKm = Ephemeris.Helio(pilotFrameBody) + pilotOffsetKm;
            var wasNear = nearGate;
            var gate = nearGate = ActiveSystem.GateInRange(shipHelioKm);
            if (gate != null && gate != wasNear)
                ShowToast("In range of " + gate.Name.ToUpperInvariant() + " to " + gate.Destination.Name.ToUpperInvariant() +
                          (gate.Admits(ship.Faction, Clock.Jd) && gate.Partner.Admits(ship.Faction, Clock.Jd) ? " (Y to activate)" : " (shut: under blockade)"));
            if (gate == null || !Input.ActivateGate) return;
            // A blockaded gate (or one whose far end is) will not open for the pilot either.
            if (!gate.Link.Open(ActiveSystem, ship.Faction, Clock.Jd)) { ShowToast(gate.Name.ToUpperInvariant() + " is shut to " + ship.Faction.Name); return; }

            var destination = gate.Destination;
            if (!GateTransit.TryPerform(gate, shipHelioKm, ship, Fleet, Clock.Jd, out var arrived)) return;
            EndSalvage(null);
            SalvageApproach = null;

            World.PilotedShip = null;
            Activate(destination);
            Piloted = Target.OfShip(arrived);
            World.PilotedShip = arrived;
            pilotFrameBody = arrived.Orbit.Body;
            SeedPilot(arrived.Orbit.Center, DVec3.Zero, arrived.Orbit.V);
            pilotBattleCheckTimer = 0;
            pilotLockTarget = Target.None;
            SpaceCamera.PilotLockTarget = Target.None;
            nearGate = null;

            Select(Piloted);
            SpaceCamera.EnterPilotChase(Piloted, 6);
            ShowToast("Jumped to " + destination.Name.ToUpperInvariant() + " through the gate");
        }

        /// <summary>Starts (or joins) a fight the instant piloting brings the ship within range of a
        /// hostile -- mirroring what <see cref="War.OnArrived"/> does for every AI ship, since a player
        /// flying there is otherwise invisible to that check (see <see cref="War.PresentGroups"/>) -- and,
        /// every frame afterward, hands the battle this frame's actual flown pose and fire/lock input
        /// instead of letting its own AI steer and pick targets for the hull.</summary>
        void UpdatePilotBattle(Ship ship, float dt)
        {
            if (ship.Battle != null && Salvage != null) EndSalvage("Clamps released: battle stations");
            if (ship.Battle != null) SalvageApproach = null;
            if (ship.Battle == null)
            {
                // Out of any fight: a hail left open when the last one ended (or before release) is gone,
                // so the next battle's demand or surrender gets its own prompt.
                demandPrompted = false;
                termsPrompted = null;
                if (!UpdateSalvage(ship, dt) && !UpdateLawHail(ship)) UpdateHavenAssault(ship);
                // Holding fire on a locked ship the player's flag has no quarrel with opens fire on it anyway: an
                // attack on a lawful ship, a crime if anyone sees it (enemies and pirates are fought as usual below).
                var locked = pilotLockTarget.IsShip ? pilotLockTarget.Ship : null;
                if (locked != null && Input.Fire && !pilotCruise.Active && !ClickPanelOpen && !World.Diplomacy.Hostile(ship.Faction, locked.Faction))
                {
                    if (World.War.TryStartPlayerAttack(ship, locked, pilotFrameBody, pilotOffsetKm, pilotForward, Clock.Jd) != null)
                    {
                        ShowToast("Weapons free on the " + locked.Name + ": an attack on a lawful ship is a crime if anyone sees it");
                        return;
                    }
                }
                pilotBattleCheckTimer -= dt;
                if (pilotBattleCheckTimer > 0) return;
                pilotBattleCheckTimer = PilotBattleCheckIntervalS;
                // The piloted ship's scanners: a hidden haven within range goes on the map.
                foreach (var found in World.War.ScanFrom(ship.Faction, Ephemeris.HelioAt(pilotFrameBody, Clock.Jd) + pilotOffsetKm, Clock.Jd, ship.Name))
                    ShowToast("Scanners pick out a hidden pirate haven: " + found.Name + " on " + Catalog[found.Body].Name);
                World.War.TryStartPlayerBattle(ship, pilotFrameBody, pilotOffsetKm, pilotForward, Clock.Jd);
                return;
            }

            var unit = ship.Battle.UnitOf(ship);
            if (unit == null) return;
            unit.PlayerControlled = true;
            unit.Pos = pilotOffsetKm - ship.Battle.Anchor;
            unit.Vel = pilotVelKmS;
            unit.Forward = pilotForward;
            // No firing while cruising -- the cruise drive is a travel tier, not a combat one.
            unit.PlayerFireHeld = Input.Fire && !pilotCruise.Active && !ClickPanelOpen;
            unit.PlayerLockTarget = pilotLockTarget.IsShip ? ship.Battle.UnitOf(pilotLockTarget.Ship) : null;
            UpdateSurrenderPrompts(ship.Battle, unit);
        }

        /// <summary>
        /// Flying over a pirate haven whose garrison platforms are gone (and with no raiders holding it):
        /// the number keys finish the assault, 1 to board and take it (the group needs marines), 2 to
        /// destroy it (see <see cref="War.AssaultHaven"/>).
        /// </summary>
        void UpdateHavenAssault(Ship ship)
        {
            Settlement haven = null;
            if (pilotOffsetKm.Length <= ScaleModel.NeighbourhoodKm(Catalog[pilotFrameBody]))
                foreach (var s in Economy.At(pilotFrameBody))
                    if (World.War.CanAssault(s, ship)) haven = s;
            if (haven == null)
            {
                havenPrompted = null;
                return;
            }
            bool marines = World.War.HasMarines(ship);
            // Destroying it takes guns (or marines to set the charges), as AssaultHaven requires.
            bool canDestroy = marines || ship.Controller.Combat.Armed;
            if (havenPrompted != haven)
            {
                havenPrompted = haven;
                ShowToast(!canDestroy ? haven.Name + "'s defences are down, but this hull has neither guns nor marines to finish it"
                    : haven.Name + "'s defences are down:  " + (marines ? "1 board and take it, " : "") + "2 destroy it");
            }
            int key = Input.NumberKey;
            if ((key == 1 && marines) || (key == 2 && canDestroy))
            {
                bool capture = key == 1;
                if (World.War.AssaultHaven(haven, ship, capture, Clock.Jd))
                    ShowToast(capture ? "Boarders take " + haven.Name + "; its raiders scatter" : haven.Name + " is destroyed; its raiders scatter");
                havenPrompted = null;
            }
        }

        Settlement havenPrompted;

        // ---------------------------------------------------------------- salvage and boarding of hulks

        /// <summary>A hulk's position and velocity in the pilot's frame (true km, km/s), from where it is at two nearby moments.</summary>
        (DVec3 offsetKm, DVec3 velKmS) HulkInPilotFrame(Func<double, DVec3> helioAt)
        {
            const double h = 1e-5;
            double jd = Clock.Jd;
            var now = helioAt(jd) - Ephemeris.HelioAt(pilotFrameBody, jd);
            var next = helioAt(jd + h) - Ephemeris.HelioAt(pilotFrameBody, jd + h);
            return (now, (next - now) / (h * Astro.SecondsPerDay));
        }

        static DVec3 DerelictHelio(Ephemeris eph, Ship s, double jd) => eph.HelioAt(s.Orbit.Body, jd) + s.Orbit.OffsetAt(jd);

        static string Num(double v, string format) => v.ToString(format, System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// Salvage while piloting, out of any battle: the nearest derelict (<see cref="Ship.Derelict"/>) or wreck
        /// gets an approach readout; matched alongside it (<see cref="SalvageSession.Check"/>), 1 latches on.
        /// Latched, the ship rides with the hulk and the number keys choose: with raiders aboard, 1 sends the
        /// marines through the airlock; once it is clear, 1 salvages cargo into the grid hold, 2 strips the
        /// most useful component, 3 claims the hull if it can fly; 4 (or throttling away) casts off. Returns
        /// true while it has the number keys (latched, or alongside and offering to latch).
        /// </summary>
        bool UpdateSalvage(Ship ship, float dt)
        {
            if (Salvage != null) return UpdateLatched(ship, dt);

            // The nearest hulk in reach of the readout.
            Ship derelict = null;
            Wreck wreck = null;
            DVec3 rel = default, relVel = default;
            double best = SalvageApproachKm;
            foreach (var s in Fleet.Ships)
            {
                if (!s.Derelict || s.Destroyed || s == ship || s.Orbit == null) continue;
                var (off, vel) = HulkInPilotFrame(jd => DerelictHelio(Ephemeris, s, jd));
                double d = (off - pilotOffsetKm).Length;
                if (d >= best) continue;
                best = d;
                derelict = s;
                rel = pilotOffsetKm - off;
                relVel = pilotVelKmS - vel;
            }
            foreach (var w in World.War.Wrecks.Items)
            {
                var (off, vel) = HulkInPilotFrame(jd => World.WreckHelio(w, jd));
                double d = (off - pilotOffsetKm).Length;
                if (d >= best) continue;
                best = d;
                wreck = w;
                derelict = null;
                rel = pilotOffsetKm - off;
                relVel = pilotVelKmS - vel;
            }
            if (derelict == null && wreck == null)
            {
                SalvageApproach = null;
                salvagePrompted = null;
                return false;
            }

            string name = derelict != null ? "DERELICT " + derelict.Name : "WRECK " + wreck.Name;
            double hulkLength = derelict != null ? derelict.LengthKm : wreck.LengthKm;
            double range = SalvageSession.LatchRangeKm(hulkLength, ship.LengthKm);
            var state = SalvageSession.Check(rel, relVel, range);
            double closing = rel.LengthSquared > 0 ? -DVec3.Dot(relVel, rel.Normalized) : 0;
            string how = state == LatchState.Ready ? "[1] LATCH ON"
                : state == LatchState.TooFast ? "match velocity: " + Num(relVel.Length * 1000, "0") + " m/s, under " + Num(SalvageSession.MatchSpeedKmS * 1000, "0") + " to latch"
                : "come within " + Num(range, "0.0") + " km and match velocity to latch";
            SalvageApproach = name + "   " + Num(best, "0.0") + " km   " + (closing >= 0 ? "closing " : "opening ") + Num(Math.Abs(closing) * 1000, "0") + " m/s   " + how;
            if (state != LatchState.Ready)
            {
                salvagePrompted = null;
                return false;
            }
            object hulk = (object)derelict ?? wreck;
            if (salvagePrompted != hulk)
            {
                salvagePrompted = hulk;
                ShowToast("Alongside " + (derelict != null ? "the derelict " + derelict.Name : "the wreck of " + wreck.Name) + ", velocity matched:  1 latch on");
            }
            if (Input.NumberKey != 1) return true;
            Salvage = derelict != null ? SalvageSession.Latch(ship, derelict) : SalvageSession.Latch(ship, wreck);
            if (Salvage == null) return true;
            salvageLatchOffsetKm = rel;
            salvageLogShown = 0;
            SalvageApproach = null;
            ShowSalvageLog();
            return true;
        }

        bool UpdateLatched(Ship ship, float dt)
        {
            var s = Salvage;
            bool hulkGone = s.Derelict != null ? s.Derelict.Destroyed || !s.Derelict.Derelict : !World.War.Wrecks.Items.Contains(s.Wreck);
            if (hulkGone) { EndSalvage("The hulk broke up under you: clamps released"); return false; }
            // Throttling or strafing away pulls the clamps off.
            if (Input.Move.sqrMagnitude > 0.25f) { EndSalvage("Clamps released"); return false; }

            // Riding with the hulk: held at the offset the clamps took hold at, moving as it moves.
            var (off, vel) = s.Derelict != null
                ? HulkInPilotFrame(jd => DerelictHelio(Ephemeris, s.Derelict, jd))
                : HulkInPilotFrame(jd => World.WreckHelio(s.Wreck, jd));
            pilotOffsetKm = off + salvageLatchOffsetKm;
            pilotVelKmS = vel;
            pilotCruise = default;

            // The fight aboard runs on the sim clock (real time while piloting, faster under warp).
            s.Step(Math.Min(10, dt * Clock.Warp.SimSecondsPerSecond * Clock.Speed));
            ShowSalvageLog();

            int key = Input.NumberKey;
            if (key == 4) { EndSalvage("Clamps released"); return true; }
            if (s.Hostile)
            {
                if (key == 1 && !s.Fighting && !s.SendBoarders()) ShowToast("No marines aboard to send across");
                return true;
            }
            if (key == 1)
            {
                if (s.SalvageCargo() > 0) ShowCargo = true;
            }
            else if (key == 2)
            {
                var part = s.BestPart();
                if (part == null) ShowToast("Nothing left aboard worth stripping");
                else s.Strip(part);
            }
            else if (key == 3)
            {
                if (!s.CanClaim(out var why)) ShowToast("Can't claim " + s.Name + ": " + why);
                else if (s.Claim(Fleet, Clock.Jd))
                {
                    var claimed = s.Derelict;
                    EndSalvage(null);
                    ShowToast("Claimed: the " + claimed.Name + " flies your colours (select it and press P to take its helm)");
                }
            }
            return true;
        }

        /// <summary>Toasts each new line of the latched session's log.</summary>
        void ShowSalvageLog()
        {
            var log = Salvage?.Log;
            if (log == null) return;
            if (salvageLogShown > log.Count) salvageLogShown = log.Count;
            if (salvageLogShown == log.Count) return;
            ShowToast(log[log.Count - 1]);
            salvageLogShown = log.Count;
        }

        /// <summary>The number-key choices open on the latched hulk right now, for the HUD.</summary>
        public string SalvageChoices()
        {
            var s = Salvage;
            if (s == null) return "";
            if (s.Hostile)
                return s.Fighting ? "Marines fighting aboard   4 cast off"
                    : (s.MarinesReady >= 0.5 ? "1 send " + Num(s.MarinesReady, "0") + " marines across   " : "No marines to send   ") + "4 cast off";
            return "1 salvage cargo   2 strip a component   3 claim the hull   4 cast off";
        }

        /// <summary>Lets go of the latched hulk: the session ends, is logged, and pays out any adventure on it.</summary>
        void EndSalvage(string toast)
        {
            var s = Salvage;
            if (s == null) return;
            Salvage = null;
            salvagePrompted = null;
            var op = World.FinishSalvage(s, Clock.Jd);
            if (op != null) ShowToast(op.Outcome);
            else if (toast != null) ShowToast(toast);
        }

        /// <summary>
        /// While piloting in a battle, the number keys answer hails: a pirate demand for this ship's cargo
        /// (1 hand it over, 2 refuse), or an enemy hull that has struck its colours to this side (1 demand
        /// its cargo, 2 take the ship, 3 let it go). Unanswered, the battle settles it after
        /// <see cref="Battle.SurrenderDecisionSeconds"/>.
        /// </summary>
        void UpdateSurrenderPrompts(Battle battle, BattleUnit unit)
        {
            int key = Input.NumberKey;
            if (unit.CargoDemandedBy != null)
            {
                if (!demandPrompted)
                {
                    demandPrompted = true;
                    ShowToast(unit.CargoDemandedBy.Name + " demand your cargo:  1 hand it over and go, 2 refuse and fight");
                }
                if (key == 1 && battle.AnswerCargoDemand(unit, true)) ShowToast("Cargo handed over; the pirates let you go");
                else if (key == 2 && battle.AnswerCargoDemand(unit, false)) ShowToast("Demand refused: weapons free");
                return;
            }
            demandPrompted = false;

            var hailing = battle.AwaitingTermsFrom(unit.Side);
            if (hailing == null)
            {
                termsPrompted = null;
                return;
            }
            if (termsPrompted != hailing)
            {
                termsPrompted = hailing;
                ShowToast("The " + hailing.Name + " strikes its colours:  1 demand its cargo, 2 take the ship, 3 let it go");
            }
            SurrenderTerms? terms = key == 1 ? SurrenderTerms.RansomCargo : key == 2 ? SurrenderTerms.TakeShip : key == 3 ? SurrenderTerms.LetGo : (SurrenderTerms?)null;
            if (terms == null) return;
            if (battle.OfferSurrenderTerms(hailing, terms.Value))
                ShowToast(terms == SurrenderTerms.RansomCargo ? "The " + hailing.Name + " hands over its cargo and is let go"
                    : terms == SurrenderTerms.TakeShip ? "Boarders away to take the " + hailing.Name
                    : "The " + hailing.Name + " is let go");
            else ShowToast("Nobody on this side can board the " + hailing.Name);
        }

        /// <summary>Locks the camera's tracking onto whatever's hovered/selected, or clears an existing
        /// lock -- the ship's own facing stays fully under the player's mouse control either way (the
        /// camera itself leans toward the lock in <see cref="Cameras.SpaceCamera"/>'s focus update).</summary>
        void ToggleLockTarget()
        {
            if (!pilotLockTarget.IsNone) { pilotLockTarget = Target.None; return; }
            // The piloted ship is itself the selection while piloting, so it can't stand in the way of the hovered one.
            bool Other(Target c) => c.IsShip && c.Ship != Piloted.Ship;
            var t = Other(Selected) ? Selected : Other(Hovered) ? Hovered : Target.None;
            if (t.IsShip) pilotLockTarget = t;
        }

        /// <summary>Camera elevation of the system overview, degrees.</summary>
        public const float OverviewPitch = 52f;

        /// <summary>How much of the system the overview frames: the inner planets in Sol, the A-B pair in Alpha Centauri, the planets elsewhere.</summary>
        double OverviewSpanKm => ActiveSystem.OverviewSpanKm;

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
            // The chase camera follows the hull the mouse is flying: there is no view of its own to level,
            // and the levelling ease would compound on the chase's own easing every frame.
            if (Travel.Active || IsPiloting) return;
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
            // A reset can rebuild the galaxy out from under the piloted ship (the HUD's Now button lands here too).
            if (IsPiloting) { ShowToast("Release the helm (P) before resetting the time"); return; }
            double now = Astro.JulianDate(DateTime.UtcNow);
            bool backwards = now < Clock.Jd;
            // Rebuilding the systems mid-trip would leave the trip pointing at the old ones: land first.
            if (backwards && Travel.Active) Travel.Finish();
            Clock.SetJd(now);
            Clock.SetWarp(SimClock.DefaultWarpIndex);
            if (backwards)
            {
                // Schedules only run forward; rebuild every system at the new date.
                int index = Galaxy.Systems.IndexOf(ActiveSystem);
                var cameraAt = SpaceCamera.ScenePosition;
                var cameraRotation = SpaceCamera.Rotation;
                Galaxy = Galaxy.CreateDefault(now);
                Materials.DestroyOwned(Interstellar.Root.transform);
                Destroy(Interstellar.Root);
                Interstellar = new InterstellarView(Galaxy, worldRoot);
                SubscribeWorlds();
                Activate(Galaxy.Systems[Math.Max(0, index)]);
                SpaceCamera.SetPose(cameraAt, cameraRotation);
                GalaxyRebuilt?.Invoke();
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
                    shipLength[i] = Scale.ShipLength(ship.LengthKm);

                    if (ship == World.PilotedShip)
                    {
                        shipScene[i] = bodyScene[pilotFrameBody] + Scale.LocalOffset(Catalog[pilotFrameBody], pilotOffsetKm);
                        // Nose and roof both: the hull rolls for real (U.SPC-T59).
                        var pilotFacing = pilotForward.SwapYZ;
                        var pilotRoof = pilot.Up.SwapYZ;
                        var pf = new Vector3((float)pilotFacing.X, (float)pilotFacing.Y, (float)pilotFacing.Z);
                        var pup = new Vector3((float)pilotRoof.X, (float)pilotRoof.Y, (float)pilotRoof.Z);
                        if (pf.sqrMagnitude < 1e-12f) pf = Vector3.forward;
                        if (pup.sqrMagnitude < 1e-12f) pup = Mathf.Abs(Vector3.Dot(pf.normalized, Vector3.up)) > 0.98f ? Vector3.right : Vector3.up;
                        shipRotation[i] = Quaternion.LookRotation(pf, pup);
                        shipRotationInitialised[i] = true;
                        // Its own state, not whatever the schedule last drew (an entry glow, a drive flame).
                        shipStates[i] = new ShipState(Ephemeris.Helio(pilotFrameBody) + pilotOffsetKm, pilotForward, pilotVelKmS.Length, false, 0);
                        continue;
                    }

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
                        shipScene[i] = Scale.LaneScene(Ephemeris, bodyScene, Fleet.Lanes, ship, st.Progress, out var travel)
                            + SideScene(Fleet.LanePoint(ship, jd, st.Progress), ship.LaneHoldSide) * Fleet.LaneRejoin(ship, jd);
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
                        facing = travel;
                    }

                    // Stepping aside to keep clear of another ship: the sidestep (true km) in scene units here.
                    var evasion = Fleet.EvasionOf(ship);
                    if (evasion.LengthSquared > 0) shipScene[i] += evasion.SwapYZ * KmToSceneNear(ship, st.HelioKm);

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

        /// <summary>Scene units per km around a ship (sideways), from the mapping of the world it is nearest, or the star's out in open space.</summary>
        double KmToSceneNear(Ship ship, DVec3 helio)
        {
            var c = ship.Controller;
            int b = c.Ring != null && (c.Phase == ShipPhase.Queued || c.Planetside) ? c.Ring.Body : c.Orbit?.Body ?? 0;
            double d = (helio - Ephemeris.Helio(b)).Length;
            var body = Catalog[b];
            if (d > ScaleModel.NeighbourhoodKm(body))
            {
                body = Catalog.Root;
                d = (helio - Ephemeris.Helio(0)).Length;
            }
            d = Math.Max(d, Math.Max(body.RadiusKm, 1));
            return Scale.LocalDistance(body, d) / d;
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

        // A ship added since this frame's sync (spawned mid-frame by a tour coroutine, say) gets its views first.
        void EnsureShipSynced(Ship ship)
        {
            if (ship.Id >= shipScene.Length && SyncShipViews()) UpdateShips(0f);
        }

        public DVec3 ShipScene(Ship ship) { EnsureShipSynced(ship); return shipScene[ship.Id]; }
        public double ShipDisplayLength(Ship ship) { EnsureShipSynced(ship); return shipLength[ship.Id]; }
        public ShipState ShipStateOf(Ship ship) { EnsureShipSynced(ship); return shipStates[ship.Id]; }

        // ---------------------------------------------------------------- ICameraWorld

        public DVec3 BodyScene(int body) => bodyScene[body];
        public double BodyDisplayRadius(int body) => bodyRadius[body];

        public DVec3 TargetScene(Target target)
        {
            // A ship spawned this same frame (the scripted tour's skirmishes, the Spawn & Pilot grid,
            // a jump-gate arrival) may not have been sized into shipScene yet -- the per-ship arrays
            // only grow once a frame, in Update, but this is also reached from a coroutine step or an
            // OnGUI click that can land before or after that. A resize alone leaves the newcomer's
            // entry zeroed, so also force one fresh UpdateShips to actually place it before reading it.
            if (target.IsShip && SyncShipViews()) UpdateShips(0f);
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

        public Quaternion TargetRotation(Target target) => target.IsShip ? shipRotation[target.Ship.Id] : Quaternion.identity;

        public double NearestSurface(DVec3 p)
        {
            double best = double.MaxValue;
            for (int i = 0; i < bodyScene.Length; i++)
                best = Math.Min(best, (p - bodyScene[i]).Length - bodyRadius[i]);
            var ships = Fleet.Ships;
            for (int i = 0; i < ships.Count; i++)
            {
                if (ships[i].Destroyed || ships[i].Controller.Phase == ShipPhase.Landed) continue;
                best = Math.Min(best, (p - shipScene[i]).Length - shipLength[i] * 0.5);
            }
            foreach (var kv in Structures.Scenes)
                best = Math.Min(best, (p - kv.Value).Length - Structures.Sizes[kv.Key] * 0.5);
            foreach (var kv in Structures.RingScenes)
                best = Math.Min(best, (p - kv.Value).Length - Structures.Sizes[kv.Key] * 0.5);
            return Math.Max(best, 1e-7);
        }

        // ---------------------------------------------------------------- views

        void UpdateClipPlanes(float dt)
        {
            double nearest = NearestSurface(SpaceCamera.ScenePosition);
            UnityCamera.nearClipPlane = (float)Math.Max(1e-6, Math.Min(nearest * 0.25, 20.0));
            UnityCamera.farClipPlane = 5e8f;
            UnityCamera.transform.SetPositionAndRotation(Vector3.zero, SpaceCamera.Rotation);

            float targetFov = IsPiloting && pilotCruise.Active ? CruiseFov : IsPiloting && pilotBoosting ? BoostFov : BaseFov;
            cameraFov = Mathf.Lerp(cameraFov, targetFov, 1f - Mathf.Exp(-dt * FovResponseHz));
            UnityCamera.fieldOfView = cameraFov;
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
                    // A jump gate retires the origin hull the same way a kill does (so its id stays put
                    // for every ship after it -- see GateTransit) but it flew off, not blew up.
                    if (ship.GateDeparted) { shipViews[i].SetVisible(false); continue; }
                    DrawWreck(i, ship);
                    continue;
                }
                if (viewFaction.Length != shipViews.Length) Array.Resize(ref viewFaction, shipViews.Length);
                if (viewFaction[i] == null) viewFaction[i] = ship.Faction;
                if (viewFaction[i] != ship.Faction)
                {
                    // Taken as a prize: repaint in the captor's livery (freeing the old view's own meshes and materials).
                    Materials.DestroyOwned(shipViews[i].Root.transform);
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
            gateView.Update(true, nearGate);
            fighterViews.Update();
            embeddedPods.Update();
            battleFx.Update();
            debrisFields.Update();
            Structures.Update(true);
            swarm.Update(ShowSatellites);
            UpdateOrbitLines(dt);
            discView.Update(ShowOrbits);
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

        /// <summary>
        /// A ship lost in battle breaks up and burns out where it died, then lingers as a slowly
        /// tumbling wreck (<see cref="World.War"/>'s persistent <see cref="Wreck"/>, not just the
        /// transient burn-out) until its own configurable lifetime runs out and the Core clears it away.
        /// </summary>
        void DrawWreck(int i, Ship ship)
        {
            var wreck = World.War.Wrecks.Of(ship);
            if (wreck == null)
            {
                shipViews[i].SetVisible(false);
                return;
            }
            if (wreckStart[i] < 0) wreckStart[i] = Time.unscaledTime;
            var rel = Relative(WreckScene(wreck));
            float length = (float)Scale.ShipLength(ship.LengthKm);
            bool stillBurning = wreckStart[i] != float.MaxValue
                && shipViews[i].Wreck(rel, shipRotation[i], length, Time.unscaledTime - wreckStart[i], Time.unscaledTime);
            if (!stillBurning)
            {
                wreckStart[i] = float.MaxValue;
                shipViews[i].Derelict(rel, shipRotation[i], length, Time.unscaledTime + ship.Id);
            }
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
                    color = body.Kind == BodyKind.DwarfPlanet || body.Kind == BodyKind.Asteroid ? new Color(0.7f, 0.62f, 0.5f, 0.32f)
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
            // The fire trigger shares the left button; a locked, centred cursor while piloting would
            // otherwise register every release as an in-place click and reselect something underfoot.
            if (!Input.SelectClicked || Travel.Active || IsPiloting) return;
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
