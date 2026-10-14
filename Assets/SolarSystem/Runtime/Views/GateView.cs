using SolarSystem.Core;
using SolarSystem.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace SolarSystem.Views
{
    /// <summary>
    /// Draws the active system's jump gate (see <see cref="SolarSystem.Core.JumpGate"/>): the same
    /// docking-ring mesh a world's docking rings use (<see cref="StructureViews"/>), reused rather than
    /// modelling a new structure, fixed at the gate's position with its axis along the line to the
    /// paired system so a ship flies straight through. Brightens when a piloted ship is close enough
    /// to activate it (see <c>SolarSystemApp.UpdateGateActivation</c>).
    /// </summary>
    public sealed class GateView
    {
        readonly SolarSystemApp app;
        readonly GameObject root;
        readonly MeshRenderer renderer;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        static readonly int EngineGlowId = Shader.PropertyToID("_EngineGlow");
        static readonly int EngineColorId = Shader.PropertyToID("_EngineColor");
        static readonly int ColorId = Shader.PropertyToID("_Tint");
        static readonly int HighlightId = Shader.PropertyToID("_Highlight");

        /// <summary>Drawn this many km across (fiction, the same scale family as a docking ring).</summary>
        public const double DiameterKm = 25;

        static readonly Color IdleColor = new Color(0.6f, 0.45f, 1f);
        static readonly Color ReadyColor = new Color(0.55f, 1f, 0.6f);

        /// <summary>This frame's true position (km, relative to the system root), for the HUD prompt.</summary>
        public DVec3 HelioKm { get; private set; }

        public GateView(SolarSystemApp app, Transform parent)
        {
            this.app = app;
            root = new GameObject("Jump Gate");
            root.transform.SetParent(parent, false);
            root.AddComponent<MeshFilter>().sharedMesh = ShipMeshFactory.GetDockingRing(null);
            renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ShipModels.ProceduralMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        public void Update(bool visible, bool readyToActivate)
        {
            var gate = app.ActiveSystem.Gate;
            renderer.enabled = visible && gate != null;
            if (!visible || gate == null) return;

            HelioKm = gate.PositionKm;
            var scenePos = app.Scale.MapNear(app.Ephemeris, app.BodyScenes, app.Catalog.Root.Index, HelioKm);
            var size = app.Scale.ShipLength(DiameterKm);
            var t = root.transform;
            t.localPosition = app.Relative(scenePos);
            t.localScale = Vector3.one * (float)size;

            // A ship passes straight through along the real line to the paired star.
            var dir = gate.PositionKm.Normalized.SwapYZ;
            var forward = new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);
            var up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            t.localRotation = Quaternion.LookRotation(forward, up);

            var color = readyToActivate ? ReadyColor : IdleColor;
            block.SetFloat(EngineGlowId, readyToActivate ? 3f : 1.3f + 0.5f * Mathf.Sin(Time.unscaledTime * 2f));
            block.SetColor(EngineColorId, color);
            block.SetColor(ColorId, Color.white);
            block.SetFloat(HighlightId, 0f);
            renderer.SetPropertyBlock(block);
        }
    }
}
