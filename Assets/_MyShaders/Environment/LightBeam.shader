Shader "CHRONO WILD/Environment/Light Beam"
{
 // Additive column of light that marks where something is, readable from far across the island.
 // Brightest at the base, fading upward, with slow bands of energy climbing it.
 Properties
 {
  [HDR] _Color("Color",Color)=(1.5,0.75,0.22,1)
  _Intensity("Intensity",Range(0,2))=1
  _BandSpeed("Band Speed",Float)=0.6
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+40"}
  Pass
  {
   Name "LightBeam" Tags {"LightMode"="UniversalForward"}
   Blend One One ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex Vertex
   #pragma fragment Fragment
   #pragma multi_compile_instancing
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _Color; half _Intensity; float _BandSpeed;
   CBUFFER_END
   struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
   struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 normalWS:TEXCOORD1; float3 positionWS:TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
   Varyings Vertex(Attributes a)
   {
    Varyings v = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
    v.positionWS = TransformObjectToWorld(a.positionOS.xyz);
    v.positionCS = TransformWorldToHClip(v.positionWS);
    v.normalWS = TransformObjectToWorldNormal(a.normalOS);
    v.uv = a.uv;
    return v;
   }
   half4 Fragment(Varyings v) : SV_Target
   {
    float3 view = normalize(_WorldSpaceCameraPos - v.positionWS);
    // Soft edges: the beam fades where its surface turns away from the viewer.
    half edge = pow(saturate(abs(dot(normalize(v.normalWS), view))), 2.5);
    half height = pow(1.0 - v.uv.y, 1.8);
    half bands = 0.75 + 0.25 * sin((v.uv.y * 9.0 - _Time.y * _BandSpeed) * 6.2831);
    float distanceBoost = saturate(distance(_WorldSpaceCameraPos, v.positionWS) / 60.0) * 0.6 + 0.4;
    return half4(_Color.rgb * edge * height * bands * _Intensity * distanceBoost, 1.0);
   }
   ENDHLSL
  }
 }
}
