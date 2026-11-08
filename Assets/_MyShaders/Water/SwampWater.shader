Shader "CHRONO WILD/Water/Swamp Water"
{
 // Stagnant swamp water. Murky water reads pale and desaturated rather than black: light bounces
 // off suspended sediment near the surface. Layers, back to front:
 //  - refracted bottom absorbed by tannin-stained water plus pale green-brown sediment scatter
 //  - screen-space reflections with a probe fallback, Fresnel-weighted
 //  - floating water-fern mats that drift with the wind and gather along the shallow edges
 //  - insect ripple rings that appear at random points, the "life" detail players notice
 // The swamp extent comes from the island biome map (blue channel), so the surface fades out
 // softly where the swamp ends.
 Properties
 {
  _Absorption("Absorption per Meter (RGB)",Vector)=(5,3.6,2.6,0)
  _ScatterColor("Suspended Sediment Color",Color)=(0.13,0.14,0.075,1)
  _Turbidity("Turbidity",Range(0,1))=0.8
  _Refraction("Refraction Strength",Range(0,0.1))=0.02
  _Roughness("Surface Roughness",Range(0,0.3))=0.06
  _RippleStrength("Ripple Strength",Range(0,1))=0.18
  _RippleScale("Ripple Scale",Float)=0.9
  _RippleSpeed("Ripple Speed",Float)=0.08
  _ReflectionStrength("Reflection Strength",Range(0,1))=0.75
  _SSRSteps("SSR Steps",Range(8,48))=32
  _SSRDistance("SSR Max Distance (m)",Float)=30
  _SSRThickness("SSR Thickness (m)",Float)=1
  _ProbeDim("Probe Reflection Strength",Range(0,1))=0.8
  _ShoreSoftness("Shore Softness (m)",Range(0.005,0.3))=0.06
  _FernColor("Water Fern Color",Color)=(0.2,0.3,0.06,1)
  _FernColorB("Water Fern Color (old)",Color)=(0.36,0.25,0.07,1)
  _FernCoverage("Water Fern Coverage",Range(0,1))=0.5
  _FernScale("Water Fern Patch Size (m)",Float)=9
  _FernDrift("Water Fern Drift (m/s)",Float)=0.03
  _InsectRipples("Insect Ripples",Range(0,1))=0.6
  [Toggle] _UseBiomeExtent("Limit To Swamp Area",Float)=1
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+5"}
  Pass
  {
   Name "SwampWater" Tags {"LightMode"="UniversalForward"}
   Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Back
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vertex
   #pragma fragment Fragment
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
   #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
   #include "Assets/_MyShaders/Environment/JungleAtmosphere.hlsl"
   #include "Assets/_MyShaders/Water/RippleSample.hlsl"

   CBUFFER_START(UnityPerMaterial)
   float4 _Absorption; half4 _ScatterColor; half _Turbidity; half _Refraction; half _Roughness;
   half _RippleStrength; float _RippleScale; float _RippleSpeed; half _ReflectionStrength;
   float _SSRSteps; float _SSRDistance; float _SSRThickness; half _ProbeDim; half _ShoreSoftness;
   half4 _FernColor; half4 _FernColorB; half _FernCoverage; float _FernScale; float _FernDrift; half _InsectRipples; half _UseBiomeExtent;
   CBUFFER_END
   float _ChronoQuality;
   TEXTURE2D(_CW_BiomeMap); SAMPLER(sampler_CW_BiomeMap);
   float4 _CW_BiomeParams;
   float4 _CW_Wind;
   float _CW_EnvDim;

   struct Attributes { float4 positionOS:POSITION; half4 color:COLOR; };
   struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half edge:TEXCOORD1; };

   Varyings Vertex(Attributes a)
   {
    Varyings v;
    v.positionWS = TransformObjectToWorld(a.positionOS.xyz);
    v.positionCS = TransformWorldToHClip(v.positionWS);
    v.edge = a.color.a;
    return v;
   }

   float2 Hash2(float2 p)
   {
    p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
    return frac(sin(p) * 43758.5453) * 2 - 1;
   }

   float3 NoiseD(float2 p)
   {
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3 - 2 * f), du = 6 * f * (1 - f);
    float2 ga = Hash2(i), gb = Hash2(i + float2(1, 0)), gc = Hash2(i + float2(0, 1)), gd = Hash2(i + float2(1, 1));
    float va = dot(ga, f), vb = dot(gb, f - float2(1, 0)), vc = dot(gc, f - float2(0, 1)), vd = dot(gd, f - float2(1, 1));
    float v = va + u.x * (vb - va) + u.y * (vc - va) + u.x * u.y * (va - vb - vc + vd);
    float2 d = ga + u.x * (gb - ga) + u.y * (gc - ga) + u.x * u.y * (ga - gb - gc + gd) + du * (u.yx * (va - vb - vc + vd) + float2(vb, vc) - va);
    return float3(v, d);
   }

   // Expanding rings where insects touch the surface; returns a slope to add to the normal.
   float2 InsectRings(float2 p)
   {
    const float cell = 2.6;
    float2 id = floor(p / cell);
    float2 rnd = Hash2(id) * 0.5 + 0.5;
    float2 center = (id + 0.3 + rnd * 0.4) * cell;
    float life = frac(_Time.y * (0.25 + rnd.x * 0.3) + rnd.y * 7.0);
    float2 d = p - center;
    float dist = length(d);
    float radius = life * 0.9;
    float ring = exp(-pow((dist - radius) * 14.0, 2.0)) * (1.0 - life) * step(0.35, rnd.x);
    float wave = sin((dist - radius) * 40.0) * ring;
    return d / max(dist, 1e-3) * wave * 0.35;
   }

   half3 SurfaceNormal(float2 p, out float2 slope)
   {
    float t = _Time.y * _RippleSpeed;
    float2 wind = _CW_Wind.xy * t * 4.0;
    slope = NoiseD(p * _RippleScale + wind).yz * 0.6
          + NoiseD(p * _RippleScale * 2.7 - float2(t * 0.8, -t * 1.1)).yz * 0.3
          + NoiseD(p * _RippleScale * 6.3 + float2(-t * 1.7, t * 0.9)).yz * 0.1;
    slope *= _RippleStrength * 0.25;
    slope += InsectRings(p) * _InsectRipples;
    float2 rippleGrad; float rippleFoam;
    CW_SampleRipple(p, rippleGrad, rippleFoam);
    slope += rippleGrad + CW_RainSlope(p);
    return normalize(half3(-slope.x, 1, -slope.y));
   }

   float RayDepthDiff(float3 p, out float2 uv)
   {
    float4 clip = TransformWorldToHClip(p);
    float4 screen = ComputeScreenPos(clip);
    uv = screen.xy / max(screen.w, 1e-4);
    return clip.w - LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
   }

   half4 TraceReflection(float3 positionWS, float3 dirWS, float jitter)
   {
    float steps = _SSRSteps;
    float stepLength = _SSRDistance / (steps * (1 + steps * 0.02));
    float3 prev = positionWS + dirWS * 0.05;
    float3 p = prev;
    [loop]
    for (int i = 0; i < 48; i++)
    {
     if (i >= (int)steps) break;
     prev = p;
     p += dirWS * stepLength * (1 + i * 0.04) * (i == 0 ? jitter + 0.5 : 1);
     float2 uv;
     float diff = RayDepthDiff(p, uv);
     if (any(uv < 0) || any(uv > 1)) break;
     if (diff > 0 && diff < _SSRThickness * (1 + i * 0.05))
     {
      float3 a = prev, b = p;
      [unroll]
      for (int k = 0; k < 5; k++)
      {
       float3 m = (a + b) * 0.5;
       float2 muv;
       if (RayDepthDiff(m, muv) > 0) b = m; else a = m;
      }
      RayDepthDiff(b, uv);
      float2 edge = smoothstep(0, 0.1, uv) * smoothstep(0, 0.1, 1 - uv);
      return half4(SampleSceneColor(uv), edge.x * edge.y * saturate(1.2 - i / steps));
     }
    }
    return 0;
   }

   // Floating water fern: clustered rosettes (cellular pattern) inside drifting patches.
   half FernMask(float2 p, float depth, out half age, out half leafShade)
   {
    float2 drift = _CW_Wind.xy * _Time.y * _FernDrift;
    float2 q = (p - drift) / _FernScale;
    float patch = NoiseD(q).x * 0.6 + NoiseD(q * 2.3 + 4.1).x * 0.3 + NoiseD(q * 5.1 - 2.7).x * 0.1;
    // Mats gather a little more in the calm shallows, but stay patchy instead of forming a rim.
    float shallow = (1.0 - saturate(depth / 0.5)) * (NoiseD(q * 4.3 + 1.7).x * 0.5 + 0.5);
    float threshold = lerp(0.35, -0.25, saturate(_FernCoverage + shallow * 0.25));
    half cover = smoothstep(threshold, threshold + 0.06, patch);
    // Rosettes: small rounded leaves a few centimetres across, two rotated, jittered layers so
    // no grid shows, with per-leaf brightness variation.
    float2 pd = p - drift;
    float2 r1 = float2(pd.x * 0.8 + pd.y * 0.6, -pd.x * 0.6 + pd.y * 0.8) * 11.0;
    float2 r2 = float2(pd.x * 0.34 - pd.y * 0.94, pd.x * 0.94 + pd.y * 0.34) * 7.3 + 0.37;
    float2 i1 = floor(r1), i2 = floor(r2);
    float l1 = 1.0 - saturate(length(frac(r1) - 0.5 - Hash2(i1) * 0.42) * 2.3);
    float l2 = 1.0 - saturate(length(frac(r2) - 0.5 - Hash2(i2) * 0.42) * 2.0);
    float leaf = max(l1 * (0.75 + 0.25 * Hash2(i1 + 3.1).x), l2 * (0.7 + 0.3 * Hash2(i2 + 7.7).y));
    leafShade = (half)leaf;
    age = (half)saturate(NoiseD(q * 3.7 + 9.0).x * 1.5 + 0.5);
    // Ragged mat edges: leaves drop out first near the boundary.
    return cover * saturate((cover - 0.15) * 4.0 + leaf * 0.6);
   }

   half4 Fragment(Varyings v) : SV_Target
   {
    half extent = 1.0h;
    if (_CW_BiomeParams.x > 0 && _UseBiomeExtent > 0.5)
     extent = SAMPLE_TEXTURE2D(_CW_BiomeMap, sampler_CW_BiomeMap, v.positionWS.xz / _CW_BiomeParams.xy).b;
    clip(extent - 0.02);

    float2 screenUV = GetNormalizedScreenSpaceUV(v.positionCS);
    float surfaceEye = -TransformWorldToView(v.positionWS).z;
    float bottomEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
    float3 view = GetWorldSpaceNormalizeViewDir(v.positionWS);
    float rayDepth = max(0, bottomEye - surfaceEye) * (length(_WorldSpaceCameraPos - v.positionWS) / max(surfaceEye, 1e-3));
    float depth = rayDepth * saturate(view.y + 0.05);

    float2 slope;
    half3 n = SurfaceNormal(v.positionWS.xz, slope);
    half calm = saturate(depth / 0.08);
    n = normalize(lerp(half3(0, 1, 0), n, calm));

    float2 refrUV = screenUV + n.xz * _Refraction * calm * step(0.5, _ChronoQuality);
    if (LinearEyeDepth(SampleSceneDepth(refrUV), _ZBufferParams) < surfaceEye) refrUV = screenUV;
    half3 bottom = SampleSceneColor(refrUV);

    float pathLength = rayDepth + depth;
    half3 transmittance = exp(-_Absorption.rgb * pathLength * (0.4 + _Turbidity));
    float4 shadowCoord = TransformWorldToShadowCoord(v.positionWS);
    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
     shadowCoord = ComputeScreenPos(v.positionCS);
    #endif
    Light sun = GetMainLight(shadowCoord);
    half sunVis = sun.shadowAttenuation;
    // Sediment is lit by the sky and the sun that reaches it, so shaded water reads darker.
    half3 ambient = SampleSH(half3(0, 1, 0));
    half3 scatterLight = ambient + sun.color * sunVis * 0.35h;
    half3 water = bottom * transmittance + _ScatterColor.rgb * scatterLight * (1 - transmittance);

    half fresnel = 0.02 + 0.98 * pow(1 - saturate(dot(n, view)), 5);
    float3 reflDir = reflect(-view, n);
    half3 probe = GlossyEnvironmentReflection(reflDir, v.positionWS, _Roughness, 1, screenUV) * _ProbeDim * (1.0 - _CW_EnvDim);
    float jitter = InterleavedGradientNoise(v.positionCS.xy, 0);
    half4 ssr = _ChronoQuality > 0.5 ? TraceReflection(v.positionWS, reflDir, jitter) : half4(0, 0, 0, 0);
    half3 reflection = lerp(probe, ssr.rgb, ssr.a) * _ReflectionStrength;

    half3 h = normalize(sun.direction + view);
    half spec = pow(saturate(dot(n, h)), 500) * 5 + pow(saturate(dot(n, h)), 70) * 0.25;
    half3 color = lerp(water, reflection, fresnel) + sun.color * spec * sunVis * calm;

    // Floating fern mats sit on top: matte, lit like leaves, hiding the reflection.
    half age, leafShade;
    half fern = FernMask(v.positionWS.xz, depth, age, leafShade);
    // Anything moving through the mat pushes the floating ferns aside and leaves pale froth.
    float2 wakeGrad; float wakeFoam;
    CW_SampleRipple(v.positionWS.xz, wakeGrad, wakeFoam);
    fern *= 1.0 - saturate(wakeFoam * 3.0);
    float frothCells = NoiseD(v.positionWS.xz * 5.0).x * 0.6 + NoiseD(v.positionWS.xz * 13.0).x * 0.4;
    half froth = saturate(wakeFoam * 1.3) * smoothstep(-0.12, 0.28, frothCells);
    color = lerp(color, (sun.color * 0.3 + SampleSH(half3(0, 1, 0))) * half3(0.62, 0.62, 0.52), froth * 0.5);
    half3 fernAlbedo = lerp(_FernColor.rgb, _FernColorB.rgb, age) * lerp(0.65h, 1.15h, leafShade);
    half3 fernN = normalize(half3(slope.x * -0.5, 1, slope.y * -0.5));
    half ndl = saturate(dot(fernN, sun.direction)) * 0.8h + 0.2h;
    half3 fernColor = fernAlbedo * (sun.color * ndl * sunVis + SampleSH(fernN));
    color = lerp(color, fernColor, fern);

    half shore = saturate(depth / _ShoreSoftness) * v.edge;
    half wetBand = 1 - saturate(depth / (_ShoreSoftness * 3));
    color *= 1 - wetBand * 0.25;
    color = ApplyJungleAtmosphere(color, v.positionWS, sun.color, sun.direction);
    half alpha = max(shore, fern * saturate(depth / 0.02)) * smoothstep(0.02, 0.35, extent);
    return half4(color, alpha);
   }
   ENDHLSL
  }
 }
}
