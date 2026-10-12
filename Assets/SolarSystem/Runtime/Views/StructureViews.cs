using System.Collections.Generic;
using SolarSystem.Core;
using SolarSystem.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace SolarSystem.Views
{
    /// <summary>
    /// What a system has in orbit: settlements' yards (with the hull on the slip framed out rib by rib
    /// and plated as the yard banks build points), factory stations and habitats, the real spacecraft,
    /// and the docking rings hovering over their cities. Drawn at the ship scale in their body's
    /// neighbourhood, like parked ships; hidden when smaller than a few pixels (the HUD shows icons).
    /// </summary>
    public sealed class StructureViews
    {
        readonly SolarSystemApp app;
        readonly List<View> views = new List<View>();
        readonly List<RingView> rings = new List<RingView>();
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        static readonly int EngineGlowId = Shader.PropertyToID("_EngineGlow");
        static readonly int EngineColorId = Shader.PropertyToID("_EngineColor");
        static readonly int ColorId = Shader.PropertyToID("_Tint");
        static readonly int HighlightId = Shader.PropertyToID("_Highlight");

        /// <summary>Every structure's scene position this frame, for the HUD and picking.</summary>
        public readonly Dictionary<OrbitalStructure, DVec3> Scenes = new Dictionary<OrbitalStructure, DVec3>();
        public readonly Dictionary<DockingRing, DVec3> RingScenes = new Dictionary<DockingRing, DVec3>();

        /// <summary>Drawn size (scene units) of each structure and ring this frame.</summary>
        public readonly Dictionary<object, double> Sizes = new Dictionary<object, double>();

        sealed class View
        {
            public OrbitalStructure S;
            public GameObject Root;
            public Renderer[] Renderers;
            public Transform Slip;
            public Transform[] Ribs;
            public GameObject Hull;
            public ShipClass? HullClass;
            public float Spin;
        }

        sealed class RingView
        {
            public DockingRing Ring;
            public GameObject Root;
            public Renderer Renderer;
        }

        /// <summary>A docking ring is drawn this many km across (fiction: the size of a city district).</summary>
        public const double RingDiameterKm = 10;

        public StructureViews(SolarSystemApp app, Transform parent)
        {
            this.app = app;
            var infra = app.World.Infrastructure;
            if (infra == null) return;
            foreach (var s in infra.Structures) views.Add(Create(s, parent));
            foreach (var r in infra.Rings)
            {
                var go = new GameObject(r.Name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = ShipMeshFactory.GetDockingRing(r.Owner);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ShipModels.ProceduralMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                rings.Add(new RingView { Ring = r, Root = go, Renderer = mr });
            }
        }

        View Create(OrbitalStructure s, Transform parent)
        {
            var root = new GameObject(s.Name);
            root.transform.SetParent(parent, false);
            var v = new View { S = s, Root = root, Spin = Random.value * 360 };
            Mesh mesh;
            switch (s.Kind)
            {
                case StructureKind.Shipyard: mesh = ShipMeshFactory.GetShipyard(s.Owner); break;
                case StructureKind.Factory: mesh = ShipMeshFactory.GetFactory(s.Owner, s.Industry); break;
                case StructureKind.Habitat: mesh = ShipMeshFactory.GetHabitat(s.Owner); break;
                case StructureKind.CrewedStation: mesh = ShipMeshFactory.GetCrewedStation(s.Name == "Tiangong"); break;
                default: mesh = ShipMeshFactory.GetSpacecraft(s.Name); break;
            }
            var list = new List<Renderer> { Part(root.transform, mesh, "Model") };
            if (s.Kind == StructureKind.Shipyard)
            {
                // The cradle holds a hull about 80% of its length, nose along +Z.
                v.Slip = new GameObject("Slip").transform;
                v.Slip.SetParent(root.transform, false);
                v.Slip.localScale = Vector3.one * 0.8f;
                v.Ribs = new Transform[12];
                for (int i = 0; i < v.Ribs.Length; i++)
                {
                    var rib = Part(v.Slip, ShipMeshFactory.GetSkeletonRib(), "Rib");
                    rib.transform.localPosition = new Vector3(0, 0, -0.46f + i * 0.084f);
                    rib.transform.localScale = new Vector3(1.6f, 1.6f, 1f);
                    v.Ribs[i] = rib.transform;
                    list.Add(rib);
                }
            }
            v.Renderers = list.ToArray();
            return v;
        }

        static Renderer Part(Transform parent, Mesh mesh, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ShipModels.ProceduralMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return mr;
        }

        /// <summary>Where everything is this frame (before the camera moves, so it can follow a structure).</summary>
        public void Layout()
        {
            var eph = app.Ephemeris;
            var scale = app.Scale;
            var scene = app.BodyScenes;
            double jd = app.Clock.Jd;
            Scenes.Clear();
            RingScenes.Clear();
            Sizes.Clear();
            foreach (var v in views)
            {
                var s = v.S;
                Scenes[s] = s.AtL2 ? scale.MapNear(eph, scene, s.Body, s.Helio(eph))
                    : scene[s.Body] + scale.LocalOffset(app.Catalog[s.Body], s.Orbit.OffsetAt(jd));
                Sizes[s] = scale.ShipLength(s.LengthKm);
            }
            foreach (var rv in rings)
            {
                var r = rv.Ring;
                RingScenes[r] = scene[r.Body] + scale.LocalOffset(app.Catalog[r.Body], r.RingPoint.OffsetAt(jd));
                Sizes[r] = scale.ShipLength(RingDiameterKm);
            }
        }

        public void Update(bool visible)
        {
            var scene = app.BodyScenes;
            double jd = app.Clock.Jd;
            double ppu = app.PixelsPerUnitAtUnitDistance();

            foreach (var v in views)
            {
                var s = v.S;
                var p = Scenes[s];
                double size = Sizes[s];
                var rel = app.Relative(p);
                bool show = visible && size / System.Math.Max(rel.magnitude, 1e-9) * ppu >= 4;
                if (v.Root.activeSelf != show) v.Root.SetActive(show);
                if (!show) continue;

                var t = v.Root.transform;
                t.localPosition = rel;
                t.localScale = Vector3.one * (float)size;
                // Long axis along the orbit; habitats face their ring to the planet's pole and spin.
                if (s.AtL2)
                {
                    var toSun = app.Relative(scene[0]) - rel;
                    t.localRotation = Quaternion.LookRotation(Vector3.Cross(toSun, Vector3.up).normalized + Vector3.up * 0.01f, -toSun);
                }
                else
                {
                    var tangent = s.Orbit.TangentAt(jd).SwapYZ;
                    var up = s.Orbit.OffsetAt(jd).SwapYZ.Normalized;
                    var f = new Vector3((float)tangent.X, (float)tangent.Y, (float)tangent.Z);
                    var u = new Vector3((float)up.X, (float)up.Y, (float)up.Z);
                    if (s.Kind == StructureKind.Habitat)
                    {
                        v.Spin = (v.Spin + Time.deltaTime * 12f) % 360f;
                        t.localRotation = Quaternion.LookRotation(u, f) * Quaternion.Euler(0, 0, v.Spin);
                    }
                    else t.localRotation = Quaternion.LookRotation(f, u);
                }

                block.SetFloat(EngineGlowId, 1.4f);
                block.SetColor(EngineColorId, new Color(1f, 0.85f, 0.6f));
                block.SetColor(ColorId, Color.white);
                block.SetFloat(HighlightId, app.Selected.Structure == s || app.Hovered.Structure == s ? 1f : 0f);
                foreach (var r in v.Renderers) r.SetPropertyBlock(block);
                if (v.Slip != null) UpdateSlip(v);
            }

            foreach (var rv in rings)
            {
                var r = rv.Ring;
                var b = app.Catalog[r.Body];
                var offset = r.RingPoint.OffsetAt(jd);
                var p = RingScenes[r];
                double size = Sizes[r];
                var rel = app.Relative(p);
                bool show = visible && size / System.Math.Max(rel.magnitude, 1e-9) * ppu >= 3;
                rv.Renderer.enabled = show;
                if (!show) continue;
                var t = rv.Root.transform;
                t.localPosition = rel;
                t.localScale = Vector3.one * (float)size;
                // The ring's axis points down at its city; ships pass through it on their way in.
                var down = (-offset).SwapYZ.Normalized;
                var d = new Vector3((float)down.X, (float)down.Y, (float)down.Z);
                var north = BodyOrientation.MapNorth(b).SwapYZ;
                t.localRotation = Quaternion.LookRotation(d, new Vector3((float)north.X, (float)north.Y, (float)north.Z));
                block.SetFloat(EngineGlowId, 1.6f + 0.6f * Mathf.Sin(Time.unscaledTime * 3f + r.Id));
                block.SetColor(EngineColorId, new Color(0.55f, 0.9f, 1f));
                block.SetColor(ColorId, Color.white);
                block.SetFloat(HighlightId, app.Selected.Ring == r || app.Hovered.Ring == r ? 1f : 0f);
                rv.Renderer.SetPropertyBlock(block);
            }
        }

        /// <summary>Ribs go up stern to bow as the yard banks build points; then the hull is plated over them.</summary>
        void UpdateSlip(View v)
        {
            var settlement = v.S.Settlement;
            var cls = settlement?.OnSlip;
            double progress = v.S.SlipProgress;
            int ribs = cls == null ? 0 : Mathf.Clamp(Mathf.CeilToInt((float)(progress / 0.6 * v.Ribs.Length)), 0, v.Ribs.Length);
            for (int i = 0; i < v.Ribs.Length; i++) v.Ribs[i].gameObject.SetActive(i < ribs && progress < 0.97);
            bool plated = cls != null && progress >= 0.6;
            if (plated && (v.Hull == null || v.HullClass != cls))
            {
                if (v.Hull != null) Object.Destroy(v.Hull);
                v.Hull = new GameObject("Hull on the slip");
                v.Hull.transform.SetParent(v.Slip, false);
                var section = ShipMeshFactory.IsWarship(cls.Value) ? ShipMeshFactory.Stern : ShipMeshFactory.WholeHull;
                var mr = Part(v.Hull.transform, ShipMeshFactory.GetSection(cls.Value, settlement.Owner, section), "Stern");
                v.HullClass = cls;
            }
            if (v.Hull != null)
            {
                v.Hull.SetActive(plated);
                if (plated)
                {
                    // Plating creeps forward from the stern: the hull shows along the slip as far as it has got.
                    float done = Mathf.Clamp01((float)((progress - 0.6) / 0.37));
                    v.Hull.transform.localScale = new Vector3(1, 1, Mathf.Max(0.05f, done));
                    v.Hull.transform.localPosition = new Vector3(0, 0, -0.5f * (1 - done));
                    foreach (var r in v.Hull.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(block);
                }
            }
        }
    }
}
