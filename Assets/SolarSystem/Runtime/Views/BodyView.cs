using System;
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

        // Gas giant weather (GasGiant.shader).
        readonly double[,] windProfile;
        readonly float[] drift = new float[37];
        readonly float flowLoopDays;
        readonly bool hasGreatRedSpot;

        /// <summary>Venus's clouds super-rotate in about 4 days, retrograde like the planet (Mariner 10, Venus Express).</summary>
        const double VenusCloudPeriodDays = 4.0;

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
                var texture = body.Texture != null ? Resources.Load<Texture2D>("Textures/" + body.Texture) : null;
                windProfile = body.GasGiant && texture != null ? GasGiantWeather.ProfileFor(body.Name) : null;
                material = Materials.Create(windProfile != null ? Materials.GasGiant : Materials.Planet, body.Name);
                if (texture != null)
                {
                    material.SetTexture("_MainTex", texture);
                    material.SetFloat("_UseTexture", 1);
                    material.SetFloat("_PoleMargin", body.TexturePoleMargin);
                    material.SetColor("_Color", body.TintTexture ? tint : Color.white);
                }
                else
                {
                    material.SetFloat("_UseTexture", 0);
                    material.SetColor("_Color", tint);
                    bool clouds = body.SurfaceHaze >= 0.99f;
                    // A visible-light cloud deck (Venus) is nearly featureless: keep its contrast faint.
                    material.SetFloat("_NoiseStrength", clouds ? 0.05f : body.Kind == BodyKind.Planet ? 0.12f : 0.32f);
                    material.SetFloat("_NoiseScale", clouds ? 1.6f : body.Kind == BodyKind.Planet ? 2.2f : 3.5f);
                }
                var normalMap = body.NormalMap != null ? Resources.Load<Texture2D>("Textures/" + body.NormalMap) : null;
                if (normalMap != null)
                {
                    material.SetTexture("_NormalMap", normalMap);
                    material.SetFloat("_UseNormalMap", 1);
                    // The maps are baked with relief exaggerated x8 (SOURCES.md); this brings it back toward legible.
                    material.SetFloat("_NormalStrength", 0.6f);
                }
                material.SetFloat("_Haze", body.SurfaceHaze);
                material.SetColor("_HazeColor", tint);
                material.SetColor("_AtmoColor", new Color(body.AtmosphereColor.R, body.AtmosphereColor.G, body.AtmosphereColor.B));
                material.SetFloat("_AtmoStrength", body.AtmosphereStrength);

                if (windProfile != null)
                {
                    GasGiantWeather.SampleDrift(body, windProfile, drift);
                    material.SetFloatArray("_Drift", drift);
                    float maxDrift = 0;
                    foreach (var d in drift) maxDrift = Mathf.Max(maxDrift, Mathf.Abs(d));
                    // Loop short enough that the fastest jet moves at most ~4 degrees before the cross-fade.
                    flowLoopDays = Mathf.Clamp(4f / Mathf.Max(maxDrift, 1e-3f), 0.02f, 2f);
                    hasGreatRedSpot = body.Name == "Jupiter";
                    material.SetVector("_GrsShape", new Vector4((float)GasGiantWeather.GreatRedSpot.HalfWidthDeg,
                        (float)GasGiantWeather.GreatRedSpot.HalfHeightDeg, (float)GasGiantWeather.GreatRedSpot.RotationPeriodDays, 0));
                }
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
        /// <paramref name="mapNorth"/> and <paramref name="primeMeridian"/> are its body-fixed frame
        /// (ecliptic unit vectors, see <see cref="BodyOrientation"/>).
        /// </summary>
        public void Apply(Vector3 relative, float radius, DVec3 mapNorth, DVec3 primeMeridian, bool highlighted, Vector3 cameraForward)
        {
            var t = Root.transform;
            t.localPosition = relative;
            t.localScale = Vector3.one * radius;
            var pole = ToUnity(Body.Pole);
            // The sphere mesh has north on +Y, longitude 0 on +X and east on +Z. Ecliptic→Unity is a
            // reflection, which turns the right-handed (meridian, north, east) triad into a proper rotation.
            var east = DVec3.Cross(mapNorth, primeMeridian);
            t.localRotation = Quaternion.LookRotation(ToUnity(east), ToUnity(mapNorth));
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

        /// <summary>
        /// Advances moving cloud decks to simulation time <paramref name="jd"/>. <paramref name="simSecondsPerSecond"/>
        /// is the time warp: when a flow loop would pass in under a couple of real seconds the swirl
        /// fades out rather than strobing, while band and spot positions stay exact.
        /// </summary>
        public void UpdateWeather(double jd, double simSecondsPerSecond)
        {
            if (windProfile != null)
            {
                double phase = jd / flowLoopDays;
                phase -= Math.Floor(phase);
                double loopRealSeconds = flowLoopDays * Astro.SecondsPerDay / Math.Max(simSecondsPerSecond, 1e-9);
                float amount = Mathf.Clamp01((float)((loopRealSeconds - 1.5) / 3.0));
                material.SetVector("_Flow", new Vector4((float)phase, (float)((phase + 0.5) % 1.0), flowLoopDays, amount));
                if (hasGreatRedSpot)
                {
                    material.SetVector("_Grs", new Vector4((float)GasGiantWeather.GreatRedSpot.EastLongitudeDeg(jd),
                        (float)GasGiantWeather.GreatRedSpot.LatitudeDeg, (float)GasGiantWeather.GreatRedSpot.MapShiftDeg(jd), 1));
                }
                else
                {
                    material.SetVector("_Grs", Vector4.zero);
                }
            }
            else if (!Body.IsStar && Body.SurfaceHaze >= 0.99f)
            {
                // Cloud deck angle relative to the (slowly turning) surface frame, in the map's east sense.
                double cloudRate = -360.0 / VenusCloudPeriodDays - BodyOrientation.RateDegPerDay(Body);
                double angle = (cloudRate * (jd - Astro.J2000)) % 360.0;
                // The shader samples its noise at (longitude + _CloudSpin), so features move by -_CloudSpin.
                material.SetFloat("_CloudSpin", (float)(-angle * Astro.Deg2Rad));
            }
        }

        public void SetVisible(bool visible)
        {
            renderer.enabled = visible;
        }

        public static Vector3 ToUnity(DVec3 ecliptic) => new Vector3((float)ecliptic.X, (float)ecliptic.Z, (float)ecliptic.Y);
    }
}
