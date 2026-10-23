// Ship hulls: Sun-lit Lambert plus a little specular and a cool fill so silhouettes read on the
// night side. Vertex colours carry the paint scheme; vertex alpha below 0.5 marks emissive
// panels (engine bells, hangar mouths), scaled by _EngineGlow. Every hull is plated with a seamless
// sci-fi panel texture (ambientCG MetalPlates015A, CC0), projected triplanar in object space since
// the procedural meshes have no UVs: R is the greyscale albedo detail, G/B the normal map's Y/X.
Shader "Unity.SpaceGod/Ship"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 1, 1, 1)
        _Tint ("Per-ship tint (wrecks, scorching)", Color) = (1, 1, 1, 1)
        _UseVertexColor ("Use Vertex Colour", Float) = 1
        _EngineGlow ("Engine Glow", Float) = 0.6
        _EngineColor ("Engine Colour", Color) = (0.55, 0.8, 1, 1)
        _Highlight ("Highlight", Float) = 0
        _Plating ("Plating (R detail, GB normal)", 2D) = "grey" {}
        _PlatingScale ("Plating tiles per unit", Float) = 10
        _PlatingStrength ("Plating strength", Float) = 0.6
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
            #include "Passes/Ship.hlsl"
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
            #include "Passes/Ship.hlsl"
            ENDHLSL
        }
    }
}
