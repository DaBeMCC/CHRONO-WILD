Shader "CHRONO WILD/Water/Coastal Ocean"
{
 // Tropical coastal water built from cheap layers:
 //  - open-sea Gerstner swell that calms in shallow water;
 //  - depth-aware surf: the vertex stage reads a baked seabed map (OceanSeabed), so waves turn
 //    toward the beach, shorten and grow as the floor rises, steepen their front and break;
 //  - per-pixel swell and chop normals whose scale grows with distance, so the far sea keeps
 //    moving after vertex waves fade, plus animated sun glitter and drifting whitecaps;
 //  - depth-texture color, refraction, foam (shore rim, breaking white water, trailing foam).
 // _ChronoQuality (global, 0 low / 1 high) drops refraction and fine detail on low.
 Properties
 {
  _ShallowColor("Shallow Color",Color)=(0.20,0.62,0.58,1)
  _DeepColor("Deep Color",Color)=(0.0,0.22,0.45,1)
  _DepthRange("Color Depth Range (m)",Float)=6
  _Clarity("Water Clarity (m)",Float)=2.2
  _Refraction("Refraction",Range(0,0.08))=0.02
  [Header(Open Sea Swell)]
  _WaveHeight("Swell Height (m)",Range(0,1.5))=0.35
  _WaveLength("Swell Wavelength (m)",Float)=22
  _WaveDirection("Swell Direction (deg, toward shore)",Float)=90
  _WaveSpeed("Wave Speed",Float)=1
  _WaveFade("Vertex Wave Fade Distance (m)",Float)=350
  [Header(Surf)]
  _SurfHeight("Surf Height (m)",Range(0,1.5))=0.45
  _SurfSpacing("Surf Spacing",Float)=2.6
  _SurfSpeed("Surf Speed",Float)=0.9
  _SurfStartDepth("Surf Start Depth (m)",Float)=7
  _BreakDepth("Break Depth (m)",Float)=0.9
  [Header(Surface Detail)]
  _Ripples("Detail Chop",Range(0,1))=0.35
  _FarSwell("Far Swell Normals",Range(0,1))=0.18
  _SwellLines("Far Swell Lines",Range(0,2))=1
  _Roughness("Roughness",Range(0,0.3))=0.06
  _Glitter("Sun Glitter",Range(0,4))=1.5
  _SkyZenith("Sky Zenith (reflection)",Color)=(0.30,0.48,0.78,1)
  _SkyHorizon("Sky Horizon (reflection)",Color)=(0.46,0.62,0.8,1)
  [Header(Foam)]
  _FoamColor("Foam Color",Color)=(0.95,0.96,0.94,1)
  _ShoreFoamDepth("Shore Foam Depth (m)",Float)=0.35
  _BreakFoam("Breaking Foam",Range(0,1))=0.85
  _Whitecaps("Far Whitecaps",Range(0,1))=0.6
  _EdgeFade("Shoreline Fade (m)",Range(0.01,0.5))=0.06
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent"}
  Pass
  {
   Name "CoastalOcean" Tags {"LightMode"="UniversalForward"}
   Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Back
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vertex
   #pragma fragment Fragment
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
   #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

   CBUFFER_START(UnityPerMaterial)
   half4 _ShallowColor; half4 _DeepColor; float _DepthRange; float _Clarity; half _Refraction;
   float _WaveHeight; float _WaveLength; float _WaveDirection; float _WaveSpeed; float _WaveFade;
   float _SurfHeight; float _SurfSpacing; float _SurfSpeed; float _SurfStartDepth; float _BreakDepth;
   half _Ripples; half _FarSwell; half _SwellLines; half _Roughness; half _Glitter; half4 _SkyZenith; half4 _SkyHorizon;
   half4 _FoamColor; float _ShoreFoamDepth; half _BreakFoam; half _Whitecaps; half _EdgeFade;
   CBUFFER_END
   float _ChronoQuality;
   TEXTURE2D(_ChronoSeabedTex); SAMPLER(sampler_ChronoSeabedTex);
   float4 _ChronoSeabedRect;
   float _ChronoSeabedOpenDepth;

   struct Attributes { float4 positionOS:POSITION; };
   struct Varyings
   {
    float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1;
    float4 waveData:TEXCOORD2; // x swell crest, y surf crest, z breaking, w seabed depth
   };

   float Hash1(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
   float2 Hash2(float2 p) { p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3))); return frac(sin(p) * 43758.5453); }
   float ValueNoise(float2 p)
   {
    float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
    return lerp(lerp(Hash1(i), Hash1(i + float2(1, 0)), f.x), lerp(Hash1(i + float2(0, 1)), Hash1(i + float2(1, 1)), f.x), f.y);
   }
   float Cells(float2 p)
   {
    float2 i = floor(p), f = frac(p); float m = 1;
    [unroll] for (int y = -1; y <= 1; y++) [unroll] for (int x = -1; x <= 1; x++)
    { float2 g = float2(x, y); m = min(m, length(g + Hash2(i + g) - f)); }
    return m;
   }

   // Seabed height (m, sea level 0) from the baked map; open sea outside it.
   float Seabed(float2 xz)
   {
    float2 uv = (xz - _ChronoSeabedRect.xy) / _ChronoSeabedRect.zw;
    float inside = step(0, uv.x) * step(0, uv.y) * step(uv.x, 1) * step(uv.y, 1);
    float h = SAMPLE_TEXTURE2D_LOD(_ChronoSeabedTex, sampler_ChronoSeabedTex, saturate(uv), 0).r;
    return lerp(-_ChronoSeabedOpenDepth, h, inside);
   }

   float3 Gerstner(float2 p, float2 dir, float wavelength, float steepness, float amplitude, float t, inout float3 tangent, inout float3 binormal)
   {
    float k = TWO_PI / wavelength;
    float c = sqrt(9.81 / k);
    float f = k * (dot(dir, p) - c * t);
    float a = amplitude;
    float q = steepness / max(k * a * 4.0, 1e-3);
    tangent += float3(-dir.x * dir.x * q * k * a * sin(f), dir.x * k * a * cos(f), -dir.x * dir.y * q * k * a * sin(f));
    binormal += float3(-dir.x * dir.y * q * k * a * sin(f), dir.y * k * a * cos(f), -dir.y * dir.y * q * k * a * sin(f));
    return float3(dir.x * q * a * cos(f), a * sin(f), dir.y * q * a * cos(f));
   }

   // Skewed surf profile: long gentle back, steep front, sharp crest. s in [0,1).
   float SurfProfile(float s) { return pow(saturate(1.0 - abs(s - 0.65) / 0.65), 2.2); }

   Varyings Vertex(Attributes a)
   {
    Varyings v;
    float3 p = TransformObjectToWorld(a.positionOS.xyz);
    float camFade = 1.0 - saturate(distance(p.xz, _WorldSpaceCameraPos.xz) / _WaveFade);
    // Vertex waves only on the fine coast grid (the seabed rectangle plus a margin);
    // the coarse far skirt stays flat and relies on normal detail.
    float2 rectCenter = _ChronoSeabedRect.xy + _ChronoSeabedRect.zw * 0.5;
    float2 outside = abs(p.xz - rectCenter) - _ChronoSeabedRect.zw * 0.5;
    camFade *= 1.0 - smoothstep(0.0, 15.0, max(outside.x, outside.y));
    float t = _Time.y * _WaveSpeed;

    float bed = Seabed(p.xz);
    float depth = max(-bed, 0.0);
    // Shore direction: uphill on the seabed.
    float e = 2.0;
    float2 grad = float2(Seabed(p.xz + float2(e, 0)) - Seabed(p.xz - float2(e, 0)), Seabed(p.xz + float2(0, e)) - Seabed(p.xz - float2(0, e)));
    float2 shoreDir = length(grad) > 1e-4 ? normalize(grad) : float2(0, 1);

    // Open-sea swell, calmed where the water is shallow (the surf takes over there).
    float ang = radians(_WaveDirection);
    float2 d0 = float2(cos(ang), sin(ang)), d1 = float2(cos(ang + 0.45), sin(ang + 0.45));
    float2 d2 = float2(cos(ang - 0.6), sin(ang - 0.6)), d3 = float2(cos(ang + 1.3), sin(ang + 1.3));
    float3 tangent = float3(1, 0, 0), binormal = float3(0, 0, 1);
    float h = _WaveHeight * camFade * saturate(depth / 5.0);
    float3 disp = Gerstner(p.xz, d0, _WaveLength, 0.55, h * 0.55, t, tangent, binormal)
                + Gerstner(p.xz, d1, _WaveLength * 0.62, 0.5, h * 0.25, t, tangent, binormal)
                + Gerstner(p.xz, d2, _WaveLength * 0.41, 0.45, h * 0.13, t, tangent, binormal)
                + Gerstner(p.xz, d3, _WaveLength * 0.27, 0.4, h * 0.07, t, tangent, binormal);
    float swellCrest = saturate(disp.y / max(h * 0.6, 1e-3));

    // Surf: phase from sqrt(depth) so crests bunch up toward the beach (shoaling) and travel shoreward.
    float along = dot(p.xz, float2(-shoreDir.y, shoreDir.x));
    float sets = 0.55 + 0.45 * ValueNoise(float2(along * 0.04, _Time.y * 0.05));
    float phase = sqrt(depth) * _SurfSpacing + _Time.y * _SurfSpeed;
    float grow = 1.0 - smoothstep(_BreakDepth * 1.6, _SurfStartDepth, depth);
    float die = smoothstep(0.05, _BreakDepth * 0.7, depth);
    float amp = _SurfHeight * grow * die * sets * camFade;
    float profile = SurfProfile(frac(-phase));
    float rise = amp * profile;
    float lean = amp * 0.6 * profile * saturate(1.0 - depth / (_BreakDepth * 2.5));
    disp.y += rise;
    disp.xz += shoreDir * lean;
    float breaking = profile * saturate(1.0 - (depth - _BreakDepth * 0.4) / (_BreakDepth * 1.8)) * grow * die * sets;
    // Surf slope along the shore direction, folded into the normal frame.
    float stepM = 0.4;
    float depthAhead = max(-Seabed(p.xz + shoreDir * stepM), 0.0);
    float slope = (amp * SurfProfile(frac(-(sqrt(depthAhead) * _SurfSpacing + _Time.y * _SurfSpeed))) - rise) / stepM;
    tangent.y += slope * shoreDir.x;
    binormal.y += slope * shoreDir.y;

    p += disp;
    v.positionWS = p;
    v.normalWS = normalize(cross(binormal, tangent));
    v.waveData = float4(swellCrest, profile * step(0.001, amp), breaking, depth);
    v.positionCS = TransformWorldToHClip(p);
    return v;
   }

   // Animated slope from a noise layer (central differences).
   float2 NoiseSlope(float2 p, float2 drift)
   {
    float2 q = p + drift;
    return float2(ValueNoise(q + float2(0.35, 0)) - ValueNoise(q - float2(0.35, 0)), ValueNoise(q + float2(0, 0.35)) - ValueNoise(q - float2(0, 0.35)));
   }

   // Per-pixel directional swell: long-crested sine trains with analytic slopes. They cost a few
   // ALU ops, never fade with distance and make crest lines visibly travel toward the horizon.
   float2 SwellLines(float2 p, float t, float baseAngle, float dist)
   {
    float2 slope = 0;
    const float lengths[4] = { 78.0, 47.0, 31.0, 19.0 };
    const float angles[4] = { 0.0, 0.32, -0.41, 0.7 };
    const float amps[4] = { 0.07, 0.055, 0.04, 0.03 };
    [unroll] for (int i = 0; i < 4; i++)
    {
     float a = baseAngle + angles[i];
     float2 d = float2(cos(a), sin(a));
     float k = TWO_PI / lengths[i];
     float w = sqrt(9.81 * k);
     // Smaller trains fade once they get below a few pixels to avoid shimmer.
     float lod = saturate(1.0 - dist / (lengths[i] * 18.0));
     slope += d * cos(dot(d, p) * k - w * t) * amps[i] * k * lengths[i] * 0.32 * lerp(0.35, 1.0, lod);
    }
    return slope;
   }

   half4 Fragment(Varyings v) : SV_Target
   {
    bool high = _ChronoQuality > 0.5;
    float2 screenUV = GetNormalizedScreenSpaceUV(v.positionCS);
    float3 view = GetWorldSpaceNormalizeViewDir(v.positionWS);
    float surfaceEye = -TransformWorldToView(v.positionWS).z;
    float bottomEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
    float rayDepth = max(0, bottomEye - surfaceEye) * (length(_WorldSpaceCameraPos - v.positionWS) / max(surfaceEye, 1e-3));
    // Beyond the terrain there is no sea floor in the depth buffer; cap with the baked seabed
    // depth so the water color doesn't jump at the terrain edge.
    rayDepth = min(rayDepth, (v.waveData.w + 0.5) / max(view.y, 0.05));
    float depth = rayDepth * saturate(view.y + 0.05);
    float dist = length(_WorldSpaceCameraPos - v.positionWS);
    float t = _Time.y;
    float2 p = v.positionWS.xz;

    // Normals: vertex waves + large swell layers that never fade (far sea keeps moving) + chop up close.
    float3 n = normalize(v.normalWS);
    float2 slope = NoiseSlope(p * 0.045, float2(t * 0.06, t * 0.035)) * 1.1
                 + NoiseSlope(p * 0.11, float2(-t * 0.05, t * 0.09)) * 0.6;
    slope *= _FarSwell * (0.6 + 0.4 * saturate(dist / 300));
    slope += SwellLines(p, t * _WaveSpeed, radians(_WaveDirection), dist) * _SwellLines * saturate(v.waveData.w / 4.0);
    if (high)
    {
     float nearChop = saturate(1 - dist / 60);
     slope += (NoiseSlope(p * 0.9, float2(t * 0.35, t * 0.2)) + NoiseSlope(p * 2.6, float2(-t * 0.5, t * 0.45)) * 0.5) * _Ripples * nearChop;
    }
    n = normalize(n + float3(slope.x, 0, slope.y));

    half3 bottom;
    if (high)
    {
     float2 uv = screenUV + n.xz * _Refraction * saturate(depth * 2);
     if (LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams) < surfaceEye) uv = screenUV;
     bottom = SampleSceneColor(uv);
    }
    else bottom = SampleSceneColor(screenUV);

    Light sun = GetMainLight(TransformWorldToShadowCoord(v.positionWS));
    half3 waterColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(depth / _DepthRange));
    // Light passing through thin wave crests turns them greener and brighter.
    waterColor = lerp(waterColor, _ShallowColor.rgb * 1.25, saturate(v.waveData.y * 0.6 + v.waveData.x * 0.25));
    half visibility = exp(-rayDepth / _Clarity);
    half3 lit = waterColor * (sun.color * saturate(sun.direction.y) * 0.55 + _SkyZenith.rgb * 0.6);
    half3 color = lerp(lit, bottom * lerp(_ShallowColor.rgb * 1.4, 1, 0.45), visibility);

    // Far away the surface is a blur of tilted facets: average reflectance drops below the
    // smooth-surface Fresnel, which keeps the distant sea a deep blue instead of milky.
    half fresnel = (0.02 + 0.98 * pow(1 - saturate(dot(n, view)), 5)) * lerp(1.0, 0.62, saturate(dist / 350));
    float3 rd = reflect(-view, n);
    half3 refl = lerp(_SkyHorizon.rgb, _SkyZenith.rgb, pow(saturate(rd.y), 0.45));
    color = lerp(color, refl, fresnel);

    // Sun: tight highlight up close, a wide glittering path far away that sparkles over time.
    half3 hv = normalize(sun.direction + view);
    half ndh = saturate(dot(n, hv));
    float spread = lerp(900, 60, saturate(dist / 400));
    half glitterNoise = step(0.93, Hash1(floor(p * lerp(4.0, 0.7, saturate(dist / 300))) + floor(t * 8)));
    half spec = pow(ndh, spread) * lerp(8, 1.2, saturate(dist / 400));
    spec += pow(ndh, spread * 0.25) * glitterNoise * _Glitter * saturate(dist / 60);
    color += sun.color * spec * sun.shadowAttenuation;

    // Foam.
    float bubbles = 1 - Cells(p * 2.4 + t * float2(0.05, 0.11)) * 0.6 - Cells(p * 5.3 - t * 0.07) * 0.4;
    float wisps = ValueNoise(p * 0.35 + t * 0.07) * 0.6 + ValueNoise(p * 1.3 - t * 0.1) * 0.4;
    float pattern = bubbles * 0.55 + wisps * 0.45;
    float rimBreak = 0.45 + 0.55 * ValueNoise(p * 0.18 + float2(t * 0.03, 0));
    float rim = (1 - saturate(depth / _ShoreFoamDepth)) * rimBreak;
    // White water: the breaking crest itself, then a foam sheet left behind it in the shallows.
    float breakFoam = saturate(v.waveData.z * 1.6) * _BreakFoam;
    float trail = saturate(1 - v.waveData.w / 1.6) * saturate(v.waveData.y + 0.35) * 0.55 * _BreakFoam;
    // Whitecaps drifting on the open sea, appearing and fading.
    float capField = smoothstep(0.7, 0.86, ValueNoise(p * 0.035 + float2(t * 0.025, t * 0.015)));
    float capPulse = smoothstep(0.55, 0.85, ValueNoise(p * 0.18 + float2(-t * 0.3, t * 0.12)));
    float caps = capField * capPulse * _Whitecaps * saturate((dist - 30) / 120) * saturate(v.waveData.w / 6);
    // Breaking water tears into streaks along the crest instead of solid blobs.
    float streaks = ValueNoise(float2(dot(p, float2(0.8, 0.6)) * 0.9, dot(p, float2(-0.6, 0.8)) * 0.18) + t * 0.2);
    breakFoam *= 0.55 + 0.6 * streaks;
    trail *= 0.4 + 0.8 * wisps;
    float foamMask = saturate(rim * 1.2 + breakFoam + trail + caps);
    float coverage = foamMask * 0.58;
    float foam = smoothstep(1.0 - coverage, 1.0 - coverage + 0.15, pattern) * saturate(foamMask * 3);
    foam = max(foam, caps * 0.8 * saturate(dist / 200));
    color = lerp(color, _FoamColor.rgb * (sun.color * saturate(sun.direction.y) + _SkyZenith.rgb * 0.5) * 0.85, foam);

    half alpha = saturate(depth / _EdgeFade);
    return half4(color, alpha * lerp(1, 0.9, foam));
   }
   ENDHLSL
  }
 }
}
