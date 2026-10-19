using UnityEngine;
using UnityEngine.Rendering;

namespace UnitySpace.Rendering
{
    /// <summary>Procedural meshes: a lat-long sphere matching equirectangular maps, a ring annulus and a quad.</summary>
    public static class MeshFactory
    {
        /// <summary>
        /// Unit-radius UV sphere. u runs 0..1 eastward from longitude -180°, v runs 0..1 from the
        /// south pole, which is the layout of the equirectangular NASA/USGS colour maps.
        /// </summary>
        public static Mesh UvSphere(int longitudeSegments = 128, int latitudeSegments = 64)
        {
            int cols = longitudeSegments + 1;
            int rows = latitudeSegments + 1;
            var vertices = new Vector3[cols * rows];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            for (int y = 0; y < rows; y++)
            {
                float v = (float)y / latitudeSegments;
                float lat = (v - 0.5f) * Mathf.PI;
                for (int x = 0; x < cols; x++)
                {
                    float u = (float)x / longitudeSegments;
                    float lon = (u - 0.5f) * Mathf.PI * 2f;
                    // East (increasing u) runs toward +Z, so seen from outside with north up the map is
                    // not mirrored and the triangles below wind clockwise (Unity's front face).
                    var n = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                    int i = y * cols + x;
                    vertices[i] = n;
                    normals[i] = n;
                    uvs[i] = new Vector2(u, v);
                }
            }

            var triangles = new int[longitudeSegments * latitudeSegments * 6];
            int t = 0;
            for (int y = 0; y < latitudeSegments; y++)
            {
                for (int x = 0; x < longitudeSegments; x++)
                {
                    int a = y * cols + x;
                    int b = a + 1;
                    int c = a + cols;
                    int d = c + 1;
                    triangles[t++] = a;
                    triangles[t++] = c;
                    triangles[t++] = b;
                    triangles[t++] = b;
                    triangles[t++] = c;
                    triangles[t++] = d;
                }
            }

            var mesh = new Mesh { name = "UvSphere", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return mesh;
        }

        /// <summary>Flat annulus in the XZ plane; uv.x runs 0 at the inner edge to 1 at the outer edge.</summary>
        public static Mesh Annulus(float inner, float outer, int segments = 256)
        {
            var vertices = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[vertices.Length];
            var normals = new Vector3[vertices.Length];
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.PI * 2f * i / segments;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                vertices[i * 2] = dir * inner;
                vertices[i * 2 + 1] = dir * outer;
                uvs[i * 2] = new Vector2(0, (float)i / segments);
                uvs[i * 2 + 1] = new Vector2(1, (float)i / segments);
                normals[i * 2] = Vector3.up;
                normals[i * 2 + 1] = Vector3.up;
            }

            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                triangles[i * 6] = a;
                triangles[i * 6 + 1] = a + 1;
                triangles[i * 6 + 2] = a + 2;
                triangles[i * 6 + 3] = a + 2;
                triangles[i * 6 + 4] = a + 1;
                triangles[i * 6 + 5] = a + 3;
            }

            var mesh = new Mesh { name = "Annulus" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = triangles;
            return mesh;
        }

        /// <summary>Unit quad in the XY plane centred on the origin.</summary>
        public static Mesh Quad()
        {
            var mesh = new Mesh { name = "Quad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0),
                new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0),
            };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>Two crossed quads along -Z from the origin, for an engine plume.</summary>
        public static Mesh Plume()
        {
            var mesh = new Mesh { name = "Plume" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(-0.5f, 0, -1), new Vector3(0.5f, 0, -1),
                new Vector3(0, -0.5f, 0), new Vector3(0, 0.5f, 0), new Vector3(0, -0.5f, -1), new Vector3(0, 0.5f, -1),
            };
            // uv.y 0.5 at the nozzle fading to the tip, so the radial glow shader gives a teardrop.
            mesh.uv = new[]
            {
                new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 1), new Vector2(1, 1),
            };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 4, 6, 5, 5, 6, 7 };
            mesh.bounds = new Bounds(new Vector3(0, 0, -0.5f), Vector3.one);
            return mesh;
        }
    }
}
