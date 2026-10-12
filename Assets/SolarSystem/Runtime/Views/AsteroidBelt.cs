using System;
using SolarSystem.Core;
using SolarSystem.Rendering;
using UnityEngine;

namespace SolarSystem.Views
{
    /// <summary>
    /// Draws the ~111,000 real asteroids of <see cref="AsteroidField"/> in one procedural draw call.
    /// The GPU propagates every orbit per frame (Asteroids.shader), so the CPU cost is a handful of
    /// uniforms; only the few named asteroids that get labels are propagated on the CPU.
    /// </summary>
    public sealed class AsteroidBelt : MonoBehaviour
    {
        /// <summary>Asteroids labelled by name (the largest and best known; Ceres is a full body).</summary>
        static readonly (int Number, string Name)[] Named =
        {
            (4, "Vesta"), (2, "Pallas"), (10, "Hygiea"), (3, "Juno"), (704, "Interamnia"), (16, "Psyche"),
            (624, "Hektor"), (588, "Achilles"), (617, "Patroclus"),
        };

        public AsteroidField Field { get; private set; }

        SolarSystemApp app;
        GraphicsBuffer buffer;
        Material material;
        int[] namedIndex;
        GUIStyle labelStyle, shadowStyle;

        public static AsteroidBelt Create(SolarSystemApp app)
        {
            var asset = Resources.Load<TextAsset>("Data/asteroids");
            if (asset == null)
            {
                Debug.LogError("[SolarSystem] Missing Resources/Data/asteroids.bytes");
                return null;
            }
            var belt = app.gameObject.AddComponent<AsteroidBelt>();
            belt.app = app;
            belt.Field = AsteroidField.Parse(asset.bytes);

            var packed = new float[belt.Field.Count * AsteroidField.GpuFloatsPerAsteroid];
            belt.Field.PackGpu(packed);
            belt.buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, belt.Field.Count * 3, 16);
            belt.buffer.SetData(packed);
            belt.material = Materials.Create(Materials.Asteroids, "Asteroids");
            belt.material.SetBuffer("_Asteroids", belt.buffer);

            belt.namedIndex = new int[Named.Length];
            for (int i = 0; i < Named.Length; i++) belt.namedIndex[i] = belt.Field.IndexOfNumber(Named[i].Number);
            return belt;
        }

        /// <summary>Called by the app after the camera and layout are final for the frame.</summary>
        public void Render(double jd, ScaleModel scale, Vector3 sunRelative)
        {
            if (material == null) return;
            AsteroidField.SunMapping(scale, app.Catalog.Sun, out var c0, out var c1);
            material.SetFloat("_DtDays", (float)(jd - Field.EpochJd));
            material.SetVector("_SunMap0", new Vector4(c0[0], c0[1], c0[2], c0[3]));
            material.SetVector("_SunMap1", new Vector4(c1[0], c1[1], c1[2], c1[3]));
            material.SetVector("_SunRelative", sunRelative);
            var rp = new RenderParams(material)
            {
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 2e9f),
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = false,
            };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, Field.Count * 6);
        }

        /// <summary>Scene position of a field asteroid, mapped like the shader does (the Sun's mapping).</summary>
        public DVec3 SceneOf(int index, double jd)
        {
            var helioKm = Field.PositionAu(index, jd) * Astro.AuKm;
            return app.BodyScene(0) + app.Scale.LocalOffset(app.Catalog.Sun, helioKm);
        }

        void OnGUI()
        {
            if (app == null || !app.ShowLabels || !app.ShowHud || Event.current.type != EventType.Repaint || app.Travel.Active || !app.SystemVisible) return;
            labelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = new Color(0.82f, 0.78f, 0.7f, 0.9f) } };
            shadowStyle ??= new GUIStyle(labelStyle) { normal = { textColor = new Color(0, 0, 0, 0.8f) } };
            double jd = app.Clock.Jd;
            var eye = app.SpaceCamera.ScenePosition;
            for (int i = 0; i < Named.Length; i++)
            {
                int k = namedIndex[i];
                if (k < 0) continue;
                var scene = SceneOf(k, jd);
                // Only label an asteroid when it is near the camera or the camera is out viewing the belt,
                // not as clutter behind a planet close-up.
                double fromSun = (scene - app.BodyScene(0)).Length;
                bool overview = (eye - app.BodyScene(0)).Length > fromSun * 0.9;
                if (!overview && (eye - scene).Length > fromSun * 0.25) continue;
                if (!app.ToGui(scene, out var p, out _) || app.Occluded(scene)) continue;
                var r = new Rect(p.x + 5, p.y - 8, 120, 18);
                GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), Named[i].Name, shadowStyle);
                GUI.Label(r, Named[i].Name, labelStyle);
            }
        }

        void OnDestroy()
        {
            buffer?.Release();
            buffer = null;
        }
    }
}
