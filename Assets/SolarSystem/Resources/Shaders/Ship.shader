// Ship hulls: Sun-lit Lambert plus a little specular and a cool fill so silhouettes read on the
// night side. Vertex colours carry the paint scheme; vertex alpha below 0.5 marks emissive
// panels (engine bells, hangar mouths), scaled by _EngineGlow.
Shader "SolarSystem/Ship"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _UseVertexColor ("Use Vertex Colour", Float) = 1
        _EngineGlow ("Engine Glow", Float) = 0.6
        _EngineColor ("Engine Colour", Color) = (0.55, 0.8, 1, 1)
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SolarCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _UseVertexColor;
                float _EngineGlow;
                float4 _EngineColor;
                float _Highlight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 color : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = lerp(float4(1, 1, 1, 1), v.color, _UseVertexColor);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 l = normalize(_SunPositionWS.xyz - i.positionWS);
                float3 v = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 albedo = _Color.rgb * i.color.rgb;

                float ndl = saturate(dot(n, l));
                float fill = saturate(dot(n, -l)) * 0.07 + 0.05;
                float spec = pow(saturate(dot(n, normalize(l + v))), 24.0) * 0.35 * step(0.0, dot(n, l));
                float rim = pow(1.0 - saturate(dot(n, v)), 4.0);

                float3 col = albedo * (ndl + fill) + spec;
                col += float3(0.35, 0.7, 1.0) * rim * _Highlight * 0.6;

                if (i.color.a < 0.5)
                    col = albedo * 0.3 + _EngineColor.rgb * _EngineGlow;

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
