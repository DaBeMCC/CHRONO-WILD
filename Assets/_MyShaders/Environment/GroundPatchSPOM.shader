Shader "CHRONO WILD/Environment/Ground Patch SPOM"
{
 // Silhouette parallax occlusion mapping on a flat quad laid over the terrain.
 // The height field is shaped into an irregular mound by an edge mask; rays that pass
 // under the relief or leave the quad are discarded, so the outline reads as real
 // geometry. Depth is written at the hit point so plants and terrain intersect it correctly.
 Properties
 {
  _BaseMap("Albedo (RGB) Height (A)",2D)="white"{}
  _BaseColor("Tint",Color)=(1,1,1,1)
  [Normal] _BumpMap("Normal",2D)="bump"{}
  _BumpScale("Normal Strength",Range(0,2))=1
  _Tiling("Texture Tiling per Patch",Float)=1.5
  _Depth("Relief Depth (patch units)",Range(0,0.3))=0.08
  _Dome("Mound Shape",Range(0,1))=0.45
  _EdgeNoise("Outline Irregularity",Range(0,1))=0.45
  _EdgeSoftness("Outline Softness",Range(0.02,0.6))=0.25
  _Cut("Silhouette Cut Height",Range(0,0.5))=0.08
  _Smoothness("Smoothness",Range(0,1))=0.15
  _Steps("Max Steps",Range(8,48))=28
  _ShadowStrength("Self Shadow",Range(0,1))=0.7
  _FadeDistance("Parallax Fade Distance (m)",Float)=30
 }
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
 TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseMap_ST; half4 _BaseColor; half _BumpScale; float _Tiling; float _Depth; half _Dome; half _EdgeNoise; half _EdgeSoftness;
 half _Cut; half _Smoothness; float _Steps; half _ShadowStrength; float _FadeDistance;
 CBUFFER_END

 struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
 struct Varyings
 {
  float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 positionWS:TEXCOORD1;
  half3 normalWS:TEXCOORD2; half4 tangentWS:TEXCOORD3; float3 viewTS:TEXCOORD4; float3 lightTS:TEXCOORD5; float seed:TEXCOORD6;
  UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO
 };

 float PatchHash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
 float PatchNoise(float2 p)
 {
  float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
  return lerp(lerp(PatchHash(i), PatchHash(i + float2(1, 0)), f.x), lerp(PatchHash(i + float2(0, 1)), PatchHash(i + float2(1, 1)), f.x), f.y);
 }

 // Height of the patch surface (0 floor .. 1 top) at quad uv.
 float PatchHeight(float2 uv, float seed, float2 dx, float2 dy)
 {
  float2 c = uv * 2 - 1;
  float angleNoise = PatchNoise(float2(atan2(c.y, c.x) * 1.6 + seed * 13.0, seed * 7.0)) - 0.5;
  float edge = 1 - length(c) + angleNoise * _EdgeNoise + (PatchNoise(uv * 9 + seed * 3) - 0.5) * _EdgeNoise * 0.35;
  float mask = smoothstep(0, _EdgeSoftness, edge);
  float detail = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uv * _Tiling + seed, dx * _Tiling, dy * _Tiling).a;
  float dome = sqrt(saturate(edge * 1.6));
  return saturate(lerp(detail, detail * 0.55 + dome * 0.55, _Dome)) * mask;
 }

 Varyings PatchVertex(Attributes v)
 {
  Varyings o = (Varyings)0;
  UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
  VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
  VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
  o.positionCS = p.positionCS; o.positionWS = p.positionWS; o.uv = v.uv;
  o.normalWS = n.normalWS; o.tangentWS = half4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
  float3 b = cross(n.normalWS, n.tangentWS) * v.tangentOS.w;
  float3x3 tbn = float3x3(n.tangentWS, b, n.normalWS);
  // Relief is authored in object units; express view and light vectors in quad-uv space.
  float3 scale = float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21), length(UNITY_MATRIX_M._m02_m12_m22));
  float3 viewWS = GetWorldSpaceViewDir(p.positionWS);
  o.viewTS = mul(tbn, viewWS) / float3(scale.x, scale.z, scale.x);
  o.lightTS = mul(tbn, _MainLightPosition.xyz) / float3(scale.x, scale.z, scale.x);
  float3 pivot = TransformObjectToWorld(float3(0, 0, 0));
  o.seed = frac(dot(pivot.xz, float2(0.137, 0.271)));
  return o;
 }

 // Marches the relief; returns hit uv, hit layer and whether the ray hit the patch.
 bool PatchMarch(Varyings i, out float2 uv, out float hitLayer, out float h)
 {
  float2 dx = ddx(i.uv), dy = ddy(i.uv);
  float3 v = normalize(i.viewTS);
  float fade = saturate(1 - distance(i.positionWS, _WorldSpaceCameraPos) / _FadeDistance);
  float depth = _Depth * max(fade, 0.25);
  float2 delta = -v.xy / max(v.z, 0.08) * depth;
  int steps = (int)lerp(_Steps, 8, v.z * v.z);
  float stepSize = 1.0 / steps;
  uv = i.uv; hitLayer = 1; h = PatchHeight(uv, i.seed, dx, dy);
  float prevLayer = 1, prevH = h; float2 prevUv = uv;
  [loop]
  for (int s = 0; s < 48; s++)
  {
   if (s >= steps || hitLayer <= h) break;
   prevLayer = hitLayer; prevH = h; prevUv = uv;
   hitLayer -= stepSize; uv += delta * stepSize;
   h = PatchHeight(uv, i.seed, dx, dy);
  }
  [unroll]
  for (int b = 0; b < 4; b++)
  {
   float2 midUv = (prevUv + uv) * 0.5; float midLayer = (prevLayer + hitLayer) * 0.5;
   float midH = PatchHeight(midUv, i.seed, dx, dy);
   if (midLayer > midH) { prevUv = midUv; prevLayer = midLayer; } else { uv = midUv; hitLayer = midLayer; h = midH; }
  }
  bool inside = all(uv >= 0) && all(uv <= 1);
  return inside && h > _Cut && hitLayer <= h + stepSize;
 }

 // Depth of the hit point: the quad sits at the top of the relief, the floor lies _Depth below.
 float PatchDepth(Varyings i, float hitLayer)
 {
  float3 scale = float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21), length(UNITY_MATRIX_M._m02_m12_m22));
  float down = (1 - hitLayer) * _Depth * scale.x;
  float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
  float3 hitWS = i.positionWS - viewDir * down / max(dot(viewDir, normalize(i.normalWS)), 0.08);
  float4 clip = TransformWorldToHClip(hitWS);
  return clip.z / clip.w;
 }
 ENDHLSL

 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="AlphaTest+20"}
  Pass
  {
   Name "ForwardLit" Tags {"LightMode"="UniversalForward"}
   Cull Back ZWrite On
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex PatchVertex
   #pragma fragment PatchFragment
   #pragma multi_compile_instancing
   #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
   #pragma multi_compile_fog
   #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
   #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
   #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

   half4 PatchFragment(Varyings i, out float depthOut : SV_Depth) : SV_Target
   {
    UNITY_SETUP_INSTANCE_ID(i);
    float2 uv; float hitLayer, h;
    if (!PatchMarch(i, uv, hitLayer, h)) discard;
    depthOut = PatchDepth(i, hitLayer);
    float2 dx = ddx(i.uv), dy = ddy(i.uv);
    float2 tuv = uv * _Tiling + i.seed;
    half4 albedo = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, tuv, dx * _Tiling, dy * _Tiling) * _BaseColor;
    half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_BumpMap, sampler_BumpMap, tuv, dx * _Tiling, dy * _Tiling), _BumpScale);
    // Mound slope from the height gradient rounds the outline.
    float e = 0.01;
    float hx = PatchHeight(uv + float2(e, 0), i.seed, dx, dy) - PatchHeight(uv - float2(e, 0), i.seed, dx, dy);
    float hy = PatchHeight(uv + float2(0, e), i.seed, dx, dy) - PatchHeight(uv - float2(0, e), i.seed, dx, dy);
    nTS = normalize(float3(nTS.xy - float2(hx, hy) * _Depth * 6.0 / e * 0.05, nTS.z));
    half3 n = normalize(i.normalWS); half3 t = normalize(i.tangentWS.xyz); half3 b = cross(n, t) * i.tangentWS.w;
    half3 normalWS = normalize(mul(nTS, half3x3(t, b, n)));

    // Self shadow toward the sun.
    half shadow = 1;
    float3 l = normalize(i.lightTS);
    if (l.z > 0.05)
    {
     float2 ld = l.xy / max(l.z, 0.15) * _Depth;
     float occ = 0;
     [unroll] for (int k = 1; k <= 5; k++) { float s = k / 5.0 * (1 - hitLayer); occ = max(occ, PatchHeight(uv + ld * s, i.seed, dx, dy) - (hitLayer + s)); }
     shadow = 1 - saturate(occ * 8) * _ShadowStrength;
    }

    InputData d = (InputData)0;
    d.positionWS = i.positionWS; d.normalWS = normalWS; d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
     d.shadowCoord = ComputeScreenPos(i.positionCS);
    #else
     d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
    #endif
    d.fogCoord = ComputeFogFactor(i.positionCS.z);
    d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
    d.shadowMask = half4(1, 1, 1, 1);
    #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
    d.bakedGI = SampleProbeVolumePixel(SampleSH(normalWS), GetAbsolutePositionWS(i.positionWS), normalWS, d.viewDirectionWS, i.positionCS.xy);
    #else
    d.bakedGI = SampleSH(normalWS);
    #endif
    SurfaceData s = (SurfaceData)0;
    s.albedo = albedo.rgb * shadow; s.alpha = 1; s.specular = 0.04; s.smoothness = _Smoothness;
    s.normalTS = half3(0, 0, 1); s.occlusion = lerp(0.45, 1.0, h);
    half4 c = UniversalFragmentPBR(d, s);
    c.rgb = MixFog(c.rgb, d.fogCoord);
    return half4(c.rgb, 1);
   }
   ENDHLSL
  }
  Pass
  {
   Name "DepthOnly" Tags {"LightMode"="DepthOnly"}
   ColorMask R
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex PatchVertex
   #pragma fragment DepthFragment
   #pragma multi_compile_instancing
   #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
   half4 DepthFragment(Varyings i, out float depthOut : SV_Depth) : SV_Target
   {
    float2 uv; float hitLayer, h;
    if (!PatchMarch(i, uv, hitLayer, h)) discard;
    depthOut = PatchDepth(i, hitLayer);
    return 0;
   }
   ENDHLSL
  }
  Pass
  {
   Name "DepthNormals" Tags {"LightMode"="DepthNormalsOnly"}
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex PatchVertex
   #pragma fragment NormalsFragment
   #pragma multi_compile_instancing
   #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
   #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
   half4 NormalsFragment(Varyings i, out float depthOut : SV_Depth) : SV_Target
   {
    float2 uv; float hitLayer, h;
    if (!PatchMarch(i, uv, hitLayer, h)) discard;
    depthOut = PatchDepth(i, hitLayer);
    half3 n = normalize(i.normalWS);
    #if defined(_GBUFFER_NORMALS_OCT)
     float2 oct = PackNormalOctQuadEncode(n); return half4(PackFloat2To888(saturate(oct * .5 + .5)), 0);
    #else
     return half4(n, 0);
    #endif
   }
   ENDHLSL
  }
 }
}
