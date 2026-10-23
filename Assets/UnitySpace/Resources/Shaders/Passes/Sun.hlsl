// The pass of Unity.SpaceGod/Sun, shared by its URP and HDRP SubShaders (U.SPC-T430): the shader includes
// PipelineCore.hlsl (and SolarCommon.hlsl where it needs it) first, so this code is the same on every pipeline.
CBUFFER_START(UnityPerMaterial)
    float4 _Color;
    float _Intensity;
CBUFFER_END

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 normalWS : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 positionOS : TEXCOORD2;
};

Varyings vert(Attributes v)
{
    Varyings o;
    float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
    o.positionCS = TransformWorldToHClip(positionWS);
    o.positionWS = positionWS;
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    o.positionOS = v.positionOS.xyz;
    return o;
}

half4 frag(Varyings i) : SV_Target
{
    float3 n = normalize(i.normalWS);
    float3 viewDir = normalize(GetCameraPositionWS() - i.positionWS);
    float mu = saturate(dot(n, viewDir));
    float limb = 0.45 + 0.55 * pow(mu, 0.55);
    float3 p = normalize(i.positionOS) * 9.0;
    float granulation = SolarFbm(p + _Time.y * 0.03) * 0.6 + SolarFbm(p * 4.0 - _Time.y * 0.05) * 0.4;
    float3 col = _Color.rgb * _Intensity * limb * (0.82 + 0.36 * granulation);
    col = lerp(col, float3(1.0, 0.55, 0.2), (1.0 - mu) * 0.35);
    return PipelineExpose(half4(col * PipelineSunBrightness(), 1));
}
