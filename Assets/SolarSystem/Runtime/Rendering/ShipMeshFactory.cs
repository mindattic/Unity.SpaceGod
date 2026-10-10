using System.Collections.Generic;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Rendering
{
    /// <summary>
    /// Kit-bashed capital ship hulls built from boxes, frustums and cylinders. Every design is one
    /// unit long on +Z (nose forward), flat-shaded, with the paint scheme in vertex colours and
    /// engine bells flagged emissive through vertex alpha (see Ship.shader).
    /// </summary>
    public static class ShipMeshFactory
    {
        static readonly Dictionary<(ShipClass, Faction), Mesh> Cache = new Dictionary<(ShipClass, Faction), Mesh>();

        public static Mesh Get(ShipClass shipClass, Faction faction)
        {
            if (Cache.TryGetValue((shipClass, faction), out var mesh)) return mesh;
            var b = new Builder(faction);
            switch (shipClass)
            {
                case ShipClass.Carrier: BuildCarrier(b); break;
                case ShipClass.Battleship: BuildBattleship(b); break;
                case ShipClass.Cruiser: BuildCruiser(b); break;
                case ShipClass.Destroyer: BuildDestroyer(b); break;
                case ShipClass.Miner: BuildHauler(b); break;
                case ShipClass.Courier: BuildCourier(b); break;
                default: BuildFreighter(b); break;
            }
            mesh = b.ToMesh(shipClass + "_" + faction.Prefix);
            Cache[(shipClass, faction)] = mesh;
            return mesh;
        }

        static void BuildBattleship(Builder b)
        {
            b.Frustum(-0.42f, 0.30f, new Vector2(0.15f, 0.085f), new Vector2(0.11f, 0.06f), 0, b.Hull);
            b.Frustum(0.30f, 0.50f, new Vector2(0.11f, 0.06f), new Vector2(0.018f, 0.016f), 0, b.Hull);
            b.Frustum(-0.36f, 0.26f, new Vector2(0.05f, 0.03f), new Vector2(0.03f, 0.02f), -0.055f, b.Dark);
            foreach (float side in new[] { -1f, 1f })
            {
                b.Box(new Vector3(side * 0.071f, 0.0f, -0.05f), new Vector3(0.008f, 0.022f, 0.6f), b.Accent);
                b.Box(new Vector3(side * 0.06f, 0.04f, -0.02f), new Vector3(0.03f, 0.016f, 0.035f), b.Hull);
                b.Cylinder(new Vector3(side * 0.06f, 0.044f, 0.01f), 0.003f, 0.05f, b.Dark);
            }
            b.Box(new Vector3(0, 0.065f, -0.12f), new Vector3(0.06f, 0.05f, 0.14f), b.Hull);
            b.Box(new Vector3(0, 0.098f, -0.1f), new Vector3(0.09f, 0.018f, 0.035f), b.Dark);
            b.Box(new Vector3(0, 0.125f, -0.13f), new Vector3(0.008f, 0.04f, 0.008f), b.Hull);
            Turret(b, 0.20f, 0.045f, 1);
            Turret(b, 0.07f, 0.06f, 1);
            Turret(b, -0.28f, 0.045f, -1);
            b.Box(new Vector3(0, 0, -0.45f), new Vector3(0.13f, 0.075f, 0.06f), b.Dark);
            foreach (float x in new[] { -0.042f, 0f, 0.042f })
                b.Nozzle(new Vector3(x, 0, -0.48f), 0.022f, 0.04f);
        }

        static void BuildCarrier(Builder b)
        {
            b.Frustum(-0.44f, 0.32f, new Vector2(0.2f, 0.07f), new Vector2(0.17f, 0.06f), 0, b.Hull);
            b.Frustum(0.32f, 0.50f, new Vector2(0.17f, 0.06f), new Vector2(0.06f, 0.028f), 0, b.Hull);
            b.Box(new Vector3(0, 0.041f, 0.0f), new Vector3(0.19f, 0.012f, 0.92f), b.Dark);
            b.Box(new Vector3(0, 0.048f, 0.0f), new Vector3(0.006f, 0.002f, 0.85f), b.Accent);
            b.Box(new Vector3(-0.055f, 0.048f, 0.02f), new Vector3(0.003f, 0.002f, 0.7f), b.Light);
            b.Box(new Vector3(0.055f, 0.048f, 0.02f), new Vector3(0.003f, 0.002f, 0.7f), b.Light);
            b.Box(new Vector3(0, 0.012f, 0.455f), new Vector3(0.11f, 0.028f, 0.02f), b.Glow(new Color(1f, 0.85f, 0.55f)));
            foreach (float side in new[] { -1f, 1f })
            {
                b.Box(new Vector3(side * 0.115f, 0, -0.05f), new Vector3(0.03f, 0.05f, 0.5f), b.Hull);
                b.Box(new Vector3(side * 0.131f, 0, -0.05f), new Vector3(0.004f, 0.012f, 0.45f), b.Accent);
                b.Box(new Vector3(side * 0.131f, -0.012f, 0.12f), new Vector3(0.003f, 0.01f, 0.1f), b.Glow(new Color(1f, 0.85f, 0.55f)));
            }
            b.Box(new Vector3(0.07f, 0.09f, -0.12f), new Vector3(0.035f, 0.08f, 0.13f), b.Hull);
            b.Box(new Vector3(0.07f, 0.135f, -0.11f), new Vector3(0.05f, 0.015f, 0.05f), b.Dark);
            b.Box(new Vector3(0.07f, 0.16f, -0.13f), new Vector3(0.006f, 0.035f, 0.006f), b.Hull);
            b.Box(new Vector3(0, 0, -0.46f), new Vector3(0.18f, 0.065f, 0.05f), b.Dark);
            foreach (float x in new[] { -0.075f, -0.027f, 0.027f, 0.075f })
                b.Nozzle(new Vector3(x, 0, -0.49f), 0.02f, 0.035f);
        }

        static void BuildCruiser(Builder b)
        {
            b.Frustum(-0.40f, 0.25f, new Vector2(0.07f, 0.05f), new Vector2(0.06f, 0.045f), 0, b.Hull);
            b.Frustum(0.25f, 0.50f, new Vector2(0.12f, 0.05f), new Vector2(0.035f, 0.02f), 0, b.Hull);
            b.Box(new Vector3(0, 0, -0.1f), new Vector3(0.2f, 0.012f, 0.18f), b.Hull);
            foreach (float side in new[] { -1f, 1f })
            {
                b.Box(new Vector3(side * 0.1f, 0, -0.1f), new Vector3(0.014f, 0.018f, 0.19f), b.Accent);
                b.Box(new Vector3(side * 0.1f, 0, -0.205f), new Vector3(0.02f, 0.02f, 0.03f), b.Dark);
                b.Nozzle(new Vector3(side * 0.1f, 0, -0.225f), 0.008f, 0.015f);
            }
            Turret(b, 0.15f, 0.03f, 1);
            Turret(b, 0.0f, 0.03f, 1);
            b.Box(new Vector3(0, 0.045f, -0.2f), new Vector3(0.04f, 0.03f, 0.08f), b.Hull);
            b.Box(new Vector3(0, 0.055f, -0.17f), new Vector3(0.042f, 0.008f, 0.02f), b.Dark);
            b.Box(new Vector3(0, 0, -0.44f), new Vector3(0.09f, 0.05f, 0.05f), b.Dark);
            b.Nozzle(new Vector3(-0.025f, 0, -0.47f), 0.022f, 0.035f);
            b.Nozzle(new Vector3(0.025f, 0, -0.47f), 0.022f, 0.035f);
        }

        static void BuildDestroyer(Builder b)
        {
            b.Frustum(-0.45f, 0.50f, new Vector2(0.15f, 0.07f), new Vector2(0.02f, 0.018f), 0, b.Hull);
            b.Frustum(-0.42f, 0.30f, new Vector2(0.06f, 0.03f), new Vector2(0.02f, 0.01f), -0.04f, b.Dark);
            b.Box(new Vector3(0, 0.05f, -0.2f), new Vector3(0.012f, 0.06f, 0.25f), b.Accent);
            b.Box(new Vector3(0, 0.04f, -0.08f), new Vector3(0.05f, 0.03f, 0.1f), b.Hull);
            Turret(b, 0.12f, 0.03f, 1);
            b.Nozzle(new Vector3(-0.035f, 0, -0.47f), 0.025f, 0.04f);
            b.Nozzle(new Vector3(0.035f, 0, -0.47f), 0.025f, 0.04f);
        }

        static void BuildFreighter(Builder b)
        {
            b.Box(new Vector3(0, 0, -0.02f), new Vector3(0.03f, 0.03f, 0.8f), b.Dark);
            Color[] crates = { new Color(0.75f, 0.35f, 0.2f), new Color(0.25f, 0.45f, 0.6f), new Color(0.7f, 0.65f, 0.3f), b.Hull };
            for (int i = 0; i < 6; i++)
            {
                float z = -0.3f + i * 0.11f;
                b.Box(new Vector3(0, 0.035f, z), new Vector3(0.09f, 0.05f, 0.1f), crates[i % crates.Length]);
                b.Box(new Vector3(0, -0.035f, z), new Vector3(0.09f, 0.05f, 0.1f), crates[(i + 2) % crates.Length]);
            }
            b.Frustum(0.36f, 0.5f, new Vector2(0.1f, 0.08f), new Vector2(0.05f, 0.04f), 0, b.Hull);
            b.Box(new Vector3(0, 0.03f, 0.43f), new Vector3(0.06f, 0.012f, 0.03f), b.Glow(new Color(0.8f, 0.9f, 1f)));
            b.Box(new Vector3(0, 0, -0.43f), new Vector3(0.1f, 0.09f, 0.06f), b.Hull);
            b.Nozzle(new Vector3(0, 0, -0.47f), 0.035f, 0.04f);
        }

        static void BuildHauler(Builder b)
        {
            b.Box(new Vector3(0, 0, 0), new Vector3(0.22f, 0.16f, 0.55f), b.Hull);
            b.Box(new Vector3(0, 0.09f, 0.05f), new Vector3(0.18f, 0.03f, 0.4f), b.Accent);
            b.Frustum(0.27f, 0.45f, new Vector2(0.16f, 0.12f), new Vector2(0.08f, 0.06f), 0, b.Dark);
            b.Box(new Vector3(0, 0, -0.35f), new Vector3(0.18f, 0.12f, 0.15f), b.Dark);
            b.Nozzle(new Vector3(-0.05f, 0, -0.45f), 0.04f, 0.05f);
            b.Nozzle(new Vector3(0.05f, 0, -0.45f), 0.04f, 0.05f);
        }

        static void BuildCourier(Builder b)
        {
            b.Frustum(-0.4f, 0.5f, new Vector2(0.18f, 0.06f), new Vector2(0.01f, 0.01f), 0, b.Hull);
            b.Box(new Vector3(0, 0.035f, 0.05f), new Vector3(0.05f, 0.02f, 0.15f), b.Glow(new Color(0.6f, 0.85f, 1f)));
            b.Box(new Vector3(0, 0, -0.1f), new Vector3(0.4f, 0.01f, 0.12f), b.Accent);
            b.Nozzle(new Vector3(0, 0, -0.44f), 0.03f, 0.05f);
        }

        static void Turret(Builder b, float z, float y, int facing)
        {
            b.Box(new Vector3(0, y, z), new Vector3(0.05f, 0.022f, 0.05f), b.Hull);
            float barrelZ = z + facing * 0.055f;
            b.Cylinder(new Vector3(-0.011f, y + 0.003f, barrelZ), 0.0045f, 0.07f, b.Dark);
            b.Cylinder(new Vector3(0.011f, y + 0.003f, barrelZ), 0.0045f, 0.07f, b.Dark);
        }

        sealed class Builder
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<Color> colors = new List<Color>();
            readonly List<int> triangles = new List<int>();

            public readonly Color Hull;
            public readonly Color Dark = new Color(0.2f, 0.21f, 0.24f, 1f);
            public readonly Color Light = new Color(0.85f, 0.86f, 0.88f, 1f);
            public readonly Color Accent;

            public Builder(Faction faction)
            {
                var f = new Color(faction.Color.R, faction.Color.G, faction.Color.B, 1f);
                Hull = Color.Lerp(new Color(0.6f, 0.62f, 0.66f, 1f), f, 0.12f);
                Accent = f;
            }

            public Color Glow(Color c) => new Color(c.r, c.g, c.b, 0f);

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
            {
                var n = Vector3.Cross(b - a, c - a).normalized;
                int i = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                vertices.Add(d);
                for (int k = 0; k < 4; k++)
                {
                    normals.Add(n);
                    colors.Add(color);
                }
                triangles.Add(i);
                triangles.Add(i + 1);
                triangles.Add(i + 2);
                triangles.Add(i);
                triangles.Add(i + 2);
                triangles.Add(i + 3);
            }

            /// <summary>Frustum along Z from z0 (back size) to z1 (front size), centred at height y.</summary>
            public void Frustum(float z0, float z1, Vector2 back, Vector2 front, float y, Color color)
            {
                Vector3 B(float sx, float sy) => new Vector3(sx * back.x * 0.5f, y + sy * back.y * 0.5f, z0);
                Vector3 F(float sx, float sy) => new Vector3(sx * front.x * 0.5f, y + sy * front.y * 0.5f, z1);
                // Unity front faces wind clockwise as seen from outside.
                Quad(F(1, -1), F(1, 1), F(-1, 1), F(-1, -1), color);
                Quad(B(-1, -1), B(-1, 1), B(1, 1), B(1, -1), color);
                Quad(F(-1, 1), F(1, 1), B(1, 1), B(-1, 1), color);
                Quad(F(1, -1), F(-1, -1), B(-1, -1), B(1, -1), color);
                Quad(F(-1, -1), F(-1, 1), B(-1, 1), B(-1, -1), color);
                Quad(F(1, 1), F(1, -1), B(1, -1), B(1, 1), color);
            }

            public void Box(Vector3 center, Vector3 size, Color color)
            {
                var h = size * 0.5f;
                Frustum(center.z - h.z, center.z + h.z, new Vector2(size.x, size.y), new Vector2(size.x, size.y), center.y, color);
                if (center.x != 0) OffsetLast(24, new Vector3(center.x, 0, 0));
            }

            void OffsetLast(int count, Vector3 offset)
            {
                for (int i = vertices.Count - count; i < vertices.Count; i++) vertices[i] += offset;
            }

            /// <summary>Cylinder along Z centred at <paramref name="center"/>.</summary>
            public void Cylinder(Vector3 center, float radius, float length, Color color, bool emissiveBack = false, int segments = 10)
            {
                float z0 = center.z - length * 0.5f, z1 = center.z + length * 0.5f;
                for (int s = 0; s < segments; s++)
                {
                    float a0 = Mathf.PI * 2 * s / segments, a1 = Mathf.PI * 2 * (s + 1) / segments;
                    var p0 = new Vector3(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius, 0) + new Vector3(center.x, center.y, 0);
                    var p1 = new Vector3(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius, 0) + new Vector3(center.x, center.y, 0);
                    Quad(new Vector3(p0.x, p0.y, z0), new Vector3(p1.x, p1.y, z0), new Vector3(p1.x, p1.y, z1), new Vector3(p0.x, p0.y, z1), color);
                    var c0 = new Vector3(center.x, center.y, z0);
                    var back = emissiveBack ? Glow(Color.white) : color;
                    Quad(c0, new Vector3(p1.x, p1.y, z0), new Vector3(p0.x, p0.y, z0), c0, back);
                    var c1 = new Vector3(center.x, center.y, z1);
                    Quad(c1, new Vector3(p0.x, p0.y, z1), new Vector3(p1.x, p1.y, z1), c1, color);
                }
            }

            public void Nozzle(Vector3 center, float radius, float length) =>
                Cylinder(center, radius, length, Dark, emissiveBack: true, segments: 12);

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetColors(colors);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
