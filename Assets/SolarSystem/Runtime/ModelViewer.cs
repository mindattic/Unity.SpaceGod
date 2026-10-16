using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace SolarSystem
{
    /// <summary>
    /// A standalone screen for inspecting any loadable model (currently: everything under
    /// Resources/Ships/) in isolation, for visual QA — this is the tool that would have caught the
    /// Kenney-craft plating bug directly instead of by chance during a tour capture. Deliberately kept
    /// independent of <see cref="UI.Hud"/>/<see cref="SolarSystemApp"/> (both have large unrelated
    /// in-flight changes elsewhere right now) rather than wired into the real menu — toggle with the
    /// <c>F9</c> key, or open straight to a model with <c>-modelviewer &lt;name&gt;</c> on the command
    /// line (e.g. scripted into a capture tour stop later). Not a content browser: no search/filter, no
    /// editing, no side-by-side comparison — just "put this one model in front of the camera and let me
    /// look at it."
    /// </summary>
    public sealed class ModelViewer : MonoBehaviour
    {
        const string CliFlag = "-modelviewer";

        /// <summary>Layer exclusive to this viewer's camera, model and light, so camera-relative scene
        /// content (always drawn near the origin, same as this viewer's own content) never shows through.</summary>
        static readonly int ModelViewerLayer = LayerMask.NameToLayer("ModelViewer");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Ensure()
        {
            if (UnityEngine.Object.FindAnyObjectByType<ModelViewer>() != null) return;
            var go = new GameObject("ModelViewer");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<ModelViewer>();
        }

        string[] modelNames = Array.Empty<string>();
        int selectedIndex = -1;
        GameObject instance;
        Camera viewCamera;
        readonly List<Camera> disabledCameras = new List<Camera>();
        bool open;
        static bool isOpen;
        static int closedFrame = -1;

        /// <summary>
        /// The viewer has the keyboard and mouse this frame, so the game underneath must ignore them:
        /// while it is open, and on the frame it closes (the Esc that closes it is not also the game's Esc,
        /// whichever of the two components updates first).
        /// </summary>
        public static bool CapturesInput => isOpen || closedFrame == Time.frameCount;

        float orbitYaw = 30f, orbitPitch = 15f, zoom = 3f;
        Bounds instanceBounds;
        Vector2 scroll;

        GUIStyle panelStyle, titleStyle, buttonStyle, buttonActiveStyle;
        bool stylesReady;

        void Awake()
        {
            modelNames = Resources.LoadAll<GameObject>("Ships").Select(g => g.name).OrderBy(n => n).ToArray();
        }

        void Start()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], CliFlag, StringComparison.OrdinalIgnoreCase)) continue;
                var requested = args[i + 1];
                var index = Array.IndexOf(modelNames, requested);
                if (index >= 0) Open(index);
                else Debug.LogWarning($"[ModelViewer] -modelviewer '{requested}' not found under Resources/Ships");
                break;
            }
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return;

            // F9, not V: V opens the Spawn & Pilot screen (Hud.DrawSpawn).
            if (kb.f9Key.wasPressedThisFrame)
            {
                if (open) Close();
                else Open(selectedIndex >= 0 ? selectedIndex : 0);
            }

            if (!open) return;

            if (kb.escapeKey.wasPressedThisFrame) Close();

            if (mouse != null)
            {
                if (mouse.rightButton.isPressed)
                {
                    var delta = mouse.delta.ReadValue();
                    orbitYaw += delta.x * 0.3f;
                    orbitPitch = Mathf.Clamp(orbitPitch - delta.y * 0.3f, -85f, 85f);
                }
                var scrollY = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scrollY) > 0.01f)
                    zoom = Mathf.Clamp(zoom * Mathf.Pow(0.9f, scrollY * 0.05f), 1.2f, 20f);
            }

            PositionCamera();
        }

        /// <summary>
        /// Disabling the other cameras alone doesn't isolate the view: <see cref="SolarSystemApp"/> renders
        /// camera-relative (its camera is pinned at <see cref="Vector3.zero"/> and every body/ship is placed
        /// at its world position minus the camera's, via <c>SolarSystemApp.Relative</c>), so the live scene's
        /// renderers always sit clustered near the origin -- exactly where this viewer's own model and camera
        /// also sit. <see cref="viewCamera"/>'s culling mask restricts it to <see cref="ModelViewerLayer"/>
        /// (set on the instantiated model and its light in <see cref="SelectModel"/>/<see cref="Open"/>) so it
        /// only ever sees its own content, regardless of what else is positioned nearby.
        /// </summary>
        void LateUpdate()
        {
            if (!open || viewCamera == null) return;
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (cam == viewCamera || !cam.enabled) continue;
                cam.enabled = false;
                if (!disabledCameras.Contains(cam)) disabledCameras.Add(cam);
            }
        }

        void Open(int index)
        {
            if (modelNames.Length == 0) return;
            index = Mathf.Clamp(index, 0, modelNames.Length - 1);

            if (viewCamera == null)
            {
                var camGo = new GameObject("ModelViewerCamera");
                camGo.transform.SetParent(transform, false);
                viewCamera = camGo.AddComponent<Camera>();
                // URP needs its own per-camera data component -- a camera added purely at runtime via
                // AddComponent<Camera>() (unlike one placed in the scene through the Inspector) doesn't
                // get one automatically, and without it URP may not render it correctly at all.
                camGo.AddComponent<UniversalAdditionalCameraData>();
                viewCamera.clearFlags = CameraClearFlags.SolidColor;
                viewCamera.backgroundColor = new Color(0.05f, 0.06f, 0.09f);
                // Highest depth in the scene wins the screen regardless of how the game's own camera is
                // tagged/found -- more robust than hunting for a "MainCamera"-tagged camera to disable.
                viewCamera.depth = 1000;
                // The game's own content is drawn camera-relative (always near the origin, same as this
                // viewer's), so restrict this camera to its own layer rather than relying on disabling
                // other cameras to keep that content out of frame.
                viewCamera.cullingMask = 1 << ModelViewerLayer;
                var light = new GameObject("ModelViewerLight").AddComponent<Light>();
                light.transform.SetParent(camGo.transform, false);
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
                light.intensity = 1.2f;
                light.cullingMask = 1 << ModelViewerLayer;
            }

            disabledCameras.Clear();
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (cam == viewCamera || !cam.enabled) continue;
                cam.enabled = false;
                disabledCameras.Add(cam);
            }
            viewCamera.gameObject.SetActive(true);
            viewCamera.enabled = true;

            SelectModel(index);
            open = isOpen = true;
        }

        void Close()
        {
            open = isOpen = false;
            closedFrame = Time.frameCount;
            if (viewCamera != null) viewCamera.enabled = false;
            foreach (var cam in disabledCameras) if (cam != null) cam.enabled = true;
            disabledCameras.Clear();
            if (instance != null) Destroy(instance);
            instance = null;
        }

        void SelectModel(int index)
        {
            selectedIndex = index;
            if (instance != null) Destroy(instance);

            var prefab = Resources.Load<GameObject>("Ships/" + modelNames[index]);
            instance = Instantiate(prefab, transform);
            instance.transform.localPosition = Vector3.zero;
            SetLayerRecursively(instance, ModelViewerLayer);

            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            instanceBounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one);
            foreach (var r in renderers) instanceBounds.Encapsulate(r.bounds);

            orbitYaw = 30f;
            orbitPitch = 15f;
            zoom = Mathf.Max(instanceBounds.extents.magnitude * 3f, 1.2f);
            PositionCamera();
        }

        static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }

        void PositionCamera()
        {
            if (viewCamera == null) return;
            var rotation = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            var center = instanceBounds.center;
            var radius = Mathf.Max(instanceBounds.extents.magnitude, 0.5f);
            viewCamera.transform.position = center + rotation * Vector3.back * (radius * zoom);
            viewCamera.transform.LookAt(center);
            viewCamera.nearClipPlane = Mathf.Max(radius * 0.01f, 0.001f);
            viewCamera.farClipPlane = Mathf.Max(radius * 100f, 100f);
        }

        void OnGUI()
        {
            if (!open) return;
            EnsureStyles();

            const float panelWidth = 220f;
            GUI.Box(new Rect(12, 12, panelWidth, Screen.height - 24), string.Empty, panelStyle);
            GUI.Label(new Rect(24, 20, panelWidth - 24, 24), "MODEL VIEWER", titleStyle);
            GUI.Label(new Rect(24, 46, panelWidth - 24, 40),
                "Right-drag: orbit\nScroll: zoom\nF9 / Esc: close", buttonStyle);

            scroll = GUI.BeginScrollView(new Rect(12, 92, panelWidth, Screen.height - 92 - 48), scroll,
                new Rect(0, 0, panelWidth - 20, modelNames.Length * 26));
            for (var i = 0; i < modelNames.Length; i++)
            {
                var style = i == selectedIndex ? buttonActiveStyle : buttonStyle;
                if (GUI.Button(new Rect(4, i * 26, panelWidth - 28, 24), modelNames[i], style))
                    SelectModel(i);
            }
            GUI.EndScrollView();

            if (GUI.Button(new Rect(24, Screen.height - 40, panelWidth - 24, 24), "Close  [Esc]", buttonStyle))
                Close();
        }

        void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;
            var panelTex = new Texture2D(1, 1);
            panelTex.SetPixel(0, 0, new Color(0.06f, 0.07f, 0.1f, 0.92f));
            panelTex.Apply();
            panelStyle = new GUIStyle(GUI.skin.box) { normal = { background = panelTex } };
            titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            buttonStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft };
            buttonActiveStyle = new GUIStyle(buttonStyle);
            var activeTex = new Texture2D(1, 1);
            activeTex.SetPixel(0, 0, new Color(0.2f, 0.45f, 0.65f, 1f));
            activeTex.Apply();
            buttonActiveStyle.normal.background = activeTex;
            buttonActiveStyle.normal.textColor = Color.white;
        }
    }
}
