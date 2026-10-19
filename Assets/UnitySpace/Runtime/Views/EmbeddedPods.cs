using System.Collections.Generic;
using UnitySpace.Core;
using UnitySpace.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnitySpace.Views
{
    /// <summary>
    /// Boarding pods stuck in hulls. Each is parented to the model of the ship (or platform) it holed,
    /// drill buried and tail standing out of the plating, so it rides along as the hull manoeuvres,
    /// and it stays there after the fight until the ship is lost or the battle is long over.
    /// </summary>
    public sealed class EmbeddedPods
    {
        readonly UnitySpaceApp app;
        readonly Dictionary<Embedded, GameObject> pods = new Dictionary<Embedded, GameObject>();

        public EmbeddedPods(UnitySpaceApp app) => this.app = app;

        public void Update()
        {
            var live = new HashSet<Embedded>();
            var war = app.World.War;
            foreach (var b in war.Battles) Track(b, live);
            foreach (var b in war.Ended) Track(b, live);
            var gone = new List<Embedded>();
            foreach (var kv in pods)
                if (!live.Contains(kv.Key) || kv.Value == null) gone.Add(kv.Key);
            foreach (var e in gone)
            {
                if (pods[e] != null) Object.Destroy(pods[e]);
                pods.Remove(e);
            }
        }

        void Track(Battle b, HashSet<Embedded> live)
        {
            foreach (var e in b.Embedded)
            {
                if (e.Breacher || e.Unit.Dead || (e.Unit.Ship != null && e.Unit.Ship.Destroyed)) continue;
                var parent = app.UnitTransform(e.Unit);
                if (parent == null) continue;
                live.Add(e);
                if (pods.TryGetValue(e, out var go) && go != null && go.transform.parent == parent) continue;
                if (go != null) Object.Destroy(go);
                pods[e] = Create(e, parent, b);
            }
        }

        GameObject Create(Embedded e, Transform parent, Battle b)
        {
            var owner = e.Side >= 0 && e.Side < 2 ? b.SideLeaders[e.Side] : e.Unit.Faction;
            var go = new GameObject("Boarding pod");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = ShipMeshFactory.GetPod(owner ?? e.Unit.Faction);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ShipModels.ProceduralMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            // Pods are drawn a little large so they read on a capital ship: a few percent of its length.
            float size = Mathf.Clamp(0.025f / Mathf.Max((float)e.Unit.LengthKm, 0.01f), 0.04f, 0.15f);
            var inward = new Vector3((float)e.Inward.X, (float)e.Inward.Y, (float)e.Inward.Z);
            if (inward.sqrMagnitude < 1e-6f) inward = Vector3.forward;
            inward.Normalize();
            var at = new Vector3((float)e.Local.X, (float)e.Local.Y, (float)e.Local.Z);
            // The drill tip is buried: the pod's centre sits half a pod out along the way it came in.
            go.transform.localPosition = at - inward * size * 0.25f;
            go.transform.localRotation = Quaternion.LookRotation(inward, Mathf.Abs(inward.y) > 0.95f ? Vector3.right : Vector3.up);
            go.transform.localScale = Vector3.one * size;
            return go;
        }
    }
}
