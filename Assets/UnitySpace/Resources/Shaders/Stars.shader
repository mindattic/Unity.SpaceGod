// Background starfield: a point cloud on a unit sphere around the camera, pushed to the far
// plane in the vertex shader so it never clips and sits behind everything at any scale.
Shader "Unity.SpaceGod/Stars"
{
    Properties
    {
        _Brightness ("Brightness", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Background" "RenderPipeline" = "UniversalPipeline" "Queue" = "Background" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "PipelineCore.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Brightness;
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
                float3 dirWS = mul((float3x3)UNITY_MATRIX_M, v.positionOS.xyz);
                float4 cs = mul(UNITY_MATRIX_VP, float4(dirWS, 0.0));
                #if UNITY_REVERSED_Z
                    cs.z = cs.w * 1e-7;
                #else
                    cs.z = cs.w * (1.0 - 1e-7);
                #endif
                o.positionCS = cs;
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(i.color.rgb * i.color.a * _Brightness, 1);
            }
            ENDHLSL
        }
    }
}
