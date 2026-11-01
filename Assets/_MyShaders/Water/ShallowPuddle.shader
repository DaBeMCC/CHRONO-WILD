Shader "CHRONO WILD/Water/Shallow Puddle"
{
 Properties{_BaseColor("Sediment Tint",Color)=(.13,.18,.12,1) _Roughness("Surface Roughness",Range(0,1))=.1 _Ripple("Ripple Strength",Range(0,.1))=.012 _DepthRange("Sediment Depth",Float)=.18}
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+10"}
  Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Back
  Pass
  {
   Name "Puddle" Tags{"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vertex
   #pragma fragment Fragment
   #pragma multi_compile_fog
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
   #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _BaseColor;half _Roughness,_Ripple;float _DepthRange;
   CBUFFER_END
   struct A{float4 positionOS:POSITION;half4 color:COLOR;};
   struct V{float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half edge:TEXCOORD1;half fog:TEXCOORD2;};
   V Vertex(A a){V v;v.positionWS=TransformObjectToWorld(a.positionOS.xyz);v.positionCS=TransformWorldToHClip(v.positionWS);v.edge=a.color.a;v.fog=ComputeFogFactor(v.positionCS.z);return v;}
   half4 Fragment(V v):SV_Target
   {
    float2 uv=GetNormalizedScreenSpaceUV(v.positionCS);float raw=SampleSceneDepth(uv);float sceneEye=LinearEyeDepth(raw,_ZBufferParams);float surfaceEye=-TransformWorldToView(v.positionWS).z;float depth=max(0,sceneEye-surfaceEye);
    float2 p=v.positionWS.xz;float time=_Time.y;float dx=cos(p.x*9+p.y*3+time*.8)*.65+cos(p.x*4-p.y*7-time*.65)*.35;float dz=cos(p.x*9+p.y*3+time*.8)*.22-cos(p.x*4-p.y*7-time*.65)*.55;
    half3 n=normalize(half3(-dx*_Ripple,1,-dz*_Ripple));half3 view=GetWorldSpaceNormalizeViewDir(v.positionWS);half fresnel=.02+.98*pow(1-saturate(dot(n,view)),5);
    half3 reflection=GlossyEnvironmentReflection(reflect(-view,n),v.positionWS,_Roughness,1,uv);half sediment=saturate(depth/max(_DepthRange,.01));half3 color=lerp(_BaseColor.rgb*.7,_BaseColor.rgb,1-sediment);color=lerp(color,reflection,saturate(.18+fresnel*.82));
    float4 shadowCoord=TransformWorldToShadowCoord(v.positionWS);
    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
     shadowCoord=ComputeScreenPos(TransformWorldToHClip(v.positionWS));
    #endif
    Light sun=GetMainLight(shadowCoord);half spec=pow(saturate(dot(n,normalize(sun.direction+view))),180)*sun.shadowAttenuation;color+=sun.color*spec*.2;
    half alpha=saturate(depth*28)*v.edge*lerp(.23,.83,max(sediment,fresnel));return half4(MixFog(color,v.fog),alpha);
   }
   ENDHLSL
  }
 }
}
