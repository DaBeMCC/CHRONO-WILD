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
  _SurfHeight("Offshore Wave Height (m)",Range(0,1.5))=0.4
  _SurfPeriod("Wave Period (s)",Range(4,16))=9
  _BreakerIndex("Breaker Index (H/depth)",Range(0.5,1.2))=0.78
  _RunUp("Swash Run-up (m)",Range(0,1.5))=0.45
  _SurfStartDepth("Shoaling Start Depth (m)",Float)=8
  _BreakDepth("Break Depth (m)",Float)=0.9
  [Header(Surface Detail)]
  _Ripples("Detail Chop",Range(0,1))=0.35
  _FarSwell("Far Swell Normals",Range(0,1))=0.18
  _SwellLines("Far Swell Lines",Range(0,2))=1
  _Roughness("Roughness",Range(0,0.3))=0.06
  _Glitter("Sun Glitter",Range(0,4))=0.6
  _SkyZenith("Sky Zenith (reflection)",Color)=(0.30,0.48,0.78,1)
  _SkyHorizon("Sky Horizon (reflection)",Color)=(0.46,0.62,0.8,1)
  [Header(Foam)]
  _FoamColor("Foam Color",Color)=(0.95,0.96,0.94,1)
  _ShoreFoamDepth("Shore Foam Depth (m)",Float)=0.35
  _BreakFoam("Breaking Foam",Range(0,1))=0.85
  _Whitecaps("Far Whitecaps",Range(0,1))=0.6
  _EdgeFade("Shoreline Fade (m)",Range(0.01,0.5))=0.06
  [Header(Baked FFT Ocean)]
  [NoScaleOffset] _FFTDisplacement("FFT Displacement (dx, h, dz, J)",2DArray)="" {}
  [NoScaleOffset] _FFTNormal("FFT Slopes, Foam, Caustics",2DArray)="" {}
  _FFTPatch("FFT Patch Size (m)",Float)=64
  _FFTFrames("FFT Frames",Float)=64
  _FFTPeriod("FFT Loop Period (s)",Float)=16
  _FFTStrength("FFT Wave Strength",Range(0,2))=1
  _FFTDetail("FFT Second Band Strength",Range(0,1))=0.45
  _FFTFoam("FFT Whitecap Foam",Range(0,2))=1
  [Header(Light In Water)]
  _SSSColor("Subsurface Color",Color)=(0.12,0.75,0.62,1)
  _SSSStrength("Subsurface Strength",Range(0,3))=1.2
  _Caustics("Caustics",Range(0,3))=1.2
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
   #include "Assets/_MyShaders/Environment/JungleAtmosphere.hlsl"
   #include "Assets/_MyShaders/Water/RippleSample.hlsl"

   CBUFFER_START(UnityPerMaterial)
   half4 _ShallowColor; half4 _DeepColor; float _DepthRange; float _Clarity; half _Refraction;
   float _WaveHeight; float _WaveLength; float _WaveDirection; float _WaveSpeed; float _WaveFade;
   float _SurfHeight; float _SurfPeriod; float _BreakerIndex; float _RunUp; float _SurfStartDepth; float _BreakDepth;
   half _Ripples; half _FarSwell; half _SwellLines; half _Roughness; half _Glitter; half4 _SkyZenith; half4 _SkyHorizon;
   half4 _FoamColor; float _ShoreFoamDepth; half _BreakFoam; half _Whitecaps; half _EdgeFade;
   CBUFFER_END
   float _ChronoQuality;
   // Time-of-day and weather sky colours, when the day-night cycle is running.
   float4 _CW_SkyZ, _CW_SkyH;
   #define SKY_ZENITH lerp(_SkyZenith.rgb, _CW_SkyZ.rgb, _CW_SkyZ.w)
   #define SKY_HORIZON lerp(_SkyHorizon.rgb, _CW_SkyH.rgb, _CW_SkyH.w)
   TEXTURE2D(_ChronoSeabedTex); SAMPLER(sampler_ChronoSeabedTex);
   float4 _ChronoSeabedRect;
   float _ChronoSeabedOpenDepth;
   TEXTURE2D_ARRAY(_FFTDisplacement); SAMPLER(sampler_FFTDisplacement);
   TEXTURE2D_ARRAY(_FFTNormal); SAMPLER(sampler_FFTNormal);
   float _FFTPatch, _FFTFrames, _FFTPeriod, _FFTStrength, _FFTDetail, _FFTFoam;
   half4 _SSSColor; half _SSSStrength, _Caustics;

   // Looping FFT frame pair and blend factor for a given time.
   void FFTFrame(float time, out float f0, out float f1, out float blend)
   {
    float fr = frac(time / _FFTPeriod) * _FFTFrames;
    f0 = floor(fr); f1 = fmod(f0 + 1.0, _FFTFrames); blend = fr - f0;
   }
   // Second band: smaller tile, rotated and time-shifted so the two never line up.
   float2 BandB(float2 xz) { return float2(xz.x * 0.8 - xz.y * 0.6, xz.x * 0.6 + xz.y * 0.8) / (_FFTPatch * 0.271) + 0.37; }

   struct Attributes { float4 positionOS:POSITION; };
   struct Varyings
   {
    float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1;
    float4 waveData:TEXCOORD2; // x swell crest, y surf crest, z breaking, w seabed depth
    float2 fftData:TEXCOORD3;  // x FFT height (m), y FFT jacobian
    float2 restXZ:TEXCOORD4;   // undisplaced position: textures and foam must not stretch with the waves
   };

   // Sine-free hashes (Hoskins): sin() of large arguments loses precision and turns noise into blocks.
   float Hash1(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }
   float2 Hash2(float2 p) { float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973)); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.xx + p3.yz) * p3.zy); }
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
    v.restXZ = p.xz;
    float camFade = 1.0 - saturate(distance(p.xz, _WorldSpaceCameraPos.xz) / _WaveFade);
    // Vertex waves only on the fine coast grid (the seabed rectangle plus a margin);
    // the coarse far skirt stays flat and relies on normal detail.
    float2 rectCenter = _ChronoSeabedRect.xy + _ChronoSeabedRect.zw * 0.5;
    float2 outside = abs(p.xz - rectCenter) - _ChronoSeabedRect.zw * 0.5;
    camFade *= 1.0 - smoothstep(-25.0, -5.0, max(outside.x, outside.y));
    float t = _Time.y * _WaveSpeed;

    float bed = Seabed(p.xz);
    float depth = max(-bed, 0.0);
    // Shore direction: uphill on the seabed.
    float e = 6.0;
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

    // Surf from shallow-water physics. Waves keep their period T while slowing to c = sqrt(g*h);
    // on a slope s the travel time from depth h to the shoreline is 2*sqrt(h)/(s*sqrt(g)), so the
    // phase spacing (and crests bunching toward the beach) follows from the real seabed slope.
    float2 swellDir = float2(cos(radians(_WaveDirection)), sin(radians(_WaveDirection)));
    float along = dot(p.xz, float2(-swellDir.y, swellDir.x));
    float bedSlope = clamp(length(grad) / (2.0 * e), 0.03, 0.45);
    float travel = 2.0 * sqrt(depth) / (bedSlope * 3.132);
    // Sets: groups of bigger waves every ~7 periods, drifting along the beach.
    float sets = 0.62 + 0.38 * sin(TWO_PI * _Time.y / (_SurfPeriod * 7.0) + along * 0.012);
    float waveH = _SurfHeight * sets;
    float phase = frac((_Time.y + travel) / _SurfPeriod + along * 0.0025);
    // Shoaling (Green's law H ~ h^-1/4) until the breaker index H/h is reached; after that the
    // broken wave is a bore whose height is a fraction of the local depth.
    float breakDepth = waveH / _BreakerIndex;
    float shoaled = waveH * pow(_SurfStartDepth / max(depth, breakDepth), 0.25) * 0.55;
    float unbroken = smoothstep(breakDepth * 0.85, breakDepth * 1.15, depth);
    float boreH = depth * 0.8;
    float growIn = 1.0 - smoothstep(_SurfStartDepth * 0.6, _SurfStartDepth, depth);
    // Unbroken: peaked crest with a steepening front. Broken: a turbulent front with a long tail.
    float sPeak = phase;
    // Asymmetric crest: long back, front steepening into a near-vertical face near the break.
    float frontW = lerp(0.03, 0.12, saturate((depth - breakDepth) / 3.0));
    float backW = lerp(0.3, 0.35, saturate((depth - breakDepth) / 3.0));
    float peak = sPeak < 0.12 ? pow(saturate(1.0 - (0.12 - sPeak) / frontW), 1.6) : pow(saturate(1.0 - (sPeak - 0.12) / backW), 2.2);
    float boreShape = sPeak < 0.06 ? sPeak / 0.06 : exp(-(sPeak - 0.06) * 7.0);
    float amp = lerp(boreH, shoaled, unbroken) * growIn * camFade;
    float profile = lerp(boreShape, peak, unbroken);
    float rise = amp * profile;
    disp.y += rise;
    // Plunging lip: just before and at the break the crest is thrown forward over the trough.
    float nearBreak = smoothstep(breakDepth * 2.0, breakDepth, depth) * unbroken;
    disp.xz += shoreDir * amp * (0.25 * (1.0 - unbroken) + 0.9 * nearBreak * pow(peak, 3.0)) * profile;
    disp.y += amp * 0.35 * nearBreak * pow(peak, 4.0);
    disp.y += (1.0 - unbroken) * growIn * camFade * 0.18 * exp(-sPeak * 3.0) * saturate(depth / 0.2);
    // White water: the plunging/spilling crest at the break point, then the bore and its foam tail.
    float breaking = (1.0 - unbroken) * saturate(exp(-sPeak * 5.0) * 1.3) * growIn * saturate(depth / 0.15);

    // Swash: the last bore runs up the sand as a thin sheet and drains back. The sheet level rises
    // fast when the bore arrives and drains slowly; the mesh over dry sand stays hidden under terrain.
    float shorePhase = frac(_Time.y / _SurfPeriod + along * 0.0025);
    float uprush = shorePhase < 0.18 ? smoothstep(0.0, 0.18, shorePhase) : exp(-(shorePhase - 0.18) * 3.2);
    float swashLevel = _RunUp * sets * uprush * camFade - 0.06;
    float nearShore = 1.0 - smoothstep(0.0, 1.2, depth);
    float onBeach = step(bed, 0.0) < 0.5 ? 1.0 : nearShore;
    float surfaceY = max(disp.y, swashLevel);
    disp.y = lerp(disp.y, surfaceY, onBeach);
    float swash = saturate((swashLevel - bed) / 0.15) * (1.0 - smoothstep(0.0, 0.8, depth));

    // Surf slope along the shore direction, folded into the normal frame.
    float stepM = 0.5;
    float depthAhead = max(-Seabed(p.xz + shoreDir * stepM), 0.0);
    float phaseAhead = frac((_Time.y + 2.0 * sqrt(depthAhead) / (bedSlope * 3.132)) / _SurfPeriod + along * 0.0025);
    float unbrokenA = smoothstep(breakDepth * 0.85, breakDepth * 1.15, depthAhead);
    float peakA = pow(saturate(1.0 - abs(phaseAhead - 0.12) / lerp(0.12, 0.35, saturate((depthAhead - breakDepth) / 3.0))), 2.4);
    float boreA = phaseAhead < 0.06 ? phaseAhead / 0.06 : exp(-(phaseAhead - 0.06) * 7.0);
    float riseAhead = lerp(depthAhead * 0.8, waveH * pow(_SurfStartDepth / max(depthAhead, breakDepth), 0.25) * 0.55, unbrokenA) * growIn * camFade * lerp(boreA, peakA, unbrokenA);
    float slope = (riseAhead - rise) / stepM;
    tangent.y += slope * shoreDir.x;
    binormal.y += slope * shoreDir.y;

    // Baked FFT displacement: two bands of the same looping patch, calmed in the surf zone
    // (the breaker model owns the shallows) and faded with camera distance like the swell.
    float f0, f1, fb; FFTFrame(_Time.y, f0, f1, fb);
    float2 uvA = p.xz / _FFTPatch;
    float4 dA = lerp(SAMPLE_TEXTURE2D_ARRAY_LOD(_FFTDisplacement, sampler_FFTDisplacement, uvA, f0, 0), SAMPLE_TEXTURE2D_ARRAY_LOD(_FFTDisplacement, sampler_FFTDisplacement, uvA, f1, 0), fb);
    float g0, g1, gb; FFTFrame(_Time.y * 1.37 + 5.3, g0, g1, gb);
    float2 uvB = BandB(p.xz);
    float4 dB = lerp(SAMPLE_TEXTURE2D_ARRAY_LOD(_FFTDisplacement, sampler_FFTDisplacement, uvB, g0, 0), SAMPLE_TEXTURE2D_ARRAY_LOD(_FFTDisplacement, sampler_FFTDisplacement, uvB, g1, 0), gb);
    float fftAmount = _FFTStrength * camFade * saturate((depth - breakDepth * 1.5) / 3.0);
    float3 fftDisp = (dA.xyz + dB.xyz * _FFTDetail * 0.271) * fftAmount;
    disp += fftDisp;
    v.fftData = float2(fftDisp.y, lerp(1.0, min(dA.w, dB.w), saturate(fftAmount)));
    p += disp;
    v.positionWS = p;
    v.normalWS = normalize(cross(binormal, tangent));
    v.waveData = float4(swellCrest, profile * saturate(amp / 0.08) * unbroken, max(breaking, swash * uprush * 0.8), depth);
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

   // Surf state at a point, recomputed per pixel for foam (x unbroken 0..1, y wave phase,
   // z whitewater, w swash sheet foam). Mirrors the vertex surf model.
   float4 SurfFoamState(float2 xz, out float2 shoreDirOut)
   {
    float bed = Seabed(xz);
    float depth = max(-bed, 0.0);
    float e = 6.0;
    float2 grad = float2(Seabed(xz + float2(e, 0)) - Seabed(xz - float2(e, 0)), Seabed(xz + float2(0, e)) - Seabed(xz - float2(0, e)));
    float2 shoreDir = length(grad) > 1e-4 ? normalize(grad) : float2(0, 1);
    shoreDirOut = shoreDir;
    float2 swellDir = float2(cos(radians(_WaveDirection)), sin(radians(_WaveDirection)));
    float along = dot(xz, float2(-swellDir.y, swellDir.x));
    float bedSlope = clamp(length(grad) / (2.0 * e), 0.03, 0.45);
    float travel = 2.0 * sqrt(depth) / (bedSlope * 3.132);
    float sets = 0.62 + 0.38 * sin(TWO_PI * _Time.y / (_SurfPeriod * 7.0) + along * 0.012);
    float waveH = _SurfHeight * sets;
    float phase = frac((_Time.y + travel) / _SurfPeriod + along * 0.0025);
    float breakDepth = waveH / _BreakerIndex;
    float unbroken = smoothstep(breakDepth * 0.85, breakDepth * 1.15, depth);
    float growIn = 1.0 - smoothstep(_SurfStartDepth * 0.6, _SurfStartDepth, depth);
    float white = (1.0 - unbroken) * exp(-phase * 2.5) * growIn * saturate(depth / 0.12);
    // Impact: where the lip lands the water explodes into dense foam that spreads shoreward.
    float impactZone = smoothstep(breakDepth * 1.3, breakDepth * 0.9, depth) * smoothstep(breakDepth * 0.35, breakDepth * 0.7, depth);
    white += impactZone * exp(-phase * 6.0) * 1.5 * growIn;
    // A thin crest of foam starts just before the break as the front steepens.
    // Spilling crest: as the wave nears the break, white water starts at the top of the crest and
    // cascades down the front face; the closer to breaking, the further down it reaches.
    float closeness = smoothstep(breakDepth * 2.2, breakDepth, depth) * unbroken;
    float crestTop = phase >= 0.12 ? exp(-(phase - 0.12) * 60.0) : exp(-(0.12 - phase) / max(0.006 + closeness * 0.05, 1e-3));
    float spillSections = smoothstep(0.3, 0.7, ValueNoise(float2(along * 0.08, floor((_Time.y + travel) / _SurfPeriod) * 2.3)));
    white += closeness * crestTop * lerp(0.5, 1.4, spillSections);
    float shorePhase = frac(_Time.y / _SurfPeriod + along * 0.0025);
    float uprush = shorePhase < 0.18 ? smoothstep(0.0, 0.18, shorePhase) : exp(-(shorePhase - 0.18) * 3.2);
    float swashLevel = _RunUp * sets * uprush - 0.06;
    // Foam lace left on the draining sheet: strongest at its leading edge and fading as it drains.
    float sheet = saturate((swashLevel - bed) / 0.2) * (1.0 - smoothstep(0.0, 0.6, depth));
    float lace = sheet * lerp(1.0, 0.45, saturate((shorePhase - 0.18) * 2.0));
    // Residual foam patches left between bands: they burst away over the period instead of fading.
    white += (1.0 - unbroken) * growIn * 0.55 * exp(-phase * 0.8);
    return float4(unbroken, phase, saturate(white), lace);
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
    // Distant terrain switches to coarse LOD triangles; color the water from the smooth baked
    // seabed there and keep the exact depth buffer only up close (shoreline, refraction).
    float smoothDepth = lerp(depth, v.waveData.w, saturate((dist - 15) / 40));
    float smoothRay = smoothDepth / max(view.y, 0.05);
    float t = _Time.y;
    float2 p = v.restXZ;
    float2 pl = p - (_ChronoSeabedRect.xy + _ChronoSeabedRect.zw * 0.5);

    // Normals: vertex waves + large swell layers that never fade (far sea keeps moving) + chop up close.
    float3 n = normalize(v.normalWS);
    float2 slope = NoiseSlope(p * 0.045, float2(t * 0.06, t * 0.035)) * 1.1
                 + NoiseSlope(p * 0.11, float2(-t * 0.05, t * 0.09)) * 0.6;
    slope *= _FarSwell * (0.6 + 0.4 * saturate(dist / 300));
    slope += SwellLines(p, t * _WaveSpeed, radians(_WaveDirection), dist) * _SwellLines * saturate(v.waveData.w / 4.0);
    if (high)
    {
     float nearChop = saturate(1 - dist / 60);
     // Third band: the same FFT patch at ~6 m tile for close-up chop (continuous, unlike value noise).
     float c0, c1, cb; FFTFrame(t * 2.3 + 9.1, c0, c1, cb);
     float2 uvC = float2(p.x * -0.5 - p.y * 0.866, p.x * 0.866 - p.y * 0.5) / (_FFTPatch * 0.093) + 0.61;
     half2 chop = lerp(SAMPLE_TEXTURE2D_ARRAY(_FFTNormal, sampler_FFTNormal, uvC, c0).xy, SAMPLE_TEXTURE2D_ARRAY(_FFTNormal, sampler_FFTNormal, uvC, c1).xy, cb) * 2 - 1;
     slope += chop * _Ripples * nearChop;
    }
    float f0, f1, fb; FFTFrame(t, f0, f1, fb);
    float g0, g1, gb; FFTFrame(t * 1.37 + 5.3, g0, g1, gb);
    float2 uvA = p / _FFTPatch, uvB = BandB(p);
    half4 nA = lerp(SAMPLE_TEXTURE2D_ARRAY(_FFTNormal, sampler_FFTNormal, uvA, f0), SAMPLE_TEXTURE2D_ARRAY(_FFTNormal, sampler_FFTNormal, uvA, f1), fb);
    half4 nB = lerp(SAMPLE_TEXTURE2D_ARRAY(_FFTNormal, sampler_FFTNormal, uvB, g0), SAMPLE_TEXTURE2D_ARRAY(_FFTNormal, sampler_FFTNormal, uvB, g1), gb);
    float fftOpen = _FFTStrength * saturate((v.waveData.w - _BreakDepth) / 3.0);
    // Second band slopes are not scaled down with its tile: same steepness at a smaller size.
    float2 fftSlope = ((nA.xy * 2 - 1) + (nB.xy * 2 - 1) * _FFTDetail) * fftOpen;
    slope += fftSlope;
    // Interactive waves: wakes, splashes and droplet impacts from the wave simulation.
    float2 rippleGrad; float rippleFoam;
    CW_SampleRipple(p, rippleGrad, rippleFoam);
    slope -= rippleGrad * 0.65;
    if (dist < 60.0) slope -= CW_RainSlope(p) * saturate(1.0 - dist / 60.0);
    n = normalize(n + float3(slope.x, 0, slope.y));

    half3 bottom;
    if (high)
    {
     float2 uv = screenUV + n.xz * _Refraction * saturate(depth * 2);
     if (LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams) < surfaceEye) uv = screenUV;
     bottom = SampleSceneColor(uv);
    }
    else bottom = SampleSceneColor(screenUV);

    {
     Light sunC = GetMainLight();
     float3 toBottom = -view * (dist * bottomEye / max(surfaceEye, 1e-3));
     float3 bottomWS = _WorldSpaceCameraPos + toBottom;
     float below = max(v.positionWS.y - bottomWS.y, 0);
     float2 cuv = (bottomWS.xz + sunC.direction.xz / max(sunC.direction.y, 0.2) * below) / (_FFTPatch * 0.5);
     half c = lerp(SAMPLE_TEXTURE2D_ARRAY(_FFTNormal, sampler_FFTNormal, cuv, f0).a, SAMPLE_TEXTURE2D_ARRAY(_FFTNormal, sampler_FFTNormal, cuv, f1).a, fb);
     half c2 = SAMPLE_TEXTURE2D_ARRAY(_FFTNormal, sampler_FFTNormal, cuv * 1.7 + 0.21, g0).a;
     half caustic = pow(saturate(c * 6.0 * c2 * 6.0 / 3.0), 2.0);
     // Fade with depth (light spreads) and at the very edge (too thin to focus).
     half cFade = saturate(below / 0.3) * exp(-below / 3.5) * saturate(sunC.direction.y * 2);
     bottom *= 1 + caustic * _Caustics * cFade * sunC.color * half3(1.0, 0.98, 0.9);
    }

    Light sun = GetMainLight(TransformWorldToShadowCoord(v.positionWS));
    half3 waterColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(smoothDepth / _DepthRange));
    // Light passing through thin wave crests turns them greener and brighter.
    waterColor = lerp(waterColor, _ShallowColor.rgb * 1.25, saturate(v.waveData.y * 0.6 + v.waveData.x * 0.25));
    half visibility = exp(-lerp(rayDepth, smoothRay, saturate((dist - 15) / 40)) / _Clarity);
    // Breaking waves stir sand into suspension: the surf zone turns opaque grey-green.
    half stirred = saturate(1.0 - v.waveData.w / (_SurfHeight * 2.6)) * saturate(v.waveData.w / 0.15);
    waterColor = lerp(waterColor, half3(0.22, 0.32, 0.27), stirred * 0.75);
    visibility *= 1.0 - stirred * 0.65;
    half3 lit = waterColor * (sun.color * saturate(sun.direction.y) * 0.55 + SKY_ZENITH * 0.6);
    half3 color = lerp(lit, bottom * lerp(_ShallowColor.rgb * 1.4, 1, 0.45), visibility);

    // Far away the surface is a blur of tilted facets: average reflectance drops below the
    // smooth-surface Fresnel, which keeps the distant sea a deep blue instead of milky.
    {
     float crestH = saturate(v.fftData.x / 0.4 + v.waveData.y * 0.8 + v.waveData.x * 0.3);
     half back = pow(saturate(dot(view, -sun.direction) * 0.6 + 0.4), 4.0);
     half side = pow(saturate(1 - abs(dot(n, view))), 2.0) * 0.35;
     color += _SSSColor.rgb * sun.color * (back + side) * crestH * _SSSStrength * saturate(sun.direction.y + 0.2) * 0.5;
     // The standing face of an unbroken wave is thin near its top: seen from the shore it glows green.
     half faceToViewer = saturate(dot(normalize(v.normalWS), view));
     half thinTop = saturate(v.waveData.y * 1.4);
     color = lerp(color, _SSSColor.rgb * (sun.color * 0.9 + SKY_ZENITH * 0.6), thinTop * faceToViewer * 0.75 * _SSSStrength);
    }
    half fresnel = (0.02 + 0.98 * pow(1 - saturate(dot(n, view)), 5)) * lerp(1.0, 0.62, saturate(dist / 350));
    float3 rd = reflect(-view, n);
    half3 refl = lerp(SKY_HORIZON, SKY_ZENITH, pow(saturate(rd.y), 0.45));
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
    float2 shoreDirF;
    float4 surf = SurfFoamState(p, shoreDirF);
    // Foam forms streaks along the wave crest: stretch the pattern across the shore direction.
    // x: along the beach, y: across it (foam features are stretched along the crest).
    float2 swellAxis = float2(cos(radians(_WaveDirection)), sin(radians(_WaveDirection)));
    float2 acrossAlong = float2(dot(pl, float2(-swellAxis.y, swellAxis.x)), dot(pl, swellAxis));
    float2 fq = float2(acrossAlong.x * 0.42, acrossAlong.y * 1.0);
    float bubbles = 1 - Cells(fq * 6.0 + t * float2(0.06, 0.02)) * 0.4 - Cells(fq * 15.0 - t * 0.05) * 0.35 - Cells(fq * 34.0 + t * 0.08) * 0.25;
    float wisps = ValueNoise(fq * 0.9 + t * 0.07) * 0.55 + ValueNoise(fq * 2.7 - t * 0.12) * 0.45;
    float holes = ValueNoise(fq * 4.3 + t * 0.2);
    float pattern = bubbles * 0.45 + wisps * 0.55 - (1 - holes) * 0.3;
    float rimBreak = 0.45 + 0.55 * ValueNoise(pl * 0.18 + float2(t * 0.03, 0));
    float rim = (1 - saturate(depth / _ShoreFoamDepth)) * rimBreak * 0.7;
    float breakFoam = surf.z * _BreakFoam * (0.65 + 0.5 * wisps);
    float swashLace = surf.w * _BreakFoam * 0.55 * (0.4 + 0.6 * holes);
    float trail = 0;
    // Whitecaps drifting on the open sea, appearing and fading.
    float capField = smoothstep(0.7, 0.86, ValueNoise(pl * 0.035 + float2(t * 0.025, t * 0.015)));
    float capPulse = smoothstep(0.55, 0.85, ValueNoise(pl * 0.18 + float2(-t * 0.3, t * 0.12)));
    float caps = capField * capPulse * _Whitecaps * saturate((dist - 30) / 120) * saturate(v.waveData.w / 6);
    // Whitecaps where the FFT waves fold over (accumulated foam baked with build-up and decay).
    float fftFoam = saturate((nA.z + nB.z * 0.6) * 0.7 * _FFTFoam * fftOpen) * saturate(1 - dist / 220);
    float foamMask = saturate(rim + swashLace + caps + fftFoam + rippleFoam * 1.4);
    float coverage = foamMask * 0.62;
    float foam = smoothstep(1.0 - coverage, 1.0 - coverage + 0.35, pattern) * saturate(foamMask * 2.5) * 0.9;
    // Dense core where the bore is freshly breaking.
    // Broken water (the bore) is a broad band of churned foam with frayed, uneven density,
    // not a thresholded pattern: density follows the whitewater amount and the bubble texture.
    float churn = saturate(0.2 + bubbles * 0.5 + wisps * 0.5 - (1 - holes) * 0.35);
    // The wave doesn't break evenly along the beach: sections peel, others stay glassy.
    float sections = smoothstep(0.25, 0.65, ValueNoise(float2(acrossAlong.x * 0.07, t * 0.04)));
    float coreFoam = ValueNoise(float2(acrossAlong.x * 0.9, acrossAlong.y * 2.2) + t * 0.3);
    // Frayed edge: density falls off through the bubble pattern instead of a clean line.
    // Whitewater keeps its bubbly structure: the amount of broken water decides how much of the
    // bubble pattern survives (bubbles burst rather than fade), so bands break into clumps and lace.
    float boreAmount = saturate(breakFoam * 1.15 * lerp(0.5, 1.0, sections)) * (0.55 + 0.45 * coreFoam);
    float clumps = churn * 0.6 + bubbles * 0.4;
    float bore = smoothstep(1.0 - boreAmount, 1.0 - boreAmount + 0.22, clumps);
    foam = max(foam, bore * 0.97);
    foam = max(foam, caps * 0.8 * saturate(dist / 200));
    // Foam is lumpy: shade it from a fake height (bubble pattern) so clumps catch light and
    // crevices go blue-grey, then add tiny bright sparkles of wet bubbles.
    float fh = bubbles * 0.6 + wisps * 0.4;
    float fhx = Cells(fq * 6.0 + float2(0.08, 0) + t * float2(0.06, 0.02)) - Cells(fq * 6.0 - float2(0.08, 0) + t * float2(0.06, 0.02));
    // Thicker foam (more coverage) is more lumpy and self-shadowed; thin lace stays bright.
    float fineLump = 1 - Cells(fq * 22.0 + t * 0.1);
    half foamLight = saturate(0.35 + fh * 0.55 + fineLump * 0.35 - fhx * 1.1);
    half3 foamLit = lerp(half3(0.4, 0.52, 0.62), half3(1.0, 1.0, 1.0), foamLight) * 0.9;
    half sparkle = step(0.985, Hash1(floor(pl * 9.0) + floor(t * 12.0))) * foam;
    color = lerp(color, _FoamColor.rgb * foamLit * (sun.color * saturate(sun.direction.y) * 0.9 + SKY_ZENITH * 0.55) + sparkle * sun.color * 0.6, foam);

    color = ApplyJungleAtmosphere(color, v.positionWS, sun.color, sun.direction);
    half alpha = saturate(depth / _EdgeFade);
    return half4(color, alpha * lerp(1, 0.9, foam));
   }
   ENDHLSL
  }
 }
}
