// The asteroid belt: one screen-facing dot per real asteroid, drawn procedurally (no mesh).
// Each vertex solves Kepler's equation for its asteroid on the GPU from the elements packed by
// AsteroidField.PackGpu (AsteroidField.GpuPosition is the C# mirror the tests check), then maps
// the heliocentric distance through ScaleModel's Sun mapping (AsteroidField.SunMapping), so the
// belt sits correctly at gameplay and realistic scale and while blending between them.
Shader "Unity.SpaceGod/Asteroids"
{
    Properties
    {
        _Color ("Colour", Color) = (0.82, 0.78, 0.72, 1)
        _Brightness ("Brightness", Float) = 1
        _PointScale ("Point Size (px)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
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
            #pragma target 4.5
            #include "PipelineCore.hlsl"
            #include "Passes/Asteroids.hlsl"
            ENDHLSL
        }
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "HDRenderPipeline" "Queue" = "Transparent" }
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
            #include "Passes/Asteroids.hlsl"
            ENDHLSL
        }
    }
}
