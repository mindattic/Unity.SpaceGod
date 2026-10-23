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
            #include "Passes/Stars.hlsl"
            ENDHLSL
        }
    }
    SubShader
    {
        Tags { "RenderType" = "Background" "RenderPipeline" = "HDRenderPipeline" "Queue" = "Background" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #define UNITYSPACE_PIPELINE_HDRP
            #include "PipelineCore.hlsl"
            #include "Passes/Stars.hlsl"
            ENDHLSL
        }
    }
}
