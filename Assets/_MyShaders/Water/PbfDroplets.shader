Shader "Hidden/CHRONO WILD/Water/PBF Droplets"
{
 // Draws the PBF particles as camera-facing sphere impostors: sky reflection through Fresnel, a
 // sun highlight, and aerated white water while a droplet is young and fast.
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+20"}
  Pass
  {
   Name "PbfDroplets" Tags {"LightMode"="UniversalForward"}
   Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma target 4.5
   #pragma vertex Vertex
   #pragma fragment Fragment
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

   struct Particle { float3 x; float life; float3 v; float age; };
   StructuredBuffer<Particle> _Particles;
   float _DropletRadius;

   struct Varyings
   {
    float4 positionCS:SV_POSITION;
    float2 quad:TEXCOORD0;
    float3 positionWS:TEXCOORD1;
    float2 data:TEXCOORD2; // x aeration, y fade
   };

   Varyings Vertex(uint vertexID:SV_VertexID, uint instanceID:SV_InstanceID)
   {
    static const float2 corners[6] = { float2(-1,-1), float2(1,-1), float2(1,1), float2(-1,-1), float2(1,1), float2(-1,1) };
    Varyings o = (Varyings)0;
    Particle p = _Particles[instanceID];
    if (p.life <= 0.0)
    {
     o.positionCS = float4(0, 0, -2, 1); // outside the clip volume
     return o;
    }
    float2 c = corners[vertexID];
    float speed = length(p.v);
    float3 view = normalize(_WorldSpaceCameraPos - p.x);
    float3 right = normalize(cross(float3(0, 1, 0), view));
    float3 up = cross(view, right);
    // Just after the impact each droplet is part of a soft mist puff; it then condenses into a
    // small bright drop.
    float condensed = saturate(p.age / 0.25);
    float radius = _DropletRadius * lerp(3.5, 1.0, condensed);
    float3 world = p.x + (right * c.x + up * c.y) * radius;
    // Drops streak along their motion in the screen plane.
    float2 screenV = float2(dot(p.v, right), dot(p.v, up));
    float screenSpeed = length(screenV);
    if (screenSpeed > 1e-3)
    {
     float2 along = screenV / screenSpeed;
     world += (right * along.x + up * along.y) * dot(c, along) * min(screenSpeed * 0.018, 0.22) * condensed;
    }
    o.positionWS = world;
    o.positionCS = TransformWorldToHClip(world);
    o.quad = c;
    o.data = float2(1.0 - condensed, saturate(p.life * 3.0));
    return o;
   }

   half4 Fragment(Varyings v) : SV_Target
   {
    float r2 = dot(v.quad, v.quad);
    if (r2 > 1.0)
     discard;
    float3 view = normalize(_WorldSpaceCameraPos - v.positionWS);
    float3 right = normalize(cross(float3(0, 1, 0), view));
    float3 up = cross(view, right);
    float3 n = normalize(right * v.quad.x + up * v.quad.y + view * sqrt(1.0 - r2));
    Light sun = GetMainLight();
    half3 sky = SampleSH(reflect(-view, n));
    half fresnel = 0.04 + 0.96 * pow(1.0 - saturate(dot(n, view)), 5.0);
    half3 body = half3(0.32, 0.46, 0.5) * (sky + sun.color * 0.25);
    half3 color = lerp(body, sky * 1.2, fresnel);
    half spec = pow(saturate(dot(n, normalize(sun.direction + view))), 180.0) * 5.0;
    half3 white = sun.color * 0.5 + sky * 0.8;
    color = lerp(color + sun.color * spec, white, v.data.x);
    // Mist is a faint soft puff, a drop is a small crisp sphere.
    half edge = lerp(smoothstep(1.0, 0.72, r2), (1.0 - r2) * (1.0 - r2), v.data.x);
    half alpha = lerp(0.6, 0.012, v.data.x) * v.data.y * edge;
    return half4(color, alpha);
   }
   ENDHLSL
  }
 }
}
