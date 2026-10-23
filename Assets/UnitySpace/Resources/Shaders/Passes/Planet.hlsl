// The pass of Unity.SpaceGod/Planet, shared by its URP and HDRP SubShaders (U.SPC-T430): the shader includes
// PipelineCore.hlsl (and SolarCommon.hlsl where it needs it) first, so this code is the same on every pipeline.
TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);
TEXTURE2D(_NormalMap);
SAMPLER(sampler_NormalMap);

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    float4 _NormalMap_ST;
    float4 _Color;
    float _UseTexture;
    float _UseNormalMap;
    float _NormalStrength;
    float _PoleMargin;
    float _NoiseStrength;
    float _NoiseScale;
    float _Haze;
    float4 _HazeColor;
    float _CloudSpin;
    float4 _AtmoColor;
    float _AtmoStrength;
    float _Ambient;
    float _Highlight;
CBUFFER_END

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
    float3 positionOS : TEXCOORD3;
};

Varyings vert(Attributes v)
{
    Varyings o;
    float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
    o.positionCS = TransformWorldToHClip(positionWS);
    o.positionWS = positionWS;
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    o.uv = v.uv;
    o.positionOS = v.positionOS.xyz;
    return o;
}

half4 frag(Varyings i) : SV_Target
{
    float3 n = normalize(i.normalWS);
    float3 l = normalize(_SunPositionWS.xyz - i.positionWS);
    float3 viewDir = normalize(GetCameraPositionWS() - i.positionWS);
    float2 uv = float2(i.uv.x, clamp(i.uv.y, _PoleMargin, 1.0 - _PoleMargin));

    float3 albedo = _Color.rgb;
    if (_UseTexture > 0.5)
    {
        albedo *= SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
    }
    else
    {
        float3 p = normalize(i.positionOS);
        // A cloud deck turns rigidly about the pole relative to the surface.
        float cs = cos(_CloudSpin), sn = sin(_CloudSpin);
        p = float3(p.x * cs - p.z * sn, p.y, p.x * sn + p.z * cs);
        if (_Haze > 0.99)
        {
            // Cloud streaks run along latitude circles: stretch the noise east-west.
            float3 q = float3(p.x, p.y * 4.0, p.z) * _NoiseScale;
            float streaks = SolarFbm(q) * 0.75 + SolarFbm(q * 3.1 + 5.3) * 0.25;
            albedo *= lerp(1.0 - _NoiseStrength, 1.0 + _NoiseStrength, streaks);
        }
        else
        {
            p *= _NoiseScale;
            float broad = SolarFbm(p);
            float fine = SolarFbm(p * 6.0 + 3.7);
            albedo *= lerp(1.0 - _NoiseStrength, 1.0 + _NoiseStrength, broad * 0.7 + fine * 0.3);
        }
    }
    if (_Haze > 0.0 && _Haze <= 0.99)
        albedo = lerp(albedo, _HazeColor.rgb, _Haze);

    // Relief: perturb the shading normal with the elevation-derived map. The terminator
    // is still taken from the smooth sphere so mountains cannot light the night side.
    float3 shadingN = n;
    if (_UseNormalMap > 0.5)
    {
        float3 t = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv).xyz * 2.0 - 1.0;
        t.xy *= _NormalStrength;
        float3 up = normalize(i.positionOS);
        float3 east = cross(up, float3(0, 1, 0));
        east = dot(east, east) > 1e-6 ? normalize(east) : float3(1, 0, 0);
        float3 north = cross(east, up);
        float3 detailOS = normalize(east * t.x + north * t.y + up * max(t.z, 0.05));
        shadingN = normalize(TransformObjectToWorldNormal(detailOS));
    }

    float ndl = dot(n, l);
    float relief = saturate(dot(shadingN, l));
    float lit = saturate(relief * 1.05 + 0.03 * saturate(ndl * 4.0)) * smoothstep(-0.12, 0.1, ndl);
    float3 l2 = normalize(_Sun2PositionWS.xyz - i.positionWS);
    float ndl2 = dot(n, l2);
    float lit2 = saturate(dot(shadingN, l2) * 1.05) * smoothstep(-0.12, 0.1, ndl2);
    float3 col = albedo * (lit * _SunColor.rgb + lit2 * _Sun2Color.rgb + _Ambient);

    float rim = pow(1.0 - saturate(dot(n, viewDir)), 3.0);
    float3 daySide = smoothstep(-0.35, 0.45, ndl) * _SunColor.rgb + smoothstep(-0.35, 0.45, ndl2) * _Sun2Color.rgb;
    col += _AtmoColor.rgb * rim * _AtmoStrength * daySide;
    col += float3(0.35, 0.7, 1.0) * rim * _Highlight * 0.35;

    return PipelineExpose(half4(col, 1));
}
