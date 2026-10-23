// The pass of Unity.SpaceGod/Ship, shared by its URP and HDRP SubShaders (U.SPC-T430): the shader includes
// PipelineCore.hlsl (and SolarCommon.hlsl where it needs it) first, so this code is the same on every pipeline.
CBUFFER_START(UnityPerMaterial)
    float4 _Color;
    float4 _Tint;
    float _UseVertexColor;
    float _EngineGlow;
    float4 _EngineColor;
    float _Highlight;
    float4 _Plating_ST;
    float _PlatingScale;
    float _PlatingStrength;
CBUFFER_END

TEXTURE2D(_Plating);
SAMPLER(sampler_Plating);

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 color : COLOR;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 normalWS : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float4 color : TEXCOORD2;
    float3 positionOS : TEXCOORD3;
    float3 normalOS : TEXCOORD4;
};

Varyings vert(Attributes v)
{
    Varyings o;
    float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
    o.positionCS = TransformWorldToHClip(positionWS);
    o.positionWS = positionWS;
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    o.color = lerp(float4(1, 1, 1, 1), v.color, _UseVertexColor);
    o.positionOS = v.positionOS.xyz;
    o.normalOS = v.normalOS;
    return o;
}

half4 frag(Varyings i) : SV_Target
{
    float3 n = normalize(i.normalWS);
    float3 l = normalize(_SunPositionWS.xyz - i.positionWS);
    float3 v = normalize(GetCameraPositionWS() - i.positionWS);
    float3 albedo = _Color.rgb * _Tint.rgb * i.color.rgb;

    // Triplanar plating: blend the three axis projections by the face normal.
    if (_PlatingStrength > 0.0 && i.color.a >= 0.5)
    {
        float3 nO = normalize(i.normalOS);
        float3 w = pow(abs(nO), 4.0);
        w /= (w.x + w.y + w.z);
        float3 p = i.positionOS * _PlatingScale;
        float3 sx = SAMPLE_TEXTURE2D(_Plating, sampler_Plating, p.zy).rgb;
        float3 sy = SAMPLE_TEXTURE2D(_Plating, sampler_Plating, p.xz).rgb;
        float3 sz = SAMPLE_TEXTURE2D(_Plating, sampler_Plating, p.xy).rgb;
        float detail = sx.r * w.x + sy.r * w.y + sz.r * w.z;
        albedo *= lerp(1.0, detail * 2.0, _PlatingStrength);
        float2 tx = sx.bg * 2.0 - 1.0, ty = sy.bg * 2.0 - 1.0, tz = sz.bg * 2.0 - 1.0;
        float3 bumped = nO + (float3(0, tx.y, tx.x) * w.x + float3(ty.x, 0, ty.y) * w.y + float3(tz.x, tz.y, 0) * w.z) * _PlatingStrength;
        n = normalize(TransformObjectToWorldNormal(normalize(bumped)));
    }

    float ndl = saturate(dot(n, l));
    float fill = saturate(dot(n, -l)) * 0.07 + 0.05;
    float spec = pow(saturate(dot(n, normalize(l + v))), 24.0) * 0.35 * step(0.0, dot(n, l));
    float rim = pow(1.0 - saturate(dot(n, v)), 4.0);
    float3 l2 = normalize(_Sun2PositionWS.xyz - i.positionWS);
    float ndl2 = saturate(dot(n, l2));

    float3 col = albedo * (ndl * _SunColor.rgb + ndl2 * _Sun2Color.rgb + fill) + spec * _SunColor.rgb;
    col += float3(0.35, 0.7, 1.0) * rim * _Highlight * 0.6;

    if (i.color.a < 0.5)
        col = albedo * 0.3 + _EngineColor.rgb * _EngineGlow;

    return PipelineExpose(half4(col, 1));
}
