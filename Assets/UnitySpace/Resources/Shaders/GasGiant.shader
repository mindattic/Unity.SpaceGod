// Giant planet cloud decks that move with the planet's real zonal winds.
//
// Each latitude of the colour map drifts in longitude at the measured wind speed relative to the
// planet's rotation frame (_Drift, deg/day, sampled every 5 degrees from GasGiantWeather). Winds
// shear neighbouring latitudes apart, so the drift is applied as a flow map: two copies of the map
// are advected over a short loop (_Flow.z days) half a loop apart and cross-faded, so detail moves
// at the right speed and direction but never smears out over long runs. Small-scale turbulence is
// a noise displacement advected the same way.
//
// Jupiter's Great Red Spot: the whole map is turned so the spot on the 2015 Hubble map lands
// at its current System III longitude (_Grs.z), and inside the oval the map turns counter-clockwise
// (an anticyclone in the southern hemisphere) along elliptical streamlines, fastest in the collar,
// once per _GrsShape.z days. Everything runs on the simulation clock; the C# side fades the flow
// (_Flow.w) when time warp would make a loop shorter than a couple of real seconds.
Shader "Unity.SpaceGod/GasGiant"
{
    Properties
    {
        _MainTex ("Colour Map", 2D) = "white" {}
        _PoleMargin ("Pole Margin", Float) = 0
        _AtmoColor ("Atmosphere Colour", Color) = (0.9, 0.8, 0.65, 1)
        _AtmoStrength ("Atmosphere Strength", Float) = 0.3
        _Ambient ("Ambient", Float) = 0.02
        _Highlight ("Highlight", Float) = 0
        _Turbulence ("Turbulence (deg)", Float) = 0.35
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
            #pragma target 3.5
            #include "PipelineCore.hlsl"
            #include "SolarCommon.hlsl"
            #include "Passes/GasGiant.hlsl"
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
            #include "Passes/GasGiant.hlsl"
            ENDHLSL
        }
    }
}
