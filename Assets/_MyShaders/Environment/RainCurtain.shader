Shader "CHRONO WILD/Environment/Rain Curtain"
{
 // Distant rain: a cylinder around the camera with scrolling streaks. A few thousand particles can
 // only fill the air close to the player; these curtains give the mass of rain further away, and
 // they are what makes heavy rain read as rain instead of as separate lines.
 Properties
 {
  _StreakTex("Streaks",2D)="white"{}
  _Tiling("Tiling Around, Down",Vector)=(9,1.2,0,0)
  _Speed("Fall Speed (tiles per second)",Float)=1.5
  _Opacity("Opacity",Range(0,1))=0.25
  _Tilt("Wind Tilt",Float)=0.05
  _SoftDistance("Soft Fade (m)",Float)=5
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+25"}
  Pass
  {
   Name "RainCurtain" Tags {"LightMode"="UniversalForward"}
   Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vertex
   #pragma fragment Fragment
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   #include "Assets/_MyShaders/Water/RippleSample.hlsl"
   TEXTURE2D(_StreakTex); SAMPLER(sampler_StreakTex);
   CBUFFER_START(UnityPerMaterial)
   float4 _Tiling; float _Speed; half _Opacity; float _Tilt; float _SoftDistance;
   CBUFFER_END
   float4 _CW_Wind;
   struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
   struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 positionWS:TEXCOORD1; };
   Varyings Vertex(Attributes a)
   {
    Varyings v;
    v.positionWS = TransformObjectToWorld(a.positionOS.xyz);
    v.positionCS = TransformWorldToHClip(v.positionWS);
    v.uv = a.uv;
    return v;
   }
   half4 Fragment(Varyings v) : SV_Target
   {
    float2 uv = v.uv * _Tiling.xy;
    uv.x += v.uv.y * _Tilt * _Tiling.y * (_CW_Wind.x + _CW_Wind.y);
    half a = SAMPLE_TEXTURE2D(_StreakTex, sampler_StreakTex, uv + float2(0, _Time.y * _Speed)).a;
    half b = SAMPLE_TEXTURE2D(_StreakTex, sampler_StreakTex, uv * float2(1.7, 0.8) + float2(0.37, _Time.y * _Speed * 1.23)).a;
    // Sheets of heavier rain sweep through the curtain.
    float sheet = sin(v.uv.x * 41.0 + _Time.y * 0.9 + sin(v.uv.x * 97.0 - _Time.y * 0.5) * 1.5) * 0.5 + 0.5;
    half edge = smoothstep(0.0, 0.12, v.uv.y) * smoothstep(1.0, 0.8, v.uv.y);
    float scene = LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(v.positionCS)), _ZBufferParams);
    float self = -TransformWorldToView(v.positionWS).z;
    half soft = saturate((scene - self) / max(_SoftDistance, 0.01));
    half alpha = saturate(a + b * 0.7) * lerp(0.55, 1.0, sheet) * edge * soft * _Opacity * saturate(_CW_RainAmount * 1.4) * CW_RainFallsAt(v.positionWS);
    Light sun = GetMainLight();
    half3 light = (SampleSH(half3(0, 1, 0)) + sun.color * 0.35) * half3(0.8, 0.84, 0.9);
    return half4(light, alpha);
   }
   ENDHLSL
  }
 }
}
