using System.Collections.Generic;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Rendering
{
    /// <summary>
    /// Builds the model under a ship's root: a Kenney Space Kit craft (CC0) for small civilian
    /// classes, or a procedural hull for warships (and as a fallback). Models are normalised to one
    /// unit long, centred, nose on +Z, and re-materialed with the Sun-lit ship shader.
    /// </summary>
    public static class ShipModels
    {
        // Kenney's craft face -Z; yaw 180 puts the nose on +Z.
        const float KenneyYaw = 180f;

        static readonly Dictionary<string, GameObject> Prototypes = new Dictionary<string, GameObject>();
        static readonly Dictionary<(string, Faction), Material[]> MaterialCache = new Dictionary<(string, Faction), Material[]>();
        static Material proceduralMaterial;

        /// <summary>The vertex-coloured, Sun-lit material every procedural hull uses.</summary>
        public static Material ProceduralMaterial
        {
            get
            {
                if (proceduralMaterial == null)
                {
                    proceduralMaterial = Materials.Create(Materials.Ship, "ShipProcedural");
                    proceduralMaterial.SetFloat("_UseVertexColor", 1);
                    Plate(proceduralMaterial, 10f);
                }
                return proceduralMaterial;
            }
        }
        static Transform prototypeRoot;

        /// <summary>
        /// Builds the hull under <paramref name="parent"/>. Warships come as two sections (stern and
        /// bow) that can break apart; anything else is one piece, returned as the stern with no bow.
        /// </summary>
        public static void Build(Ship ship, Transform parent, List<Renderer> renderers, out Transform stern, out Transform bow)
        {
            bow = null;
            string kenney = KenneyModelFor(ship);
            if (kenney != null)
            {
                var model = InstantiateKenney(kenney, ship.Faction, parent, renderers);
                if (model != null)
                {
                    stern = model.transform;
                    return;
                }
            }
            if (ShipMeshFactory.IsWarship(ship.Spec.Class))
            {
                stern = Section(ship, parent, renderers, ShipMeshFactory.Stern, "Stern");
                bow = Section(ship, parent, renderers, ShipMeshFactory.Bow, "Bow");
                return;
            }
            stern = Section(ship, parent, renderers, ShipMeshFactory.WholeHull, "Hull");
        }

        static Transform Section(Ship ship, Transform parent, List<Renderer> renderers, int section, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = ShipMeshFactory.GetSection(ship.Spec.Class, ship.Faction, section);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ProceduralMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            renderers.Add(mr);
            return go.transform;
        }

        /// <summary>A hardpoint part (or scar) under a hull section, using the procedural material.</summary>
        public static MeshRenderer Part(Transform parent, Mesh mesh, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ProceduralMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        static string KenneyModelFor(Ship ship)
        {
            switch (ship.Spec.Class)
            {
                case ShipClass.Freighter: return ship.Id % 2 == 0 ? "craft_cargoA" : "craft_cargoB";
                case ShipClass.Miner: return "craft_miner";
                case ShipClass.Courier: return ship.Id % 2 == 0 ? "craft_speederA" : "craft_racer";
                default: return null;
            }
        }

        static GameObject InstantiateKenney(string modelName, Faction faction, Transform parent, List<Renderer> renderers)
        {
            if (!Prototypes.TryGetValue(modelName, out var prototype))
            {
                var source = Resources.Load<GameObject>("Ships/" + modelName);
                prototype = source != null ? Normalise(source, modelName) : null;
                Prototypes[modelName] = prototype;
            }
            if (prototype == null) return null;

            var instance = Object.Instantiate(prototype, parent, false);
            instance.name = modelName;
            instance.SetActive(true);
            var materials = MaterialsFor(modelName, faction, prototype);
            int index = 0;
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = materials[Mathf.Min(index++, materials.Length - 1)];
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                renderers.Add(r);
            }
            return instance;
        }

        /// <summary>Wraps the imported model so it is one unit long, centred, nose +Z. Kept inactive as a template.</summary>
        static GameObject Normalise(GameObject source, string modelName)
        {
            if (prototypeRoot == null)
            {
                prototypeRoot = new GameObject("ShipPrototypes").transform;
                prototypeRoot.gameObject.SetActive(false);
                Object.DontDestroyOnLoad(prototypeRoot.gameObject);
            }

            var wrapper = new GameObject(modelName);
            wrapper.transform.SetParent(prototypeRoot, false);
            var model = Object.Instantiate(source, wrapper.transform, false);
            // Kenney roots carry a layout offset from the kit's sample scene; drop it before centring.
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0, KenneyYaw, 0);

            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return null;
            var bounds = new Bounds();
            bool first = true;
            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var m = wrapper.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int c = 0; c < 8; c++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (first)
                    {
                        bounds = new Bounds(p, Vector3.zero);
                        first = false;
                    }
                    else bounds.Encapsulate(p);
                }
            }

            float length = Mathf.Max(bounds.size.z, 1e-4f);
            float s = 1f / length;
            model.transform.localScale *= s;
            model.transform.localPosition = -bounds.center * s;
            return wrapper;
        }

        static Texture2D plating;

        /// <summary>The seamless sci-fi panel texture every hull is plated with (see Ship.shader).</summary>
        public static void Plate(Material m, float tilesPerUnit)
        {
            plating ??= Resources.Load<Texture2D>("Textures/ship_plating");
            if (plating == null) return;
            m.SetTexture("_Plating", plating);
            m.SetFloat("_PlatingScale", tilesPerUnit);
        }

        static Material[] MaterialsFor(string modelName, Faction faction, GameObject prototype)
        {
            if (MaterialCache.TryGetValue((modelName, faction), out var cached)) return cached;
            var list = new List<Material>();
            var accent = new Color(faction.Color.R, faction.Color.G, faction.Color.B);
            // Plate the model about seven panels along its length, whatever units it was built in.
            var bounds = new Bounds();
            bool any = false;
            foreach (var mf in prototype.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                if (!any) bounds = b;
                else bounds.Encapsulate(b);
                any = true;
            }
            float length = any ? Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) : 1f;
            foreach (var r in prototype.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var source in r.sharedMaterials)
                {
                    var m = Materials.Create(Materials.Ship, "Ship_" + modelName);
                    m.SetFloat("_UseVertexColor", 0);
                    var color = Color.gray;
                    if (source != null)
                    {
                        if (source.HasProperty("_BaseColor")) color = source.GetColor("_BaseColor");
                        else if (source.HasProperty("_Color")) color = source.GetColor("_Color");
                        // Kenney's red trim becomes the owner's livery; the rest of the hull takes a wash of it.
                        if (source.name.ToLowerInvariant().Contains("red")) color = accent;
                        else color = Color.Lerp(color, color * accent * 1.6f, 0.35f);
                    }
                    m.SetColor("_Color", color);
                    Plate(m, 10f / Mathf.Max(length, 1e-3f));
                    list.Add(m);
                }
            }
            cached = list.Count > 0 ? list.ToArray() : new[] { Materials.Create(Materials.Ship, "Ship_" + modelName) };
            MaterialCache[(modelName, faction)] = cached;
            return cached;
        }
    }
}
