using System;
using System.Collections.Generic;
using Unity.SpaceGod.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.SpaceGod.Rendering
{
    /// <summary>What a camera is for, so the pipeline can set it up as that pipeline needs (U.SPC-T429).</summary>
    public enum CameraRole
    {
        /// <summary>The game's own camera, drawing the universe to the screen.</summary>
        Main,

        /// <summary>A camera drawn over the game's own on the screen (the model viewer).</summary>
        Overlay,

        /// <summary>A camera rendered by hand into a texture (ship thumbnails), never part of the frame.</summary>
        Offscreen,
    }

    /// <summary>
    /// The light the hand-written shaders read (U.SPC-T429): the nearest star and a companion, in camera-relative world
    /// space (a position's w is unused; a colour's w is its strength). Set through <see cref="IRenderBackend.Lighting"/>.
    /// </summary>
    public struct StarLighting
    {
        public Vector4 SunPosition, SunColor, Sun2Position, Sun2Color;
    }

    /// <summary>
    /// A post-process look in pipeline-neutral terms (U.SPC-T429): each pipeline turns it into its own volume and
    /// overrides. <see cref="None"/> (the default) means no post-processing at all, which is how the game draws today.
    /// </summary>
    public struct PostProcessLook
    {
        public bool Enabled;

        /// <summary>Exposure compensation, in stops.</summary>
        public float ExposureEv;

        /// <summary>Bloom strength (0 for none) and the brightness it starts at.</summary>
        public float BloomIntensity, BloomThreshold;

        /// <summary>Contrast and saturation, -100 to 100 (0 leaves the image as it is).</summary>
        public float Contrast, Saturation;

        public static PostProcessLook None => default;
    }

    /// <summary>
    /// Every pipeline-specific piece of rendering, behind one interface (U.SPC-T429): the game's shaders, its cameras,
    /// the lights of preview scenes, the star lighting its shaders read, post-processing and renderer features. Game
    /// code reaches the pipeline only through <see cref="RenderBackend.Active"/>, so it never assumes URP; URP's
    /// backend lives in its own assembly (<c>Unity.SpaceGod.Rendering.Urp</c>), the only code that references URP.
    /// </summary>
    public interface IRenderBackend
    {
        RenderPipelineProfile Profile { get; }

        /// <summary>This pipeline's version of the game's shader <paramref name="name"/> (one of <see cref="RenderShaders"/>).</summary>
        Shader LoadShader(string name);

        /// <summary>Readies <paramref name="camera"/> for its <paramref name="role"/> under this pipeline.</summary>
        void SetupCamera(Camera camera, CameraRole role);

        /// <summary>Turns multisample anti-aliasing on or off for <paramref name="camera"/>.</summary>
        void SetMsaa(Camera camera, bool on);

        /// <summary>Adds a directional key light under <paramref name="parent"/> that lights only <paramref name="cullingMask"/>.</summary>
        Light AddKeyLight(Transform parent, string name, Quaternion rotation, float intensity, int cullingMask, HideFlags hideFlags = HideFlags.None);

        /// <summary>The star light the game's shaders read, for every camera.</summary>
        StarLighting Lighting { get; set; }

        /// <summary>Applies <paramref name="look"/> to what <paramref name="camera"/> draws (<see cref="PostProcessLook.None"/> turns it off).</summary>
        void SetPostProcessing(Camera camera, PostProcessLook look);

        /// <summary>The renderer features the active renderer carries, by name.</summary>
        IReadOnlyList<string> RenderFeatures { get; }

        /// <summary>
        /// The photoreal choices of the settings menu (U.SPC-T430). A backend puts into effect those its pipeline
        /// offers (<see cref="RenderPipelineProfile.Features"/>) and keeps the rest only to report them back.
        /// </summary>
        PhotorealOptions Photoreal { get; set; }

        /// <summary>
        /// The planet atmosphere the game's own camera is at, for a physically based sky (U.SPC-T430):
        /// <paramref name="shell"/> describes it and <paramref name="centre"/> is the planet's centre relative to the
        /// camera. <see cref="AtmosphereShell.None"/> clears it. A pipeline without such a sky ignores it (the shaders'
        /// own atmosphere rims draw it there).
        /// </summary>
        void SetAtmosphere(Camera camera, AtmosphereShell shell, Vector3 centre);
    }

    /// <summary>
    /// The active render pipeline's backend (U.SPC-T429). The pipeline is chosen by the pipeline asset Unity is running
    /// (<see cref="GraphicsSettings.currentRenderPipeline"/>), matched to a <see cref="RenderPipelineProfile"/>; the
    /// backend registered for that profile serves it. Switching pipelines is a restart: assign the other asset before
    /// the first camera renders and call <see cref="Reset"/>.
    /// </summary>
    public static class RenderBackend
    {
        static readonly Dictionary<string, Func<IRenderBackend>> factories = new Dictionary<string, Func<IRenderBackend>>();
        static IRenderBackend active;

        /// <summary>Registers how to make the backend for the pipeline <paramref name="profileId"/> (a later one replaces it).</summary>
        public static void Register(string profileId, Func<IRenderBackend> make)
        {
            if (string.IsNullOrEmpty(profileId)) throw new ArgumentException("A backend registers under a pipeline id.", nameof(profileId));
            factories[profileId] = make ?? throw new ArgumentNullException(nameof(make));
            if (active != null && active.Profile.Id == profileId) active = null;
        }

        public static IRenderBackend Active => active ??= Resolve();

        /// <summary>Forgets the active backend, so the next use matches the pipeline asset afresh.</summary>
        public static void Reset() => active = null;

        static IRenderBackend Resolve()
        {
            var asset = GraphicsSettings.currentRenderPipeline;
            string typeName = asset != null ? asset.GetType().Name : null;
            var profile = RenderPipelineProfiles.ForAsset(typeName);
            if (profile == null)
            {
                Debug.LogWarning($"[Unity.SpaceGod] No render pipeline profile for '{typeName ?? "none"}'; drawing as {RenderPipelineProfiles.Default.DisplayName}.");
                profile = RenderPipelineProfiles.Default;
            }
            if (factories.TryGetValue(profile.Id, out var make)) return make();
            Debug.LogWarning($"[Unity.SpaceGod] No backend registered for {profile.DisplayName}; using the pipeline-neutral one.");
            return new NeutralRenderBackend(profile);
        }
    }

    /// <summary>
    /// What every pipeline does the same way (U.SPC-T429): shaders from the profile's Resources folder, camera flags,
    /// a plain directional light, the star lighting as shader globals, and no post-processing or renderer features.
    /// A pipeline's backend derives from it and overrides only what that pipeline does differently; it also serves
    /// on its own when no backend is registered for the active pipeline.
    /// </summary>
    public class NeutralRenderBackend : IRenderBackend
    {
        static readonly int SunPositionId = Shader.PropertyToID(RenderGlobals.SunPosition), SunColorId = Shader.PropertyToID(RenderGlobals.SunColor);
        static readonly int Sun2PositionId = Shader.PropertyToID(RenderGlobals.Sun2Position), Sun2ColorId = Shader.PropertyToID(RenderGlobals.Sun2Color);

        public NeutralRenderBackend(RenderPipelineProfile profile) => Profile = profile ?? throw new ArgumentNullException(nameof(profile));

        public RenderPipelineProfile Profile { get; }

        public virtual Shader LoadShader(string name)
        {
            string path = Profile.ShaderPath(name);
            var shader = Resources.Load<Shader>(path);
            if (shader == null)
            {
                Debug.LogError($"[Unity.SpaceGod] Missing shader Resources/{path}");
                shader = Shader.Find("Hidden/InternalErrorShader");
            }
            return shader;
        }

        public virtual void SetupCamera(Camera camera, CameraRole role)
        {
            // The hand-written shaders draw straight to the display's range: the game's own camera renders LDR.
            if (role == CameraRole.Main && Profile.HonoursCameraHdr) camera.allowHDR = false;
        }

        public virtual void SetMsaa(Camera camera, bool on)
        {
            if (Profile.HonoursCameraMsaa) camera.allowMSAA = on;
        }

        public virtual Light AddKeyLight(Transform parent, string name, Quaternion rotation, float intensity, int cullingMask, HideFlags hideFlags = HideFlags.None)
        {
            var light = new GameObject(name) { hideFlags = hideFlags }.AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.type = LightType.Directional;
            light.transform.rotation = rotation;
            light.intensity = intensity;
            light.cullingMask = cullingMask;
            return light;
        }

        public virtual StarLighting Lighting
        {
            get => new StarLighting
            {
                SunPosition = Shader.GetGlobalVector(SunPositionId), SunColor = Shader.GetGlobalVector(SunColorId),
                Sun2Position = Shader.GetGlobalVector(Sun2PositionId), Sun2Color = Shader.GetGlobalVector(Sun2ColorId),
            };
            set
            {
                Shader.SetGlobalVector(SunPositionId, value.SunPosition);
                Shader.SetGlobalVector(SunColorId, value.SunColor);
                Shader.SetGlobalVector(Sun2PositionId, value.Sun2Position);
                Shader.SetGlobalVector(Sun2ColorId, value.Sun2Color);
            }
        }

        public virtual void SetPostProcessing(Camera camera, PostProcessLook look) { }

        public virtual IReadOnlyList<string> RenderFeatures => Array.Empty<string>();

        public virtual PhotorealOptions Photoreal { get; set; } = PhotorealOptions.Default;

        public virtual void SetAtmosphere(Camera camera, AtmosphereShell shell, Vector3 centre) { }
    }
}
