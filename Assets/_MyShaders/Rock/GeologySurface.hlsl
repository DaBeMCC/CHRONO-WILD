// Shared PBR and bounded POM implementation for rock and ground materials.
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
 TEXTURE2D(_BumpMap);SAMPLER(sampler_BumpMap);
 TEXTURE2D(_HeightAO);SAMPLER(sampler_HeightAO);
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseMap_ST;half4 _BaseColor;half4 _MossColor;
 float _Tiling,_Height,_PomDistance;half _NormalStrength,_UsePOM,_Triplanar,_Wetness,_Moss,_EdgeClip;
 CBUFFER_END
 struct Attributes{float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 tangentOS:TANGENT;float2 uv:TEXCOORD0;half4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct Varyings{float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;half4 tangentWS:TEXCOORD2;float2 uv:TEXCOORD3;half4 color:TEXCOORD4;half fog:TEXCOORD5;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
 Varyings GeoVertex(Attributes v){Varyings o=(Varyings)0;UNITY_SETUP_INSTANCE_ID(v);UNITY_TRANSFER_INSTANCE_ID(v,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.positionWS=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);VertexNormalInputs n=GetVertexNormalInputs(v.normalOS,v.tangentOS);o.normalWS=n.normalWS;o.tangentWS=half4(n.tangentWS,v.tangentOS.w*GetOddNegativeScale());o.uv=v.uv;o.color=v.color;o.fog=ComputeFogFactor(o.positionCS.z);return o;}
 half Hash(float2 p){return frac(sin(dot(floor(p*130),float2(12.9898,78.233)))*43758.5453);}
 void Edge(Varyings v){if(_EdgeClip>.5)clip(v.color.a-.001);}
 float2 Relief(float2 uv,float3 view,float distance)
 {
  #if defined(_POM)
   float2 dx=ddx(uv),dy=ddy(uv);
   float fade=saturate((_PomDistance-distance)/max(_PomDistance*.35,.01));
   fade*=smoothstep(.15,.35,view.z);
   if(fade>.001 && view.z>.12){
    // Explicit gradients keep mip selection stable inside the divergent ray loop.
    float steps=lerp(28,12,saturate(view.z));float layer=1/steps;
    float2 delta=view.xy/max(view.z,.16)*(_Height*fade)/steps;
    float2 current=uv;float depth=0;float h=1-SAMPLE_TEXTURE2D_GRAD(_HeightAO,sampler_HeightAO,current,dx,dy).r;
    [loop]for(int i=0;i<28;i++){if(depth>=h)break;current-=delta;depth+=layer;h=1-SAMPLE_TEXTURE2D_GRAD(_HeightAO,sampler_HeightAO,current,dx,dy).r;}
    float2 previous=current+delta;float after=h-depth;float before=1-SAMPLE_TEXTURE2D_GRAD(_HeightAO,sampler_HeightAO,previous,dx,dy).r-depth+layer;
    float weight=saturate(after/(after-before-1e-5));uv=lerp(current,previous,weight);
   }
  #endif
  return uv;
 }
 void SamplePlane(float2 uv,float3 view,float distance,out half3 albedo,out half3 normal,out half3 mask){uv=Relief(uv,view,distance);albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv).rgb;normal=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,uv),_NormalStrength);half3 micro=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,uv*5.37),.13*saturate((14-distance)/5));normal=normalize(half3(normal.xy+micro.xy,normal.z));mask=SAMPLE_TEXTURE2D(_HeightAO,sampler_HeightAO,uv).rgb;}
 void Surface(Varyings v,out SurfaceData s,out half3 normal)
 {
  half3 n=normalize(v.normalWS);float3 view=GetWorldSpaceNormalizeViewDir(v.positionWS);float distance=length(GetCameraPositionWS()-v.positionWS);half3 albedo,tn,mask;
  #if defined(_TRIPLANAR)
   half3 weights=pow(abs(n),6);weights/=max(dot(weights,1),.001);half3 signs=half3(n.x<0?-1:1,n.y<0?-1:1,n.z<0?-1:1);
   half3 ax,ay,az,nx,ny,nz,mx,my,mz;float3 p=v.positionWS*_Tiling;
   SamplePlane(p.zy*float2(-signs.x,1),float3(-view.z*signs.x,view.y,view.x*signs.x),distance,ax,nx,mx);
   SamplePlane(p.xz*float2(1,-signs.y),float3(view.x,-view.z*signs.y,view.y*signs.y),distance,ay,ny,my);
   SamplePlane(p.xy*float2(signs.z,1),float3(view.x*signs.z,view.y,view.z*signs.z),distance,az,nz,mz);
   albedo=ax*weights.x+ay*weights.y+az*weights.z;mask=mx*weights.x+my*weights.y+mz*weights.z;
   // Surface-gradient normal perturbation preserves smooth geometry across projections.
   float3 perturb=float3(0,nx.y,-nx.x*signs.x)*weights.x+float3(ny.x,0,-ny.y*signs.y)*weights.y+float3(nz.x*signs.z,nz.y,0)*weights.z;
   normal=normalize(n+perturb);
  #else
   half3 tangent=normalize(v.tangentWS.xyz);half3 bitangent=cross(n,tangent)*v.tangentWS.w;half3x3 basis=half3x3(tangent,bitangent,n);
   SamplePlane(v.positionWS.xz*_Tiling,float3(view.x,-view.z,max(view.y,.01)),distance,albedo,tn,mask);
   normal=normalize(n+float3(tn.x,0,-tn.y));
  #endif
  float macro=sin(v.positionWS.x*.63+sin(v.positionWS.z*.31)*2)*sin(v.positionWS.z*.47+v.positionWS.y*.75)*.5+.5;
  half wet=_Wetness*lerp(.35,1,macro);half moss=_Moss*smoothstep(.30,.80,n.y)*smoothstep(.25,.68,macro)*(1-wet*.3);
  albedo*=lerp(.86,1.07,macro);albedo=lerp(albedo,albedo*_MossColor.rgb*2.1,moss);
  albedo*=lerp(1,.67,wet);
  s=(SurfaceData)0;s.albedo=albedo*_BaseColor.rgb;s.alpha=1;s.metallic=0;s.specular=half3(.04,.04,.04);s.smoothness=lerp(1-mask.b,.73,wet)*lerp(1,.48,moss);s.occlusion=lerp(.6,1,mask.g);s.normalTS=half3(0,0,1);
 }
