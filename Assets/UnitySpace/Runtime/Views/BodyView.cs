using System;
using Unity.SpaceGod.Core;
using Unity.SpaceGod.Rendering;
using UnityEngine;

namespace Unity.SpaceGod.Views
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
        readonly Material glowMaterial;

        /// <summary>Scales how large a star's corona is kept on screen from afar (1 normally): arriving from between the
        /// stars it starts no bigger than the star point it takes over from and grows to full as the camera falls in.</summary>
        public static float CoronaPresence = 1f;

        /// <summary>A free-flying station: drawn as a model with the ship shader, not a sphere.</summary>
        readonly bool station;

        // A flare star brightens suddenly and fades over seconds (Proxima Centauri).
        readonly bool flareStar;
        float flare;
        float nextFlareAt;

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

            station = body.Kind == BodyKind.Station;
            Root.AddComponent<MeshFilter>().sharedMesh = station ? ShipMeshFactory.GetStationBody(body.Name) : sphereMesh;
            renderer = Root.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var tint = new Color(body.Color.R, body.Color.G, body.Color.B);
            if (!body.IsVisible)
            {
                // A barycentre: an empty point in space.
                material = Materials.Create(Materials.Planet, body.Name);
                renderer.sharedMaterial = material;
                renderer.enabled = false;
                return;
            }
            if (station)
            {
                material = ShipModels.ProceduralMaterial;
                renderer.sharedMaterial = material;
                return;
            }
            if (body.IsStar && body.Singularity)
            {
                // A Void Base's generator (U.SPC-T372): dark and light-swallowing, not lit like an ordinary star --
                // its own Color stands as-is, skipping the usual blackbody tint, and a tight violet glow stands in
                // for an event horizon instead of a corona that would otherwise balloon with distance (see Apply).
                material = Materials.Create(Materials.Sun, body.Name);
                material.SetColor("_Color", new Color(body.Color.R, body.Color.G, body.Color.B));

                var horizonGo = new GameObject("Event Horizon");
                horizonGo.transform.SetParent(parent, false);
                horizonGo.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                var horizonRenderer = horizonGo.AddComponent<MeshRenderer>();
                glowMaterial = Materials.Create(Materials.Glow, "Event Horizon");
                glowMaterial.SetColor("_Color", new Color(0.55f, 0.15f, 0.85f));
                glowMaterial.SetFloat("_Intensity", 0.7f);
                glowMaterial.SetFloat("_Falloff", 4.5f);
                horizonRenderer.sharedMaterial = glowMaterial;
                horizonRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                glow = horizonGo.transform;
            }
            else if (body.IsStar)
            {
                // Tinted by photosphere temperature relative to the Sun's, so the Sun keeps its look.
                var light = UnitySpaceApp.LightColor(body, 1);
                var t = new Color(light.x, light.y, light.z);
                // Cool red dwarfs flare; brown dwarfs, cooler still, do not.
                flareStar = body.TeffK < 3300 && body.TeffK > 2400;
                nextFlareAt = Time.unscaledTime + 6f + UnityEngine.Random.value * 10f;
                material = Materials.Create(Materials.Sun, body.Name);
                material.SetColor("_Color", new Color(1f, 0.9f, 0.72f) * t);

                var glowGo = new GameObject("Corona");
                glowGo.transform.SetParent(parent, false);
                glowGo.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                var glowRenderer = glowGo.AddComponent<MeshRenderer>();
                glowMaterial = Materials.Create(Materials.Glow, "Corona");
                glowMaterial.SetColor("_Color", new Color(1f, 0.72f, 0.4f) * t);
                glowMaterial.SetFloat("_Intensity", 1.1f);
                glowMaterial.SetFloat("_Falloff", 2.6f);
                glowRenderer.sharedMaterial = glowMaterial;
                glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                glow = glowGo.transform;
            }
            else
            {
                // Its maps load in the background (U.SPC-T414): until they arrive it is drawn in its own colour (a
                // gas giant, whose shader has no tint, on a swatch of it), the way an unmapped body always is.
                bool mapped = body.Texture != null;
                windProfile = body.GasGiant && mapped ? GasGiantWeather.ProfileFor(body.Name) : null;
                material = Materials.Create(windProfile != null ? Materials.GasGiant : Materials.Planet, body.Name);
                material.SetFloat("_UseTexture", 0);
                material.SetColor("_Color", tint);
                bool clouds = body.SurfaceHaze >= 0.99f;
                // A visible-light cloud deck (Venus) is nearly featureless: keep its contrast faint.
                material.SetFloat("_NoiseStrength", clouds ? 0.05f : body.Kind == BodyKind.Planet ? 0.12f : 0.32f);
                material.SetFloat("_NoiseScale", clouds ? 1.6f : body.Kind == BodyKind.Planet ? 2.2f : 3.5f);
                if (windProfile != null) material.SetTexture("_MainTex", AssetStreamer.Swatch(tint));
                if (mapped)
                {
                    var m = material;
                    bool tintTexture = body.TintTexture;
                    float poleMargin = body.TexturePoleMargin;
                    AssetStreamer.Request<Texture2D>(AssetStreamer.TexturePath(body.Texture), texture =>
                    {
                        if (m == null || texture == null) return;
                        m.SetTexture("_MainTex", texture);
                        m.SetFloat("_UseTexture", 1);
                        m.SetFloat("_PoleMargin", poleMargin);
                        m.SetColor("_Color", tintTexture ? tint : Color.white);
                    });
                }
                if (body.NormalMap != null)
                {
                    var m = material;
                    AssetStreamer.Request<Texture2D>(AssetStreamer.TexturePath(body.NormalMap), normalMap =>
                    {
                        if (m == null || normalMap == null) return;
                        m.SetTexture("_NormalMap", normalMap);
                        m.SetFloat("_UseNormalMap", 1);
                        // The maps are baked with relief exaggerated x8 (SOURCES.md); this brings it back toward legible.
                        m.SetFloat("_NormalStrength", 0.6f);
                    });
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
                ringGo.AddComponent<MeshFilter>().sharedMesh = Materials.Own(MeshFactory.Annulus(inner, outer));
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
            if (station)
            {
                // Spine along the station's pole, turning slowly about it.
                var axis = ToUnity(Body.Pole);
                var side = Vector3.Cross(axis, Vector3.up).sqrMagnitude > 1e-6f ? Vector3.Cross(axis, Vector3.up).normalized : Vector3.right;
                t.localRotation = Quaternion.LookRotation(axis, Quaternion.AngleAxis(Time.unscaledTime * 6f, axis) * side);
                return;
            }
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

            if (glow != null && Body.Singularity)
            {
                // Hugs the sphere instead of ballooning with distance like a real corona: an event horizon is
                // part of the body, not something that reads as a star from across a whole system.
                float distance = relative.magnitude;
                var toCamera = distance > 0 ? -relative / distance : -cameraForward;
                glow.localPosition = relative + toCamera * Mathf.Min(radius * 1.05f, distance * 0.5f);
                glow.localRotation = Quaternion.LookRotation(-toCamera, Vector3.up);
                glow.localScale = Vector3.one * (radius * 2.4f);
            }
            else if (glow != null)
            {
                float distance = relative.magnitude;
                // Corona: at least ~4% of the view so the Sun reads as a star from anywhere in the system;
                // dimmer stars (K and M dwarfs) a little less.
                float presence = Mathf.Clamp(Mathf.Pow((float)Body.LuminositySolar, 0.12f), 0.45f, 1.1f);
                float size = Mathf.Max(radius * 7f, distance * 0.07f * presence * CoronaPresence) * (1 + flare * 0.8f);
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
            if (flareStar)
            {
                // Proxima is a flare star: every so often it brightens several-fold in a second and fades.
                float now = Time.unscaledTime;
                if (now >= nextFlareAt)
                {
                    flare = 0.6f + UnityEngine.Random.value * 1.4f;
                    nextFlareAt = now + 14f + UnityEngine.Random.value * 26f;
                }
                flare = Mathf.Max(0, flare - flare * Time.unscaledDeltaTime * 0.9f - Time.unscaledDeltaTime * 0.02f);
                material.SetFloat("_Intensity", 1.15f * (1 + flare));
                glowMaterial.SetFloat("_Intensity", 1.1f * (1 + flare * 1.5f));
            }
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
            else if (!Body.IsStar && !station && Body.SurfaceHaze >= 0.99f)
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
            renderer.enabled = visible && Body.IsVisible;
        }

        public static Vector3 ToUnity(DVec3 ecliptic) => new Vector3((float)ecliptic.X, (float)ecliptic.Z, (float)ecliptic.Y);
    }
}
