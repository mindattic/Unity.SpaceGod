// The pass of Unity.SpaceGod/Fx, shared by its URP and HDRP SubShaders (U.SPC-T430): the shader includes
// PipelineCore.hlsl (and SolarCommon.hlsl where it needs it) first, so this code is the same on every pipeline.
struct Attributes
{
    float4 positionOS : POSITION;
    float4 color : COLOR;
    float3 uv : TEXCOORD0;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float4 color : COLOR;
    float3 uv : TEXCOORD0;
};

Varyings vert(Attributes v)
{
    Varyings o;
    o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
    o.color = v.color;
    o.uv = v.uv;
    return o;
}

half4 frag(Varyings i) : SV_Target
{
    float2 p = i.uv.xy * 2.0 - 1.0;
    float shape;
    if (i.uv.z < 0.5)
    {
        // Radial: a hot core with a soft halo.
        float d = saturate(1.0 - length(p));
        shape = d * d + pow(d, 8.0) * 1.5;
    }
    else if (i.uv.z < 1.5)
    {
        // Beam: bright core across the ribbon, tapered at both ends.
        float across = saturate(1.0 - abs(p.y));
        float along = saturate(min(i.uv.x, 1.0 - i.uv.x) * 12.0);
        shape = (across * across + pow(across, 10.0) * 2.0) * along;
    }
    else if (i.uv.z < 2.5)
    {
        // Ring: a thin bright shell (shield flashes too small on screen for a bubble).
        float d = length(p);
        shape = saturate(1.0 - abs(d - 0.85) * 8.0) * step(d, 1.0) + saturate(1.0 - d) * 0.15;
    }
    else
    {
        // Shield bubble segment (U.SPC-T40): a hex lattice on a patch of the shield shell, centred on
        // the impact, with a ripple running out from it. TEXCOORD0.z - 3 is the flash's age, 0 to 1.
        float phase = saturate(i.uv.z - 3.0);
        float r = length(p);
        float mask = saturate((1.0 - r) * 3.0);
        float2 q = p * 5.0;
        const float2 s = float2(1.0, 1.7320508);
        float2 a = q - s * floor(q / s) - s * 0.5;
        float2 b = (q - s * 0.5) - s * floor((q - s * 0.5) / s) - s * 0.5;
        float2 g = dot(a, a) < dot(b, b) ? a : b;
        float2 ag = abs(g);
        float edge = 0.5 - max(dot(ag, normalize(s)), ag.x);
        float lines = saturate(1.0 - edge * 10.0);
        float front = phase * 1.1;
        float ripple = saturate(1.0 - abs(r - front) * 6.0) * (1.0 - phase * 0.5);
        float core = (1.0 - phase) * pow(saturate(1.0 - r * 2.5), 2.0);
        float fall = 1.0 - r * 0.6;
        shape = mask * (lines * (0.35 + ripple * 1.4) * fall + ripple * 0.35 + core * 1.5 + 0.08);
    }
    return PipelineExpose(half4(i.color.rgb * shape * i.color.a, 1));
}
