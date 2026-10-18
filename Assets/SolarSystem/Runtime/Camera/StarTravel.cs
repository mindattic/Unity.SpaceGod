using System;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Cameras
{
    /// <summary>
    /// The camera's trip from one star system to another, in three legs:
    ///  1. Leave: pull straight back from the system until it shrinks to a point.
    ///  2. Cross: in the interstellar view, turn toward the destination star and glide across the
    ///     light-years between (eased in and out), with the neighbouring stars sliding past.
    ///  3. Arrive: the destination system takes over and the camera falls in from far out to its overview.
    /// The simulation of both systems carries on throughout; this is only the view moving.
    /// </summary>
    public sealed class StarTravel
    {
        public enum Leg
        {
            Leave,
            Cross,
            Arrive,
        }

        public bool Active { get; private set; }
        public Leg Stage { get; private set; }
        public StarSystem From { get; private set; }
        public StarSystem To { get; private set; }

        /// <summary>The view's position between the stars (light-years) during the crossing.</summary>
        public DVec3 CameraLy { get; private set; }

        /// <summary>A line for the HUD describing what is happening.</summary>
        public string Banner { get; private set; }

        /// <summary>0..1 through the whole trip.</summary>
        public float Overall => Active ? Mathf.Clamp01(((int)Stage + t) / 3f) : 0;

        /// <summary>How hard the view is going to warp, 0..1: building as the system falls away, swelling with the glide
        /// across, dying away on arrival. Drives the warp lines and stellar dust.</summary>
        public float Warp
        {
            get
            {
                if (!Active) return 0;
                switch (Stage)
                {
                    case Leg.Leave: return 0.6f * Smoother(Mathf.Clamp01((t - 0.55f) / 0.45f));
                    case Leg.Cross: return 0.6f + 0.4f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - 0.18f) / 0.82f));
                    default: return 0.6f * (1 - Smoother(Mathf.Clamp01(t / 0.5f)));
                }
            }
        }

        /// <summary>The way the crossing heads, ecliptic: from the star left toward the one bound for.</summary>
        public DVec3 Heading => Active ? (To.PositionLy - From.PositionLy).Normalized : DVec3.Zero;

        const float LeaveSeconds = 2.2f, CrossSeconds = 3.8f, ArriveSeconds = 3.4f;

        /// <summary>The crossing starts and ends this far from each star (light-years).</summary>
        const double StandOffLy = 0.22;

        readonly SolarSystemApp app;
        float t;
        DVec3 leaveStart;
        double leaveFrom, leaveTo;
        Quaternion startRotation, crossStartRotation;
        Vector3 forward;
        DVec3 crossStart, crossEnd;
        DVec3 arriveDirection;
        double arriveFrom, arriveTo;
        Quaternion arriveEndRotation;

        public StarTravel(SolarSystemApp app) => this.app = app;

        public void Begin(StarSystem to)
        {
            if (to == null || to == app.ActiveSystem) return;
            From = app.ActiveSystem;
            To = to;
            Active = true;
            Stage = Leg.Leave;
            t = 0;
            var root = app.BodyScene(0);
            leaveStart = app.SpaceCamera.ScenePosition - root;
            if (leaveStart.LengthSquared < 1) leaveStart = new DVec3(0, 0.4, -1);
            leaveFrom = leaveStart.Length;
            leaveTo = Math.Max(leaveFrom * 25, app.Scale.LocalDistance(app.Catalog.Root, LeaveSpanKm(From)));
            startRotation = app.SpaceCamera.Rotation;
            destinationBuilt = false;
            // The view keeps the plane it left until it passes into the destination's Oort cloud, then (with
            // auto-reorient on) swings to the destination's own plane before arriving.
            fromUp = app.SpaceCamera.UpFrame * Vector3.up;
            toUp = app.AutoReorient ? SolarSystemApp.PlaneNormal(To) : Vector3.up;
            oortLy = SolarSystemApp.OortCloudLy(To);
            Banner = To.Name.ToUpperInvariant() + "   ·   " + Galaxy.DistanceLy(From, To).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " LIGHT-YEARS";
        }

        /// <summary>Whether the destination's views are already built (hidden, part way across).</summary>
        bool destinationBuilt;

        /// <summary>Up for the view: the plane left behind, the destination's plane, and the destination's Oort cloud radius (ly).</summary>
        Vector3 fromUp = Vector3.up, toUp = Vector3.up;
        double oortLy;

        /// <summary>0 outside the destination's Oort cloud, easing to 1 by the end of the crossing.</summary>
        float ReorientShare()
        {
            double dist = (To.PositionLy - CameraLy).Length;
            double span = Math.Max(oortLy - StandOffLy, 1e-6);
            return Smoother(Mathf.Clamp01((float)((oortLy - dist) / span)));
        }

        /// <summary>How far out the camera pulls before the system is a point: past its farthest member.</summary>
        static double LeaveSpanKm(StarSystem s) =>
            s.Catalog.Root.Name == "Sun" ? 400 * Astro.AuKm : Math.Max(s.ExtentKm * 2.5, 40 * Astro.AuKm);

        /// <summary>Skips to the end: the destination's overview.</summary>
        public void Finish()
        {
            if (!Active) return;
            if (app.ActiveSystem != To) app.Activate(To);
            Views.BodyView.CoronaPresence = 1f;
            Active = false;
            app.Interstellar.Visible = false;
            app.FocusSystemOverview();
        }

        public void Update(float dt)
        {
            if (!Active) return;
            switch (Stage)
            {
                case Leg.Leave: UpdateLeave(dt); break;
                case Leg.Cross: UpdateCross(dt); break;
                default: UpdateArrive(dt); break;
            }
        }

        void UpdateLeave(float dt)
        {
            t = Mathf.Min(1, t + dt / LeaveSeconds);
            float e = Smoother(t);
            var root = app.BodyScene(0);
            var dir = leaveStart / leaveFrom;
            double d = Math.Exp(Math.Log(leaveFrom) + (Math.Log(leaveTo) - Math.Log(leaveFrom)) * e);
            var look = Quaternion.LookRotation(ToVector(-dir), fromUp);
            app.SpaceCamera.SetPose(root + dir * d, Quaternion.Slerp(startRotation, look, Smoother(Mathf.Min(1, t * 1.6f))));
            if (t < 1) return;

            // Hand over to the view between the stars, looking the same way at the same star.
            forward = app.SpaceCamera.Rotation * Vector3.forward;
            var fwdEcliptic = new DVec3(forward.x, forward.z, forward.y);
            crossStart = From.PositionLy - fwdEcliptic * StandOffLy;
            var travel = (To.PositionLy - From.PositionLy).Normalized;
            crossEnd = To.PositionLy - travel * StandOffLy;
            crossStartRotation = app.SpaceCamera.Rotation;
            CameraLy = crossStart;
            app.Interstellar.Visible = true;
            app.SetSystemVisible(false);
            Stage = Leg.Cross;
            t = 0;
        }

        void UpdateCross(float dt)
        {
            t = Mathf.Min(1, t + dt / CrossSeconds);
            var travel = (To.PositionLy - From.PositionLy).Normalized;
            // Turn toward the destination first, then glide: rotation leads, position follows.
            float turn = Smoother(Mathf.Clamp01(t / 0.4f));
            float glide = Smoother(Mathf.Clamp01((t - 0.18f) / 0.82f));
            CameraLy = DVec3.Lerp(crossStart, crossEnd, glide);
            var up = Vector3.Slerp(fromUp, toUp, ReorientShare());
            var lookAtDest = Quaternion.LookRotation(ToVector(travel.SwapYZ), up);
            var rotation = Quaternion.Slerp(crossStartRotation, lookAtDest, turn);
            app.SpaceCamera.SetRotation(rotation);
            app.Interstellar.Update(CameraLy, rotation, 1f);
            // Half way across, while nothing of either system is on screen, the destination is built (hidden), so
            // nothing has to be made at the moment it comes into view.
            if (!destinationBuilt && t >= 0.5f)
            {
                app.Activate(To);
                app.SetSystemVisible(false);
                destinationBuilt = true;
            }
            if (t < 1) return;

            // Arrive: the destination takes over, the camera so far out along the line of travel that the whole system
            // is one point where its star was, and it opens out from there as the camera falls in.
            if (!destinationBuilt) app.Activate(To);
            // The view arrives level with the destination's plane (or the ecliptic with auto-reorient off).
            var frame = Quaternion.FromToRotation(Vector3.up, toUp);
            app.SpaceCamera.SetUpFrame(frame);
            Views.BodyView.CoronaPresence = ArrivalCorona;
            app.Interstellar.Visible = false;
            app.SetSystemVisible(true);
            arriveDirection = -travel.SwapYZ;
            arriveTo = app.OverviewDistance();
            double span = app.Scale.LocalDistance(app.Catalog.Root, LeaveSpanKm(To));
            arriveFrom = Math.Min(Math.Max(Math.Max(arriveTo * 30, span), span * app.PixelsPerUnitAtUnitDistance() / PointPixels), MaxArriveDistance);
            // End where the overview focus will put us, so handing over is seamless: the same elevation, measured
            // from the destination's plane.
            var local = Quaternion.Inverse(frame) * ToVector(arriveDirection);
            var flat = new Vector3(local.x, 0, local.z);
            if (flat.sqrMagnitude < 1e-6f) flat = Vector3.back;
            flat.Normalize();
            float el = SolarSystemApp.OverviewPitch * Mathf.Deg2Rad;
            var endDir = frame * new Vector3(flat.x * Mathf.Cos(el), Mathf.Sin(el), flat.z * Mathf.Cos(el));
            arriveEndRotation = Quaternion.LookRotation(-endDir, toUp);
            arriveEndDirection = new DVec3(endDir.x, endDir.y, endDir.z);
            crossStartRotation = rotation;
            Stage = Leg.Arrive;
            t = 0;
        }

        /// <summary>How wide, in pixels, the whole destination system is when it first takes over from its star.</summary>
        const double PointPixels = 6;

        /// <summary>The destination star's corona on taking over, as a share of its usual size: about the star point's.</summary>
        const float ArrivalCorona = 0.25f;

        /// <summary>Kept well inside the camera's far plane (5e8 scene units).</summary>
        const double MaxArriveDistance = 2e8;

        DVec3 arriveEndDirection;

        void UpdateArrive(float dt)
        {
            t = Mathf.Min(1, t + dt / ArriveSeconds);
            float e = Smoother(t);
            Views.BodyView.CoronaPresence = Mathf.Lerp(ArrivalCorona, 1f, e);
            var root = app.BodyScene(0);
            var dir = Slerp(arriveDirection, arriveEndDirection, e);
            double d = Math.Exp(Math.Log(arriveFrom) + (Math.Log(arriveTo) - Math.Log(arriveFrom)) * e);
            var look = Quaternion.LookRotation(ToVector(-dir), toUp);
            app.SpaceCamera.SetPose(root + dir * d, Quaternion.Slerp(crossStartRotation, look, Smoother(Mathf.Min(1, t * 2f))));
            if (t < 1) return;
            Active = false;
            Banner = null;
            app.FocusSystemOverview();
        }

        static DVec3 Slerp(DVec3 a, DVec3 b, float t)
        {
            var v = Vector3.Slerp(ToVector(a), ToVector(b), t);
            return new DVec3(v.x, v.y, v.z);
        }

        static float Smoother(float x) => x * x * x * (x * (x * 6 - 15) + 10);
        static Vector3 ToVector(DVec3 v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);
    }
}
