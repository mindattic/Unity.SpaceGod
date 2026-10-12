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

        const float LeaveSeconds = 2.2f, CrossSeconds = 3.8f, ArriveSeconds = 2.6f;

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
            Banner = "LEAVING " + From.Name.ToUpperInvariant();
        }

        /// <summary>How far out the camera pulls before the system is a point: past its farthest member.</summary>
        static double LeaveSpanKm(StarSystem s) => s.Catalog.Root.Kind == BodyKind.Barycentre ? 30000 * Astro.AuKm : 400 * Astro.AuKm;

        /// <summary>Skips to the end: the destination's overview.</summary>
        public void Finish()
        {
            if (!Active) return;
            if (app.ActiveSystem != To) app.Activate(To);
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
            var look = Quaternion.LookRotation(ToVector(-dir), Vector3.up);
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
            Banner = Galaxy.DistanceLy(From, To).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " LIGHT-YEARS TO " + To.Name.ToUpperInvariant();
        }

        void UpdateCross(float dt)
        {
            t = Mathf.Min(1, t + dt / CrossSeconds);
            var travel = (To.PositionLy - From.PositionLy).Normalized;
            // Turn toward the destination first, then glide: rotation leads, position follows.
            float turn = Smoother(Mathf.Clamp01(t / 0.4f));
            float glide = Smoother(Mathf.Clamp01((t - 0.18f) / 0.82f));
            var lookAtDest = Quaternion.LookRotation(ToVector(travel.SwapYZ), Vector3.up);
            var rotation = Quaternion.Slerp(crossStartRotation, lookAtDest, turn);
            CameraLy = DVec3.Lerp(crossStart, crossEnd, glide);
            app.SpaceCamera.SetRotation(rotation);
            app.Interstellar.Update(CameraLy, rotation, 1f);
            if (t < 1) return;

            // Arrive: the destination takes over, the camera far out along the line of travel.
            app.Activate(To);
            app.Interstellar.Visible = false;
            app.SetSystemVisible(true);
            arriveDirection = -travel.SwapYZ;
            arriveTo = app.OverviewDistance();
            arriveFrom = Math.Max(arriveTo * 30, app.Scale.LocalDistance(app.Catalog.Root, LeaveSpanKm(To)));
            // End where the overview focus will put us, so handing over is seamless.
            var flat = new Vector3((float)arriveDirection.X, 0, (float)arriveDirection.Z);
            if (flat.sqrMagnitude < 1e-6f) flat = Vector3.back;
            flat.Normalize();
            float el = SolarSystemApp.OverviewPitch * Mathf.Deg2Rad;
            var endDir = new Vector3(flat.x * Mathf.Cos(el), Mathf.Sin(el), flat.z * Mathf.Cos(el));
            arriveEndRotation = Quaternion.LookRotation(-endDir, Vector3.up);
            arriveEndDirection = new DVec3(endDir.x, endDir.y, endDir.z);
            crossStartRotation = rotation;
            Stage = Leg.Arrive;
            t = 0;
            Banner = "ARRIVING AT " + To.Name.ToUpperInvariant();
        }

        DVec3 arriveEndDirection;

        void UpdateArrive(float dt)
        {
            t = Mathf.Min(1, t + dt / ArriveSeconds);
            float e = Smoother(t);
            var root = app.BodyScene(0);
            var dir = Slerp(arriveDirection, arriveEndDirection, e);
            double d = Math.Exp(Math.Log(arriveFrom) + (Math.Log(arriveTo) - Math.Log(arriveFrom)) * e);
            var look = Quaternion.LookRotation(ToVector(-dir), Vector3.up);
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
