// The pass of Unity.SpaceGod/Asteroids, shared by its URP and HDRP SubShaders (U.SPC-T430): the shader includes
// PipelineCore.hlsl (and SolarCommon.hlsl where it needs it) first, so this code is the same on every pipeline.
CBUFFER_START(UnityPerMaterial)
    float4 _Color;
    float _Brightness;
    float _PointScale;
CBUFFER_END

StructuredBuffer<float4> _Asteroids; // 3 per asteroid: (e, n, M0, H), (a·P, 0), (b·Q, 0)
float _DtDays;                        // simulation JD minus the elements' epoch
float4 _SunMap0;                      // blend, units/AU (realistic), Sun radius (gameplay), units/AU^p (gameplay)
float4 _SunMap1;                      // p, (R_sun/AU)^p
float3 _SunRelative;                  // camera-relative scene position of the Sun

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 corner : TEXCOORD0;
    float brightness : TEXCOORD1;
};

static const float2 Corners[6] = { float2(-1, -1), float2(1, -1), float2(1, 1), float2(-1, -1), float2(1, 1), float2(-1, 1) };

Varyings vert(uint vid : SV_VertexID)
{
    Varyings o;
    uint id = vid / 6;
    float2 corner = Corners[vid % 6];
    float4 k = _Asteroids[id * 3];
    float3 aP = _Asteroids[id * 3 + 1].xyz;
    float3 bQ = _Asteroids[id * 3 + 2].xyz;

    float e = k.x;
    float m = k.z + k.y * _DtDays;
    m -= 6.2831853 * floor(m / 6.2831853 + 0.5);
    float ecc = m + e * sin(m);
    [unroll] for (int it = 0; it < 5; it++)
        ecc -= (ecc - e * sin(ecc) - m) / (1.0 - e * cos(ecc));
    float3 au = aP * (cos(ecc) - e) + bQ * sin(ecc); // heliocentric, ecliptic J2000

    float d = max(length(au), 1e-4);
    float trueScale = d * _SunMap0.y;
    float gameScale = _SunMap0.z + _SunMap0.w * (pow(d, _SunMap1.x) - _SunMap1.y);
    float scene = exp(lerp(log(trueScale), log(gameScale), _SunMap0.x));
    float3 world = _SunRelative + float3(au.x, au.z, au.y) * (scene / d); // ecliptic -> Unity axes

    float4 clip = TransformWorldToHClip(world);
    // Brighter (lower H) asteroids are drawn bigger and brighter: 1 px at H 15.5, ~3 px for Vesta.
    float h = k.w;
    float size = lerp(3.2, 1.0, saturate((h - 3.0) / 12.5)) * _PointScale;
    clip.xy += corner * size / _ScreenParams.xy * 2.0 * clip.w;
    o.positionCS = clip;
    o.corner = corner;
    // Faint ones fade most: from inside the belt 100,000 dots would otherwise fill the sky.
    o.brightness = _Brightness * lerp(0.9, 0.16, saturate((h - 5.0) / 10.5));
    // Seen from well inside the belt (Earth, Mars) the dots would cover the whole sky:
    // dim them there, full strength from the belt itself or from outside it.
    float cameraFromSun = length(_SunRelative);
    o.brightness *= lerp(0.2, 1.0, smoothstep(0.5, 1.1, cameraFromSun / scene));
    return o;
}

half4 frag(Varyings i) : SV_Target
{
    float r2 = dot(i.corner, i.corner);
    float a = saturate(1.0 - r2);
    a *= a;
    return PipelineExpose(half4(_Color.rgb * (a * i.brightness), 1));
}
