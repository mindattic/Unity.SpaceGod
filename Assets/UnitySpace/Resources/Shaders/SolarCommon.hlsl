#ifndef SOLAR_COMMON_INCLUDED
#define SOLAR_COMMON_INCLUDED

// Camera-relative world position of the Sun, set every frame by UnitySpaceApp. The camera sits at
// the world origin (floating origin), so this is also the direction to the Sun from the eye.
float4 _SunPositionWS;

// The main star's light colour times intensity (white for the Sun), and a second star's
// (Alpha Centauri B beside A); the second is black where there is none. Set by UnitySpaceApp.
float4 _SunColor;
float4 _Sun2PositionWS;
float4 _Sun2Color;

float SolarHash13(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float SolarValueNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float n000 = SolarHash13(i + float3(0, 0, 0));
    float n100 = SolarHash13(i + float3(1, 0, 0));
    float n010 = SolarHash13(i + float3(0, 1, 0));
    float n110 = SolarHash13(i + float3(1, 1, 0));
    float n001 = SolarHash13(i + float3(0, 0, 1));
    float n101 = SolarHash13(i + float3(1, 0, 1));
    float n011 = SolarHash13(i + float3(0, 1, 1));
    float n111 = SolarHash13(i + float3(1, 1, 1));
    float x00 = lerp(n000, n100, f.x);
    float x10 = lerp(n010, n110, f.x);
    float x01 = lerp(n001, n101, f.x);
    float x11 = lerp(n011, n111, f.x);
    return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
}

float SolarFbm(float3 p)
{
    float sum = 0.0;
    float amp = 0.5;
    for (int octave = 0; octave < 5; octave++)
    {
        sum += amp * SolarValueNoise(p);
        p = p * 2.03 + 17.1;
        amp *= 0.5;
    }
    return sum / 0.97;
}

#endif
