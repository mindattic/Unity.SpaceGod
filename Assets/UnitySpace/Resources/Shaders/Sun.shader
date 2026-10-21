// Emissive photosphere with limb darkening and slowly churning granulation.
Shader "Unity.SpaceGod/Sun"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.9, 0.7, 1)
        _Intensity ("Intensity", Float) = 1.15
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
                float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.positionWS);
                float mu = saturate(dot(n, viewDir));
                float limb = 0.45 + 0.55 * pow(mu, 0.55);
                float3 p = normalize(i.positionOS) * 9.0;
                float granulation = SolarFbm(p + _Time.y * 0.03) * 0.6 + SolarFbm(p * 4.0 - _Time.y * 0.05) * 0.4;
                float3 col = _Color.rgb * _Intensity * limb * (0.82 + 0.36 * granulation);
                col = lerp(col, float3(1.0, 0.55, 0.2), (1.0 - mu) * 0.35);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
