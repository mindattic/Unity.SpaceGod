using System;
using System.Collections.Generic;
using Unity.SpaceGod.Core;
using Unity.SpaceGod.Rendering;
using Unity.SpaceGod.Views;
using UnityEngine;

namespace Unity.SpaceGod
{
    /// <summary>
    /// Seamless streaming (U.SPC-T414). A system is entered with none of its ships' models built: each is built when
    /// <see cref="Streaming"/> says it is wanted (big enough on screen, or about to be at the view's present speed and
    /// heading), a few each frame within the loading budget, and let go of again once it has dwindled well out of
    /// sight; until then it is drawn as its icon, as any far-off ship is. Body maps and ship models load in the
    /// background (<see cref="AssetStreamer"/>), and the systems beyond the gates the view is closing on are warmed
    /// ahead of arrival. What is still loading (<see cref="StreamingPending"/>) holds a jump gate's tunnel open, or the
    /// helm's own flare through a ring (<see cref="HelmGateGlow"/>), so the far side is never seen half built.
    /// </summary>
    public sealed partial class UnitySpaceApp
    {
        readonly List<StreamItem> streamItems = new List<StreamItem>();
        readonly List<int> streamLoad = new List<int>(), streamUnload = new List<int>();
        readonly System.Diagnostics.Stopwatch streamClock = System.Diagnostics.Stopwatch.StartNew();
        readonly HashSet<string> streamKeep = new HashSet<string>();
        readonly List<string> activePaths = new List<string>();
        Func<int, bool> buildShipView;
        Func<double> streamNow;
        StarSystem streamSystem;
        DVec3 streamLastCamera, streamVelocity;
        float warmTimer, releaseInS;
        bool releasePending;

        /// <summary>The most ship views let go of in one frame.</summary>
        const int MaxUnloadsPerFrame = 24;

        /// <summary>How often (seconds) the gates ahead are looked at for systems to warm.</summary>
        const float WarmEvery = 0.5f;

        /// <summary>How long after entering a system the maps of the one left behind are let go of.</summary>
        const float ReleaseAfterS = 12f;

        /// <summary>How near (in gate ring diameters) a gate must be for its far system to be warmed whatever the view's speed.</summary>
        const double WarmGateDiameters = 400;

        /// <summary>
        /// What is still loading that the view needs now: ship models big enough on screen to be drawn in full but not
        /// built yet, and the active system's maps still on their way. A gate transit holds its cover while this is above 0.
        /// </summary>
        public int StreamingPending { get; private set; }

        /// <summary>Where the view will be (scene units) when the system shows: set while a gate's tunnel hides the system,
        /// to the far gate it comes out of, so what is about to be seen is loaded first; null otherwise.</summary>
        public DVec3? StreamFocus { get; set; }

        /// <summary>The way the view will look from <see cref="StreamFocus"/>.</summary>
        public DVec3 StreamFocusHeading { get; set; }

        /// <summary>The helm's own flight through a gate: its flare-and-fold covering the far side's loading.</summary>
        readonly GateCover helmCover = new GateCover();

        /// <summary>The glow over the screen of the helm's flight through a gate, 0..1.</summary>
        public float HelmGateGlow => helmCover.Active ? (float)GateCover.HelmGlow(helmCover.T) : 0;

        /// <summary>How hard space is folded shut in the helm's flight through a gate, 0..1 (the view narrows with it).</summary>
        public float HelmGateFold => helmCover.Active ? (float)GateCover.HelmFold(helmCover.T) : 0;

        /// <summary>How far the view narrows (degrees) at the height of the helm's fold through a gate.</summary>
        const float HelmFoldDeg = 22f;

        /// <summary>Whether <paramref name="ship"/> is drawn as its 3D model right now: big enough on screen (see
        /// <see cref="ShipLod"/>) and its model built. The HUD draws its icon whenever it is not.</summary>
        public bool ShipModelShown(Ship ship, double px) =>
            ShipLod.DrawsModel(px) && ship != null && shipViews != null && ship.Id < shipViews.Length && shipViews[ship.Id] != null;

        /// <summary>Starts the helm's cover for a jump just made: a flash, a fold of space and a glow held while the far
        /// side loads.</summary>
        void BeginHelmGateCover()
        {
            helmCover.Begin(GateCover.HelmSeconds, GateCover.HelmHoldAt, Streaming.GateCoverMaxSeconds.Value);
            GateFlash = 1;
        }

        /// <summary>Called on entering a system: what it is drawn with is noted (its maps are part of what a gate waits
        /// for), and the system left behind will be let go of in a while.</summary>
        void OnSystemStreamed(StarSystem system)
        {
            activePaths.Clear();
            var catalog = system.Catalog;
            for (int i = 0; i < catalog.Count; i++)
            {
                if (catalog[i].Texture != null) activePaths.Add(AssetStreamer.TexturePath(catalog[i].Texture));
                if (catalog[i].NormalMap != null) activePaths.Add(AssetStreamer.TexturePath(catalog[i].NormalMap));
            }
            releaseInS = ReleaseAfterS;
            releasePending = true;
        }

