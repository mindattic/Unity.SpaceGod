using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Unity.SpaceGod.Core;
using UnityEngine;

namespace Unity.SpaceGod
{
    /// <summary>
    /// The helm's own FTL drive, I-War 2 style (U.SPC-T417, the rules in <see cref="HelmFtl"/>). At the helm of a capital
    /// ship or a survey hull, the nose is put on a star, a planet or a beacon (the HUD names what it is on), Enter spools
    /// the drive, and once charged it engages: to somewhere in the same system the hull runs along the line under the
    /// drive's own speed profile and drops out beside it; to another star the hull itself goes out into open space with
    /// the rest of the traffic between the stars (<see cref="DeepSpace"/>), the view goes over to the stars sliding by, and
    /// it drops out at the edge of that star's well, the helm back in hand. The clock runs fast enough that the charge,
    /// the run and the crossing each take seconds to a minute or so, and slows again after. Small craft carry no drive.
    /// </summary>
    public sealed partial class UnitySpaceApp
    {
        /// <summary>The helm's FTL drive: its charge and any jump within the system.</summary>
        public HelmFtl Ftl { get; } = new HelmFtl();

        /// <summary>What the nose is on, that the drive could be spooled for (null: nothing, or a hull with no drive).</summary>
        public FtlTarget FtlPointed { get; private set; }

        /// <summary>The player's hull out between the stars under its own drive, or null.</summary>
        public Cruise FtlCruise { get; private set; }

        /// <summary>Whether the player's hull is out between the stars under its own drive (the helm is back at the far end).</summary>
        public bool InFtlCruise => FtlCruise != null;

        /// <summary>Whether a jump within the system is under way at the helm.</summary>
        public bool FtlRunning => IsPiloting && Ftl.Stage == FtlStage.Engaged;

        /// <summary>The hull flown out between the stars (the same object all the way, see <see cref="Fleet.LetGo"/>).</summary>
        Ship ftlShip;

        /// <summary>The warp the clock ran at before the drive was spooled (back to it after), or -1.</summary>
        int ftlWarpBefore = -1;

        /// <summary>Whether the system bound for is already built (hidden), part way across.</summary>
        bool ftlDestinationBuilt;

        /// <summary>What there is to point at here, gathered now and then rather than every frame.</summary>
        List<FtlTarget> ftlTargets;
        StarSystem ftlTargetsFor;
        int ftlTargetsChart = -1;
        float ftlTargetsAge;

        /// <summary>The charge, a run within the system and a crossing to another star each run at the slowest warp that gets them over in about this many real seconds.</summary>
        const double FtlSpoolRealSeconds = 8, FtlRunRealSeconds = 20, FtlCruiseRealSeconds = 45;

        /// <summary>Real seconds left of the charge or of the trip at the clock's present rate.</summary>
        public double FtlSecondsLeft
        {
            get
            {
                double jd = Clock.Jd, end;
                if (InFtlCruise) end = FtlCruise.ArriveJd;
                else if (Ftl.Stage == FtlStage.Spooling) end = Ftl.ChargedJd;
                else if (Ftl.Stage == FtlStage.Engaged) end = Ftl.ArriveJd;
                else return 0;
                if (double.IsInfinity(end) || double.IsNaN(end)) return 0;
                return Math.Max(0, end - jd) * Astro.SecondsPerDay / Math.Max(Clock.Warp.SimSecondsPerSecond, 1e-9);
            }
        }

        /// <summary>A run within the system's speed as a share of its peak (0 at either end), for the streaks and the view.</summary>
        public float FtlRunSpeed => FtlRunning ? Mathf.Sin(Mathf.PI * (float)Ftl.Progress(Clock.Jd)) : 0;

        /// <summary>Where the hull is, km from the system's root.</summary>
        DVec3 PilotRootKm => Ephemeris.Helio(pilotFrameBody) + pilotOffsetKm;

        // ---------------------------------------------------------------- at the helm

