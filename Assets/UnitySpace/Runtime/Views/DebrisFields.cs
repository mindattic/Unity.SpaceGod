using System.Collections.Generic;
using UnitySpace.Core;
using UnitySpace.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnitySpace.Views
{
    /// <summary>
    /// The drifting debris cloud round every lasting <see cref="Wreck"/>: dark tumbling shards that spread
    /// slowly from the hull (<see cref="Wreck.DebrisOffsetKm"/>) and shrink away as the wreck breaks up
    /// (<see cref="Wreck.Fade"/>). Drawn at the battle scale, where the fight that made it was drawn.
    /// </summary>
    public sealed class DebrisFields
    {
        readonly UnitySpaceApp app;
        readonly Transform parent;
        readonly Dictionary<Wreck, Transform[]> fields = new Dictionary<Wreck, Transform[]>();
        readonly List<Wreck> gone = new List<Wreck>();
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        static readonly int ColorId = Shader.PropertyToID("_Tint");
        static Mesh shard;

        public DebrisFields(UnitySpaceApp app, Transform parent)
        {
            this.app = app;
            this.parent = parent;
            block.SetColor(ColorId, new Color(0.24f, 0.22f, 0.21f));
        }

        public void Update()
        {
            var wrecks = app.World.War.Wrecks;
            double jd = app.Clock.Jd;
            float now = Time.unscaledTime;
            foreach (var w in wrecks.Items)
            {
                if (!fields.TryGetValue(w, out var pieces)) fields[w] = pieces = Create(w);
                var centre = app.WreckScene(w);
                float length = (float)app.Scale.ShipLength(w.LengthKm);
                float size = length * 0.07f * (float)w.Fade(jd);
                for (int k = 0; k < pieces.Length; k++)
                {
                    var t = pieces[k];
                    t.localPosition = app.Relative(centre + w.DebrisOffsetKm(k, jd).SwapYZ * app.BattleKmScale);
                    t.localRotation = Quaternion.Euler(now * (11 + k * 3), now * (7 + k * 5), now * (5 + k * 2));
                    t.localScale = new Vector3(size, size * (0.3f + 0.15f * (k % 4)), size * (0.6f + 0.1f * (k % 3)));
                }
            }
            gone.Clear();
            foreach (var w in fields.Keys)
                if (!wrecks.Items.Contains(w)) gone.Add(w);
            foreach (var w in gone)
            {
                foreach (var t in fields[w]) Object.Destroy(t.gameObject);
                fields.Remove(w);
            }
        }

        Transform[] Create(Wreck w)
        {
            shard ??= Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var pieces = new Transform[w.DebrisPieces];
            for (int k = 0; k < pieces.Length; k++)
            {
                var go = new GameObject($"{w.Name} debris {k}");
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = shard;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ShipModels.ProceduralMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.SetPropertyBlock(block);
                pieces[k] = go.transform;
            }
            return pieces;
        }
    }
}
