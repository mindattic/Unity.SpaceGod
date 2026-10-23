using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// What the game knows about one render pipeline, as data (U.SPC-T429): which pipeline asset selects it, the
    /// <c>RenderPipeline</c> tag its hand-written shaders' SubShaders carry, the Resources folder they load from, and
    /// how the camera flags behave under it. Game code never names a pipeline: it asks the active backend
    /// (<c>Unity.SpaceGod.Rendering.RenderBackend</c>) for a shader, a camera, a light or a post-process look, and the
    /// backend registered for the active profile supplies it. A second pipeline is one more profile here and one more
    /// backend registered for it, never an edit to the code that draws the game.
    /// </summary>
    public sealed class RenderPipelineProfile
    {
        /// <summary>The key a backend registers under and a setting would store ("urp").</summary>
        public readonly string Id;

        public readonly string DisplayName;

        /// <summary>The type name of the pipeline asset that selects this pipeline (matched against the active one).</summary>
        public readonly string AssetTypeName;

        /// <summary>The <c>"RenderPipeline"</c> SubShader tag every hand-written shader for this pipeline carries.</summary>
        public readonly string ShaderTag;

        /// <summary>The Resources folder this pipeline's versions of the game's shaders load from.</summary>
        public readonly string ShaderFolder;

        /// <summary>Whether the camera's own HDR flag decides HDR (false where the pipeline always renders HDR).</summary>
        public readonly bool HonoursCameraHdr;

        /// <summary>Whether the camera's own MSAA flag decides anti-aliasing (false where the pipeline sets it elsewhere).</summary>
        public readonly bool HonoursCameraMsaa;

        /// <summary>The renderer features the game expects this pipeline's renderer to carry (none, today).</summary>
        public readonly IReadOnlyList<string> RenderFeatures;

        public RenderPipelineProfile(string id, string displayName, string assetTypeName, string shaderTag, string shaderFolder,
            bool honoursCameraHdr = true, bool honoursCameraMsaa = true, IReadOnlyList<string> renderFeatures = null)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A render pipeline needs an id.", nameof(id));
            if (string.IsNullOrEmpty(shaderFolder)) throw new ArgumentException("A render pipeline needs a shader folder.", nameof(shaderFolder));
            Id = id;
            DisplayName = string.IsNullOrEmpty(displayName) ? id : displayName;
            AssetTypeName = assetTypeName ?? "";
            ShaderTag = shaderTag ?? "";
            ShaderFolder = shaderFolder.TrimEnd('/');
            HonoursCameraHdr = honoursCameraHdr;
            HonoursCameraMsaa = honoursCameraMsaa;
            RenderFeatures = renderFeatures ?? Array.Empty<string>();
        }

        /// <summary>The Resources path of <paramref name="shader"/> (one of <see cref="RenderShaders"/>) under this pipeline.</summary>
        public string ShaderPath(string shader) => ShaderFolder + "/" + shader;

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// The game's hand-written shaders by name, independent of any pipeline (U.SPC-T429). Each pipeline supplies its
    /// own version of every one (<see cref="RenderPipelineProfile.ShaderPath"/>); the shading math they share lives in
    /// the pipeline-neutral <c>SolarCommon.hlsl</c>, and the one pipeline-specific include in <c>PipelineCore.hlsl</c>.
    /// </summary>
    public static class RenderShaders
    {
        public const string Planet = "Planet", Sun = "Sun", Glow = "Glow", Ring = "Ring", Ship = "Ship", Line = "Line",
            Stars = "Stars", GasGiant = "GasGiant", Asteroids = "Asteroids", Fx = "Fx", Points = "Points";

        public static readonly IReadOnlyList<string> All = new[] { Planet, Sun, Glow, Ring, Ship, Line, Stars, GasGiant, Asteroids, Fx, Points };
    }

    /// <summary>
    /// The shader globals the game lights everything by (U.SPC-T429): the nearest star and a companion star, in
    /// camera-relative world space. Hand-written shaders read these instead of Unity lights, on every pipeline.
    /// </summary>
    public static class RenderGlobals
    {
        public const string SunPosition = "_SunPositionWS", SunColor = "_SunColor", Sun2Position = "_Sun2PositionWS", Sun2Color = "_Sun2Color";
    }

    /// <summary>
    /// The render pipelines the game knows (U.SPC-T429). URP is the first and the default; another registers its
    /// profile here and its backend with <c>RenderBackend.Register</c>.
    /// </summary>
    public static class RenderPipelineProfiles
    {
        /// <summary>
        /// URP 17: the shaders carry <c>"UniversalPipeline"</c> and load from <c>Resources/Shaders</c>, the camera's HDR
        /// and MSAA flags are honoured, and the renderer carries no custom features.
        /// </summary>
        public static readonly RenderPipelineProfile Universal = new RenderPipelineProfile("urp", "Universal Render Pipeline",
            "UniversalRenderPipelineAsset", "UniversalPipeline", "Shaders");

        static readonly List<RenderPipelineProfile> registered = new List<RenderPipelineProfile> { Universal };

        public static IReadOnlyList<RenderPipelineProfile> All => registered;

        /// <summary>The pipeline used when the active one is none the game knows.</summary>
        public static RenderPipelineProfile Default => Universal;

        /// <summary>Adds <paramref name="profile"/>, replacing one already registered under its id.</summary>
        public static void Register(RenderPipelineProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            int i = registered.FindIndex(p => p.Id == profile.Id);
            if (i >= 0) registered[i] = profile;
            else registered.Add(profile);
        }

        /// <summary>The profile registered under <paramref name="id"/>, or null.</summary>
        public static RenderPipelineProfile Find(string id) => registered.Find(p => p.Id == id);

        /// <summary>
        /// The profile whose pipeline asset is of type <paramref name="assetTypeName"/>, or null when none is (no
        /// pipeline asset at all, or one the game has no backend for).
        /// </summary>
        public static RenderPipelineProfile ForAsset(string assetTypeName)
        {
            if (string.IsNullOrEmpty(assetTypeName)) return null;
            return registered.Find(p => string.Equals(p.AssetTypeName, assetTypeName, StringComparison.Ordinal));
        }
    }
}
