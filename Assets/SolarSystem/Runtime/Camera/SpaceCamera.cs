using System;
using SolarSystem.Core;
using SolarSystem.Input;
using UnityEngine;

namespace SolarSystem.Cameras
{
    /// <summary>What the camera needs to know about the world.</summary>
    public interface ICameraWorld
    {
        BodyCatalog Catalog { get; }
        ScaleModel Scale { get; }
        DVec3 BodyScene(int body);
        DVec3 TargetScene(Target target);

        /// <summary>A target's framing radius in scene units (body radius, or half a ship's length).</summary>
        double TargetRadius(Target target);

        /// <summary>Distance from a scene point to the nearest surface (body or ship hull), scene units.</summary>
        double NearestSurface(DVec3 scenePosition);
    }

    /// <summary>
    /// Homeworld 3 style space camera.
    ///
    ///  - Free: swim like Quake. WASD along the view, Q/E (Space/C) up and down, hold the right
    ///    mouse button to look. Speed scales with distance to the nearest surface, so the same keys
    ///    cross an AU in deep space and creep along a hull up close.
    ///  - Focus (F): fly to the selected body or ship and orbit it; right-drag orbits, the wheel
    ///    zooms, and the camera rides along with the target as it moves. Any movement key drops
    ///    straight back into free swim from where you are.
    ///
    /// Free-camera position is stored as a true (km) offset from a reference body, the body whose
    /// sphere of influence you are in, so the camera co-moves with whatever you are near under any
    /// time warp, and keeps its place relative to it when the scale is toggled.
    /// </summary>
    public sealed class SpaceCamera
    {
        public enum CameraMode
        {
            Free,
            Travel,
            Focus,
        }

        public CameraMode Mode { get; private set; } = CameraMode.Free;
        public DVec3 ScenePosition { get; private set; }
        public Quaternion Rotation { get; private set; } = Quaternion.identity;
        public Target FocusTarget { get; private set; } = Target.None;
        public int FrameBody => frame;

        /// <summary>Current swim speed in scene units per second (for the HUD).</summary>
        public double CurrentSpeed { get; private set; }

        public double SpeedMultiplier { get; private set; } = 1.0;

        const float TravelSeconds = 1.6f;
        const float LookSensitivity = 0.12f;
        const double MinSpeedMultiplier = 1.0 / 64, MaxSpeedMultiplier = 64;

        readonly ICameraWorld world;

        int frame;
        DVec3 trueOffset;
        float yaw, pitch;
        Vector3 velocity;

        float orbitYaw, orbitPitch;
        double zoom = 4, zoomTarget = 4;

        DVec3 travelStartOffset;
        Quaternion travelStartRotation;
        DVec3 travelEndDirection;
        double travelEndZoom;
        float travelT;

        public SpaceCamera(ICameraWorld world)
        {
            this.world = world;
        }

        // ---------------------------------------------------------------- commands

        /// <summary>Places the free camera at a scene position looking along yaw/pitch.</summary>
        public void PlaceFree(DVec3 scenePosition, float yawDeg, float pitchDeg)
        {
            Mode = CameraMode.Free;
            FocusTarget = Target.None;
            yaw = yawDeg;
            pitch = Mathf.Clamp(pitchDeg, -89f, 89f);
            Rotation = Quaternion.Euler(pitch, yaw, 0);
            velocity = Vector3.zero;
            SetScenePosition(scenePosition);
        }

        /// <summary>Flies to <paramref name="target"/> and enters focus. <paramref name="radii"/> overrides the framing distance.</summary>
        public void FocusOn(Target target, double radii = double.NaN, float? pitchOverride = null)
        {
            if (target.IsNone) return;
            var targetScene = world.TargetScene(target);
            double r = Math.Max(world.TargetRadius(target), 1e-9);
            var offset = ScenePosition - targetScene;
            double distance = offset.Length;

            double framing = !double.IsNaN(radii) ? radii : DefaultFraming(target);
            double currentRadii = distance / r;
            if (double.IsNaN(radii) && Mode != CameraMode.Free && FocusTarget == target) framing = zoomTarget;
            else if (double.IsNaN(radii) && currentRadii > MinZoom(target) * 1.05 && currentRadii < framing) framing = currentRadii;

            var dir = distance > 0 ? offset / distance : new DVec3(0, 0.3, -1).Normalized;
            // Approach from the side we are already on, but never from straight over a pole.
            var flat = new Vector3((float)dir.X, 0, (float)dir.Z);
            float elevation = Mathf.Asin(Mathf.Clamp((float)dir.Y, -1, 1)) * Mathf.Rad2Deg;
            elevation = pitchOverride ?? Mathf.Clamp(elevation, -55f, 55f);
            if (flat.sqrMagnitude < 1e-8f) flat = Vector3.back;
            flat.Normalize();
            float e = elevation * Mathf.Deg2Rad;
            var endDir = new Vector3(flat.x * Mathf.Cos(e), Mathf.Sin(e), flat.z * Mathf.Cos(e));

            travelStartOffset = distance > 0 ? offset : new DVec3(endDir.x, endDir.y, endDir.z) * r * framing * 3;
            travelStartRotation = Rotation;
            travelEndDirection = new DVec3(endDir.x, endDir.y, endDir.z);
            travelEndZoom = framing;
            travelT = 0;

            // The orbit angles that reproduce endDir: offset = Euler(pitch, yaw) * back.
            var look = -endDir;
            orbitYaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            orbitPitch = -Mathf.Asin(Mathf.Clamp(look.y, -1, 1)) * Mathf.Rad2Deg;
            zoom = zoomTarget = framing;

            FocusTarget = target;
            Mode = CameraMode.Travel;
            velocity = Vector3.zero;
        }

