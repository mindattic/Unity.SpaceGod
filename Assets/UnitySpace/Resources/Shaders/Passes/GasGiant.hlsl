// The pass of Unity.SpaceGod/GasGiant, shared by its URP and HDRP SubShaders (U.SPC-T430): the shader includes
// PipelineCore.hlsl (and SolarCommon.hlsl where it needs it) first, so this code is the same on every pipeline.
TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    float _PoleMargin;
    float4 _AtmoColor;
    float _AtmoStrength;
    float _Ambient;
    float _Highlight;
    float _Turbulence;
CBUFFER_END

// Set per material from C# every frame (not material properties).
float _Drift[37];     // deg/day eastward drift at latitude -90, -85, ... 90
float4 _Flow;         // phase A (0..1), phase B, loop length (days), amount (0..1)
float4 _Grs;          // centre east longitude (deg), latitude (deg), band shift (deg), enabled
float4 _GrsShape;     // half-width (deg lon), half-height (deg lat), collar rotation period (days)

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float3 positionWS : TEXCOORD2;
};

Varyings vert(Attributes v)
{
    Varyings o;
    float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
    o.positionCS = TransformWorldToHClip(positionWS);
    o.positionWS = positionWS;
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    o.uv = v.uv;
    return o;
}

float DriftAt(float lat)
{
    float f = clamp((lat + 90.0) / 5.0, 0.0, 35.999);
    int k = (int)f;
    return lerp(_Drift[k], _Drift[k + 1], f - k);
}

float WrapDeg(float d) { return d - 360.0 * floor(d / 360.0 + 0.5); }

// Map coordinates (lon, lat in degrees) to sample for display point (lon, lat) after tau days of flow.
float2 Advect(float lon, float lat, float tau, float amount)
{
    // The whole map is turned in longitude so the mapped spot sits at its current System III
    // longitude (sliding only its band would shear the band edges into streaks).
    float baseLon = lon + _Grs.w * _Grs.z;

    // Zonal wind: the cloud now at lon came from upstream.
    float2 zonal = float2(baseLon - DriftAt(lat) * tau * amount, lat);

    // Turbulent eddies, advected with the same clock.
    // Sampled on the sphere (no seam at the date line), squashed north-south like the bands.
    float lonR = radians(lon), latR = radians(lat);
    float3 q = float3(cos(latR) * cos(lonR), sin(latR) * 1.8, cos(latR) * sin(lonR)) * 14.0 + tau * 1.7;
    float2 eddy = float2(SolarValueNoise(q) - 0.5, SolarValueNoise(q + 19.7) - 0.5);
    zonal += eddy * _Turbulence * float2(1.0, 0.5) * amount;

    if (_Grs.w < 0.5) return zonal;

    // Inside the oval: rotate counter-clockwise along elliptical streamlines.
    float2 e = float2(WrapDeg(lon - _Grs.x) / _GrsShape.x, (lat - _Grs.y) / _GrsShape.y);
    float r = length(e);
    if (r > 1.35) return zonal;
    // Angular speed profile: quiet core, fastest in the collar near r = 0.8, fading outside.
    float speed = lerp(0.25, 1.0, smoothstep(0.1, 0.75, r)) * (1.0 - smoothstep(0.95, 1.3, r));
    float angle = -6.2831853 * tau / _GrsShape.z * speed * amount; // sample clockwise = features turn CCW
    float c = cos(angle), s = sin(angle);
    float2 er = float2(e.x * c - e.y * s, e.x * s + e.y * c);
    float2 vortex = float2(_Grs.x + er.x * _GrsShape.x + _Grs.z, _Grs.y + er.y * _GrsShape.y);
    float m = 1.0 - smoothstep(1.0, 1.35, r);
    return lerp(zonal, vortex, m);
}

// Explicit gradients from the mesh UVs: the advected coordinates jump where they wrap in
// longitude, which would otherwise pick the smallest mip along a seam.
float3 SampleMap(float2 lonLat, float2 dx, float2 dy)
{
    float2 uv = float2(lonLat.x / 360.0 + 0.5, lonLat.y / 180.0 + 0.5);
    uv.y = clamp(uv.y, _PoleMargin, 1.0 - _PoleMargin);
    return SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, uv, dx, dy).rgb;
}

half4 frag(Varyings i) : SV_Target
{
    float3 n = normalize(i.normalWS);
    float3 l = normalize(_SunPositionWS.xyz - i.positionWS);
    float3 viewDir = normalize(GetCameraPositionWS() - i.positionWS);

    float lon = (i.uv.x - 0.5) * 360.0;
    float lat = (i.uv.y - 0.5) * 180.0;
    float2 dx = ddx(i.uv), dy = ddy(i.uv);

    float phaseA = _Flow.x, phaseB = _Flow.y, loopDays = _Flow.z, amount = _Flow.w;
    float3 a = SampleMap(Advect(lon, lat, phaseA * loopDays, amount), dx, dy);
    float3 b = SampleMap(Advect(lon, lat, phaseB * loopDays, amount), dx, dy);
    float wa = 1.0 - abs(2.0 * phaseA - 1.0);
    float3 albedo = lerp(b, a, wa);

    float ndl = dot(n, l);
    float lit = saturate(ndl * 1.05 + 0.03) * smoothstep(-0.12, 0.1, ndl);
    float3 l2 = normalize(_Sun2PositionWS.xyz - i.positionWS);
    float ndl2 = dot(n, l2);
    float lit2 = saturate(ndl2 * 1.05) * smoothstep(-0.12, 0.1, ndl2);
    // Mild limb darkening of a deep atmosphere.
    float mu = saturate(dot(n, viewDir));
    float limb = lerp(0.72, 1.0, sqrt(mu));
    float3 col = albedo * ((lit * _SunColor.rgb + lit2 * _Sun2Color.rgb) * limb + _Ambient);

    float rim = pow(1.0 - mu, 3.0);
    float3 daySide = smoothstep(-0.35, 0.45, ndl) * _SunColor.rgb + smoothstep(-0.35, 0.45, ndl2) * _Sun2Color.rgb;
    col += _AtmoColor.rgb * rim * _AtmoStrength * daySide;
    col += float3(0.35, 0.7, 1.0) * rim * _Highlight * 0.35;
    return PipelineExpose(half4(col, 1));
}
