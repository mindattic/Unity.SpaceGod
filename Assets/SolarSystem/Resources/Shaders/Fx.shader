// Additive battle effects batched into one dynamic mesh of camera-facing quads: laser beams and
// tracers (soft-edged ribbons), explosions and missile glows (radial), shield flashes (rings).
// TEXCOORD0.xy is the quad UV, TEXCOORD0.z picks the shape, the vertex colour carries tint and fade.
Shader "SolarSystem/Fx"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+20" "IgnoreProjector" = "True" }
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

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float3 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float3 uv : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv.xy * 2.0 - 1.0;
                float shape;
                if (i.uv.z < 0.5)
                {
                    // Radial: a hot core with a soft halo.
                    float d = saturate(1.0 - length(p));
                    shape = d * d + pow(d, 8.0) * 1.5;
                }
                else if (i.uv.z < 1.5)
                {
                    // Beam: bright core across the ribbon, tapered at both ends.
                    float across = saturate(1.0 - abs(p.y));
                    float along = saturate(min(i.uv.x, 1.0 - i.uv.x) * 12.0);
                    shape = (across * across + pow(across, 10.0) * 2.0) * along;
                }
                else
                {
                    // Ring: a thin bright shell (shield flashes).
                    float d = length(p);
                    shape = saturate(1.0 - abs(d - 0.85) * 8.0) * step(d, 1.0) + saturate(1.0 - d) * 0.15;
                }
                return half4(i.color.rgb * shape * i.color.a, 1);
            }
            ENDHLSL
        }
    }
}
