Shader "CHRONO WILD/Environment/Geology Lit"
{
 Properties
 {
  _BaseMap("Albedo",2D)="white"{}
  [Normal] _BumpMap("Normal",2D)="bump"{}
  _HeightAO("Height (R), AO (G), Roughness (B)",2D)="white"{}
  _BaseColor("Tint",Color)=(1,1,1,1)
  _MossColor("Moss Tint",Color)=(.19,.24,.075,1)
  _Tiling("Tiles per Meter",Float)=.45
  _NormalStrength("Normal Strength",Range(0,2))=1
  _Height("Relief in Texture UV",Range(0,.12))=.025
  _PomDistance("POM Fade Distance",Float)=10
  [Toggle(_POM)] _UsePOM("Enable Near POM",Float)=1
  [Toggle(_TRIPLANAR)] _Triplanar("Triplanar Projection",Float)=1
  _Wetness("Wetness",Range(0,1))=.2
  _Moss("Moss Coverage",Range(0,1))=.25
  _EdgeClip("Ground Edge Feather",Range(0,1))=0
 }
 HLSLINCLUDE
 #include "GeologySurface.hlsl"
 ENDHLSL
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+5"}
  Cull Back ZWrite On
  Pass
  {
   Name "ForwardLit" Tags{"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex GeoVertex
   #pragma fragment GeoFragment
   #pragma shader_feature_local _POM
   #pragma shader_feature_local _TRIPLANAR
   #pragma multi_compile_instancing
   #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
   #pragma multi_compile_fog
   #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
   #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
   #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
   #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
   #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
   half4 GeoFragment(Varyings v):SV_Target{UNITY_SETUP_INSTANCE_ID(v);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);Edge(v);SurfaceData s;half3 n;Surface(v,s,n);InputData d=(InputData)0;d.positionWS=v.positionWS;d.normalWS=n;d.viewDirectionWS=GetWorldSpaceNormalizeViewDir(v.positionWS);
   #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    d.shadowCoord=ComputeScreenPos(TransformWorldToHClip(v.positionWS));
   #else
    d.shadowCoord=TransformWorldToShadowCoord(v.positionWS);
   #endif
   
    #if defined(PROBE_VOLUMES_L1)||defined(PROBE_VOLUMES_L2)
    d.bakedGI=SampleProbeVolumePixel(SampleSH(n),GetAbsolutePositionWS(v.positionWS),n,GetWorldSpaceNormalizeViewDir(v.positionWS),v.positionCS.xy);
    #else
    d.bakedGI=SampleSH(n);
    #endif
    d.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(v.positionCS);d.shadowMask=half4(1,1,1,1);half4 c=UniversalFragmentPBR(d,s);c.rgb=MixFog(c.rgb,v.fog);return half4(c.rgb,_EdgeClip>.5?v.color.a:1);}
   ENDHLSL
  }
  Pass
  {
   Name "ShadowCaster" Tags{"LightMode"="ShadowCaster"} ColorMask 0
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex ShadowVertex
   #pragma fragment DepthFragment
   #pragma multi_compile_instancing
   #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
   #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
   float3 _LightDirection;float3 _LightPosition;
   Varyings ShadowVertex(Attributes v){Varyings o=GeoVertex(v);float3 light=_LightDirection;
   #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    light=normalize(_LightPosition-o.positionWS);
   #endif
   o.positionCS=TransformWorldToHClip(ApplyShadowBias(o.positionWS,o.normalWS,light));
   #if UNITY_REVERSED_Z
    o.positionCS.z=min(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
   #else
    o.positionCS.z=max(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
   #endif
   return o;}
   half4 DepthFragment(Varyings v):SV_Target{Edge(v);return 0;}
   ENDHLSL
  }
  Pass
  {
   Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ColorMask R
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex GeoVertex
   #pragma fragment DepthFragment
   #pragma multi_compile_instancing
   #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
   half4 DepthFragment(Varyings v):SV_Target{Edge(v);return 0;}
   ENDHLSL
  }
  Pass
  {
   Name "DepthNormals" Tags{"LightMode"="DepthNormalsOnly"}
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex GeoVertex
   #pragma fragment NormalFragment
   #pragma shader_feature_local _POM
   #pragma shader_feature_local _TRIPLANAR
   #pragma multi_compile_instancing
   #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
   #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
   half4 NormalFragment(Varyings v):SV_Target{Edge(v);SurfaceData s;half3 n;Surface(v,s,n);
   #if defined(_GBUFFER_NORMALS_OCT)
    float2 oct=PackNormalOctQuadEncode(n);return half4(PackFloat2To888(saturate(oct*.5+.5)),0);
   #else
    return half4(n,0);
   #endif
   }
   ENDHLSL
  }
 }
 FallBack "Universal Render Pipeline/Lit"
}
