// Additive battle effects batched into one dynamic mesh of camera-facing quads: laser beams and
// tracers (soft-edged ribbons), explosions and missile glows (radial), shield flashes (rings) and shield
// bubble segments (a hex lattice with a ripple from the impact).
// TEXCOORD0.xy is the quad UV, TEXCOORD0.z picks the shape, the vertex colour carries tint and fade.
Shader "Unity.SpaceGod/Fx"
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
            #include "PipelineCore.hlsl"
            #include "Passes/Fx.hlsl"
            ENDHLSL
        }
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "HDRenderPipeline" "Queue" = "Transparent+20" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #define UNITYSPACE_PIPELINE_HDRP
            #include "PipelineCore.hlsl"
            #include "Passes/Fx.hlsl"
            ENDHLSL
        }
    }
}
