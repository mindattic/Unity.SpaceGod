// The pass of Unity.SpaceGod/Line, shared by its URP and HDRP SubShaders (U.SPC-T430): the shader includes
// PipelineCore.hlsl (and SolarCommon.hlsl where it needs it) first, so this code is the same on every pipeline.
CBUFFER_START(UnityPerMaterial)
    float4 _Color;
CBUFFER_END

struct Attributes
{
    float4 positionOS : POSITION;
    float4 color : COLOR;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float4 color : TEXCOORD0;
};

Varyings vert(Attributes v)
{
    Varyings o;
    o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
    o.color = v.color * _Color;
    return o;
}

half4 frag(Varyings i) : SV_Target
{
    return PipelineExpose(i.color);
}
