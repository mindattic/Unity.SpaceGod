using System.Collections.Generic;
using UnitySpace.Core;
using UnitySpace.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnitySpace.Views
{
    /// <summary>
    /// Draws the active system's jump gates (see <see cref="UnitySpace.Core.JumpGate"/>), one for each
    /// link of the gate network: the same docking-ring mesh a world's docking rings use
    /// (<see cref="StructureViews"/>), reused rather than modelling a new structure, fixed at each
    /// gate's position with its axis along the line to the system it leads to so a ship flies straight
    /// through. The one a piloted ship can activate brightens (see <c>UnitySpaceApp.UpdateGateActivation</c>);
    /// one under blockade glows red.
    /// </summary>
    public sealed class GateView
    {
        readonly UnitySpaceApp app;
        readonly Transform parent;
        readonly List<(JumpGate gate, GameObject root, MeshRenderer renderer)> views = new List<(JumpGate, GameObject, MeshRenderer)>();
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        static readonly int EngineGlowId = Shader.PropertyToID("_EngineGlow");
        static readonly int EngineColorId = Shader.PropertyToID("_EngineColor");
        static readonly int ColorId = Shader.PropertyToID("_Tint");
        static readonly int HighlightId = Shader.PropertyToID("_Highlight");

        /// <summary>Drawn this many km across (fiction, the same scale family as a docking ring).</summary>
        public const double DiameterKm = 25;

        static readonly Color IdleColor = new Color(0.6f, 0.45f, 1f);
        static readonly Color ReadyColor = new Color(0.55f, 1f, 0.6f);
        static readonly Color ShutColor = new Color(1f, 0.3f, 0.25f);
        static readonly Color DownColor = new Color(0.35f, 0.32f, 0.3f);

        public GateView(UnitySpaceApp app, Transform parent)
        {
            this.app = app;
            this.parent = parent;
            AddNew();
        }

        /// <summary>A view for every gate of the active system not yet drawn: those it started with, and any finished since (U.SPC-T97).</summary>
        void AddNew()
        {
            var gates = app.ActiveSystem.Gates;
            for (int i = views.Count; i < gates.Count; i++)
            {
                var gate = gates[i];
                var root = new GameObject(gate.Name);
                root.transform.SetParent(parent, false);
                root.AddComponent<MeshFilter>().sharedMesh = ShipMeshFactory.GetDockingRing(null);
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = ShipModels.ProceduralMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                views.Add((gate, root, renderer));
            }
        }

        public void Update(bool visible, JumpGate readyToActivate)
        {
            if (views.Count < app.ActiveSystem.Gates.Count) AddNew();
            double jd = app.Clock.Jd;
            foreach (var (gate, root, renderer) in views)
            {
                renderer.enabled = visible;
                if (!visible) continue;

                var scenePos = app.Scale.MapNear(app.Ephemeris, app.BodyScenes, app.Catalog.Root.Index, gate.PositionKm);
                var size = app.Scale.ShipLength(DiameterKm);
                var t = root.transform;
                t.localPosition = app.Relative(scenePos);
                t.localScale = Vector3.one * (float)size;

                // A ship passes straight through along the real line to the paired star.
                var dir = gate.PositionKm.Normalized.SwapYZ;
                var forward = new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);
                var up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
                t.localRotation = Quaternion.LookRotation(forward, up);

                bool ready = gate == readyToActivate;
                bool shut = gate.Blockaded(jd) || (gate.Partner != null && gate.Partner.Blockaded(jd));
                // A ring out of action (wrecked in a war, or its far end) is dark until it is rebuilt.
                bool down = gate.Link == null || !gate.Link.Working;
                var color = ready ? ReadyColor : down ? DownColor : shut ? ShutColor : IdleColor;
                block.SetFloat(EngineGlowId, ready ? 3f : down ? 0.2f : 1.3f + 0.5f * Mathf.Sin(Time.unscaledTime * 2f));
                block.SetColor(EngineColorId, color);
                block.SetColor(ColorId, Color.white);
                block.SetFloat(HighlightId, 0f);
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
