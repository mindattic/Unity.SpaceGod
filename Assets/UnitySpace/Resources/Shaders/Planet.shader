// Lit by the Sun's position (not a URP light): Lambert with a softened terminator, a faint
// ambient so night sides read, an optional relief normal map derived from real elevation data,
// procedural surface variation for bodies with no colour map, an opaque haze or cloud layer
// (Titan, Venus), and an atmosphere rim on the day side.
Shader "Unity.SpaceGod/Planet"
{
    Properties
    {
        _MainTex ("Colour Map", 2D) = "white" {}
        _NormalMap ("Relief (tangent-space normals: east, north, up)", 2D) = "bump" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _UseTexture ("Use Texture", Float) = 0
        _UseNormalMap ("Use Normal Map", Float) = 0
        _NormalStrength ("Normal Strength", Float) = 1
        _PoleMargin ("Pole Margin", Float) = 0
        _NoiseStrength ("Noise Strength", Float) = 0.3
        _NoiseScale ("Noise Scale", Float) = 3
        _Haze ("Haze Opacity", Float) = 0
        _HazeColor ("Haze Colour", Color) = (1, 1, 1, 1)
        _CloudSpin ("Cloud Deck Angle (rad, about the pole)", Float) = 0
        _AtmoColor ("Atmosphere Colour", Color) = (0.4, 0.6, 1, 1)
        _AtmoStrength ("Atmosphere Strength", Float) = 0
        _Ambient ("Ambient", Float) = 0.02
        _Highlight ("Highlight", Float) = 0
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
            #include "Passes/Planet.hlsl"
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
            #include "Passes/Planet.hlsl"
            ENDHLSL
        }
    }
}
