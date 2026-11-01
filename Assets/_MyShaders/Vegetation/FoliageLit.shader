Shader "CHRONO WILD/Vegetation/Foliage Lit"
{
 Properties
 {
  _BaseMap("Albedo and Alpha Cutoff",2D)="white"{}
  _BaseColor("Color",Color)=(1,1,1,1)
  [Normal] _BumpMap("Normal",2D)="bump"{}
  _BumpScale("Normal Strength",Range(0,2))=.5
  _Cutoff("Alpha Cutoff",Range(0,1))=.4
  _Smoothness("Smoothness",Range(0,1))=.28
  _Transmission("Leaf Transmission",Range(0,1))=.35
  _WindStrength("Breeze (meters)",Range(0,.3))=.05
 }
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
 TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseMap_ST;half4 _BaseColor;half _BumpScale;half _Cutoff;half _Smoothness;half _Transmission;half _WindStrength;
 CBUFFER_END
 struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 tangentOS:TANGENT;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct Varyings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;half4 tangentWS:TEXCOORD2;float2 uv:TEXCOORD3;half fog:TEXCOORD4;float4 shadowCoord:TEXCOORD5;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
 float3 WindPosition(Attributes v)
 {
  float3 p=TransformObjectToWorld(v.positionOS.xyz);
  float height=max(0,p.y-TransformObjectToWorld(float3(0,0,0)).y);
  float phase=dot(p.xz,float2(.27,.19))+_Time.y*.9;
  float weight=saturate(height/4)*saturate(v.uv.y);
  p.xz+=float2(.93,.37)*(sin(phase)+sin(phase*1.73)*.25)*_WindStrength*weight;
  return p;
 }
 Varyings FoliageVertex(Attributes v)
 {
  Varyings o=(Varyings)0;UNITY_SETUP_INSTANCE_ID(v);UNITY_TRANSFER_INSTANCE_ID(v,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
  o.positionWS=WindPosition(v);o.positionCS=TransformWorldToHClip(o.positionWS);
  VertexNormalInputs n=GetVertexNormalInputs(v.normalOS,v.tangentOS);o.normalWS=n.normalWS;o.tangentWS=half4(n.tangentWS,v.tangentOS.w*GetOddNegativeScale());
  o.uv=v.uv*_BaseMap_ST.xy+_BaseMap_ST.zw;o.fog=ComputeFogFactor(o.positionCS.z);
  #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
   o.shadowCoord=ComputeScreenPos(o.positionCS);
  #else
   o.shadowCoord=TransformWorldToShadowCoord(o.positionWS);
  #endif
  return o;
 }
 half4 LeafSample(float2 uv){half4 c=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv)*_BaseColor;clip(c.a-_Cutoff);return c;}
 half3 LeafNormal(Varyings v,half sign)
 {
  half3 tangentNormal=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,v.uv),_BumpScale);
  half3 normal=normalize(v.normalWS);half3 tangent=normalize(v.tangentWS.xyz);half3 bitangent=cross(normal,tangent)*v.tangentWS.w;
  return normalize(mul(tangentNormal,half3x3(tangent,bitangent,normal)))*sign;
 }
 ENDHLSL
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest"}
  Cull Off ZWrite On
  Pass
  {
   Name "ForwardLit" Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex FoliageVertex
   #pragma fragment FoliageFragment
   #pragma multi_compile_instancing
   #pragma multi_compile_fog
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
   #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
   #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
   #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
   #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
   half4 FoliageFragment(Varyings v,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
   {
    UNITY_SETUP_INSTANCE_ID(v);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
    half4 leaf=LeafSample(v.uv);half3 n=LeafNormal(v,IS_FRONT_VFACE(face,1.0h,-1.0h));
    InputData data=(InputData)0;data.positionWS=v.positionWS;data.normalWS=n;data.viewDirectionWS=GetWorldSpaceNormalizeViewDir(v.positionWS);data.shadowCoord=v.shadowCoord;data.fogCoord=v.fog;data.bakedGI=SampleSH(n);data.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(v.positionCS);data.shadowMask=half4(1,1,1,1);
    SurfaceData surface=(SurfaceData)0;surface.albedo=leaf.rgb;surface.alpha=1;surface.metallic=0;surface.specular=half3(.04,.04,.04);surface.smoothness=_Smoothness;surface.normalTS=half3(0,0,1);surface.occlusion=1;
    half4 color=UniversalFragmentPBR(data,surface);
    Light sun=GetMainLight(v.shadowCoord);
    half back=pow(saturate(dot(-sun.direction,data.viewDirectionWS)),3)*.7+saturate(dot(-n,sun.direction))*.3;
    color.rgb+=leaf.rgb*sun.color*back*_Transmission*sun.distanceAttenuation*saturate(sun.shadowAttenuation+.15);
    color.rgb=MixFog(color.rgb,v.fog);return half4(color.rgb,1);
   }
   ENDHLSL
  }
  Pass
  {
   Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"}
   ColorMask 0
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex ShadowVertex
   #pragma fragment ShadowFragment
   #pragma multi_compile_instancing
   #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
   float3 _LightDirection;float3 _LightPosition;
   Varyings ShadowVertex(Attributes v)
   {
    Varyings o=FoliageVertex(v);UNITY_SETUP_INSTANCE_ID(v);
    float3 lightDirection=_LightDirection;
    #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
     lightDirection=normalize(_LightPosition-o.positionWS);
    #endif
    o.positionCS=TransformWorldToHClip(ApplyShadowBias(o.positionWS,o.normalWS,lightDirection));
    #if UNITY_REVERSED_Z
     o.positionCS.z=min(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
    #else
     o.positionCS.z=max(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
    #endif
    return o;
   }
   half4 ShadowFragment(Varyings v):SV_Target{LeafSample(v.uv);return 0;}
   ENDHLSL
  }
  Pass
  {
   Name "DepthOnly" Tags {"LightMode"="DepthOnly"} ColorMask R
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex FoliageVertex
   #pragma fragment DepthFragment
   #pragma multi_compile_instancing
   half4 DepthFragment(Varyings v):SV_Target{LeafSample(v.uv);return 0;}
   ENDHLSL
  }
  Pass
  {
   Name "DepthNormals" Tags {"LightMode"="DepthNormalsOnly"}
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex FoliageVertex
   #pragma fragment NormalsFragment
   #pragma multi_compile_instancing
   #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
   half4 NormalsFragment(Varyings v,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
   {
    LeafSample(v.uv);half3 n=LeafNormal(v,IS_FRONT_VFACE(face,1.0h,-1.0h));
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
