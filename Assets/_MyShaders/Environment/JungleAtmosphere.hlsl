#ifndef CHRONO_JUNGLE_ATMOSPHERE_INCLUDED
#define CHRONO_JUNGLE_ATMOSPHERE_INCLUDED

// Analytic version of the JungleFog full-screen pass (height fog + distance haze, no shafts).
// Transparent surfaces such as water apply it themselves: the full-screen pass runs before
// transparents and only sees the depth behind them.

float4 _JungleFogParams;      // x density, y base height, z height falloff, w sky distance
float4 _JungleFogAmbient;
float4 _JungleHaze;           // x density, y start distance, z height falloff
float4 _JungleHazeColor;      // rgb color, a sun tint
float4 _JungleFogRegion;      // xy center, z radius, w falloff
float _JungleFogOpenDensity;
float4 _JungleOpenHaze;       // rgb color, a density outside the forest

// Extra local fog banks (swamp ground mist, moor haze...), set by JungleAtmosphere.extraRegions.
float4 _JungleFogExtraA[4];   // xy center, z radius, w falloff
float4 _JungleFogExtraB[4];   // x density, y base height, z height falloff
float _JungleFogExtraCount;

float JungleExtraFogDensity(float3 p)
{
    float d = 0.0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        if (i >= (int)_JungleFogExtraCount)
            break;
        float4 a = _JungleFogExtraA[i];
        float4 b = _JungleFogExtraB[i];
        float region = 1.0 - smoothstep(a.z, a.z + a.w, distance(p.xz, a.xy));
        d += b.x * min(exp(-(p.y - b.y) * b.z), 4.0) * region;
    }
    return d;
}

float JungleFogDensityAt(float3 p)
{
    float height = min(exp(-(p.y - _JungleFogParams.y) * _JungleFogParams.z), 3.0);
    float region = 1.0 - smoothstep(_JungleFogRegion.z, _JungleFogRegion.z + _JungleFogRegion.w, distance(p.xz, _JungleFogRegion.xy));
    return _JungleFogParams.x * height * lerp(_JungleFogOpenDensity, 1.0, region) + JungleExtraFogDensity(p);
}

float JunglePhaseHG(float cosTheta, float g)
{
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * PI * pow(max(1.0 + g2 - 2.0 * g * cosTheta, 1e-4), 1.5));
}

half3 ApplyJungleAtmosphere(half3 color, float3 positionWS, half3 sunColor, float3 sunDir)
{
    float3 camera = _WorldSpaceCameraPos;
    float3 toPoint = positionWS - camera;
    float dist = length(toPoint);
    float3 dir = toPoint / max(dist, 1e-4);

    float3 mid = camera + toPoint * 0.5;
    float fog = 1.0 - exp(-JungleFogDensityAt(mid) * dist);
    color = lerp(color, _JungleFogAmbient.rgb + sunColor * 0.05, fog);

    float hazeDist = max(dist - _JungleHaze.y, 0.0);
    float midHeight = camera.y + dir.y * min(dist, 400.0) * 0.5;
    float thin = exp(-max(midHeight - _JungleFogParams.y, 0.0) * _JungleHaze.z);
    float inForest = 1.0 - smoothstep(_JungleFogRegion.z, _JungleFogRegion.z + _JungleFogRegion.w, distance(camera.xz, _JungleFogRegion.xy));
    float hazeDensity = lerp(_JungleOpenHaze.a, _JungleHaze.x, inForest);
    float haze = 1.0 - exp(-hazeDist * hazeDensity * thin);
    float3 hazeColor = lerp(_JungleOpenHaze.rgb, _JungleHazeColor.rgb, inForest) + sunColor * JunglePhaseHG(dot(dir, sunDir), 0.45) * _JungleHazeColor.a * 4.0 * PI;
    return lerp(color, hazeColor, haze);
}

#endif
