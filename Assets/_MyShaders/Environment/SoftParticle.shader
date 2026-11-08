Shader "CHRONO WILD/Environment/Soft Particle"
{
 // Lit, alpha-blended particle that fades where it meets geometry, so smoke and mist have no hard
 // intersection lines. Used for the volcano plume, steam, ground mist, ash, pollen and insects.
 Properties
 {
  _BaseMap("Texture",2D)="white"{}
  _SunAmount("Sun Light",Range(0,1))=0.6
  _SoftDistance("Soft Fade (m)",Float)=2
  _NearFade("Fade Near Camera (m)",Float)=0
  _NightDim("Darken At Night",Range(0,1))=0
  [Toggle] _RainDrop("Stop Under Canopy",Float)=0
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+30"}
  Pass
  {
   Name "SoftParticle" Tags {"LightMode"="UniversalForward"}
   Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vertex
   #pragma fragment Fragment
   #pragma multi_compile_instancing
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   #include "Assets/_MyShaders/Water/RippleSample.hlsl"
   TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseMap_ST; half _SunAmount; float _SoftDistance; float _NearFade; half _NightDim; half _RainDrop;
   CBUFFER_END
   float _CW_EnvDim;
   struct Attributes { float4 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
   struct Varyings { float4 positionCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; float3 positionWS:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
   Varyings Vertex(Attributes a)
   {
    Varyings v = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
    v.positionWS = TransformObjectToWorld(a.positionOS.xyz);
    v.positionCS = TransformWorldToHClip(v.positionWS);
    v.color = a.color; v.uv = TRANSFORM_TEX(a.uv, _BaseMap);
    return v;
   }
   half4 Fragment(Varyings v) : SV_Target
   {
    half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, v.uv);
    Light sun = GetMainLight();
    half3 light = (SampleSH(half3(0, 1, 0)) + sun.color * _SunAmount) * (1.0 - _CW_EnvDim * _NightDim);
    half4 color = tex * v.color;
    color.rgb *= light;
    float2 screen = GetNormalizedScreenSpaceUV(v.positionCS);
    float scene = LinearEyeDepth(SampleSceneDepth(screen), _ZBufferParams);
    float self = -TransformWorldToView(v.positionWS).z;
    color.a *= saturate((scene - self) / max(_SoftDistance, 0.01));
    // Under the canopy almost every drop is gone; the coarse cells leave a few drips falling.
    if (_RainDrop > 0.5 && CW_RainFallsAt(v.positionWS) < 0.5)
     color.a *= step(0.965, frac(sin(dot(floor(v.positionWS.xz * 1.7), float2(12.9898, 78.233))) * 43758.5453));
    if (_NearFade > 0.0)
     color.a *= saturate(self / _NearFade);
    return color;
   }
   ENDHLSL
  }
 }
}
