#ifndef CHRONO_RIPPLE_SAMPLE_INCLUDED
#define CHRONO_RIPPLE_SAMPLE_INCLUDED

// Reads the interactive wave simulation published by WaterSimulation.
// Texture: x height (m), y vertical velocity, z foam. Rect: min x, min z, 1/size, texel size (m).
TEXTURE2D(_CW_RippleTex); SAMPLER(sampler_CW_RippleTex);
float4 _CW_RippleRect;

// gradient = (dh/dx, dh/dz). Returns false outside the simulated area.
bool CW_SampleRipple(float2 xz, out float2 gradient, out float foam)
{
    gradient = 0;
    foam = 0;
    if (_CW_RippleRect.z <= 0.0)
        return false;
    float2 uv = (xz - _CW_RippleRect.xy) * _CW_RippleRect.z;
    if (uv.x <= 0.0 || uv.y <= 0.0 || uv.x >= 1.0 || uv.y >= 1.0)
        return false;
    float texel = _CW_RippleRect.w;
    // Slightly wider than one texel: hides the grid in sharp sun glints.
    float step = texel * 1.5 * _CW_RippleRect.z;
    float l = SAMPLE_TEXTURE2D_LOD(_CW_RippleTex, sampler_CW_RippleTex, uv - float2(step, 0), 0).x;
    float r = SAMPLE_TEXTURE2D_LOD(_CW_RippleTex, sampler_CW_RippleTex, uv + float2(step, 0), 0).x;
    float d = SAMPLE_TEXTURE2D_LOD(_CW_RippleTex, sampler_CW_RippleTex, uv - float2(0, step), 0).x;
    float u = SAMPLE_TEXTURE2D_LOD(_CW_RippleTex, sampler_CW_RippleTex, uv + float2(0, step), 0).x;
    gradient = float2(r - l, u - d) / (3.0 * texel);
    // Very steep slopes only produce broken sun glints; real crests that steep would be foam.
    float steepness = length(gradient);
    if (steepness > 0.55)
        gradient *= 0.55 / steepness;
    foam = SAMPLE_TEXTURE2D_LOD(_CW_RippleTex, sampler_CW_RippleTex, uv, 0).z;
    return true;
}

// Rain on water: expanding rings at random spots, two overlapping layers. Density follows the rain.
float _CW_RainAmount;

// Top-down shelter map of the island's canopy: R = canopy height (m), G = how covered the spot is.
TEXTURE2D(_CW_RainOcclusion); SAMPLER(sampler_CW_RainOcclusion);
float4 _CW_RainOcclusionRect;   // x, z, 1/width, 1/depth

// How much rain reaches a surface here (1 in the open, low under trees).
float CW_RainExposure(float2 xz)
{
    if (_CW_RainOcclusionRect.z <= 0.0)
        return 1.0;
    float2 uv = (xz - _CW_RainOcclusionRect.xy) * _CW_RainOcclusionRect.zw;
    return 1.0 - 0.9 * SAMPLE_TEXTURE2D_LOD(_CW_RainOcclusion, sampler_CW_RainOcclusion, uv, 0).g;
}

// Whether a falling drop exists at this point: gone below the canopy, except for a few drips.
float CW_RainFallsAt(float3 p)
{
    if (_CW_RainOcclusionRect.z <= 0.0)
        return 1.0;
    float2 uv = (p.xz - _CW_RainOcclusionRect.xy) * _CW_RainOcclusionRect.zw;
    float canopy = SAMPLE_TEXTURE2D_LOD(_CW_RainOcclusion, sampler_CW_RainOcclusion, uv, 0).r;
    return p.y > canopy ? 1.0 : 0.0;
}

float2 CW_RingLayer(float2 p, float seed)
{
    float2 id = floor(p);
    float3 q = frac(float3(id.xyx + seed) * float3(0.1031, 0.1030, 0.0973));
    q += dot(q, q.yzx + 33.33);
    float2 rnd = frac((q.xx + q.yz) * q.zy);
    if (rnd.x > _CW_RainAmount * 0.9 + 0.1)
        return 0;
    float life = frac(_Time.y * (1.1 + rnd.y) + rnd.x * 7.0);
    float2 d = p - (id + 0.3 + rnd * 0.4);
    float dist = length(d);
    float offset = dist - life * 0.34;
    float ring = exp(-offset * offset * 900.0) * (1.0 - life);
    return d / max(dist, 1e-3) * ring * sin(offset * 70.0);
}

// Slope (dh/dx, dh/dz) from the falling rain.
float2 CW_RainSlope(float2 xz)
{
    if (_CW_RainAmount <= 0.001)
        return 0;
    return (CW_RingLayer(xz * 1.6, 1.0) + CW_RingLayer(float2(xz.y, -xz.x) * 2.3, 7.0)) * saturate(_CW_RainAmount * 3.0) * 0.6 * CW_RainExposure(xz);
}

#endif