        public void ExitFocus()
        {
            if (Mode == CameraMode.Free) return;
            var forward = Rotation * Vector3.forward;
            PlaceFree(ScenePosition, Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, -Mathf.Asin(Mathf.Clamp(forward.y, -1, 1)) * Mathf.Rad2Deg);
        }

        public static double DefaultFraming(Target target)
        {
            return target.IsShip ? 5.0 : 3.4;
        }

        static double MinZoom(Target target) => target.IsShip ? 1.6 : 1.12;

        // ---------------------------------------------------------------- per frame

        public void Update(float dt, InputState input)
        {
            bool moving = input.Move.sqrMagnitude > 0.01f;
            if (Mode != CameraMode.Free && moving) ExitFocus();

            switch (Mode)
            {
                case CameraMode.Travel:
                    UpdateTravel(dt);
                    break;
                case CameraMode.Focus:
                    UpdateFocus(dt, input);
                    break;
                default:
                    UpdateFree(dt, input);
                    break;
            }
        }

        void UpdateFree(float dt, InputState input)
        {
            if (input.LookHeld)
            {
                yaw += input.LookDelta.x * LookSensitivity;
                pitch = Mathf.Clamp(pitch - input.LookDelta.y * LookSensitivity, -89f, 89f);
            }
            Rotation = Quaternion.Euler(pitch, yaw, 0);

            if (Math.Abs(input.Scroll) > 0)
                SpeedMultiplier = Math.Max(MinSpeedMultiplier, Math.Min(MaxSpeedMultiplier, SpeedMultiplier * Math.Pow(1.3, input.Scroll)));

            // Re-derive the position from the frame (the frame body may have moved or the scale changed).
            ScenePosition = FramePosition();

            double surface = Math.Max(world.NearestSurface(ScenePosition), 1e-6);
            double speed = surface * 0.9 * SpeedMultiplier * (input.Boost ? 6 : 1) * (input.Slow ? 0.15 : 1);
            CurrentSpeed = speed;

            var desired = Rotation * Vector3.ClampMagnitude(input.Move, 1f) * (float)speed;
            float response = 1f - Mathf.Exp(-dt * 7f);
            velocity = Vector3.Lerp(velocity, desired, response);
            if (input.Move.sqrMagnitude < 0.01f && velocity.magnitude < speed * 0.002) velocity = Vector3.zero;

            var delta = new DVec3(velocity.x, velocity.y, velocity.z) * dt;
            if (delta.LengthSquared > 0) ApplySceneDelta(delta);

            ScenePosition = FramePosition();
            if (PushOutOfBodies()) velocity *= 0.3f;
            UpdateFrame();
            ScenePosition = FramePosition();
        }

        void UpdateFocus(float dt, InputState input)
        {
            if (input.LookHeld)
            {
                orbitYaw += input.LookDelta.x * LookSensitivity;
                orbitPitch = Mathf.Clamp(orbitPitch - input.LookDelta.y * LookSensitivity, -88f, 88f);
            }
            if (Math.Abs(input.Scroll) > 0)
                zoomTarget = Math.Max(MinZoom(FocusTarget), Math.Min(5e7, zoomTarget * Math.Pow(0.82, input.Scroll)));
            zoom = Math.Exp(Math.Log(zoom) + (Math.Log(zoomTarget) - Math.Log(zoom)) * (1 - Math.Exp(-dt * 10)));

            Rotation = Quaternion.Euler(orbitPitch, orbitYaw, 0);
            var back = Rotation * Vector3.back;
            double r = world.TargetRadius(FocusTarget);
            ScenePosition = world.TargetScene(FocusTarget) + new DVec3(back.x, back.y, back.z) * (zoom * r);
            CurrentSpeed = 0;
        }

