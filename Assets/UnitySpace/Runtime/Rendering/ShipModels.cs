using System.Collections.Generic;
using Unity.SpaceGod.Core;
using UnityEngine;

namespace Unity.SpaceGod.Rendering
{
    /// <summary>
    /// Builds the model under a ship's root: a Kenney Space Kit craft (CC0) for small civilian
    /// classes, or a procedural hull for warships (and as a fallback), re-materialed with the Sun-lit
    /// ship shader. The Kenney craft themselves are pre-normalised offline (one unit long on Z, centred,
    /// nose on +Z, yaw already baked into the mesh data) by MindAttic.AssetForge's mesh pipeline, not at
    /// runtime here — see github.com/mindattic/AssetForge and Resources/Ships/*.glb.
    /// </summary>
    public static class ShipModels
    {
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
                    proceduralMaterial = Materials.MarkShared(Materials.Create(Materials.Ship, "ShipProcedural"));
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

        /// <summary>
        /// Whether <paramref name="ship"/>'s model can be built this frame without waiting on the disk (U.SPC-T414): a
        /// procedural hull always can; a Kenney craft once its model has been loaded in the background (asked for here
        /// if it has not been).
        /// </summary>
        public static bool Ready(Ship ship)
        {
            string kenney = KenneyModelFor(ship);
            return kenney == null || Prototypes.ContainsKey(kenney) || AssetStreamer.Ready(AssetStreamer.ShipPath(kenney));
        }

        /// <summary>The Kenney craft a ship is drawn as, or null for a procedural hull.</summary>
        public static string KenneyModelFor(Ship ship)
        {
            // Local traffic (U.SPC-T375): a boxy bus, nippy taxis and skiffs, and a roomier six-seat tourer, each drawn
            // at its own design's length.
            switch (ship.Civil)
            {
                case CivilCraft.Bus: return "craft_cargoB";
                case CivilCraft.Taxi: return "craft_speederA";
                case CivilCraft.Skiff: return "craft_racer";
                case CivilCraft.Runabout: return "craft_speederA";
                case CivilCraft.Tourer: return "craft_cargoA";
            }
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
                // Loaded in the background beforehand (see Ready); should a view be built before it arrives, the
                // procedural hull stands in rather than the frame waiting on the disk.
                if (!AssetStreamer.Ready(AssetStreamer.ShipPath(modelName))) return null;
                var source = AssetStreamer.Loaded<GameObject>(AssetStreamer.ShipPath(modelName));
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

        /// <summary>Wraps the imported model, kept inactive as a template. The model's own geometry is
        /// already one unit long on Z, centred, and nose +Z — baked in offline by AssetForge, not fixed
        /// up here (the pack's own GLTF root transform comes in as identity for the same reason).</summary>
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

            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return null;
            return wrapper;
        }

        const string PlatingPath = "Textures/ship_plating";

        /// <summary>The seamless sci-fi panel texture every hull is plated with (see Ship.shader): set on the material
        /// as soon as it has loaded in the background (at once if it already has).</summary>
        public static void Plate(Material m, float tilesPerUnit)
        {
            m.SetFloat("_PlatingScale", tilesPerUnit);
            AssetStreamer.Pin(PlatingPath);
            AssetStreamer.Request<Texture2D>(PlatingPath, plating =>
            {
                if (m != null && plating != null) m.SetTexture("_Plating", plating);
            });
        }

        static Material[] MaterialsFor(string modelName, Faction faction, GameObject prototype)
        {
            faction = ShipMeshFactory.Canon(faction);
            if (MaterialCache.TryGetValue((modelName, faction), out var cached)) return cached;
            var list = new List<Material>();
            var accent = new Color(faction.Color.R, faction.Color.G, faction.Color.B);
            foreach (var r in prototype.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var source in r.sharedMaterials)
                {
                    var m = Materials.MarkShared(Materials.Create(Materials.Ship, "Ship_" + modelName));
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
                    // Ship.shader's plating is triplanar-projected in OBJECT SPACE specifically because
                    // procedural hulls have no UVs (see its own header comment) -- it was never meant for
                    // these Kenney craft, which do have real UVs and their own baked-in surface detail.
                    // Confirmed by isolating it: with _PlatingStrength at its 0.6 default, these low-poly,
                    // many-small-flat-faces hulls render as a checkerboard-like mess, because triplanar
                    // blending snaps between the three axis projections face-to-face on boxy, multi-angled
                    // low-poly geometry (each facet picking a different slice of the repeating plating
                    // texture) -- a procedural hull's simpler, more uniformly-oriented panels don't expose
                    // this. Leaving plating off entirely for Kenney models renders them correctly.
                    m.SetFloat("_PlatingStrength", 0f);
                    list.Add(m);
                }
            }
            cached = list.Count > 0 ? list.ToArray() : new[] { Materials.MarkShared(Materials.Create(Materials.Ship, "Ship_" + modelName)) };
            MaterialCache[(modelName, faction)] = cached;
            return cached;
        }
    }
}
