using System.Collections.Generic;
using Unity.SpaceGod.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Unity.SpaceGod.Rendering.Urp
{
    /// <summary>
    /// The Universal Render Pipeline's backend (U.SPC-T429), the first implementation of <see cref="IRenderBackend"/>
    /// and the only code in the game that references URP. On top of what every pipeline does the same way it gives each
    /// camera URP's per-camera data (a camera added at runtime gets none of its own), turns post-processing into a URP
    /// volume with URP's own overrides, and reads the renderer features off the active URP renderers.
    /// </summary>
    public sealed class UrpRenderBackend : NeutralRenderBackend
    {
        const string VolumeName = "PostProcessVolume";

        public UrpRenderBackend() : base(RenderPipelineProfiles.Universal) { }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void RegisterAtStartup() => RenderBackend.Register(RenderPipelineProfiles.Universal.Id, () => new UrpRenderBackend());

#if UNITY_EDITOR
        // Editor tools (scene setup, previews) draw outside play mode too.
        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterInEditor() => RegisterAtStartup();
#endif

        static UniversalAdditionalCameraData CameraData(Camera camera)
        {
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            return data;
        }

        public override void SetupCamera(Camera camera, CameraRole role)
        {
            base.SetupCamera(camera, role);
            // A camera added purely at runtime (unlike one placed in the scene) gets no URP camera data of its own,
            // and without it URP may not render it correctly at all.
            CameraData(camera);
        }

        public override void SetPostProcessing(Camera camera, PostProcessLook look)
        {
            var data = CameraData(camera);
            data.renderPostProcessing = look.Enabled;
            var existing = camera.transform.Find(VolumeName);
            if (!look.Enabled)
            {
                if (existing != null) existing.gameObject.SetActive(false);
                return;
            }

            Volume volume;
            if (existing != null)
            {
                existing.gameObject.SetActive(true);
                volume = existing.GetComponent<Volume>();
            }
            else
            {
                var go = new GameObject(VolumeName) { layer = camera.gameObject.layer, hideFlags = HideFlags.DontSave };
                go.transform.SetParent(camera.transform, false);
                volume = go.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                volume.sharedProfile.hideFlags = HideFlags.DontSave;
            }
            data.volumeLayerMask |= 1 << camera.gameObject.layer;

            var profile = volume.sharedProfile;
            if (!profile.TryGet(out ColorAdjustments colour)) colour = profile.Add<ColorAdjustments>(true);
            colour.postExposure.Override(look.ExposureEv);
            colour.contrast.Override(look.Contrast);
            colour.saturation.Override(look.Saturation);
            if (!profile.TryGet(out Bloom bloom)) bloom = profile.Add<Bloom>(true);
            bloom.active = look.BloomIntensity > 0f;
            bloom.intensity.Override(look.BloomIntensity);
            bloom.threshold.Override(look.BloomThreshold);
        }

        public override IReadOnlyList<string> RenderFeatures
        {
            get
            {
                var names = new List<string>();
                var asset = UniversalRenderPipeline.asset;
                if (asset == null) return names;
                foreach (var renderer in asset.rendererDataList)
                {
                    if (renderer == null) continue;
                    foreach (var feature in renderer.rendererFeatures)
                        if (feature != null && !names.Contains(feature.name)) names.Add(feature.name);
                }
                return names;
            }
        }
    }
}
