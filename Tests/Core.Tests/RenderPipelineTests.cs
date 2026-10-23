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
    /// lights, or its shader library).
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
        public void EveryShaderHasAUrpVersionThatIncludesOnlyThePipelineShim()
        {
            var urp = RenderPipelineProfiles.Universal;
            foreach (var name in RenderShaders.All)
            {
                string path = Path.Combine(UnitySpace, "Resources", urp.ShaderFolder, name + ".shader");
                Assert.That(File.Exists(path), Is.True, "missing " + Rel(path));
                string text = File.ReadAllText(path);
                Assert.That(text, Does.Contain("\"RenderPipeline\" = \"" + urp.ShaderTag + "\""), name);
                Assert.That(text, Does.Contain("#include \"PipelineCore.hlsl\""), name);
                Assert.That(text, Does.Not.Contain("Packages/com.unity.render-pipelines"), name + " names a pipeline package; include PipelineCore.hlsl");
                Assert.That(text, Does.Not.Contain("GetVertexPositionInputs"), name + " uses a URP-only helper; call the shared core transforms");
            }
            var shaders = Directory.GetFiles(Path.Combine(UnitySpace, "Resources", urp.ShaderFolder), "*.shader")
                .Select(Path.GetFileNameWithoutExtension);
            Assert.That(shaders, Is.EquivalentTo(RenderShaders.All), "every shader is listed in RenderShaders");
        }

        static readonly (Regex pattern, string what, Func<string, bool> allowed)[] PipelineOnly =
        {
            (new Regex(@"UnityEngine\.Rendering\.Universal|UniversalAdditionalCameraData|UniversalRenderPipeline\b"), "URP",
                p => Under(p, UrpBackend)),
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
        public void OnlyTheUrpBackendAssemblyReferencesUrp()
        {
            foreach (var asmdef in Directory.GetFiles(UnitySpace, "*.asmdef", SearchOption.AllDirectories))
            {
                bool urp = File.ReadAllText(asmdef).Contains("Unity.RenderPipelines.Universal");
                Assert.That(urp, Is.EqualTo(Under(asmdef, UrpBackend)), Rel(asmdef));
            }
            var hlsl = Directory.GetFiles(Path.Combine(UnitySpace, "Resources"), "*.hlsl", SearchOption.AllDirectories)
                .Where(f => File.ReadAllText(f).Contains("com.unity.render-pipelines.universal"))
                .Select(Path.GetFileName);
            Assert.That(hlsl, Is.EqualTo(new[] { "PipelineCore.hlsl" }));
        }
    }
}
