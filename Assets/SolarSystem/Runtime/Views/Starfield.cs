using SolarSystem.Core;
using SolarSystem.Rendering;
using UnityEngine;

namespace SolarSystem.Views
{
    /// <summary>
    /// A point-cloud sky: uniformly scattered stars plus a denser band along the galactic plane
    /// (galactic north pole at RA 192.86°, Dec 27.13°, so the Milky Way sits where it really does).
    /// </summary>
    public static class Starfield
    {
        public static GameObject Create(Transform parent, int count = 9000, int seed = 7)
        {
            var rng = new System.Random(seed);
            var galacticPole = BodyView.ToUnity(Astro.PoleFromRaDec(192.86, 27.13)).normalized;
            var vertices = new Vector3[count];
            var colors = new Color[count];
            var indices = new int[count];

            for (int i = 0; i < count; i++)
            {
                Vector3 dir = RandomDirection(rng);
                if (i % 3 == 0)
                {
                    // Squash toward the galactic plane.
                    float h = Vector3.Dot(dir, galacticPole);
                    dir = (dir - galacticPole * h * 0.88f).normalized;
                }
                vertices[i] = dir;
                indices[i] = i;

                double r = rng.NextDouble();
                float brightness = (float)(0.12 + 0.88 * System.Math.Pow(r, 7));
                float temperature = (float)rng.NextDouble();
                var tint = temperature < 0.25f ? new Color(0.7f, 0.8f, 1f)
                    : temperature < 0.8f ? new Color(1f, 0.97f, 0.92f)
                    : new Color(1f, 0.8f, 0.6f);
                colors[i] = new Color(tint.r, tint.g, tint.b, brightness);
            }

            var mesh = new Mesh { name = "Starfield" };
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.SetIndices(indices, MeshTopology.Points, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);

            var go = new GameObject("Starfield");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var material = Materials.Create(Materials.Stars, "Stars");
            material.SetFloat("_Brightness", 1.1f);
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static Vector3 RandomDirection(System.Random rng)
        {
            double z = rng.NextDouble() * 2 - 1;
            double a = rng.NextDouble() * System.Math.PI * 2;
            double s = System.Math.Sqrt(1 - z * z);
            return new Vector3((float)(s * System.Math.Cos(a)), (float)z, (float)(s * System.Math.Sin(a)));
        }
    }
}
