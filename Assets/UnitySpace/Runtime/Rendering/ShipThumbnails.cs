using System.Collections.Generic;
using Unity.SpaceGod.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Unity.SpaceGod.Rendering
{
    /// <summary>
    /// Renders a small offscreen snapshot of each <see cref="ShipClass"/>'s hull -- the same model a
    /// real ship uses (<see cref="ShipModels.Build"/>), just a bare throwaway <see cref="Ship"/> never
    /// added to any <see cref="Fleet"/> -- for the spawn screen's picker grid (<see cref="UI.Hud"/>).
    /// Renders straight to a <see cref="RenderTexture"/> via a manual <see cref="Camera.Render"/> call
    /// rather than an enabled camera, so it never competes with or needs to disable the game's own
    /// camera (unlike <see cref="ModelViewer"/>, whose camera draws to the screen itself). Cached per
    /// class and livery: each one is built and rendered once, not every frame.
    /// </summary>
    public static class ShipThumbnails
    {
        const int Size = 192;
        static readonly int Layer = LayerMask.NameToLayer("ModelViewer");

        static Camera camera;
        static readonly int SunPositionId = Shader.PropertyToID("_SunPositionWS"), SunColorId = Shader.PropertyToID("_SunColor");
        static readonly int Sun2PositionId = Shader.PropertyToID("_Sun2PositionWS"), Sun2ColorId = Shader.PropertyToID("_Sun2Color");
        // Keyed by livery too: the spawn screen shows the same class in the home or the hostile colours.
        static readonly Dictionary<(ShipClass, Faction), RenderTexture> cache = new Dictionary<(ShipClass, Faction), RenderTexture>();

        /// <summary>A cached snapshot of <paramref name="cls"/>'s hull in <paramref name="faction"/>'s livery, rendering it the first time asked.</summary>
        public static Texture For(ShipClass cls, Faction faction)
        {
            var key = (cls, ShipMeshFactory.Canon(faction));
            if (cache.TryGetValue(key, out var tex) && tex != null) return tex;
            tex = Render(cls, faction);
            cache[key] = tex;
            return tex;
        }

        /// <summary>Drops every cached snapshot (e.g. if a faction's livery colour could change later).</summary>
        public static void Clear()
        {
            foreach (var tex in cache.Values) if (tex != null) Object.DestroyImmediate(tex);
            cache.Clear();
        }

        static RenderTexture Render(ShipClass cls, Faction faction)
        {
            EnsureCamera();

            var root = new GameObject("ShipThumbPreview") { hideFlags = HideFlags.HideAndDontSave };
            // Off to one side, but near enough the origin for float precision: at 1e6 world units the hull's
            // vertices snapped to a 1/16-unit grid (6% of a one-unit hull), stair-stepping every edge. The
            // preview lives only inside this call and on its own layer, so nothing else ever draws it.
            root.transform.position = new Vector3(1000f, 1000f, 1000f);

            var ship = new Ship { Id = 0, Spec = ShipClassSpec.Of(cls), Faction = faction };
            var renderers = new List<Renderer>();
            ShipModels.Build(ship, root.transform, renderers, out _, out _);
            SetLayerRecursively(root);

            var bounds = new Bounds(root.transform.position, Vector3.one * 0.01f);
            bool any = false;
            foreach (var r in renderers)
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            float radius = Mathf.Max(bounds.extents.magnitude, 0.05f);

            camera.transform.position = bounds.center + Quaternion.Euler(18f, -35f, 0f) * Vector3.back * (radius * 2.6f);
            camera.transform.LookAt(bounds.center);
            camera.nearClipPlane = Mathf.Max(radius * 0.01f, 0.001f);
            camera.farClipPlane = radius * 10f;

            var rt = new RenderTexture(Size, Size, 16, RenderTextureFormat.ARGB32) { name = "ShipThumb_" + cls };
            // Ship.shader lights only from the scene's global star(s), not Unity lights: light the hull from
            // a fixed key direction for the snapshot, not from wherever the game's Sun happens to be just now.
            var sunPos = Shader.GetGlobalVector(SunPositionId);
            var sunColor = Shader.GetGlobalVector(SunColorId);
            var sun2Pos = Shader.GetGlobalVector(Sun2PositionId);
            var sun2Color = Shader.GetGlobalVector(Sun2ColorId);
            var key = bounds.center + Quaternion.Euler(35f, -30f, 0f) * Vector3.back * (radius * 1000f);
            Shader.SetGlobalVector(SunPositionId, key);
            Shader.SetGlobalVector(SunColorId, new Vector4(1.2f, 1.2f, 1.2f, 1f));
            Shader.SetGlobalVector(Sun2PositionId, key);
            Shader.SetGlobalVector(Sun2ColorId, Vector4.zero);
            camera.targetTexture = rt;
            camera.Render();
            camera.targetTexture = null;
            Shader.SetGlobalVector(SunPositionId, sunPos);
            Shader.SetGlobalVector(SunColorId, sunColor);
            Shader.SetGlobalVector(Sun2PositionId, sun2Pos);
            Shader.SetGlobalVector(Sun2ColorId, sun2Color);

            Object.DestroyImmediate(root);
            return rt;
        }

        static void EnsureCamera()
        {
            if (camera != null) return;
            var go = new GameObject("ShipThumbnailCamera") { hideFlags = HideFlags.HideAndDontSave };
            camera = go.AddComponent<Camera>();
            go.AddComponent<UniversalAdditionalCameraData>();
            // Never auto-rendered into the normal camera stack -- only Render() called explicitly above.
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.06f, 0.09f, 1f);
            camera.cullingMask = 1 << Layer;

            var light = new GameObject("ShipThumbnailLight") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Light>();
            light.transform.SetParent(go.transform, false);
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            light.cullingMask = 1 << Layer;
            light.intensity = 1.2f;
        }

        static void SetLayerRecursively(GameObject root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
        }
    }
}
