using SolarSystem.Core;
using SolarSystem.Rendering;
using UnityEngine;

namespace SolarSystem.Views
{
    /// <summary>The visible sphere (plus rings or corona) for one body. Positioned camera-relative every frame.</summary>
    public sealed class BodyView
    {
        public readonly BodyDefinition Body;
        public readonly GameObject Root;
        readonly MeshRenderer renderer;
        readonly Material material;
        readonly Transform ring;
        readonly Material ringMaterial;
        readonly Transform glow;

        static Mesh sphereMesh;
        static Mesh quadMesh;

        public BodyView(BodyDefinition body, Transform parent)
        {
            Body = body;
            Root = new GameObject(body.Name);
            Root.transform.SetParent(parent, false);

            sphereMesh ??= MeshFactory.UvSphere();
            quadMesh ??= MeshFactory.Quad();

            Root.AddComponent<MeshFilter>().sharedMesh = sphereMesh;
            renderer = Root.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var tint = new Color(body.Color.R, body.Color.G, body.Color.B);
            if (body.IsStar)
            {
                material = Materials.Create(Materials.Sun, "Sun");
                material.SetColor("_Color", new Color(1f, 0.9f, 0.72f));

                var glowGo = new GameObject("Corona");
                glowGo.transform.SetParent(parent, false);
                glowGo.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                var glowRenderer = glowGo.AddComponent<MeshRenderer>();
                var glowMaterial = Materials.Create(Materials.Glow, "Corona");
                glowMaterial.SetColor("_Color", new Color(1f, 0.72f, 0.4f));
                glowMaterial.SetFloat("_Intensity", 1.1f);
                glowMaterial.SetFloat("_Falloff", 2.6f);
                glowRenderer.sharedMaterial = glowMaterial;
                glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                glow = glowGo.transform;
            }
            else
            {
                material = Materials.Create(Materials.Planet, body.Name);
                var texture = body.Texture != null ? Resources.Load<Texture2D>("Textures/" + body.Texture) : null;
                if (texture != null)
                {
                    material.SetTexture("_MainTex", texture);
                    material.SetFloat("_UseTexture", 1);
                    material.SetFloat("_PoleMargin", body.TexturePoleMargin);
                    material.SetColor("_Color", Color.white);
                }
                else
                {
                    material.SetFloat("_UseTexture", 0);
                    material.SetColor("_Color", tint);
                    material.SetFloat("_NoiseStrength", body.Kind == BodyKind.Planet ? 0.12f : 0.32f);
                    material.SetFloat("_NoiseScale", body.Kind == BodyKind.Planet ? 2.2f : 3.5f);
                }
                material.SetColor("_AtmoColor", new Color(body.AtmosphereColor.R, body.AtmosphereColor.G, body.AtmosphereColor.B));
                material.SetFloat("_AtmoStrength", body.AtmosphereStrength);
            }
            renderer.sharedMaterial = material;

            if (body.HasRings)
            {
                var ringGo = new GameObject(body.Name + " Rings");
                ringGo.transform.SetParent(Root.transform, false);
                float inner = (float)(body.RingInnerKm / body.RadiusKm);
                float outer = (float)(body.RingOuterKm / body.RadiusKm);
                ringGo.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Annulus(inner, outer);
                var rr = ringGo.AddComponent<MeshRenderer>();
                ringMaterial = Materials.Create(Materials.Ring, body.Name + " Rings");
                ringMaterial.SetFloat("_InnerKm", (float)body.RingInnerKm);
                ringMaterial.SetFloat("_OuterKm", (float)body.RingOuterKm);
                rr.sharedMaterial = ringMaterial;
                rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ring = ringGo.transform;
            }
        }

        /// <summary>
        /// Places the body. <paramref name="relative"/> is scene position minus camera position;
        /// <paramref name="spinDeg"/> is the prime-meridian angle (prograde positive).
        /// </summary>
        public void Apply(Vector3 relative, float radius, float spinDeg, bool highlighted, Vector3 cameraForward)
        {
            var t = Root.transform;
            t.localPosition = relative;
            t.localScale = Vector3.one * radius;
            var pole = ToUnity(Body.Pole);
            // Ecliptic→Unity is a reflection, which reverses rotation sense: prograde is a negative Unity angle.
            t.localRotation = Quaternion.AngleAxis(-spinDeg, pole) * Quaternion.FromToRotation(Vector3.up, pole);
            if (ring != null)
            {
                // Rings lie in the equator and must not spin with the surface.
                ring.rotation = Quaternion.FromToRotation(Vector3.up, pole);
                ringMaterial.SetVector("_PlanetPosRadius", new Vector4(relative.x, relative.y, relative.z, radius));
            }
            if (!Body.IsStar) material.SetFloat("_Highlight", highlighted ? 1f : 0f);

            if (glow != null)
            {
                float distance = relative.magnitude;
                // Corona: at least ~4% of the view so the Sun reads as a star from anywhere in the system.
                float size = Mathf.Max(radius * 7f, distance * 0.07f);
                var toCamera = distance > 0 ? -relative / distance : -cameraForward;
                glow.localPosition = relative + toCamera * Mathf.Min(radius * 1.05f, distance * 0.5f);
                glow.localRotation = Quaternion.LookRotation(-toCamera, Vector3.up);
                glow.localScale = Vector3.one * size;
            }
        }

        public void SetVisible(bool visible)
        {
            renderer.enabled = visible;
        }

        public static Vector3 ToUnity(DVec3 ecliptic) => new Vector3((float)ecliptic.X, (float)ecliptic.Z, (float)ecliptic.Y);
    }
}
