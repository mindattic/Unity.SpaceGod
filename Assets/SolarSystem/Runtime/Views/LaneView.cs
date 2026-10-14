using System.Collections.Generic;
using SolarSystem.Core;
using SolarSystem.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace SolarSystem.Views
{
    /// <summary>
    /// Draws the hyperlane network: each lane a glowing ribbon along its curve between its two ramps,
    /// a ring gate at each ramp, and pulses of light streaming both ways so it reads as a highway.
    /// Everything is a few pixels wide at any distance, so the network shows from across the system.
    /// </summary>
    public sealed class LaneView
    {
        readonly SolarSystemApp app;
        readonly Mesh mesh;
        readonly MeshRenderer renderer;
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<Vector3> uvs = new List<Vector3>();
        readonly List<int> indices = new List<int>();
        readonly Vector3[] curve = new Vector3[Samples + 1];

        const int Samples = 48;
        static readonly Color LaneColor = new Color(0.25f, 0.6f, 1f);
        static readonly Color PulseColor = new Color(0.6f, 0.9f, 1f);

        public LaneView(SolarSystemApp app, Transform parent)
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
            foreach (var lane in lanes.Lanes)
            {
                var aKm = lanes.RampHelio(lane, lane.A, eph.Jd);
                var bKm = lanes.RampHelio(lane, lane.B, eph.Jd);
                for (int i = 0; i <= Samples; i++)
                    curve[i] = app.Relative(scale.LaneCurveScene(eph, scene, lanes, lane, lane.A, aKm, lane.B, bKm, (double)i / Samples, out _));
                for (int i = 0; i < Samples; i++)
                {
                    Ribbon(curve[i], curve[i + 1], 6f, LaneColor * 0.16f);
                    Ribbon(curve[i], curve[i + 1], 1.4f, LaneColor * 0.45f);
                }
                Sprite(curve[0], 16, LaneColor * 0.7f, 2);
                Sprite(curve[Samples], 16, LaneColor * 0.7f, 2);
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