        /// <summary>
        /// A frame of the drive at the helm, after the hull has flown: what the nose is on, Enter to spool (again to call it
        /// off), the charge held or lost, and once charged the jump. True when the hull has left the system this frame.
        /// </summary>
        bool UpdateHelmFtl(Ship ship)
        {
            double jd = Clock.Jd;
            bool canJump = HelmFtl.Refusal(ship) == null;
            FtlPointed = canJump && Ftl.Stage == FtlStage.Idle ? PointedTarget(PilotRootKm, pilot.Forward, jd) : null;

            if (Input.EngageFtl)
            {
                if (Ftl.Stage == FtlStage.Spooling)
                {
                    Ftl.Abort("Charge called off");
                    EndFtlWarp();
                    ShowToast("FTL charge called off");
                    return false;
                }
                if (!Ftl.Spool(ship, ActiveSystem, FtlPointed, PilotRootKm, pilot.Forward, ship.Battle != null, jd))
                {
                    ShowToast(Ftl.AbortReason);
                    return false;
                }
                if (ftlWarpBefore < 0) ftlWarpBefore = Clock.WarpIndex;
                Clock.SetWarp(SimClock.WarpToCover((Ftl.ChargedJd - jd) * Astro.SecondsPerDay, FtlSpoolRealSeconds, ftlWarpBefore));
                var t = Ftl.Target;
                ShowToast("FTL DRIVE SPOOLING for " + t.Name.ToUpperInvariant() + ", " + Distance(t, jd) + "  (hold the nose on it; Enter calls it off)");
                return false;
            }

            if (Ftl.Stage != FtlStage.Spooling) return false;
            switch (Ftl.Step(ActiveSystem, PilotRootKm, pilot.Forward, ship.Battle != null, jd))
            {
                case FtlEvent.Aborted:
                    EndFtlWarp();
                    ShowToast(Ftl.AbortReason);
                    return false;
                case FtlEvent.Charged:
                    return Ftl.Target.Interstellar(ActiveSystem) ? EngageCruise(ship, jd) : EngageRun(ship, jd);
                default:
                    return false;
            }
        }

        /// <summary>The target dead ahead of the nose, from a list of what is here brought up to date every second or so.</summary>
        FtlTarget PointedTarget(DVec3 rootKm, DVec3 forward, double jd)
        {
            ftlTargetsAge -= Time.unscaledDeltaTime;
            if (ftlTargets == null || ftlTargetsFor != ActiveSystem || ftlTargetsChart != Galaxy.ChartVersion || ftlTargetsAge <= 0)
            {
                ftlTargets = FtlTargets.All(Galaxy, ActiveSystem);
                ftlTargetsFor = ActiveSystem;
                ftlTargetsChart = Galaxy.ChartVersion;
                ftlTargetsAge = 1f;
            }
            return HelmFtl.Pointed(ftlTargets, ActiveSystem, rootKm, forward, jd);
        }

        /// <summary>How far <paramref name="t"/> is from the hull, in light-years, AU or km as suits.</summary>
        public string Distance(FtlTarget t, double jd)
        {
            if (t == null || !IsPiloting) return "";
            HelmFtl.Bearing(t, ActiveSystem, PilotRootKm, jd, out double km);
            return DistanceText(km);
        }

        static string DistanceText(double km)
        {
            var inv = CultureInfo.InvariantCulture;
            if (km >= 0.01 * Astro.LightYearKm) return (km / Astro.LightYearKm).ToString("0.00", inv) + " ly";
            if (km >= 0.01 * Astro.AuKm) return (km / Astro.AuKm).ToString("0.00", inv) + " AU";
            return km.ToString("N0", inv) + " km";
        }

        /// <summary>Charged, for somewhere in this system: the run begins, the clock fast enough to get it over in seconds.</summary>
        bool EngageRun(Ship ship, double jd)
        {
            Ftl.EngageInSystem(ActiveSystem, PilotRootKm, jd);
            // The run is flown in the root's frame: a body's frame would be carried along under the hull as it ran.
            pilotFrameBody = Catalog.Root.Index;
            pilotOffsetKm = Ftl.StartKm - Ephemeris.Helio(pilotFrameBody);
            EndSalvage(null);
            SalvageApproach = null;
            World.Pursuit?.ReleaseAll(jd);
            pilotCruise = default;
            Clock.SetWarp(SimClock.WarpToCover((Ftl.ArriveJd - jd) * Astro.SecondsPerDay, FtlRunRealSeconds, Math.Max(0, ftlWarpBefore)));
            GateFlash = 0.6f;
            fovKick = GateFovKick;
            ShowToast("FTL ENGAGED for " + Ftl.Target.Name.ToUpperInvariant() + ", " + DistanceText((Ftl.EndKm - Ftl.StartKm).Length));
            return false;
        }

