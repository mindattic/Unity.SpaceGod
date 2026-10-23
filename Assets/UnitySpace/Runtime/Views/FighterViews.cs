using System.Collections.Generic;
using Unity.SpaceGod.Core;
using Unity.SpaceGod.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.SpaceGod.Views
{
    /// <summary>
    /// Every fighter in every battle: its hull, an engine glow kept a few pixels across so a squadron
    /// reads as a cluster of sparks from afar, and a fading trail that traces its runs and break-offs.
    /// </summary>
    public sealed class FighterViews
    {
        sealed class View
        {
            public GameObject Hull;
            public Transform Glow;
            public LineStrip Trail;
            public readonly List<DVec3> History = new List<DVec3>();
            public float NextSample;
        }

        readonly UnitySpaceApp app;
        readonly Transform root;
        readonly Dictionary<BattleUnit, View> views = new Dictionary<BattleUnit, View>();
        readonly Vector3[] trailPoints = new Vector3[TrailLength];
        static Material glowMaterial, shipMaterial;
        static Mesh quad;

        const int TrailLength = 24;
        const float SampleSeconds = 0.12f;

        public FighterViews(UnitySpaceApp app, Transform root)
        {
            this.app = app;
            this.root = root;
            quad ??= MeshFactory.Quad();
            if (glowMaterial == null)
            {
                glowMaterial = Materials.MarkShared(Materials.Create(Materials.Glow, "FighterGlow"));
                glowMaterial.SetColor("_Color", new Color(0.55f, 0.8f, 1f));
                glowMaterial.SetFloat("_Intensity", 1.6f);
                glowMaterial.SetFloat("_Falloff", 2f);
            }
            shipMaterial ??= ShipModels.ProceduralMaterial;
        }

        public void Update()
        {
            float now = Time.unscaledTime;
            var live = new HashSet<BattleUnit>();
            var cam = app.UnityCamera.transform;
            double ppu = app.PixelsPerUnitAtUnitDistance();
            foreach (var b in app.World.War.Battles)
            {
                foreach (var u in b.Units)
                {
                    if (u.Kind != UnitKind.Fighter || !u.Present) continue;
                    live.Add(u);
                    if (!views.TryGetValue(u, out var v)) views[u] = v = Create(u);
                    var scene = app.BattleScene(b, u.Pos);
                    var rel = app.Relative(scene);
                    // Its model while big enough, then a glow impostor, then nothing below a pixel (U.SPC-T415): a battle
                    // seen from across the system is its ships' icons, not a cloud of sparks.
                    var tier = RenderLod.Tier(RenderLod.Fighter, RenderLod.ApparentPx(app.Scale.ShipLength(u.LengthKm), rel.magnitude, ppu));
                    bool model = RenderLod.DrawsModel(tier), shown = tier != LodTier.Culled;
                    if (v.Hull.activeSelf != model) v.Hull.SetActive(model);
                    if (v.Glow.gameObject.activeSelf != shown) v.Glow.gameObject.SetActive(shown);
                    if (!shown)
                    {
                        v.Trail.Visible = false;
                        continue;
                    }
                    var f = u.Forward.SwapYZ;
                    var fwd = new Vector3((float)f.X, (float)f.Y, (float)f.Z);
                    v.Hull.transform.localPosition = rel;
                    if (fwd.sqrMagnitude > 1e-12f) v.Hull.transform.localRotation = Quaternion.LookRotation(fwd, Mathf.Abs(fwd.normalized.y) > 0.98f ? Vector3.right : Vector3.up);
                    v.Hull.transform.localScale = Vector3.one * (float)app.Scale.ShipLength(u.LengthKm);

                    float px = 5f * Mathf.Max(rel.magnitude, 1e-6f) / (float)ppu;
                    v.Glow.localPosition = rel - fwd.normalized * (float)app.Scale.ShipLength(u.LengthKm) * 0.55f;
                    v.Glow.rotation = Quaternion.LookRotation(v.Glow.position - cam.position, cam.up);
                    v.Glow.localScale = Vector3.one * Mathf.Max(px, (float)app.Scale.ShipLength(u.LengthKm) * 0.8f);

                    if (now >= v.NextSample)
                    {
                        v.History.Add(u.Pos);
                        if (v.History.Count > TrailLength) v.History.RemoveAt(0);
                        v.NextSample = now + SampleSeconds;
                    }
                    int n = v.History.Count;
                    if (n >= 2)
                    {
                        for (int i = 0; i < n; i++) trailPoints[i] = app.Relative(app.BattleScene(b, v.History[i]));
                        trailPoints[n - 1] = rel;
                        var c = u.Faction.Color;
                        v.Trail.SetPoints(trailPoints, n, new Color(c.R, c.G, c.B, 0.55f), 0f, 0.8f);
                        v.Trail.SetTint(Color.white);
                        v.Trail.Root.transform.localPosition = Vector3.zero;
                        v.Trail.Visible = true;
                    }
                }
            }
            var gone = new List<BattleUnit>();
            foreach (var kv in views)
                if (!live.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var u in gone)
            {
                var v = views[u];
                Object.Destroy(v.Hull);
                Object.Destroy(v.Glow.gameObject);
                v.Trail.Dispose();
                views.Remove(u);
            }
        }

        View Create(BattleUnit u)
        {
            var v = new View();
            v.Hull = new GameObject(u.Fighter.Bomber ? "Bomber" : "Fighter");
            v.Hull.transform.SetParent(root, false);
            v.Hull.AddComponent<MeshFilter>().sharedMesh = ShipMeshFactory.GetFighter(u.Faction, u.Fighter.Bomber);
            var mr = v.Hull.AddComponent<MeshRenderer>();
            mr.sharedMaterial = shipMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;

            var glow = new GameObject("Engine");
            glow.transform.SetParent(root, false);
            glow.AddComponent<MeshFilter>().sharedMesh = quad;
            var gr = glow.AddComponent<MeshRenderer>();
            gr.sharedMaterial = glowMaterial;
            gr.shadowCastingMode = ShadowCastingMode.Off;
            v.Glow = glow.transform;

            v.Trail = new LineStrip("Trail", root);
            v.Trail.Visible = false;
            return v;
        }
    }
}
