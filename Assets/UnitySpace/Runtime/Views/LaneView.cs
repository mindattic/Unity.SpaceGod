using System.Collections.Generic;
using Unity.SpaceGod.Core;
using Unity.SpaceGod.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.SpaceGod.Views
{
    /// <summary>
    /// Draws the hyperlane network: each lane a glowing ribbon along its curve between its two ramps,
    /// a ring gate at each ramp, and pulses of light streaming both ways so it reads as a highway.
    /// Everything is a few pixels wide at any distance, so the network shows from across the system.
    /// Up close each ring gate is drawn at its true size (<see cref="Hyperlanes.GateRadiusKm"/>), square
    /// to its lane, for a pilot to fly through; it flashes as a hull goes in or comes out (U.SPC-T41).
    /// Between the ramps each lane is a chain of rings (U.SPC-T379, <see cref="RingChain"/>), and the spurs
    /// that lead the network on to the jump gates (<see cref="Hyperlanes.GateSpurs"/>) are drawn the same
    /// way in amber.
    /// </summary>
    public sealed class LaneView
    {
        readonly UnitySpaceApp app;
        readonly Mesh mesh;
        readonly MeshRenderer renderer;
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<Vector3> uvs = new List<Vector3>();
        readonly List<int> indices = new List<int>();
        readonly Vector3[] curve = new Vector3[Samples + 1];

        const int Samples = 48;
        const int RingSegments = 40;
        const float FlashSeconds = 0.9f;
        static readonly Color LaneColor = new Color(0.25f, 0.6f, 1f);
        static readonly Color SpurColor = new Color(1f, 0.7f, 0.3f);
        static readonly Color PulseColor = new Color(0.6f, 0.9f, 1f);
        static readonly Color GateColor = new Color(0.45f, 0.8f, 1f);

        Lane flashLane;
        int flashAt = -1;
        float flashTime = -10f;

        /// <summary>Lights up the ring gate at <paramref name="at"/>'s end of <paramref name="lane"/> for a moment.</summary>
        public void Flash(Lane lane, int at)
        {
            flashLane = lane;
            flashAt = at;
            flashTime = Time.unscaledTime;
        }

        float FlashOf(Lane lane, int at, float now) =>
            lane == flashLane && at == flashAt ? Mathf.Clamp01(1f - (now - flashTime) / FlashSeconds) : 0f;

        public LaneView(UnitySpaceApp app, Transform parent)
        {
            this.app = app;
            var go = new GameObject("Hyperlanes");
            go.transform.SetParent(parent, false);
            mesh = Materials.Own(new Mesh { name = "Hyperlanes", indexFormat = IndexFormat.UInt32 });
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Materials.Create(Materials.Fx, "Hyperlanes");
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        public void Update(bool visible)
        {
            renderer.enabled = visible;
            var lanes = app.Fleet.Lanes;
            if (!visible || lanes == null) return;
            vertices.Clear();
            colors.Clear();
            uvs.Clear();
            indices.Clear();

            var eph = app.Ephemeris;
            var scale = app.Scale;
            var scene = app.BodyScenes;
            float now = Time.unscaledTime;
            int count = lanes.Lanes.Count + lanes.GateSpurs.Count;
            for (int n = 0; n < count; n++)
            {
                var lane = n < lanes.Lanes.Count ? lanes.Lanes[n] : lanes.GateSpurs[n - lanes.Lanes.Count];
                var color = lane.IsGateSpur ? SpurColor : LaneColor;
                var aKm = lanes.RampHelio(lane, lane.A, eph.Jd);
                var bKm = lanes.RampHelio(lane, lane.B, eph.Jd);
                for (int i = 0; i <= Samples; i++)
                    curve[i] = app.Relative(scale.LaneCurveScene(eph, scene, lanes, lane, lane.A, aKm, lane.B, bKm, (double)i / Samples, out _));
                for (int i = 0; i < Samples; i++)
                {
                    Ribbon(curve[i], curve[i + 1], 6f, color * 0.16f);
                    Ribbon(curve[i], curve[i + 1], 1.4f, color * 0.45f);
                }
                Sprite(curve[0], 16, color * 0.7f, 2);
                Sprite(curve[Samples], 16, color * 0.7f, 2);
                Gate(lane, lane.A, aKm, curve[0], curve[1], now);
                Gate(lane, lane.B, bKm, curve[Samples], curve[Samples - 1], now);
                RingChain(lane, color, now);
                // Ambient flow: a faint pulse each way, just to read as a live highway at any zoom,
                // even when nothing is actually riding it right now.
                for (int k = 0; k < 2; k++)
                {
                    float phase = (now * 0.06f + k / 2f + lane.Id * 0.137f) % 1f;
                    Sprite(Along(phase), 4, PulseColor * 0.35f, 0);
                }
                // Real traffic: every ship actually riding this lane, or holding just off it after an
                // interrupted ride, drawn at its own live progress and tinted by faction -- the lane is
                // the simulation's real interplanetary transport, not a decorative loop (see
                // Hyperlanes.cs); a pulse reaching the far ramp should correspond to an actual ship.
                foreach (var ship in app.Fleet.Ships)
                {
                    if (ship.Destroyed) continue;
                    double progress;
                    if (ship.Phase == ShipPhase.Lane && ship.LaneLeg.Lane == lane) progress = Fleet.LaneProgress(ship, eph.Jd);
                    else if ((ship.Phase == ShipPhase.Lurk || ship.Phase == ShipPhase.Adrift) && ship.LaneLeg.Lane == lane) progress = ship.LaneHold;
                    else continue;
                    // Progress counts from the ramp the ship set out from; the curve runs A to B.
                    if (ship.LaneLeg.From != lane.A) progress = 1 - progress;
                    var fc = ship.Faction.Color;
                    Sprite(Along((float)progress), 7, new Color(fc.R, fc.G, fc.B) * 1.3f, 0);
                }
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);
        }

        /// <summary>
        /// A ramp's ring gate: a hoop square to the lane, its true radius as mapped near its planet (never
        /// under a few pixels), brightening and swelling for a moment when it flashes.
        /// </summary>
        void Gate(Lane lane, int at, DVec3 rampKm, Vector3 centre, Vector3 inward, float now)
        {
            var axis = inward - centre;
            if (axis.sqrMagnitude < 1e-12f) return;
            axis.Normalize();
            var eph = app.Ephemeris;
            var radial = app.Fleet.Lanes.RampDirection(lane, at, eph.Jd);
            var across = DVec3.Cross(radial, DVec3.UnitZ);
            across = across.LengthSquared > 1e-12 ? across.Normalized : DVec3.UnitX;
            var c = app.Scale.MapNear(eph, app.BodyScenes, at, rampKm);
            var e = app.Scale.MapNear(eph, app.BodyScenes, at, rampKm + across * Hyperlanes.GateRadiusKm);
            float flash = FlashOf(lane, at, now);
            float radius = Mathf.Max((float)(e - c).Length, PixelSize(centre, 9)) * (1f + 0.25f * flash);
            var u = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.98f ? Vector3.right : Vector3.up).normalized;
            var v = Vector3.Cross(axis, u);
            var color = GateColor * (0.75f + 2.5f * flash);
            float width = 2.2f + 5f * flash;
            var prev = centre + u * radius;
            for (int k = 1; k <= RingSegments; k++)
            {
                float t = k * Mathf.PI * 2f / RingSegments;
                var p = centre + (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * radius;
                Ribbon(prev, p, width, color);
                prev = p;
            }
            if (flash > 0) Sprite(centre, 18 + 70 * flash, PulseColor * (2.2f * flash), 0);
        }

        /// <summary>
        /// The lane's chain of rings (U.SPC-T379): <see cref="Hyperlanes.RingCount"/> hoops of the ring gates' size strung
        /// evenly between the ramps, square to the lane, which accelerate a rider through the first half and brake it
        /// through the second. Rendered only where they can be seen: a ring under a couple of pixels across is skipped,
        /// so from across the system the lane is its ribbon and only up close (riding it, or flying near) does it read as
        /// a chain of hoops. The lane being ridden at the helm strings its rings along the rider's own curve, so the hull
        /// threads them, and the rings just flown through glow.
        /// </summary>
        void RingChain(Lane lane, Color color, float now)
        {
            var lanes = app.Fleet.Lanes;
            var eph = app.Ephemeris;
            double trueKm = lanes.LaneLengthKm(lane, eph.Jd);
            if (trueKm <= 0) return;
            float sceneLen = 0;
            for (int i = 0; i < Samples; i++) sceneLen += (curve[i + 1] - curve[i]).magnitude;
            float ringRadius = (float)(Hyperlanes.GateRadiusKm * sceneLen / trueKm);
            if (ringRadius <= 0) return;

            Ship rider = app.RidingLane && app.Piloted.Ship.LaneLeg.Lane == lane ? app.Piloted.Ship : null;
            int rings = Hyperlanes.RingCount(rider != null ? rider.LaneLengthKm : trueKm);
            int passed = rider != null ? Hyperlanes.RingsPassed(Fleet.LaneProgress(rider, eph.Jd), rings) : -10;
            for (int k = 0; k < rings; k++)
            {
                double s = Hyperlanes.RingFraction(k, rings);
                Vector3 centre, axis;
                if (rider != null)
                {
                    // The rider's curve runs from the ramp it set out from: ring k from there.
                    centre = app.Relative(app.Scale.LaneScene(eph, app.BodyScenes, lanes, rider, s, out var dir));
                    axis = new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);
                }
                else
                {
                    float fs = (float)s;
                    centre = Along(fs);
                    axis = Along(Mathf.Min(1f, fs + 0.002f)) - Along(Mathf.Max(0f, fs - 0.002f));
                }
                if (axis.sqrMagnitude < 1e-12f) continue;
                // Below a couple of pixels a ring is not drawn at all: the ribbon carries the lane from afar.
                if (ringRadius < PixelSize(centre, 2.5f)) continue;
                axis.Normalize();
                // The magnetic pulse running down the chain, and the rings the rider has just flown through flaring.
                float wave = 0.55f + 0.45f * Mathf.Sin(k * 0.9f - now * 5f);
                float flare = rider != null ? Mathf.Clamp01(1f - Mathf.Abs(passed - 1 - k) * 0.5f) : 0f;
                Hoop(centre, axis, ringRadius, 1.6f + 2.5f * flare, color * (0.45f + 0.5f * wave + 1.6f * flare));
            }
        }

        /// <summary>A hoop of ribbon square to <paramref name="axis"/>.</summary>
        void Hoop(Vector3 centre, Vector3 axis, float radius, float width, Color color)
        {
            var u = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.98f ? Vector3.right : Vector3.up).normalized;
            var v = Vector3.Cross(axis, u);
            var prev = centre + u * radius;
            for (int k = 1; k <= RingSegments; k++)
            {
                float t = k * Mathf.PI * 2f / RingSegments;
                var p = centre + (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * radius;
                Ribbon(prev, p, width, color);
                prev = p;
            }
        }

        Vector3 Along(float s)
        {
            float f = s * Samples;
            int i = Mathf.Clamp((int)f, 0, Samples - 1);
            return Vector3.Lerp(curve[i], curve[i + 1], f - i);
        }

        float PixelSize(Vector3 p, float pixels) => pixels * Mathf.Max(p.magnitude, 1e-6f) / (float)app.PixelsPerUnitAtUnitDistance();

        void Ribbon(Vector3 a, Vector3 b, float pixels, Color color)
        {
            var along = b - a;
            if (along.sqrMagnitude < 1e-12f) return;
            var side = Vector3.Cross(along, (a + b) * 0.5f).normalized;
            float wa = PixelSize(a, pixels) * 0.5f, wb = PixelSize(b, pixels) * 0.5f;
            int i = vertices.Count;
            vertices.Add(a - side * wa);
            vertices.Add(b - side * wb);
            vertices.Add(b + side * wb);
            vertices.Add(a + side * wa);
            // A long continuous ribbon: shape it across only (uv.x mid-range keeps the ends untapered).
            Corners(color, 1, 0.5f, 0.5f);
            Indices(i);
        }

        void Sprite(Vector3 c, float pixels, Color color, float shape)
        {
            var cam = app.UnityCamera.transform;
            float r = PixelSize(c, pixels);
            var right = cam.right * r;
            var up = cam.up * r;
            int i = vertices.Count;
            vertices.Add(c - right - up);
            vertices.Add(c + right - up);
            vertices.Add(c + right + up);
            vertices.Add(c - right + up);
            Corners(color, shape, 0, 1);
            Indices(i);
        }

        void Corners(Color color, float shape, float u0, float u1)
        {
            for (int k = 0; k < 4; k++) colors.Add(color);
            uvs.Add(new Vector3(u0, 0, shape));
            uvs.Add(new Vector3(u1, 0, shape));
            uvs.Add(new Vector3(u1, 1, shape));
            uvs.Add(new Vector3(u0, 1, shape));
        }

        void Indices(int i)
        {
            indices.Add(i);
            indices.Add(i + 1);
            indices.Add(i + 2);
            indices.Add(i);
            indices.Add(i + 2);
            indices.Add(i + 3);
        }
    }
}
