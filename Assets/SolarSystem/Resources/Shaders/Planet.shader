// Lit by the Sun's position (not a URP light): Lambert with a softened terminator, a faint
// ambient so night sides read, procedural surface variation for bodies with no colour map, and
// an atmosphere rim on the day side.
Shader "SolarSystem/Planet"
{
    Properties
    {
        _MainTex ("Colour Map", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _UseTexture ("Use Texture", Float) = 0
        _PoleMargin ("Pole Margin", Float) = 0
        _NoiseStrength ("Noise Strength", Float) = 0.3
        _NoiseScale ("Noise Scale", Float) = 3
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SolarCommon.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _UseTexture;
                float _PoleMargin;
                float _NoiseStrength;
                float _NoiseScale;
                float4 _AtmoColor;
                float _AtmoStrength;
                float _Ambient;
                float _Highlight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 l = normalize(_SunPositionWS.xyz - i.positionWS);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.positionWS);

                float3 albedo = _Color.rgb;
                if (_UseTexture > 0.5)
                {
                    float2 uv = float2(i.uv.x, clamp(i.uv.y, _PoleMargin, 1.0 - _PoleMargin));
                    albedo *= SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                }
                else
                {
                    float3 p = normalize(i.positionOS) * _NoiseScale;
                    float broad = SolarFbm(p);
                    float fine = SolarFbm(p * 6.0 + 3.7);
                    albedo *= lerp(1.0 - _NoiseStrength, 1.0 + _NoiseStrength, broad * 0.7 + fine * 0.3);
                }

                float ndl = dot(n, l);
                float lit = saturate(ndl * 1.05 + 0.03) * smoothstep(-0.12, 0.1, ndl);
                float3 col = albedo * (lit + _Ambient);

                float rim = pow(1.0 - saturate(dot(n, viewDir)), 3.0);
                float daySide = smoothstep(-0.35, 0.45, ndl);
                col += _AtmoColor.rgb * rim * _AtmoStrength * daySide;
                col += float3(0.35, 0.7, 1.0) * rim * _Highlight * 0.35;

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
