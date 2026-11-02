Shader "CHRONO WILD/Water/Jungle Puddle"
{
 // Shallow forest water: depth-based absorption over a refracted bottom, screen-space
 // reflections of the surrounding plants with a baked probe fallback, slow wind ripples
 // and a soft shoreline driven by the water depth.
 Properties
 {
  _Absorption("Absorption per Meter (RGB)",Vector)=(9,7.5,6,0)
  _ScatterColor("Suspended Sediment Color",Color)=(0.06,0.05,0.032,1)
  _Turbidity("Turbidity",Range(0,1))=0.55
  _Refraction("Refraction Strength",Range(0,0.1))=0.025
  _Roughness("Surface Roughness",Range(0,0.3))=0.04
  _RippleStrength("Ripple Strength",Range(0,1))=0.22
  _RippleScale("Ripple Scale",Float)=1.6
  _RippleSpeed("Ripple Speed",Float)=0.12
  _ReflectionStrength("Reflection Strength",Range(0,1))=0.85
  _SSRSteps("SSR Steps",Range(8,48))=40
  _SSRDistance("SSR Max Distance (m)",Float)=14
  _SSRThickness("SSR Thickness (m)",Float)=0.6
  _ProbeDim("Probe Fallback Under Canopy",Range(0,1))=0.3
  _ShoreSoftness("Shore Softness (m)",Range(0.005,0.2))=0.035
  _WetEdgeDarkening("Wet Edge Darkening",Range(0,1))=0.5
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+10"}
  Pass
  {
   Name "JunglePuddle" Tags {"LightMode"="UniversalForward"}
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

   CBUFFER_START(UnityPerMaterial)
   float4 _Absorption; half4 _ScatterColor; half _Turbidity; half _Refraction; half _Roughness;
   half _RippleStrength; float _RippleScale; float _RippleSpeed; half _ReflectionStrength;
   float _SSRSteps; float _SSRDistance; float _SSRThickness; half _ProbeDim; half _ShoreSoftness; half _WetEdgeDarkening;
   CBUFFER_END
   float _ChronoQuality;

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

   // Gradient noise with analytic derivatives (value in x, d/dx in y, d/dz in z).
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

   half3 RippleNormal(float2 p)
   {
    float t = _Time.y * _RippleSpeed;
    float2 slope = NoiseD(p * _RippleScale + float2(t, t * 0.6)).yz * 0.6
                 + NoiseD(p * _RippleScale * 2.3 - float2(t * 0.8, -t * 1.1)).yz * 0.3
                 + NoiseD(p * _RippleScale * 5.1 + float2(-t * 1.7, t * 0.9)).yz * 0.1;
    return normalize(half3(-slope.x * _RippleStrength * 0.25, 1, -slope.y * _RippleStrength * 0.25));
   }

   float RayDepthDiff(float3 p, out float2 uv)
   {
    float4 clip = TransformWorldToHClip(p);
    float4 screen = ComputeScreenPos(clip);
    uv = screen.xy / max(screen.w, 1e-4);
    return clip.w - LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
   }

   // Screen-space reflection along the reflected view ray; returns color and confidence.
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
      // Bisect between the last two samples so the hit lands on the surface instead of in slices.
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
      half fade = edge.x * edge.y * saturate(1.2 - i / steps);
      return half4(SampleSceneColor(uv), fade);
     }
    }
    return 0;
   }

   half4 Fragment(Varyings v) : SV_Target
   {
    float2 screenUV = GetNormalizedScreenSpaceUV(v.positionCS);
    float surfaceEye = -TransformWorldToView(v.positionWS).z;
    float bottomEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
    float3 view = GetWorldSpaceNormalizeViewDir(v.positionWS);
    // Water depth along the view ray, converted to vertical depth.
    float rayDepth = max(0, bottomEye - surfaceEye) * (length(_WorldSpaceCameraPos - v.positionWS) / max(surfaceEye, 1e-3));
    float depth = rayDepth * saturate(view.y + 0.05);

    half3 n = RippleNormal(v.positionWS.xz);
    // Ripples fade out in the shallows so the shoreline stays calm.
    half calm = saturate(depth / 0.06);
    n = normalize(lerp(half3(0, 1, 0), n, calm));

    // Refraction: offset the bottom lookup, rejecting samples that land in front of the water.
    float2 offset = n.xz * _Refraction * calm * step(0.5, _ChronoQuality);
    float2 refrUV = screenUV + offset;
    if (LinearEyeDepth(SampleSceneDepth(refrUV), _ZBufferParams) < surfaceEye) refrUV = screenUV;
    half3 bottom = SampleSceneColor(refrUV);

    // Light absorbed on the way down and back up; sediment scatters a murky tea color.
    float pathLength = rayDepth + depth;
    half3 transmittance = exp(-_Absorption.rgb * pathLength * (0.4 + _Turbidity));
    half3 scatter = _ScatterColor.rgb * (1 - transmittance);
    half3 water = bottom * transmittance + scatter;

    // Reflection: screen-space first, baked probe where the ray misses.
    half fresnel = 0.02 + 0.98 * pow(1 - saturate(dot(n, view)), 5);
    float3 reflDir = reflect(-view, n);
    half3 probe = GlossyEnvironmentReflection(reflDir, v.positionWS, _Roughness, 1, screenUV) * _ProbeDim;
    float jitter = InterleavedGradientNoise(v.positionCS.xy, 0);
    half4 ssr = _ChronoQuality > 0.5 ? TraceReflection(v.positionWS, reflDir, jitter) : half4(0, 0, 0, 0);
    half3 reflection = lerp(probe, ssr.rgb, ssr.a) * _ReflectionStrength;

    float4 shadowCoord = TransformWorldToShadowCoord(v.positionWS);
    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
     shadowCoord = ComputeScreenPos(v.positionCS);
    #endif
    Light sun = GetMainLight(shadowCoord);
    half3 h = normalize(sun.direction + view);
    half spec = pow(saturate(dot(n, h)), 600) * 6 + pow(saturate(dot(n, h)), 90) * 0.3;
    half3 color = lerp(water, reflection, fresnel) + sun.color * spec * sun.shadowAttenuation * calm;

    // Soft shoreline: the thin edge darkens like soaked mud before it turns into open water.
    half shore = saturate(depth / _ShoreSoftness) * v.edge;
    half wetBand = 1 - saturate(depth / (_ShoreSoftness * 3));
    color *= 1 - wetBand * _WetEdgeDarkening * 0.5;
    color = ApplyJungleAtmosphere(color, v.positionWS, sun.color, sun.direction);
    return half4(color, shore);
   }
   ENDHLSL
  }
 }
}
