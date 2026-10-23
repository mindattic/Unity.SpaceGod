// The pass of Unity.SpaceGod/Ring, shared by its URP and HDRP SubShaders (U.SPC-T430): the shader includes
// PipelineCore.hlsl (and SolarCommon.hlsl where it needs it) first, so this code is the same on every pipeline.
CBUFFER_START(UnityPerMaterial)
    float4 _Color;
    float _InnerKm;
    float _OuterKm;
    float4 _PlanetPosRadius;
CBUFFER_END

struct Attributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
};

Varyings vert(Attributes v)
{
    Varyings o;
    float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
    o.positionCS = TransformWorldToHClip(positionWS);
    o.positionWS = positionWS;
    o.normalWS = TransformObjectToWorldNormal(float3(0, 1, 0));
    o.uv = v.uv;
    return o;
}

float Opacity(float r)
{
    if (r < 92000.0) return 0.14 + 0.06 * sin(r * 0.0021);
    if (r < 117580.0) return 0.78 + 0.12 * sin(r * 0.0011) + 0.07 * sin(r * 0.0053);
    if (r < 122170.0) return 0.05;
    if (r > 133423.0 && r < 133745.0) return 0.04;
    return 0.55 + 0.1 * sin(r * 0.0031);
}

half4 frag(Varyings i) : SV_Target
{
    float r = lerp(_InnerKm, _OuterKm, i.uv.x);
    float alpha = Opacity(r) * smoothstep(0.0, 0.01, i.uv.x) * smoothstep(1.0, 0.99, i.uv.x);

    float3 l = normalize(_SunPositionWS.xyz - i.positionWS);
    float3 n = normalize(i.normalWS);
    float3 light = 0.3 * _SunColor.rgb + 0.7 * abs(dot(n, l)) * _SunColor.rgb
        + 0.7 * abs(dot(n, normalize(_Sun2PositionWS.xyz - i.positionWS))) * _Sun2Color.rgb;

    float3 oc = i.positionWS - _PlanetPosRadius.xyz;
    float b = dot(oc, l);
    float c = dot(oc, oc) - _PlanetPosRadius.w * _PlanetPosRadius.w;
    float disc = b * b - c;
    if (disc > 0.0 && (-b + sqrt(disc)) > 0.0 && c > 0.0 && b < 0.0) light *= 0.06;

    float3 col = _Color.rgb * (0.85 + 0.25 * sin(r * 0.00042)) * light;
    return PipelineExpose(half4(col, alpha));
}