        void UpdateStreaming(float dt)
        {
            if (shipViews == null || ActiveSystem == null) return;
            if (helmCover.Active) helmCover.Advance(dt, StreamingPending > 0);

            var camera = StreamFocus ?? SpaceCamera.ScenePosition;
            if (streamSystem != ActiveSystem)
            {
                // A new system: the camera's jump into it is no motion to lead by.
                streamSystem = ActiveSystem;
                streamVelocity = DVec3.Zero;
                OnSystemStreamed(ActiveSystem);
            }
            else if (dt > 1e-4f && StreamFocus == null)
            {
                var v = (camera - streamLastCamera) / dt;
                streamVelocity = DVec3.Lerp(streamVelocity, v, 1 - Math.Exp(-6 * dt));
            }
            streamLastCamera = camera;
            DVec3 heading;
            if (StreamFocus != null) heading = StreamFocusHeading;
            else
            {
                var f = SpaceCamera.Rotation * Vector3.forward;
                heading = new DVec3(f.x, f.y, f.z);
            }
            double ppu = PixelsPerUnitAtUnitDistance();
            double modelPx = ShipLod.IconBelowPx.Value;
            var view = new StreamView(camera, StreamFocus != null ? DVec3.Zero : streamVelocity, heading, ppu, modelPx, Streaming.LeadSeconds.Value);

            var ships = Fleet.Ships;
            int n = Math.Min(ships.Count, shipViews.Length);
            streamItems.Clear();
            for (int i = 0; i < n; i++)
            {
                var ship = ships[i];
                bool loaded = shipViews[i] != null;
                bool shown = !ship.GateDeparted && ship.Controller.Phase != ShipPhase.Landed;
                Wreck wreck = null;
                if (ship.Destroyed && shown)
                {
                    wreck = World.War.Wrecks.Of(ship);
                    shown = wreck != null;
                }
                if (!shown)
                {
                    // Nothing of it is drawn: a view it has goes.
                    if (loaded) streamItems.Add(new StreamItem(i, camera, 0, true));
                    continue;
                }
                var at = wreck != null ? WreckScene(wreck) : shipScene[i];
                bool always = ship == Piloted.Ship || (Selected.IsShip && Selected.Ship == ship) ||
                              (SpaceCamera.FocusTarget.IsShip && SpaceCamera.FocusTarget.Ship == ship);
                streamItems.Add(new StreamItem(i, at, shipLength[i] * ShipLod.ExtentFactor(ship), loaded, always));
            }
            Streaming.Plan(streamItems, view, streamLoad, streamUnload);

            buildShipView ??= BuildStreamedShipView;
            streamNow ??= () => streamClock.Elapsed.TotalMilliseconds;
            Streaming.Spend(streamLoad, buildShipView, Streaming.FrameBudgetMs.Value, streamNow);

            int unloads = 0;
            foreach (int i in streamUnload)
            {
                if (unloads++ >= MaxUnloadsPerFrame) break;
                DropShipView(i);
            }

            // What the view needs now and does not have yet.
            int pending = 0;
            foreach (int i in streamLoad)
            {
                if (shipViews[i] != null) continue;
                double px = ShipLod.ApparentPx(shipLength[i] * ShipLod.ExtentFactor(Fleet.Ships[i]), (shipScene[i] - camera).Length, ppu);
                if (ShipLod.DrawsModel(px)) pending++;
            }
            foreach (var path in activePaths)
                if (!AssetStreamer.Ready(path)) pending++;
            StreamingPending = pending;

            UpdateWarming(dt, camera);
        }

        /// <summary>Builds ship <paramref name="i"/>'s view if its model is in memory (false, and nothing built, if not).</summary>
        bool BuildStreamedShipView(int i)
        {
            if (shipViews[i] != null) return false;
            var ship = Fleet.Ships[i];
            if (!ShipModels.Ready(ship)) return false;
            shipViews[i] = new ShipView(ship, systemRoot);
            if (viewFaction.Length != shipViews.Length) Array.Resize(ref viewFaction, shipViews.Length);
            viewFaction[i] = ship.Faction;
            shipViews[i].SetVisible(false);
            return true;
        }

        /// <summary>Lets go of ship <paramref name="i"/>'s view (its own meshes and materials with it); drawn as its icon
        /// until it is wanted again.</summary>
        void DropShipView(int i)
        {
            var v = shipViews[i];
            if (v == null) return;
            Materials.DestroyOwned(v.Root.transform);
            Destroy(v.Root);
            shipViews[i] = null;
        }

        /// <summary>Warms the systems the view is heading for: the destination of a trip under way, and the far side of
        /// any gate it is near or closing on. Some while after a system is entered, the maps of systems neither drawn
        /// nor warmed are let go of.</summary>
        void UpdateWarming(float dt, DVec3 camera)
        {
            warmTimer -= dt;
            if (releaseInS > 0) releaseInS -= dt;
            if (warmTimer > 0) return;
            warmTimer = WarmEvery;

            streamKeep.Clear();
            foreach (var p in activePaths) streamKeep.Add(p);
            if (Travel.Active && Travel.To != null) { Warm(Travel.To); if (Travel.Hop != null) Warm(Travel.Hop); }
            if (nearGate != null) Warm(nearGate.Destination);
            double ring = Scale.ShipLength(GateView.DiameterKm);
            foreach (var gate in ActiveSystem.Gates)
            {
                if (gate.Destination == null) continue;
                var at = Scale.MapNear(Ephemeris, bodyScene, Catalog.Root.Index, gate.PositionKm);
                if (Streaming.ShouldWarm(camera, streamVelocity, at, ring * WarmGateDiameters)) Warm(gate.Destination);
            }
            if (releasePending && releaseInS <= 0)
            {
                releasePending = false;
                AssetStreamer.ReleaseExcept(streamKeep);
            }
        }

        void Warm(StarSystem system)
        {
            if (system == null) return;
            foreach (var path in AssetStreamer.SystemPaths(system))
            {
                streamKeep.Add(path);
                AssetStreamer.Warm(path);
            }
        }
    }
}
