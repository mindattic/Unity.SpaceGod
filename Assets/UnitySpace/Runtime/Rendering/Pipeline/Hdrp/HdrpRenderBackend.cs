using System.Collections.Generic;
using Unity.SpaceGod.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using HdExposure = UnityEngine.Rendering.HighDefinition.Exposure;
using HdExposureMode = UnityEngine.Rendering.HighDefinition.ExposureMode;

namespace Unity.SpaceGod.Rendering.Hdrp
{
    /// <summary>
    /// The High Definition Render Pipeline's backend (U.SPC-T430), the photoreal path and the second implementation of
    /// <see cref="IRenderBackend"/>; with the URP backend's assembly, the only code that references a pipeline. The
    /// game's shaders draw under HDRP from their own <c>"HDRenderPipeline"</c> SubShaders, display-referred colours
    /// scaled by the scene luminance and the camera's exposure (<c>PipelineCore.hlsl</c>), so at the reference
    /// exposure they look as they do on URP. On top of them this backend gives:
    /// <list type="bullet">
    /// <item>exposure metered for the star's light (EV100 15), fixed or adapting like an eye, and filmic tonemapping;</item>
    /// <item>a physically based sky for the planet the camera is at (<see cref="SetAtmosphere"/>), with volumetric
    /// light inside its air, lit by a directional light that follows the star;</item>
    /// <item>ray-traced shadows and reflections where the GPU and graphics API support them.</item>
    /// </list>
    /// HDRP ignores a light's culling mask, so the preview key lights are switched on only while their own camera draws.
    /// </summary>
    public sealed class HdrpRenderBackend : NeutralRenderBackend
    {
        // Volumes are filtered by GameObject layer, and only the cameras below look at these two (they never render).
        const int EnvironmentLayer = 31, NeutralLayer = 30;
        const float FarFog = 1e7f;

        static readonly int SceneLuminanceId = Shader.PropertyToID(RenderGlobals.SceneLuminance);
        static readonly int SunBrightnessId = Shader.PropertyToID(RenderGlobals.SunBrightness);

        static HdrpRenderBackend live;

        readonly Dictionary<Camera, CameraRole> roles = new Dictionary<Camera, CameraRole>();
        readonly List<KeyValuePair<Light, Camera>> keyLights = new List<KeyValuePair<Light, Camera>>();
        PostProcessLook mainLook = PostProcessLook.None, otherLook = PostProcessLook.None;
        PhotorealOptions photoreal = PhotorealOptions.Default;
        AtmosphereShell atmosphere = AtmosphereShell.None;
        Vector3 atmosphereCentre;
        StarLighting starLighting;
        Volume environment, neutral;
        Light sun;
        HDAdditionalLightData sunData;

