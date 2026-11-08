Shader "CHRONO WILD/Environment/Forest Impostor"
{
 Properties{_BaseMap("Eight View Atlas",2D)="white"{} _BaseColor("Tint",Color)=(1,1,1,1) _Cutoff("Alpha Cutoff",Range(0,1))=.35}
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseMap_ST;half4 _BaseColor;half _Cutoff;
 CBUFFER_END
 struct Attributes{float4 positionOS:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct Varyings{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half fog:TEXCOORD1;half3 normalWS:TEXCOORD2;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
 Varyings ImpostorVertex(Attributes v){Varyings o=(Varyings)0;UNITY_SETUP_INSTANCE_ID(v);UNITY_TRANSFER_INSTANCE_ID(v,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);float3 pivot=TransformObjectToWorld(float3(0,0,0));float3 toCamera=GetCameraPositionWS()-pivot;toCamera.y=0;toCamera=normalize(toCamera+float3(0,0,.0001));float3 right=cross(toCamera,float3(0,1,0));float sx=length(TransformObjectToWorldDir(float3(1,0,0),false));float sy=length(TransformObjectToWorldDir(float3(0,1,0),false));float3 position=pivot+right*v.positionOS.x*sx+float3(0,v.positionOS.y*sy,0);o.normalWS=toCamera;o.positionCS=TransformWorldToHClip(position);float3 localView=TransformWorldToObjectDir(toCamera);float angle=atan2(localView.x,localView.z);float frame=fmod(floor(angle*1.2732395447+8.5),8);float2 tile=float2(fmod(frame,4),floor(frame/4));o.uv=(tile+lerp(float2(.006,.004),float2(.994,.996),v.uv))/float2(4,2);o.fog=ComputeFogFactor(o.positionCS.z);return o;}
 half4 Atlas(Varyings v){half4 c=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,v.uv)*_BaseColor;clip(c.a-_Cutoff);return c;}
 ENDHLSL
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest"} Cull Off ZWrite On
  Pass
  {
   Name "Forward" Tags{"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex ImpostorVertex
   #pragma fragment Fragment
   #pragma multi_compile_instancing
   #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
   #pragma multi_compile_fog
   half4 Fragment(Varyings v):SV_Target{UNITY_SETUP_INSTANCE_ID(v);half4 c=Atlas(v);return half4(MixFog(c.rgb,v.fog),1);}
   ENDHLSL
  }
  Pass
  {
   Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ColorMask R
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex ImpostorVertex
   #pragma fragment FragmentDepth
   #pragma multi_compile_instancing
   #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
   half4 FragmentDepth(Varyings v):SV_Target{Atlas(v);return 0;}
   ENDHLSL
  }
  Pass
  {
   Name "DepthNormals" Tags{"LightMode"="DepthNormalsOnly"}
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex ImpostorVertex
   #pragma fragment FragmentNormals
   #pragma multi_compile_instancing
   #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
   #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
   half4 FragmentNormals(Varyings v):SV_Target{Atlas(v);half3 n=normalize(v.normalWS);
   #if defined(_GBUFFER_NORMALS_OCT)
    float2 oct=PackNormalOctQuadEncode(n);return half4(PackFloat2To888(saturate(oct*.5+.5)),0);
   #else
    return half4(n,0);
   #endif
   }
   ENDHLSL
  }
 }
}
