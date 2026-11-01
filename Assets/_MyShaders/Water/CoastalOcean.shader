Shader "CHRONO WILD/Water/Coastal Ocean"
{
 // Cheap tropical coastal water: a few Gerstner waves in the vertex stage (faded with
 // distance so far water stays flat), depth-texture shading from turquoise shallows to deep
 // blue, shoreline foam bands that travel toward the beach, crest foam, probe reflections.
 // _ChronoQuality (global, 0 low / 1 high) turns off refraction and detail ripples.
 Properties
 {
  _ShallowColor("Shallow Color",Color)=(0.20,0.62,0.58,1)
  _DeepColor("Deep Color",Color)=(0.0,0.2,0.4,1)
  _DepthRange("Color Depth Range (m)",Float)=6
  _Clarity("Water Clarity (m)",Float)=2.2
  _Refraction("Refraction",Range(0,0.08))=0.02
  _WaveHeight("Wave Height (m)",Range(0,1))=0.22
  _WaveLength("Main Wavelength (m)",Float)=14
  _WaveDirection("Wave Direction (deg, toward shore)",Float)=90
  _WaveSpeed("Wave Speed",Float)=1
  _WaveFade("Wave Fade Distance (m)",Float)=180
  _Ripples("Detail Ripples",Range(0,1))=0.35
  _Roughness("Roughness",Range(0,0.3))=0.06
  _SkyZenith("Sky Zenith (reflection)",Color)=(0.30,0.48,0.78,1)
  _SkyHorizon("Sky Horizon (reflection)",Color)=(0.56,0.7,0.84,1)
  _FoamColor("Foam Color",Color)=(0.95,0.96,0.94,1)
  _ShoreFoamDepth("Shore Foam Depth (m)",Float)=0.35
  _FoamBands("Shore Bands Depth (m)",Float)=2.2
  _FoamBandSpeed("Shore Band Speed",Float)=0.35
  _CrestFoam("Crest Foam",Range(0,1))=0.4
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
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
   #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

   CBUFFER_START(UnityPerMaterial)
   half4 _ShallowColor; half4 _DeepColor; float _DepthRange; float _Clarity; half _Refraction;
   float _WaveHeight; float _WaveLength; float _WaveDirection; float _WaveSpeed; float _WaveFade;
   half _Ripples; half _Roughness; half4 _SkyZenith; half4 _SkyHorizon; half4 _FoamColor; float _ShoreFoamDepth; float _FoamBands; float _FoamBandSpeed;
   half _CrestFoam; half _EdgeFade;
   CBUFFER_END
   float _ChronoQuality;

   struct Attributes { float4 positionOS:POSITION; };
   struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; float crest:TEXCOORD2; };

   // One Gerstner wave: returns displacement, accumulates tangent-frame derivatives.
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

   Varyings Vertex(Attributes a)
   {
    Varyings v;
    float3 p = TransformObjectToWorld(a.positionOS.xyz);
    float fade = 1.0 - saturate(distance(p.xz, _WorldSpaceCameraPos.xz) / _WaveFade);
    float t = _Time.y * _WaveSpeed;
    float ang = radians(_WaveDirection);
    float2 d0 = float2(cos(ang), sin(ang));
    float2 d1 = float2(cos(ang + 0.45), sin(ang + 0.45));
    float2 d2 = float2(cos(ang - 0.6), sin(ang - 0.6));
    float2 d3 = float2(cos(ang + 1.3), sin(ang + 1.3));
    float3 tangent = float3(1, 0, 0), binormal = float3(0, 0, 1);
    float h = _WaveHeight * fade;
    float3 disp = Gerstner(p.xz, d0, _WaveLength, 0.6, h * 0.55, t, tangent, binormal)
                + Gerstner(p.xz, d1, _WaveLength * 0.62, 0.5, h * 0.25, t, tangent, binormal)
                + Gerstner(p.xz, d2, _WaveLength * 0.41, 0.45, h * 0.13, t, tangent, binormal)
                + Gerstner(p.xz, d3, _WaveLength * 0.27, 0.4, h * 0.07, t, tangent, binormal);
    p += disp;
    v.positionWS = p;
    v.normalWS = normalize(cross(binormal, tangent));
    v.crest = saturate(disp.y / max(_WaveHeight * 0.5, 1e-3));
    v.positionCS = TransformWorldToHClip(p);
    return v;
   }

   float2 Hash2(float2 p) { p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3))); return frac(sin(p) * 43758.5453); }

   // Cellular noise: bubbly foam pattern without a texture.
   float Cells(float2 p)
   {
    float2 i = floor(p), f = frac(p); float m = 1;
    [unroll] for (int y = -1; y <= 1; y++) [unroll] for (int x = -1; x <= 1; x++)
    { float2 g = float2(x, y); float2 o = Hash2(i + g); m = min(m, length(g + o - f)); }
    return m;
   }

   float ValueNoise(float2 p)
   {
    float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
    float a = Hash2(i).x, b = Hash2(i + float2(1, 0)).x, c = Hash2(i + float2(0, 1)).x, d = Hash2(i + float2(1, 1)).x;
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
   }

   half4 Fragment(Varyings v) : SV_Target
   {
    bool high = _ChronoQuality > 0.5;
    float2 screenUV = GetNormalizedScreenSpaceUV(v.positionCS);
    float3 view = GetWorldSpaceNormalizeViewDir(v.positionWS);
    float surfaceEye = -TransformWorldToView(v.positionWS).z;
    float bottomEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
    float rayDepth = max(0, bottomEye - surfaceEye) * (length(_WorldSpaceCameraPos - v.positionWS) / max(surfaceEye, 1e-3));
    float depth = rayDepth * saturate(view.y + 0.05);

    // Detail ripples on top of the wave normal.
    float3 n = normalize(v.normalWS);
    if (high && _Ripples > 0)
    {
     float t = _Time.y;
     float2 p = v.positionWS.xz;
     float2 r = float2(ValueNoise(p * 0.9 + t * 0.35) - ValueNoise(p * 0.9 + float2(0.5, 0) + t * 0.35),
                       ValueNoise(p * 0.9 + float2(0, 7.3) - t * 0.3) - ValueNoise(p * 0.9 + float2(0, 7.8) - t * 0.3));
     r += float2(ValueNoise(p * 2.7 - t * 0.6) - 0.5, ValueNoise(p * 2.7 + float2(3.1, 1.7) + t * 0.5) - 0.5) * 0.5;
     n = normalize(n + float3(r.x, 0, r.y) * _Ripples * saturate(1 - surfaceEye / 45));
    }

    // Bottom seen through the water.
    half3 bottom;
    if (high)
    {
     float2 uv = screenUV + n.xz * _Refraction * saturate(depth * 2);
     if (LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams) < surfaceEye) uv = screenUV;
     bottom = SampleSceneColor(uv);
    }
    else bottom = SampleSceneColor(screenUV);

    half3 waterColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(depth / _DepthRange));
    half visibility = exp(-rayDepth / _Clarity);
    Light sun = GetMainLight(TransformWorldToShadowCoord(v.positionWS));
    half3 lit = waterColor * (sun.color * saturate(sun.direction.y) * 0.55 + _SkyZenith.rgb * 0.6);
    half3 color = lerp(lit, bottom * lerp(_ShallowColor.rgb * 1.4, 1, 0.45), visibility);

    // Reflection and sun glint.
    half fresnel = 0.02 + 0.98 * pow(1 - saturate(dot(n, view)), 5);
    // Open-sky reflection from a gradient: the scene cubemap was captured under the canopy.
    float3 rd = reflect(-view, n);
    half3 refl = lerp(_SkyHorizon.rgb, _SkyZenith.rgb, pow(saturate(rd.y), 0.45));
    color = lerp(color, refl, fresnel);
    half3 hv = normalize(sun.direction + view);
    color += sun.color * (pow(saturate(dot(n, hv)), 900) * 8 + pow(saturate(dot(n, hv)), 120) * 0.25) * sun.shadowAttenuation;

    // Foam: a frothy rim right at the sand, bands that roll in from deeper water, crests.
    float2 fp = v.positionWS.xz;
    float bubbles = 1 - Cells(fp * 2.4 + _Time.y * float2(0.05, 0.11)) * 0.6 - Cells(fp * 5.3 - _Time.y * 0.07) * 0.4;
    float wisps = ValueNoise(fp * 0.35 + _Time.y * 0.07) * 0.6 + ValueNoise(fp * 1.3 - _Time.y * 0.1) * 0.4;
    float rim = 1 - saturate(depth / _ShoreFoamDepth);
    float bandPhase = depth / _FoamBands * 3.0 + _Time.y * _FoamBandSpeed;
    float band = saturate(1 - abs(frac(bandPhase) - 0.5) * 6) * (1 - saturate(depth / _FoamBands));
    float nearFade = saturate(1 - surfaceEye / 70);
    // The rim breaks up along the beach so it never reads as a constant strip.
    float rimBreak = 0.45 + 0.55 * ValueNoise(fp * 0.18 + float2(_Time.y * 0.03, 0));
    float foamMask = saturate(rim * 1.2 * rimBreak + band * 0.7 + v.crest * v.crest * _CrestFoam * nearFade);
    // Coverage threshold: the mask decides how much of the bubble pattern survives, so foam
    // breaks into lace instead of a flat white strip.
    float pattern = bubbles * 0.55 + wisps * 0.45;
    float coverage = foamMask * 0.6;
    float foam = smoothstep(1.0 - coverage, 1.0 - coverage + 0.15, pattern) * saturate(foamMask * 3);
    color = lerp(color, _FoamColor.rgb * (sun.color * saturate(sun.direction.y) + _SkyZenith.rgb * 0.5) * 0.85, foam);

    half alpha = saturate(depth / _EdgeFade);
    return half4(color, alpha * lerp(1, 0.9, foam));
   }
   ENDHLSL
  }
 }
}
