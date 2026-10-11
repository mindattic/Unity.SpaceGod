using System.Collections.Generic;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Rendering
{
    /// <summary>
    /// Kit-bashed hulls built from boxes, frustums and cylinders. Every design is one unit long on +Z
    /// (nose forward), flat-shaded, with the paint scheme in vertex colours and engine bells flagged
    /// emissive through vertex alpha (see Ship.shader).
    ///
    /// Warship hulls are bare: their guns, launchers, shield emitters and sensor masts are separate
    /// parts (<see cref="GetPart"/>) bolted on at the hardpoints the combat model uses, so each can
    /// traverse, be shot away and leave a scar. Warship hulls also come in two sections, bow and
    /// stern, split amidships, so a destroyed ship breaks in two.
    /// </summary>
    public static class ShipMeshFactory
    {
        static readonly Dictionary<(ShipClass, Faction, int), Mesh> Cache = new Dictionary<(ShipClass, Faction, int), Mesh>();
        static readonly Dictionary<(SubsystemKind, Faction, bool), Mesh> PartCache = new Dictionary<(SubsystemKind, Faction, bool), Mesh>();
        static readonly Dictionary<Faction, Mesh> PlatformCache = new Dictionary<Faction, Mesh>();
        static Mesh scar;

        public const int WholeHull = -1, Stern = 0, Bow = 1;

        /// <summary>Where warship hulls break in two (ship lengths along the keel).</summary>
        public const float SplitZ = -0.06f;

        public static bool IsWarship(ShipClass c) => c == ShipClass.Carrier || c == ShipClass.Battleship || c == ShipClass.Cruiser || c == ShipClass.Destroyer;

        /// <summary>The whole hull (both sections).</summary>
        public static Mesh Get(ShipClass shipClass, Faction faction) => GetSection(shipClass, faction, WholeHull);

        /// <summary>One section of a hull: <see cref="Bow"/>, <see cref="Stern"/> or <see cref="WholeHull"/>.</summary>
        public static Mesh GetSection(ShipClass shipClass, Faction faction, int section)
        {
            if (Cache.TryGetValue((shipClass, faction, section), out var mesh)) return mesh;
            var b = new Builder(faction, section);
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
            mesh = b.ToMesh(shipClass + "_" + faction.Prefix + "_" + section);
            Cache[(shipClass, faction, section)] = mesh;
            return mesh;
        }

        /// <summary>An orbital defence platform: a gun core inside a ring (its guns are parts too).</summary>
        public static Mesh GetPlatform(Faction faction)
        {
            if (PlatformCache.TryGetValue(faction, out var mesh)) return mesh;
            var b = new Builder(faction, WholeHull);
            b.Box(new Vector3(0, 0, 0), new Vector3(0.16f, 0.32f, 0.16f), b.Hull);
            b.Box(new Vector3(0, 0, 0), new Vector3(0.2f, 0.06f, 0.2f), b.Dark);
            for (int k = 0; k < 8; k++)
            {
                float a = Mathf.PI * 2 * k / 8;
                var p = new Vector3(Mathf.Cos(a) * 0.4f, 0, Mathf.Sin(a) * 0.4f);
                b.Box(p, new Vector3(0.12f, 0.035f, 0.12f), k % 2 == 0 ? b.Accent : b.Hull);
            }
            b.Box(new Vector3(0.24f, 0, 0), new Vector3(0.3f, 0.02f, 0.02f), b.Dark);
            b.Box(new Vector3(-0.24f, 0, 0), new Vector3(0.3f, 0.02f, 0.02f), b.Dark);
            b.Box(new Vector3(0, 0, 0), new Vector3(0.02f, 0.02f, 0.78f), b.Dark);
            b.Box(new Vector3(0, 0.03f, 0.3f), new Vector3(0.1f, 0.01f, 0.02f), b.Glow(new Color(1f, 0.85f, 0.55f)));
            mesh = b.ToMesh("Platform_" + faction.Prefix);
            PlatformCache[faction] = mesh;
            return mesh;
        }

        /// <summary>
        /// A hardpoint's model, centred on its mount, built upright (+Y) and facing +Z, in ship lengths.
        /// Turrets come in two meshes: the traversing base and the elevating barrels.
        /// </summary>
        public static Mesh GetPart(SubsystemKind kind, Faction faction, bool barrels)
        {
            if (PartCache.TryGetValue((kind, faction, barrels), out var mesh)) return mesh;
            var b = new Builder(faction, WholeHull);
            switch (kind)
            {
                case SubsystemKind.HeavyLaser:
                    if (barrels)
                    {
                        b.Cylinder(new Vector3(-0.011f, 0, 0.04f), 0.0048f, 0.075f, b.Dark);
                        b.Cylinder(new Vector3(0.011f, 0, 0.04f), 0.0048f, 0.075f, b.Dark);
                        b.Box(new Vector3(0, 0, 0.004f), new Vector3(0.036f, 0.012f, 0.02f), b.Hull);
                    }
                    else
                    {
                        b.Box(new Vector3(0, 0.01f, 0), new Vector3(0.056f, 0.022f, 0.056f), b.Hull);
                        b.Box(new Vector3(0, 0.023f, -0.01f), new Vector3(0.03f, 0.006f, 0.024f), b.Accent);
                    }
                    break;
                case SubsystemKind.LaserTurret:
                    if (barrels)
                    {
                        b.Cylinder(new Vector3(-0.007f, 0, 0.026f), 0.003f, 0.05f, b.Dark);
                        b.Cylinder(new Vector3(0.007f, 0, 0.026f), 0.003f, 0.05f, b.Dark);
                    }
                    else b.Box(new Vector3(0, 0.007f, 0), new Vector3(0.036f, 0.015f, 0.036f), b.Hull);
                    break;
                case SubsystemKind.PointDefense:
                    if (barrels) b.Cylinder(new Vector3(0, 0, 0.015f), 0.0018f, 0.028f, b.Dark);
                    else b.Box(new Vector3(0, 0.005f, 0), new Vector3(0.016f, 0.012f, 0.016f), b.Light);
                    break;
                case SubsystemKind.MissileLauncher:
                    b.Box(new Vector3(0, 0.008f, 0), new Vector3(0.03f, 0.018f, 0.05f), b.Hull);
                    for (int r = 0; r < 2; r++)
                        for (int c = 0; c < 2; c++)
                            b.Box(new Vector3(-0.007f + c * 0.014f, 0.004f + r * 0.009f, 0.026f), new Vector3(0.008f, 0.006f, 0.003f), b.Glow(new Color(1f, 0.55f, 0.3f)));
                    break;
                case SubsystemKind.ShieldGenerator:
                    b.Cylinder(new Vector3(0, 0.008f, 0), 0.008f, 0.016f, b.Dark);
                    b.Box(new Vector3(0, 0.018f, 0), new Vector3(0.03f, 0.004f, 0.03f), b.Glow(new Color(0.4f, 0.75f, 1f)));
                    break;
                case SubsystemKind.Bridge:
                    b.Box(new Vector3(0, 0.02f, 0), new Vector3(0.004f, 0.04f, 0.004f), b.Light);
                    b.Box(new Vector3(0, 0.035f, 0), new Vector3(0.024f, 0.003f, 0.003f), b.Light);
                    b.Box(new Vector3(0, 0.005f, 0.012f), new Vector3(0.02f, 0.006f, 0.004f), b.Glow(new Color(0.75f, 0.9f, 1f)));
                    break;
                case SubsystemKind.Hangar:
                    b.Box(new Vector3(0, 0, 0), new Vector3(0.05f, 0.014f, 0.006f), b.Glow(new Color(1f, 0.85f, 0.55f)));
                    break;
                default:
                    b.Box(Vector3.zero, new Vector3(0.01f, 0.01f, 0.01f), b.Dark);
                    break;
            }
            mesh = b.ToMesh("Part_" + kind + (barrels ? "_Barrels" : "") + "_" + faction.Prefix);
            PartCache[(kind, faction, barrels)] = mesh;
            return mesh;
        }

        static readonly Dictionary<(Faction, bool), Mesh> FighterCache = new Dictionary<(Faction, bool), Mesh>();

        /// <summary>A carrier fighter (swept wings, twin guns) or bomber (heavier, torpedo slung under).</summary>
        public static Mesh GetFighter(Faction faction, bool bomber)
        {
            if (FighterCache.TryGetValue((faction, bomber), out var mesh)) return mesh;
            var b = new Builder(faction, WholeHull);
            if (bomber)
            {
                b.Frustum(-0.45f, 0.45f, new Vector2(0.22f, 0.16f), new Vector2(0.08f, 0.07f), 0, b.Hull);
                b.Box(new Vector3(0, -0.02f, -0.05f), new Vector3(0.9f, 0.03f, 0.25f), b.Accent);
                b.Box(new Vector3(0, -0.12f, 0.05f), new Vector3(0.07f, 0.07f, 0.55f), b.Dark);
                b.Box(new Vector3(0, 0.07f, 0.15f), new Vector3(0.09f, 0.04f, 0.18f), b.Glow(new Color(0.6f, 0.85f, 1f)));
                b.Nozzle(new Vector3(-0.07f, 0, -0.47f), 0.05f, 0.06f);
                b.Nozzle(new Vector3(0.07f, 0, -0.47f), 0.05f, 0.06f);
            }
            else
            {
                b.Frustum(-0.45f, 0.5f, new Vector2(0.16f, 0.12f), new Vector2(0.02f, 0.02f), 0, b.Hull);
                b.Frustum(-0.35f, 0.05f, new Vector2(0.95f, 0.025f), new Vector2(0.12f, 0.02f), 0, b.Accent);
                b.Box(new Vector3(0.22f, 0, 0.15f), new Vector3(0.02f, 0.02f, 0.4f), b.Dark);
                b.Box(new Vector3(-0.22f, 0, 0.15f), new Vector3(0.02f, 0.02f, 0.4f), b.Dark);
                b.Box(new Vector3(0, 0.06f, 0.12f), new Vector3(0.07f, 0.04f, 0.16f), b.Glow(new Color(0.6f, 0.85f, 1f)));
                b.Nozzle(new Vector3(0, 0, -0.47f), 0.06f, 0.06f);
            }
            mesh = b.ToMesh((bomber ? "Bomber_" : "Fighter_") + faction.Prefix);
            FighterCache[(faction, bomber)] = mesh;
            return mesh;
        }

        /// <summary>A blackened stub left where a hardpoint was shot away.</summary>
        public static Mesh GetScar()
        {
            if (scar != null) return scar;
            var b = new Builder(null, WholeHull);
            var burnt = new Color(0.07f, 0.06f, 0.06f, 1f);
            b.Box(new Vector3(0, 0.003f, 0), new Vector3(0.03f, 0.006f, 0.03f), burnt);
            b.Box(new Vector3(0.006f, 0.008f, 0.004f), new Vector3(0.012f, 0.008f, 0.006f), burnt);
            b.Box(new Vector3(-0.008f, 0.006f, -0.006f), new Vector3(0.008f, 0.01f, 0.01f), new Color(0.12f, 0.08f, 0.05f, 1f));
            scar = b.ToMesh("Scar");
            return scar;
        }

        static void BuildBattleship(Builder b)
        {
            b.Frustum(-0.42f, 0.30f, new Vector2(0.15f, 0.085f), new Vector2(0.11f, 0.06f), 0, b.Hull);
            b.Frustum(0.30f, 0.50f, new Vector2(0.11f, 0.06f), new Vector2(0.018f, 0.016f), 0, b.Hull);
            b.Frustum(-0.36f, 0.26f, new Vector2(0.05f, 0.03f), new Vector2(0.03f, 0.02f), -0.055f, b.Dark);
            foreach (float side in new[] { -1f, 1f })
            {
                b.Box(new Vector3(side * 0.071f, 0.0f, -0.05f), new Vector3(0.008f, 0.022f, 0.6f), b.Accent);
                b.Box(new Vector3(side * 0.06f, 0.035f, -0.02f), new Vector3(0.03f, 0.012f, 0.035f), b.Hull);
            }
            b.Box(new Vector3(0, 0.065f, -0.12f), new Vector3(0.06f, 0.05f, 0.14f), b.Hull);
            b.Box(new Vector3(0, 0.098f, -0.1f), new Vector3(0.09f, 0.018f, 0.035f), b.Dark);
            // Turret barbettes: the turrets themselves are parts.
            b.Box(new Vector3(0, 0.038f, 0.20f), new Vector3(0.05f, 0.01f, 0.05f), b.Dark);
            b.Box(new Vector3(0, 0.048f, 0.07f), new Vector3(0.05f, 0.03f, 0.05f), b.Hull);
            b.Box(new Vector3(0, 0.038f, -0.28f), new Vector3(0.05f, 0.01f, 0.05f), b.Dark);
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
            b.Box(new Vector3(0, 0.012f, 0.455f), new Vector3(0.11f, 0.028f, 0.02f), b.Dark);
            foreach (float side in new[] { -1f, 1f })
            {
                b.Box(new Vector3(side * 0.115f, 0, -0.05f), new Vector3(0.03f, 0.05f, 0.5f), b.Hull);
                b.Box(new Vector3(side * 0.131f, 0, -0.05f), new Vector3(0.004f, 0.012f, 0.45f), b.Accent);
            }
            b.Box(new Vector3(0.07f, 0.09f, -0.12f), new Vector3(0.035f, 0.08f, 0.13f), b.Hull);
            b.Box(new Vector3(0.07f, 0.135f, -0.11f), new Vector3(0.05f, 0.015f, 0.05f), b.Dark);
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
            b.Box(new Vector3(0, 0.024f, 0.15f), new Vector3(0.036f, 0.006f, 0.036f), b.Dark);
            b.Box(new Vector3(0, 0.024f, 0.0f), new Vector3(0.036f, 0.006f, 0.036f), b.Dark);
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
            b.Box(new Vector3(0, 0.026f, 0.12f), new Vector3(0.036f, 0.006f, 0.036f), b.Dark);
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

        /// <summary>
        /// Accumulates flat-shaded primitives. With a section chosen, only primitives (or the parts of
        /// frustums) on that side of <see cref="SplitZ"/> are kept, so the two halves meet exactly.
        /// </summary>
        sealed class Builder
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<Color> colors = new List<Color>();
            readonly List<int> triangles = new List<int>();
            readonly int section;
            bool keep = true;

            public readonly Color Hull;
            public readonly Color Dark = new Color(0.2f, 0.21f, 0.24f, 1f);
            public readonly Color Light = new Color(0.85f, 0.86f, 0.88f, 1f);
            public readonly Color Accent;

            public Builder(Faction faction, int section)
            {
                this.section = section;
                var f = faction != null ? new Color(faction.Color.R, faction.Color.G, faction.Color.B, 1f) : Color.gray;
                Hull = Color.Lerp(new Color(0.6f, 0.62f, 0.66f, 1f), f, 0.12f);
                Accent = f;
            }

            public Color Glow(Color c) => new Color(c.r, c.g, c.b, 0f);

            void Keep(float z) => keep = section == WholeHull || (section == Bow) == (z >= SplitZ);

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
            {
                if (!keep) return;
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
                if (section != WholeHull && z0 < SplitZ && z1 > SplitZ)
                {
                    // Cut at the split so each section gets its own closed piece.
                    var mid = Vector2.Lerp(back, front, (SplitZ - z0) / (z1 - z0));
                    Frustum(z0, SplitZ, back, mid, y, color);
                    Frustum(SplitZ, z1, mid, front, y, color);
                    return;
                }
                Keep((z0 + z1) * 0.5f);
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
                int before = vertices.Count;
                Frustum(center.z - h.z, center.z + h.z, new Vector2(size.x, size.y), new Vector2(size.x, size.y), center.y, color);
                if (center.x != 0)
                    for (int i = before; i < vertices.Count; i++) vertices[i] += new Vector3(center.x, 0, 0);
            }

            /// <summary>Cylinder along Z centred at <paramref name="center"/>.</summary>
            public void Cylinder(Vector3 center, float radius, float length, Color color, bool emissiveBack = false, int segments = 10)
            {
                Keep(center.z);
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
