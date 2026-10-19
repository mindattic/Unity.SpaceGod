using System.Collections.Generic;
using UnitySpace.Core;
using UnitySpace.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnitySpace.Views
{
    /// <summary>
    /// Every drifting cluster of escape pods (<see cref="Crews.Pods"/>): a handful of small capsules tumbling
    /// slowly where their hull died, and a distress beacon that blinks a few pixels wide however far off it is,
    /// so a pilot can find them. Drawn at the battle scale, where the fight that launched them was drawn;
    /// gone once they are picked up or their air runs out.
    /// </summary>
    public sealed class PodBeacons
    {
        sealed class View
        {
            public Transform[] Capsules;
            public Transform Beacon;
        }

        readonly UnitySpaceApp app;
        readonly Transform parent;
        readonly Dictionary<EscapePod, View> views = new Dictionary<EscapePod, View>();
        readonly List<EscapePod> gone = new List<EscapePod>();
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        static readonly int ColorId = Shader.PropertyToID("_Tint");
        static Mesh capsule, quad;
        static Material beaconMaterial;

        /// <summary>The most capsules drawn for one cluster, however many it launched.</summary>
        const int MaxCapsules = 6;

        /// <summary>A pod's drawn length, km (a lifeboat capsule, a little larger than life so it reads beside its hull).</summary>
        const double PodLengthKm = 0.02;

        public PodBeacons(UnitySpaceApp app, Transform parent)
        {
            this.app = app;
            this.parent = parent;
            block.SetColor(ColorId, new Color(0.85f, 0.55f, 0.2f));
            if (beaconMaterial == null)
            {
                beaconMaterial = Materials.MarkShared(Materials.Create(Materials.Glow, "PodBeacon"));
                beaconMaterial.SetColor("_Color", new Color(0.35f, 0.95f, 1f));
                beaconMaterial.SetFloat("_Intensity", 1.8f);
                beaconMaterial.SetFloat("_Falloff", 2f);
            }
        }

        public void Update()
        {
            var pods = app.World.War.Crews.Pods;
            var cam = app.UnityCamera.transform;
            double ppu = app.PixelsPerUnitAtUnitDistance();
            float now = Time.unscaledTime;
            foreach (var p in pods)
            {
                if (!p.Drifting) continue;
                if (!views.TryGetValue(p, out var v)) views[p] = v = Create(p);
                var centre = app.PodScene(p);
                var rel = app.Relative(centre);
                float length = (float)app.Scale.ShipLength(PodLengthKm);
                for (int k = 0; k < v.Capsules.Length; k++)
                {
                    var t = v.Capsules[k];
                    // Each capsule keeps its own place in the loose cluster, a few pod lengths apart.
                    float a = k * 2.399f + p.Id, r = length * (1.5f + 1.2f * k);
                    var off = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a * 1.7f) * r * 0.4f, Mathf.Sin(a) * r);
                    t.localPosition = rel + off;
                    t.localRotation = Quaternion.Euler(now * (6 + k * 2), now * (4 + k * 3), now * (3 + k));
                    t.localScale = new Vector3(length * 0.45f, length * 0.5f, length * 0.45f);
                }
                // The beacon blinks once a second: a few pixels wide at any range.
                bool on = Mathf.Repeat(now + p.Id * 0.37f, 1f) < 0.35f;
                v.Beacon.gameObject.SetActive(on);
                if (on)
                {
                    float px = 6f * Mathf.Max(rel.magnitude, 1e-6f) / (float)ppu;
                    v.Beacon.localPosition = rel;
                    v.Beacon.rotation = Quaternion.LookRotation(v.Beacon.position - cam.position, cam.up);
                    v.Beacon.localScale = Vector3.one * Mathf.Max(px, length * 2f);
                }
            }
            gone.Clear();
            foreach (var p in views.Keys)
                if (!p.Drifting || !pods.Contains(p)) gone.Add(p);
            foreach (var p in gone)
            {
                var v = views[p];
                foreach (var t in v.Capsules) Object.Destroy(t.gameObject);
                Object.Destroy(v.Beacon.gameObject);
                views.Remove(p);
            }
        }

        View Create(EscapePod p)
        {
            capsule ??= Resources.GetBuiltinResource<Mesh>("Capsule.fbx");
            quad ??= MeshFactory.Quad();
            var v = new View { Capsules = new Transform[Mathf.Clamp(p.Pods, 1, MaxCapsules)] };
            for (int k = 0; k < v.Capsules.Length; k++)
            {
                var go = new GameObject($"{p.Name} escape pod {k}");
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = capsule;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ShipModels.ProceduralMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.SetPropertyBlock(block);
                v.Capsules[k] = go.transform;
            }
            var beacon = new GameObject($"{p.Name} pod beacon");
            beacon.transform.SetParent(parent, false);
            beacon.AddComponent<MeshFilter>().sharedMesh = quad;
            var br = beacon.AddComponent<MeshRenderer>();
            br.sharedMaterial = beaconMaterial;
            br.shadowCastingMode = ShadowCastingMode.Off;
            v.Beacon = beacon.transform;
            return v;
        }
    }
}