        void UpdateTravel(float dt)
        {
            travelT = Mathf.Min(1f, travelT + dt / TravelSeconds);
            float e = SmootherStep(travelT);

            var target = world.TargetScene(FocusTarget);
            double r = world.TargetRadius(FocusTarget);
            double startDist = Math.Max(travelStartOffset.Length, 1e-9);
            double endDist = travelEndZoom * r;
            double dist = Math.Exp(Math.Log(startDist) + (Math.Log(endDist) - Math.Log(startDist)) * e);

            var startDir = (travelStartOffset / startDist);
            var dir = Vector3.Slerp(ToVector(startDir), ToVector(travelEndDirection), e).normalized;
            ScenePosition = target + new DVec3(dir.x, dir.y, dir.z) * dist;

            var lookAt = Quaternion.LookRotation(-dir, Vector3.up);
            Rotation = Quaternion.Slerp(travelStartRotation, lookAt, SmootherStep(Mathf.Min(1f, travelT * 1.8f)));

            if (travelT >= 1f)
            {
                Mode = CameraMode.Focus;
                zoom = zoomTarget = travelEndZoom;
            }
        }

        static float SmootherStep(float t) => t * t * t * (t * (t * 6 - 15) + 10);
        static Vector3 ToVector(DVec3 v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

        // ---------------------------------------------------------------- reference frames

        DVec3 FramePosition()
        {
            var body = world.Catalog[frame];
            return world.BodyScene(frame) + world.Scale.LocalOffset(body, trueOffset);
        }

        /// <summary>Moves by a scene-space delta, converted to true km through the local scale mapping's Jacobian.</summary>
        void ApplySceneDelta(DVec3 delta)
        {
            var body = world.Catalog[frame];
            var scale = world.Scale;
            double d = trueOffset.Length;
            if (d <= 0)
            {
                trueOffset = delta.SwapYZ * (body.RadiusKm / Math.Max(scale.DisplayRadius(body), 1e-12));
                return;
            }
            double sceneDist = scale.LocalDistance(body, d);
            var dirScene = trueOffset.SwapYZ / d;
            double radial = DVec3.Dot(delta, dirScene);
            var tangential = delta - dirScene * radial;
            double derivative = Math.Max(scale.LocalDistanceDerivative(body, d), 1e-15);
            var trueDeltaScene = dirScene * (radial / derivative) + tangential * (d / sceneDist);
            trueOffset += trueDeltaScene.SwapYZ;

            double minimum = body.RadiusKm * 1.002;
            if (trueOffset.Length < minimum) trueOffset = trueOffset.Normalized * minimum;
        }

        /// <summary>Re-anchors to whichever body's sphere of influence contains the scene point, keeping the scene position.</summary>
        public void SetScenePosition(DVec3 scenePosition)
        {
            int f = 0;
            while (true)
            {
                int best = -1;
                double bestRatio = 1.0;
                foreach (int child in world.Catalog.ChildrenOf(f))
                {
                    double soi = world.Scale.SceneSoi(world.Catalog[child]);
                    double ratio = (scenePosition - world.BodyScene(child)).Length / soi;
                    if (ratio < bestRatio)
                    {
                        bestRatio = ratio;
                        best = child;
                    }
                }
                if (best < 0) break;
                f = best;
            }
            frame = f;
            trueOffset = world.Scale.InverseLocalOffset(world.Catalog[f], scenePosition - world.BodyScene(f));
            ScenePosition = scenePosition;
        }

        /// <summary>Frame hysteresis: enter a child's SOI below 92% of it, leave ours above 108%.</summary>
        void UpdateFrame()
        {
            var pos = FramePosition();
            if (frame != 0)
            {
                var body = world.Catalog[frame];
                if ((pos - world.BodyScene(frame)).Length > world.Scale.SceneSoi(body) * 1.08)
                {
                    int parent = body.ParentIndex;
                    frame = parent;
                    trueOffset = world.Scale.InverseLocalOffset(world.Catalog[parent], pos - world.BodyScene(parent));
                    return;
                }
            }
            foreach (int child in world.Catalog.ChildrenOf(frame))
            {
                var c = world.Catalog[child];
                if ((pos - world.BodyScene(child)).Length < world.Scale.SceneSoi(c) * 0.92)
                {
                    frame = child;
                    trueOffset = world.Scale.InverseLocalOffset(c, pos - world.BodyScene(child));
                    return;
                }
            }
        }

        bool PushOutOfBodies()
        {
            var pos = ScenePosition;
            bool pushed = false;
            for (int i = 0; i < world.Catalog.Count; i++)
            {
                var c = world.BodyScene(i);
                double minimum = world.Scale.DisplayRadius(world.Catalog[i]) * 1.003;
                var d = pos - c;
                double len = d.Length;
                if (len < minimum)
                {
                    pos = c + (len > 0 ? d / len : DVec3.UnitY) * minimum;
                    pushed = true;
                }
            }
            if (pushed) SetScenePosition(pos);
            return pushed;
        }
    }
}
