// Emissive photosphere with limb darkening and slowly churning granulation.
Shader "Unity.SpaceGod/Sun"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.9, 0.7, 1)
        _Intensity ("Intensity", Float) = 1.15
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "PipelineCore.hlsl"
            #include "SolarCommon.hlsl"
            #include "Passes/Sun.hlsl"
            ENDHLSL
        }
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "HDRenderPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #define UNITYSPACE_PIPELINE_HDRP
            #include "PipelineCore.hlsl"
            #include "SolarCommon.hlsl"
            #include "Passes/Sun.hlsl"
            ENDHLSL
        }
    }
}
