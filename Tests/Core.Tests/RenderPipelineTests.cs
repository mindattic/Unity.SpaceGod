using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The render-pipeline abstraction (U.SPC-T429): pipelines are known as data, URP is the first and the default, and
    /// nothing outside the pipeline layer assumes URP (its namespace, its camera data, the shader globals, camera flags,
    /// lights, or its shader library). HDRP is the second (U.SPC-T430): every shader carries a SubShader for it, its
    /// backend is the only code that references it, and the project runs it from a quality level of its own.
    /// </summary>
    [TestFixture]
    public class RenderPipelineTests
    {
        static readonly Lazy<string> Root = new Lazy<string>(() =>
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "UnitySpace"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Assets/UnitySpace not found above " + AppContext.BaseDirectory);
        });

        static string UnitySpace => Path.Combine(Root.Value, "Assets", "UnitySpace");
        static string PipelineLayer => Path.Combine(UnitySpace, "Runtime", "Rendering", "Pipeline");
        static string UrpBackend => Path.Combine(PipelineLayer, "Urp");
        static string HdrpBackend => Path.Combine(PipelineLayer, "Hdrp");
        static string ShaderRoot => Path.Combine(UnitySpace, "Resources", "Shaders");

        static string Rel(string path) => Path.GetRelativePath(Root.Value, path).Replace('\\', '/');

        static bool Under(string path, string dir) =>
            Path.GetFullPath(path).StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        [Test]
        public void UrpIsTheFirstPipelineAndTheDefault()
        {
            var urp = RenderPipelineProfiles.Universal;
            Assert.That(RenderPipelineProfiles.Default, Is.SameAs(urp));
            Assert.That(RenderPipelineProfiles.Find("urp"), Is.SameAs(urp));
            Assert.That(RenderPipelineProfiles.ForAsset("UniversalRenderPipelineAsset"), Is.SameAs(urp));
            Assert.That(urp.ShaderTag, Is.EqualTo("UniversalPipeline"));
            Assert.That(urp.ShaderPath(RenderShaders.Planet), Is.EqualTo("Shaders/Planet"));
            Assert.That(urp.RenderFeatures, Is.Empty, "the URP renderer carries no custom features");
            Assert.That(urp.HasExposure, Is.False, "URP draws the shaders' colours as they are");
            Assert.That(urp.ReferenceLuminance, Is.EqualTo(1.0));
            Assert.That(urp.LuxPerLightUnit, Is.EqualTo(1.0));
            Assert.That(urp.Features, Is.EqualTo(PipelineFeatures.None));
            Assert.That(RenderPipelineProfiles.All[0], Is.SameAs(urp), "URP heads the menu");
        }

        [Test]
        public void HdrpIsTheSecondPipelineAndThePhotorealPath()
        {
            var hdrp = RenderPipelineProfiles.HighDefinition;
            Assert.That(RenderPipelineProfiles.Find("hdrp"), Is.SameAs(hdrp));
            Assert.That(RenderPipelineProfiles.ForAsset("HDRenderPipelineAsset"), Is.SameAs(hdrp));
            Assert.That(RenderPipelineProfiles.All.ToList().IndexOf(hdrp), Is.EqualTo(1));
            Assert.That(hdrp.ShaderTag, Is.EqualTo("HDRenderPipeline"));
            Assert.That(hdrp.ShaderFolder, Is.EqualTo(RenderPipelineProfiles.Universal.ShaderFolder), "one dual-pipeline file per shader");
            Assert.That(hdrp.HonoursCameraHdr, Is.False, "HDRP always renders HDR");
            Assert.That(hdrp.HonoursCameraMsaa, Is.False, "HDRP sets multisampling in its frame settings");
            Assert.That(hdrp.RequiresComputeShaders, Is.True);
            foreach (var f in new[] { PipelineFeatures.PhysicalSky, PipelineFeatures.Volumetrics, PipelineFeatures.Exposure, PipelineFeatures.RayTracing })
                Assert.That(hdrp.Offers(f), Is.True, f.ToString());
            Assert.That(RenderPipelineProfiles.Universal.Offers(PipelineFeatures.PhysicalSky), Is.False);

            // Metered for sunlight: EV100 15 shows 1.2 x 2^15 cd/m2 as white, and a light of 1 is about full sunlight in lux.
            Assert.That(hdrp.HasExposure, Is.True);
            Assert.That(hdrp.ReferenceEv100, Is.EqualTo(15));
            Assert.That(hdrp.ReferenceLuminance, Is.EqualTo(39321.6).Within(1e-6));
            Assert.That(hdrp.LuxPerLightUnit, Is.EqualTo(123532).Within(1));
            Assert.That(ExposureMath.Multiplier(15) * hdrp.ReferenceLuminance, Is.EqualTo(1).Within(1e-12),
                "at the reference exposure a shader's colour reaches the image unchanged");
            Assert.That(ExposureMath.Ev100ForLuminance(ExposureMath.LuminanceAtEv100(7.5)), Is.EqualTo(7.5).Within(1e-12));
        }

        [Test]
        public void ThePipelineIsChosenByItsMenuNameOrId()
        {
            Assert.That(RenderPipelineProfiles.ForMenuName(RenderPipelineProfiles.HighDefinition.MenuName), Is.SameAs(RenderPipelineProfiles.HighDefinition));
            Assert.That(RenderPipelineProfiles.ForMenuName(RenderPipelineProfiles.Universal.MenuName), Is.SameAs(RenderPipelineProfiles.Universal));
            Assert.That(RenderPipelineProfiles.ForMenuName("HDRP"), Is.SameAs(RenderPipelineProfiles.HighDefinition), "-pipeline hdrp");
            Assert.That(RenderPipelineProfiles.ForMenuName("urp"), Is.SameAs(RenderPipelineProfiles.Universal));
            Assert.That(RenderPipelineProfiles.ForMenuName("Vulkan Ray Pipeline"), Is.Null);
            Assert.That(RenderPipelineProfiles.ForMenuName(null), Is.Null);
            Assert.That(RenderPipelineProfiles.All.Select(p => p.MenuName), Is.Unique, "the menu remembers the choice by name");
        }

        [Test]
        public void APipelineRunsFromAQualityLevelThatNamesItsAsset()
        {
            var urp = RenderPipelineProfiles.Universal;
            var hdrp = RenderPipelineProfiles.HighDefinition;
            // As the project has them: five levels on the default (URP) asset, Ultra naming URP, then HDRP's own.
            var levels = new[] { null, null, null, null, null, "UniversalRenderPipelineAsset", "HDRenderPipelineAsset" };
            const string def = "UniversalRenderPipelineAsset";
            Assert.That(RenderPipelineProfiles.QualityLevelFor(urp, levels, def, 5), Is.EqualTo(5), "already on URP: stays");
            Assert.That(RenderPipelineProfiles.QualityLevelFor(urp, levels, def, 2), Is.EqualTo(2), "a level falling back to the URP default runs URP");
            Assert.That(RenderPipelineProfiles.QualityLevelFor(hdrp, levels, def, 5), Is.EqualTo(6));
            Assert.That(RenderPipelineProfiles.QualityLevelFor(urp, levels, def, 6), Is.EqualTo(5), "back from HDRP to the highest URP level");
            Assert.That(RenderPipelineProfiles.QualityLevelFor(hdrp, levels.Take(6).ToArray(), def, 5), Is.EqualTo(-1), "no HDRP level: stays put");
            Assert.That(RenderPipelineProfiles.QualityLevelFor(null, levels, def, 5), Is.EqualTo(-1));
        }

        static readonly Regex GuidPattern = new Regex(@"guid:\s*([0-9a-f]{32})");

        static string Guid(string metaPath) => GuidPattern.Match(File.ReadAllText(metaPath)).Groups[1].Value;

        [Test]
        public void TheProjectRunsUrpByDefaultAndHdrpFromItsOwnQualityLevel()
        {
            string manifest = File.ReadAllText(Path.Combine(Root.Value, "Packages", "manifest.json"));
            Assert.That(manifest, Does.Contain("\"com.unity.render-pipelines.universal\""));
            Assert.That(manifest, Does.Contain("\"com.unity.render-pipelines.high-definition\""));

            string urpGuid = Guid(Path.Combine(UnitySpace, "Settings", "UnitySpace_URP.asset.meta"));
            string hdrpGuid = Guid(Path.Combine(UnitySpace, "Settings", "UnitySpace_HDRP.asset.meta"));
            Assert.That(hdrpGuid, Is.Not.Empty.And.Not.EqualTo(urpGuid));
            Assert.That(File.ReadAllText(Path.Combine(UnitySpace, "Settings", "UnitySpace_HDRP.asset")), Does.Contain("HDRenderPipelineAsset"));

            string graphics = File.ReadAllText(Path.Combine(Root.Value, "ProjectSettings", "GraphicsSettings.asset"));
            Assert.That(Regex.Match(graphics, @"m_CustomRenderPipeline:.*guid: ([0-9a-f]{32})").Groups[1].Value, Is.EqualTo(urpGuid), "URP is the default asset");
            Assert.That(graphics, Does.Contain("HighDefinition.HDRenderPipeline:"), "HDRP's global settings are registered, so a player carries its resources");

            string quality = File.ReadAllText(Path.Combine(Root.Value, "ProjectSettings", "QualitySettings.asset"));
            var levelPipelines = Regex.Matches(quality, @"customRenderPipeline: \{[^}]*\}").Cast<Match>()
                .Select(m => GuidPattern.Match(m.Value).Groups[1].Value).ToList();
            Assert.That(levelPipelines.Count(g => g == hdrpGuid), Is.EqualTo(1), "one quality level runs HDRP");
            string hdrpLevel = levelPipelines.IndexOf(hdrpGuid).ToString();
            Assert.That(Regex.Match(quality, @"m_CurrentQuality: (\d+)").Groups[1].Value, Is.Not.EqualTo(hdrpLevel), "the editor stays on URP");
            Assert.That(Regex.Match(quality, @"Standalone: (\d+)").Groups[1].Value, Is.Not.EqualTo(hdrpLevel), "a player starts on URP");
            // Unity builds no player with two pipelines: the URP player leaves HDRP's level out, and the HDRP player is
            // built on its own (ProjectSetup.BuildWindowsHdrp).
            string hdrpBlock = quality.Substring(quality.IndexOf("name: Photoreal (HDRP)", StringComparison.Ordinal));
            Assert.That(Regex.Match(hdrpBlock, @"excludedTargetPlatforms:\s*\n\s*- Standalone").Success, Is.True, "HDRP's level is not in the URP player");
        }

        [Test]
        public void EachPipelineIsItsOwnPlayerBesideTheDefaultOne()
        {
            var urp = RenderPipelineProfiles.Universal;
            var hdrp = RenderPipelineProfiles.HighDefinition;
            Assert.That(urp.PlayerFolder, Is.Empty, "the default player is the install itself");
            Assert.That(hdrp.PlayerFolder, Is.EqualTo("HDRP"));
            string root = Path.Combine("C:", "Apps", "Unity.SpaceGod");
            string urpExe = Path.Combine(root, "Unity.SpaceGod.exe");
            string hdrpExe = Path.Combine(root, "HDRP", "Unity.SpaceGod.exe");
            Assert.That(RenderPipelineProfiles.PlayerExecutable(urpExe, urp, hdrp), Is.EqualTo(hdrpExe));
            Assert.That(RenderPipelineProfiles.PlayerExecutable(hdrpExe, hdrp, urp), Is.EqualTo(urpExe));
            Assert.That(RenderPipelineProfiles.PlayerExecutable(urpExe, urp, urp), Is.Null, "nothing to switch");
            Assert.That(RenderPipelineProfiles.PlayerExecutable(null, urp, hdrp), Is.Null);

            string setup = File.ReadAllText(Path.Combine(UnitySpace, "Editor", "ProjectSetup.cs"));
            Assert.That(setup, Does.Contain("BuildWindowsHdrp()"), "the batch build makes the HDRP player too");
            Assert.That(setup, Does.Contain("RenderPipelineProfiles.HighDefinition.PlayerFolder"), "where the restart looks for it");
        }

        [Test]
        public void AnUnknownOrMissingPipelineAssetMatchesNoProfile()
        {
            Assert.That(RenderPipelineProfiles.ForAsset(null), Is.Null);
            Assert.That(RenderPipelineProfiles.ForAsset(""), Is.Null);
            Assert.That(RenderPipelineProfiles.ForAsset("SomeOtherPipelineAsset"), Is.Null);
        }

        [Test]
        public void ASecondPipelineIsOneRegistration()
        {
            var test = new RenderPipelineProfile("test-t429", "Test pipeline", "TestPipelineAssetT429", "TestPipeline", "Shaders/Test/",
                honoursCameraHdr: false, honoursCameraMsaa: false, renderFeatures: new[] { "Sky" });
            RenderPipelineProfiles.Register(test);
            Assert.That(RenderPipelineProfiles.ForAsset("TestPipelineAssetT429"), Is.SameAs(test));
            Assert.That(test.ShaderPath(RenderShaders.Ship), Is.EqualTo("Shaders/Test/Ship"));

            var replacement = new RenderPipelineProfile("test-t429", "Test pipeline 2", "TestPipelineAssetT429", "TestPipeline", "Shaders/Test");
            RenderPipelineProfiles.Register(replacement);
            Assert.That(RenderPipelineProfiles.Find("test-t429"), Is.SameAs(replacement));
            Assert.That(RenderPipelineProfiles.All.Count(p => p.Id == "test-t429"), Is.EqualTo(1));
            Assert.That(RenderPipelineProfiles.Find("urp"), Is.SameAs(RenderPipelineProfiles.Universal));
        }

        [Test]
        public void EveryShaderHasAVersionForEveryPipelineThatIncludesOnlyThePipelineShim()
        {
            foreach (var profile in new[] { RenderPipelineProfiles.Universal, RenderPipelineProfiles.HighDefinition })
            foreach (var name in RenderShaders.All)
            {
                string path = Path.Combine(UnitySpace, "Resources", profile.ShaderFolder, name + ".shader");
                Assert.That(File.Exists(path), Is.True, "missing " + Rel(path));
                string text = File.ReadAllText(path);
                Assert.That(text, Does.Contain("\"RenderPipeline\" = \"" + profile.ShaderTag + "\""), name + " has no " + profile.DisplayName + " SubShader");
                Assert.That(text, Does.Contain("#include \"PipelineCore.hlsl\""), name);
                Assert.That(text, Does.Contain("#include \"Passes/" + name + ".hlsl\""), name + " keeps its pass in Passes/" + name + ".hlsl");
                Assert.That(text, Does.Not.Contain("Packages/com.unity.render-pipelines"), name + " names a pipeline package; include PipelineCore.hlsl");
            }
            var shaders = Directory.GetFiles(ShaderRoot, "*.shader").Select(Path.GetFileNameWithoutExtension);
            Assert.That(shaders, Is.EquivalentTo(RenderShaders.All), "every shader is listed in RenderShaders");
        }

        [Test]
        public void EachShadersHdrpSubShaderPicksTheHdrpLibraryAndDrawsInHdrpsForwardPass()
        {
            foreach (var name in RenderShaders.All)
            {
                string text = File.ReadAllText(Path.Combine(ShaderRoot, name + ".shader"));
                int hdrp = text.IndexOf("\"RenderPipeline\" = \"HDRenderPipeline\"", StringComparison.Ordinal);
                Assert.That(hdrp, Is.GreaterThan(0), name);
                string sub = text.Substring(hdrp);
                Assert.That(sub, Does.Contain("\"LightMode\" = \"ForwardOnly\""), name);
                int define = sub.IndexOf("#define UNITYSPACE_PIPELINE_HDRP", StringComparison.Ordinal);
                int include = sub.IndexOf("#include \"PipelineCore.hlsl\"", StringComparison.Ordinal);
                Assert.That(define, Is.GreaterThan(0), name);
                Assert.That(define, Is.LessThan(include), name + ": the define picks the library, so it comes first");
                Assert.That(text.Substring(0, hdrp), Does.Not.Contain("UNITYSPACE_PIPELINE_HDRP"), name + ": the URP SubShader keeps URP's library");
            }
        }

        [Test]
        public void EveryPassIsPipelineNeutralAndDrawsThroughTheExposure()
        {
            foreach (var name in RenderShaders.All)
            {
                string path = Path.Combine(ShaderRoot, "Passes", name + ".hlsl");
                Assert.That(File.Exists(path), Is.True, "missing " + Rel(path));
                string text = File.ReadAllText(path);
                Assert.That(text, Does.Not.Contain("#include"), name + ": a pass takes its includes from the shader");
                Assert.That(text, Does.Not.Contain("GetVertexPositionInputs"), name + " uses a URP-only helper; call the shared core transforms");
                Assert.That(text, Does.Not.Contain("_WorldSpaceCameraPos"), name + ": HDRP renders camera-relative; use GetCameraPositionWS()");
                string frag = text.Substring(text.IndexOf("frag(", StringComparison.Ordinal));
                Assert.That(Regex.Matches(frag, @"\breturn\b").Count, Is.EqualTo(1), name);
                Assert.That(frag, Does.Contain("return PipelineExpose("), name + ": the colour reaches the image through the pipeline's exposure");
            }
            Assert.That(File.ReadAllText(Path.Combine(ShaderRoot, "Passes", RenderShaders.Sun + ".hlsl")), Does.Contain("PipelineSunBrightness()"),
                "the star's face glares above a lit surface's white");
        }

        static readonly (Regex pattern, string what, Func<string, bool> allowed)[] PipelineOnly =
        {
            (new Regex(@"UnityEngine\.Rendering\.Universal|UniversalAdditionalCameraData|UniversalRenderPipeline\b"), "URP",
                p => Under(p, UrpBackend)),
            (new Regex(@"UnityEngine\.Rendering\.HighDefinition|HDAdditionalCameraData|HDAdditionalLightData|(?<!"")HDRenderPipeline\b(?!"")"), "HDRP",
                p => Under(p, HdrpBackend)),
            (new Regex(@"QualitySettings\.SetQualityLevel|GraphicsSettings\.defaultRenderPipeline\s*="), "switching the pipeline",
                p => Under(p, PipelineLayer) || Under(p, Path.Combine(UnitySpace, "Editor"))),
            (new Regex(@"Shader\.(Set|Get)Global"), "shader globals", p => Under(p, PipelineLayer)),
            (new Regex(@"\.allowHDR\b|\.allowMSAA\s*="), "camera HDR/MSAA flags", p => Under(p, PipelineLayer)),
            (new Regex(@"AddComponent<Light>"), "a Unity light", p => Under(p, PipelineLayer)),
            (new Regex(@"Resources\.Load<Shader>"), "shader loading", p => Under(p, PipelineLayer)),
        };

        [Test]
        public void GameCodeReachesThePipelineOnlyThroughTheBackend()
        {
            var offences = Directory.GetFiles(UnitySpace, "*.cs", SearchOption.AllDirectories)
                .SelectMany(file =>
                {
                    string text = File.ReadAllText(file);
                    return PipelineOnly.Where(r => r.pattern.IsMatch(text) && !r.allowed(file)).Select(r => Rel(file) + ": " + r.what);
                })
                .ToList();
            Assert.That(offences, Is.Empty);
        }

        [Test]
        public void OnlyEachBackendsAssemblyReferencesItsPipeline()
        {
            foreach (var asmdef in Directory.GetFiles(UnitySpace, "*.asmdef", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(asmdef);
                Assert.That(text.Contains("Unity.RenderPipelines.Universal"), Is.EqualTo(Under(asmdef, UrpBackend)), Rel(asmdef) + " (URP)");
                Assert.That(text.Contains("Unity.RenderPipelines.HighDefinition"), Is.EqualTo(Under(asmdef, HdrpBackend)), Rel(asmdef) + " (HDRP)");
            }
            foreach (var package in new[] { "com.unity.render-pipelines.universal", "com.unity.render-pipelines.high-definition" })
            {
                var hlsl = Directory.GetFiles(Path.Combine(UnitySpace, "Resources"), "*.hlsl", SearchOption.AllDirectories)
                    .Where(f => File.ReadAllText(f).Contains(package))
                    .Select(Path.GetFileName);
                Assert.That(hlsl, Is.EqualTo(new[] { "PipelineCore.hlsl" }), package);
            }
        }
    }
}
