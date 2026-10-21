using System;
using System.Collections.Generic;
using Unity.SpaceGod.Core;
using Unity.SpaceGod.Rendering;
using UnityEngine;

namespace Unity.SpaceGod.Views
{
    /// <summary>
    /// The view between star systems: the simulated systems' stars and the other nearest stars at
    /// their real positions (light-years, J2000 ecliptic axes like everything else), each a glow sized
    /// by its luminosity, with the jump-gate lattice (<see cref="GateNetwork"/>) drawn as lines (red
    /// where a gate is under blockade) and the ships inside the gates as sparks moving along them. It
    /// has its own coordinates (the camera's position in light-years) and never mixes with a system's
    /// scene. The seed of a galactic map.
    /// </summary>
    public sealed class InterstellarView
    {
        public readonly GameObject Root;
        readonly List<Star> stars = new List<Star>();
        readonly List<LineStrip> links = new List<LineStrip>();
        readonly List<(Transform t, Material m)> sparks = new List<(Transform, Material)>();
        readonly Galaxy galaxy;
        static Mesh quad;

        /// <summary>Scene units per light-year inside the view.</summary>
        public const double UnitsPerLy = 1000;

        public sealed class Star
        {
            public string Name;

            /// <summary>What the HUD writes beside it: the system's name for a simulated system's first star, null for none.</summary>
            public string Label;

            /// <summary>The simulated system whose name it carries (its first star), for who holds it; null otherwise.</summary>
            public StarSystem Of;

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
                bool labelled = false;
                foreach (var b in system.Catalog.Bodies)
                {
                    if (!b.IsStar) continue;
                    var offsetLy = system.Ephemeris.Helio(b.Index) / Astro.LightYearKm;
                    var c = b.Color;
                    float size = Mathf.Clamp(Mathf.Pow((float)b.LuminositySolar, 0.25f), 0.45f, 3f);
                    // The system's first star carries its name; a companion far enough out to stand apart
                    // (Proxima, Epsilon Indi B) its own; a close partner (Alpha Centauri B, Sirius B) none, as it would overlap.
                    bool far = system.Ephemeris.Helio(b.Index).Length > 500 * Astro.AuKm;
                    string label = !labelled ? (b.Name == "Sun" ? "Sol" : system.Name) : far ? b.Name : null;
                    var star = Add(b.Name, system.PositionLy + offsetLy, new Color(c.R, c.G, c.B), size, system: !labelled || far, label);
                    if (!labelled) star.Of = system;
                    labelled = true;
                }
            }
            foreach (var n in galaxy.NearbyStars)
            {
                double absMag = n.AbsoluteMagnitude;
                float luminosity = double.IsNaN(absMag) ? 1e-5f : Mathf.Pow(10, -0.4f * (float)(absMag - 4.83));
                float size = double.IsNaN(absMag) ? 0.35f : Mathf.Clamp(Mathf.Pow(luminosity, 0.25f), 0.45f, 3f);
                Add(n.Name, n.PositionLy, new Color(n.Color.R, n.Color.G, n.Color.B), size, false, n.Name);
            }
            foreach (var l in galaxy.Network.Links) links.Add(new LineStrip(l.ToString(), Root.transform));
            Root.SetActive(false);
        }

        Star Add(string name, DVec3 ly, Color color, float size, bool system, string label)
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
            var star = new Star { Name = name, Label = label, PositionLy = ly, Color = color, Size = size, System = system, T = go.transform, Material = m };
            stars.Add(star);
            return star;
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

            // The gate links, each its own strip: pale blue when open, red when either end is shut.
            double jd = galaxy.Systems.Count > 0 ? galaxy.Systems[0].Ephemeris.Jd : 0;
            Vector3 At(DVec3 ly)
            {
                var d = (ly - cameraLy).SwapYZ * UnitsPerLy;
                return new Vector3((float)d.X, (float)d.Y, (float)d.Z);
            }
            var pair = new Vector3[2];
            var net = galaxy.Network.Links;
            // A strip for each link finished since (U.SPC-T97).
            while (links.Count < net.Count) links.Add(new LineStrip(net[links.Count].ToString(), Root.transform));
            for (int i = 0; i < net.Count && i < links.Count; i++)
            {
                var l = net[i];
                bool shut = l.AtA.Blockaded(jd) || l.AtB.Blockaded(jd);
                pair[0] = At(l.A.PositionLy);
                pair[1] = At(l.B.PositionLy);
                // Grey and faint while a ring is out of action, red under blockade, pale blue when open.
                var c = !l.Working ? new Color(0.5f, 0.5f, 0.5f, 0.25f * fade)
                    : shut ? new Color(1f, 0.35f, 0.3f, 0.55f * fade) : new Color(0.45f, 0.85f, 1f, 0.4f * fade);
                links[i].SetPoints(pair, 2, c, 1f, 1f);
                links[i].SetTint(Color.white);
                links[i].Root.transform.localPosition = Vector3.zero;
                links[i].Visible = true;
            }

            // Ships in the gates: a spark each, partway along its link.
            var inGates = galaxy.Traffic.InGates;
            while (sparks.Count < inGates.Count)
            {
                var go = new GameObject("Passage");
                go.transform.SetParent(Root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var mr = go.AddComponent<MeshRenderer>();
                var m = Materials.Create(Materials.Glow, "Passage");
                m.SetFloat("_Falloff", 2.4f);
                mr.sharedMaterial = m;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sparks.Add((go.transform, m));
            }
            for (int i = 0; i < sparks.Count; i++)
            {
                var (t, m) = sparks[i];
                bool on = i < inGates.Count;
                t.gameObject.SetActive(on);
                if (!on) continue;
                var p = inGates[i];
                var from = p.From.Home.PositionLy;
                var ly = from + (p.To.Home.PositionLy - from) * p.Progress(jd);
                var rel = At(ly);
                float dist = Mathf.Max(rel.magnitude, 1e-3f);
                t.localPosition = rel;
                t.localRotation = Quaternion.LookRotation(rel / dist, cameraRotation * Vector3.up);
                t.localScale = Vector3.one * dist * 0.012f;
                var fc = p.Leader.Faction.Color;
                m.SetColor("_Color", new Color(fc.R, fc.G, fc.B));
                m.SetFloat("_Intensity", 1.8f * fade);
            }
        }
    }
}
