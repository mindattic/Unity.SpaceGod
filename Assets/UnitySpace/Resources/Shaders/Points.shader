// Pixel-sized points at real positions (satellite constellations): depth-tested against the
// planets so a world hides the satellites behind it, added over whatever is behind them.
Shader "Unity.SpaceGod/Points"
{
    Properties
    {
        _Brightness ("Brightness", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "PipelineCore.hlsl"
            #include "Passes/Points.hlsl"
            ENDHLSL
        }
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "HDRenderPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #define UNITYSPACE_PIPELINE_HDRP
            #include "PipelineCore.hlsl"
            #include "Passes/Points.hlsl"
            ENDHLSL
        }
    }
}
