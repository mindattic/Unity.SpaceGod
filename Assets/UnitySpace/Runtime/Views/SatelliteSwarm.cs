using System.Collections.Generic;
using UnitySpace.Core;
using UnitySpace.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnitySpace.Views
{
    /// <summary>
    /// Satellite constellations (Starlink, GPS, Galileo, GLONASS, BeiDou, the geostationary belt) as
    /// clouds of pixel-sized points, propagated on the CPU on their circular orbits and placed with
    /// the planet's own neighbourhood mapping. They fade out with distance from the planet.
    /// </summary>
    public sealed class SatelliteSwarm
    {
        readonly UnitySpaceApp app;
        readonly List<Cloud> clouds = new List<Cloud>();

        sealed class Cloud
        {
            public Constellation C;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public Material Material;
            public DVec3[] Offsets;
            public Vector3[] Vertices;
            public Transform T;
        }

        public SatelliteSwarm(UnitySpaceApp app, Transform parent)
        {
            this.app = app;
            var infra = app.World.Infrastructure;
            if (infra == null) return;
            foreach (var c in infra.Constellations)
            {
                int n = c.Count;
                var go = new GameObject(c.Name);
                go.transform.SetParent(parent, false);
                var mesh = Materials.Own(new Mesh { name = c.Name, indexFormat = IndexFormat.UInt32 });
                mesh.MarkDynamic();
                var vertices = new Vector3[n];
                var colors = new Color[n];
                var indices = new int[n];
                for (int i = 0; i < n; i++)
                {
                    colors[i] = new Color(c.Color.R, c.Color.G, c.Color.B, 0.85f);
                    indices[i] = i;
                }
                mesh.vertices = vertices;
                mesh.colors = colors;
                mesh.SetIndices(indices, MeshTopology.Points, 0, false);
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e7f);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                var material = Materials.Create(Materials.Points, c.Name);
                material.SetFloat("_Brightness", 1.4f);
                mr.sharedMaterial = material;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                clouds.Add(new Cloud { C = c, Mesh = mesh, Renderer = mr, Material = material, Offsets = new DVec3[n], Vertices = vertices, T = go.transform });
            }
        }

        public void Update(bool visible)
        {
            var scale = app.Scale;
            double jd = app.Clock.Jd;
            foreach (var cl in clouds)
            {
                var body = app.Catalog[cl.C.Body];
                var centre = app.BodyScene(cl.C.Body);
                double camera = (app.SpaceCamera.ScenePosition - centre).Length;
                double reach = scale.LocalDistance(body, 42164 * 1.2);
                // Only worth drawing within a few times the belt's reach, and dimmed past it.
                float alpha = Mathf.Clamp01(2.2f - (float)(camera / (reach * 6)));
                cl.Renderer.enabled = visible && alpha > 0.02f;
                if (!cl.Renderer.enabled) continue;
                cl.C.Offsets(body, jd, cl.Offsets);
                for (int i = 0; i < cl.Offsets.Length; i++)
                {
                    var o = scale.LocalOffset(body, cl.Offsets[i]);
                    cl.Vertices[i] = new Vector3((float)o.X, (float)o.Y, (float)o.Z);
                }
                cl.Mesh.vertices = cl.Vertices;
                cl.T.localPosition = app.Relative(centre);
                cl.Material.SetFloat("_Brightness", 1.4f * alpha);
            }
        }
    }
}
