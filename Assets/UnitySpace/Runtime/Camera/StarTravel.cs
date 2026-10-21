using System;
using Unity.SpaceGod.Core;
using UnityEngine;

namespace Unity.SpaceGod.Cameras
{
    /// <summary>
    /// The camera's trip from one star system to another. Where the system left has a jump gate on the way, the
    /// camera goes through it (paced and chosen by <see cref="GateJump"/>):
    ///  1. Leave: down to the gate that leads toward the destination, lined up on its ring with the star behind,
    ///     then a plunge straight through it.
    ///  2. Cross: a short spell inside the gate, warp lines streaming past and nothing of either system on screen.
    ///  3. Arrive: out of the partner gate in the destination, looking back at it and its star, pulling back to the
    ///     system's overview.
    /// Where there is no gate, the old way across, in the same three legs:
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

        /// <summary>Whether this trip goes through the jump gates (false: across the interstellar view).</summary>
        public bool ThroughGate { get; private set; }

        /// <summary>The view's position between the stars (light-years) during the crossing.</summary>
        public DVec3 CameraLy { get; private set; }

        /// <summary>A line for the HUD describing what is happening.</summary>
        public string Banner { get; private set; }

        /// <summary>0..1 through the whole trip.</summary>
        public float Overall => !Active ? 0 : ThroughGate ? (float)GateJump.Overall(phase, t) : Mathf.Clamp01(((int)Stage + t) / 3f);

