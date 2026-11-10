Shader "CHRONO WILD/Environment/Flipbook Sprite"
{
 // Animated sprite for far-away effects (the meteor): one camera-facing quad that steps through a
 // grid of frames, like an animated GIF. Additive, so the black background disappears.
 Properties
 {
  _MainTex("Frames",2D)="black"{}
  [HDR] _Color("Tint",Color)=(3,2.4,1.8,1)
  _Grid("Columns, Rows",Vector)=(4,4,0,0)
  _FPS("Frames per Second",Float)=12
  _Rotation("Rotation (degrees)",Float)=0
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+45"}
  Pass
  {
   Name "Flipbook" Tags {"LightMode"="UniversalForward"}
   Blend One One ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma target 3.0
   #pragma vertex Vertex
   #pragma fragment Fragment
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
   CBUFFER_START(UnityPerMaterial)
   float4 _MainTex_ST; half4 _Color; float4 _Grid; float _FPS; float _Rotation;
   CBUFFER_END
   struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
   struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
   Varyings Vertex(Attributes a)
   {
    Varyings v;
    // Billboard: keep the object's centre and scale, but build the quad in view space.
    float3 center = TransformObjectToWorld(float3(0, 0, 0));
    float scale = length(TransformObjectToWorldDir(float3(1, 0, 0), false));
    float r = radians(_Rotation);
    float2 corner = float2(a.positionOS.x * cos(r) - a.positionOS.y * sin(r), a.positionOS.x * sin(r) + a.positionOS.y * cos(r));
    float3 view = TransformWorldToView(center) + float3(corner * scale, 0);
    v.positionCS = TransformWViewToHClip(view);
    v.uv = a.uv;
    return v;
   }
   half4 Fragment(Varyings v) : SV_Target
   {
    float frames = _Grid.x * _Grid.y;
    float frame = floor(fmod(_Time.y * _FPS, frames));
    float2 cell = float2(fmod(frame, _Grid.x), _Grid.y - 1 - floor(frame / _Grid.x));
    float2 uv = (cell + v.uv) / _Grid.xy;
    return half4(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb * _Color.rgb, 1);
   }
   ENDHLSL
  }
 }
}
