using System.Collections.Generic;
using SolarSystem.Core;
using SolarSystem.Rendering;
using UnityEngine;

namespace SolarSystem.Views
{
    /// <summary>The 3D model of one ship, with an engine plume that lights while the drive burns.</summary>
    public sealed class ShipView
    {
        public readonly Ship Ship;
        public readonly GameObject Root;
        readonly List<Renderer> renderers;
        readonly MeshRenderer plume;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        bool visible = true;

        static Mesh plumeMesh;
        static Material plumeMaterial;

        static readonly int EngineGlowId = Shader.PropertyToID("_EngineGlow");
        static readonly int HighlightId = Shader.PropertyToID("_Highlight");

        public ShipView(Ship ship, Transform parent)
        {
            Ship = ship;
            Root = new GameObject(ship.Name);
            Root.transform.SetParent(parent, false);
            ShipModels.Build(ship, Root.transform, out renderers);

            plumeMesh ??= MeshFactory.Plume();
            if (plumeMaterial == null)
            {
                plumeMaterial = Materials.Create(Materials.Glow, "Plume");
                plumeMaterial.SetColor("_Color", new Color(0.45f, 0.75f, 1f));
                plumeMaterial.SetFloat("_Intensity", 1.4f);
                plumeMaterial.SetFloat("_Falloff", 1.6f);
            }
            var plumeGo = new GameObject("Plume");
            plumeGo.transform.SetParent(Root.transform, false);
            plumeGo.transform.localPosition = new Vector3(0, 0, -0.5f);
            plumeGo.transform.localScale = new Vector3(0.12f, 0.12f, 0.6f);
            plumeGo.AddComponent<MeshFilter>().sharedMesh = plumeMesh;
            plume = plumeGo.AddComponent<MeshRenderer>();
            plume.sharedMaterial = plumeMaterial;
            plume.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public void Apply(Vector3 relative, Quaternion rotation, float length, bool burning, bool highlighted)
        {
            var t = Root.transform;
            t.localPosition = relative;
            t.localRotation = rotation;
            t.localScale = Vector3.one * length;

            block.SetFloat(EngineGlowId, burning ? 2.2f : 0.35f);
            block.SetFloat(HighlightId, highlighted ? 1f : 0f);
            foreach (var r in renderers) r.SetPropertyBlock(block);
            plume.enabled = visible && burning;
        }

        public void SetVisible(bool value)
        {
            if (visible == value) return;
            visible = value;
            foreach (var r in renderers) r.enabled = value;
            if (!value) plume.enabled = false;
        }
    }
}