        /// <summary>How hard the view is going to warp, 0..1: building as the system falls away, swelling with the glide
        /// across, dying away on arrival. Drives the warp lines and stellar dust.</summary>
        public float Warp
        {
            get
            {
                if (!Active) return 0;
                if (ThroughGate) return (float)GateJump.Warp(phase, t);
                switch (Stage)
                {
                    case Leg.Leave: return 0.6f * Smoother(Mathf.Clamp01((t - 0.55f) / 0.45f));
                    case Leg.Cross: return 0.6f + 0.4f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - 0.18f) / 0.82f));
                    default: return 0.6f * (1 - Smoother(Mathf.Clamp01(t / 0.5f)));
                }
            }
        }

        /// <summary>The flash of going through a gate's ring, 0..1 (the HUD washes the screen with it).</summary>
        public float GateFlash => Active && ThroughGate ? (float)GateJump.Flash(phase, t) : 0;

        /// <summary>The glow of the inside of a gate, 0..1 (the HUD tints the screen with it).</summary>
        public float GateGlow => Active && ThroughGate ? (float)GateJump.Glow(phase, t) : 0;

        /// <summary>How much wider than usual the view opens going through a gate, degrees.</summary>
        public float FovKick => Active && ThroughGate ? (float)GateJump.FovKick(phase, t) : 0;

        /// <summary>How near the camera is to the ring of the gate it is going through (scene units), for the near
        /// clip plane; infinite when no ring is near.</summary>
        public double GateClearance => Active && ThroughGate ? gateClearance : double.MaxValue;

        /// <summary>The way the crossing heads, ecliptic: from the star left toward the one bound for, or through a gate
        /// the way the view looks.</summary>
        public DVec3 Heading
        {
            get
            {
                if (!Active) return DVec3.Zero;
                if (!ThroughGate) return (To.PositionLy - From.PositionLy).Normalized;
                var f = app.SpaceCamera.Rotation * Vector3.forward;
                return new DVec3(f.x, f.z, f.y);
            }
        }

        const float LeaveSeconds = 2.2f, CrossSeconds = 3.8f, ArriveSeconds = 3.4f;

        /// <summary>The crossing starts and ends this far from each star (light-years).</summary>
        const double StandOffLy = 0.22;

        readonly UnitySpaceApp app;
        float t;
        DVec3 leaveStart;
        double leaveFrom, leaveTo;
        Quaternion startRotation, crossStartRotation;
        Vector3 forward;
        DVec3 crossStart, crossEnd;
        DVec3 arriveDirection;
        double arriveFrom, arriveTo;
        Quaternion arriveEndRotation;
        float arriveCorona;

        /// <summary>Through the gates: the gate left by, the one come out of, where in the trip the view is, and how near
        /// it is to a ring.</summary>
        JumpGate departure, arrival;
        GateJump.Phase phase;
        double gateClearance = double.MaxValue;

        public StarTravel(UnitySpaceApp app) => this.app = app;

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
            toUp = app.AutoReorient ? UnitySpaceApp.PlaneNormal(To) : Vector3.up;
            oortLy = UnitySpaceApp.OortCloudLy(To);
            Banner = To.Name.ToUpperInvariant() + "   ·   " + Galaxy.DistanceLy(From, To).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " LIGHT-YEARS";
            // Through the gate leading that way, if this system has one; the old way across if not.
            ThroughGate = GateJump.TryChoose(app.Galaxy.Network, From, To, out departure, out arrival);
            phase = GateJump.Phase.Approach;
            gateClearance = double.MaxValue;
            if (ThroughGate) Banner += "   ·   VIA " + departure.Name.ToUpperInvariant();
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
            Banner = null;
            app.Interstellar.Visible = false;
            // Built part way across, the destination is still hidden.
            app.SetSystemVisible(true);
            app.FocusSystemOverview();
        }

        public void Update(float dt)
        {
            if (!Active) return;
            if (ThroughGate)
            {
                UpdateGate(dt);
                return;
            }
            switch (Stage)
            {
                case Leg.Leave: UpdateLeave(dt); break;
                case Leg.Cross: UpdateCross(dt); break;
                default: UpdateArrive(dt); break;
            }
        }

        // ---------------------------------------------------------------- through the gates

        void UpdateGate(float dt)
        {
            if (phase == GateJump.Phase.Emerge)
            {
                UpdateArrive(dt);
                if (Active)
                {
                    GateScene(arrival, out var exit, out _, out double exitRing);
                    gateClearance = Clearance(exit, exitRing);
                }
                return;
            }
            t = Mathf.Min(1, t + dt / (float)GateJump.Seconds(phase));
            switch (phase)
            {
                case GateJump.Phase.Approach: UpdateApproach(); break;
                case GateJump.Phase.Plunge: UpdatePlunge(); break;
                default: UpdateTunnel(); break;
            }
        }

        /// <summary>Down to the gate: swinging round the system's root onto the line of the ring while dropping to just in
        /// front of it, turning to look at it.</summary>
        void UpdateApproach()
        {
            GateScene(departure, out var gate, out var axis, out double ring);
            var root = app.BodyScene(0);
            double lineUp = (gate - root).Length + ring * GateJump.LineUpDiameters;
            var dir = Slerp(leaveStart / leaveFrom, axis, (float)GateJump.ApproachSwing(t));
            double d = Math.Exp(Math.Log(leaveFrom) + (Math.Log(lineUp) - Math.Log(leaveFrom)) * GateJump.ApproachDescent(t));
            var position = root + dir * d;
            var look = Look(ToVector(gate - position), fromUp);
            app.SpaceCamera.SetPose(position, Quaternion.Slerp(startRotation, look, (float)GateJump.ApproachLook(t)));
            gateClearance = Clearance(gate, ring);
            if (t < 1) return;
            phase = GateJump.Phase.Plunge;
            t = 0;
        }

        /// <summary>Straight through the ring, gathering speed; at the ring the view is inside the gate.</summary>
        void UpdatePlunge()
        {
            GateScene(departure, out var gate, out var axis, out double ring);
            var position = gate + axis * (ring * GateJump.LineUpDiameters * GateJump.PlungeRemaining(t));
            forward = ToVector(-axis);
            app.SpaceCamera.SetPose(position, Look(forward, fromUp));
            gateClearance = Clearance(gate, ring);
            if (t < 1) return;
            app.SetSystemVisible(false);
            gateClearance = double.MaxValue;
            phase = GateJump.Phase.Tunnel;
            Stage = Leg.Cross;
            t = 0;
        }

        /// <summary>Inside the gate: nothing of either system on screen while the destination is built (hidden), the
        /// view's up swinging to the destination's plane; then out of the partner gate.</summary>
        void UpdateTunnel()
        {
            app.SpaceCamera.SetRotation(Look(forward, Vector3.Slerp(fromUp, toUp, Smoother(t))));
            if (!destinationBuilt && t >= GateJump.BuildAt)
            {
                app.Activate(To);
                app.SetSystemVisible(false);
                destinationBuilt = true;
            }
            if (t < 1) return;

            if (!destinationBuilt) app.Activate(To);
            destinationBuilt = true;
            app.SpaceCamera.SetUpFrame(Quaternion.FromToRotation(Vector3.up, toUp));
            Views.BodyView.CoronaPresence = 1f;
            app.SetSystemVisible(true);
            // In the ring of the far gate, looking back through it at the destination's star, and carried out of it
            // and back from there to the overview.
            GateScene(arrival, out var exit, out var axis, out double ring);
            var root = app.BodyScene(0);
            PrepareArrival(axis, Math.Max((exit - root).Length, 1e-9), 1f);
            crossStartRotation = Look(ToVector(-axis), toUp);
            app.SpaceCamera.SetPose(exit, crossStartRotation);
            gateClearance = Clearance(exit, ring);
            phase = GateJump.Phase.Emerge;
            Stage = Leg.Arrive;
            t = 0;
        }

        /// <summary>Where <paramref name="gate"/> (in the active system) is drawn, the way out through its ring (away from
        /// the system's root, along the line to the star it leads to) and how wide the ring is drawn, scene units.</summary>
        void GateScene(JumpGate gate, out DVec3 scene, out DVec3 axis, out double ring)
        {
            var root = app.BodyScene(0);
            scene = app.Scale.MapNear(app.Ephemeris, app.BodyScenes, app.Catalog.Root.Index, gate.PositionKm);
            var off = scene - root;
            axis = off.LengthSquared > 1e-24 ? off.Normalized : gate.PositionKm.Normalized.SwapYZ;
            ring = app.Scale.ShipLength(Views.GateView.DiameterKm);
        }

        /// <summary>How far the camera is from the ring of a gate drawn at <paramref name="gate"/>, never less than a tenth
        /// of its width (in its middle the nearest of it is half a width away).</summary>
        double Clearance(DVec3 gate, double ring) =>
            Math.Max((app.SpaceCamera.ScenePosition - gate).Length - ring * 0.5, ring * 0.1);

        /// <summary>LookRotation that keeps working when the view looks straight along <paramref name="up"/>.</summary>
        static Quaternion Look(Vector3 forward, Vector3 up)
        {
            if (forward.sqrMagnitude < 1e-12f) forward = Vector3.forward;
            if (Mathf.Abs(Vector3.Dot(forward.normalized, up.normalized)) > 0.999f)
                up = Mathf.Abs(forward.normalized.y) < 0.9f ? Vector3.up : Vector3.forward;
            return Quaternion.LookRotation(forward, up);
        }

        // ---------------------------------------------------------------- across the interstellar view

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
            app.SpaceCamera.SetUpFrame(Quaternion.FromToRotation(Vector3.up, toUp));
            Views.BodyView.CoronaPresence = ArrivalCorona;
            app.Interstellar.Visible = false;
            app.SetSystemVisible(true);
            double overview = app.OverviewDistance();
            double span = app.Scale.LocalDistance(app.Catalog.Root, LeaveSpanKm(To));
            double from = Math.Min(Math.Max(Math.Max(overview * 30, span), span * app.PixelsPerUnitAtUnitDistance() / PointPixels), MaxArriveDistance);
            PrepareArrival(-travel.SwapYZ, from, ArrivalCorona);
            crossStartRotation = rotation;
            Stage = Leg.Arrive;
            t = 0;
        }

        /// <summary>Sets up the fall (or pull back) to the overview: from <paramref name="from"/> scene units out along
        /// <paramref name="direction"/> from the destination's root, its star's corona starting at <paramref name="corona"/>
        /// of its usual size.</summary>
        void PrepareArrival(DVec3 direction, double from, float corona)
        {
            var frame = Quaternion.FromToRotation(Vector3.up, toUp);
            arriveDirection = direction;
            arriveTo = app.OverviewDistance();
            arriveFrom = from;
            arriveCorona = corona;
            // End where the overview focus will put us, so handing over is seamless: the same elevation, measured
            // from the destination's plane.
            var local = Quaternion.Inverse(frame) * ToVector(arriveDirection);
            var flat = new Vector3(local.x, 0, local.z);
            if (flat.sqrMagnitude < 1e-6f) flat = Vector3.back;
            flat.Normalize();
            float el = UnitySpaceApp.OverviewPitch * Mathf.Deg2Rad;
            var endDir = frame * new Vector3(flat.x * Mathf.Cos(el), Mathf.Sin(el), flat.z * Mathf.Cos(el));
            arriveEndRotation = Quaternion.LookRotation(-endDir, toUp);
            arriveEndDirection = new DVec3(endDir.x, endDir.y, endDir.z);
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
            t = Mathf.Min(1, t + dt / (ThroughGate ? (float)GateJump.EmergeSeconds : ArriveSeconds));
            float e = Smoother(t);
            Views.BodyView.CoronaPresence = Mathf.Lerp(arriveCorona, 1f, e);
            var root = app.BodyScene(0);
            var dir = Slerp(arriveDirection, arriveEndDirection, e);
            double d = Math.Exp(Math.Log(arriveFrom) + (Math.Log(arriveTo) - Math.Log(arriveFrom)) * e);
            var look = Look(ToVector(-dir), toUp);
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
