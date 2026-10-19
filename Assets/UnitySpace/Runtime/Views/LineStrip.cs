using UnitySpace.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnitySpace.Views
{
    /// <summary>
    /// A 1-pixel line strip mesh. Pixel lines stay legible at every scale, where a LineRenderer's
    /// world-space width would be either invisible or a slab depending on the zoom.
    /// </summary>
    public sealed class LineStrip
    {
        public readonly GameObject Root;
        readonly Mesh mesh;
        readonly MeshRenderer renderer;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        Vector3[] vertices;
        Color[] colors;
        int[] indices;

        static Material sharedMaterial;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public LineStrip(string name, Transform parent)
        {
            Root = new GameObject(name);
            Root.transform.SetParent(parent, false);
            mesh = Materials.Own(new Mesh { name = name });
            mesh.MarkDynamic();
            Root.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = Root.AddComponent<MeshRenderer>();
            if (sharedMaterial == null) sharedMaterial = Materials.MarkShared(Materials.Create(Materials.Line, "Line"));
            renderer.sharedMaterial = sharedMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public int Capacity => vertices?.Length ?? 0;

        /// <summary>Destroys the line and its mesh.</summary>
        public void Dispose()
        {
            Materials.DestroyMesh(mesh);
            Object.Destroy(Root);
        }

        /// <summary>Sets points (local to Root) with a per-point alpha ramp from <paramref name="alphaStart"/> to <paramref name="alphaEnd"/>.</summary>
        public void SetPoints(Vector3[] points, int count, Color color, float alphaStart = 1f, float alphaEnd = 1f)
        {
            if (vertices == null || vertices.Length != count)
            {
                vertices = new Vector3[count];
                colors = new Color[count];
                indices = new int[count];
                for (int i = 0; i < count; i++) indices[i] = i;
                mesh.Clear();
            }
            for (int i = 0; i < count; i++)
            {
                vertices[i] = points[i];
                float t = count > 1 ? (float)i / (count - 1) : 0;
                colors[i] = new Color(color.r, color.g, color.b, color.a * Mathf.Lerp(alphaStart, alphaEnd, t));
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.SetIndices(indices, MeshTopology.LineStrip, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);
        }

        public void SetTint(Color tint)
        {
            block.SetColor(ColorId, tint);
            renderer.SetPropertyBlock(block);
        }

        public bool Visible
        {
            get => renderer.enabled;
            set => renderer.enabled = value;
        }
    }
}
