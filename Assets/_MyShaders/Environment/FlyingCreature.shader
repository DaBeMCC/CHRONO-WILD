Shader "CHRONO WILD/Environment/Flying Creature"
{
 // Small flying animals. Vertex colour r says how far a vertex is along the wing; the flap is done
 // here, so each creature is just one moving transform.
 Properties
 {
  _BaseColor("Color",Color)=(0.13,0.1,0.09,1)
  _WingColor("Wing Color",Color)=(0.3,0.17,0.12,1)
  _FlapRate("Flaps per Second",Float)=1.6
  _FlapHeight("Flap Height (object units)",Float)=0.55
  _Translucency("Wing Translucency",Range(0,2))=0.7
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
  Cull Off
  Pass
  {
   Name "ForwardLit" Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vertex
   #pragma fragment Fragment
   #pragma multi_compile_instancing
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _BaseColor; half4 _WingColor; float _FlapRate; float _FlapHeight; half _Translucency;
   CBUFFER_END
   struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
   struct Varyings { float4 positionCS:SV_POSITION; half3 normalWS:TEXCOORD0; half wing:TEXCOORD1; float3 positionWS:TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
   Varyings Vertex(Attributes a)
   {
    Varyings v = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
    float3 pivot = TransformObjectToWorld(float3(0, 0, 0));
    float offset = frac(pivot.x * 0.37 + pivot.z * 0.11) * 6.2831;
    float3 p = a.positionOS.xyz;
    p.y += a.color.r * a.color.r * sin(_Time.y * _FlapRate * 6.2831 + offset) * _FlapHeight;
    v.positionWS = TransformObjectToWorld(p);
    v.positionCS = TransformWorldToHClip(v.positionWS);
    v.normalWS = TransformObjectToWorldNormal(a.normalOS);
    v.wing = saturate(a.color.r * 4.0);
    return v;
   }
   half4 Fragment(Varyings v, FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC) : SV_Target
   {
    half3 n = normalize(v.normalWS) * IS_FRONT_VFACE(face, 1.0h, -1.0h);
    Light sun = GetMainLight();
    half3 albedo = lerp(_BaseColor.rgb, _WingColor.rgb, v.wing);
    half3 color = albedo * (sun.color * saturate(dot(n, sun.direction) * 0.8 + 0.2) + SampleSH(n));
    color += _WingColor.rgb * sun.color * saturate(dot(n, -sun.direction)) * _Translucency * v.wing;
    return half4(color, 1.0);
   }
   ENDHLSL
  }
 }
}