        public HdrpRenderBackend() : base(RenderPipelineProfiles.HighDefinition)
        {
            live = this;
            Shader.SetGlobalFloat(SceneLuminanceId, (float)Profile.ReferenceLuminance);
            Shader.SetGlobalFloat(SunBrightnessId, (float)photoreal.SunBrightness);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void RegisterAtStartup()
        {
            RenderBackend.Register(RenderPipelineProfiles.HighDefinition.Id, () => new HdrpRenderBackend());
            RenderPipelineManager.beginContextRendering -= BeginContext;
            RenderPipelineManager.beginContextRendering += BeginContext;
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.beginCameraRendering += BeginCamera;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterInEditor() => RegisterAtStartup();
#endif

        /// <summary>Whether ray tracing can run: the HDRP instance running supports it (asset, GPU and graphics API).</summary>
        static bool RayTracingAvailable => SystemInfo.supportsRayTracing && RenderPipelineManager.currentPipeline is HDRenderPipeline hd && hd.rayTracingSupported;

        bool RayTracingOn => photoreal.RayTracing && Profile.Offers(PipelineFeatures.RayTracing) && RayTracingAvailable;

        bool SkyOn => photoreal.PhysicalSky && Profile.Offers(PipelineFeatures.PhysicalSky) && !atmosphere.IsNone;

        static HDAdditionalCameraData CameraData(Camera camera)
        {
            var data = camera.GetComponent<HDAdditionalCameraData>();
            if (data == null) data = camera.gameObject.AddComponent<HDAdditionalCameraData>();
            return data;
        }

        public override void SetupCamera(Camera camera, CameraRole role)
        {
            base.SetupCamera(camera, role);
            var data = CameraData(camera);
            roles[camera] = role;
            data.volumeLayerMask = 1 << (role == CameraRole.Main ? EnvironmentLayer : NeutralLayer);
            EnsureVolumes();
            SyncCamera(camera, role, data);
        }

        public override void SetMsaa(Camera camera, bool on)
        {
            // HDRP sets multisampling in its frame settings, not per camera: a camera's anti-aliasing is post-process (SMAA).
            CameraData(camera).antialiasing = on ? HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing
                : HDAdditionalCameraData.AntialiasingMode.None;
        }

        public override Light AddKeyLight(Transform parent, string name, Quaternion rotation, float intensity, int cullingMask, HideFlags hideFlags = HideFlags.None)
        {
            var go = new GameObject(name) { hideFlags = hideFlags };
            go.transform.SetParent(parent, false);
            go.AddHDLight(LightType.Directional);
            var light = go.GetComponent<Light>();
            go.transform.rotation = rotation;
            light.intensity = intensity * (float)Profile.LuxPerLightUnit;
            light.cullingMask = cullingMask;
            light.shadows = LightShadows.None;
            keyLights.Add(new KeyValuePair<Light, Camera>(light, parent != null ? parent.GetComponentInParent<Camera>() : null));
            return light;
        }

        public override StarLighting Lighting
        {
            get => base.Lighting;
            set
            {
                base.Lighting = value;
                starLighting = value;
                UpdateSun();
            }
        }

        public override void SetPostProcessing(Camera camera, PostProcessLook look)
        {
            if (roles.TryGetValue(camera, out var role) && role == CameraRole.Main) mainLook = look;
            else otherLook = look;
            EnsureVolumes();
            UpdateVolumes();
        }

        public override PhotorealOptions Photoreal
        {
            get => photoreal;
            set
            {
                photoreal = value;
                Shader.SetGlobalFloat(SunBrightnessId, (float)System.Math.Max(1, value.SunBrightness));
                UpdateSun();
                if (environment != null) UpdateVolumes();
            }
        }

        public override void SetAtmosphere(Camera camera, AtmosphereShell shell, Vector3 centre)
        {
            atmosphere = shell;
            atmosphereCentre = centre;
            if (environment != null) UpdateEnvironment(environment.sharedProfile);
        }

        // ---- the star's light -------------------------------------------------------------------------------------

        void UpdateSun()
        {
            if (sun == null)
            {
                var go = new GameObject("HdrpStarLight") { hideFlags = HideFlags.HideAndDontSave };
                sunData = go.AddHDLight(LightType.Directional);
                sun = go.GetComponent<Light>();
                sunData.angularDiameter = 0f; // the game draws the star itself; the sky must not add a second disc
                sunData.interactsWithSky = true;
            }
            var toStar = (Vector3)starLighting.SunPosition;
            if (toStar.sqrMagnitude > 0f) sun.transform.rotation = Quaternion.LookRotation(-toStar.normalized);
            var c = starLighting.SunColor;
            float max = Mathf.Max(c.x, Mathf.Max(c.y, c.z));
            sun.color = max > 0f ? new Color(c.x / max, c.y / max, c.z / max) : Color.white;
            sun.intensity = Mathf.Max(0f, c.w) * (float)Profile.LuxPerLightUnit;
            bool rt = RayTracingOn;
            sun.shadows = rt ? LightShadows.Soft : LightShadows.None;
            sunData.useRayTracedShadows = rt;
            sunData.affectsVolumetric = photoreal.Volumetrics;
        }

        // ---- volumes ----------------------------------------------------------------------------------------------

        static Volume MakeVolume(string name, int layer, int priority)
        {
            var go = new GameObject(name) { layer = layer, hideFlags = HideFlags.HideAndDontSave };
            if (Application.isPlaying) Object.DontDestroyOnLoad(go);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = priority;
            volume.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile.hideFlags = HideFlags.HideAndDontSave;
            return volume;
        }

        void EnsureVolumes()
        {
            if (environment != null && neutral != null) return;
            if (environment == null) environment = MakeVolume("HdrpEnvironment", EnvironmentLayer, 100);
            if (neutral == null) neutral = MakeVolume("HdrpNeutral", NeutralLayer, 100);
            UpdateVolumes();
        }

        static T Get<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component)) component = profile.Add<T>(true);
            return component;
        }

