// The pass of Unity.SpaceGod/Glow, shared by its URP and HDRP SubShaders (U.SPC-T430): the shader includes
// PipelineCore.hlsl (and SolarCommon.hlsl where it needs it) first, so this code is the same on every pipeline.
CBUFFER_START(UnityPerMaterial)
    float4 _Color;
    float _Intensity;
    float _Falloff;
CBUFFER_END

struct Attributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    // Only Unity's own particle system meshes actually populate this (static glow quads
    // (MeshFactory.Quad/Plume) carry no colour stream, which URP fills in as opaque white)
    // -- it's what lets a ParticleSystem's Start Colour / Colour-over-Lifetime modules fade
    // an engine's exhaust particles instead of every particle popping in and out at full
    // brightness.
    float4 color : COLOR;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 color : TEXCOORD1;
};

Varyings vert(Attributes v)
{
    Varyings o;
    o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
    o.uv = v.uv;
    o.color = v.color;
    return o;
}

half4 frag(Varyings i) : SV_Target
{
    float d = length(i.uv * 2.0 - 1.0);
    float a = saturate(1.0 - d);
    float glow = pow(a, _Falloff) + pow(a, 12.0) * 0.6;
    return PipelineExpose(half4(_Color.rgb * i.color.rgb * glow * _Intensity * i.color.a, 1));
}
