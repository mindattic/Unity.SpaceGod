using System;
using System.Collections.Generic;
using SolarSystem.Core;
using SolarSystem.Rendering;
using UnityEngine;

namespace SolarSystem.Views
{
    /// <summary>
    /// The view between star systems: the Sun, Alpha Centauri and the twenty nearest stars beyond at
    /// their real positions (light-years, J2000 ecliptic axes like everything else), each a glow sized
    /// by its luminosity, with the route between the simulated systems drawn as a line. It has its
    /// own coordinates (the camera's position in light-years) and never mixes with a system's scene.
    /// The seed of a galactic map.
    /// </summary>
    public sealed class InterstellarView
    {
        public readonly GameObject Root;
        readonly List<Star> stars = new List<Star>();
        readonly LineStrip route;
        readonly Galaxy galaxy;
        static Mesh quad;

        /// <summary>Scene units per light-year inside the view.</summary>
        public const double UnitsPerLy = 1000;

        public sealed class Star
        {
            public string Name;
            public DVec3 PositionLy;
            public Color Color;
            public float Size;
            public bool System;
            public Transform T;
            public Material Material;

            /// <summary>Camera-relative position this frame.</summary>
            public Vector3 Relative;
        }

        public IReadOnlyList<Star> Stars => stars;

        public InterstellarView(Galaxy galaxy, Transform parent)
        {
            this.galaxy = galaxy;
            Root = new GameObject("Interstellar");
            Root.transform.SetParent(parent, false);
            quad ??= MeshFactory.Quad();

            foreach (var system in galaxy.Systems)
            {
                foreach (var b in system.Catalog.Bodies)
                {
                    if (!b.IsStar) continue;
                    var offsetLy = system.Ephemeris.Helio(b.Index) / Astro.LightYearKm;
                    var c = b.Color;
                    float size = Mathf.Clamp(Mathf.Pow((float)b.LuminositySolar, 0.25f), 0.45f, 3f);
                    Add(b.Name == "Sun" ? "Sun" : b.Name, system.PositionLy + offsetLy, new Color(c.R, c.G, c.B), size,
                        system: b.Index == system.Catalog.Root.Index || (b.ParentIndex == 0 && b.Name.EndsWith(" A")));
                }
            }
            foreach (var n in galaxy.NearbyStars)
            {
                double absMag = n.AbsoluteMagnitude;
                float luminosity = double.IsNaN(absMag) ? 1e-5f : Mathf.Pow(10, -0.4f * (float)(absMag - 4.83));
                float size = double.IsNaN(absMag) ? 0.35f : Mathf.Clamp(Mathf.Pow(luminosity, 0.25f), 0.45f, 3f);
                Add(n.Name, n.PositionLy, new Color(n.Color.R, n.Color.G, n.Color.B), size, false);
            }
            route = new LineStrip("Route", Root.transform);
            Root.SetActive(false);
        }

        void Add(string name, DVec3 ly, Color color, float size, bool system)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var mr = go.AddComponent<MeshRenderer>();
            var m = Materials.Create(Materials.Glow, name);
            m.SetColor("_Color", Color.Lerp(color, Color.white, 0.25f));
            m.SetFloat("_Intensity", 1.6f);
            m.SetFloat("_Falloff", 2.4f);
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            stars.Add(new Star { Name = name, PositionLy = ly, Color = color, Size = size, System = system, T = go.transform, Material = m });
        }

        public bool Visible
        {
            get => Root.activeSelf;
            set => Root.SetActive(value);
        }

        /// <summary>Places every star relative to a camera at <paramref name="cameraLy"/>, keeping each a steady few pixels across.</summary>
        public void Update(DVec3 cameraLy, Quaternion cameraRotation, float fade)
        {
            if (!Visible) return;
            foreach (var s in stars)
            {
                var d = (s.PositionLy - cameraLy).SwapYZ * UnitsPerLy;
                var rel = new Vector3((float)d.X, (float)d.Y, (float)d.Z);
                s.Relative = rel;
                float dist = Mathf.Max(rel.magnitude, 1e-3f);
                s.T.localPosition = rel;
                s.T.localRotation = Quaternion.LookRotation(rel / dist, cameraRotation * Vector3.up);
                // Steady angular size, a little larger as the camera closes in.
                float near = Mathf.Clamp(1.6f - dist / 1500f, 1f, 3.2f);
                s.T.localScale = Vector3.one * dist * 0.022f * s.Size * near;
                s.Material.SetFloat("_Intensity", 1.6f * fade);
            }

            var points = new Vector3[galaxy.Systems.Count];
            for (int i = 0; i < points.Length; i++)
            {
                var d = (galaxy.Systems[i].PositionLy - cameraLy).SwapYZ * UnitsPerLy;
                points[i] = new Vector3((float)d.X, (float)d.Y, (float)d.Z);
            }
            route.SetPoints(points, points.Length, new Color(0.45f, 0.85f, 1f, 0.55f * fade), 1f, 1f);
            route.SetTint(Color.white);
            route.Root.transform.localPosition = Vector3.zero;
            route.Visible = points.Length > 1;
        }
    }
}
