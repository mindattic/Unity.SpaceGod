using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnitySpace.EditorTools
{
    /// <summary>
    /// Builds the main scene and the Windows player. The scene is generated rather than hand-made
    /// so it stays trivially reproducible: it only holds a camera and the UnitySpaceApp object,
    /// and everything else is constructed at runtime from data.
    ///
    /// Batch: Unity -batchmode -quit -projectPath . -executeMethod UnitySpace.EditorTools.ProjectSetup.SetupAndBuild
    ///
    /// The scene is committed and is only written when it is missing (U.SPC-T262): Unity gives every
    /// object a fresh random file ID, so regenerating it on each build left a no-op diff in git after
    /// every deploy. A change to <see cref="CreateScene"/> therefore reaches the player only once the
    /// scene is rebuilt (Solar System > Rebuild Main Scene, or -executeMethod ...ProjectSetup.CreateScene)
    /// and committed together with the code change.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/UnitySpace.unity";
        public const string PlayerPath = "Builds/Windows/UnitySpace.exe";

        static ProjectSetup()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(ScenePath)) CreateScene();
                var active = SceneManager.GetActiveScene();
                if (string.IsNullOrEmpty(active.path) && !active.isDirty && !EditorApplication.isPlayingOrWillChangePlaymode)
                    EditorSceneManager.OpenScene(ScenePath);
            };
        }

        [MenuItem("Solar System/Rebuild Main Scene")]
        public static void CreateScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.farClipPlane = 5e8f;
            cameraGo.AddComponent<AudioListener>();

            new GameObject("Solar System").AddComponent<UnitySpaceApp>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[UnitySpace] Scene written to " + ScenePath);
        }

        [MenuItem("Solar System/Build Windows Player")]
        public static void BuildWindows()
        {
            if (!File.Exists(ScenePath)) CreateScene();
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = PlayerPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("[UnitySpace] Build failed: " + report.summary.result + " (" + report.summary.totalErrors + " errors)");
            Debug.Log("[UnitySpace] Player built: " + PlayerPath + " (" + report.summary.totalSize / (1024 * 1024) + " MB)");
        }

        /// <summary>Batch-mode entry point: build, writing the scene first only if it is missing. Exits non-zero on failure.</summary>
        public static void SetupAndBuild()
        {
            try
            {
                BuildWindows();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }
    }
}
