using UnityEngine;

namespace SolarSystem.Rendering
{
    /// <summary>Loads the project's shaders from Resources (which also guarantees they ship in builds).</summary>
    public static class Materials
    {
        public static Shader Planet => Load("Planet");
        public static Shader Sun => Load("Sun");
        public static Shader Glow => Load("Glow");
        public static Shader Ring => Load("Ring");
        public static Shader Ship => Load("Ship");
        public static Shader Line => Load("Line");
        public static Shader Stars => Load("Stars");
        public static Shader GasGiant => Load("GasGiant");
        public static Shader Asteroids => Load("Asteroids");
        public static Shader Fx => Load("Fx");
        public static Shader Points => Load("Points");

        static Shader Load(string name)
        {
            var shader = Resources.Load<Shader>("Shaders/" + name);
            if (shader == null)
            {
                Debug.LogError($"[SolarSystem] Missing shader Resources/Shaders/{name}");
                shader = Shader.Find("Hidden/InternalErrorShader");
            }
            return shader;
        }

        public static Material Create(Shader shader, string name)
        {
            return new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
        }

        static readonly System.Collections.Generic.HashSet<Material> Shared = new System.Collections.Generic.HashSet<Material>();
        static readonly System.Collections.Generic.HashSet<Mesh> OwnedMeshes = new System.Collections.Generic.HashSet<Mesh>();

        /// <summary>A mesh built for one view alone (a line, an effects mesh): destroyed with that view's system.</summary>
        public static Mesh Own(Mesh mesh)
        {
            if (mesh != null) OwnedMeshes.Add(mesh);
            return mesh;
        }

        /// <summary>Destroys one view-owned mesh now (a fighter's trail when it dies).</summary>
        public static void DestroyMesh(Mesh mesh)
        {
            if (mesh == null) return;
            OwnedMeshes.Remove(mesh);
            Object.Destroy(mesh);
        }

        /// <summary>A material cached for every view to share (it outlives any one star system's views).</summary>
        public static Material MarkShared(Material m)
        {
            if (m != null) Shared.Add(m);
            return m;
        }

        /// <summary>
        /// Destroys the materials used under <paramref name="root"/> that belong only to those views
        /// (DontSave materials outlive their GameObjects otherwise), leaving the shared caches alone.
        /// </summary>
        public static void DestroyOwned(Transform root)
        {
            var owned = new System.Collections.Generic.HashSet<Material>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var mat in r.sharedMaterials)
                    if (mat != null && !Shared.Contains(mat)) owned.Add(mat);
            foreach (var mat in owned) Object.Destroy(mat);
            // Meshes too, but only those built for these views (cached hull meshes and imported models stay).
            var meshes = new System.Collections.Generic.HashSet<Mesh>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && OwnedMeshes.Contains(mf.sharedMesh)) meshes.Add(mf.sharedMesh);
            foreach (var mesh in meshes) DestroyMesh(mesh);
        }
    }
}
