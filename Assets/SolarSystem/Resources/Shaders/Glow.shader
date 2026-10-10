// Additive radial glow on a camera-facing quad (the Sun's corona, engine plumes).
Shader "SolarSystem/Glow"
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _Falloff;
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
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float d = length(i.uv * 2.0 - 1.0);
                float a = saturate(1.0 - d);
                float glow = pow(a, _Falloff) + pow(a, 12.0) * 0.6;
                return half4(_Color.rgb * glow * _Intensity, 1);
            }
            ENDHLSL
        }
    }
}