        /// <summary>
        /// A frame of a run within the system: the hull is where the drive has it (so the sensors, the law and the HUD see it
        /// there), nose on its way, and at the end it drops out beside the target at rest, the helm back in hand. True while
        /// the run has the hull (nothing else flies it this frame).
        /// </summary>
        bool UpdateFtlRun(Ship ship)
        {
            if (Ftl.Stage != FtlStage.Engaged) return false;
            double jd = Clock.Jd;
            var dir = Ftl.Direction;
            bool arrived = Ftl.Step(ActiveSystem, PilotRootKm, dir, false, jd) == FtlEvent.Arrived;
            var at = arrived ? Ftl.EndKm : Ftl.PositionKmAt(jd);
            pilotOffsetKm = at - Ephemeris.Helio(pilotFrameBody);
            pilotVelKmS = dir * Ftl.SpeedKmS(jd);
            pilot.Forward = dir;
            var level = pilot.Up - dir * DVec3.Dot(pilot.Up, dir);
            pilot.Up = level.LengthSquared > 1e-9 ? level.Normalized : PilotState.LevelUp(dir);
            pilot.AngularVelocity = DVec3.Zero;
            ResetPilotAim();
            World.Sensors?.UpdatePilot(ship, at, true, jd);
            if (!arrived) return true;

            // Out beside it: in its frame if it is a body, at rest there, the drive ready again.
            var target = Ftl.Target;
            Ftl.Arrived();
            if (target.Kind == FtlTargetKind.Body && target.System == ActiveSystem) pilotFrameBody = target.Body;
            pilot = PilotState.At(at - Ephemeris.Helio(pilotFrameBody), DVec3.Zero, dir);
            var up = level.LengthSquared > 1e-9 ? level.Normalized : PilotState.LevelUp(dir);
            pilot.Up = up;
            ResetPilotAim();
            PinWherePiloted(ship);
            EndFtlWarp();
            GateFlash = 0.6f;
            LaneExitFade = 1;
            fovKick = GateFovKick * 0.5f;
            ShowToast("Dropped out of FTL beside " + target.Name.ToUpperInvariant() + ", " + Distance(target, Clock.Jd) + " off");
            return true;
        }

        // ---------------------------------------------------------------- between the stars

        /// <summary>
        /// Charged, for another star: the hull itself goes out into open space under its own drive, the helm let go of
        /// for the crossing (nothing to steer between the stars yet), and the view goes over to the stars. True if it went.
        /// </summary>
        bool EngageCruise(Ship ship, double jd)
        {
            var from = ActiveSystem;
            var rootKm = PilotRootKm;
            var c = Ftl.EngageInterstellar(Galaxy, from, ship, rootKm, jd);
            if (c == null)
            {
                EndFtlWarp();
                ShowToast(Ftl.AbortReason);
                return false;
            }
            EndSalvage(null);
            SalvageApproach = null;
            // The system left no longer sees the pilot, nor holds a patrol's hail or a chase on it.
            World.PilotedShip = null;
            World.Sensors?.UpdatePilot(null, default, false, jd);
            World.Law?.UpdatePilot(null, 0, default, default, jd);
            World.Pursuit?.ReleaseAll(jd);
            Piloted = Target.None;
            pilotLockTarget = Target.None;
            pilotLockUnit = null;
            pilotBoosting = false;
            pilotCruise = default;
            TankerDueS = -1;
            SpaceCamera.PilotLockTarget = Target.None;
            SpaceCamera.PilotView = null;
            SpaceCamera.ExitFocus();
            Select(Target.None);

            FtlCruise = c;
            ftlShip = ship;
            ftlDestinationBuilt = false;
            Clock.SetWarp(SimClock.WarpToCover((c.ArriveJd - jd) * Astro.SecondsPerDay, FtlCruiseRealSeconds, Math.Max(0, ftlWarpBefore)));
            // What the far system is drawn with starts loading now, in the background (U.SPC-T414).
            Rendering.AssetStreamer.WarmSystem(c.To);
            SetSystemVisible(false);
            Interstellar.Visible = true;
            GateFlash = 1;
            fovKick = GateFovKick;
            ShowToast("FTL ENGAGED for " + c.ToName.ToUpperInvariant() + ", " + c.Profile.Ly.ToString("0.00", CultureInfo.InvariantCulture) +
                      " ly through open space");
            return true;
        }