        void UpdateVolumes()
        {
            if (environment != null) UpdateEnvironment(environment.sharedProfile);
            if (neutral != null) UpdateNeutral(neutral.sharedProfile);
        }

        static void ApplyLook(VolumeProfile profile, PostProcessLook look, float defaultBloom)
        {
            var colour = Get<ColorAdjustments>(profile);
            colour.postExposure.Override(look.Enabled ? look.ExposureEv : 0f);
            colour.contrast.Override(look.Enabled ? look.Contrast : 0f);
            colour.saturation.Override(look.Enabled ? look.Saturation : 0f);
            var bloom = Get<Bloom>(profile);
            bloom.intensity.Override(look.Enabled ? Mathf.Clamp01(look.BloomIntensity) : defaultBloom);
            bloom.threshold.Override(look.Enabled ? look.BloomThreshold : 1f);
        }

        /// <summary>What the game's own camera sees by: its metering, the sky and air of the planet it is at, ray tracing.</summary>
        void UpdateEnvironment(VolumeProfile profile)
        {
            double reference = Profile.HasExposure ? Profile.ReferenceEv100 : 0;
            var exposure = Get<HdExposure>(profile);
            bool adaptive = photoreal.Exposure == SunExposure.Adaptive && Profile.Offers(PipelineFeatures.Exposure);
            exposure.mode.Override(adaptive ? HdExposureMode.AutomaticHistogram : HdExposureMode.Fixed);
            exposure.fixedExposure.Override((float)photoreal.MeteredEv100(reference));
            exposure.compensation.Override(adaptive ? (float)photoreal.ExposureCompensation : 0f);
            // An eye adapts a long way, but never so far that deep space turns grey or the star's face goes black.
            exposure.limitMin.Override((float)(reference - 6));
            exposure.limitMax.Override((float)(reference + 4));

            Get<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
            ApplyLook(profile, mainLook, 0.15f);

            bool sky = SkyOn;
            var visual = Get<VisualEnvironment>(profile);
            visual.skyType.Override(sky ? (int)SkyType.PhysicallyBased : 0);
            visual.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            visual.renderingSpace.Override(RenderingSpace.World);
            visual.centerMode.Override(VisualEnvironment.PlanetMode.Manual);
            if (sky)
            {
                visual.planetRadius.Override((float)atmosphere.Radius);
                visual.planetCenter.Override(atmosphereCentre);
                var pbs = Get<PhysicallyBasedSky>(profile);
                pbs.type.Override(PhysicallyBasedSkyModel.Custom);
                pbs.atmosphericScattering.Override(true);
                pbs.airDensityR.Override((float)atmosphere.AirR);
                pbs.airDensityG.Override((float)atmosphere.AirG);
                pbs.airDensityB.Override((float)atmosphere.AirB);
                pbs.airTint.Override(Color.white);
                pbs.airMaximumAltitude.Override((float)atmosphere.Thickness);
                pbs.aerosolDensity.Override((float)atmosphere.Haze);
                pbs.aerosolMaximumAltitude.Override((float)atmosphere.HazeThickness);
                pbs.ozoneDensityDimmer.Override(0f);
                pbs.groundTint.Override(new Color(0.12f, 0.10f, 0.09f));
                pbs.spaceEmissionMultiplier.Override(0f);
            }

            var fog = Get<Fog>(profile);
            fog.enabled.Override(sky);
            bool inside = sky && atmosphere.CameraInside;
            float freePath = inside && !double.IsInfinity(atmosphere.FogMeanFreePath) ? (float)System.Math.Min(atmosphere.FogMeanFreePath, FarFog) : FarFog;
            fog.meanFreePath.Override(Mathf.Max(1f, freePath));
            fog.baseHeight.Override(-FarFog);
            fog.maximumHeight.Override(FarFog);
            fog.maxFogDistance.Override(sky ? Mathf.Max(1f, (float)atmosphere.Thickness * 20f) : 5000f);
            fog.enableVolumetricFog.Override(inside && photoreal.Volumetrics && Profile.Offers(PipelineFeatures.Volumetrics));
            fog.depthExtent.Override(Mathf.Clamp((float)atmosphere.Thickness * 4f, 0.1f, 1e5f));

            bool rt = RayTracingOn;
            var reflections = Get<ScreenSpaceReflection>(profile);
            reflections.enabled.Override(rt);
            reflections.tracing.Override(RayCastingMode.RayTracing);
            UpdateSun();
        }

