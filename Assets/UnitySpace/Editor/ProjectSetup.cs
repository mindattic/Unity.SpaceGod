using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.SpaceGod.UI;

namespace Unity.SpaceGod.EditorTools
{
    /// <summary>
    /// Builds the main scene and the Windows player. The scene is generated rather than hand-made
    /// so it stays trivially reproducible: it only holds a camera and the UnitySpaceApp object,
    /// and everything else is constructed at runtime from data.
    ///
    /// Batch: Unity -batchmode -quit -projectPath . -executeMethod Unity.SpaceGod.EditorTools.ProjectSetup.SetupAndBuild
    ///
    /// The scene is committed and is only written when it is missing (U.SPC-T262): Unity gives every
    /// object a fresh random file ID, so regenerating it on each build left a no-op diff in git after
    /// every deploy. A change to <see cref="CreateScene"/> therefore reaches the player only once the
    /// scene is rebuilt (Unity.SpaceGod > Rebuild Main Scene, or -executeMethod ...ProjectSetup.CreateScene)
    /// and committed together with the code change.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/UnitySpace.unity";
        public const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
        public const string PlayerPath = "Builds/Windows/Unity.SpaceGod.exe";

        static ProjectSetup()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(ScenePath)) CreateScene();
                if (!File.Exists(MenuScenePath)) CreateMainMenuScene();
                var active = SceneManager.GetActiveScene();
                if (string.IsNullOrEmpty(active.path) && !active.isDirty && !EditorApplication.isPlayingOrWillChangePlaymode)
                    EditorSceneManager.OpenScene(ScenePath);
            };
        }

        [MenuItem("Unity.SpaceGod/Rebuild Main Scene")]
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

            new GameObject("Unity.SpaceGod").AddComponent<UnitySpaceApp>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            SetBuildScenes();
            Debug.Log("[Unity.SpaceGod] Scene written to " + ScenePath);
        }

        /// <summary>The main menu scene the player opens on: a camera on the sky and the <see cref="MainMenu"/>.</summary>
        [MenuItem("Unity.SpaceGod/Rebuild Main Menu Scene")]
        public static void CreateMainMenuScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Path.GetDirectoryName(MenuScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.farClipPlane = 5e8f;
            cameraGo.AddComponent<AudioListener>();

            new GameObject("Main Menu").AddComponent<MainMenu>();

            EditorSceneManager.SaveScene(scene, MenuScenePath);
            SetBuildScenes();
            Debug.Log("[Unity.SpaceGod] Scene written to " + MenuScenePath);
        }

        /// <summary>The main menu first (the player opens on it), then the game.</summary>
        static void SetBuildScenes()
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MenuScenePath, true), new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Unity.SpaceGod/Build Windows Player")]
        public static void BuildWindows()
        {
            if (!File.Exists(ScenePath)) CreateScene();
            if (!File.Exists(MenuScenePath)) CreateMainMenuScene();
            var options = new BuildPlayerOptions
            {
                scenes = new[] { MenuScenePath, ScenePath },
                locationPathName = PlayerPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("[Unity.SpaceGod] Build failed: " + report.summary.result + " (" + report.summary.totalErrors + " errors)");
            Debug.Log("[Unity.SpaceGod] Player built: " + PlayerPath + " (" + report.summary.totalSize / (1024 * 1024) + " MB)");
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
