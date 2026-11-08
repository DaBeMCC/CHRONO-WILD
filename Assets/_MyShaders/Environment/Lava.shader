Shader "CHRONO WILD/Environment/Lava"
{
 // Crater lava: dark cooled plates drifting over a glowing melt, with cracks that pulse.
 // Outputs HDR values so bloom picks the cracks up.
 Properties
 {
  _HotColor("Hot Color (HDR)",Color)=(9,2.6,0.35,1)
  _CrustColor("Crust Color",Color)=(0.035,0.028,0.026,1)
  _Scale("Plate Size (m)",Float)=4.5
  _Flow("Flow Speed",Float)=0.05
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
  Pass
  {
   Name "Lava" Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vertex
   #pragma fragment Fragment
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _HotColor; half4 _CrustColor; float _Scale; float _Flow;
   CBUFFER_END
   struct Attributes { float4 positionOS:POSITION; };
   struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; };
   Varyings Vertex(Attributes a){ Varyings v; v.positionWS = TransformObjectToWorld(a.positionOS.xyz); v.positionCS = TransformWorldToHClip(v.positionWS); return v; }
   float2 H(float2 p){ float3 q = frac(float3(p.xyx) * float3(0.1031, 0.103, 0.0973)); q += dot(q, q.yzx + 33.33); return frac((q.xx + q.yz) * q.zy); }
   // Distance to the cell border: small along the cracks between plates.
   float Crack(float2 p, float t)
   {
    float2 i = floor(p), f = frac(p), best = 0; float md = 8;
    for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
    {
     float2 g = float2(x, y), o = H(i + g); o = 0.5 + 0.5 * sin(t + 6.2831 * o);
     float2 r = g + o - f; float d = dot(r, r);
     if (d < md) { md = d; best = r; }
    }
    float edge = 8;
    for (int y = -2; y <= 2; y++) for (int x = -2; x <= 2; x++)
    {
     float2 g = float2(x, y), o = H(i + g); o = 0.5 + 0.5 * sin(t + 6.2831 * o);
     float2 r = g + o - f;
     if (dot(best - r, best - r) > 1e-5) edge = min(edge, dot(0.5 * (best + r), normalize(r - best)));
    }
    return edge;
   }
   half4 Fragment(Varyings v) : SV_Target
   {
    float2 p = v.positionWS.xz / _Scale;
    float t = _Time.y * _Flow;
    float a = Crack(p + float2(t, t * 0.6), _Time.y * 0.12), b = Crack(p * 2.3 - float2(t * 1.4, 0), _Time.y * 0.2);
    float pulse = 0.75 + 0.25 * sin(_Time.y * 0.7 + p.x * 1.3 + p.y);
    float glow = pow(saturate(1.0 - a * 3.4), 3.0) + pow(saturate(1.0 - b * 4.5), 4.0) * 0.45;
    half3 color = _CrustColor.rgb * (0.7 + a) + _HotColor.rgb * glow * pulse;
    return half4(color, 1.0);
   }
   ENDHLSL
  }
 }
}