        /// <summary>
        /// A frame of the crossing: the charts follow the hull, the far system is built (hidden) half way, and once the hull
        /// has dropped out into it the helm is taken again there; lost out there, the session ends.
        /// </summary>
        void UpdateFtlCruise()
        {
            var c = FtlCruise;
            double jd = Clock.Jd;
            if (Galaxy.DeepSpace.Cruises.Contains(c))
            {
                // The galaxy is charted and built about the hull as it goes (U.SPC-T413).
                Galaxy.Viewpoint = c.PositionAt(jd);
                double total = c.ArriveJd - c.LegJd;
                double share = total > 0 && !double.IsInfinity(total) ? (jd - c.LegJd) / total : 0;
                if (!ftlDestinationBuilt && share >= 0.5 && c.To != null)
                {
                    Activate(c.To);
                    SetSystemVisible(false);
                    Interstellar.Visible = true;
                    ftlDestinationBuilt = true;
                }
                return;
            }

            var ship = ftlShip;
            var to = c.To;
            EndFtlCruise();
            if (ship == null || ship.Destroyed || to == null || !to.World.Fleet.Ships.Contains(ship))
            {
                // Destroyed out there (a fight between the stars): nothing comes out the far end.
                SetSystemVisible(true);
                FocusSystemOverview();
                ShowToast("The " + (ship?.Name ?? "ship") + " was lost between the stars");
                return;
            }
            if (ActiveSystem != to) Activate(to);
            SetSystemVisible(true);
            Interstellar.Visible = false;
            EnterPiloting(Target.OfShip(ship));
            EndFtlWarp();
            // The far side streams in behind the drop-out's own flash and fold (U.SPC-T414).
            BeginHelmGateCover();
            ShowToast("Dropped out of FTL at the edge of " + to.Name.ToUpperInvariant() + ", " +
                      DistanceText(ship.Orbit.Center.Length) + " from its star  (in from " + (c.From?.Name ?? "open space") + ")");
        }

        /// <summary>The view between the stars during the crossing: from where the hull is, looking the way it goes.</summary>
        void UpdateFtlCruiseView(float dt)
        {
            var c = FtlCruise;
            if (c == null) return;
            var heading = c.Direction.SwapYZ;
            var forward = new Vector3((float)heading.X, (float)heading.Y, (float)heading.Z);
            if (forward.sqrMagnitude < 1e-12f) forward = Vector3.forward;
            var want = Quaternion.LookRotation(forward, Mathf.Abs(forward.normalized.y) > 0.98f ? Vector3.forward : Vector3.up);
            var rotation = Quaternion.Slerp(SpaceCamera.Rotation, want, 1f - Mathf.Exp(-2.5f * dt));
            SpaceCamera.SetRotation(rotation);
            Interstellar.Visible = true;
            Interstellar.Update(c.PositionAt(Clock.Jd).Ly, rotation, 1f);
        }

        /// <summary>The crossing is over (arrived, or lost): back to the view of a system.</summary>
        void EndFtlCruise()
        {
            FtlCruise = null;
            ftlShip = null;
            ftlDestinationBuilt = false;
            Interstellar.Visible = false;
        }

        /// <summary>The clock back to the pace it had before the drive was spooled.</summary>
        void EndFtlWarp()
        {
            if (ftlWarpBefore >= 0) Clock.SetWarp(ftlWarpBefore);
            ftlWarpBefore = -1;
        }

        /// <summary>The helm let go of mid-charge or mid-run: the drive stops where it is.</summary>
        void StopFtl()
        {
            if (Ftl.Stage == FtlStage.Idle) return;
            Ftl.Reset();
            EndFtlWarp();
        }
    }
}
