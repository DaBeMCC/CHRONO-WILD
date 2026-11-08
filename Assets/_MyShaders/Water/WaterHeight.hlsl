#ifndef CHRONO_WATER_HEIGHT_INCLUDED
#define CHRONO_WATER_HEIGHT_INCLUDED

// Compute-side evaluation of the water surface height. The ocean part mirrors the vertex stage of
// CoastalOcean.shader (swell + surf + baked FFT) so physics follows the waves the player sees.
// If the ocean vertex model changes, change OceanHeight() with it. Horizontal displacement is ignored.

#define WATER_TWO_PI 6.2831853

SamplerState sampler_LinearClamp;
SamplerState sampler_LinearRepeat;

Texture2D<float4> _BiomeTex;          // B channel: swamp water extent
Texture2D<float> _TerrainHeight;      // world height in metres
float4 _TerrainRect;                  // x, z, 1/width, 1/depth
float _SwampLevel;

Texture2D<float4> _SeabedTex;
float4 _SeabedRect;
float _SeabedOpenDepth;
Texture2DArray<float4> _FFTDisp;

float _SimTime;
float _WaveHeight, _WaveLength, _WaveDirection, _WaveSpeed;
float _SurfHeight, _SurfPeriod, _BreakerIndex, _RunUp, _SurfStartDepth;
float _FFTPatch, _FFTFrames, _FFTPeriod, _FFTStrength, _FFTDetail;

float TerrainHeightAt(float2 xz)
{
    float2 uv = (xz - _TerrainRect.xy) * _TerrainRect.zw;
    return _TerrainHeight.SampleLevel(sampler_LinearClamp, uv, 0);
}

bool IsSwampAt(float2 xz)
{
    float2 uv = (xz - _TerrainRect.xy) * _TerrainRect.zw;
    return _BiomeTex.SampleLevel(sampler_LinearClamp, uv, 0).b > 0.05;
}

float StillLevelAt(float2 xz)
{
    return IsSwampAt(xz) ? _SwampLevel : 0.0;
}

float SeabedAt(float2 xz)
{
    float2 uv = (xz - _SeabedRect.xy) / _SeabedRect.zw;
    if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0)
        return -_SeabedOpenDepth;
    return _SeabedTex.SampleLevel(sampler_LinearClamp, uv, 0).r;
}

float GerstnerY(float2 p, float2 dir, float wavelength, float amplitude, float t)
{
    float k = WATER_TWO_PI / wavelength;
    float c = sqrt(9.81 / k);
    return amplitude * sin(k * (dot(dir, p) - c * t));
}

float FFTHeight(float2 uv, float time)
{
    float fr = frac(time / _FFTPeriod) * _FFTFrames;
    float f0 = floor(fr), f1 = fmod(f0 + 1.0, _FFTFrames), blend = fr - f0;
    float a = _FFTDisp.SampleLevel(sampler_LinearRepeat, float3(uv, f0), 0).y;
    float b = _FFTDisp.SampleLevel(sampler_LinearRepeat, float3(uv, f1), 0).y;
    return lerp(a, b, blend);
}

float OceanHeight(float2 xz)
{
    float2 rectCenter = _SeabedRect.xy + _SeabedRect.zw * 0.5;
    float2 outside = abs(xz - rectCenter) - _SeabedRect.zw * 0.5;
    float fade = 1.0 - smoothstep(-25.0, -5.0, max(outside.x, outside.y));
    float t = _SimTime * _WaveSpeed;

    float bed = SeabedAt(xz);
    float depth = max(-bed, 0.0);
    float e = 6.0;
    float2 grad = float2(SeabedAt(xz + float2(e, 0)) - SeabedAt(xz - float2(e, 0)), SeabedAt(xz + float2(0, e)) - SeabedAt(xz - float2(0, e)));

    float ang = _WaveDirection * 0.01745329;
    float2 d0 = float2(cos(ang), sin(ang)), d1 = float2(cos(ang + 0.45), sin(ang + 0.45));
    float2 d2 = float2(cos(ang - 0.6), sin(ang - 0.6)), d3 = float2(cos(ang + 1.3), sin(ang + 1.3));
    float h = _WaveHeight * fade * saturate(depth / 5.0);
    float y = GerstnerY(xz, d0, _WaveLength, h * 0.55, t) + GerstnerY(xz, d1, _WaveLength * 0.62, h * 0.25, t)
            + GerstnerY(xz, d2, _WaveLength * 0.41, h * 0.13, t) + GerstnerY(xz, d3, _WaveLength * 0.27, h * 0.07, t);

    float along = dot(xz, float2(-d0.y, d0.x));
    float bedSlope = clamp(length(grad) / (2.0 * e), 0.03, 0.45);
    float travel = 2.0 * sqrt(depth) / (bedSlope * 3.132);
    float sets = 0.62 + 0.38 * sin(WATER_TWO_PI * _SimTime / (_SurfPeriod * 7.0) + along * 0.012);
    float waveH = _SurfHeight * sets;
    float phase = frac((_SimTime + travel) / _SurfPeriod + along * 0.0025);
    float breakDepth = waveH / _BreakerIndex;
    float shoaled = waveH * pow(_SurfStartDepth / max(depth, breakDepth), 0.25) * 0.55;
    float unbroken = smoothstep(breakDepth * 0.85, breakDepth * 1.15, depth);
    float growIn = 1.0 - smoothstep(_SurfStartDepth * 0.6, _SurfStartDepth, depth);
    float depthRamp = saturate((depth - breakDepth) / 3.0);
    float frontW = lerp(0.03, 0.12, depthRamp), backW = lerp(0.3, 0.35, depthRamp);
    float peak = phase < 0.12 ? pow(saturate(1.0 - (0.12 - phase) / frontW), 1.6) : pow(saturate(1.0 - (phase - 0.12) / backW), 2.2);
    float boreShape = phase < 0.06 ? phase / 0.06 : exp(-(phase - 0.06) * 7.0);
    float amp = lerp(depth * 0.8, shoaled, unbroken) * growIn * fade;
    y += amp * lerp(boreShape, peak, unbroken);
    float nearBreak = smoothstep(breakDepth * 2.0, breakDepth, depth) * unbroken;
    y += amp * 0.35 * nearBreak * pow(peak, 4.0);
    y += (1.0 - unbroken) * growIn * fade * 0.18 * exp(-phase * 3.0) * saturate(depth / 0.2);

    float fftAmount = _FFTStrength * fade * saturate((depth - breakDepth * 1.5) / 3.0);
    if (fftAmount > 0.0)
    {
        float2 uvB = float2(xz.x * 0.8 - xz.y * 0.6, xz.x * 0.6 + xz.y * 0.8) / (_FFTPatch * 0.271) + 0.37;
        y += (FFTHeight(xz / _FFTPatch, _SimTime) + FFTHeight(uvB, _SimTime * 1.37 + 5.3) * _FFTDetail * 0.271) * fftAmount;
    }
    return y;
}

float WaterHeightAt(float2 xz)
{
    return IsSwampAt(xz) ? _SwampLevel : OceanHeight(xz);
}

#endif
