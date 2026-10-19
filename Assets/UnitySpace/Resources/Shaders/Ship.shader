// Ship hulls: Sun-lit Lambert plus a little specular and a cool fill so silhouettes read on the
// night side. Vertex colours carry the paint scheme; vertex alpha below 0.5 marks emissive
// panels (engine bells, hangar mouths), scaled by _EngineGlow. Every hull is plated with a seamless
// sci-fi panel texture (ambientCG MetalPlates015A, CC0), projected triplanar in object space since
// the procedural meshes have no UVs: R is the greyscale albedo detail, G/B the normal map's Y/X.
Shader "UnitySpace/Ship"
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SolarCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _Tint;
                float _UseVertexColor;
                float _EngineGlow;
                float4 _EngineColor;
                float _Highlight;
                float4 _Plating_ST;
                float _PlatingScale;
                float _PlatingStrength;
            CBUFFER_END

            TEXTURE2D(_Plating);
            SAMPLER(sampler_Plating);

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
                float3 positionOS : TEXCOORD3;
                float3 normalOS : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = lerp(float4(1, 1, 1, 1), v.color, _UseVertexColor);
                o.positionOS = v.positionOS.xyz;
                o.normalOS = v.normalOS;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 l = normalize(_SunPositionWS.xyz - i.positionWS);
                float3 v = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 albedo = _Color.rgb * _Tint.rgb * i.color.rgb;

                // Triplanar plating: blend the three axis projections by the face normal.
                if (_PlatingStrength > 0.0 && i.color.a >= 0.5)
                {
                    float3 nO = normalize(i.normalOS);
                    float3 w = pow(abs(nO), 4.0);
                    w /= (w.x + w.y + w.z);
                    float3 p = i.positionOS * _PlatingScale;
                    float3 sx = SAMPLE_TEXTURE2D(_Plating, sampler_Plating, p.zy).rgb;
                    float3 sy = SAMPLE_TEXTURE2D(_Plating, sampler_Plating, p.xz).rgb;
                    float3 sz = SAMPLE_TEXTURE2D(_Plating, sampler_Plating, p.xy).rgb;
                    float detail = sx.r * w.x + sy.r * w.y + sz.r * w.z;
                    albedo *= lerp(1.0, detail * 2.0, _PlatingStrength);
                    float2 tx = sx.bg * 2.0 - 1.0, ty = sy.bg * 2.0 - 1.0, tz = sz.bg * 2.0 - 1.0;
                    float3 bumped = nO + (float3(0, tx.y, tx.x) * w.x + float3(ty.x, 0, ty.y) * w.y + float3(tz.x, tz.y, 0) * w.z) * _PlatingStrength;
                    n = normalize(TransformObjectToWorldNormal(normalize(bumped)));
                }

                float ndl = saturate(dot(n, l));
                float fill = saturate(dot(n, -l)) * 0.07 + 0.05;
                float spec = pow(saturate(dot(n, normalize(l + v))), 24.0) * 0.35 * step(0.0, dot(n, l));
                float rim = pow(1.0 - saturate(dot(n, v)), 4.0);
                float3 l2 = normalize(_Sun2PositionWS.xyz - i.positionWS);
                float ndl2 = saturate(dot(n, l2));

                float3 col = albedo * (ndl * _SunColor.rgb + ndl2 * _Sun2Color.rgb + fill) + spec * _SunColor.rgb;
                col += float3(0.35, 0.7, 1.0) * rim * _Highlight * 0.6;

                if (i.color.a < 0.5)
                    col = albedo * 0.3 + _EngineColor.rgb * _EngineGlow;

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
