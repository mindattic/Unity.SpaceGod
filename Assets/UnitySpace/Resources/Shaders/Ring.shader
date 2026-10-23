// Saturn's rings: radial opacity profile of the C ring, B ring, Cassini Division and A ring
// (with the Encke Gap), lit from either face, and shadowed by the planet with an analytic
// ray-sphere test toward the Sun.
Shader "Unity.SpaceGod/Ring"
{
    Properties
    {
        _Color ("Colour", Color) = (0.86, 0.78, 0.64, 1)
        _InnerKm ("Inner Radius (km)", Float) = 74658
        _OuterKm ("Outer Radius (km)", Float) = 136775
        _PlanetPosRadius ("Planet Position (xyz) Radius (w)", Vector) = (0, 0, 0, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "PipelineCore.hlsl"
            #include "SolarCommon.hlsl"
            #include "Passes/Ring.hlsl"
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
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #define UNITYSPACE_PIPELINE_HDRP
            #include "PipelineCore.hlsl"
            #include "SolarCommon.hlsl"
            #include "Passes/Ring.hlsl"
            ENDHLSL
        }
    }
}