        /// <summary>What previews and thumbnails see by: the reference metering, no sky, no fog, so they look as drawn.</summary>
        void UpdateNeutral(VolumeProfile profile)
        {
            var exposure = Get<HdExposure>(profile);
            exposure.mode.Override(HdExposureMode.Fixed);
            exposure.fixedExposure.Override(Profile.HasExposure ? (float)Profile.ReferenceEv100 : 0f);
            exposure.compensation.Override(0f);
            Get<Tonemapping>(profile).mode.Override(TonemappingMode.None);
            ApplyLook(profile, otherLook, 0f);
            Get<VisualEnvironment>(profile).skyType.Override(0);
            Get<Fog>(profile).enabled.Override(false);
            Get<ScreenSpaceReflection>(profile).enabled.Override(false);
        }

        // ---- per frame --------------------------------------------------------------------------------------------

        void SyncCamera(Camera camera, CameraRole role, HDAdditionalCameraData data)
        {
            switch (camera.clearFlags)
            {
                case CameraClearFlags.Nothing:
                case CameraClearFlags.Depth:
                    data.clearColorMode = HDAdditionalCameraData.ClearColorMode.None;
                    break;
                default:
                    // The game's own camera clears to the sky where a planet's air is drawn (black space elsewhere).
                    data.clearColorMode = role == CameraRole.Main && SkyOn ? HDAdditionalCameraData.ClearColorMode.Sky
                        : HDAdditionalCameraData.ClearColorMode.Color;
                    data.backgroundColorHDR = camera.backgroundColor.linear;
                    break;
            }
        }

        // Before any camera of the frame: each one's clear follows its own clear flags and colour (set after SetupCamera).
        static void BeginContext(ScriptableRenderContext context, List<Camera> cameras)
        {
            var backend = live;
            if (backend == null || !(RenderPipelineManager.currentPipeline is HDRenderPipeline)) return;
            foreach (var camera in cameras)
            {
                if (camera == null) continue;
                // A scene's own camera nobody set up (the main menu's) is the game's view: it sees by the environment.
                if (!backend.roles.ContainsKey(camera) && camera.cameraType == CameraType.Game) backend.SetupCamera(camera, CameraRole.Main);
                if (!backend.roles.TryGetValue(camera, out var role)) continue;
                var data = camera.GetComponent<HDAdditionalCameraData>();
                if (data != null) backend.SyncCamera(camera, role, data);
            }
        }

        // HDRP lights every camera with every light: only the camera a key light belongs to sees it, and only the
        // game's own camera sees the star's light.
        static void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            var backend = live;
            if (backend == null || !(RenderPipelineManager.currentPipeline is HDRenderPipeline)) return;
            for (int i = backend.keyLights.Count - 1; i >= 0; i--)
            {
                var light = backend.keyLights[i].Key;
                if (light == null)
                {
                    backend.keyLights.RemoveAt(i);
                    continue;
                }
                light.enabled = backend.keyLights[i].Value == camera;
            }
            if (backend.sun != null)
                backend.sun.enabled = backend.roles.TryGetValue(camera, out var role) ? role == CameraRole.Main : camera.cameraType != CameraType.Game;
        }
    }
}
