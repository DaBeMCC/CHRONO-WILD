Shader "CHRONO WILD/Vegetation/Ground Cover"
{
 // Dense ground cover made of real tapered blades (no alpha, so no overdraw). What sells it:
 //  - colour runs from a dark root that matches the soil to a lighter tip;
 //  - normals are blended toward "up", so a field shades like one soft surface, not spikes;
 //  - large patches of hue and dryness drift across the field;
 //  - sunlight glows through the blades when looking toward the sun;
 //  - distant clumps thin out and sink into the ground instead of popping;
 //  - travelling gust waves bend the blades and flash their pale undersides.
 // Mesh vertex colour: r = position along the blade (0 root, 1 tip), g = random per blade.
 Properties
 {
  _RootColor("Root Color",Color)=(0.09,0.12,0.04,1)
  _TipColor("Tip Color",Color)=(0.34,0.46,0.13,1)
  _DryColor("Dry Patch Color",Color)=(0.56,0.5,0.22,1)
  _DryAmount("Dry Patches",Range(0,1))=0.3
  _PatchScale("Patch Size (m)",Float)=16
  _Translucency("Translucency",Range(0,2))=0.8
  _WindStrength("Breeze (m)",Range(0,0.3))=0.05
  _GustStrength("Gust Bend (m)",Range(0,1))=0.28
  _FadeEnd("Fade End Distance (m)",Float)=68
 }
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 CBUFFER_START(UnityPerMaterial)
 half4 _RootColor; half4 _TipColor; half4 _DryColor; half _DryAmount; float _PatchScale; half _Translucency;
 half _WindStrength; half _GustStrength; float _FadeEnd;
 CBUFFER_END
 float4 _CW_Wind;
 float _CW_RainWet;

 struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
 struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; half3 tint:TEXCOORD2; half data:TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };

 float Noise2(float2 p)
 {
  float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
  float a = frac(sin(dot(i, float2(127.1, 311.7))) * 43758.5453), b = frac(sin(dot(i + float2(1, 0), float2(127.1, 311.7))) * 43758.5453);
  float c = frac(sin(dot(i + float2(0, 1), float2(127.1, 311.7))) * 43758.5453), d = frac(sin(dot(i + float2(1, 1), float2(127.1, 311.7))) * 43758.5453);
  return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
 }

 Varyings GroundVertex(Attributes v)
 {
  Varyings o = (Varyings)0;
  UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
  float3 pivot = TransformObjectToWorld(float3(0, 0, 0));
  float3 local = v.positionOS.xyz;
  float dist = distance(pivot, _WorldSpaceCameraPos);
  // Thin the field with distance: part of the clumps leave early, the rest get wider to cover.
  float pick = frac(sin(dot(pivot.xz, float2(12.9898, 78.233))) * 43758.5453);
  float leave = lerp(0.42, 1.0, pick) * _FadeEnd;
  float keep = 1.0 - smoothstep(leave * 0.8, leave, dist);
  float widen = 1.0 + saturate(dist / _FadeEnd) * 1.2;
  local.xz *= widen;
  local.y *= keep;
  float3 p = TransformObjectToWorld(local);
  float t = v.color.r;
  float height = t * t;
  float phase = dot(p.xz, float2(0.61, 0.43)) + _Time.y * 2.2 + v.color.g * 6.0;
  float2 wind = _CW_Wind.xy;
  p.xz += float2(0.8, 0.6) * sin(phase) * _WindStrength * height;
  // Gust fronts sweeping along the wind direction, broken up by slow patches.
  float along = dot(pivot.xz, wind) / max(_CW_Wind.w, 1.0) - _Time.y * _CW_Wind.z / max(_CW_Wind.w, 1.0);
  float front = pow(sin(along * 6.2831) * 0.5 + 0.5, 3.0) * lerp(0.35, 1.0, Noise2(pivot.xz * 0.04 - wind * _Time.y * 0.1));
  float bend = front * _GustStrength * height;
  p.xz += wind * bend;
  p.y -= bend * bend * 0.5;
  o.positionWS = p;
  o.positionCS = TransformWorldToHClip(p);
  // Mostly "up": neighbouring blades light alike, like the ground they grow from.
  o.normalWS = normalize(lerp(TransformObjectToWorldNormal(v.normalOS), half3(0, 1, 0), 0.72));
  float patch = Noise2(pivot.xz / _PatchScale) * 0.65 + Noise2(pivot.xz / (_PatchScale * 0.31) + 7.3) * 0.35;
  half dry = smoothstep(0.62 - _DryAmount * 0.5, 0.82 - _DryAmount * 0.3, patch);
  half3 tip = lerp(_TipColor.rgb, _DryColor.rgb, dry) * lerp(0.82, 1.15, v.color.g);
  half3 color = lerp(_RootColor.rgb, tip, smoothstep(0.0, 0.85, t));
  color *= 1.0 + front * 0.28 * t;
  o.tint = color * lerp(1.0, 0.72, _CW_RainWet);
  o.data = t;
  return o;
 }
 ENDHLSL
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+20"}
  Cull Off
  Pass
  {
   Name "ForwardLit" Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex GroundVertex
   #pragma fragment GroundFragment
   #pragma multi_compile_instancing
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
   half4 GroundFragment(Varyings v) : SV_Target
   {
    UNITY_SETUP_INSTANCE_ID(v);
    half3 n = normalize(v.normalWS);
    Light sun = GetMainLight(TransformWorldToShadowCoord(v.positionWS));
    half3 view = GetWorldSpaceNormalizeViewDir(v.positionWS);
    half visible = sun.shadowAttenuation;
    half diffuse = saturate(dot(n, sun.direction) * 0.75 + 0.25);
    half3 color = v.tint * (sun.color * diffuse * visible + SampleSH(n));
    // Light passing through thin blades, strongest near the tips and against the sun.
    half back = pow(saturate(dot(-sun.direction, view) * 0.5 + 0.5), 4.0);
    color += v.tint * sun.color * back * visible * _Translucency * v.data;
    return half4(color, 1.0);
   }
   ENDHLSL
  }
  Pass
  {
   Name "DepthOnly" Tags {"LightMode"="DepthOnly"} ColorMask R
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex GroundVertex
   #pragma fragment DepthFragment
   #pragma multi_compile_instancing
   half4 DepthFragment(Varyings v) : SV_Target { return 0; }
   ENDHLSL
  }
  Pass
  {
   Name "DepthNormals" Tags {"LightMode"="DepthNormalsOnly"}
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex GroundVertex
   #pragma fragment NormalsFragment
   #pragma multi_compile_instancing
   #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
   half4 NormalsFragment(Varyings v) : SV_Target
   {
    half3 n = normalize(v.normalWS);
    #if defined(_GBUFFER_NORMALS_OCT)
     float2 oct = PackNormalOctQuadEncode(n); return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0);
    #else
     return half4(n, 0);
    #endif
   }
   ENDHLSL
  }
 }
}
