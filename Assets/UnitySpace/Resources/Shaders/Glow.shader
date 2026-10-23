// Additive radial glow on a camera-facing quad (the Sun's corona, engine plumes).
Shader "Unity.SpaceGod/Glow"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.8, 0.5, 1)
        _Intensity ("Intensity", Float) = 1
        _Falloff ("Falloff", Float) = 2.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "PipelineCore.hlsl"

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
                return half4(_Color.rgb * i.color.rgb * glow * _Intensity * i.color.a, 1);
            }
            ENDHLSL
        }
    }
}
